using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Infrastructure.OpenDota;

// OpenDota's free quota is per IP, so it belongs to our server: every visitor shares it. This one instance
// (Singleton) counts the calls of the whole app and decides if one more can go out (see OpenDotaRateLimitHandler).
// - Sliding window: at most PermitsPerMinute calls in any 60 s, kept as the times of the recent calls.
// - A call that finds the window full can reserve the next free slot if it opens within MaxQueueWait, and waits
//   for it; otherwise it is refused at once.
// - After OpenDota itself answers 429, every call is refused until the pause is over.
// The clock is the TimeProvider, so tests control time instead of waiting for it.
public sealed class OpenDotaRateLimiter(
    IOptions<OpenDotaOptions> options,
    TimeProvider timeProvider,
    ILogger<OpenDotaRateLimiter> logger)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan DefaultPause = TimeSpan.FromMinutes(1);

    // A wrong Retry-After (e.g. a week, or a broken date from a proxy) must not switch OpenDota off until a restart.
    public static readonly TimeSpan MaxPause = TimeSpan.FromMinutes(15);

    // Only a warning in the log: the daily quota is OpenDota's to enforce.
    public const int LowDailyQuota = 300;
    public static readonly TimeSpan LowDailyQuotaWarningInterval = TimeSpan.FromMinutes(15);

    private readonly Lock gate = new();

    // UTC ticks of the calls in the current window, oldest first (may include reserved slots a bit in the future).
    private readonly List<long> calls = [];
    private long pausedUntilTicks;
    private long lastLowQuotaWarningTicks;

    // How long the caller must wait before its call goes out (usually zero), or null when it is refused.
    public TimeSpan? TryReserve(int cost = 1)
    {
        var now = timeProvider.GetUtcNow().UtcTicks;
        var permits = options.Value.PermitsPerMinute;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cost);
        if (cost > permits) return null;

        lock (gate)
        {
            if (now < pausedUntilTicks)
            {
                return null;
            }

            // Calls that left the window no longer count.
            var expired = calls.FindIndex(ticks => ticks > now - Window.Ticks);
            calls.RemoveRange(0, expired < 0 ? calls.Count : expired);

            // The new call must be at least one window after the call "permits" places before it.
            // All units must fit at the same time; reserve atomically without a partial charge.
            var requiredExpiry = calls.Count + cost - permits;
            var slot = requiredExpiry <= 0 ? now : Math.Max(now, calls[requiredExpiry - 1] + Window.Ticks);
            if (calls.Count > 0) slot = Math.Max(slot, calls[^1]);
            var wait = TimeSpan.FromTicks(slot - now);
            if (wait > options.Value.MaxQueueWait)
            {
                return null;
            }

            for (var i = 0; i < cost; i++) calls.Add(slot);
            return wait;
        }
    }

    // True while calls are paused after a 429 from OpenDota. Checked again by calls that waited for a slot.
    public bool IsPaused =>
        timeProvider.GetUtcNow().UtcTicks < Interlocked.Read(ref pausedUntilTicks);

    // OpenDota answered 429: stop calling it for retryAfter (what OpenDota asked, at most MaxPause) or, when it
    // asked for nothing usable (missing, zero, negative or a date already past), DefaultPause.
    public void Pause(TimeSpan? retryAfter)
    {
        var pause = retryAfter is { } asked && asked > TimeSpan.Zero
            ? TimeSpan.FromTicks(Math.Min(asked.Ticks, MaxPause.Ticks))
            : DefaultPause;
        var until = timeProvider.GetUtcNow() + pause;

        lock (gate)
        {
            if (until.UtcTicks <= pausedUntilTicks)
            {
                return;
            }

            Interlocked.Exchange(ref pausedUntilTicks, until.UtcTicks);
        }

        // Once per pause, not once per refused request.
        logger.LogWarning("OpenDota answered 429 (rate limit). Not calling it for {Pause}.", pause);
    }

    // From OpenDota's X-Rate-Limit-Remaining-Day header.
    public void ReportRemainingToday(int remaining)
    {
        if (remaining >= LowDailyQuota)
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcTicks;
        lock (gate)
        {
            if (lastLowQuotaWarningTicks != 0 && now - lastLowQuotaWarningTicks < LowDailyQuotaWarningInterval.Ticks)
            {
                return;
            }

            lastLowQuotaWarningTicks = now;
        }

        logger.LogWarning("OpenDota daily quota is running out: {Remaining} calls left today.", remaining);
    }
}
