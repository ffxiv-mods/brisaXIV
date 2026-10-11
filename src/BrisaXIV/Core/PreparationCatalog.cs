using System.Text.Json;
using System.Text.Encodings.Web;

namespace BrisaXIV.Core;

public sealed record PreparationText(string Text, int Expansion, string Group);

public sealed class PreparationCatalog
{
    private readonly Dictionary<string, PreparationText> texts = new(StringComparer.Ordinal);
    public IReadOnlyList<PreparationText> Ordered { get; private set; } = [];
    public string Fingerprint { get; private set; } = "";
    private volatile bool ready;
    public bool Ready => ready;
    public long CharacterCount { get; private set; }
    private long[] characterPrefix = [0];
    public long ConfirmedCharacters(int cursor) => characterPrefix[Math.Clamp(cursor, 0, characterPrefix.Length - 1)];
    private void Measure()
    {
        characterPrefix = new long[Ordered.Count + 1];
        for (var i = 0; i < Ordered.Count; i++) characterPrefix[i + 1] = characterPrefix[i] + TextAmount.Measure(Ordered[i].Text).Characters;
        CharacterCount = characterPrefix[^1];
    }

    public void Add(string text, int expansion, string group)
    {
        text = TextRules.Normalize(text);
        if (!TextRules.ContainsJapanese(text)) return;
        if (!texts.TryGetValue(text, out var old) || expansion > old.Expansion) texts[text] = new(text, expansion, group);
    }

    public void Finish(string fingerprint)
    {
        Ordered = texts.Values.OrderByDescending(x => x.Expansion).ThenBy(x => x.Group, StringComparer.Ordinal).ThenBy(x => x.Text, StringComparer.Ordinal).ToArray();
        Measure();
        Fingerprint = fingerprint;
        ready = true;
        texts.Clear();
    }

    public bool TryLoad(string path, string fingerprint, CancellationToken token)
    {
        if (!File.Exists(path)) return false;
        using var reader = File.OpenText(path);
        if (reader.ReadLine() != "# BrisaXIV Japanese corpus v1 " + fingerprint) return false;
        var loaded = new List<PreparationText>();
        while (reader.ReadLine() is { } line)
        {
            token.ThrowIfCancellationRequested();
            if (JsonSerializer.Deserialize<PreparationText>(line) is { } entry) loaded.Add(entry);
        }
        Ordered = loaded; Measure(); Fingerprint = fingerprint; ready = true;
        return true;
    }

    public void Save(string path, CancellationToken token)
    {
        var temporary = path + "." + Guid.NewGuid() + ".tmp";
        try
        {
            using (var writer = new StreamWriter(temporary, false, new System.Text.UTF8Encoding(false)))
            {
                writer.WriteLine("# BrisaXIV Japanese corpus v1 " + Fingerprint);
                var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
                foreach (var entry in Ordered) { token.ThrowIfCancellationRequested(); writer.WriteLine(JsonSerializer.Serialize(entry, options)); }
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
