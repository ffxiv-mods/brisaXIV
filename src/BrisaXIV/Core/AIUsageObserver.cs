#if !BRISA_READ_ONLY
using System.Text.Json;

namespace BrisaXIV.Core;

public sealed partial class Translator
{
    private readonly Action<AIUsageRecord>? aiUsage;
    public TranslationProvider Provider => options.Provider;
    public string ActiveModel => options.Provider == TranslationProvider.OpenAI ? options.OpenAIModel.Trim() : options.OllamaModel.Trim();
    public bool AIConfigured => options.Provider switch
    {
        TranslationProvider.OpenAI => !string.IsNullOrWhiteSpace(options.OpenAIApiKey) && !string.IsNullOrWhiteSpace(options.OpenAIModel),
        TranslationProvider.Ollama => !string.IsNullOrWhiteSpace(options.OllamaModel) && Uri.TryCreate(options.OllamaUrl, UriKind.Absolute, out var uri) && uri.IsLoopback && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo),
        _ => false,
    };
    private void RecordUsage(AIUsageRecord record) { try { aiUsage?.Invoke(record); } catch { /* Telemetry never interrupts translations. */ } }
    private AIUsageRecord StartUsage(string provider, string model)
    {
        var record = new AIUsageRecord(Guid.NewGuid(), DateTimeOffset.UtcNow, provider, model.Trim(), "", AIRequestState.Pending);
        RecordUsage(record); return record;
    }
    private void ResponseUsage(JsonElement root, AIUsageRecord request)
    {
        static long Count(JsonElement element, string name) => element.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var value) ? value : -1;
        long input, output, cached = 0, write = 0;
        if (request.Provider == "OpenAI")
        {
            if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;
            input = Count(usage, "input_tokens"); output = Count(usage, "output_tokens");
            if (usage.TryGetProperty("input_tokens_details", out var details) && details.ValueKind == JsonValueKind.Object)
            {
                if (details.TryGetProperty("cached_tokens", out _)) cached = Count(details, "cached_tokens");
                if (details.TryGetProperty("cache_write_tokens", out _)) write = Count(details, "cache_write_tokens");
            }
        }
        else { input = Count(root, "prompt_eval_count"); output = Count(root, "eval_count"); }
        if (input < 0 || output < 0 || cached < 0 || write < 0 || cached > input || write > input - cached) return;
        var model = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : request.Model;
        var tier = request.Provider == "Ollama" ? "local" : root.TryGetProperty("service_tier", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "unknown";
        RecordUsage(request with { Model = model, Tier = tier, State = AIRequestState.Reported, Input = input, Output = output, Cached = cached, CacheWrite = write });
    }
}
#endif
