using BrisaXIV.Core;
using Lumina.Text.ReadOnly;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BrisaXIV;

internal sealed record CapturedWindow(string Name, string Label, string Category, string[] Lines);

// All traversal and native writes run synchronously on the Dalamud framework thread.
// A node address is only used after rediscovery in the current live addon tree.
internal sealed unsafe class GameTextCapture
{
    private sealed record AppliedText(ushort AddonId, uint NodeId, byte[] OriginalBytes, string Original, string Translation,
        string Rendered, NativeTextLayout.Change[] Layout, bool Loading);
    private readonly Dictionary<nint, DialoguePresentation> presentations = new();
    private HashSet<string> npcNames = new(StringComparer.Ordinal);
    public void SetNpcNames(IEnumerable<string> names) => npcNames = names.Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal);
    private readonly Dictionary<nint, AppliedText> applied = new();
    private readonly HashSet<nint> live = new();
    private readonly Dictionary<nint, double> since = new();
    private readonly Dictionary<nint, string> lastSource = new();
    private static readonly HashSet<string> DialogueNames = new(StringComparer.Ordinal)
        { "Talk", "_BattleTalk", "BattleTalk", "TalkSubtitle", "_MiniTalk", "MiniTalk", "CutSceneSelectString" };
    private static bool NpcInteraction(string name) => DialogueNames.Contains(name)
        || name is "SelectString" or "SelectIconString" or "SelectYesno" or "JournalAccept" or "JournalResult";
    private static readonly HashSet<string> MenuNames = new(StringComparer.Ordinal)
        { "SelectString", "SelectIconString", "SelectYesno", "SelectOk", "ContextMenu", "_MainCommand",
          "Character", "CharacterStatus", "CharacterProfile", "ArmouryBoard", "Inventory", "InventoryLarge",
          "InventoryExpansion", "RecipeNote", "RecipeTree", "ContentsFinder", "ContentsFinderConfirm", "Teleport",
          "Config", "ConfigSystem", "ConfigCharacter", "ConfigChara", "ConfigKeybind", "SystemMenu", "MainCommand",
          "Shop", "ShopExchangeItem", "ShopExchangeCurrency", "RetainerList", "Achievement", "RecommendList",
          "HowTo", "Guide", "QuestRedo", "Social", "Buddy", "MountNote", "MinionNote", "FishGuide", "AdventureNoteBook" };
    private static bool Excluded(string name) => name.Contains("Chat", StringComparison.OrdinalIgnoreCase)
        || name.Contains("NamePlate", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Letter", StringComparison.Ordinal) || name.Contains("Input", StringComparison.OrdinalIgnoreCase);

    private static string? Category(string name, Configuration config)
    {
        if (Excluded(name)) return null;
        if (TextCategoryPolicy.IsJournal(name)) return (config.Journal ?? config.Quests) ? "Missões" : null;
        switch (TextCategoryPolicy.SpecializedCategory(name))
        {
            case "Locais e viagens": return (config.Locations ?? (name == "_AreaText" ? config.Notices : config.Menus)) ? (name == "_AreaText" ? "Avisos" : "Menus") : null;
            case "Receitas e fabricação": return (config.Crafting ?? config.Menus) ? "Menus" : null;
            case "Conquistas e coleções": return (config.Collections ?? config.Menus) ? "Menus" : null;
        }
        if (TextCategoryPolicy.IsSubtitle(name)) return config.Subtitles ? "Falas" : null;
        if (DialogueNames.Contains(name)) return config.Dialogue ? "Falas" : null;
        if (name is "_WideText" or "_TextError" or "_AreaText" or "_TextClassChange" or "_TextGimmickHint")
            return config.Notices ? "Avisos" : null;
        if (name.StartsWith("Journal", StringComparison.Ordinal) || name is "ScenarioTree" or "ToDoList" or "_ToDoList")
            return config.Quests ? "Missões" : null;
        if (name == "ItemDetail") return config.Items ? "Itens e ações" : null;
        if (name == "ActionDetail") return config.Actions ? "Itens e ações" : null;
        if (TextCategoryPolicy.IsConfirmation(name)) return config.Confirmations ? "Menus" : null;
        if (MenuNames.Contains(name)) return config.Menus ? "Menus" : null;
        return config.OtherWindows ? "Outras janelas" : null;
    }

    public IReadOnlyList<CapturedWindow> Scan(Configuration config, TranslationRouter router, bool restoreOnly = false)
    {
        var output = new List<CapturedWindow>();
        var module = RaptureAtkModule.Instance();
        if (module == null) { applied.Clear(); presentations.Clear(); return output; }
        var list = &module->RaptureAtkUnitManager.AtkUnitManager.AllLoadedUnitsList;
        live.Clear();
        var count = Math.Min((int)list->Count, 256);
        for (var index = 0; index < count; index++)
        {
            var addon = list->Entries[index].Value;
            if (addon == null || !addon->IsReady) continue;
            var name = addon->NameString;
            var category = Category(name, config);
            // Keep translated text throughout close animations. Restore only on an explicit
            // disable/unload; an invisible addon can still be drawn while fading out.
            var active = !restoreOnly && config.Enabled && addon->IsVisible && category != null;
            var restore = NativeTextPolicy.ShouldRestore(restoreOnly, config.Enabled, config.NativeHud, category != null);
            if (!active && applied.Count == 0) continue;
            var lines = new List<(float Y, float X, string Text)>();
            var visited = new HashSet<nint>();
            var budget = 2048;
            Walk(addon, &addon->UldManager, addon->Id, name, active, config.NativeHud, restore, config.WaitForDialogue, config.DialogueWaitSeconds,
                router, category ?? "", NpcInteraction(name), config, lines, visited, 0, false, ref budget);
            if (active && lines.Count > 0)
            {
                var texts = lines.OrderBy(x => x.Y).ThenBy(x => x.X).Select(x => x.Text).Distinct(StringComparer.Ordinal).Take(160).ToArray();
                output.Add(new(name, Label(name), category!, texts));
            }
        }
        foreach (var address in applied.Keys.Where(x => !live.Contains(x)).ToArray()) applied.Remove(address);
        foreach (var address in lastSource.Keys.Where(x => !live.Contains(x)).ToArray()) { lastSource.Remove(address); since.Remove(address); presentations.Remove(address); }
        return output.OrderBy(x => x.Category == "Falas" ? 0 : 1).ThenBy(x => x.Name).ToArray();
    }

    public void BeforeDraw(nint addonAddress, Configuration config, TranslationRouter router)
    {
        var addon = (AtkUnitBase*)addonAddress;
        if (addon == null || !addon->IsReady) return;
        var category = Category(addon->NameString, config);
        if (category == null) return;
        var active = config.Enabled && addon->IsVisible && category != null;
        var restore = NativeTextPolicy.ShouldRestore(false, config.Enabled, config.NativeHud, category != null);
        if (!active && applied.Count == 0) return;
        var budget = 2048;
        Walk(addon, &addon->UldManager, addon->Id, addon->NameString, active, config.NativeHud, restore, config.WaitForDialogue, config.DialogueWaitSeconds,
            router, category ?? "", NpcInteraction(addon->NameString), config, new List<(float, float, string)>(), new HashSet<nint>(), 0, false, ref budget);
    }

    private void Walk(AtkUnitBase* addon, AtkUldManager* manager, ushort addonId, string addonName, bool active, bool native, bool restore, bool showLoading, int maximumWait,
        TranslationRouter router, string category, bool priority, Configuration config, List<(float Y, float X, string Text)> lines, HashSet<nint> visited, int depth, bool windowComponent, ref int budget)
    {
        if (depth > 12 || manager == null || manager->NodeList == null || manager->NodeListCount > 4096) return;
        for (var i = 0; i < manager->NodeListCount && budget-- > 0; i++)
        {
            var node = manager->NodeList[i];
            if (node == null || !visited.Add((nint)node)) continue;
            var address = (nint)node;
            if (node->Type == NodeType.Text)
            {
                live.Add(address);
                var textNode = (AtkTextNode*)node;
                var bytes = textNode->NodeText.AsSpan();
                if (bytes.Length == 0 || bytes.Length > 24000) continue;
                var seString = new ReadOnlySeStringSpan(bytes);
                var current = NativeTextContent.Extract(seString);
                var source = current;
                var originalSource = applied.TryGetValue(address, out var prior) ? prior.Original : source;
                var characterName = DialoguePresentation.IsSpeakerName(addonName, node->NodeId) || npcNames.Contains(originalSource);
                var heading = TextCategoryPolicy.IsWindowHeading(windowComponent, node->NodeId)
                    || addonName == "ItemDetail" && ((AddonItemDetail*)addon)->ItemNameText == textNode
                    || addonName == "JournalDetail" && ((AddonJournalDetail*)addon)->DutyNameTextNode == textNode
                    || addonName == "JournalAccept" && ((AddonJournalAccept*)addon)->QuestTitleText == textNode;
                var partCategory = TextCategoryPolicy.PartCategory(addonName, category);
                var nodeEnabled = characterName ? config.CharacterNames
                    : TextCategoryPolicy.IsNodeEnabled(config.TextPartEnabled(partCategory, heading), true, heading, false);
                var restoreNode = restore || !nodeEnabled;
                if (applied.TryGetValue(address, out var saved))
                {
                    if (NativeTextPolicy.MatchesAppliedText(saved.AddonId == addonId && saved.NodeId == node->NodeId, current, saved.Original, saved.Rendered))
                    {
                        source = saved.Original;
                        if (restoreNode)
                        {
                            textNode->SetText(saved.OriginalBytes.AsSpan());
                            NativeTextLayout.Restore(addon, manager, saved.Layout);
                            applied.Remove(address);
                            saved = null;
                        }
                        else if ((!active || !Visible(node)) && current == saved.Original)
                        {
                            // The game can repopulate its original as the addon closes.
                            // Keep the last rendered text without any new API request.
                            if (NativeTextContent.TryReplace(new ReadOnlySeStringSpan(saved.OriginalBytes.AsSpan(0, saved.OriginalBytes.Length - 1)), saved.Rendered, out var closingBytes))
                                textNode->SetText(closingBytes.AsSpan());
                        }
                    }
                    else
                    {
                        if ((active || restoreNode) && saved.AddonId == addonId && saved.NodeId == node->NodeId) NativeTextLayout.Restore(addon, manager, saved.Layout);
                        applied.Remove(address); // The game populated a new line or reused a node.
                        saved = null;
                    }
                }
                if (!active || !nodeEnabled || !Visible(node) || !TextRules.HasReadableText(source)) continue;
                if (!presentations.TryGetValue(address, out var presentation)) presentations[address] = presentation = new();
                presentation.ObserveSource(source);
                if (!lastSource.TryGetValue(address, out var previous) || previous != source)
                {
                    lastSource[address] = source; since[address] = Environment.TickCount64;
                }
                var unchanged = (long)(Environment.TickCount64 - since[address]);
                var result = characterName ? router.Get(source, true, SourceTextIndex.NamesScope)
                    : HudTranslation.Get(router, source, category, priority, unchanged);
                if (unchanged >= 350 || result.Text != null) lines.Add((node->ScreenY, node->ScreenX, source));
                if (!native) continue;
                var translated = result.Text;
                var loading = false;
                if (translated == null)
                {
                    var preparing = !characterName && showLoading && DialogueNames.Contains(addonName) && result.Error == null
                        && router.CanRequest(category, priority) && NativeTextContent.CanReplace(seString)
                        && (result.Pending || unchanged < 350);
                    if (presentation.ShouldWait(Environment.TickCount64, preparing, maximumWait))
                    { translated = "Traduzindo…"; loading = true; }
                    else
                    {
                        if (saved?.Loading == true)
                        {
                            textNode->SetText(saved.OriginalBytes.AsSpan());
                            NativeTextLayout.Restore(addon, manager, saved.Layout);
                            applied.Remove(address);
                        }
                        continue;
                    }
                }
                else presentation.ShouldWait(Environment.TickCount64, false, maximumWait);
                translated = TextRules.Normalize(translated);
                if (applied.TryGetValue(address, out saved))
                {
                    if (translated == saved.Translation && loading == saved.Loading && current == saved.Rendered && NativeTextLayout.IsCurrent(manager, saved.Layout)) continue;
                    NativeTextLayout.Restore(addon, manager, saved.Layout);
                }
                if (current == translated && saved == null) continue;
                var original = saved?.OriginalBytes ?? new byte[bytes.Length + 1];
                if (saved == null) bytes.CopyTo(original);
                if (!NativeTextLayout.TryPrepare(addon, manager, textNode, new ReadOnlySeStringSpan(original.AsSpan(0, original.Length - 1)), translated,
                    category == "Falas", addonName, out var layout))
                {
                    if (saved != null) { textNode->SetText(original.AsSpan()); applied.Remove(address); }
                    continue;
                }
                textNode->SetText(layout.Bytes.AsSpan());
                applied[address] = new(addonId, node->NodeId, original, source, translated, layout.Rendered, layout.Changes, loading);
            }
            else if ((ushort)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null && component->GetComponentType() is not (ComponentType.TextInput or ComponentType.NumericInput))
                    Walk(addon, &component->UldManager, addonId, addonName, active, native, restore, showLoading, maximumWait, router, category, priority, config, lines, visited, depth + 1,
                        component->GetComponentType() == ComponentType.Window, ref budget);
            }
        }
    }

    public void ReleaseHold(nint addonAddress)
    {
        foreach (var presentation in presentations.Values) presentation.Reset();
    }

    private static bool Visible(AtkResNode* node)
    {
        for (var i = 0; node != null && i < 64; i++, node = node->ParentNode)
            if (!node->NodeFlags.HasFlag(NodeFlags.Visible) || node->IsDrawDisabled) return false;
        return node == null;
    }

    private static string Label(string name) => name switch
    {
        "Talk" => "Diálogo", "BattleTalk" or "_BattleTalk" => "Fala em batalha", "TalkSubtitle" => "Legenda",
        "JournalDetail" => "Detalhes da missão", "JournalAccept" => "Aceitar missão", "JournalResult" => "Conclusão da missão",
        "Journal" => "Diário de missões", "ToDoList" => "Objetivos", "ItemDetail" => "Descrição do item",
        "ActionDetail" => "Descrição da habilidade", "SelectYesno" => "Confirmação", "SelectString" => "Escolhas",
        "_MainCommand" => "Menu principal", _ => name,
    };
}
