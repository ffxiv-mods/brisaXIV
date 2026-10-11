using System.Reflection;
using System.Runtime.InteropServices;
using BrisaXIV;
using FFXIVClientStructs.FFXIV.Component.GUI;

// Native setters are replaced only in this isolated test executable. No game process is accessed.
internal static unsafe class NativeTextLayoutTests
{
    public static void Run(Action<bool, string> check)
    {
        Bind(typeof(AtkResNode.Addresses), "SetXFloat", (nint)(delegate* unmanaged<AtkResNode*, float, void>)&SetX);
        Bind(typeof(AtkResNode.Addresses), "SetYFloat", (nint)(delegate* unmanaged<AtkResNode*, float, void>)&SetY);
        Bind(typeof(AtkResNode.Addresses), "SetWidth", (nint)(delegate* unmanaged<AtkResNode*, ushort, void>)&SetWidth);
        Bind(typeof(AtkResNode.Addresses), "SetHeight", (nint)(delegate* unmanaged<AtkResNode*, ushort, void>)&SetHeight);
        Bind(typeof(AtkUnitBase.Addresses), "SetSize", (nint)(delegate* unmanaged<AtkUnitBase*, ushort, ushort, void>)&SetSize);
        AtkResNode journalViewport = new() { NodeId = 40, Type = NodeType.Res, Width = 480, Height = 438 };
        AtkResNode journalContent = new() { NodeId = 44, Type = NodeType.Res, ParentNode = &journalViewport };
        AtkTextNode journalDescription = new() { NodeId = 46, Type = NodeType.Text, ParentNode = &journalContent,
            Width = 376, Height = 300, TextFlags = TextFlags.MultiLine | TextFlags.Ellipsis };
        var journalBytes = NativeTextContent.EncodePlain("Original description");
        check(NativeTextLayout.IsJournalScrollText("JournalDetail", &journalDescription)
            && !NativeTextLayout.IsJournalScrollText("Talk", &journalDescription),
            "Journal scrolling content is identified by its verified viewport without changing dialogue boxes");
        check(NativeTextLayout.TryPrepare(null, null, &journalDescription,
                new Lumina.Text.ReadOnly.ReadOnlySeStringSpan(journalBytes.AsSpan(0, journalBytes.Length - 1)),
                new string('a', 2000), false, "JournalDetail", out var journalLayout)
            && journalDescription.Width == 376 && journalDescription.Height == 300
            && journalViewport.Height == 438 && journalDescription.TextFlags.HasFlag(TextFlags.WordWrap)
            && !journalDescription.TextFlags.HasFlag(TextFlags.Ellipsis),
            "Long translated Journal descriptions use native wrapping without resizing the scrolling viewport or touching footer buttons");
        AtkResNode** journalNodes = stackalloc AtkResNode*[1]; journalNodes[0] = (AtkResNode*)&journalDescription;
        AtkUldManager journalManager = new() { NodeList = journalNodes, NodeListCount = 1 };
        NativeTextLayout.Restore(null, &journalManager, journalLayout.Changes);
        check(journalDescription.TextFlags == (TextFlags.MultiLine | TextFlags.Ellipsis)
            && journalDescription.Height == 300, "Disabling Journal translation restores its original native wrapping flags and dimensions");
        AtkResNode root = new() { NodeId = 1, Type = NodeType.Res, Width = 400, Height = 150 };
        AtkResNode button = new() { NodeId = 3, Type = NodeType.Res, Y = 108, Width = 100, Height = 28 };
        AtkResNode skin = new() { NodeId = 9, Type = NodeType.NineGrid, Width = 400, Height = 150 };
        AtkResNode collision = new() { NodeId = 12, Type = NodeType.Collision, Width = 400, Height = 150 };
        AtkTextNode prompt = new() { NodeId = 2, Type = NodeType.Text, Width = 344, Height = 72, TextFlags = TextFlags.MultiLine };
        AtkComponentBase windowComponent = new();
        AtkComponentNode window = new() { NodeId = 4, Type = (NodeType)1005, Component = &windowComponent, Width = 400, Height = 150 };
        AtkResNode** inner = stackalloc AtkResNode*[2]; inner[0] = &skin; inner[1] = &collision;
        windowComponent.UldManager.NodeList = inner; windowComponent.UldManager.NodeListCount = 2;
        AtkResNode** outer = stackalloc AtkResNode*[4]; outer[0] = &root; outer[1] = (AtkResNode*)&prompt; outer[2] = &button; outer[3] = (AtkResNode*)&window;
        AtkUnitBase addon = new() { RootNode = &root, WindowNode = &window };
        addon.UldManager.NodeList = outer; addon.UldManager.NodeListCount = 4;
        NativeTextLayout.State State(AtkResNode* n) => new((nint)n, n->NodeId, n->X, n->Y, n->Width, n->Height,
            n->Type == NodeType.Text ? ((AtkTextNode*)n)->TextFlags : null);
        var rootApplied = State(&root); var promptApplied = State((AtkResNode*)&prompt); var buttonApplied = State(&button);
        var skinApplied = State(&skin); var collisionApplied = State(&collision); var windowApplied = State((AtkResNode*)&window);
        NativeTextLayout.Change[] changes =
        [
            new(rootApplied with { Height = 112 }, rootApplied, true), new(promptApplied with { Height = 34, Flags = TextFlags.WordWrap }, promptApplied),
            new(buttonApplied with { Y = 70 }, buttonApplied), new(windowApplied with { Height = 112 }, windowApplied),
            new(skinApplied with { Height = 112 }, skinApplied), new(collisionApplied with { Height = 112 }, collisionApplied),
        ];
        check(NativeTextLayout.IsCurrent(&addon.UldManager, changes), "Native layout checks find changed background/collision nodes inside a window component");
        skin.Height = 149;
        check(!NativeTextLayout.IsCurrent(&addon.UldManager, changes), "A game-side reset of the inner background invalidates the saved layout before reuse");
        skin.Height = 150;
        NativeTextLayout.Restore(&addon, &addon.UldManager, changes);
        check(root.Height == 112 && window.Height == 112 && skin.Height == 112 && collision.Height == 112
            && button.Y == 70 && prompt.Height == 34 && prompt.TextFlags == TextFlags.WordWrap,
            "Restoring a confirmation resets its size, skin, click area, button position and original text flags together");
        button.Y = 95; root.Height = 160; window.Height = 160; skin.Height = 160; collision.Height = 160;
        NativeTextLayout.Restore(&addon, &addon.UldManager, changes);
        check(root.Height == 160 && button.Y == 95 && skin.Height == 160,
            "Restoration preserves a newer game layout instead of overwriting changed dimensions or button positions");
        button.NodeId = 999;
        check(!NativeTextLayout.IsCurrent(&addon.UldManager, [changes[2]]), "Reused node addresses with a different identity are rejected");
        addon.UldManager.NodeListCount = 0;
        NativeTextLayout.Restore(&addon, &addon.UldManager, changes);
        check(!NativeTextLayout.IsCurrent(&addon.UldManager, changes) && button.Y == 95,
            "Removed nodes are not dereferenced or modified by stale layout state");
    }
    private static void Bind(Type type, string name, nint pointer)
    {
        var address = type.GetField(name, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var value = address.GetType().GetField("Value");
        if (value != null) value.SetValue(address, pointer);
        else address.GetType().GetProperty("Value")!.SetValue(address, pointer);
    }
    [UnmanagedCallersOnly] private static void SetX(AtkResNode* node, float value) => node->X = value;
    [UnmanagedCallersOnly] private static void SetY(AtkResNode* node, float value) => node->Y = value;
    [UnmanagedCallersOnly] private static void SetWidth(AtkResNode* node, ushort value) => node->Width = value;
    [UnmanagedCallersOnly] private static void SetHeight(AtkResNode* node, ushort value) => node->Height = value;
    [UnmanagedCallersOnly] private static void SetSize(AtkUnitBase* addon, ushort width, ushort height)
    {
        var delta = height - addon->RootNode->Height;
        addon->RootNode->Width = width; addon->RootNode->Height = height;
        addon->WindowNode->Width = width; addon->WindowNode->Height = height;
        // Simulate the game's bottom anchoring as well as background/collision resizing.
        addon->UldManager.NodeList[2]->Y += delta;
        var manager = &addon->WindowNode->Component->UldManager;
        for (var i = 0; i < manager->NodeListCount; i++) { manager->NodeList[i]->Width = width; manager->NodeList[i]->Height = height; }
    }
}
