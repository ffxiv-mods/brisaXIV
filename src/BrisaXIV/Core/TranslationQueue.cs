using System.Collections.Concurrent;
using System.Text.Json;
using System.Text;

namespace BrisaXIV.Core;

public sealed record TranslationView(string? Text, string? Error, bool Pending);
internal sealed class TranslationYieldException : Exception;

// Concurrent workers share one deduplication map and disk cache.
// Only copied strings cross this queue; game pointers stay on the framework thread.
public sealed class TranslationQueue : IDisposable
{
    private sealed class Entry(string text, bool priority, long progressId, string group, int expansion)
    {
        public string Text { get; } = text;
        public bool Priority = priority;
        public bool InBatch;
        public long ProgressId { get; } = progressId;
        public string Group { get; } = group;
        public int Expansion { get; } = expansion;
        public int Failures;
        public bool Single;
        public DateTimeOffset LastPrioritySeen;
        public DeferredTranslation? Retry;
    }
    private readonly TranslationFile file;
    private readonly Translator translator;
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> pending = new(StringComparer.Ordinal);
    private readonly Queue<Entry> normal = new();
    private readonly Queue<Entry> urgent = new();
    private readonly ConcurrentDictionary<string, string> errors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> deferred = new(StringComparer.Ordinal);
    private readonly TranslationRetryPolicy retryPolicy;
    private readonly TranslationRetryJournal retryJournal;
    private readonly SemaphoreSlim wake = new(0, 1);
    private readonly SemaphoreSlim priorityWake = new(0, 1);
    private readonly TranslationConcurrency? concurrency;
    public TranslationProgress Progress { get; }
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private readonly int capacity;
    private volatile string? pausedReason;
    private volatile string? billingFailureCode;
    private int disposed;
    public int PendingCount { get { lock (gate) return pending.Count; } }
    public int DeferredCount { get { lock (gate) return deferred.Count; } }
    public bool IsDeferred(string text) { lock (gate) return deferred.ContainsKey(TextRules.Normalize(text)); }
    public string? RetryWarning => retryJournal.Warning;
    public int PriorityPendingCount { get { lock (gate) return pending.Values.Count(x => x.Priority && !file.TryGet(x.Text, out _)); } }
    public Task Completion => worker;
    public bool UsingGoogleFallback => translator.UsingGoogleFallback;
    public string? PausedReason => pausedReason ?? concurrency?.BillingReason;
    public string? BillingFailureCode => billingFailureCode ?? concurrency?.BillingCode;
    public void PauseForBilling(string code, string message)
    { concurrency?.PauseBilling(code); billingFailureCode = code; pausedReason = message; }
    public string ProviderName => translator.ProviderName;
    public TranslationProvider Provider => translator.Provider;
    public string ActiveModel => translator.ActiveModel;
    public bool AIConfigured => translator.AIConfigured;
    public int RequestsPerMinute => translator.RequestsPerMinute;
    public int TokensPerMinute => translator.TokensPerMinute;
    public int PreparationWindow { get; }

