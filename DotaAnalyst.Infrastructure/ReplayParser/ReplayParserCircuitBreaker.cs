namespace DotaAnalyst.Infrastructure.ReplayParser;

// "Circuit breaker" of the replay parser (one for the whole app: Singleton), like StorageGuard is for the
// database. When the parser is unavailable (machine off, tunnel down, busy, wrong key), replay parsing is treated
// as off for a while: the queue answers Unavailable (the analysis says replayStatus "unavailable" without
// queueing) and the worker drops what was waiting. It closes by itself when the time is up; the next visit
// queues the match again and, if the parser is still down, it opens again.
public sealed class ReplayParserCircuitBreaker(TimeProvider timeProvider)
{
    public static readonly TimeSpan OpenFor = TimeSpan.FromMinutes(5);

    // UTC ticks until which the parser is skipped. Read/written with Interlocked: requests and the worker share it.
    private long openUntilTicks;

    public bool IsOpen => timeProvider.GetUtcNow().UtcTicks < Interlocked.Read(ref openUntilTicks);

    public void Open() => Interlocked.Exchange(ref openUntilTicks, (timeProvider.GetUtcNow() + OpenFor).UtcTicks);
}
