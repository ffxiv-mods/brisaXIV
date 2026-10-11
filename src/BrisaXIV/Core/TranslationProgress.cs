using System.Text;

namespace BrisaXIV.Core;

public enum TranslationStage { Queued, Preparing, WaitingCapacity, WaitingLimit, AwaitingResponse, Validating, Retrying, Saving, Saved, Error, Interrupted, RetryScheduled }

// Characters are Unicode scalar values, including spaces and line breaks. Word counts
// are whitespace-separated units containing a letter/digit, not Japanese segmentation.
public readonly record struct TextAmount(long Characters, long Words)
{
    public static TextAmount Measure(string text)
    {
        long characters = 0, words = 0;
        var inWord = false;
        foreach (var rune in text.EnumerateRunes())
        {
            characters++;
            if (Rune.IsWhiteSpace(rune)) inWord = false;
            else if (!inWord && Rune.IsLetterOrDigit(rune)) { words++; inWord = true; }
        }
        return new(characters, words);
    }
    public static TextAmount Sum(IEnumerable<string> texts) => texts.Aggregate(new TextAmount(), (sum, text) => sum + Measure(text));
    public static TextAmount operator +(TextAmount a, TextAmount b) => new(a.Characters + b.Characters, a.Words + b.Words);
    public static TextAmount operator -(TextAmount a, TextAmount b) => new(a.Characters - b.Characters, a.Words - b.Words);
}

public sealed record TranslationActivity(TranslationStage Stage, IReadOnlyList<int> Indices, string Provider,
    IReadOnlyList<string>? SentTexts = null);
public sealed record ProgressText(long Id, string Source, string? Translation, TranslationStage Stage, bool Priority,
    string Language, string Provider, string Group, int Expansion, int Attempts, DateTimeOffset Updated, string? Error, TextAmount Amount,
    DateTimeOffset? RetryAt = null, int RetryAfterTranslations = 0);
public sealed record ProgressRequest(long Id, string Provider, DateTimeOffset Started, DateTimeOffset? Finished,
    TranslationStage Stage, string[] Texts, TextAmount Amount, bool Priority);
public sealed record ProgressSnapshot(ProgressText[] Texts, ProgressRequest[] Requests, long RequestCount,
    long SentTextCount, TextAmount SentAmount, long SavedCount, TextAmount SavedSource, TextAmount SavedPortuguese,
    int RequestsLastMinute, int RequestsInFlight, DateTimeOffset SessionStarted);

// Bounded session history; active entries are retained until terminal. No credentials or
// request headers are accepted here, and the UI receives copies rather than worker state.
public sealed class TranslationProgress
{
    private readonly object gate = new();
    private readonly Dictionary<long, ProgressText> texts = new();
    private readonly Dictionary<long, ProgressRequest> requests = new();
    private readonly Queue<long> recentSends = new();
    private long nextId, requestCount, sentTextCount, savedCount;
    private TextAmount sentAmount, savedSource, savedPortuguese;
    private int inFlight;
    private int trimCounter;
    private readonly DateTimeOffset started = DateTimeOffset.Now;
    public static bool Terminal(TranslationStage stage) => stage is TranslationStage.Saved or TranslationStage.Error or TranslationStage.Interrupted;

