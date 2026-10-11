using System.Globalization;
using System.Text;

namespace BrisaXIV.Core;

public static class TextBoxLayout
{
    public readonly record struct ConfirmationFit(int AvailableTextHeight, int ExtraHeight, int WindowHeight);

    // The footer is occupied by buttons/check boxes, not by the message.
    public static bool TryFitConfirmation(float textY, int required, float footerY, int windowHeight, out ConfirmationFit fit)
    {
        fit = default;
        if (!float.IsFinite(textY) || !float.IsFinite(footerY) || textY < 0 || footerY <= textY
            || footerY >= windowHeight || required <= 0 || required > 1200 || windowHeight <= 0 || windowHeight > 1200) return false;
        var available = Math.Max(0, (int)Math.Floor(footerY - textY - 12));
        var extra = ExtraHeight(required, available);
        if (extra > 500 || windowHeight + extra > 1200) return false;
        fit = new(available, extra, windowHeight + extra);
        return true;
    }

    // The caller measures with the game's own font, in unscaled node coordinates.
    public static string Wrap(string text, float width, Func<string, float> measure)
    {
        if (width <= 0) return text;
        var output = new List<string>();
        foreach (var paragraph in TextRules.Normalize(text).Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (measure(candidate) <= width) { line = candidate; continue; }
                if (line.Length > 0) { output.Add(line); line = ""; }
                if (measure(word) <= width) { line = word; continue; }
                // URLs and long words can exceed a full line. Never split a Unicode grapheme.
                var elements = StringInfo.GetTextElementEnumerator(word);
                while (elements.MoveNext())
                {
                    var element = elements.GetTextElement();
                    if (line.Length > 0 && measure(line + element) > width) { output.Add(line); line = ""; }
                    line += element;
                }
            }
            output.Add(line);
        }
        return string.Join("\n", output);
    }

    public static int ExtraHeight(int required, int available) => Math.Max(0, required - available);
}
