namespace DotaAnalyst.Infrastructure.OpenDota;

public class OpenDotaOptions
{
    public const string SectionName = "OpenDota";

    public required Uri BaseUrl { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    // Calls per minute for the whole server (see OpenDotaRateLimiter). OpenDota's free tier allows 60 per IP;
    // 50 leaves some margin.
    public int PermitsPerMinute { get; init; } = 50;

    // How long a call may wait for a free slot before being refused. Must stay below Timeout, which also
    // counts the wait.
    public TimeSpan MaxQueueWait { get; init; } = TimeSpan.FromSeconds(3);
}
