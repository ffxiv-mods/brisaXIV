namespace BrisaXIV.Core;

// One provider's queues for Japanese originals and for client-language text.
public sealed record FallbackQueues(TranslationQueue Japanese, TranslationQueue Client);

// The fallback chain receives what the main path cannot deliver: text blocked by TXT-only mode after
// resolving to a game original, or text whose request failed. Providers are tried in the chain's
// order (Google, then OpenAI and DeepL by configured keys); a failure moves on to the next one.
public sealed class TranslationRouter(TranslationQueue queue, TranslationFile file, SourceTextIndex index, bool japaneseSource, TranslationQueue? clientQueue = null, bool cachedOnly = false, bool prioritizeDialogue = false,
    IReadOnlyList<FallbackQueues>? fallback = null, ISourceTextResolver? live = null)
{
    // Dialogue often embeds item, place or character names through macros, so no static original
    // matches it; such a client-language sentence may fall back. Everything else stays offline unless a
    // request failed: journal and objective texts are prefetched from their complete Japanese
    // originals, and unmatched labels, names, counters and prices would only flood the providers.
    private static bool DialogueSentence(string scope, string text)
        => scope == "Falas" && text.Count(char.IsLetter) >= 12 && text.Count(char.IsWhiteSpace) >= 2;
    private readonly IReadOnlyList<FallbackQueues> chain = fallback ?? [];
    public bool CanRequest(string scope, bool priority = false) => !cachedOnly || chain.Count > 0 || (prioritizeDialogue && (scope == "Falas" || priority));
    public TranslationView Get(string displayed, bool request = true, string scope = "", bool priority = false)
    {
        priority = request && prioritizeDialogue && (scope == "Falas" || priority);
        var main = request && (!cachedOnly || priority);
        if (file.TryGet(displayed, out var saved)) return new(saved, null, false);
        if (!japaneseSource) return Fetch(clientQueue ?? queue, x => x.Client, displayed, request, main, priority, false);
        if (TextRules.ContainsJapanese(displayed)) return Fetch(queue, x => x.Japanese, displayed, request, main, priority, true);
        // Live sources come first: they hold the evaluated text of the hovered tooltip,
        // including numbers and level-dependent clauses that the static index cannot match.
        if (live?.TryResolve(displayed, scope, out var japanese) == true || index.TryResolve(displayed, scope, out japanese))
            return Fetch(queue, x => x.Japanese, japanese, request, main, priority, true);
        if (index.Ready && clientQueue != null) return Fetch(clientQueue, x => x.Client, displayed, request, main, priority, DialogueSentence(scope, displayed));
        return new(null, index.Ready
            ? "Original japonês correspondente não localizado para este texto."
            : "Preparando o índice local de originais japoneses…", !index.Ready);
    }

    private TranslationView Fetch(TranslationQueue primary, Func<FallbackQueues, TranslationQueue> lane, string text, bool request, bool main, bool priority, bool gameContent)
    {
        var result = primary.Get(text, main, priority);
        if (result.Text != null || chain.Count == 0 || !request || result.Pending && result.Error == null) return result;
        // Outside game content, client-language text (counters, player names, chat-like strings)
        // is only sent after a real failure; TXT-only mode never forwards it on its own.
        var primaryFailed = result.Error != null;
        if (!primaryFailed && (main || !gameContent)) return result;
        foreach (var queues in chain)
        {
            var next = lane(queues);
            // The main queue is skipped only when it failed itself; when TXT-only mode merely kept it
            // idle, it still serves as the next provider after a failed one (e.g. Google blocked).
            if (primaryFailed && ReferenceEquals(next, primary)) continue;
            var attempt = next.Get(text, true, priority);
            if (attempt.Text != null || attempt.Pending && attempt.Error == null) return attempt;
            if (attempt.Error != null) result = attempt;
        }
        return result;
    }
}
