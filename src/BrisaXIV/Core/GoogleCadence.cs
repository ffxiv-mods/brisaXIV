namespace BrisaXIV.Core;

// The public Google endpoint limits by connection. One instance is shared by every translator of
// the plugin so all queues follow a single cadence and a temporary block pauses all of them.
public sealed class GoogleCadence
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(1200);
    public static readonly TimeSpan BlockPause = TimeSpan.FromMinutes(5);
    private long next;

    // Milliseconds to wait before the next request; a yielded or canceled wait costs nothing.
    public long Wait => Interlocked.Read(ref next) - Environment.TickCount64;
    public void Sent() => Interlocked.Exchange(ref next, Environment.TickCount64 + (long)Interval.TotalMilliseconds);
    public void Blocked() => Interlocked.Exchange(ref next, Environment.TickCount64 + (long)BlockPause.TotalMilliseconds);
}
