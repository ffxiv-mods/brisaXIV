using BrisaXIV.Core;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Text.ReadOnly;

namespace BrisaXIV;

// Evaluates sheet text the way the game renders it (numbers, level-dependent clauses, item and
// character names, gender) in the client language and in Japanese, giving the pair that maps the
// rendered English to its complete Japanese original. Must run on the framework thread.
internal sealed class GameTextEvaluator(IDataManager data, ISeStringEvaluator evaluator, ClientLanguage client)
{
    public ClientLanguage Client => client;

    public bool TryPair<T>(uint id, Func<T, ReadOnlySeString> text, out string display, out string japanese) where T : struct, IExcelRow<T>
    {
        display = japanese = "";
        if (id == 0) return false;
        try
        {
            return data.GetExcelSheet<T>(client).TryGetRow(id, out var shown) && data.GetExcelSheet<T>(ClientLanguage.Japanese).TryGetRow(id, out var original)
                && TryPair(text(shown), text(original), out display, out japanese);
        }
        // A sheet missing in this client version only disables this lookup.
        catch (Exception) { return false; }
    }

    public bool TryPair(ReadOnlySeString shown, ReadOnlySeString original, out string display, out string japanese)
    {
        display = japanese = "";
        try
        {
            display = NativeTextContent.Extract(evaluator.Evaluate(shown, default, client).AsSpan());
            japanese = NativeTextContent.Extract(evaluator.Evaluate(original, default, ClientLanguage.Japanese).AsSpan());
        }
        // An unsupported macro only disables this pair; it must never stop the framework update.
        catch (Exception) { return false; }
        return TextRules.HasReadableText(display) && TextRules.ContainsJapanese(japanese);
    }
}
