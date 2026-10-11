namespace BrisaXIV.Core;

// The public Google endpoints limit by connection. One instance is shared by every translator of
// the plugin so all queues follow a single cadence and a temporary block pauses all of them.
public sealed class GoogleCadence
{
    // One request per second, like Echoglossian: faster bursts are what triggers Google's blocks.
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    // A block pauses briefly first and doubles while blocks repeat, up to the maximum.
    public static readonly TimeSpan FirstBlockPause = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan MaxBlockPause = TimeSpan.FromMinutes(5);
    private long next;
    private int consecutiveBlocks;

    // Milliseconds to wait before the next request; a yielded or canceled wait costs nothing.
    public long Wait => Interlocked.Read(ref next) - Environment.TickCount64;
    public void Sent() => Interlocked.Exchange(ref next, Environment.TickCount64 + (long)Interval.TotalMilliseconds);
    public void Succeeded() => Interlocked.Exchange(ref consecutiveBlocks, 0);

    // Pauses every queue and returns the pause, so the failed text is retried after it.
    public TimeSpan Blocked()
    {
        var blocks = Interlocked.Increment(ref consecutiveBlocks);
        var pause = TimeSpan.FromTicks(Math.Min(MaxBlockPause.Ticks, FirstBlockPause.Ticks << Math.Min(blocks - 1, 10)));
        Interlocked.Exchange(ref next, Environment.TickCount64 + (long)pause.TotalMilliseconds);
        return pause;
    }
}
