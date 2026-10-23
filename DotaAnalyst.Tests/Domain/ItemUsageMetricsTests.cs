using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

// ItemUsageMetrics: fight items held ready at a fight's start and used during it.
public class ItemUsageMetricsTests
{
    private const int Hero = 1;

    private static Teamfight Fight(int start, int end) => new(start, end, 2, 1, Team.Radiant, []);

    private static ReplayActivity Activity(IReadOnlyList<RecordedUse> uses, IReadOnlyList<CooldownStart>? cooldowns = null) =>
        new([], uses, [],
            slotChanges: [new(Hero, 10, 0, "item_black_king_bar"), new(Hero, 10, 1, "item_blink"), new(Hero, 10, 2, "item_branches")],
            itemCooldowns: cooldowns ?? []);

    [Fact]
    public void BkbHeldReady_CountsTheFight_AndAUseDuringItCountsAsUsed()
    {
        var uses = new List<RecordedUse> { new(Hero, 495, "item_black_king_bar"), new(Hero, 1205, "item_blink") };

        var metrics = ItemUsageMetricsCalculator.Calculate(Hero, [Fight(500, 520), Fight(1200, 1215)], Activity(uses));

        Assert.Equal(2, metrics.Fights);
        Assert.Equal((2, 2), (metrics.KeyItemReady!.Value, metrics.KeyItemUsed!.Value));
        // BKB used in the first fight only (5 s before its first death: still the lead-in).
        Assert.Equal((2, 1), (metrics.BkbReady!.Value, metrics.BkbUsed!.Value));
    }

    [Fact]
    public void ItemOnCooldownAtTheStart_IsNotReady()
    {
        var cooling = new List<CooldownStart> { new(Hero, 480, "item_black_king_bar", 560), new(Hero, 480, "item_blink", 560) };

        var metrics = ItemUsageMetricsCalculator.Calculate(Hero, [Fight(500, 520)], Activity([], cooling));

        Assert.Equal(0, metrics.KeyItemReady);
        Assert.Equal(0, metrics.BkbReady);
    }

    [Fact]
    public void WithoutInventories_TheCountsAreUnknown()
    {
        var metrics = ItemUsageMetricsCalculator.Calculate(Hero, [Fight(500, 520)], new ReplayActivity([], [], []));

        Assert.Equal(1, metrics.Fights);
        Assert.Null(metrics.KeyItemReady);
        Assert.Null(metrics.BkbUsed);
    }
}
