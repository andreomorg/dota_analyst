using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Replays;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Application;

public class GetGoldFactsHandlerTests
{
    private static MatchId Id { get { MatchId.TryCreate(123, out var id); return id; } }
    private static ArchivedReplay Replay(double? duration = 60.4, bool withEvents = true) => new(
        new ReplayAnalysis(Id, 60, [new(1, Team.Radiant, Lane.Bottom, 0, 0, 0, 0, 0)]), [],
        new ReplayActivity([], [], [], economy: [new(1, 58, EconomyResource.Gold, EconomySource.Creeps, 9),
            new(1, 60, EconomyResource.Gold, EconomySource.Creeps, 999)]),
        Metadata: duration is { } d ? new ReplayMetadata(d, 6962, null, true) : null,
        Facts: withEvents ? new ReplayAdditionalFacts { GoldEvents = [new(1, 60.4, 40, 13, null, null), new(1, 60.41, 999, 13, null, null)] } : null);

    [Fact]
    public async Task ExactEnd_UsesEventsWithoutLeakingPastDuration()
    {
        var archive = new Archive(Replay());
        var result = await new GetGoldFactsHandler(archive).HandleAsync(Id, 1, 60.4, 30, default);
        Assert.Equal(ReplayFactsStatus.Found, result.Status);
        Assert.Equal(40, result.Facts!.Window.Changes!.NetChange);
        Assert.Equal(GoldResolution.IndividualEvents, result.Facts.Window.Resolution);
        Assert.Equal(1, archive.Reads);
    }

    [Fact]
    public async Task OldDocument_UsesOnlyCompleteBuckets()
    {
        var result = await new GetGoldFactsHandler(new Archive(Replay(withEvents: false))).HandleAsync(Id, 1, 60.4, 30, default);
        Assert.Equal(GoldResolution.TwoSecondBuckets, result.Facts!.Window.Resolution);
        Assert.Equal(9, result.Facts.Window.Changes!.NetChange);
    }

    [Theory]
    [InlineData(0, 10, 30)]
    [InlineData(1, double.NaN, 30)]
    [InlineData(1, double.PositiveInfinity, 30)]
    [InlineData(1, -1, 30)]
    [InlineData(1, 10, 301)]
    [InlineData(1, 10, -1)]
    public async Task InvalidInput_DoesNotReadArchive(int hero, double at, int window)
    {
        var archive = new Archive(Replay());
        Assert.Equal(ReplayFactsStatus.Invalid, (await new GetGoldFactsHandler(archive).HandleAsync(Id, hero, at, window, default)).Status);
        Assert.Equal(0, archive.Reads);
    }

    [Fact]
    public async Task MissingDocumentAndMissingGoldAreDifferent()
    {
        Assert.Equal(ReplayFactsStatus.NoDocument,
            (await new GetGoldFactsHandler(new Archive(null)).HandleAsync(Id, 1, 10, 30, default)).Status);
        var replay = Replay() with { Facts = null, Activity = null };
        var result = await new GetGoldFactsHandler(new Archive(replay)).HandleAsync(Id, 1, 10, 30, default);
        Assert.Equal(ReplayFactsStatus.Found, result.Status);
        Assert.Equal(GoldResolution.Unavailable, result.Facts!.Window.Resolution);
        Assert.Null(result.Facts.Window.Changes);
    }

    [Fact]
    public async Task UnknownDurationOtherHeroAndPastEnd_AreRejected()
    {
        foreach (var (replay, hero, at) in new[] { (Replay(null), 1, 10d), (Replay(double.NaN), 1, 10d),
                     (Replay(), 2, 10d), (Replay(), 1, 61d) })
            Assert.Equal(ReplayFactsStatus.Invalid,
                (await new GetGoldFactsHandler(new Archive(replay)).HandleAsync(Id, hero, at, 30, default)).Status);
    }

    [Fact]
    public async Task Balance_IsRecentPastSample_IncludingPartialComponents_NeverFutureOrStale()
    {
        var replay = Replay() with { Facts = new ReplayAdditionalFacts { GoldBalances =
            [new(1, 10, 40, null), new(1, 12, 80, 20), new(2, 11, 999, 999)] } };
        var handler = new GetGoldFactsHandler(new Archive(replay));
        var at11 = (await handler.HandleAsync(Id, 1, 11, 30, default)).Facts!.SampledBalance!;
        Assert.Equal(10, at11.Seconds);
        Assert.Equal(40, at11.Reliable);
        Assert.Null(at11.Total);
        Assert.Null((await handler.HandleAsync(Id, 1, 9, 30, default)).Facts!.SampledBalance);
        Assert.Equal(100, (await handler.HandleAsync(Id, 1, 14, 30, default)).Facts!.SampledBalance!.Total);
        Assert.Null((await handler.HandleAsync(Id, 1, 14.01, 30, default)).Facts!.SampledBalance);
    }

    [Fact]
    public async Task BuybacksUseDedicatedRecordedCosts_NotGoldLogAbsence()
    {
        var replay = Replay() with { Activity = new ReplayActivity([], [], [], buybacks: [new(1, 55.5, 637), new(1, 60.41, 999)]) };
        var result = await new GetGoldFactsHandler(new Archive(replay)).HandleAsync(Id, 1, 60.4, 30, default);
        Assert.Equal(637, result.Facts!.Buybacks!.RecordedCostTotal);
        Assert.Single(result.Facts.Buybacks.Uses);
        Assert.False(result.Facts.Payments!.CompleteLedgerEstablished);
    }

    private sealed class Archive(ArchivedReplay? replay) : IArchivedReplaySource
    {
        public int Reads { get; private set; }
        public Task<ArchivedReplay?> FindAsync(MatchId matchId, CancellationToken cancellationToken)
        { Reads++; return Task.FromResult(replay); }
    }
}
