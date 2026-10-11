using BrisaXIV.Core;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text.ReadOnly;

namespace BrisaXIV;

internal sealed unsafe class NativeTextLayout
{
    internal sealed record State(nint Address, uint Id, float X, float Y, ushort Width, ushort Height, TextFlags? Flags, byte? FontSize = null);
    internal sealed record Change(State Original, State Applied, bool UnitSize = false);
    internal sealed record Result(byte[] Bytes, string Rendered, Change[] Changes);

    private static State Snapshot(AtkResNode* node) => new((nint)node, node->NodeId, node->X, node->Y, node->Width, node->Height,
        node->Type == NodeType.Text ? ((AtkTextNode*)node)->TextFlags : null, node->Type == NodeType.Text ? ((AtkTextNode*)node)->FontSize : null);

    public static AtkResNode* Background(AtkUldManager* manager, AtkTextNode* text, string addonName)
    {
        // Talk uses a container of image layers, unlike MiniTalk's nine-grid balloon.
        if (addonName == "Talk" && text->NodeId == 3)
            for (var i = 0; i < manager->NodeListCount; i++)
            {
                var node = manager->NodeList[i];
                if (node != null && node->NodeId == 10 && node->Type == NodeType.Res
                    && node->Width >= 200 && node->Height >= 50 && node->ParentNode == text->ParentNode) return node;
            }
        AtkResNode* best = null;
        for (var i = 0; i < manager->NodeListCount; i++)
        {
            var node = manager->NodeList[i];
            if (node == null || node->Type != NodeType.NineGrid || node->ParentNode != text->ParentNode
                || !node->NodeFlags.HasFlag(NodeFlags.Visible) || node->Width < 60 || node->Height < 20
                || node->X > text->X || node->Y > text->Y || node->X + node->Width <= text->X + 50
                || node->Y + node->Height <= text->Y) continue;
            if (best == null || node->Width * node->Height < best->Width * best->Height) best = node;
        }
        return best;
    }

    public static bool TryPrepare(AtkUnitBase* addon, AtkUldManager* manager, AtkTextNode* text, ReadOnlySeStringSpan original, string translation, bool dialogue, string addonName, out Result result)
    {
        result = null!;
        if (!NativeTextContent.TryReplace(original, translation, out var plain)) return false;
        // JournalDetail's node 40 is a clipped scrolling viewport (verified in
        // its ULD). Its content is laid out by the game, not a dialogue balloon.
        // Preserve its dimensions and let native word wrapping render the text.
        if (IsJournalScrollText(addonName, text))
        {
            var beforeJournal = Snapshot((AtkResNode*)text);
            text->TextFlags = (text->TextFlags | TextFlags.MultiLine | TextFlags.WordWrap) & ~TextFlags.Ellipsis;
            text->IsDirty = true;
            result = new(plain, Rendered(plain), [new(beforeJournal, Snapshot((AtkResNode*)text))]);
            return true;
        }
        if (ConfirmationPrompt(addon, manager, text, addonName))
            return TryPrepareConfirmation(addon, manager, text, original, translation, out result);
        if (addonName is "ActionDetail" or "ItemDetail") return TryPrepareTooltip(addon, text, original, translation, out result);
        if ((!dialogue && !text->TextFlags.HasFlag(TextFlags.MultiLine)) || text->Width < 60)
        { result = FitSingleLine(text, plain); return true; }

        var before = Snapshot((AtkResNode*)text);
        var background = dialogue ? Background(manager, text, addonName) : null;
        var width = (int)text->Width;
        if (background != null)
        {
            var leftInset = text->X - background->X;
            width = Math.Min(width, (int)(background->X + background->Width - text->X - Math.Clamp(leftInset, 8, 36)));
        }
        if (width < 60) { result = new(plain, Rendered(plain), []); return true; }
        var flags = text->TextFlags;
        string wrapped;
        ushort measuredWidth = 0, measuredHeight = 0;
        try
        {
            // Measure explicit lines; automatic wrapping would hide an overlong candidate's width.
            text->TextFlags = (flags | TextFlags.MultiLine) & ~(TextFlags.WordWrap | TextFlags.Ellipsis | TextFlags.AutoAdjustNodeSize);
            var textAddress = (nint)text;
            wrapped = TextBoxLayout.Wrap(translation, width - 2, value =>
            {
                var bytes = NativeTextContent.EncodePlain(value);
                ushort w = 0, h = 0;
                fixed (byte* pointer = bytes) ((AtkTextNode*)textAddress)->GetTextDrawSize(&w, &h, pointer);
                return w;
            });
            if (!NativeTextContent.TryReplace(original, wrapped, out plain)) return false;
            fixed (byte* pointer = plain) text->GetTextDrawSize(&measuredWidth, &measuredHeight, pointer);
        }
        finally { text->TextFlags = flags; }
        var required = Math.Max((int)measuredHeight + 2, text->FontSize + 2);
        var available = (int)text->Height;
        if (background != null)
            available = (int)(background->Y + background->Height - text->Y - 16);
        var extra = TextBoxLayout.ExtraHeight(required, available);
        // Unknown containers retain their layout and use the reading panel instead of spilling.
        if (required > 1200 || (extra > 0 && (background == null || extra > 500))) return false;
        var changes = new List<Change>();
        if (extra > 0 && background != null)
        {
            var originalBackground = Snapshot(background);
            background->SetHeight((ushort)(background->Height + extra));
            background->SetYFloat(background->Y - extra); // Grow upwards, keeping the tail and bottom in place.
            changes.Add(new(originalBackground, Snapshot(background)));
            if (background->Type == NodeType.Res)
                for (var i = 0; i < manager->NodeListCount; i++)
                {
                    var layer = manager->NodeList[i];
                    if (layer == null || layer->ParentNode != background || layer->Type != NodeType.Image
                        || layer->Height != originalBackground.Height) continue;
                    var image = Snapshot(layer); layer->SetHeight((ushort)(layer->Height + extra));
                    changes.Add(new(image, Snapshot(layer)));
                }
            var oldTextY = text->Y;
            text->SetYFloat(text->Y - extra);
            // Keep the speaker heading attached to the upper edge of the Talk box.
            for (var i = 0; i < manager->NodeListCount; i++)
            {
                var node = manager->NodeList[i];
                if (node == null || node == (AtkResNode*)text || node->Type != NodeType.Text || node->ParentNode != text->ParentNode
                    || node->Y >= oldTextY || node->Y < originalBackground.Y - 40) continue;
                var heading = Snapshot(node); node->SetYFloat(node->Y - extra);
                changes.Add(new(heading, Snapshot(node)));
            }
        }
        text->SetWidth((ushort)width); text->SetHeight((ushort)required);
        text->TextFlags = (flags | TextFlags.MultiLine) & ~(TextFlags.WordWrap | TextFlags.Ellipsis | TextFlags.AutoAdjustNodeSize);
        text->IsDirty = true;
        changes.Add(new(before, Snapshot((AtkResNode*)text)));
        result = new(plain, NativeTextContent.Extract(new ReadOnlySeStringSpan(plain.AsSpan(0, plain.Length - 1))), changes.ToArray());
        return true;
    }

