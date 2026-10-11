using System.Collections.Concurrent;
using System.Text;

namespace BrisaXIV.Core;

// Maps the client's rendered text to Japanese by matching sheet/row/column identities.
// Conflicting originals stay unresolved rather than guessing a story translation.
public sealed class SourceTextIndex : ISourceTextResolver
{
    // Character names from the NPC sheets. Within this scope the first original wins,
    // because one name often appears with harmless variants in unrelated sheets.
    public const string NamesScope = "Nomes";
    // Case-insensitive matches are limited to short labels and names: enemy names are stored
    // in lowercase ("striking dummy") but displayed capitalized ("Striking Dummy").
    private const int FoldedMaximumLength = 80;
    private sealed record Entry(string? Japanese);
    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Entry> folded = new(StringComparer.Ordinal);
    public int Count => entries.Count;
    private volatile bool ready;
    public bool Ready { get => ready; set => ready = value; }

    public static string Key(string text)
    {
        var output = new StringBuilder();
        var space = false;
        foreach (var c in text.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsWhiteSpace(c)) { space = output.Length > 0; continue; }
            if (space) { output.Append(' '); space = false; }
            output.Append(c switch { '’' or '‘' => '\'', '“' or '”' => '"', '─' or '—' or '–' => '-', _ => c });
        }
        return output.ToString();
    }

    private static string EntryKey(string display, string scope) => scope + "\u001f" + Key(display);

    public void Add(string display, string japanese, string scope)
    {
        if (!TextRules.HasReadableText(display) || !TextRules.ContainsJapanese(japanese)) return;
        japanese = TextRules.Normalize(japanese);
        AddOne(EntryKey(display, scope), japanese, false);
        AddOne(EntryKey(display, ""), japanese, false);
    }

    public void AddName(string display, string japanese)
    {
        if (!TextRules.HasReadableText(display) || !TextRules.ContainsJapanese(japanese)) return;
        AddOne(EntryKey(display, NamesScope), TextRules.Normalize(japanese), true);
    }

    private void AddOne(string key, string? japanese, bool firstWins)
    {
        Merge(entries, key, japanese, firstWins);
        if (Fold(key) is { } foldedKey) Merge(folded, foldedKey, japanese, firstWins);
    }

    private static void Merge(ConcurrentDictionary<string, Entry> target, string key, string? japanese, bool firstWins) => target.AddOrUpdate(key,
        new Entry(japanese), (_, old) => firstWins || old.Japanese == japanese ? old : new Entry(null));

    private static string? Fold(string key) => key.Length - key.IndexOf('\u001f') - 1 <= FoldedMaximumLength ? key.ToLowerInvariant() : null;

    public bool TryResolve(string display, string scope, out string japanese)
    {
        // The surface scope wins over the global key, so a name from the NPC sheets is used
        // even when the same English text is ambiguous across the whole game.
        var entry = Find(EntryKey(display, scope)) ?? Find(EntryKey(display, ""));
        japanese = entry?.Japanese ?? "";
        return japanese.Length > 0;
    }

    private Entry? Find(string key) => entries.TryGetValue(key, out var exact) ? exact
        : Fold(key) is { } foldedKey && folded.TryGetValue(foldedKey, out var other) ? other : null;

    public IEnumerable<string?[]> Export() => entries.Select(x => new string?[] { x.Key, x.Value.Japanese });
    public void Import(string key, string? japanese)
    {
        entries[key] = new Entry(japanese);
        if (Fold(key) is { } foldedKey) Merge(folded, foldedKey, japanese, key.StartsWith(NamesScope + "\u001f", StringComparison.Ordinal));
    }
}
