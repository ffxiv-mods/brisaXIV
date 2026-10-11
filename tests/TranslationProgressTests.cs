using System.Net;
using System.Text.Json;
using BrisaXIV.Core;

internal static class TranslationProgressTests
{
    private static TranslatorOptions Options => new(TranslationProvider.OpenAI, "", false, "", "", OpenAIApiKey: "sk-test-only");
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
    }
    private static async Task<string[]> Input(HttpRequestMessage request)
    {
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        using var input = JsonDocument.Parse(body.RootElement.GetProperty("input").GetString()!);
        return input.RootElement.GetProperty("texts").EnumerateArray().Select(x => x.GetProperty("text").GetString()!).ToArray();
    }
    private static HttpResponseMessage Output(string[] texts) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text",
                text = JsonSerializer.Serialize(new { translations = texts.Select((_, i) => (Key: "t" + i, Value: "Olá, mundo.")) .ToDictionary(x => x.Key, x => x.Value) }) } } } },
        })),
    };
    private static async Task Until(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!predicate()) { if (DateTime.UtcNow >= deadline) throw new Exception("Progress test timeout"); await Task.Delay(10); }
    }

    public static async Task Run(Action<bool, string> check, string scratch)
    {
        check(TextAmount.Measure("A😀 B\n!!! cão") == new TextAmount(12, 3) && TextAmount.Measure("日本語の文").Words == 1,
            "Progress counts Unicode characters and explicitly space-delimited words without pretending to segment Japanese");
        var file = new TranslationFile(Path.Combine(scratch, "progress-totals.txt"));
        check(file.Store("日😀", "Olá mundo") && !file.Store("日😀", "Repetido") && file.Statistics is { Count: 1, Source.Characters: 2, Portuguese.Words: 2 },
            "TXT totals count unique persisted records and keep source and Portuguese amounts separate");
        File.AppendAllText(file.Path, "日😀\tOlá, mundo inteiro!\n"); file.Reload();
        check(file.Statistics.Portuguese.Words == 3 && new TranslationFile(file.Path).Statistics == file.Statistics,
            "Reload and human corrections recompute permanent totals without double counting duplicate lines");
        var catalog = new PreparationCatalog(); catalog.Add("日😀", 5, "a"); catalog.Add("日本語", 4, "b"); catalog.Finish("test");
        check(catalog.CharacterCount == 5 && catalog.ConfirmedCharacters(1) == 2 && catalog.ConfirmedCharacters(2) == 5,
            "Catalog progress character totals match Unicode accounting and the confirmed cursor");

        var tracker = new TranslationProgress();
        var cacheCalls = 0;
        using (var cached = new TranslationQueue(file, new Translator(Options, new Handler((_, _) => { cacheCalls++; throw new Exception(); })), progress: tracker))
        {
            check(cached.Get("日😀").Text == "Olá, mundo inteiro!" && cacheCalls == 0 && tracker.Snapshot().RequestCount == 0 && tracker.Snapshot().Texts.Length == 0,
                "Reading cached text neither schedules a job nor invents API history for old TXT entries");
        }
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var savedFile = new TranslationFile(Path.Combine(scratch, "progress-stages.txt"));
        var queue = new TranslationQueue(savedFile, new Translator(Options, new Handler(async (request, token) =>
        {
            var source = await Input(request); started.TrySetResult(); await release.Task.WaitAsync(token); return Output(source);
        })), progress: tracker);
        try
        {
            queue.Get("第一"); queue.Get("第二"); await started.Task.WaitAsync(TimeSpan.FromSeconds(8));
            queue.Get("待機");
            var state = tracker.Snapshot();
            check(state.RequestCount == 1 && state.RequestsInFlight == 1 && state.SentTextCount == 2 && state.SentAmount.Characters == 4
                && state.Texts.Count(x => x.Stage == TranslationStage.AwaitingResponse) == 2 && state.Texts.Single(x => x.Source == "待機").Stage == TranslationStage.Queued
                && state.Requests.Single().Texts.SequenceEqual(new[] { "第一", "第二" }) && state.SavedCount == 0,
                "A held API response distinguishes exact sent texts from queued unsent text and does not claim disk completion");
            release.TrySetResult(); await Until(() => tracker.Snapshot().SavedCount == 3);
            state = tracker.Snapshot();
            check(state.RequestsInFlight == 0 && state.Texts.All(x => x.Stage == TranslationStage.Saved) && state.SavedSource.Characters == 6 && state.SavedPortuguese.Words == 6
                && savedFile.Count == 3 && state.RequestCount == 2,
                "Response validation and confirmed TXT writes move live progress to saved with accurate session words and characters");
        }
        finally { release.TrySetResult(); queue.Dispose(); await queue.Completion; }

        var longProgress = new TranslationProgress(); var longFile = new TranslationFile(Path.Combine(scratch, "progress-long.txt"));
        var longQueue = new TranslationQueue(longFile, new Translator(Options, new Handler(async (request, _) => Output(await Input(request)))), progress: longProgress);
        try
        {
            var source = new string('あ', 9001); longQueue.Get(source); await Until(() => longProgress.Snapshot().SavedCount == 1);
            var state = longProgress.Snapshot();
            check(state.SentAmount.Characters == 9001 && state.SentTextCount == 3 && state.SavedCount == 1 && state.SavedSource.Characters == 9001
                && state.Texts.Single().Source == source && state.Texts.Single().Attempts == 2 && state.RequestCount == 2,
                "Long-text progress maps sent fragments back to one saved original and counts actual fragment requests");
        }
        finally { longQueue.Dispose(); await longQueue.Completion; }

        var retryProgress = new TranslationProgress(); var retryFile = new TranslationFile(Path.Combine(scratch, "progress-recovery.txt"));
        var recoveryCalls = 0;
        var retryQueue = new TranslationQueue(retryFile, new Translator(Options, new Handler(async (request, _) =>
        {
            var source = await Input(request);
            if (Interlocked.Increment(ref recoveryCalls) == 1) return Output([]);
            return Output(source);
        })), progress: retryProgress);
        try
        {
            retryQueue.Get("日本"); retryQueue.Get("語文"); await Until(() => retryProgress.Snapshot().SavedCount == 2);
            var state = retryProgress.Snapshot();
            check(state.RequestCount == 3 && state.SentTextCount == 4 && state.SentAmount.Characters == 8 && state.SavedSource.Characters == 4
                && state.Texts.All(x => x.Attempts == 2 && x.Stage == TranslationStage.Saved) && state.RequestsInFlight == 0,
                "Recovery counts repeated API sends while counting each original only once when saved");
        }
        finally { retryQueue.Dispose(); await retryQueue.Completion; }

        // Saturate shared background slots from both language queues, then prove the
        // dedicated HUD lane can send before any background response completes.
        var parallelProgress = new TranslationProgress();
        var parallelFile = new TranslationFile(Path.Combine(scratch, "progress-parallel.txt"));
        var concurrency = new TranslationConcurrency(3); var limiter = new OpenAIRateLimiter();
        var backgroundRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var npcStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var normalActive = 0; var maxNormal = 0;
        async Task<HttpResponseMessage> Respond(HttpRequestMessage request, CancellationToken token)
        {
            var source = await Input(request);
            if (source.Contains("NPC優先")) { npcStarted.TrySetResult(); return Output(source); }
            var active = Interlocked.Increment(ref normalActive);
            int observed; do { observed = maxNormal; if (observed >= active) break; } while (Interlocked.CompareExchange(ref maxNormal, active, observed) != observed);
            try { await backgroundRelease.Task.WaitAsync(token); return Output(source); }
            finally { Interlocked.Decrement(ref normalActive); }
        }
        var japanese = new TranslationQueue(parallelFile, new Translator(Options, new Handler(Respond), limiter), capacity: 160,
            progress: parallelProgress, parallelism: 3, concurrency: concurrency);
        var client = new TranslationQueue(parallelFile, new Translator(Options with { SourceLanguage = "EN" }, new Handler(Respond), limiter), capacity: 160,
            progress: parallelProgress, parallelism: 3, concurrency: concurrency);
        try
        {
            for (var i = 0; i < 60; i++)
                for (var repeat = 0; repeat < 3; repeat++) { japanese.Get("日本" + i); client.Get("Client text " + i); }
            await Until(() => Volatile.Read(ref normalActive) == 3);
            var state = parallelProgress.Snapshot();
            check(state.RequestCount == 3 && state.RequestsInFlight == 3 && state.Texts.Any(x => x.Stage == TranslationStage.WaitingCapacity),
                "Both language queues run multiple API batches but share the configured background concurrency ceiling");
            japanese.Get("NPC優先", priority: true);
            await npcStarted.Task.WaitAsync(TimeSpan.FromSeconds(8)); await Until(() => parallelFile.TryGet("NPC優先", out _));
            check(normalActive == 3 && maxNormal == 3 && parallelFile.Count == 1,
                "Reserved NPC capacity translates and saves an open HUD line while every background API slot is occupied");
            backgroundRelease.TrySetResult(); await Until(() => parallelProgress.Snapshot().SavedCount == 121);
            check(parallelFile.Count == 121 && maxNormal == 3 && parallelProgress.Snapshot().RequestsInFlight == 0 && parallelProgress.Snapshot().SentTextCount == 121,
                "Concurrent batches preserve every unique result, deduplication and the shared concurrency limit");
        }
        finally { backgroundRelease.TrySetResult(); japanese.Dispose(); client.Dispose(); await Task.WhenAll(japanese.Completion, client.Completion); }

        var oneFile = new TranslationFile(Path.Combine(scratch, "progress-one-slot.txt"));
        var oneHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oneStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oneQueue = new TranslationQueue(oneFile, new Translator(Options, new Handler(async (request, token) =>
        {
            var source = await Input(request);
            if (source.Contains("通常")) { oneStarted.TrySetResult(); await oneHold.Task.WaitAsync(token); }
            return Output(source);
        })), parallelism: 1, concurrency: new(1));
        try
        {
            oneQueue.Get("通常"); await oneStarted.Task.WaitAsync(TimeSpan.FromSeconds(8)); oneQueue.Get("優先", priority: true);
            await Until(() => oneFile.TryGet("優先", out _));
            check(oneFile.Count == 1 && !oneFile.TryGet("通常", out _),
                "The reserved HUD worker remains available even when background concurrency is set to one");
        }
        finally { oneQueue.Dispose(); await oneQueue.Completion; }

        var globalSlots = new TranslationConcurrency(2);
        using var prioritySlot = await globalSlots.EnterAsync(true, CancellationToken.None, null);
        var waitingSlot = globalSlots.EnterAsync(false, CancellationToken.None, null);
        await Task.Delay(40);
        check(!waitingSlot.IsCompleted && globalSlots.PriorityActive,
            "HUD activity blocks new background slots globally across language queues even when a background slot is free");
        prioritySlot.Dispose(); using var releasedSlot = await waitingSlot.WaitAsync(TimeSpan.FromSeconds(8));
        check(!globalSlots.PriorityActive, "Background slots resume as soon as the shared HUD priority lease ends");

        var rateProgress = new TranslationProgress(); var rateFile = new TranslationFile(Path.Combine(scratch, "progress-rate.txt"));
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rateLimiter = new OpenAIRateLimiter(); var calls = 0;
        var rateQueue = new TranslationQueue(rateFile, new Translator(Options with { OpenAIRequestsPerMinute = 2 }, new Handler(async (request, token) =>
        {
            Interlocked.Increment(ref calls); var source = await Input(request); await hold.Task.WaitAsync(token); return Output(source);
        }), rateLimiter), capacity: 160, progress: rateProgress, parallelism: 4, concurrency: new(4));
        for (var i = 0; i < 80; i++) rateQueue.Get("制限" + i);
        await Until(() => calls == 2 && rateLimiter.IsWaiting);
        var waiting = rateProgress.Snapshot();
        check(waiting.RequestCount == 2 && waiting.Texts.Any(x => x.Stage == TranslationStage.WaitingLimit)
            && rateLimiter.Snapshot(2, 2_000_000).Requests == 2,
            "Concurrent workers reserve RPM atomically and expose unsent rate-limit waits rather than reporting them as sends");
        rateQueue.Dispose(); await rateQueue.Completion.WaitAsync(TimeSpan.FromSeconds(8));
        check(rateProgress.Snapshot().RequestsInFlight == 0 && rateProgress.Snapshot().Texts.All(x => TranslationProgress.Terminal(x.Stage)) && rateFile.Count == 0,
            "Unload cancels concurrent HTTP and limiter waits, clears active telemetry, and never marks unsaved texts complete");

        var partialProgress = new TranslationProgress(); var partialFile = new TranslationFile(Path.Combine(scratch, "progress-cancel-next.txt"));
        var partialLimiter = new OpenAIRateLimiter();
        var partialQueue = new TranslationQueue(partialFile, new Translator(Options with { OpenAIRequestsPerMinute = 1 },
            new Handler(async (request, _) => Output(await Input(request))), partialLimiter), progress: partialProgress);
        partialQueue.Get(new string('あ', 4000)); partialQueue.Get(new string('い', 4000)); partialQueue.Get(new string('う', 4000));
        await Until(() => partialProgress.Snapshot().SavedCount == 2 && partialLimiter.IsWaiting);
        partialQueue.Dispose(); await partialQueue.Completion;
        check(partialProgress.Snapshot().RequestCount == 1 && partialProgress.Snapshot().Requests.Single().Stage is TranslationStage.Saving or TranslationStage.Saved
            && partialFile.Count == 2 && partialProgress.Snapshot().Texts.Count(x => x.Stage == TranslationStage.Saved) == 2,
            "Canceling an unsent later chunk preserves the prior validated request and its saved texts in the progress history");

        var largeCatalog = new PreparationCatalog(); for (var i = 0; i < 200; i++) largeCatalog.Add("準備" + i, 5, "large"); largeCatalog.Finish("large");
        var fillFile = new TranslationFile(Path.Combine(scratch, "progress-feeder.txt")); var fillHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fillQueue = new TranslationQueue(fillFile, new Translator(Options, new Handler(async (request, token) =>
        { var source = await Input(request); await fillHold.Task.WaitAsync(token); return Output(source); })), capacity: 160, parallelism: 4, concurrency: new(4));
        try
        {
            var preparation = new BackgroundPreparation(largeCatalog, fillFile, Path.Combine(scratch, "progress-feeder.json"));
            preparation.Pump(fillQueue, true, 1_000_000, continuous: true);
            check(fillQueue.PendingCount == 160 && preparation.Cursor == 0 && preparation.SessionCharacters > 0,
                "Concurrent preparation fills a larger work window in one pump without advancing progress before TXT confirmation");
        }
        finally { fillQueue.Dispose(); await fillQueue.Completion; }
    }
}
