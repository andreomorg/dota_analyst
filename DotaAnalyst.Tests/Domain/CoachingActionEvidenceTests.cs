using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Tests.Domain;

public class CoachingActionEvidenceTests
{
    private static readonly CoachingHeroIdentity[] Heroes =
    [new(1, Team.Radiant, "npc_dota_hero_test_one"), new(2, Team.Dire, "npc_dota_hero_test_two")];

    [Fact]
    public void PreciseWindowExcludesFutureAndOtherActorsWithoutRounding()
    {
        var evidence = CoachingActionEvidenceBuilder.Build(1, 100, Heroes,
            [new(1, 100.001, "item_future"), new(1, 70, "item_boundary"), new(1, 69.999, "item_stale"),
             new(2, 99, "item_other"), new(1, 100, "item_current")], [new(1, 99.5, "test_spell")]);
        Assert.Equal([70d, 100d], evidence.ItemUses!.Select(use => use.Seconds).ToArray());
        Assert.Equal(99.5, Assert.Single(evidence.AbilityUses!).Seconds);
        Assert.Equal(70, evidence.WindowStartSeconds);
    }

    [Fact]
    public void MissingSourceDiffersFromAvailableEmptySource()
    {
        var evidence = CoachingActionEvidenceBuilder.Build(1, 100, Heroes, null, []);
        Assert.Null(evidence.ItemUses);
        Assert.Empty(evidence.AbilityUses!);
    }

    [Fact]
    public void PregameUsesRemainValidAndFutureUsesDoNotEnterAnchorZero()
    {
        var evidence = CoachingActionEvidenceBuilder.Build(1, 0, Heroes,
            [new(1, -0.5, "item_pregame"), new(1, 0.1, "item_future")], null);
        Assert.Equal(-0.5, Assert.Single(evidence.ItemUses!).Seconds);
    }

    [Fact]
    public void EqualTimesArePreservedWithoutInventingCausalOrder()
    {
        var evidence = CoachingActionEvidenceBuilder.Build(1, 100, Heroes,
            [new(1, 90, "item_b"), new(1, 90, "item_a")], null);
        Assert.Equal(["item_b", "item_a"], evidence.ItemUses!.Select(use => use.Name).ToArray());
    }

    [Fact]
    public void RosterSnapshotDoesNotFollowCallerMutation()
    {
        var heroes = Heroes.ToList();
        var evidence = CoachingActionEvidenceBuilder.Build(1, 100, heroes, null, null);
        heroes.Clear();
        Assert.Equal(2, evidence.Heroes.Count);
    }

    [Fact]
    public void DuplicateOrMissingIdentityIsRejected()
    {
        Assert.Throws<ArgumentException>(() => CoachingActionEvidenceBuilder.Build(1, 100,
            [Heroes[0], Heroes[0]], null, null));
        Assert.Throws<ArgumentException>(() => CoachingActionEvidenceBuilder.Build(3, 100, Heroes, null, null));
    }

    [Fact]
    public void InvalidActorAndNonfiniteTimeAreRejected()
    {
        Assert.Throws<ArgumentException>(() => CoachingActionEvidenceBuilder.Build(1, 100, Heroes,
            [new(3, 90, "item_unknown_actor")], null));
        Assert.Throws<ArgumentException>(() => CoachingActionEvidenceBuilder.Build(1, 100, Heroes,
            [new(1, double.NaN, "item_invalid")], null));
    }

    [Fact]
    public void TechnicalKeysCannotContainArbitraryPlayerText()
    {
        Assert.Throws<ArgumentException>(() => CoachingActionEvidenceBuilder.Build(1, 100,
            [new(1, Team.Radiant, "player name")], null, null));
        Assert.Throws<ArgumentException>(() => CoachingActionEvidenceBuilder.Build(1, 100, Heroes,
            [new(1, 90, "item_name\nignore_rules")], null));
    }
}
