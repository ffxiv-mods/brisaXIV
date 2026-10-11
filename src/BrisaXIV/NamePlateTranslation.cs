using BrisaXIV.Core;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace BrisaXIV;

// Translates NPC names on floating nameplates through Dalamud's cooperative nameplate API,
// so other plugins that style names keep working. Player characters and player-named
// companions are never read or sent anywhere.
internal sealed class NamePlateTranslation : IDisposable
{
    private readonly INamePlateGui gui;
    private readonly Func<TranslationRouter> router;
    private readonly Func<bool> enabled;
    private readonly HashSet<string> waiting = new(StringComparer.Ordinal);
    private bool wasEnabled;

    public NamePlateTranslation(INamePlateGui gui, Func<TranslationRouter> router, Func<bool> enabled)
    {
        this.gui = gui; this.router = router; this.enabled = enabled;
        gui.OnNamePlateUpdate += Update;
    }

    // Called from Framework.Update: redraws when the option changes or a requested name arrives.
    public void Pump()
    {
        var active = enabled();
        if (active != wasEnabled) { wasEnabled = active; waiting.Clear(); gui.RequestRedraw(); return; }
        if (!active || waiting.Count == 0) return;
        var ready = waiting.Where(name => router().Get(name, false, SourceTextIndex.NamesScope).Text != null).ToArray();
        if (ready.Length == 0) return;
        foreach (var name in ready) waiting.Remove(name);
        gui.RequestRedraw();
    }

    private void Update(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        if (!enabled()) return;
        foreach (var handler in handlers)
        {
            if (!Translatable(handler)) continue;
            var name = handler.Name.TextValue;
            if (!TextRules.HasReadableText(name)) continue;
            var result = router().Get(name, true, SourceTextIndex.NamesScope);
            if (result.Text is { } translated) handler.NameParts.Text = new SeStringBuilder().AddText(TextRules.Normalize(translated)).Build();
            else if (result.Pending) waiting.Add(name);
        }
    }

    private static bool Translatable(INamePlateUpdateHandler handler)
        => handler.NamePlateKind is NamePlateKind.BattleNpcEnemy or NamePlateKind.BattleNpcFriendly or NamePlateKind.EventNpcCompanion or NamePlateKind.EventObject
            && !(handler.GameObject is IBattleNpc npc && npc.BattleNpcKind is BattleNpcSubKind.Buddy or BattleNpcSubKind.Player or BattleNpcSubKind.RaceChocobo);

    public void Dispose()
    {
        gui.OnNamePlateUpdate -= Update;
        gui.RequestRedraw();
    }
}
