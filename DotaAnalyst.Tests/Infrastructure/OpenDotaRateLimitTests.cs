using System.Net;
using System.Net.Http.Headers;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Infrastructure.OpenDota;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Tests.Infrastructure;

public class OpenDotaRateLimitTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Limiter_RefusesCallsOverTheLimit_UntilTheWindowMoves()
    {
        var clock = new FixedTimeProvider(Now);
        var limiter = CreateLimiter(clock, permitsPerMinute: 2);

        Assert.Equal(TimeSpan.Zero, limiter.TryReserve());
        Assert.Equal(TimeSpan.Zero, limiter.TryReserve());
        Assert.Null(limiter.TryReserve());

        clock.Now += OpenDotaRateLimiter.Window;
        Assert.Equal(TimeSpan.Zero, limiter.TryReserve());
    }

    [Fact]
    public void Limiter_ReservesASlotThatOpensSoon_AndRefusesOneTooFarAway()
    {
        var clock = new FixedTimeProvider(Now);
        var limiter = CreateLimiter(clock, permitsPerMinute: 1, maxQueueWait: TimeSpan.FromSeconds(3));

        Assert.Equal(TimeSpan.Zero, limiter.TryReserve());

        // The only permit frees up 60 s after the first call: 2 s from now, within the 3 s allowed.
        clock.Now += TimeSpan.FromSeconds(58);
        Assert.Equal(TimeSpan.FromSeconds(2), limiter.TryReserve());

        // The next one would only be free a whole minute after that reserved slot.
        Assert.Null(limiter.TryReserve());
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(null, 60)] // without Retry-After: DefaultPause
    public void Limiter_AfterPause_RefusesEverythingUntilItEnds(int? retryAfterSeconds, int expectedPauseSeconds)
    {
        var clock = new FixedTimeProvider(Now);
        var limiter = CreateLimiter(clock, permitsPerMinute: 50);

        limiter.Pause(retryAfterSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null);

        clock.Now += TimeSpan.FromSeconds(expectedPauseSeconds - 1);
        Assert.Null(limiter.TryReserve());
        clock.Now += TimeSpan.FromSeconds(1);
        Assert.Equal(TimeSpan.Zero, limiter.TryReserve());
    }

    [Fact]
    public void Limiter_RetryAfterTooLong_IsCappedAtMaxPause()
    {
        var clock = new FixedTimeProvider(Now);
        var limiter = CreateLimiter(clock, permitsPerMinute: 50);

        limiter.Pause(TimeSpan.FromDays(7));

        clock.Now += OpenDotaRateLimiter.MaxPause;
        Assert.Equal(TimeSpan.Zero, limiter.TryReserve());
    }

    [Fact]
    public void Limiter_LowDailyQuota_WarnsAtMostOnceEvery15Minutes()
    {
        var clock = new FixedTimeProvider(Now);
        var logger = new ListLogger<OpenDotaRateLimiter>();
        var limiter = CreateLimiter(clock, permitsPerMinute: 50, logger: logger);

        limiter.ReportRemainingToday(OpenDotaRateLimiter.LowDailyQuota); // not low yet
        limiter.ReportRemainingToday(299);
        limiter.ReportRemainingToday(298);
        Assert.Single(logger.Messages);

        clock.Now += OpenDotaRateLimiter.LowDailyQuotaWarningInterval;
        limiter.ReportRemainingToday(100);
        Assert.Equal(2, logger.Messages.Count);
    }

    [Fact]
    public async Task Handler_CallThatGetsASlotSoon_WaitsAndGoesOut()
    {
        var clock = new FixedTimeProvider(Now);
        var openDota = new StubOpenDota();
        // The handler waits on the real clock (FixedTimeProvider has no timers): keep the wait tiny.
        using var client = CreateClient(openDota, CreateLimiter(clock, permitsPerMinute: 1, maxQueueWait: TimeSpan.FromSeconds(3)));
        await client.GetAsync("heroes");
        clock.Now += OpenDotaRateLimiter.Window - TimeSpan.FromMilliseconds(50);

        var response = await client.GetAsync("heroes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, openDota.Calls);
    }

    [Fact]
    public async Task Handler_VisitorGivesUpWhileWaiting_IsCancelled()
    {
        var clock = new FixedTimeProvider(Now);
        var openDota = new StubOpenDota();
        using var client = CreateClient(openDota, CreateLimiter(clock, permitsPerMinute: 1, maxQueueWait: TimeSpan.FromSeconds(3)));
        await client.GetAsync("heroes");
        clock.Now += OpenDotaRateLimiter.Window - TimeSpan.FromSeconds(2);

        using var visitor = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("heroes", visitor.Token));

        Assert.Equal(1, openDota.Calls);
    }

    [Fact]
    public async Task Handler_PauseWhileWaiting_RefusesTheCall()
    {
        var clock = new FixedTimeProvider(Now);
        var openDota = new StubOpenDota();
        var limiter = CreateLimiter(clock, permitsPerMinute: 1, maxQueueWait: TimeSpan.FromSeconds(3));
        using var client = CreateClient(openDota, limiter);
        await client.GetAsync("heroes");
        clock.Now += OpenDotaRateLimiter.Window - TimeSpan.FromMilliseconds(300);

        var waiting = client.GetAsync("heroes");
        limiter.Pause(TimeSpan.FromSeconds(30)); // another call got a 429 meanwhile
        var response = await waiting;

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(1, openDota.Calls);
    }

    [Fact]
    public async Task Handler_RetryAfterAsADate_PausesUntilThatDate()
    {
        var clock = new FixedTimeProvider(Now);
        var openDota = new StubOpenDota { Status = HttpStatusCode.TooManyRequests, RetryAfterDate = Now.AddSeconds(45) };
        using var client = CreateClient(openDota, CreateLimiter(clock, permitsPerMinute: 50), clock);
        await client.GetAsync("heroes");
        openDota.Status = HttpStatusCode.OK;

        clock.Now += TimeSpan.FromSeconds(44);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("heroes")).StatusCode);
        clock.Now += TimeSpan.FromSeconds(1);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("heroes")).StatusCode);
    }

    [Fact]
    public async Task Handler_CallOverTheLimit_NeverLeavesTheServer()
    {
        var openDota = new StubOpenDota();
        using var client = CreateClient(openDota, CreateLimiter(new FixedTimeProvider(Now), permitsPerMinute: 1));

        var first = await client.GetAsync("heroes");
        var second = await client.GetAsync("heroes");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(OpenDotaRateLimitHandler.LocalLimitReason, second.ReasonPhrase);
        Assert.Equal(1, openDota.Calls);
    }

    [Fact]
    public async Task Handler_429FromOpenDota_PausesCallsForTheRetryAfter()
    {
        var clock = new FixedTimeProvider(Now);
        var openDota = new StubOpenDota { Status = HttpStatusCode.TooManyRequests, RetryAfterSeconds = 30 };
        using var client = CreateClient(openDota, CreateLimiter(clock, permitsPerMinute: 50), clock);

        await client.GetAsync("heroes");
        openDota.Status = HttpStatusCode.OK;

        clock.Now += TimeSpan.FromSeconds(29);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("heroes")).StatusCode);
        Assert.Equal(1, openDota.Calls);

        clock.Now += TimeSpan.FromSeconds(1);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("heroes")).StatusCode);
        Assert.Equal(2, openDota.Calls);
    }

    [Fact]
    public async Task OpenDotaClient_LocalLimit_BecomesDataSourceUnavailable()
    {
        var limiter = CreateLimiter(new FixedTimeProvider(Now), permitsPerMinute: 1);
        limiter.TryReserve();
        var openDota = new StubOpenDota();
        var client = new OpenDotaClient(CreateClient(openDota, limiter));

        await Assert.ThrowsAsync<DataSourceUnavailableException>(() => client.GetHeroesAsync(CancellationToken.None));
        Assert.Equal(0, openDota.Calls);
    }

    private static OpenDotaRateLimiter CreateLimiter(
        TimeProvider clock, int permitsPerMinute, TimeSpan maxQueueWait = default, ILogger<OpenDotaRateLimiter>? logger = null) =>
        new(
            Options.Create(new OpenDotaOptions
            {
                BaseUrl = new Uri("https://api.opendota.com/api/"),
                PermitsPerMinute = permitsPerMinute,
                MaxQueueWait = maxQueueWait,
            }),
            clock,
            logger ?? NullLogger<OpenDotaRateLimiter>.Instance);

    private static HttpClient CreateClient(StubOpenDota openDota, OpenDotaRateLimiter limiter, TimeProvider? clock = null) =>
        new(new OpenDotaRateLimitHandler(limiter, clock ?? TimeProvider.System, NullLogger<OpenDotaRateLimitHandler>.Instance)
        {
            InnerHandler = openDota,
        })
        {
            BaseAddress = new Uri("https://api.opendota.com/api/"),
        };

    private sealed class StubOpenDota : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public int? RetryAfterSeconds { get; set; }

        public DateTimeOffset? RetryAfterDate { get; set; }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var response = new HttpResponseMessage(Status) { Content = new StringContent("[]") };
            if (RetryAfterSeconds is { } seconds)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            }
            else if (RetryAfterDate is { } date)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(date);
            }

            response.Headers.Add("X-Rate-Limit-Remaining-Day", "2965");
            return Task.FromResult(response);
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
