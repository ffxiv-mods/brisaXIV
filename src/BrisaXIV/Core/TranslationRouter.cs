namespace BrisaXIV.Core;

public sealed class TranslationRouter(TranslationQueue queue, TranslationFile file, SourceTextIndex index, bool japaneseSource, TranslationQueue? clientQueue = null, bool cachedOnly = false, bool prioritizeDialogue = false)
{
    public bool CanRequest(string scope, bool priority = false) => !cachedOnly || (prioritizeDialogue && (scope == "Falas" || priority));
    public TranslationView Get(string displayed, bool request = true, string scope = "", bool priority = false)
    {
        priority = request && prioritizeDialogue && (scope == "Falas" || priority);
        request &= !cachedOnly || priority;
        if (file.TryGet(displayed, out var saved)) return new(saved, null, false);
        if (!japaneseSource) return (clientQueue ?? queue).Get(displayed, request, priority);
        if (TextRules.ContainsJapanese(displayed)) return queue.Get(displayed, request, priority);
        if (index.TryResolve(displayed, scope, out var japanese)) return queue.Get(japanese, request, priority);
        if (index.Ready && clientQueue != null) return clientQueue.Get(displayed, request, priority);
        return new(null, index.Ready
            ? "Original japonês correspondente não localizado para este texto."
            : "Preparando o índice local de originais japoneses…", !index.Ready);
    }
}
