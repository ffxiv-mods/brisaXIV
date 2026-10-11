#if BRISA_READ_ONLY
namespace BrisaXIV.Core;

// Used only in the final build: no HTTP client, service endpoints or online translators.
public sealed class Translator : IDisposable
{
    private readonly TranslatorOptions options;
    public Translator(TranslatorOptions options) { this.options = options; }
    public string SourceLanguage => options.SourceLanguage;
    public string ProviderName => "TXT local";
    public TranslationProvider Provider => options.Provider;
    public string ActiveModel => "";
    public bool AIConfigured => false;
    public int RequestsPerMinute => 0;
    public int TokensPerMinute => 0;
    public bool UsingGoogleFallback => false;
    public void RetryPreferredProvider() { }
    public Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts, CancellationToken token, Action<int, string>? completed = null, Func<bool>? shouldYield = null, Action<TranslationActivity>? progress = null)
        => Task.FromException<IReadOnlyList<string>>(new TranslationException("Esta DLL é somente de leitura e não contém tradutores online. O texto precisa existir no TXT.", true));
    public void Dispose() { }
}
#endif
