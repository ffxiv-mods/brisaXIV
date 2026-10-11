namespace BrisaXIV.Core;

public sealed class DialoguePresentation
{
    private long? started;
    private string source = "";
    public void ObserveSource(string value) { if (source != value) { source = value; started = null; } }
    public bool ShouldWait(long now, bool preparing, int maximumSeconds)
    {
        if (!preparing) { started = null; return false; }
        started ??= now;
        return now - started.Value < Math.Clamp(maximumSeconds, 1, 30) * 1000L;
    }
    public void Reset() { started = null; source = ""; }
    public static bool IsSpeakerName(string addon, uint nodeId) => (addon == "Talk" && nodeId == 2)
        || (addon is "_BattleTalk" or "BattleTalk" && nodeId == 4);
}
