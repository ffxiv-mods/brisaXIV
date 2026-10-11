using BrisaXIV.Core;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text.ReadOnly;

namespace BrisaXIV;

// Translates an NPC dialogue line before the game processes it: the Talk refresh values carry the
// line (value 0) and the speaker (value 1). With a translation already available, the values are
// replaced and the game itself lays out the box, wraps the lines and types the text out.
// Lines still being translated keep the PreDraw path of GameTextCapture.
internal sealed unsafe class TalkRefreshTranslation(Func<TranslationRouter> router, Configuration config, InjectedTextMap injected)
{
    public void PreRefresh(AddonEvent type, AddonArgs args)
    {
        if (args is not AddonRefreshArgs refresh || refresh.AtkValueCount < 2 || refresh.AtkValues == 0
            || !config.Enabled || !config.NativeHud || !config.Dialogue) return;
        var values = (AtkValue*)refresh.AtkValues;
        Translate(&values[0], line: true);
        if (config.CharacterNames) Translate(&values[1], line: false);
    }

    private void Translate(AtkValue* value, bool line)
    {
        if (value->Type is not (AtkValueType.String or AtkValueType.ConstString or AtkValueType.ManagedString)
            || !value->String.HasValue) return;
        var shown = new ReadOnlySeStringSpan(value->String.AsSpan());
        var text = NativeTextContent.Extract(shown);
        if (!TextRules.HasReadableText(text)) return;
        // A refresh can carry a line this class already translated; its source is the original.
        var source = injected.TryGetOriginal(text, out var original) ? original : text;
        var result = line ? router().Get(source, true, "Falas", priority: true) : router().Get(source, true, SourceTextIndex.NamesScope);
        if (result.Text is not { } translation || !NativeTextContent.TryReplace(shown, translation, out var bytes)) return;
        var content = bytes.AsSpan(0, bytes.Length - 1);
        value->SetManagedString(content);
        injected.Add(NativeTextContent.Extract(new ReadOnlySeStringSpan(content)), source);
    }
}