    internal static bool IsJournalScrollText(string addonName, AtkTextNode* text)
    {
        if (addonName != "JournalDetail" || text == null) return false;
        var node = text->ParentNode;
        for (var depth = 0; node != null && depth < 32; depth++, node = node->ParentNode)
            if (node->NodeId == 40 && node->Type == NodeType.Res) return true;
        return false;
    }

    private static bool ConfirmationPrompt(AtkUnitBase* addon, AtkUldManager* manager, AtkTextNode* text, string name)
    {
        if (addon == null || manager != &addon->UldManager) return false;
        return name == "SelectYesno" ? ((AddonSelectYesno*)addon)->PromptText == text
            : name == "SelectOk" && text->NodeId == 2;
    }

    private static bool TryPrepareConfirmation(AtkUnitBase* addon, AtkUldManager* manager, AtkTextNode* text,
        ReadOnlySeStringSpan original, string translation, out Result result)
    {
        result = null!;
        var nodes = LiveNodes(manager);
        if (nodes == null || addon->RootNode == null || addon->WindowNode == null
            || !nodes.Contains((nint)addon->RootNode) || !nodes.Contains((nint)addon->WindowNode)
            || text->ParentNode != addon->RootNode || addon->WindowNode->Component == null
            || addon->WindowNode->Component->GetComponentType() != ComponentType.Window) return false;
        var root = addon->RootNode;
        var width = Math.Min((int)text->Width, (int)(root->Width - text->X - Math.Clamp(text->X, 12, 36)));
        if (width < 60 || root->Width > 2000) return false;
        var footer = new List<State>();
        foreach (var address in nodes)
        {
            var node = (AtkResNode*)address;
            if (node->ParentNode == text->ParentNode && node != (AtkResNode*)text && node != (AtkResNode*)addon->WindowNode
                && node->Y > text->Y && node->Width > 0 && node->Height > 0 && Visible(node)) footer.Add(Snapshot(node));
        }
        if (footer.Count == 0) return false;
        var flags = text->TextFlags;
        string wrapped;
        byte[] bytes;
        ushort measuredWidth = 0, measuredHeight = 0;
        try
        {
            text->TextFlags = (flags | TextFlags.MultiLine) & ~(TextFlags.WordWrap | TextFlags.Ellipsis | TextFlags.AutoAdjustNodeSize);
            var textAddress = (nint)text;
            wrapped = TextBoxLayout.Wrap(translation, width - 2, value =>
            {
                var candidate = NativeTextContent.EncodePlain(value); ushort w = 0, h = 0;
                fixed (byte* pointer = candidate) ((AtkTextNode*)textAddress)->GetTextDrawSize(&w, &h, pointer);
                return w;
            });
            if (!NativeTextContent.TryReplace(original, wrapped, out bytes)) return false;
            fixed (byte* pointer = bytes) text->GetTextDrawSize(&measuredWidth, &measuredHeight, pointer);
        }
        finally { text->TextFlags = flags; }
        var required = Math.Max((int)measuredHeight + 2, text->FontSize + 2);
        if (!TextBoxLayout.TryFitConfirmation(text->Y, required, footer.Min(n => n.Y), root->Height, out var fit)) return false;
        var before = nodes.Select(address => Snapshot((AtkResNode*)address)).ToArray();
        if (fit.ExtraHeight > 0)
        {
            // The game's resize routine updates the window skin and collision bounds together.
            addon->SetSize(root->Width, (ushort)fit.WindowHeight);
            nodes = LiveNodes(manager);
            if (nodes == null) return false;
            foreach (var item in footer)
                if (nodes.Contains(item.Address) && ((AtkResNode*)item.Address)->NodeId == item.Id)
                    ((AtkResNode*)item.Address)->SetYFloat(item.Y + fit.ExtraHeight);
        }
        text->SetWidth((ushort)width); text->SetHeight((ushort)required);
        text->TextFlags = (flags | TextFlags.MultiLine) & ~(TextFlags.WordWrap | TextFlags.Ellipsis | TextFlags.AutoAdjustNodeSize);
        text->IsDirty = true;
        var changes = new List<Change>();
        foreach (var item in before)
        {
            if (!nodes.Contains(item.Address) || ((AtkResNode*)item.Address)->NodeId != item.Id) continue;
            var after = Snapshot((AtkResNode*)item.Address);
            if (item != after) changes.Add(new(item, after, fit.ExtraHeight > 0 && item.Address == (nint)root));
        }
        result = new(bytes, NativeTextContent.Extract(new ReadOnlySeStringSpan(bytes.AsSpan(0, bytes.Length - 1))), changes.ToArray());
        return true;
    }

