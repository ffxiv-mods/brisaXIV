namespace BrisaXIV.Core;

// Append providers to preserve their numeric values in existing Dalamud configurations.
public enum TranslationProvider { DeepL, Ollama, Google, OpenAI }
public sealed record TranslatorOptions(
    TranslationProvider Provider, string ApiKey, bool DeepLPro, string OllamaUrl, string OllamaModel,
    string SourceLanguage = "JA", bool GoogleFallback = true,
    string OpenAIApiKey = "", string OpenAIModel = "gpt-6-luna",
    int OpenAIRequestsPerMinute = 5000, int OpenAITokensPerMinute = 2_000_000);

public class TranslationException(string message, bool stopRequests = false, bool isolatedFailure = false, TimeSpan? retryAfter = null, string? billingFailureCode = null) : Exception(message)
{
    public bool StopRequests { get; } = stopRequests;
    public bool IsolatedFailure { get; } = isolatedFailure;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public string? BillingFailureCode { get; } = billingFailureCode;
}
