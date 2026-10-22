using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class PlayQualityMetricsTests
{
    private static readonly Dictionary<int, Team> Teams = new()
    { [1] = Team.Radiant, [2] = Team.Radiant, [3] = Team.Dire };

    private static Dictionary<int, HeroPositions> Positions(int health = 350, int second = 100) => new()
    {
        [2] = new(2, [new(second, 0, 0, new HeroVitals(health, 1000, null, null))]),
        [3] = new(3, [new(second, 100, 0, new HeroVitals(1000, 1000, null, null))]),
    };

    private static PlayQualityMetrics Calculate(ReplayActivity? activity, IReadOnlyList<HeroDeath>? deaths = null,
        double end = 200, IReadOnlyCollection<string>? kit = null, Dictionary<int, HeroPositions>? positions = null) =>
        PlayQualityMetricsCalculator.Calculate(1, activity, Teams, positions ?? Positions(), deaths ?? [], end, kit);

    [Theory]
    [InlineData(102, 3, 1)]
    [InlineData(106.1, 3, 0)]
    [InlineData(102, 2, 0)]
    public void Skillshot_MatchesOnlyEnemyEffectInWindow(double effectTime, int target, int hits)
    {
        var activity = new ReplayActivity([], [], [new(1, 101, "pudge_meat_hook")],
            debuffs: [new(effectTime, 108, 1, target, "modifier_pudge_meat_hook", DebuffKind.Stun)]);
        var result = Calculate(activity);
        Assert.Equal(1, result.SkillshotUses);
        Assert.Equal(hits, result.SkillshotHits);
    }

    [Fact]
    public void HeroWithoutSkillshot_IsNotMeasuredAsZero()
    {
        var result = Calculate(new([], [], [], debuffs: []), kit: ["axe_berserkers_call"]);
        Assert.Null(result.SkillshotUses);
        Assert.Null(result.SkillshotHits);
    }

    [Fact]
    public void KnownSkillshotWithoutCasts_IsZero()
    {
        var result = Calculate(new([], [], [], debuffs: []), kit: ["mirana_arrow"]);
        Assert.Equal(0, result.SkillshotUses);
        Assert.Equal(0, result.SkillshotHits);
    }

    [Fact]
    public void MissingDebuffsAndTruncatedWindow_AreUnknown()
    {
        Assert.Null(Calculate(new([], [], [new(1, 101, "mirana_arrow")])).SkillshotHits);
        Assert.Null(Calculate(new([], [], [new(1, 101, "mirana_arrow")], debuffs: []), end: 103).SkillshotHits);
        Assert.Null(Calculate(null).Saves);
    }

    [Fact]
    public void SameEffectCannotHitTwoCasts()
    {
        var result = Calculate(new([], [], [new(1, 101, "mirana_arrow"), new(1, 102, "mirana_arrow")],
            debuffs: [new(103, 107, 1, 3, "modifier_mirana_arrow", DebuffKind.Stun)]));
        Assert.Equal(2, result.SkillshotUses);
        Assert.Equal(1, result.SkillshotHits);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void Save_CountsLowHealthAllyAndSurvival(bool died, int saves)
    {
        var result = Calculate(new([], [new(1, 101, "item_glimmer_cape")], [], buffs: [new(101, 106, 1, 2, "modifier_item_glimmer_cape_fade", BuffKind.Save)]),
            died ? [new(2, 111, 0, 0, KillerKind.Hero, 3, false)] : []);
        Assert.Equal(1, result.LowHealthAllyUses);
        Assert.Equal(saves, result.Saves);
    }

    [Fact]
    public void Save_BuffAndAbilityCountOnce_AndUntargetedItemsDoNotInvalidate()
    {
        var result = Calculate(new([], [new(1, 101, "item_force_staff")],
            [new(1, 101, "oracle_false_promise", 2)],
            buffs: [new(101, 108, 1, 2, "modifier_oracle_false_promise_timer", BuffKind.Save)]));
        Assert.Equal(1, result.LowHealthAllyUses);
        Assert.Equal(1, result.Saves);
        Assert.Null(Calculate(new([], [], [])).LowHealthAllyUses);
    }

    [Fact]
    public void Save_AllyBuffOnEvaluatedHeroIsNotTheirSave()
    {
        var result = Calculate(new([], [], [],
            buffs: [new(101, 106, 2, 1, "modifier_item_glimmer_cape_fade", BuffKind.Save)]));
        Assert.Equal(0, result.LowHealthAllyUses);
        Assert.Equal(0, result.Saves);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void SelfOrEnemyTarget_IsNotAnAllySave(int target)
    {
        var result = Calculate(new([], [], [], buffs: [new(101, 106, 1, target, "modifier_item_forcestaff_active", BuffKind.Other)]));
        Assert.Equal(0, result.LowHealthAllyUses);
        Assert.Equal(0, result.Saves);
    }

    [Fact]
    public void MissingHealthOrEnemyPosition_IsUnknown()
    {
        var activity = new ReplayActivity([], [], [], buffs: [new(101, 106, 1, 2, "modifier_item_forcestaff_active", BuffKind.Other)]);
        Assert.Null(Calculate(activity, positions: Positions(second: 98)).LowHealthAllyUses);
        var positions = Positions();
        positions.Remove(3);
        Assert.Null(Calculate(activity, positions: positions).LowHealthAllyUses);
    }

    [Fact]
    public void FullSurvivalWindowAndDeathFamilyAreRequired()
    {
        var activity = new ReplayActivity([], [], [], buffs: [new(101, 106, 1, 2, "modifier_item_forcestaff_active", BuffKind.Other)]);
        Assert.Null(Calculate(activity, end: 105).Saves);
        var result = PlayQualityMetricsCalculator.Calculate(1, activity, Teams, Positions(), null, 200);
        Assert.Equal(1, result.LowHealthAllyUses);
        Assert.Null(result.Saves);
    }

    [Theory]
    [InlineData(101, 1)]
    [InlineData(101.5, 1)]
    [InlineData(101.501, 0)]
    [InlineData(100.9, 0)]
    public void Dispel_CountsDisableEndingInsideWindow(double end, int expected)
    {
        var result = Calculate(new([], [new(1, 101, "item_manta")], [],
            buffs: [], debuffs: [new(99, end, 3, 1, "modifier_silence", DebuffKind.Silence)]));
        Assert.Equal(expected, result.DispelledDebuffs);
    }

    [Fact]
    public void Dispel_UnknownEndIsUnknown_AndUntargetedUseAloneIsZero()
    {
        Assert.Null(Calculate(new([], [new(1, 101, "item_manta")], [], buffs: [],
            debuffs: [new(99, null, 3, 1, "modifier_silence", DebuffKind.Silence)])).DispelledDebuffs);
        Assert.Equal(0, Calculate(new([], [new(1, 101, "item_cyclone")], [], buffs: [], debuffs: [])).DispelledDebuffs);
        Assert.Null(Calculate(new([], [], [], debuffs: [])).DispelledDebuffs);
    }

    [Theory]
    [InlineData("modifier_eul_cyclone", 1, 1)]
    [InlineData("modifier_wind_waker", 1, 1)]
    [InlineData("modifier_item_lotus_orb_active", 1, 1)]
    [InlineData("modifier_eul_cyclone", 2, 0)]
    public void Dispel_TargetedBuffOnSelfCountsWithoutItemTarget(string modifier, int target, int expected)
    {
        var result = Calculate(new([], [new(1, 101, "item_cyclone")], [],
            buffs: [new(101, 103, 2, target, modifier, BuffKind.Save)],
            debuffs: [new(99, 101.2, 3, 1, "modifier_silence", DebuffKind.Silence)]));
        Assert.Equal(expected, result.DispelledDebuffs);
    }
}