    public long Enqueue(string source, bool priority, string language, string provider, string group, int expansion)
    {
        lock (gate)
        {
            var id = ++nextId;
            texts[id] = new(id, source, null, TranslationStage.Queued, priority, language, provider, group, expansion, 0, DateTimeOffset.Now, null, TextAmount.Measure(source));
            Trim(); return id;
        }
    }
    public void Promote(long id) { lock (gate) if (texts.TryGetValue(id, out var item)) texts[id] = item with { Priority = true }; }
    public void RetryScheduled(long id, string error, DateTimeOffset due, int remaining)
    {
        lock (gate)
        {
            if (!texts.TryGetValue(id, out var item) || item.Stage == TranslationStage.Saved) return;
            if (item.Stage == TranslationStage.RetryScheduled && item.RetryAt == due && item.RetryAfterTranslations == remaining && item.Error == error) return;
            texts[id] = item with { Stage = TranslationStage.RetryScheduled, Updated = DateTimeOffset.Now, Error = error, RetryAt = due, RetryAfterTranslations = remaining, Priority = false };
        }
    }
    public void Set(long id, TranslationStage stage, string? error = null)
    {
        lock (gate)
        {
            if (!texts.TryGetValue(id, out var item) || item.Stage == TranslationStage.Saved) return;
            texts[id] = item with { Stage = stage, Updated = DateTimeOffset.Now, Error = error };
            Trim();
        }
    }
    public long Report(IReadOnlyList<long> ids, TranslationActivity activity, long requestId)
    {
        lock (gate)
        {
            var affected = activity.Indices.Select(i => ids[i]).Distinct().ToArray();
            if (activity.SentTexts is { } sent)
            {
                requestId = ++nextId;
                var amount = TextAmount.Sum(sent);
                requests[requestId] = new(requestId, activity.Provider, DateTimeOffset.Now, null, activity.Stage, sent.ToArray(), amount,
                    affected.Any(id => texts.TryGetValue(id, out var text) && text.Priority));
                requestCount++; sentTextCount += sent.Count; sentAmount += amount; inFlight++;
                var now = Environment.TickCount64;
                while (recentSends.TryPeek(out var time) && time <= now - 60000) recentSends.Dequeue();
                recentSends.Enqueue(Environment.TickCount64);
            }
            else if (activity.Stage is TranslationStage.Validating or TranslationStage.Retrying or TranslationStage.Saving
                && requestId != 0 && requests.TryGetValue(requestId, out var request))
            {
                requests[requestId] = request with { Stage = activity.Stage, Finished = request.Finished ?? DateTimeOffset.Now };
                if (request.Finished == null) inFlight--;
            }
            foreach (var id in affected)
                if (texts.TryGetValue(id, out var item) && !Terminal(item.Stage))
                    texts[id] = item with { Stage = activity.Stage, Provider = activity.Provider, Attempts = item.Attempts + (activity.SentTexts != null ? 1 : 0), Updated = DateTimeOffset.Now };
            Trim(); return requestId;
        }
    }
    public void FinishRequest(long id, TranslationStage stage)
    {
        lock (gate)
        {
            if (!requests.TryGetValue(id, out var request)) return;
            // Cancellation/error before the next HTTP call must not relabel a prior
            // validated response. Disk failures are reported on the corresponding job.
            if (request.Finished != null && request.Stage is TranslationStage.Saving or TranslationStage.Saved && stage != TranslationStage.Saved) return;
            if (request.Finished == null) inFlight--;
            requests[id] = request with { Stage = stage, Finished = request.Finished ?? DateTimeOffset.Now };
            Trim();
        }
    }
    public void Saved(long id, string translated, bool newlyWritten = true)
    {
        lock (gate)
        {
            if (!texts.TryGetValue(id, out var item) || item.Stage == TranslationStage.Saved) return;
            if (newlyWritten) { savedCount++; savedSource += item.Amount; savedPortuguese += TextAmount.Measure(translated); }
            texts[id] = item with { Stage = TranslationStage.Saved, Translation = translated, Updated = DateTimeOffset.Now, Error = null };
            Trim();
        }
    }
    public ProgressSnapshot Snapshot()
    {
        lock (gate)
        {
            var now = Environment.TickCount64;
            while (recentSends.TryPeek(out var time) && time <= now - 60000) recentSends.Dequeue();
            return new(texts.Values.OrderByDescending(x => x.Id).ToArray(), requests.Values.OrderByDescending(x => x.Id).ToArray(),
                requestCount, sentTextCount, sentAmount, savedCount, savedSource, savedPortuguese, recentSends.Count, inFlight, started);
        }
    }
    private void Trim()
    {
        // Avoid rescanning large active queues on every notification.
        if (texts.Count > 1600 && ++trimCounter % 200 == 0)
            foreach (var id in texts.Values.Where(x => Terminal(x.Stage)).OrderByDescending(x => x.Id).Skip(200).Select(x => x.Id).ToArray()) texts.Remove(id);
        if (requests.Count > 200)
            foreach (var id in requests.Values.Where(x => x.Finished != null).OrderByDescending(x => x.Id).Skip(100).Select(x => x.Id).ToArray()) requests.Remove(id);
    }
}
