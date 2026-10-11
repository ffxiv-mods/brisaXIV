using System.Diagnostics;
using BrisaXIV.Core;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace BrisaXIV;

// Translates ahead of time what the player is about to read:
// the journal summaries, objectives and captions of accepted quests, the current job's action
// descriptions and the descriptions of items in the inventory and on the hotbars. Each text is
// evaluated as the game renders it, in the client language and in Japanese; the pair is registered
// in PrefetchedSources and the complete Japanese original is translated in the background.
// Runs on the framework thread, a few texts per frame, and only while the queue has room.
// Background work goes to a provider with a key (OpenAI, then DeepL); on the public Google endpoint
// it is paced slowly (Slow), leaving its rate to the text on screen.
internal sealed record PrefetchTarget(TranslationQueue Queue, bool Slow);

internal sealed unsafe class TranslationPrefetch(IDataManager data, GameTextEvaluator texts, PrefetchedSources sources,
    TranslationFile file, Func<PrefetchTarget?> target, Configuration config)
{
    private sealed record Work(string Display, string Japanese, string Scope);
    private sealed record Snapshot(uint[] Quests, uint Job, uint Level, uint[] Items);
    private const long CheckInterval = 3000;
    private const long SlowInterval = 5000;
    private string signature = "";
    private IEnumerator<Work>? pass;
    private Work? held;
    private long nextCheck;
    private long nextSlowRequest;

    public void Tick()
    {
        if (!config.Enabled || !config.Prefetch) return;
        if (Environment.TickCount64 >= nextCheck)
        {
            nextCheck = Environment.TickCount64 + CheckInterval;
            var snapshot = Capture();
            var current = string.Join(',', snapshot.Quests) + "|" + snapshot.Job + ":" + snapshot.Level + "|" + string.Join(',', snapshot.Items);
            if (current != signature) { signature = current; pass = Collect(snapshot).GetEnumerator(); held = null; }
        }
        if (pass == null && held == null) return;
        var started = Stopwatch.GetTimestamp();
        for (var n = 0; n < 24 && Stopwatch.GetElapsedTime(started).TotalMilliseconds < 2; n++)
        {
            var work = held;
            held = null;
            if (work == null)
            {
                if (pass == null || !pass.MoveNext()) { pass = null; return; }
                work = pass.Current;
            }
            sources.Add(work.Display, work.Japanese);
            if (file.TryGet(work.Japanese, out _) || file.TryGet(work.Display, out _)) continue;
            // Never flood the providers: wait for room and resume with this text on a later frame.
            // The public Google endpoint (no key) gets at most one background text every few seconds.
            var destination = target();
            if (destination == null || destination.Queue.PendingCount >= 8 || destination.Slow && Environment.TickCount64 < nextSlowRequest)
            { held = work; return; }
            destination.Queue.Get(work.Japanese, true);
            if (destination.Slow) nextSlowRequest = Environment.TickCount64 + SlowInterval;
        }
    }

    private Snapshot Capture()
    {
        var quests = new SortedSet<uint>();
        var manager = QuestManager.Instance();
        if (manager != null && (config.Quests || (config.Journal ?? config.Quests)))
        {
            // The sequence is part of the identity so a quest's next step is prefetched again.
            foreach (ref var quest in manager->NormalQuests)
                if (quest.QuestId != 0) quests.Add(quest.QuestId * 256u + quest.Sequence);
            foreach (ref var quest in manager->DailyQuests)
                if (quest.QuestId != 0) quests.Add(quest.QuestId * 256u);
        }
        var player = PlayerState.Instance();
        var job = player != null && config.Actions ? player->CurrentClassJobId : 0u;
        var level = job != 0 && data.GetExcelSheet<ClassJob>().TryGetRow(job, out var classJob) && classJob.ExpArrayIndex >= 0
            ? (uint)player->ClassJobLevels[classJob.ExpArrayIndex] : 0u;
        return new([.. quests], job, level, config.Items ? OwnedItems() : []);
    }

    private static uint[] OwnedItems()
    {
        var items = new SortedSet<uint>();
        var inventory = InventoryManager.Instance();
        if (inventory != null)
            foreach (var type in new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
                         InventoryType.EquippedItems, InventoryType.Crystals, InventoryType.KeyItems })
            {
                var container = inventory->GetInventoryContainer(type);
                if (container == null || !container->IsLoaded || container->Items == null) continue;
                for (var slot = 0; slot < container->Size; slot++)
                {
                    var item = container->GetInventorySlot(slot);
                    if (item != null && !item->IsEmpty() && item->GetBaseItemId() is var id and > 0 and < 2_000_000) items.Add(id);
                }
            }
        var hotbars = RaptureHotbarModule.Instance();
        if (hotbars != null && hotbars->ModuleReady)
            for (uint bar = 0; bar < 18; bar++)
                for (uint index = 0; index < 16; index++)
                {
                    var slot = hotbars->GetSlotById(bar, index);
                    if (slot == null || slot->IsEmpty || slot->ApparentSlotType is not (RaptureHotbarModule.HotbarSlotType.Item or RaptureHotbarModule.HotbarSlotType.InventoryItem)) continue;
                    var id = (slot->ApparentActionId != 0 ? slot->ApparentActionId : slot->CommandId) % 500_000;
                    if (id is > 0 and < 2_000_000) items.Add(id);
                }
        return [.. items];
    }

    // Quests first (what the player reads next), then the job's actions, then items.
    private IEnumerable<Work> Collect(Snapshot snapshot)
    {
        foreach (var quest in snapshot.Quests)
            foreach (var work in QuestTexts(quest / 256))
                yield return work;
        if (snapshot.Job != 0)
            foreach (var action in JobActions(snapshot.Job))
                if (texts.TryPair<ActionTransient>(action, x => x.Description, out var display, out var japanese))
                    yield return new(display, japanese, "Itens e ações");
        foreach (var item in snapshot.Items)
            if (texts.TryPair<Item>(item, x => x.Description, out var display, out var japanese))
                yield return new(display, japanese, "Itens e ações");
    }

    // Journal summaries (SEQ), objectives (TODO) and captions (SYSTEM) of one quest's text sheet.
    private IEnumerable<Work> QuestTexts(uint questId)
    {
        if (!data.GetExcelSheet<Quest>(texts.Client).TryGetRow(questId + 0x10000, out var quest)) yield break;
        var id = quest.Id.ExtractText();
        if (id.Length < 5) yield break;
        var name = $"quest/{id.Substring(id.Length - 5, 3)}/{id}";
        ExcelSheet<RawRow>? shown = null, japanese = null;
        try { shown = data.GetExcelSheet<RawRow>(texts.Client, name); japanese = data.GetExcelSheet<RawRow>(Dalamud.Game.ClientLanguage.Japanese, name); }
        catch (Exception) { }
        if (shown == null || japanese == null) yield break;
        foreach (var row in shown)
        {
            var key = row.ReadStringColumn(0).ExtractText();
            if (!key.Contains("_SEQ_", StringComparison.Ordinal) && !key.Contains("_TODO_", StringComparison.Ordinal)
                && !key.Contains("_SYSTEM_", StringComparison.Ordinal)) continue;
            if (japanese.TryGetRow(row.RowId, out var original)
                && texts.TryPair(row.ReadStringColumn(1), original.ReadStringColumn(1), out var display, out var source))
                yield return new(display, source, "Missões");
        }
    }

    // Player actions of the job, its base class and its role (class/job category).
    private IEnumerable<uint> JobActions(uint job)
    {
        var parent = data.GetExcelSheet<ClassJob>().TryGetRow(job, out var classJob) ? classJob.ClassJobParent.RowId : 0;
        var categories = data.GetExcelSheet<RawRow>(name: "ClassJobCategory");
        foreach (var action in data.GetExcelSheet<Lumina.Excel.Sheets.Action>())
        {
            if (action.RowId == 0 || !action.IsPlayerAction || action.IsPvP) continue;
            var owner = action.ClassJob.RowId;
            if (owner == job || (parent != 0 && owner == parent) || InCategory(categories, action.ClassJobCategory.RowId, job))
                yield return action.RowId;
        }
    }

    // ClassJobCategory: column 0 is the name, then one flag per ClassJob row.
    private static bool InCategory(ExcelSheet<RawRow> categories, uint category, uint job)
    {
        if (category == 0 || !categories.TryGetRow(category, out var row)) return false;
        try { return row.ReadBoolColumn((int)job + 1); }
        catch (Exception) { return false; }
    }
}