    // Tooltips are sized by the game for the English text before PreDraw. The translation is
    // wrapped to the space inside the window; when it is taller, the window, its background and
    // everything below the text grow or move down by the same amount. Restore undoes all of it.
    private static bool TryPrepareTooltip(AtkUnitBase* addon, AtkTextNode* text, ReadOnlySeStringSpan original, string translation, out Result result)
    {
        result = null!;
        var manager = &addon->UldManager;
        var nodes = LiveNodes(manager);
        var root = addon->RootNode;
        if (nodes == null || root == null || !nodes.Contains((nint)root) || root->Width > 2000 || root->ScaleX <= 0) return false;
        var textNode = (AtkResNode*)text;
        var insideX = (text->ScreenX - root->ScreenX) / root->ScaleX;
        var available = (int)(root->Width - insideX - 14);
        var description = text->TextFlags.HasFlag(TextFlags.MultiLine) || text->TextFlags.HasFlag(TextFlags.WordWrap) || translation.Length >= 40;
        var flags = text->TextFlags;
        // A short label (cast, recast, cost) that fits before the window's right edge keeps its slot.
        if (!description && !translation.Contains('\n') && NativeTextContent.TryReplace(original, translation, out var label))
        {
            ushort labelWidth = 0, labelHeight = 0;
            fixed (byte* pointer = label) text->GetTextDrawSize(&labelWidth, &labelHeight, pointer);
            if (labelWidth <= available) { result = new(label, NativeTextContent.Extract(new ReadOnlySeStringSpan(label.AsSpan(0, label.Length - 1))), []); return true; }
        }
        // Descriptions wrap in their own width; an overlong label wraps in the space left in the window.
        var width = description ? Math.Min(Math.Max((int)text->Width, 60), available) : available;
        if (width < 60) return false;
        byte[] bytes;
        ushort measuredWidth = 0, measuredHeight = 0;
        try
        {
            text->TextFlags = (flags | TextFlags.MultiLine) & ~(TextFlags.WordWrap | TextFlags.Ellipsis | TextFlags.AutoAdjustNodeSize);
            var textAddress = (nint)text;
            var wrapped = TextBoxLayout.Wrap(translation, width - 2, value =>
            {
                var candidate = NativeTextContent.EncodePlain(value); ushort w = 0, h = 0;
                fixed (byte* pointer = candidate) ((AtkTextNode*)textAddress)->GetTextDrawSize(&w, &h, pointer);
                return w;
            });
            if (!NativeTextContent.TryReplace(original, wrapped, out bytes)) return false;
            fixed (byte* pointer = bytes) text->GetTextDrawSize(&measuredWidth, &measuredHeight, pointer);
        }
        finally { text->TextFlags = flags; }
        var required = Math.Max((int)measuredHeight + 2, text->FontSize + 2);
        var extra = TextBoxLayout.ExtraHeight(required, text->Height);
        if (required > 1200 || extra > 600) return false;
        var before = nodes.Select(address => Snapshot((AtkResNode*)address)).ToArray();
        if (extra > 0)
        {
            var scale = root->ScaleY > 0 ? root->ScaleY : 1;
            var textTop = text->ScreenY;
            var textBottom = text->ScreenY + text->Height * scale;
            // The game's resize routine keeps the unit's collision bounds in step with the root.
            addon->SetSize(root->Width, (ushort)Math.Min(ushort.MaxValue, root->Height + extra));
            foreach (var address in nodes)
            {
                var node = (AtkResNode*)address;
                if (node == textNode || node == root || node->Height == 0) continue;
                // Containers and background frames around the description grow with it;
                // rows that start below it (e.g. notes, costs) move down.
                if (node->ScreenY <= textTop + 1 && node->ScreenY + node->Height * scale >= textBottom - 1) node->SetHeight((ushort)(node->Height + extra));
                else if (node->ScreenY >= textBottom - 1 && IsTopLevelBelow(node, textBottom)) node->SetYFloat(node->Y + extra);
            }
            // A taller tooltip near the bottom of the screen moves up instead of being cut off.
            var device = Device.Instance();
            var bottom = addon->Y + root->Height * addon->Scale;
            if (device != null && device->Height > 0 && bottom > device->Height - 8)
                addon->SetPosition(addon->X, (short)Math.Max(0, addon->Y - (bottom - (device->Height - 8))));
        }
        text->SetWidth((ushort)width); text->SetHeight((ushort)required);
        text->TextFlags = (flags | TextFlags.MultiLine) & ~(TextFlags.WordWrap | TextFlags.Ellipsis | TextFlags.AutoAdjustNodeSize);
        text->IsDirty = true;
        var changes = new List<Change>();
        foreach (var item in before)
        {
            if (!nodes.Contains(item.Address) || ((AtkResNode*)item.Address)->NodeId != item.Id) continue;
            var after = Snapshot((AtkResNode*)item.Address);
            if (item != after) changes.Add(new(item, after, extra > 0 && item.Address == (nint)root));
        }
        result = new(bytes, NativeTextContent.Extract(new ReadOnlySeStringSpan(bytes.AsSpan(0, bytes.Length - 1))), changes.ToArray());
        return true;
    }

