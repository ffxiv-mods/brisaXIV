using System.Collections.Concurrent;
using System.Text;

namespace BrisaXIV.Core;

// Maps the client's rendered text to Japanese by matching sheet/row/column identities.
// Conflicting originals stay unresolved rather than guessing a story translation.
public sealed class SourceTextIndex
{
    private sealed record Entry(string? Japanese);
    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);
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
        AddOne(EntryKey(display, scope), japanese);
        AddOne(EntryKey(display, ""), japanese);
    }

    private void AddOne(string key, string japanese) => entries.AddOrUpdate(key,
        new Entry(japanese), (_, old) => old.Japanese == japanese ? old : new Entry(null));

    public bool TryResolve(string display, string scope, out string japanese)
    {
        var key = EntryKey(display, scope);
        if (!entries.TryGetValue(key, out var entry)) entries.TryGetValue(EntryKey(display, ""), out entry);
        japanese = entry?.Japanese ?? "";
        return japanese.Length > 0;
    }

    public IEnumerable<string?[]> Export() => entries.Select(x => new string?[] { x.Key, x.Value.Japanese });
    public void Import(string key, string? japanese) => entries[key] = new Entry(japanese);
}
