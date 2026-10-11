using BrisaXIV.Core;
using Dalamud.Game;
using Dalamud.Game.Gui;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace BrisaXIV;

// Tooltip descriptions contain macros (potency, level-dependent clauses, item values) that the
// static index drops. For the action or item under the cursor, this evaluates the description in
// the client language and in Japanese with the live player state, so the rendered tooltip maps
// to its exact Japanese original. Refresh runs on the framework thread; lookups read a snapshot.
internal sealed class HoveredTextSources(IGameGui gui, IDataManager data, ISeStringEvaluator evaluator, ClientLanguage client) : ISourceTextResolver
{
    private (DetailKind Kind, uint Action, uint Base, ulong Item) last;
    private volatile IReadOnlyDictionary<string, string> current = new Dictionary<string, string>();

    public bool TryResolve(string display, string scope, out string japanese)
        => current.TryGetValue(SourceTextIndex.Key(TextRules.Normalize(display)), out japanese!);

    public void Refresh()
    {
        var action = gui.HoveredAction;
        var key = (action.DetailKind, action.ActionId, action.BaseActionId, gui.HoveredItem);
        if (key == last) return;
        last = key;
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in new[] { action.ActionId, action.BaseActionId }.Distinct())
            switch (action.DetailKind)
            {
                case DetailKind.Action: AddRow<ActionTransient>(pairs, id, x => x.Description); break;
                case DetailKind.Trait: AddRow<TraitTransient>(pairs, id, x => x.Description); break;
                case DetailKind.GeneralAction: AddRow<GeneralAction>(pairs, id, x => x.Description); break;
                case DetailKind.CraftingAction: AddRow<CraftAction>(pairs, id, x => x.Description); break;
                case DetailKind.PetOrder: AddRow<PetAction>(pairs, id, x => x.Description); break;
            }
        // High-quality (+1,000,000) and collectible (+500,000) ids share the base item row.
        if (gui.HoveredItem is > 0 and < 2_000_000) AddRow<Item>(pairs, (uint)(gui.HoveredItem % 500_000), x => x.Description);
        current = pairs;
    }

    private void AddRow<T>(Dictionary<string, string> pairs, uint id, Func<T, ReadOnlySeString> text) where T : struct, IExcelRow<T>
    {
        if (id == 0) return;
        try
        {
            if (!data.GetExcelSheet<T>(client).TryGetRow(id, out var shown) || !data.GetExcelSheet<T>(ClientLanguage.Japanese).TryGetRow(id, out var original)) return;
            var display = NativeTextContent.Extract(evaluator.Evaluate(text(shown), default, client).AsSpan());
            var japanese = NativeTextContent.Extract(evaluator.Evaluate(text(original), default, ClientLanguage.Japanese).AsSpan());
            if (TextRules.HasReadableText(display) && TextRules.ContainsJapanese(japanese)) pairs[SourceTextIndex.Key(display)] = japanese;
        }
        // A sheet missing in this client version or an unsupported macro only disables this lookup;
        // it must never pause the whole HUD capture, which runs in the same framework update.
        catch (Exception) { }
    }
}
