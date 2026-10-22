using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public sealed class NeutralItemEvaluatorTests
{
    // The 7.41 tiers: 0:00, 15:00, 25:00, 35:00, 60:00.
    private static readonly NeutralRules Rules = new(
        [Tier(1, 0, 6), Tier(2, 900, 10), Tier(3, 1500, 10), Tier(4, 2100, 10), Tier(5, 3600, 10)], 4200, [2, 3, 4], [1, 2, 3]);

    private static NeutralTier Tier(int tier, int opensAt, int cost) =>
        new(tier, opensAt, 5, 5, cost, 0, [], [], new Dictionary<HeroAttribute, IReadOnlyList<NeutralEnhancement>>());

    [Fact]
    public void Crafts_GetTheirDelay_AndOpenedTiersWithoutACraftAreSkipped()
    {
        var crafts = new[]
        {
            new NeutralCraft(1, 400, "item_occult_bracelet", 1, 1, "item_enhancement_vital", 1),
            new NeutralCraft(1, 1150.4, "item_pogo_stick", 1, 2, "item_enhancement_brawny", 2),
            new NeutralCraft(6, 950, "item_essence_ring", 1, 2, null, null),
        };

        // A 41-minute match: tiers 1-4 opened in time (tier 4 at 35:00 + 2 min grace); tier 5 never opened.
        var heroes = NeutralItemEvaluator.Evaluate([1, 6], crafts, [new NeutralProgress(1, 30, 12, 4, 500)], Rules, 2460);

        var lion = heroes[0];
        Assert.Equal([400, 250], lion.Crafts.Select(craft => craft.DelaySeconds));
        Assert.Equal([3, 4], lion.TiersSkipped);
        Assert.Equal(325, lion.AverageDelaySeconds);
        Assert.Equal(4, lion.Madstones!.InHandAtEnd);
        Assert.Equal([1, 3, 4], heroes[1].TiersSkipped);
        Assert.Null(heroes[1].Madstones);
    }

    [Fact]
    public void WithoutTheBuildRules_CraftsHaveNoDelay_AndNothingIsSkipped()
    {
        var hero = NeutralItemEvaluator.Evaluate([1], [new NeutralCraft(1, 400, "item_occult_bracelet", 1, 1, null, null)], null, null, 2400)[0];

        Assert.Null(hero.Crafts[0].DelaySeconds);
        Assert.Empty(hero.TiersSkipped);
        Assert.Null(hero.AverageDelaySeconds);
    }

    [Fact]
    public void ATierOpenedJustBeforeTheEnd_IsNotSkipped() =>
        Assert.DoesNotContain(4, NeutralItemEvaluator.Evaluate([1], [], null, Rules, 2100 + NeutralItemEvaluator.GraceSeconds - 1)[0].TiersSkipped);
}
