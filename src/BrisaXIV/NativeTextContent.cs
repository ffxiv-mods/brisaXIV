using Lumina.Text.ReadOnly;
using Lumina.Text.Payloads;
using System.Text;
using System.Text.RegularExpressions;

namespace BrisaXIV;

internal static class NativeTextContent
{
    public static string Extract(ReadOnlySeStringSpan text) => Core.TextRules.Normalize(text.ExtractText());

    public static bool CanReplace(ReadOnlySeStringSpan text)
    {
        foreach (var payload in text)
            if (payload.Type != ReadOnlySePayloadType.Text && (payload.Type != ReadOnlySePayloadType.Macro
                || payload.MacroCode is not (MacroCode.NewLine or MacroCode.Color or MacroCode.EdgeColor
                    or MacroCode.ShadowColor or MacroCode.ColorType or MacroCode.EdgeColorType or MacroCode.Bold or MacroCode.Italic
                    or MacroCode.SoftHyphen or MacroCode.NonBreakingSpace or MacroCode.Hyphen))) return false;
        return true;
    }

    public static byte[] EncodePlain(string text) => Encoding.UTF8.GetBytes(text.Replace("\n", "\u0002\u0010\u0001\u0003") + "\0");

    // A highlighted name in the translation: the quotes a translator puts around 「」 names.
    private static readonly Regex Quoted = new("“([^”\\n]{1,60})”|「([^」\\n]{1,60})」|『([^』\\n]{1,60})』|«([^»\\n]{1,60})»|\"([^\"\\n]{1,60})\"", RegexOptions.Compiled);

    public static bool TryReplace(ReadOnlySeStringSpan text, string translation, out byte[] replacement)
    {
        replacement = [];
        translation = Core.TextRules.NormalizeQuotes(translation);
        if (!CanReplace(text) || translation.IndexOfAny(['\0', '\u0002', '\u0003']) >= 0) return false;
        var items = new List<byte[]?>(); // null marks a text position
        foreach (var payload in text)
        {
            if (payload.Type == ReadOnlySePayloadType.Text || payload.MacroCode is MacroCode.NewLine or MacroCode.SoftHyphen or MacroCode.NonBreakingSpace or MacroCode.Hyphen)
            { items.Add(null); continue; }
            var envelope = new byte[payload.EnvelopeByteLength];
            payload.WriteEnvelopeTo(envelope);
            items.Add(envelope);
        }
        var first = items.IndexOf(null);
        if (first < 0) return false;
        var last = items.LastIndexOf(null);
        // Style runs between text positions are inline emphasis, e.g. a colored action name:
        // the first run opens it and the next one resets it. Translation changes word order,
        // so the emphasis moves to the quoted names of the translation instead.
        var runs = new List<byte[]>();
        for (var i = first; i <= last; i++)
        {
            if (items[i] == null) continue;
            var run = new MemoryStream();
            for (; i <= last && items[i] != null; i++) run.Write(items[i]);
            runs.Add(run.ToArray());
        }
        var highlight = runs.Count >= 2 && Quoted.IsMatch(translation);
        var content = highlight ? EncodeHighlighted(translation, runs[0], runs[1]) : EncodePlain(translation)[..^1];
        if (content.Length > 24000) return false;
        using var buffer = new MemoryStream();
        for (var i = 0; i < first; i++) buffer.Write(items[i]);
        buffer.Write(content);
        // Without highlighting, keep every envelope and reset in order after the text.
        for (var i = highlight ? last + 1 : first; i < items.Count; i++)
            if (items[i] is { } envelope) buffer.Write(envelope);
        buffer.WriteByte(0);
        replacement = buffer.ToArray();
        return true;
    }

    private static byte[] EncodeHighlighted(string translation, byte[] open, byte[] close)
    {
        using var buffer = new MemoryStream();
        var position = 0;
        foreach (Match match in Quoted.Matches(translation))
        {
            buffer.Write(EncodePlain(translation[position..match.Index])[..^1]);
            var name = match.Groups.Cast<Group>().Skip(1).First(x => x.Success).Value;
            buffer.Write(open); buffer.Write(EncodePlain(name)[..^1]); buffer.Write(close);
            position = match.Index + match.Length;
        }
        buffer.Write(EncodePlain(translation[position..])[..^1]);
        return buffer.ToArray();
    }
}
