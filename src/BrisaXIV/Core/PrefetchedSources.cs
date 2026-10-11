using System.Collections.Concurrent;

namespace BrisaXIV.Core;

// Rendered client text -> complete Japanese original, evaluated ahead of time for accepted quests,
// the current job's actions and owned items (see TranslationPrefetch). The router consults it like
// the hovered-tooltip resolver, so the prefetched translation is found the moment the text appears.
public sealed class PrefetchedSources(int capacity = 20_000) : ISourceTextResolver
{
    private readonly ConcurrentDictionary<string, string> originals = new(StringComparer.Ordinal);
    public int Count => originals.Count;

    private static string Key(string display) => SourceTextIndex.Key(TextRules.Normalize(display));

    public void Add(string display, string japanese)
    {
        if (!TextRules.HasReadableText(display) || !TextRules.ContainsJapanese(japanese)) return;
        // The set follows what the player has; past quests and items are rebuilt when needed.
        if (originals.Count >= capacity) originals.Clear();
        originals[Key(display)] = TextRules.Normalize(japanese);
    }

    public bool TryResolve(string display, string scope, out string japanese) => originals.TryGetValue(Key(display), out japanese!);
}

// Asks each resolver in order; the first match wins.
public sealed class CompositeSourceResolver(params ISourceTextResolver[] resolvers) : ISourceTextResolver
{
    public bool TryResolve(string display, string scope, out string japanese)
    {
        foreach (var resolver in resolvers)
            if (resolver.TryResolve(display, scope, out japanese)) return true;
        japanese = "";
        return false;
    }
}
