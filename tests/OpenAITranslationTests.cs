using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BrisaXIV.Core;

internal static class OpenAITranslationTests
{
    private const string Key = "sk-test-only-not-a-real-openai-key";
    private static TranslatorOptions Options => new(TranslationProvider.OpenAI, "test-deepl-key:fx", false, "", "", OpenAIApiKey: Key);
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request).WaitAsync(token);
    }
    private sealed class VirtualClock
    {
        public long Now;
        public readonly OpenAIRateLimiter Limiter;
        public VirtualClock() => Limiter = new(() => Now, (wait, token) => { token.ThrowIfCancellationRequested(); Now += (long)Math.Ceiling(wait.TotalMilliseconds); return Task.CompletedTask; });
    }
    private static HttpResponseMessage Output(object translations, string status = "completed")
    {
        using var source = JsonDocument.Parse(JsonSerializer.Serialize(translations));
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject(); writer.WritePropertyName("translations"); writer.WriteStartObject();
            foreach (var entry in source.RootElement.EnumerateArray())
                writer.WriteString("t" + entry.GetProperty("id").GetInt32(), entry.GetProperty("text").GetString());
            writer.WriteEndObject(); writer.WriteEndObject();
        }
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
        {
            status, output = new object[]
            {
                new { type = "reasoning", summary = Array.Empty<object>() },
                new { type = "message", content = new[] { new { type = "output_text", text = System.Text.Encoding.UTF8.GetString(buffer.ToArray()) } } },
            },
        })) };
    }
    private static HttpResponseMessage Error(int status, string code) => new((HttpStatusCode)status)
    { Content = new StringContent(JsonSerializer.Serialize(new { error = new { code, message = Key + " raw-source-日本語" } })) };
    private static async Task<(int Id, string Text)[]> Input(HttpRequestMessage request)
    {
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        using var input = JsonDocument.Parse(body.RootElement.GetProperty("input").GetString()!);
        return input.RootElement.GetProperty("texts").EnumerateArray().Select(x => (x.GetProperty("id").GetInt32(), x.GetProperty("text").GetString()!)).ToArray();
    }
    private static async Task WaitUntil(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!predicate()) { if (DateTime.UtcNow > deadline) throw new Exception("OpenAI test timed out"); await Task.Delay(10); }
    }
    private static async Task<TranslationException?> Failure(Func<Task> action)
    {
        try { await action(); return null; }
        catch (TranslationException ex) { return ex; }
    }

    public static async Task Run(Action<bool, string> check, string scratch)
    {
        check((int)TranslationProvider.DeepL == 0 && (int)TranslationProvider.Ollama == 1 && (int)TranslationProvider.Google == 2
            && Options.OpenAIModel == "gpt-6-luna" && Options.OpenAIRequestsPerMinute == 5000 && Options.OpenAITokensPerMinute == 2_000_000,
            "OpenAI Luna defaults preserve existing provider configuration values and requested RPM/TPM");
        using (var translator = new Translator(Options, new Handler(async request =>
        {
            check(request.RequestUri!.AbsoluteUri == "https://api.openai.com/v1/responses" && request.Headers.Authorization?.Scheme == "Bearer"
                && request.Headers.Authorization.Parameter == Key, "OpenAI uses the official Responses endpoint and its separate Bearer key");
            var raw = await request.Content!.ReadAsStringAsync();
            using var body = JsonDocument.Parse(raw);
            var root = body.RootElement;
            var mappingSchema = root.GetProperty("text").GetProperty("format").GetProperty("schema").GetProperty("properties").GetProperty("translations");
            check(mappingSchema.GetProperty("type").GetString() == "object"
                && mappingSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "t0", "t1" })
                && !mappingSchema.GetProperty("additionalProperties").GetBoolean(),
                "Every source has an individually required schema key, preventing omitted or repeated translation IDs");
            using var input = JsonDocument.Parse(root.GetProperty("input").GetString()!);
            check(root.GetProperty("model").GetString() == "gpt-6-luna" && root.GetProperty("reasoning").GetProperty("effort").GetString() == "none"
                && !root.GetProperty("store").GetBoolean() && !root.GetProperty("stream").GetBoolean()
                && input.RootElement.GetProperty("source_language").GetString() == "JA" && input.RootElement.GetProperty("target_language").GetString() == "pt-BR"
                && root.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean() && !raw.Contains(Key) && !raw.Contains(Options.ApiKey),
                "Luna receives Japanese to PT-BR data, strict JSON, no reasoning, no stored response, and no credentials in the body");
            return Output(new[] { new { id = 1, text = "Obrigado, Iyaate.\nVocê tem {0} itens e %s." }, new { id = 0, text = "Olá." } });
        })))
        {
            var results = await translator.TranslateAsync(["こんにちは。", "ありがとう、Iyaate。\n{0} %s"], CancellationToken.None);
            check(results.SequenceEqual(new[] { "Olá.", "Obrigado, Iyaate.\nVocê tem {0} itens e %s." }),
                "Structured output maps reversed IDs correctly and preserves names, line breaks and placeholders");
        }

        var noKeyCalls = 0;
        using (var translator = new Translator(Options with { OpenAIApiKey = "" }, new Handler(_ => { noKeyCalls++; return Task.FromResult(Output(Array.Empty<object>())); })))
            check(await Failure(async () => await translator.TranslateAsync(["日本語"], CancellationToken.None)) is { StopRequests: true } && noKeyCalls == 0,
                "A missing OpenAI key pauses before any request and never substitutes the DeepL key");

        var cache = new TranslationFile(Path.Combine(scratch, "openai-cache.txt"));
        var cacheCalls = 0;
        var cachedQueue = new TranslationQueue(cache, new Translator(Options, new Handler(async request =>
        {
            cacheCalls++;
            return Output((await Input(request)).Select(x => new { id = x.Id, text = "Tradução " + x.Id }));
        })));
        for (var i = 0; i < 100; i++) cachedQueue.Get("重複");
        cachedQueue.Get("別の行");
        await WaitUntil(() => cache.Count == 2);
        cachedQueue.Dispose(); await cachedQueue.Completion;
        var reopen = new TranslationFile(cache.Path);
        var offlineQueue = new TranslationQueue(reopen, new Translator(Options with { OpenAIApiKey = "" }, new Handler(_ => throw new Exception("Unexpected API request"))));
        check(cacheCalls == 1 && offlineQueue.Get("重複").Text != null && offlineQueue.Get("別の行").Text != null,
            "OpenAI results are deduplicated, saved in the existing TXT format, and served offline after restart");
        offlineQueue.Dispose(); await offlineQueue.Completion;

        var clock = new VirtualClock();
        var requestTimes = new List<long>();
        Handler SharedHandler() => new(_ => { requestTimes.Add(clock.Now); return Task.FromResult(Output(new[] { new { id = 0, text = "Tradução" } })); });
        var limited = Options with { OpenAIRequestsPerMinute = 1 };
        using (var japanese = new Translator(limited, SharedHandler(), clock.Limiter))
            await japanese.TranslateAsync(["日本語"], CancellationToken.None);
        using (var english = new Translator(limited with { SourceLanguage = "EN" }, SharedHandler(), clock.Limiter))
            await english.TranslateAsync(["English"], CancellationToken.None);
        check(requestTimes.Count == 2 && requestTimes[1] - requestTimes[0] >= 60_000,
            "The shared RPM limiter survives translator replacement and covers both source-language queues");

        clock = new VirtualClock();
        await clock.Limiter.WaitAsync(600, 5000, 1000, CancellationToken.None, null);
        await clock.Limiter.WaitAsync(600, 5000, 1000, CancellationToken.None, null);
        check(clock.Now >= 60_000, "TPM reservations wait for the rolling minute before exceeding the token budget");
        check(await Failure(() => clock.Limiter.WaitAsync(1001, 5000, 1000, CancellationToken.None, null)) is { StopRequests: true },
            "A single request larger than the TPM allowance is rejected instead of waiting forever");

        clock = new VirtualClock();
        using (var headers = new HttpResponseMessage(HttpStatusCode.OK))
        {
            headers.Headers.Add("x-ratelimit-limit-requests", "100");
            headers.Headers.Add("x-ratelimit-limit-tokens", "10000");
            headers.Headers.Add("x-ratelimit-limit-project-tokens", "8000");
            headers.Headers.Add("x-ratelimit-remaining-requests", "0");
            headers.Headers.Add("x-ratelimit-reset-requests", "1s");
            headers.Headers.Add("x-ratelimit-remaining-tokens", "50");
            headers.Headers.Add("x-ratelimit-reset-tokens", "1500ms");
            headers.Headers.Add("x-ratelimit-remaining-project-tokens", "50");
            headers.Headers.Add("x-ratelimit-reset-project-tokens", "2s");
            clock.Limiter.Observe(headers);
        }
        await clock.Limiter.WaitAsync(100, 5000, 2_000_000, CancellationToken.None, null);
        check(clock.Now >= 2000 && clock.Limiter.TokenLimit(2_000_000) == 8000
            && OpenAIRateLimiter.ParseDuration("6m0.5s") == TimeSpan.FromMilliseconds(360500),
            "OpenAI headers constrain requests, model/project tokens and reset durations below configured limits");

        clock = new VirtualClock();
        var smallBudgetCalls = new List<int>();
        using (var translator = new Translator(Options with { OpenAITokensPerMinute = 6500 }, new Handler(async request =>
        {
            var input = await Input(request);
            smallBudgetCalls.Add(input.Length);
            return Output(input.Select(x => new { id = x.Id, text = x.Text[0] == 'あ' ? "Primeiro" : "Segundo" }));
        }), clock.Limiter))
        {
            var output = await translator.TranslateAsync([new string('あ', 500), new string('い', 500)], CancellationToken.None);
            check(output.SequenceEqual(new[] { "Primeiro", "Segundo" }) && smallBudgetCalls.SequenceEqual(new[] { 1, 1 }) && clock.Now >= 60_000,
                "Luna splits batches to fit a smaller TPM allowance and reserves prompt plus maximum output before sending");
        }

        var longRetryCalls = 0;
        using (var translator = new Translator(Options, new Handler(_ =>
        {
            longRetryCalls++;
            var response = Error(429, "rate_limit_exceeded");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(5));
            return Task.FromResult(response);
        })))
            check(await Failure(async () => await translator.TranslateAsync(["待つ"], CancellationToken.None)) is { StopRequests: false, RetryAfter: not null } && longRetryCalls == 1,
                "Retry-After longer than the immediate retry budget defers without permanently stopping the queue or retrying too early");

        clock = new VirtualClock();
        var retryTimes = new List<long>();
        using (var retrying = new Translator(Options, new Handler(_ =>
        {
            retryTimes.Add(clock.Now);
            if (retryTimes.Count > 1) return Task.FromResult(Output(new[] { new { id = 0, text = "Retomou" } }));
            var response = Error(429, "rate_limit_exceeded");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
            return Task.FromResult(response);
        }), clock.Limiter))
        {
            check((await retrying.TranslateAsync(["再開"], CancellationToken.None))[0] == "Retomou"
                && retryTimes.Count == 2 && retryTimes[1] - retryTimes[0] >= 2000 && !retrying.UsingGoogleFallback,
                "Temporary HTTP 429 retries automatically after Retry-After and never sends the OpenAI key to Google");
        }

        clock = new VirtualClock();
        var retryCalls = 0;
        using (var retrying = new Translator(Options, new Handler(_ => { retryCalls++; return Task.FromResult(Error(503, "server_is_overloaded")); }), clock.Limiter))
            check(await Failure(async () => await retrying.TranslateAsync(["再試行"], CancellationToken.None)) is { StopRequests: false, RetryAfter: not null }
                && retryCalls == 3 && clock.Now >= 3000, "Transient failures use bounded immediate attempts followed by automatic deferred retry");

        foreach (var (status, code) in new[] { (429, "insufficient_quota"), (401, "invalid_api_key"), (404, "model_not_found") })
        {
            var errorCalls = 0;
            using var translator = new Translator(Options, new Handler(_ => { errorCalls++; return Task.FromResult(Error(status, code)); }));
            var error = await Failure(async () => await translator.TranslateAsync(["秘密"], CancellationToken.None));
            check(error is { StopRequests: true } && errorCalls == 1 && !error.Message.Contains(Key) && !error.Message.Contains("raw-source"),
                "OpenAI " + code + " pauses without retries, fallback, source text or key disclosure");
        }

        var invalidResponses = new Func<HttpResponseMessage>[]
        {
            () => Output(new[] { new { id = 0, text = "A" }, new { id = 0, text = "B" } }),
            () => Output(new[] { new { id = 1, text = "B" } }),
            () => Output(new[] { new { id = 0, text = "A" }, new { id = 4, text = "B" } }),
            () => Output(new[] { new { id = 0, text = "A" }, new { id = 1, text = "" } }),
            () => Output(new[] { new { id = 0, text = "A" }, new { id = 1, text = "B" } }, "incomplete"),
            () => new(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"no\"}]}]}") },
            () => new(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"not-json\"}]}]}") },
        };
        var rejectedAll = true;
        foreach (var invalid in invalidResponses)
        {
            var completions = 0;
            using var translator = new Translator(Options, new Handler(_ => Task.FromResult(invalid())));
            rejectedAll &= await Failure(async () => await translator.TranslateAsync(["一番", "二番"], CancellationToken.None, (_, _) => completions++)) != null && completions == 0;
        }
        check(rejectedAll, "Duplicate/missing/wrong IDs, empty output, truncation, refusal and malformed JSON are rejected before any TXT write");
        using (var translator = new Translator(Options, new Handler(_ => Task.FromResult(Output(new[] { new { id = 0, text = "Marcador perdido" } })))))
            check(await Failure(async () => await translator.TranslateAsync(["アイテム {0} %s"], CancellationToken.None)) != null,
                "OpenAI output with altered or dropped game placeholders is rejected");

        var recovered = new TranslationFile(Path.Combine(scratch, "openai-recovered-format.txt"));
        var recoverySizes = new List<int>();
        var recoveryDiagnostics = new List<string>();
        using (var translator = new Translator(Options, new Handler(async request =>
        {
            var input = await Input(request); recoverySizes.Add(input.Length);
            return recoverySizes.Count == 1 ? Output(new[] { new { id = 0, text = "Resposta incompleta" } })
                : Output(input.Select(x => new { id = x.Id, text = "Tradução " + x.Text }));
        }), openAIDiagnostic: recoveryDiagnostics.Add))
        {
            var sources = new[] { "第一", "第二", "第三", "第四" };
            var result = await translator.TranslateAsync(sources, CancellationToken.None, (i, text) => recovered.Store(sources[i], text));
            check(result.Count == 4 && recovered.Count == 4 && recoverySizes.SequenceEqual(new[] { 4, 2, 2 })
                && recoveryDiagnostics.Single().Contains("mapping-count") && !recoveryDiagnostics.Single().Contains("第一") && !recoveryDiagnostics.Single().Contains(Key),
                "An invalid Luna batch recovers automatically in smaller groups, saves each completed group, and logs only safe metadata");
        }

        var recoverPart = new TranslationFile(Path.Combine(scratch, "openai-recovery-then-fail.txt"));
        var recoveryCalls = 0;
        using (var translator = new Translator(Options, new Handler(async request =>
        {
            var input = await Input(request);
            if (++recoveryCalls == 1 || input.Any(x => x.Text == "失敗")) return Output(Array.Empty<object>());
            return Output(input.Select(x => new { id = x.Id, text = "Concluído" }));
        })))
        {
            var sources = new[] { "成功", "失敗" };
            check(await Failure(async () => await translator.TranslateAsync(sources, CancellationToken.None, (i, text) => recoverPart.Store(sources[i], text))) is { StopRequests: false, IsolatedFailure: true }
                && recoverPart.Count == 1 && recoverPart.TryGet(sources[0], out _) && recoveryCalls == 4,
                "Persistent invalid single-text output ends its bounded recovery without stopping unrelated texts or discarding prior results");
        }

        var partial = new TranslationFile(Path.Combine(scratch, "openai-partial.txt"));
        var partialCalls = 0;
        var longSources = new[] { new string('あ', 4000), new string('い', 4000), new string('う', 4000) };
        using (var translator = new Translator(Options, new Handler(async request =>
        {
            if (++partialCalls > 1) return Error(429, "insufficient_quota");
            return Output((await Input(request)).Select(x => new { id = x.Id, text = "Salvo " + x.Id }));
        })))
            check(await Failure(async () => await translator.TranslateAsync(longSources, CancellationToken.None, (i, result) => partial.Store(longSources[i], result))) != null
                && partialCalls == 2 && new TranslationFile(partial.Path).Count == 2 && !partial.TryGet(longSources[2], out _),
                "Completed OpenAI sub-batches remain in the TXT if a later sub-batch exhausts API balance");

        var priorityFile = new TranslationFile(Path.Combine(scratch, "openai-priority.txt"));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();
        var priorityQueue = new TranslationQueue(priorityFile, new Translator(Options, new Handler(async request =>
        {
            var input = await Input(request);
            order.Add(input[0].Text);
            if (order.Count == 1) { started.TrySetResult(); await release.Task; }
            return Output(input.Select(x => new { id = x.Id, text = x.Text == "会話の優先" ? "Fala prioritária" : "Texto salvo" }));
        })));
        foreach (var source in longSources) priorityQueue.Get(source);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(8));
        priorityQueue.Get("会話の優先", true, true); release.TrySetResult();
        await WaitUntil(() => priorityFile.Count == 4);
        priorityQueue.Dispose(); await priorityQueue.Completion;
        check(order.SequenceEqual(new[] { longSources[0], "会話の優先", longSources[2] }),
            "OpenAI yields between sub-batches for NPC priority, then resumes without translating saved lines again");

        long yieldTime = 0;
        var shouldYield = false;
        var yieldLimiter = new OpenAIRateLimiter(() => yieldTime, (wait, token) => { yieldTime += (long)wait.TotalMilliseconds; shouldYield = true; return Task.CompletedTask; });
        await yieldLimiter.WaitAsync(10, 1, 1000, CancellationToken.None, null);
        var yielded = false;
        try { await yieldLimiter.WaitAsync(10, 1, 1000, CancellationToken.None, () => shouldYield); }
        catch (TranslationYieldException) { yielded = true; }
        check(yielded && yieldTime == 250, "A rate-limit wait yields promptly to NPC priority before sending or reserving another request");

        using var cancel = new CancellationTokenSource();
        var cancellable = new OpenAIRateLimiter(() => 0, (_, token) => Task.Delay(Timeout.Infinite, token));
        await cancellable.WaitAsync(10, 1, 1000, CancellationToken.None, null);
        var pending = cancellable.WaitAsync(10, 1, 1000, cancel.Token, null);
        cancel.Cancel();
        var canceled = false;
        try { await pending; } catch (OperationCanceledException) { canceled = true; }
        check(canceled && !cancellable.IsWaiting, "Unloading the plugin cancels rate-limit waits without blocking the game thread");
    }
}
