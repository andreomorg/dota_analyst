using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Infrastructure.ReplayParser;
using DotaAnalyst.Tests.Domain;

namespace DotaAnalyst.Tests.Infrastructure;

public class ReplayParseQueueTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri ReplayUrl = new("http://replay202.valve.net/570/1_2.dem.bz2");

    [Fact]
    public void CircuitBreaker_StaysOpenForAWhile_ThenClosesByItself()
    {
        var clock = new FixedTimeProvider(Now);
        var breaker = new ReplayParserCircuitBreaker(clock);
        Assert.False(breaker.IsOpen);

        breaker.Open();
        clock.Now += ReplayParserCircuitBreaker.OpenFor - TimeSpan.FromSeconds(1);
        Assert.True(breaker.IsOpen);

        clock.Now += TimeSpan.FromSeconds(1);
        Assert.False(breaker.IsOpen);
    }

    [Fact]
    public void TryEnqueue_NewMatch_IsQueuedUntilCompleted()
    {
        var queue = CreateQueue(out _);
        var match = ReplayBuilder.MatchIdOf(1);

        Assert.False(queue.IsQueued(match));
        Assert.Equal(ReplayEnqueueResult.Enqueued, queue.TryEnqueue(match, ReplayUrl));
        Assert.True(queue.IsQueued(match));

        // Still queued while the worker parses it; free again once it is done.
        queue.Complete(match);
        Assert.False(queue.IsQueued(match));
        Assert.Equal(ReplayEnqueueResult.Enqueued, queue.TryEnqueue(match, ReplayUrl));
    }

    [Fact]
    public void TryEnqueue_SameMatchTwice_IsNotDuplicated()
    {
        var queue = CreateQueue(out _);
        var match = ReplayBuilder.MatchIdOf(1);

        queue.TryEnqueue(match, ReplayUrl);

        Assert.Equal(ReplayEnqueueResult.AlreadyQueued, queue.TryEnqueue(match, ReplayUrl));
    }

    [Fact]
    public void TryEnqueue_WhenFull_RefusesWithoutWaiting()
    {
        var queue = CreateQueue(out _);
        for (var i = 1; i <= ReplayParseQueue.Capacity; i++)
        {
            Assert.Equal(ReplayEnqueueResult.Enqueued, queue.TryEnqueue(ReplayBuilder.MatchIdOf(i), ReplayUrl));
        }

        var oneTooMany = ReplayBuilder.MatchIdOf(ReplayParseQueue.Capacity + 1);
        Assert.Equal(ReplayEnqueueResult.Full, queue.TryEnqueue(oneTooMany, ReplayUrl));
        // Refused means not queued: the next visit tries again.
        Assert.False(queue.IsQueued(oneTooMany));
    }

    [Fact]
    public void TryEnqueue_WhileTheParserIsDown_IsUnavailable()
    {
        var queue = CreateQueue(out var breaker);
        var match = ReplayBuilder.MatchIdOf(1);

        breaker.Open();

        Assert.Equal(ReplayEnqueueResult.Unavailable, queue.TryEnqueue(match, ReplayUrl));
        Assert.False(queue.IsQueued(match));
    }

    private static ReplayParseQueue CreateQueue(out ReplayParserCircuitBreaker breaker)
    {
        breaker = new ReplayParserCircuitBreaker(new FixedTimeProvider(Now));
        return new ReplayParseQueue(breaker);
    }
}
