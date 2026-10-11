using System.Text;
using System.Text.Json;

namespace BrisaXIV.Core;

public enum AIRequestState { Pending, Reported, Unknown }
public sealed record AIUsageRecord(Guid Id, DateTimeOffset Started, string Provider, string Model,
    string Tier, AIRequestState State, long Input = 0, long Cached = 0, long CacheWrite = 0, long Output = 0);
public sealed record ModelTokenPrice(string Model, decimal InputPerMillion, decimal CachedPerMillion,
    decimal OutputPerMillion, decimal CacheWritePerMillion = 0, bool Configured = true);
public sealed record AIUsageSummary(string Provider, string Model, long Requests, long Pending, long Unknown,
    long Input, long Cached, long CacheWrite, long Output, decimal? EstimatedUsd, long Unpriced);
public sealed record AIDailyUsage(DateOnly Day, AIUsageSummary Total);
public sealed record AIUsageSnapshot(AIUsageSummary Total, AIUsageSummary[] Models, AIDailyUsage[] Days,
    DateTimeOffset? TrackingStarted);

public static class AIPricing
{
    // Standard text rates verified on 2026-10-10. Editable per model; these are
    // list-price estimates, never a statement about credits or an actual invoice.
    public static ModelTokenPrice Luna => new("gpt-6-luna", 0.10m, 0.01m, 0.50m, 0.125m);
    public static decimal? Estimate(AIUsageRecord record, IReadOnlyList<ModelTokenPrice> prices)
    {
        if (record.Provider == "Ollama") return 0;
        if (record.State != AIRequestState.Reported) return null;
        var price = prices.FirstOrDefault(x => x.Model == record.Model)
            ?? (record.Model.StartsWith("gpt-6-luna-", StringComparison.Ordinal) ? prices.FirstOrDefault(x => x.Model == "gpt-6-luna") : null);
        if (price == null || !price.Configured || price.InputPerMillion < 0 || price.CachedPerMillion < 0 || price.OutputPerMillion < 0 || price.CacheWritePerMillion < 0) return null;
        var multiplier = record.Tier switch { "" or "auto" or "default" or "standard" => 1m, "flex" or "batch" => 0.5m, "fast" or "priority" => 2m, _ => 0m };
        if (multiplier == 0) return null;
        var longContext = (record.Model == "gpt-6-luna" || record.Model.StartsWith("gpt-6-luna-", StringComparison.Ordinal)) && record.Input > 272000;
        var inputMultiplier = longContext ? 2m : 1m;
        var outputMultiplier = longContext ? 1.5m : 1m;
        return ((record.Input - record.Cached - record.CacheWrite) * price.InputPerMillion * inputMultiplier
            + record.Cached * price.CachedPerMillion * inputMultiplier
            + record.CacheWrite * price.CacheWritePerMillion * inputMultiplier
            + record.Output * price.OutputPerMillion * outputMultiplier) * multiplier / 1_000_000m;
    }
}

// Local accounting accepts counts only. No source, translation, credential, account
// identifier, response body or request header is stored in this journal.
public sealed class AIUsageLedger
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, AIUsageRecord> records = new();
    private readonly string? path;
    public string? Warning { get; private set; }
    public AIUsageLedger(string? path = null)
    {
        this.path = path;
        if (path == null || !File.Exists(path)) return;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                try
                {
                    if (JsonSerializer.Deserialize<AIUsageRecord>(line) is not { } record || !Valid(record))
                    { Warning = "Há registros de uso ilegíveis. O histórico pode estar incompleto."; continue; }
                    if (!records.TryGetValue(record.Id, out var previous) || previous.State != AIRequestState.Reported) records[record.Id] = record;
                }
                catch (JsonException) { Warning = "Há registros de uso ilegíveis. O histórico pode estar incompleto."; }
            }
            foreach (var record in records.Values.Where(x => x.State == AIRequestState.Pending).ToArray())
                records[record.Id] = record with { State = AIRequestState.Unknown };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { Warning = "Não foi possível ler o histórico de uso. O custo anterior pode estar incompleto."; }
    }
    private static bool Valid(AIUsageRecord r) => r.Id != Guid.Empty && r.Provider is "OpenAI" or "Ollama" && Enum.IsDefined(r.State)
        && !string.IsNullOrWhiteSpace(r.Model) && r.Model.Length <= 200 && !r.Model.StartsWith("sk-", StringComparison.Ordinal)
        && r.Input >= 0 && r.Output >= 0 && r.Cached >= 0 && r.CacheWrite >= 0
        && r.Cached <= r.Input && r.CacheWrite <= r.Input - r.Cached && r.Tier != null && r.Tier.Length <= 50;
    public void Record(AIUsageRecord record)
    {
        if (!Valid(record)) return;
        lock (gate)
        {
            if (records.TryGetValue(record.Id, out var old) && (old == record || old.State == AIRequestState.Reported)) return;
            records[record.Id] = record;
            if (path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, JsonSerializer.Serialize(record) + "\n", new UTF8Encoding(false));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { Warning = "O histórico de uso não pôde ser salvo. Traduções continuam; os custos desta sessão podem não sobreviver à recarga."; }
        }
    }
    private static AIUsageSummary Summarize(IEnumerable<AIUsageRecord> input, IReadOnlyList<ModelTokenPrice> prices, string provider = "", string model = "")
    {
        long count = 0, pending = 0, unknown = 0, tokens = 0, cached = 0, writes = 0, output = 0, unpriced = 0;
        decimal estimate = 0;
        foreach (var r in input)
        {
            count++; if (r.State == AIRequestState.Pending) pending++;
            else if (r.State == AIRequestState.Unknown) unknown++;
            if (r.State == AIRequestState.Reported) { tokens += r.Input; cached += r.Cached; writes += r.CacheWrite; output += r.Output; }
            if (AIPricing.Estimate(r, prices) is { } usd) estimate += usd;
            else if (r.State == AIRequestState.Reported) unpriced++;
        }
        return new(provider, model, count, pending, unknown, tokens, cached, writes, output,
            count > 0 && count == pending + unknown + unpriced ? null : estimate, unpriced);
    }
    public AIUsageSnapshot Snapshot(DateOnly firstDay, int days, IReadOnlyList<ModelTokenPrice> prices)
    {
        lock (gate)
        {
            var lastDay = firstDay.AddDays(days);
            var selected = records.Values.Where(x => DateOnly.FromDateTime(x.Started.UtcDateTime) >= firstDay
                && DateOnly.FromDateTime(x.Started.UtcDateTime) < lastDay).ToArray();
            var daily = selected.GroupBy(x => DateOnly.FromDateTime(x.Started.UtcDateTime)).ToDictionary(x => x.Key, x => Summarize(x, prices));
            return new(Summarize(selected, prices), selected.GroupBy(x => (x.Provider, x.Model)).Select(x => Summarize(x, prices, x.Key.Provider, x.Key.Model)).OrderByDescending(x => x.EstimatedUsd).ToArray(),
                Enumerable.Range(0, days).Select(i => new AIDailyUsage(firstDay.AddDays(i), daily.GetValueOrDefault(firstDay.AddDays(i)) ?? Summarize([], prices))).ToArray(),
                records.Count == 0 ? null : records.Values.Min(x => x.Started));
        }
    }
}
