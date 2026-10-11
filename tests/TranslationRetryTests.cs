using System.Net;
using System.Text.Json;
using BrisaXIV.Core;

internal static class TranslationRetryTests
{
    private static TranslatorOptions Options => new(TranslationProvider.OpenAI, "", false, "", "", OpenAIApiKey: "sk-test-retry-not-real");
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
    private static HttpResponseMessage Output(string[] source, Func<string, string>? translate = null) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text",
                text = JsonSerializer.Serialize(new { translations = source.Select((text, i) => (Key: "t" + i, Value: translate?.Invoke(text) ?? "Tradução válida")) .ToDictionary(x => x.Key, x => x.Value) }) } } } },
        })),
    };
    private static async Task Until(Func<bool> check, Action? pump = null)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!check()) { if (DateTime.UtcNow >= end) throw new Exception("Retry test timeout"); pump?.Invoke(); await Task.Delay(15); }
    }

    public static async Task Run(Action<bool, string> check, string scratch)
    {
        var now = new DateTimeOffset(2026, 10, 9, 21, 30, 0, TimeSpan.Zero);
        var policy = new TranslationRetryPolicy(() => now, _ => TimeSpan.FromDays(1));
        var retryFile = new TranslationFile(Path.Combine(scratch, "retry-after-count.txt"));
        var tracker = new TranslationProgress();
        var journalPath = Path.Combine(scratch, "retry-after-count.json");
        var allowBad = false; var singleBadCalls = 0;
        const string bad = "テスト000 {0}";
        var corpus = new PreparationCatalog(); corpus.Add(bad, 5, "new");
        for (var i = 1; i < 100; i++) corpus.Add("テスト" + i.ToString("000"), 5, "new"); corpus.Finish("retry-count");
        var queue = new TranslationQueue(retryFile, new Translator(Options, new Handler(async (request, _) =>
        {
            var source = await Input(request);
            if (source.Length == 1 && source[0] == bad) Interlocked.Increment(ref singleBadCalls);
            return Output(source, text => text == bad && allowBad ? "Texto traduzido {0}" : "Texto traduzido");
        })), capacity: 160, progress: tracker, parallelism: 3, concurrency: new(3), retryPath: journalPath, retryPolicy: policy);
        var preparation = new BackgroundPreparation(corpus, retryFile, Path.Combine(scratch, "retry-preparation.json"));
        try
        {
            void Pump() => preparation.Pump(queue, true, 1_000_000, continuous: true);
            // Establish the first failure before starting the successful batches.
            // Otherwise simultaneous completions can precede the failure threshold.
            queue.Get(bad, priority: true);
            await Until(() => queue.DeferredCount == 1);
            Pump();
            await Until(() => retryFile.Count == 99 && singleBadCalls >= 4 && queue.DeferredCount == 1, Pump);
            check(queue.PausedReason == null && !retryFile.TryGet(bad, out _) && !preparation.Completed && retryFile.Count == 99,
                "A repeatedly invalid individual text does not pause the stream: all 99 other originals are saved and completion stays false");
            check(singleBadCalls >= 4 && now == new DateTimeOffset(2026, 10, 9, 21, 30, 0, TimeSpan.Zero)
                && tracker.Snapshot().Texts.Single(x => x.Source == bad).Stage == TranslationStage.RetryScheduled,
                "Automatic retry starts after 50 more saved translations without advancing time or restarting the plugin");
            await Until(() => File.Exists(journalPath) && JsonSerializer.Deserialize<DeferredTranslation[]>(File.ReadAllText(journalPath)) is { Length: 1 } records && records[0].Failures >= 2);
            var journal = JsonSerializer.Deserialize<DeferredTranslation[]>(File.ReadAllText(journalPath))!;
            check(journal.Single().Text == bad && journal.Single().Failures >= 2 && journal.Single().AfterSavedCount > retryFile.Count
                && !File.ReadAllText(journalPath).Contains(Options.OpenAIApiKey),
                "Unresolved original and retry thresholds persist separately from the TXT without credentials or false translated records");
            allowBad = true; now = now.AddDays(2);
            await Until(() => preparation.Completed, Pump);
            check(retryFile.Count == 100 && retryFile.TryGet(bad, out var output) && output == "Texto traduzido {0}"
                && preparation.Verification.Ready && preparation.Verification.MissingCount == 0 && tracker.Snapshot().SavedCount == 100,
                "When only failures remain, the timed fallback heals the final original automatically and the full TXT audit confirms completion");
            await Until(() => JsonSerializer.Deserialize<DeferredTranslation[]>(File.ReadAllText(journalPath))?.Length == 0);
            check(queue.DeferredCount == 0, "A pending record is removed only after its translation is confirmed in the TXT");
        }
        finally { queue.Dispose(); await queue.Completion; }

        // Restore a persisted retry without paying for sources already saved. This also
        // checks that changing providers/reapplying a queue retains pending work.
        var restoreFile = new TranslationFile(Path.Combine(scratch, "retry-restore.txt")); restoreFile.Store("済み", "Pronto");
        var restorePath = Path.Combine(scratch, "retry-restore.json");
        File.WriteAllText(restorePath, JsonSerializer.Serialize(new[]
        {
            new DeferredTranslation("済み", "old", 0, 1, now.AddMinutes(-1), "Falha antiga", true),
            new DeferredTranslation("未完", "new", 5, 2, now.AddMinutes(-1), "Falha antiga", true),
        }));
        var restoreCalls = new List<string>();
        var restored = new TranslationQueue(restoreFile, new Translator(Options, new Handler(async (request, _) =>
        { var source = await Input(request); restoreCalls.AddRange(source); return Output(source); })), retryPath: restorePath, retryPolicy: policy);
        try
        {
            await Until(() => restoreFile.TryGet("未完", out _));
            check(restoreCalls.SequenceEqual(new[] { "未完" }) && restoreFile.Count == 2,
                "Restored retry journals resume the missing original and skip all originals already present in the TXT");
        }
        finally { restored.Dispose(); await restored.Completion; }

        var takeoverPath = Path.Combine(scratch, "retry-takeover.json");
        var oldJournal = new TranslationRetryJournal(takeoverPath);
        var record = new DeferredTranslation("再開", "takeover", 5, 2, now, "Falha", true);
        oldJournal.Put(record);
        var newJournal = new TranslationRetryJournal(takeoverPath);
        check(newJournal.Snapshot().Single().Failures == 2,
            "Replacing a queue flushes the prior pending snapshot before the new journal takes ownership");
        newJournal.Put(record with { Failures = 3 }); newJournal.Flush();
        oldJournal.Remove(record.Text); oldJournal.Flush();
        check(JsonSerializer.Deserialize<DeferredTranslation[]>(File.ReadAllText(takeoverPath))!.Single().Failures == 3,
            "Late cleanup of old canceled workers cannot overwrite new pending records after translator replacement");

        var limitFile = new TranslationFile(Path.Combine(scratch, "retry-minimum.txt"));
        var limitPath = Path.Combine(scratch, "retry-minimum.json");
        File.WriteAllText(limitPath, JsonSerializer.Serialize(new[]
        {
            new DeferredTranslation("再試行", "limit", 5, 1, now.AddSeconds(-1), "Espera mínima", true, 50, now.AddMinutes(2)),
        }));
        var otherStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var limitCalls = 0;
        var limited = new TranslationQueue(limitFile, new Translator(Options, new Handler(async (request, token) =>
        {
            var source = await Input(request);
            if (source.Single() == "他の原文") { otherStarted.TrySetResult(); await otherRelease.Task.WaitAsync(token); }
            else Interlocked.Increment(ref limitCalls);
            return Output(source);
        })), parallelism: 2, retryPath: limitPath, retryPolicy: policy);
        try
        {
            limited.Get("他の原文"); await otherStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(350);
            check(limitCalls == 0 && limited.Get("再試行", priority: true).Pending,
                "An overdue retry still respects the server minimum and remains pending when the HUD checks it");
            now = now.AddMinutes(3); await Task.Delay(350);
            check(limitCalls == 0 && limited.DeferredCount == 1,
                "The fallback timer cannot overtake the saved-text threshold while other runnable work is in flight");
            for (var i = 0; i < 50; i++) limitFile.Store("保存" + i, "Salvo " + i);
            await Until(() => limitFile.TryGet("再試行", out _));
            check(limitCalls == 1 && limited.PausedReason == null,
                "After the successful-text threshold and server minimum are met, retry resumes during other in-flight work");
        }
        finally { otherRelease.TrySetResult(); limited.Dispose(); await limited.Completion; }

        // Old cursor and SavedCount can both look complete after one key is removed and
        // replaced by a different key. Count equality must never substitute for audit.
        var auditFile = new TranslationFile(Path.Combine(scratch, "retry-audit-hole.txt"));
        var auditCorpus = new PreparationCatalog();
        for (var i = 0; i < 3; i++) { auditCorpus.Add("原文" + i, 5, "audit"); auditFile.Store("原文" + i, "Texto " + i); }
        auditCorpus.Finish("audit-hole");
        var auditStatePath = Path.Combine(scratch, "retry-audit-hole.json");
        File.WriteAllText(auditStatePath, JsonSerializer.Serialize(new { Fingerprint = "audit-hole", Cursor = 3, SavedCount = 3, Completed = true }));
        File.WriteAllText(auditFile.Path, "原文1\tTexto 1\n原文2\tTexto 2\n追加\tOutro texto\n"); auditFile.Reload();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var auditCalls = new List<string>();
        var auditQueue = new TranslationQueue(auditFile, new Translator(Options, new Handler(async (request, token) =>
        { var source = await Input(request); auditCalls.AddRange(source); await release.Task.WaitAsync(token); return Output(source); })));
        try
        {
            var repair = new BackgroundPreparation(auditCorpus, auditFile, auditStatePath);
            repair.Pump(auditQueue, true, 1000, continuous: true);
            check(!repair.Completed && repair.Verification.Ready && repair.Verification.MissingCount == 1 && auditFile.Count == 3,
                "Full-catalog audit detects a missing key before an old completed cursor even if TXT record count is unchanged");
            release.TrySetResult(); await Until(() => repair.Completed, () => repair.Pump(auditQueue, true, 1000, continuous: true));
            check(auditCalls.SequenceEqual(new[] { "原文0" }) && auditFile.Count == 4 && repair.Verification.MissingCount == 0,
                "Audit repairs only the missing earlier original and preserves cached entries and unrelated personal translations");
            File.WriteAllText(auditFile.Path, "原文0\tTexto 0\n原文2\tTexto 2\n追加\tOutro texto\n"); auditFile.Reload();
            repair.Verification.Pump();
            check(!repair.Completed && repair.Verification.MissingCount == 1,
                "Reloading an edited TXT invalidates completion and triggers a fresh audit of every catalog original");
        }
        finally { release.TrySetResult(); auditQueue.Dispose(); await auditQueue.Completion; }

        var authCalls = 0; var authFile = new TranslationFile(Path.Combine(scratch, "retry-auth.txt"));
        var auth = new TranslationQueue(authFile, new Translator(Options, new Handler((_, _) =>
        { authCalls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }); })), retryPolicy: policy);
        try
        {
            auth.Get("認証"); await Until(() => auth.PausedReason != null); now = now.AddDays(2); await Task.Delay(350);
            check(authCalls == 1 && authFile.Count == 0, "Invalid credentials stop API attempts rather than becoming an automatic retry billing loop");
        }
        finally { auth.Dispose(); await auth.Completion; }

        var diskFile = new TranslationFile(Path.Combine(scratch, "retry-disk.txt")); var diskCalls = 0;
        var diskQueue = new TranslationQueue(diskFile, new Translator(Options, new Handler(async (request, _) =>
        { Interlocked.Increment(ref diskCalls); return Output(await Input(request)); })), retryPolicy: policy);
        try
        {
            using (var locked = new FileStream(diskFile.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                diskQueue.Get("保存失敗"); await Until(() => diskQueue.PausedReason != null);
                check(diskFile.Count == 0 && !diskQueue.Get("次の原文").Pending && diskCalls == 1,
                    "A failed TXT write stops further spending and does not mark an unsaved original complete");
            }
            diskQueue.RetryErrors(); await Until(() => diskFile.TryGet("保存失敗", out _));
            check(diskFile.Count == 1 && diskCalls == 2 && diskQueue.PausedReason == null,
                "Fixing TXT permissions and manually resuming retries the original without restarting the game");
        }
        finally { diskQueue.Dispose(); await diskQueue.Completion; }
        check(TranslationRetryPolicy.TranslationGap(1) == 50 && TranslationRetryPolicy.TranslationGap(2) == 100
            && TranslationRetryPolicy.TranslationGap(20) == 500,
            "Repeated isolated failures increase the saved-text threshold to 100/200/400 and cap it at 500");
    }
}
