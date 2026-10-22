using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class GoldAccountingTests
{
    private static GoldEvent Event(double at, long amount, uint? reason = 13, int hero = 1) => new(hero, at, amount, reason, null, null);
    private static EconomyBucket Bucket(int at, int amount, EconomyResource resource = EconomyResource.Gold) =>
        new(1, at, resource, EconomySource.Creeps, amount);

    [Fact]
    public void Events_PreserveSignsReasonsAndFractionalClosedWindow_WithoutDoubleCounting()
    {
        var result = GoldAccounting.Window(1, 10, 12.4,
            [Event(9.99, 999), Event(10, 40), Event(11, -15), Event(12.4, 60, 12), Event(12.41, 999), Event(11, 999, hero: 2)],
            [Bucket(10, 1000)]);
        Assert.Equal(GoldResolution.IndividualEvents, result.Resolution);
        Assert.Equal(new GoldChanges(100, 15, 85), result.Changes);
        Assert.Equal(new GoldChanges(40, 15, 25), Assert.Single(result.Sources, row => row.RawReason == 13).Changes);
    }

    [Fact]
    public void UnknownReasons_RemainSeparate_AndLargeTotalsDoNotWrapInt32()
    {
        var result = GoldAccounting.Window(1, 0, 10,
            [Event(1, int.MaxValue, null), Event(2, int.MaxValue, uint.MaxValue), Event(3, -20, 123)], null);
        Assert.Equal(2L * int.MaxValue - 20, result.Changes!.NetChange);
        Assert.Equal(3, result.Sources.Count);
        Assert.All(result.Sources, row => Assert.Equal(EconomySource.Other, row.Source));
    }

    [Fact]
    public void MissingFamilies_AreUnknown_PresentEmptyEventsAreZeroAndAuthoritative()
    {
        Assert.Null(GoldAccounting.Window(1, 0, 10, null, null).Changes);
        var empty = GoldAccounting.Window(1, 0, 10, [], [Bucket(0, 999)]);
        Assert.Equal(GoldResolution.IndividualEvents, empty.Resolution);
        Assert.Equal(new GoldChanges(0, 0, 0), empty.Changes);
        Assert.Empty(empty.Sources);
    }

    [Fact]
    public void LegacyBuckets_AreGoldOnlyAndEntirelyInsideWindow()
    {
        var result = GoldAccounting.Window(1, 1, 7.9, null,
            [Bucket(0, 999), Bucket(2, 80), Bucket(4, -10), Bucket(6, 999), Bucket(2, 999, EconomyResource.Experience)]);
        Assert.Equal(GoldResolution.TwoSecondBuckets, result.Resolution);
        Assert.Equal(new GoldChanges(80, 10, 70), result.Changes);
        Assert.Null(Assert.Single(result.Sources).RawReason);
    }

    [Fact]
    public void SameInstantIncludesEvents_ButNoPartialBucket()
    {
        Assert.Equal(40, GoldAccounting.Window(1, 2, 2, [Event(2, 40)], null).Changes!.NetChange);
        Assert.Equal(0, GoldAccounting.Window(1, 2, 2, null, [Bucket(2, 40)]).Changes!.NetChange);
    }

    [Theory]
    [InlineData(double.NaN, 10)]
    [InlineData(0, double.NaN)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(-1, 10)]
    [InlineData(10, 9)]
    public void InvalidWindowIsRejected(double from, double to) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => GoldAccounting.Window(1, from, to, [], null));

    [Fact]
    public void OverflowIsRejectedInsteadOfFabricatingAnAmount() =>
        Assert.Throws<OverflowException>(() => GoldAccounting.Window(1, 0, 10, [Event(1, long.MinValue)], null));
}
