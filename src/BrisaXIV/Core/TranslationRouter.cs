namespace BrisaXIV.Core;

// One provider's queues for Japanese originals and for client-language text.
public sealed record FallbackQueues(TranslationQueue Japanese, TranslationQueue Client);

// The fallback chain receives what the main path cannot deliver: text blocked by TXT-only mode after
// resolving to a game original, or text whose request failed. Providers are tried in the chain's
// order (Google, then OpenAI and DeepL by configured keys); a failure moves on to the next one.
public sealed class TranslationRouter(TranslationQueue queue, TranslationFile file, SourceTextIndex index, bool japaneseSource, TranslationQueue? clientQueue = null, bool cachedOnly = false, bool prioritizeDialogue = false,
    IReadOnlyList<FallbackQueues>? fallback = null, ISourceTextResolver? live = null)
{
    // Game content (quest journal and objectives, dialogue, notices): text there often embeds item,
    // place or character names through macros, so no static original matches it. Such client-language
    // text may fall back. Tooltips are excluded: their unmatched lines are counters, prices and other
    // plugins' rows, which would flood the providers (descriptions resolve live instead).
    // HUD lists and other windows (party list, counters) never send it on their own either.
    private static readonly HashSet<string> ContentScopes = new(StringComparer.Ordinal) { "Missões", "Falas", "Avisos" };
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
        if (index.Ready && clientQueue != null) return Fetch(clientQueue, x => x.Client, displayed, request, main, priority, ContentScopes.Contains(scope));
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
        var failed = result.Error != null;
        if (!failed && (main || !gameContent)) return result;
        foreach (var queues in chain)
        {
            var next = lane(queues);
            // A failed queue is not asked again; a queue only skipped by TXT-only mode is.
            if (failed && ReferenceEquals(next, primary)) continue;
            var attempt = next.Get(text, true, priority);
            if (attempt.Text != null || attempt.Pending && attempt.Error == null) return attempt;
            if (attempt.Error != null) { result = attempt; failed = true; }
        }
        return result;
    }
}
