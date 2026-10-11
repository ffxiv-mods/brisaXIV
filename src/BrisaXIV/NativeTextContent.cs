using Lumina.Text.ReadOnly;
using Lumina.Text.Payloads;
using System.Text;

namespace BrisaXIV;

internal static class NativeTextContent
{
    public static string Extract(ReadOnlySeStringSpan text) => Core.TextRules.Normalize(text.ExtractText());

    public static bool CanReplace(ReadOnlySeStringSpan text)
    {
        foreach (var payload in text)
            if (payload.Type != ReadOnlySePayloadType.Text && (payload.Type != ReadOnlySePayloadType.Macro
                || payload.MacroCode is not (MacroCode.NewLine or MacroCode.Color or MacroCode.EdgeColor
                    or MacroCode.ShadowColor or MacroCode.ColorType or MacroCode.EdgeColorType or MacroCode.Bold or MacroCode.Italic
                    or MacroCode.SoftHyphen or MacroCode.NonBreakingSpace or MacroCode.Hyphen))) return false;
        return true;
    }

    public static byte[] EncodePlain(string text) => Encoding.UTF8.GetBytes(text.Replace("\n", "\u0002\u0010\u0001\u0003") + "\0");

    public static bool TryReplace(ReadOnlySeStringSpan text, string translation, out byte[] replacement)
    {
        replacement = [];
        if (!CanReplace(text) || translation.IndexOfAny(['\0', '\u0002', '\u0003']) >= 0) return false;
        var content = EncodePlain(translation);
        if (content.Length > 24000) return false;
        using var buffer = new MemoryStream();
        var inserted = false;
        foreach (var payload in text)
        {
            if (payload.Type == ReadOnlySePayloadType.Text || payload.MacroCode is MacroCode.NewLine or MacroCode.SoftHyphen or MacroCode.NonBreakingSpace or MacroCode.Hyphen)
            {
                if (!inserted) { buffer.Write(content.AsSpan(0, content.Length - 1)); inserted = true; }
                continue;
            }
            // Keep style envelopes and their resets in order. Translation changes word order,
            // so inline emphasis cannot be aligned to individual translated words here.
            var envelope = new byte[payload.EnvelopeByteLength];
            payload.WriteEnvelopeTo(envelope);
            buffer.Write(envelope);
        }
        if (!inserted) return false;
        buffer.WriteByte(0);
        replacement = buffer.ToArray();
        return true;
    }
}