    // A fixed-width single-line cell (e.g. a shop column) cannot grow: a longer translation would
    // spill over the neighboring column. Shrink the font down to 75%, then cut with an ellipsis.
    private static Result FitSingleLine(AtkTextNode* text, byte[] plain)
    {
        var rendered = Rendered(plain);
        var flags = text->TextFlags;
        var size = text->FontSize;
        if (flags.HasFlag(TextFlags.MultiLine) || flags.HasFlag(TextFlags.AutoAdjustNodeSize) || text->Width < 16 || size == 0) return new(plain, rendered, []);
        var before = Snapshot((AtkResNode*)text);
        var minimum = (byte)Math.Max(9, size * 3 / 4);
        ushort width = 0, height = 0;
        for (var candidate = size; ; candidate--)
        {
            text->FontSize = candidate;
            fixed (byte* pointer = plain) text->GetTextDrawSize(&width, &height, pointer);
            if (width <= text->Width || candidate <= minimum) break;
        }
        if (text->FontSize == size) return new(plain, rendered, []);
        if (width > text->Width) text->TextFlags = flags | TextFlags.Ellipsis;
        text->IsDirty = true;
        return new(plain, rendered, [new(before, Snapshot((AtkResNode*)text))]);
    }

    private static string Rendered(byte[] bytes) => NativeTextContent.Extract(new ReadOnlySeStringSpan(bytes.AsSpan(0, bytes.Length - 1)));

