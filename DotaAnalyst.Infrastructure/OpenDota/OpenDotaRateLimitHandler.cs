using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Infrastructure.OpenDota;

// Sits inside OpenDota's HttpClient (AddHttpMessageHandler): every call to OpenDota passes through here first.
// A call the limiter refuses never leaves the server; it gets a local 429, which OpenDotaClient turns into
// DataSourceUnavailableException exactly like a real 429 from OpenDota.
public sealed class OpenDotaRateLimitHandler(
    OpenDotaRateLimiter limiter,
    TimeProvider timeProvider,
    ILogger<OpenDotaRateLimitHandler> logger) : DelegatingHandler
{
    public const string LocalLimitReason = "Local OpenDota rate limit";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (limiter.TryReserve() is not { } wait)
        {
            return Refused(request);
        }

        if (wait > TimeSpan.Zero)
        {
            // If the visitor gives up meanwhile, this throws and the reserved slot just goes unused.
            await Task.Delay(wait, timeProvider, cancellationToken);

            // OpenDota may have answered 429 to another call while this one waited.
            if (limiter.IsPaused)
            {
                return Refused(request);
            }
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            limiter.Pause(RetryAfter(response));
        }

        if (response.Headers.TryGetValues("X-Rate-Limit-Remaining-Day", out var values)
            && int.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var remaining))
        {
            limiter.ReportRemainingToday(remaining);
        }

        return response;
    }

    // Debug, not Warning: the caller already logs the resulting DataSourceUnavailableException (with this
    // reason phrase), and while paused every request would add one more warning. The pause itself is logged once.
    private HttpResponseMessage Refused(HttpRequestMessage request)
    {
        logger.LogDebug("OpenDota call to {Path} refused by the local rate limit.", request.RequestUri?.AbsolutePath);
        return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            ReasonPhrase = LocalLimitReason,
            RequestMessage = request,
        };
    }

    // Retry-After comes either as seconds ("30") or as a date (a past date ends up as the default pause).
    private TimeSpan? RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - timeProvider.GetUtcNow(),
            _ => null,
        };
}
