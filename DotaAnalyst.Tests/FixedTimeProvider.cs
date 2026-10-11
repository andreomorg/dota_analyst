namespace DotaAnalyst.Tests;

// A clock that always says the same time (and can be moved forward), so time-dependent code is testable.
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
