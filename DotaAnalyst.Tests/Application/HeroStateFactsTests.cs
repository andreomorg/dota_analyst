using DotaAnalyst.Application.Heroes;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Application;

public class HeroStateFactsTests
{
    [Fact]
    public void From_KeepsEachSourceApart_AndHidesTheFog()
    {
        var sample = new PositionSample(100, 10, 20,
            new HeroVitals(500, 1000, 100, 400, VisibleToEnemies: true, UnitStateMask: 1 << 7, Gold: 900),
            new HeroCombatStats(2, 25, 300, 50, 60, 20, 30, 15), Facing: 90,
            CombatDetails: new HeroCombatDetails(19, 22, 14, 40, 46, null, 1.7, 150, 1, 0.25, 0.5));
        var details = new HeroDetails(HeroAttribute.Agility, HeroAttackType.Melee, new HashSet<HeroRole>());
        var result = new HeroStateResult("found", 1, 100, sample, details, new GameBuild("abc", 6962, DateTimeOffset.UnixEpoch));

        var facts = HeroStateFacts.From(result);

        Assert.Equal(("found", "matchBuild", "abc"), (facts.Status, facts.CatalogScope, facts.BuildId));
        Assert.Equal((10, 20, 90, 900), (facts.Visible!.X, facts.Visible.Y, facts.Visible.Facing!.Value, facts.Visible.Gold!.Value));
        Assert.True(facts.Visible.Invisible);
        Assert.Equal(2, facts.ReplicatedTotals!.Armor);
        Assert.Equal(1.7, facts.Components!.BaseAttackTime);
        Assert.Equal(1, facts.Components.AttackCapability);           // raw enum, not melee/ranged
        Assert.True(facts.HiddenSeenByEnemies);
        Assert.Null(facts.Nominal);                                  // the catalog had no profile
        Assert.False(facts.EffectiveDefenseKnown);
    }

    [Fact]
    public void From_NoSample_HasNoState()
    {
        var facts = HeroStateFacts.From(new HeroStateResult("noExactSample", 1, 101, null, null, null));

        Assert.Null(facts.Visible);
        Assert.Null(facts.ReplicatedTotals);
        Assert.Null(facts.HiddenSeenByEnemies);
        Assert.Equal("unavailable", facts.CatalogScope);
    }
}
