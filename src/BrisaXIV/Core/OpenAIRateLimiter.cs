#if !BRISA_READ_ONLY
using System.Globalization;
using System.Text.RegularExpressions;

namespace BrisaXIV.Core;

// Shared by both language queues and kept when the translator is reapplied. Each attempt,
// including a retry, reserves a request and estimated input + maximum output tokens.
public sealed class OpenAIRateLimiter
{
    private readonly object gate = new();
    private readonly Queue<(long Time, int Tokens)> reservations = new();
    private readonly Func<long> clock;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private long reservedTokens;
    private long cooldownUntil;
    private int serverRequests = int.MaxValue;
    private int serverTokens = int.MaxValue;
    private int serverProjectTokens = int.MaxValue;
    private int waiting;
    private readonly Budget requestBudget = new();
    private readonly Budget tokenBudget = new();
    private readonly Budget projectBudget = new();
    public bool IsWaiting => Volatile.Read(ref waiting) > 0;
    public (int Requests, long ReservedTokens, int Rpm, int Tpm, bool Waiting) Snapshot(int rpm, int tpm)
    {
        lock (gate)
        {
            var now = clock();
            while (reservations.TryPeek(out var old) && old.Time <= now - 60_000)
            { reservations.Dequeue(); reservedTokens -= old.Tokens; }
            return (reservations.Count, reservedTokens, Math.Min(rpm, serverRequests),
                Math.Min(tpm, Math.Min(serverTokens, serverProjectTokens)), IsWaiting);
        }
    }

    private sealed class Budget
    {
        public int? Remaining;
        public long Reset;
        public void Expire(long now) { if (now >= Reset) Remaining = null; }
        public long Wait(int needed, long now) => Remaining is { } value && value < needed ? Math.Max(0, Reset - now) : 0;
        public void Reserve(int count) { if (Remaining is { } value) Remaining = Math.Max(0, value - count); }
    }

    public OpenAIRateLimiter() : this(() => Environment.TickCount64, (wait, token) => Task.Delay(wait, token)) { }
    internal OpenAIRateLimiter(Func<long> clock, Func<TimeSpan, CancellationToken, Task> delay)
    { this.clock = clock; this.delay = delay; }

    internal int TokenLimit(int configured)
    { lock (gate) return Math.Min(configured, Math.Min(serverTokens, serverProjectTokens)); }

    internal async Task WaitAsync(int tokens, int rpm, int tpm, CancellationToken token, Func<bool>? shouldYield)
    {
        if (tokens < 1 || rpm < 1 || tpm < 1)
            throw new TranslationException("Os limites RPM e TPM da OpenAI precisam ser positivos.", true);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (shouldYield?.Invoke() == true) throw new TranslationYieldException();
            long wait;
            lock (gate)
            {
                var now = clock();
                while (reservations.TryPeek(out var old) && old.Time <= now - 60_000)
                { reservations.Dequeue(); reservedTokens -= old.Tokens; }
                requestBudget.Expire(now); tokenBudget.Expire(now); projectBudget.Expire(now);
                var allowedRequests = Math.Min(rpm, serverRequests);
                var allowedTokens = Math.Min(tpm, Math.Min(serverTokens, serverProjectTokens));
                if (tokens > allowedTokens)
                    throw new TranslationException("O limite TPM configurado ou informado pela OpenAI é pequeno para este texto. Aumente-o somente se sua conta permitir.", true);
                wait = Math.Max(0, cooldownUntil - now);
                if (reservations.Count >= allowedRequests || reservedTokens + tokens > allowedTokens)
                    wait = Math.Max(wait, Math.Max(1, reservations.Peek().Time + 60_000 - now));
                wait = Math.Max(wait, requestBudget.Wait(1, now));
                wait = Math.Max(wait, tokenBudget.Wait(tokens, now));
                wait = Math.Max(wait, projectBudget.Wait(tokens, now));
                if (wait == 0)
                {
                    reservations.Enqueue((now, tokens)); reservedTokens += tokens;
                    requestBudget.Reserve(1); tokenBudget.Reserve(tokens); projectBudget.Reserve(tokens);
                    return;
                }
            }
            // Brief cancellable waits also let NPC priority interrupt a background retry.
            Interlocked.Increment(ref waiting);
            try { await delay(TimeSpan.FromMilliseconds(Math.Min(wait, 250)), token).ConfigureAwait(false); }
            finally { Interlocked.Decrement(ref waiting); }
        }
    }

    internal void Observe(HttpResponseMessage response)
    {
        lock (gate)
        {
            var now = clock();
            UpdateLimit(response, "x-ratelimit-limit-requests", ref serverRequests);
            UpdateLimit(response, "x-ratelimit-limit-tokens", ref serverTokens);
            UpdateLimit(response, "x-ratelimit-limit-project-tokens", ref serverProjectTokens);
            UpdateBudget(response, "requests", requestBudget, now);
            UpdateBudget(response, "tokens", tokenBudget, now);
            UpdateBudget(response, "project-tokens", projectBudget, now);
        }
    }

    internal void Defer(TimeSpan wait)
    { lock (gate) cooldownUntil = Math.Max(cooldownUntil, clock() + (long)Math.Ceiling(wait.TotalMilliseconds)); }

    internal static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta && delta >= TimeSpan.Zero) return delta;
        if (response.Headers.RetryAfter?.Date is { } date) return date > DateTimeOffset.UtcNow ? date - DateTimeOffset.UtcNow : TimeSpan.Zero;
        return null;
    }

    private static string? Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static void UpdateLimit(HttpResponseMessage response, string header, ref int limit)
    {
        if (int.TryParse(Header(response, header), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
            limit = Math.Min(limit, value);
    }

    private static void UpdateBudget(HttpResponseMessage response, string suffix, Budget budget, long now)
    {
        if (!int.TryParse(Header(response, "x-ratelimit-remaining-" + suffix), NumberStyles.Integer, CultureInfo.InvariantCulture, out var remaining)
            || remaining < 0 || ParseDuration(Header(response, "x-ratelimit-reset-" + suffix)) is not { } reset) return;
        budget.Expire(now);
        // Concurrent responses can arrive out of order. Never increase a live budget.
        budget.Remaining = budget.Remaining is { } previous ? Math.Min(previous, remaining) : remaining;
        budget.Reset = Math.Max(budget.Reset, now + (long)Math.Ceiling(reset.TotalMilliseconds));
    }

    internal static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var matches = Regex.Matches(value, @"(\d+(?:\.\d+)?)(ms|s|m|h)", RegexOptions.CultureInvariant);
        if (matches.Count == 0 || string.Concat(matches.Select(x => x.Value)) != value) return null;
        double ms = 0;
        foreach (Match match in matches)
        {
            if (!double.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)) return null;
            ms += number * (match.Groups[2].Value switch { "ms" => 1, "s" => 1000, "m" => 60000, _ => 3600000 });
        }
        return double.IsFinite(ms) && ms <= TimeSpan.MaxValue.TotalMilliseconds ? TimeSpan.FromMilliseconds(ms) : null;
    }
}
#endif
