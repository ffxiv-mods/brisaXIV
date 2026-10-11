using System.Net;
using System.Text.Json;
using BrisaXIV.Core;

internal static class AIUsageTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
    private static HttpResponseMessage Json(object value, int code = 200) => new((HttpStatusCode)code) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    private static HttpResponseMessage Output(string translation, object? usage, string tier = "default") => Json(new
    {
        status = "completed", model = "gpt-6-luna", service_tier = tier, usage,
        output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = JsonSerializer.Serialize(new { translations = new { t0 = translation } }) } } } },
    });
    private static readonly TranslatorOptions Options = new(TranslationProvider.OpenAI, "", false, "", "", OpenAIApiKey: "sk-fake-test-only");
    private static readonly object Tokens = new { input_tokens = 1000, input_tokens_details = new { cached_tokens = 400, cache_write_tokens = 100 }, output_tokens = 200 };
    private static async Task<TranslationException?> Failure(Func<Task> action)
    { try { await action(); return null; } catch (TranslationException ex) { return ex; } }
    public static async Task Run(Action<bool, string> check, string scratch)
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var example = new AIUsageRecord(Guid.NewGuid(), DateTimeOffset.UtcNow, "OpenAI", "gpt-6-luna", "default", AIRequestState.Reported, 1000, 400, 100, 200);
        check(AIPricing.Estimate(example, [AIPricing.Luna]) == 0.0001665m, "Cost estimates distinguish uncached, cached, cache-write and output tokens without double counting");
        check(AIPricing.Estimate(example with { Tier = "priority" }, [AIPricing.Luna]) == 0.000333m
            && AIPricing.Estimate(example with { Tier = "flex" }, [AIPricing.Luna]) == 0.00008325m, "Reported service tiers adjust estimates using their published multipliers");
        check(AIPricing.Estimate(example with { Input = 273000, Cached = 0, CacheWrite = 0, Output = 1000 }, [AIPricing.Luna]) == 0.05535m, "Luna long-context input and output premiums are estimated separately");
        check(AIPricing.Estimate(example with { Model = "other-model" }, [AIPricing.Luna]) == null
            && AIPricing.Estimate(example with { Tier = "unknown" }, [AIPricing.Luna]) == null
            && AIPricing.Estimate(example, [AIPricing.Luna with { Configured = false }]) == null, "Unknown models, unknown tiers and unconfigured prices never look free");
        var path = Path.Combine(scratch, "usage.jsonl"); var ledger = new AIUsageLedger(path);
        ledger.Record(example with { State = AIRequestState.Pending, Input = 0, Cached = 0, CacheWrite = 0, Output = 0 });
        ledger.Record(example); ledger.Record(example); ledger.Record(example with { State = AIRequestState.Unknown });
        var snapshot = ledger.Snapshot(day, 1, [AIPricing.Luna]);
        check(snapshot.Total.Requests == 1 && snapshot.Total.Input == 1000 && snapshot.Total.Cached == 400 && snapshot.Total.EstimatedUsd == 0.0001665m, "A request is counted once when pending, reported and finally completed");
        var pending = example with { Id = Guid.NewGuid(), State = AIRequestState.Pending, Input = 0, Cached = 0, CacheWrite = 0, Output = 0 };
        ledger.Record(pending);
        File.AppendAllText(path, "{\"Id\":\"" + Guid.NewGuid() + "\",\"Provider\":\"OpenAI\",\"Model\":\"gpt-6-luna\",\"Tier\":null,\"State\":99}\n{broken\n");
        var reopened = new AIUsageLedger(path).Snapshot(day, 1, [AIPricing.Luna]);
        check(reopened.Total.Requests == 2 && reopened.Total.Unknown == 1 && reopened.Total.Input == 1000, "Reload preserves reported usage, marks unfinished requests unknown and ignores corrupt records");
        var utc = new AIUsageLedger();
        utc.Record(example with { Started = new DateTimeOffset(2026, 9, 30, 22, 30, 0, TimeSpan.FromHours(-3)) });
        check(utc.Snapshot(new DateOnly(2026, 10, 1), 1, [AIPricing.Luna]).Total.Requests == 1, "Daily accounting uses UTC even across a local midnight boundary");
        var concurrent = new AIUsageLedger();
        Parallel.For(0, 500, _ => { var r = example with { Id = Guid.NewGuid() }; concurrent.Record(r); concurrent.Record(r); });
        check(concurrent.Snapshot(day, 1, [AIPricing.Luna]).Total.Requests == 500, "Concurrent translation queues share deduplicated accounting");
        var usage = new AIUsageLedger();
        using (var translator = new Translator(Options, new Handler(_ => Output("Olá.", Tokens)), aiUsage: usage.Record))
            await translator.TranslateAsync(["こんにちは。"], CancellationToken.None);
        check(usage.Snapshot(day, 1, [AIPricing.Luna]).Total.Input == 1000 && usage.Snapshot(day, 1, [AIPricing.Luna]).Total.Pending == 0,
            "The translator records real response counts rather than limiter reservations");
        var invalidUsage = new AIUsageLedger(); var attempts = 0;
        using (var translator = new Translator(Options, new Handler(_ => { attempts++; return Output("", Tokens); }), aiUsage: invalidUsage.Record))
            await Failure(() => translator.TranslateAsync(["こんにちは。"], CancellationToken.None));
        var invalid = invalidUsage.Snapshot(day, 1, [AIPricing.Luna]);
        check(attempts == 2 && invalid.Total.Requests == 2 && invalid.Total.Input == 2000 && invalid.Total.EstimatedUsd == 0.000333m,
            "Rejected translations and their recovery attempts still count consumed tokens");
        var missing = new AIUsageLedger();
        using (var translator = new Translator(Options, new Handler(_ => Output("Olá.", null)), aiUsage: missing.Record))
            await translator.TranslateAsync(["こんにちは。"], CancellationToken.None);
        check(missing.Snapshot(day, 1, [AIPricing.Luna]).Total is { Unknown: 1, EstimatedUsd: null }, "A response without usage remains unknown instead of showing zero cost");
        using (var translator = new Translator(Options, new Handler(_ => Output("Olá.", Tokens)), aiUsage: _ => throw new IOException("test")))
            check((await translator.TranslateAsync(["こんにちは。"], CancellationToken.None))[0] == "Olá.", "Accounting failures do not interrupt a valid translation");
        var local = new AIUsageLedger();
        using (var translator = new Translator(new(TranslationProvider.Ollama, "", false, "http://127.0.0.1:11434", "local-test"),
            new Handler(_ => Json(new { response = "Olá.", done_reason = "stop", prompt_eval_count = 60, eval_count = 12 })), aiUsage: local.Record))
            await translator.TranslateAsync(["こんにちは。"], CancellationToken.None);
        check(local.Snapshot(day, 1, []).Total is { Input: 60, Output: 12, EstimatedUsd: 0 }, "Local AI reports token counts with no external API cost");
        check(!File.ReadAllText(path).Contains("sk-fake") && !File.ReadAllText(path).Contains("こんにちは"), "The accounting journal contains counts without credentials or source text");
        foreach (var code in new[] { "credit_balance_exhausted", "insufficient_quota", "organization_spend_limit_exceeded", "project_spend_limit_exceeded", "organization_usage_limit_exceeded", "billing_hard_limit_reached", "billing_not_active" })
        {
            var sends = 0;
            using var translator = new Translator(Options, new Handler(_ => { sends++; return Json(new { error = new { code, type = "insufficient_quota", message = "sk-fake-test-only" } }, 429); }));
            var error = await Failure(() => translator.TranslateAsync(["こんにちは。"], CancellationToken.None));
            check(error is { StopRequests: true } && error.BillingFailureCode == code && sends == 1 && !error.Message.Contains("sk-fake"), "Billing alert classifies " + code + " without retrying or leaking service messages");
        }
        check(BillingFailure.Message("rate_limit_exceeded") == null && BillingFailure.Message("slow_down") == null, "Temporary request limits never trigger the no-credit alert");
        using (var translator = new Translator(Options, new Handler(_ => Json(new { error = new { code = "future_billing_code", type = "insufficient_quota" } }, 429))))
        {
            var error = await Failure(() => translator.TranslateAsync(["こんにちは。"], CancellationToken.None));
            check(error?.BillingFailureCode == "insufficient_quota", "A broader billing type still produces a safe alert when a future specific code is unknown");
        }
        using (var translator = new Translator(Options, new Handler(_ => Json(new[] { "malformed-error" }, 401))))
        {
            var error = await Failure(() => translator.TranslateAsync(["こんにちは。"], CancellationToken.None));
            check(error is { StopRequests: true, BillingFailureCode: null }, "Malformed authentication error bodies still pause access without inventing a billing failure");
        }
        var balanceFile = new TranslationFile(Path.Combine(scratch, "balance.txt"));
        using (var translator = new Translator(Options, new Handler(_ => Json(new { error = new { code = "credit_balance_exhausted" } }, 429))))
        using (var queue = new TranslationQueue(balanceFile, translator))
        {
            queue.Get("こんにちは。"); var end = DateTime.UtcNow.AddSeconds(5);
            while (queue.BillingFailureCode == null && DateTime.UtcNow < end) await Task.Delay(10);
            check(queue.BillingFailureCode == "credit_balance_exhausted" && queue.PausedReason != null && queue.DeferredCount == 1,
                "A billing failure exposes alert metadata while preserving deferred translation work");
            using var otherTranslator = new Translator(Options, new Handler(_ => Output("Olá.", Tokens)));
            using var otherQueue = new TranslationQueue(balanceFile, otherTranslator);
            otherQueue.PauseForBilling(queue.BillingFailureCode!, queue.PausedReason!);
            check(otherQueue.PausedReason != null && otherQueue.BillingFailureCode == queue.BillingFailureCode, "A billing pause can be shared with the second language queue");
        }
        await WaitingBillingPause(check, scratch);
        await Costs(check);
    }
    private static async Task WaitingBillingPause(Action<bool, string> check, string scratch)
    {
        var shared = new TranslationConcurrency(2);
        var limiter = new OpenAIRateLimiter(() => 1000L, (_, token) => Task.Delay(10, token));
        await limiter.WaitAsync(1, 1, 2_000_000, CancellationToken.None, null);
        var sends = 0; var progress = new TranslationProgress(); var usage = new AIUsageLedger();
        var file = new TranslationFile(Path.Combine(scratch, "waiting-billing.txt"));
        using var waitingTranslator = new Translator(Options with { OpenAIRequestsPerMinute = 1 }, new Handler(_ => { sends++; return Output("Olá.", Tokens); }), openAIRateLimiter: limiter, aiUsage: usage.Record);
        using var failedTranslator = new Translator(Options, new Handler(_ => Json(new { error = new { code = "credit_balance_exhausted" } }, 429)));
        using var waitingQueue = new TranslationQueue(file, waitingTranslator, progress: progress, concurrency: shared);
        using var failedQueue = new TranslationQueue(file, failedTranslator, concurrency: shared);
        async Task Wait(Func<bool> condition)
        { var end = DateTime.UtcNow.AddSeconds(5); while (!condition()) { if (DateTime.UtcNow > end) throw new Exception("Billing wait timeout"); await Task.Delay(10); } }
        waitingQueue.Get("こんばんは。");
        await Wait(() => progress.Snapshot().Texts.Any(x => x.Stage == TranslationStage.WaitingLimit));
        failedQueue.Get("こんにちは。");
        await Wait(() => failedQueue.BillingFailureCode != null && waitingQueue.PendingCount == 0);
        check(waitingQueue.PausedReason != null && sends == 0 && waitingQueue.DeferredCount == 1,
            "A credit failure immediately stops the other queue's unsent rate-limit wait without losing its original");
        check(usage.Snapshot(DateOnly.FromDateTime(DateTime.UtcNow), 1, [AIPricing.Luna]).Total.Requests == 0,
            "Unsent work canceled by a shared billing pause does not invent billable API calls");
    }
    private static async Task Costs(Action<bool, string> check)
    {
        var first = new DateOnly(2026, 9, 1);
        var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        object Page(decimal? usd, bool more = false, string? next = null) => new
        {
            data = new[] { new { start_time = start, end_time = start + 86400, results = usd.HasValue
                ? new object[] { new { amount = new { value = usd.Value, currency = "usd" }, line_item = "gpt-6-luna" } } : [] } }, has_more = more, next_page = next,
        };
        var requests = 0; var correct = true;
        using (var client = new OpenAICostsClient(new Handler(request =>
        {
            requests++; correct &= request.Method == HttpMethod.Get && request.RequestUri!.Host == "api.openai.com"
                && request.Headers.Authorization?.Parameter == "admin-test-only" && request.Content == null
                && request.RequestUri.Query.Contains("project_ids%5B%5D=proj_test") && request.RequestUri.Query.Contains("api_key_ids%5B%5D=key_test");
            return Json(requests == 1 ? Page(0.75m, true, "next") : Page(0.25m));
        })))
        {
            var report = await client.FetchAsync(first, 30, "admin-test-only", "proj_test", "key_test", CancellationToken.None);
            check(correct && requests == 2 && report.Valid && report.TotalUsd == 1 && report.Items[0].Usd == 1,
                "Costs queries use a separate admin key, project/key filters and all result pages");
            check(report.Daily[0].HasAmount && !report.Daily[1].HasAmount && report.Daily.Length == 30,
                "The report distinguishes reported costs from missing daily amounts");
        }
        using (var client = new OpenAICostsClient(new Handler(_ => Json(Page(0)))))
        { var report = await client.FetchAsync(first, 1, "admin-test-only", "", "", CancellationToken.None); check(report.HasAmounts && report.TotalUsd == 0, "An explicitly reported zero remains different from an absent amount"); }
        using (var client = new OpenAICostsClient(new Handler(_ => Json(Page(null)))))
        { var report = await client.FetchAsync(first, 1, "admin-test-only", "", "", CancellationToken.None); check(!report.HasAmounts, "An empty Costs response does not prove free coverage"); }
        using (var client = new OpenAICostsClient(new Handler(_ => Json(new { error = new { message = "admin-test-only" } }, 403))))
        {
            BillingException? error = null; try { await client.FetchAsync(first, 1, "admin-test-only", "", "", CancellationToken.None); } catch (BillingException ex) { error = ex; }
            check(error != null && !error.Message.Contains("admin-test-only"), "A denied billing query stays unverified and hides the response body");
        }
        var pages = 0;
        using (var client = new OpenAICostsClient(new Handler(_ => ++pages == 1 ? Json(Page(1, true, "next")) : Json(new { error = "test" }, 500))))
        {
            BillingException? error = null; try { await client.FetchAsync(first, 1, "admin-test-only", "", "", CancellationToken.None); } catch (BillingException ex) { error = ex; }
            check(error != null && pages == 2, "A failed later page cannot publish a misleading partial account total");
        }
        using (var client = new OpenAICostsClient(new Handler(_ => { var response = Json(new { error = "test" }, 429); response.Headers.RetryAfter = new(TimeSpan.FromSeconds(120)); return response; })))
        {
            BillingException? error = null; try { await client.FetchAsync(first, 1, "admin-test-only", "", "", CancellationToken.None); } catch (BillingException ex) { error = ex; }
            check(error?.RetryAfter == TimeSpan.FromSeconds(120), "Administrative queries honor Retry-After independently of translation limits");
        }
        using (var client = new OpenAICostsClient(new Handler(_ => throw new Exception("must not send"))))
        {
            BillingException? error = null; try { await client.FetchAsync(first, 1, "admin-test-only", "", "sk-fake-secret", CancellationToken.None); } catch (BillingException ex) { error = ex; }
            check(error != null, "A secret entered into an ID filter is rejected before network access");
        }
        check(!(new OpenAICostReport(first, 1, DateTimeOffset.UtcNow, "", "", [], [])).Valid, "Malformed cached reports cannot be displayed as verified account costs");
        var cache = new OpenAICostCache("profile-A", new(first, 1, DateTimeOffset.UtcNow, "", "", [new(first, 1, true)], []));
        check(cache.Matches("profile-A") && !cache.Matches("profile-B") && !cache.Matches(""),
            "A saved cost report cannot be reused after changing the billing credential profile");
    }
}
