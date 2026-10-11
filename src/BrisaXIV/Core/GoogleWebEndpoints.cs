#if !BRISA_READ_ONLY
using System.Text.Json;

namespace BrisaXIV.Core;

// Outcome of one Google web call: a translation, a temporary block of this endpoint, or another failure.
public enum GoogleEndpointStatus { Translated, Blocked, Failed }

public readonly record struct GoogleEndpointResult(GoogleEndpointStatus Status, string Text = "", int HttpStatus = 0)
{
    public static GoogleEndpointResult Failed(int httpStatus = 0) => new(GoogleEndpointStatus.Failed, HttpStatus: httpStatus);
    public static GoogleEndpointResult Blocked(int httpStatus) => new(GoogleEndpointStatus.Blocked, HttpStatus: httpStatus);
}

// One unofficial Google web translation endpoint. The Translator tries them in order for each text.
public interface IGoogleEndpoint
{
    string Name { get; }
    Task<GoogleEndpointResult> TranslateAsync(HttpClient client, string text, string sourceLanguage, CancellationToken token);
}

// Endpoint used by the Chrome dictionary extension (the same one Echoglossian uses by default). It tolerates
// much more traffic than translate_a/single. It identifies itself with the extension's public key and referer,
// detects the source language itself, and only accepts GET, so the text goes in the URL.
public sealed class GoogleDictionaryEndpoint : IGoogleEndpoint
{
    private const string Url = "https://dictionaryextension-pa.googleapis.com/v1/dictionaryExtensionData";
    private const string ExtensionKey = "AIzaSyA6EEtrDCfBkHV8uU2lgGY-N383ZgAOo7Y";
    private const string ExtensionReferer = "chrome-extension://mgijmajocgfcbeboacabfgobmjgjcoja";
    // Longer texts would exceed practical URL limits; they go to the POST endpoint instead.
    private const int MaxEscapedLength = 6000;

    public string Name => "Google (dicionário)";

    public async Task<GoogleEndpointResult> TranslateAsync(HttpClient client, string text, string sourceLanguage, CancellationToken token)
    {
        var term = Uri.EscapeDataString(text);
        if (term.Length > MaxEscapedLength) return GoogleEndpointResult.Failed();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Url}?language=pt-BR&key={ExtensionKey}&strategy=2&term={term}");
        request.Headers.TryAddWithoutValidation("x-referer", ExtensionReferer);
        using var response = await client.SendAsync(request, token).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        if (status is 302 or 403 or 429) return GoogleEndpointResult.Blocked(status);
        if (!response.IsSuccessStatusCode) return GoogleEndpointResult.Failed(status);
        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), cancellationToken: token).ConfigureAwait(false);
            if (!json.RootElement.TryGetProperty("translateResponse", out var body)
                || !body.TryGetProperty("translateText", out var translatedElement)
                || translatedElement.ValueKind != JsonValueKind.String)
                return GoogleEndpointResult.Failed(status);
            var translated = TextRules.NormalizeQuotes(translatedElement.GetString() ?? "");
            // An unchanged echo means the endpoint did not translate (for example, wrong language detection).
            return string.IsNullOrWhiteSpace(translated) || translated.Trim() == text.Trim()
                ? GoogleEndpointResult.Failed(status)
                : new(GoogleEndpointStatus.Translated, translated, status);
        }
        catch (JsonException) { return GoogleEndpointResult.Failed(status); }
    }
}

// Public translate_a/single endpoint (client=gtx). POST keeps the text out of the URL. It limits traffic
// aggressively, so it is the fallback when the dictionary endpoint fails.
public sealed class GoogleGtxEndpoint : IGoogleEndpoint
{
    public string Name => "Google (gtx)";

    public async Task<GoogleEndpointResult> TranslateAsync(HttpClient client, string text, string sourceLanguage, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://translate.googleapis.com/translate_a/single");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        { ["client"] = "gtx", ["sl"] = sourceLanguage.ToLowerInvariant(), ["tl"] = "pt-BR", ["dt"] = "t", ["q"] = text });
        using var response = await client.SendAsync(request, token).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        // A redirect to Google's "unusual traffic" page is a temporary block for this connection.
        if (status is 302 or 403 or 429) return GoogleEndpointResult.Blocked(status);
        if (!response.IsSuccessStatusCode) return GoogleEndpointResult.Failed(status);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), cancellationToken: token).ConfigureAwait(false);
        var translated = TextRules.NormalizeQuotes(string.Concat(json.RootElement[0].EnumerateArray()
            .Where(segment => segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0 && segment[0].ValueKind == JsonValueKind.String)
            .Select(segment => segment[0].GetString())));
        return string.IsNullOrWhiteSpace(translated) ? GoogleEndpointResult.Failed(status) : new(GoogleEndpointStatus.Translated, translated, status);
    }
}
#endif