    // Moving a parent already moves its children; only the outermost node below the text moves.
    private static bool IsTopLevelBelow(AtkResNode* node, float textBottom)
        => node->ParentNode == null || node->ParentNode->ScreenY < textBottom - 1;

    private static bool Visible(AtkResNode* node)
    {
        for (var i = 0; node != null && i < 64; i++, node = node->ParentNode)
            if (!node->NodeFlags.HasFlag(NodeFlags.Visible) || node->IsDrawDisabled) return false;
        return node == null;
    }

    // Window skin/collision nodes live in a component, outside the addon's flat list.
    private static HashSet<nint>? LiveNodes(AtkUldManager* manager)
    {
        var nodes = new HashSet<nint>(); var budget = 2048;
        return CollectNodes(manager, nodes, 0, ref budget) ? nodes : null;
    }
    private static bool CollectNodes(AtkUldManager* manager, HashSet<nint> nodes, int depth, ref int budget)
    {
        if (manager == null || manager->NodeListCount > 4096 || depth > 12) return false;
        if (manager->NodeListCount == 0) return true;
        if (manager->NodeList == null) return false;
        for (var i = 0; i < manager->NodeListCount; i++)
        {
            var node = manager->NodeList[i];
            if (node == null || !nodes.Add((nint)node)) continue;
            if (--budget < 0) return false;
            if ((ushort)node->Type < 1000) continue;
            var component = ((AtkComponentNode*)node)->Component;
            if (component != null && !CollectNodes(&component->UldManager, nodes, depth + 1, ref budget)) return false;
        }
        return true;
    }

    public static bool IsCurrent(AtkUldManager* manager, Change[] changes)
    {
        if (changes.Length == 0) return true;
        var nodes = LiveNodes(manager);
        if (nodes == null) return false;
        foreach (var change in changes)
        {
            if (!nodes.Contains(change.Original.Address)) return false;
            var node = (AtkResNode*)change.Original.Address;
            if (node->NodeId != change.Original.Id) return false;
            if ((change.Original.X != change.Applied.X && node->X != change.Applied.X)
                || (change.Original.Y != change.Applied.Y && node->Y != change.Applied.Y)
                || (change.Original.Width != change.Applied.Width && node->Width != change.Applied.Width)
                || (change.Original.Height != change.Applied.Height && node->Height != change.Applied.Height)
                || (node->Type == NodeType.Text && change.Original.Flags != change.Applied.Flags
                    && ((AtkTextNode*)node)->TextFlags != change.Applied.Flags)
                || (node->Type == NodeType.Text && change.Original.FontSize != change.Applied.FontSize
                    && ((AtkTextNode*)node)->FontSize != change.Applied.FontSize)) return false;
        }
        return true;
    }

    public static void Restore(AtkUnitBase* addon, AtkUldManager* manager, Change[] changes)
    {
        // Stored addresses are never dereferenced: find them again in this live node list.
        if (changes.Length == 0) return;
        var nodes = LiveNodes(manager);
        if (nodes == null) return;
        foreach (var change in changes.Where(c => c.UnitSize))
        {
            if (addon == null || !nodes.Contains(change.Original.Address) || addon->RootNode != (AtkResNode*)change.Original.Address) continue;
            var root = addon->RootNode;
            if (root->NodeId == change.Original.Id && root->Width == change.Applied.Width && root->Height == change.Applied.Height)
                addon->SetSize(change.Original.Width, change.Original.Height);
        }
        nodes = LiveNodes(manager);
        if (nodes == null) return;
        foreach (var change in changes)
        {
            if (!nodes.Contains(change.Original.Address)) continue;
            var node = (AtkResNode*)change.Original.Address;
            if (node->NodeId != change.Original.Id) continue;
            if (node->X == change.Applied.X) node->SetXFloat(change.Original.X);
            if (node->Y == change.Applied.Y) node->SetYFloat(change.Original.Y);
            if (node->Width == change.Applied.Width) node->SetWidth(change.Original.Width);
            if (node->Height == change.Applied.Height) node->SetHeight(change.Original.Height);
            if (node->Type == NodeType.Text && change.Original.Flags is { } originalFlags
                && ((AtkTextNode*)node)->TextFlags == change.Applied.Flags) ((AtkTextNode*)node)->TextFlags = originalFlags;
            if (node->Type == NodeType.Text && change.Original.FontSize is { } originalSize
                && ((AtkTextNode*)node)->FontSize == change.Applied.FontSize) ((AtkTextNode*)node)->FontSize = originalSize;
            node->IsDirty = true;
        }
    }
}
