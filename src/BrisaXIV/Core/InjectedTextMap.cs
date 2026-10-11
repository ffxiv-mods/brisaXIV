namespace BrisaXIV.Core;

// Translations written into the game's own refresh values (the game then lays them out natively).
// Remembers translation -> original so a later read of the same text, by the HUD capture or by the
// next refresh, maps back to its source instead of treating Portuguese as new text to translate.
public sealed class InjectedTextMap(int capacity = 256)
{
    private readonly object gate = new();
    private readonly Dictionary<string, string> originals = new(StringComparer.Ordinal);
    private readonly Queue<string> order = new();

    private static string Key(string text) => SourceTextIndex.Key(TextRules.Normalize(text));

    public void Add(string injected, string original)
    {
        var key = Key(injected);
        if (key.Length == 0) return;
        lock (gate)
        {
            if (!originals.ContainsKey(key)) order.Enqueue(key);
            originals[key] = original;
            while (order.Count > capacity) originals.Remove(order.Dequeue());
        }
    }

    public bool TryGetOriginal(string text, out string original)
    {
        lock (gate) return originals.TryGetValue(Key(text), out original!);
    }
}
