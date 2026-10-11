using System.Text;

namespace BrisaXIV.Core;

public static class TextRules
{
    public static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    // Google sometimes returns names in TeX-style quotes (``name''); show them as typographic quotes.
    private static readonly System.Text.RegularExpressions.Regex TexQuotes = new("``([^`'\\n]{1,80})''", System.Text.RegularExpressions.RegexOptions.Compiled);
    public static string NormalizeQuotes(string text) => text.Contains("``") ? TexQuotes.Replace(text, "“$1”") : text;

    public static bool ContainsJapanese(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            var c = rune.Value;
            if (c is >= 0x3040 and <= 0x30ff or >= 0x3400 and <= 0x9fff or
                >= 0xf900 and <= 0xfaff or >= 0xff66 and <= 0xff9d or >= 0x20000 and <= 0x3134f)
                return true;
        }
        return false;
    }

    public static bool HasReadableText(string text) => text.Any(char.IsLetter)
        && !Uri.TryCreate(text.Trim(), UriKind.Absolute, out _)
        && !text.StartsWith("_rsv_", StringComparison.Ordinal);

    public static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");

    public static string Unescape(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 == text.Length) { result.Append(text[i]); continue; }
            var next = text[++i];
            switch (next)
            {
                case 'n': result.Append('\n'); break;
                case 'r': result.Append('\r'); break;
                case 't': result.Append('\t'); break;
                case '\\': result.Append('\\'); break;
                default: result.Append('\\').Append(next); break;
            }
        }
        return result.ToString();
    }
}