    public TranslationQueue(TranslationFile file, Translator translator, int capacity = 128,
        TranslationProgress? progress = null, int parallelism = 1, TranslationConcurrency? concurrency = null,
        string? retryPath = null, TranslationRetryPolicy? retryPolicy = null)
    {
        this.file = file; this.translator = translator; this.capacity = capacity;
        Progress = progress ?? new TranslationProgress(); this.concurrency = concurrency;
        this.retryPolicy = retryPolicy ?? new TranslationRetryPolicy(); retryJournal = new(retryPath);
        foreach (var item in retryJournal.Snapshot())
        {
            if (file.TryGet(item.Text, out _)) { retryJournal.Remove(item.Text); continue; }
            var entry = new Entry(item.Text, false, Progress.Enqueue(item.Text, false, translator.SourceLanguage, translator.ProviderName, item.Group, item.Expansion), item.Group, item.Expansion)
            { Failures = item.Failures, Single = item.Single, Retry = item };
            deferred[item.Text] = entry;
            Progress.RetryScheduled(entry.ProgressId, item.Error, item.Due, Math.Max(0, item.AfterSavedCount - file.Count));
        }
        parallelism = Math.Clamp(parallelism, 1, 32);
        PreparationWindow = Math.Min(capacity, parallelism > 1 ? parallelism * 40 : 24);
        var count = parallelism;
        worker = Task.Run(async () =>
        {
            try
            {
                var workers = Enumerable.Range(0, count).Select(_ => WorkAsync()).ToList();
                if (count > 1 || concurrency != null) workers.Add(WorkAsync(priorityOnly: true));
                workers.Add(RetryAsync());
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            finally
            {
                lock (gate)
                {
                    foreach (var entry in pending.Values) Progress.Set(entry.ProgressId, TranslationStage.Interrupted);
                    foreach (var entry in deferred.Values) Progress.Set(entry.ProgressId, TranslationStage.Interrupted);
                    pending.Clear(); deferred.Clear(); normal.Clear(); urgent.Clear();
                }
                retryJournal.Flush();
                translator.Dispose(); wake.Dispose(); priorityWake.Dispose(); stop.Dispose();
            }
        });
    }

    public TranslationView Get(string source, bool request = true, bool priority = false, string group = "", int expansion = -2)
    {
        source = TextRules.Normalize(source);
        if (file.TryGet(source, out var translated)) return new(translated, null, false);
        if (Volatile.Read(ref disposed) != 0) return new(null, "Tradução desativada.", false);
        if (errors.TryGetValue(source, out var error)) return new(null, error, false);
        if (PausedReason is { } reason) return new(null, reason, false);
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0) return new(null, "Tradução desativada.", false);
            if (pending.TryGetValue(source, out var existing))
            {
                if (request && priority && !existing.Priority)
                {
                    existing.Priority = true;
                    Progress.Promote(existing.ProgressId);
                    if (!existing.InBatch) urgent.Enqueue(existing);
                    Signal();
                }
                if (request && priority) existing.LastPrioritySeen = this.retryPolicy.Now;
                return new(null, null, true);
            }
            if (deferred.TryGetValue(source, out var waiting))
            {
                if (request && priority) waiting.LastPrioritySeen = retryPolicy.Now;
                return new(null, waiting.Retry?.Error, true);
            }
            if (!request) return new(null, null, false);
            // Reserved slots let an open NPC dialogue enter a full preparation queue.
            if (pending.Count >= capacity + (priority ? 32 : 0))
                return new(null, "Fila cheia. Será tentado novamente enquanto o texto estiver na tela.", false);
            var entry = new Entry(source, priority, Progress.Enqueue(source, priority, translator.SourceLanguage, translator.ProviderName, group, expansion), group, expansion)
            { LastPrioritySeen = priority ? retryPolicy.Now : default };
            pending.Add(source, entry);
            (priority ? urgent : normal).Enqueue(entry);
            Signal();
            return new(null, null, true);
        }
    }

    public void RetryErrors()
    {
        concurrency?.ClearBillingPause(); errors.Clear(); billingFailureCode = null; pausedReason = null; translator.RetryPreferredProvider();
        lock (gate)
            foreach (var entry in deferred.Values)
                if (entry.Retry is { } retry)
                {
                    entry.Retry = retry with { Due = retryPolicy.Now, AfterSavedCount = 0 };
                    retryJournal.Put(entry.Retry);
                }
        Signal();
    }
    private async Task RetryAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                lock (gate)
                {
                    var now = retryPolicy.Now;
                    var idle = pending.Count == 0;
                    foreach (var entry in deferred.Values.ToArray())
                    {
                        if (file.TryGet(entry.Text, out var translated))
                        { Progress.Saved(entry.ProgressId, translated, false); deferred.Remove(entry.Text); retryJournal.Remove(entry.Text); continue; }
                        if (entry.InBatch || entry.Retry is not { } retry || PausedReason != null) continue;
                        var remaining = Math.Max(0, retry.AfterSavedCount - file.Count);
                        Progress.RetryScheduled(entry.ProgressId, retry.Error, retry.Due, remaining);
                        // Prefer the requested number of successful translations. The
                        // timer is a fallback only when no other runnable work remains.
                        if (now < retry.NotBefore || (remaining > 0 && (!idle || now < retry.Due)) || pending.Count >= capacity) continue;
                        deferred.Remove(entry.Text);
                        entry.Priority = entry.LastPrioritySeen >= now.AddSeconds(-2);
                        if (entry.Priority) Progress.Promote(entry.ProgressId);
                        entry.InBatch = false;
                        pending[entry.Text] = entry;
                        Progress.Set(entry.ProgressId, TranslationStage.Queued);
                        (entry.Priority ? urgent : normal).Enqueue(entry);
                        Signal();
                    }
                }
                retryJournal.Flush();
                await Task.Delay(250, stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    private bool ShouldYieldBackground() => PriorityPendingCount > 0 || concurrency?.PriorityActive == true;
    private void Signal()
    {
        try { wake.Release(); }
        catch (SemaphoreFullException) { }
        catch (ObjectDisposedException) { }
        try { priorityWake.Release(); }
        catch (SemaphoreFullException) { }
        catch (ObjectDisposedException) { }
    }

    // Called while holding gate. Promotion leaves a stale token in normal, which is skipped.
    private Entry? Take(bool priority)
    {
        var lane = priority ? urgent : normal;
        while (lane.TryDequeue(out var entry))
        {
            if (!pending.TryGetValue(entry.Text, out var live) || !ReferenceEquals(live, entry)
                || entry.InBatch || entry.Priority != priority) continue;
            if (file.TryGet(entry.Text, out var cached)) { Progress.Saved(entry.ProgressId, cached, false); pending.Remove(entry.Text); continue; }
            entry.InBatch = true;
            Progress.Set(entry.ProgressId, TranslationStage.Preparing);
            Signal(); // Wake another worker when additions arrived as one burst.
            return entry;
        }
        return null;
    }

    private async Task WorkAsync(bool priorityOnly = false)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                Entry? first;
                lock (gate) first = Take(true) ?? (priorityOnly ? null : Take(false));
                if (first == null) { await (priorityOnly ? priorityWake : wake).WaitAsync(stop.Token).ConfigureAwait(false); continue; }
                var priorityBatch = first.Priority;
                var batch = new List<Entry> { first };
                var yielded = false;
                long requestId = 0;
                var requestResult = TranslationStage.Interrupted;
                try
                {
                    await Task.Delay(priorityBatch ? 10 : 40, stop.Token).ConfigureAwait(false);
                    var bytes = Encoding.UTF8.GetByteCount(first.Text);
                    lock (gate)
                    {
                        while (!first.Single && batch.Count < (priorityBatch ? 4 : 20))
                        {
                            var extra = Take(priorityBatch);
                            if (extra == null) break;
                            if (extra.Single)
                            {
                                extra.InBatch = false; Progress.Set(extra.ProgressId, TranslationStage.Queued);
                                (extra.Priority ? urgent : normal).Enqueue(extra); break;
                            }
                            var size = Encoding.UTF8.GetByteCount(extra.Text);
                            if (bytes + size > 60000)
                            {
                                extra.InBatch = false;
                                (extra.Priority ? urgent : normal).Enqueue(extra);
                                break;
                            }
                            batch.Add(extra); bytes += size;
                        }
                    }
                    if (!priorityBatch && ShouldYieldBackground()) throw new TranslationYieldException();
                    var missing = batch.Where(x => !file.TryGet(x.Text, out _)).ToArray();
                    bool YieldOrPause()
                    {
                        if (PausedReason is { } reason) throw new TranslationException(reason, true, billingFailureCode: BillingFailureCode);
                        return !priorityBatch && ShouldYieldBackground();
                    }
                    YieldOrPause();
                    if (missing.Length > 0)
                    {
                        foreach (var entry in missing) Progress.Set(entry.ProgressId, TranslationStage.WaitingCapacity);
                        using var slot = concurrency == null ? null : await concurrency.EnterAsync(priorityBatch, stop.Token,
                            YieldOrPause).ConfigureAwait(false);
                        YieldOrPause();
                        foreach (var entry in missing) Progress.Set(entry.ProgressId, TranslationStage.Preparing);
                        var ids = missing.Select(x => x.ProgressId).ToArray();
                        var output = await translator.TranslateAsync(missing.Select(x => x.Text).ToArray(), stop.Token, (i, result) =>
                        {
                            Progress.Set(missing[i].ProgressId, TranslationStage.Saving);
                            var added = file.Store(missing[i].Text, result);
                            if (!file.TryGet(missing[i].Text, out _)) throw new TranslationException("Não foi possível gravar o TXT. Tradução pausada para evitar consumir cota sem salvar.", true);
                            file.TryGet(missing[i].Text, out var written);
                            Progress.Saved(missing[i].ProgressId, written, added);
                            retryJournal.Remove(missing[i].Text);
                        }, YieldOrPause,
                        activity => requestId = Progress.Report(ids, activity, requestId)).ConfigureAwait(false);
                        for (var i = 0; i < missing.Length; i++) file.Store(missing[i].Text, output[i]);
                        requestResult = TranslationStage.Saved;
                    }
                }
                catch (TranslationYieldException) { yielded = true; }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                { foreach (var entry in batch) Progress.Set(entry.ProgressId, TranslationStage.Interrupted); break; }
                catch (Exception ex)
                {
                    // Response bodies and exception URLs can contain private text.
                    var error = ex switch
                    {
                        TranslationException e => e.Message,
                        OperationCanceledException => "O serviço demorou demais. Nova tentativa agendada.",
                        HttpRequestException => "Não foi possível conectar ao tradutor. Nova tentativa agendada; confira a conexão se persistir.",
                        JsonException or KeyNotFoundException or InvalidOperationException => "Resposta inválida do tradutor. Nova tentativa agendada.",
                        _ => "Falha na tradução. Nova tentativa agendada; confira as configurações se persistir.",
                    };
                    foreach (var entry in batch.Where(x => !file.TryGet(x.Text, out _)))
                    {
                        entry.Failures++;
                        entry.Single |= ex is TranslationException { IsolatedFailure: true };
                        var minimum = (ex as TranslationException)?.RetryAfter;
                        entry.Retry = new(entry.Text, entry.Group, entry.Expansion, entry.Failures,
                            retryPolicy.Schedule(entry.Failures, minimum), error, entry.Single,
                            file.Count + TranslationRetryPolicy.TranslationGap(entry.Failures), retryPolicy.Now + (minimum ?? TimeSpan.Zero));
                        lock (gate) deferred[entry.Text] = entry;
                        retryJournal.Put(entry.Retry);
                        Progress.RetryScheduled(entry.ProgressId, error, entry.Retry.Due, TranslationRetryPolicy.TranslationGap(entry.Failures));
                        if (ex is TranslationException { StopRequests: true }) errors[entry.Text] = error;
                    }
                    requestResult = TranslationStage.Error;
                    if (ex is TranslationException { StopRequests: true } stopped)
                    { if (stopped.BillingFailureCode != null) { if (Volatile.Read(ref disposed) == 0) concurrency?.PauseBilling(stopped.BillingFailureCode); billingFailureCode = stopped.BillingFailureCode; } pausedReason = error; }
                    retryJournal.Flush();
                }
                finally
                {
                    if (requestId != 0) Progress.FinishRequest(requestId, requestResult);
                    lock (gate)
                    {
                        foreach (var entry in batch)
                        {
                            if (file.TryGet(entry.Text, out var cached)) Progress.Saved(entry.ProgressId, cached, false);
                            if (yielded && !file.TryGet(entry.Text, out _))
                            {
                                entry.InBatch = false;
                                Progress.Set(entry.ProgressId, TranslationStage.Queued);
                                (entry.Priority ? urgent : normal).Enqueue(entry);
                            }
                            else { pending.Remove(entry.Text); entry.InBatch = false; }
                        }
                    }
                    Signal();
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        // No blocking wait on the game thread; the worker owns cleanup after cancellation.
    }
}
