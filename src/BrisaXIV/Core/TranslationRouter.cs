namespace BrisaXIV.Core;

// Fallback queues receive what the main path cannot deliver: text blocked by TXT-only mode
// after resolving to a Japanese game original, or text whose main request failed.
public sealed record FallbackQueues(TranslationQueue Japanese, TranslationQueue Client);

public sealed class TranslationRouter(TranslationQueue queue, TranslationFile file, SourceTextIndex index, bool japaneseSource, TranslationQueue? clientQueue = null, bool cachedOnly = false, bool prioritizeDialogue = false,
    FallbackQueues? fallback = null, ISourceTextResolver? live = null)
{
    // Game content (quest journal, tooltips, dialogue, notices): text there often embeds item, place or
    // character names through macros, so no static original matches it. Such client-language text may
    // fall back to Google; HUD lists and other windows (party list, counters) never send it on their own.
    private static readonly HashSet<string> ContentScopes = new(StringComparer.Ordinal) { "Missões", "Itens e ações", "Falas", "Avisos" };
    public bool CanRequest(string scope, bool priority = false) => !cachedOnly || fallback != null || (prioritizeDialogue && (scope == "Falas" || priority));
    public TranslationView Get(string displayed, bool request = true, string scope = "", bool priority = false)
    {
        priority = request && prioritizeDialogue && (scope == "Falas" || priority);
        var main = request && (!cachedOnly || priority);
        if (file.TryGet(displayed, out var saved)) return new(saved, null, false);
        if (!japaneseSource) return Fetch(clientQueue ?? queue, fallback?.Client, displayed, request, main, priority, false);
        if (TextRules.ContainsJapanese(displayed)) return Fetch(queue, fallback?.Japanese, displayed, request, main, priority, true);
        // Live sources come first: they hold the evaluated text of the hovered tooltip,
        // including numbers and level-dependent clauses that the static index cannot match.
        if (live?.TryResolve(displayed, scope, out var japanese) == true || index.TryResolve(displayed, scope, out japanese))
            return Fetch(queue, fallback?.Japanese, japanese, request, main, priority, true);
        if (index.Ready && clientQueue != null) return Fetch(clientQueue, fallback?.Client, displayed, request, main, priority, ContentScopes.Contains(scope));
        return new(null, index.Ready
            ? "Original japonês correspondente não localizado para este texto."
            : "Preparando o índice local de originais japoneses…", !index.Ready);
    }

    private static TranslationView Fetch(TranslationQueue primary, TranslationQueue? backup, string text, bool request, bool main, bool priority, bool gameContent)
    {
        var result = primary.Get(text, main, priority);
        if (result.Text != null || backup == null || !request || result.Pending && result.Error == null) return result;
        // Outside game content, client-language text (counters, player names, chat-like strings)
        // is only sent after a real failure; TXT-only mode never forwards it on its own.
        var failed = result.Error != null;
        if (!failed && (main || !gameContent)) return result;
        var second = backup.Get(text, true, priority);
        return second.Text != null || second.Pending ? second : failed ? result : second;
    }
}
