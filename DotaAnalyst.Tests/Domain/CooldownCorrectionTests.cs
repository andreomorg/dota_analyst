using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class CooldownCorrectionTests
{
    private static ReplayActivity Activity(IReadOnlyList<CooldownStart> abilities, IReadOnlyList<CooldownCorrection> changes) =>
        new([], [], [], abilityCooldowns: abilities, abilityCooldownChanges: changes);

    [Fact]
    public void Reset_ReadyFromThenOn_NeverBackwards()
    {
        var activity = Activity([new CooldownStart(1, 100, "tidehunter_ravage", 250)],
            [new CooldownCorrection(1, 130, "tidehunter_ravage", 900, 3, 130)]);    // Refresher at 130

        var ravage = Assert.Single(activity.AbilityCooldowns!);
        Assert.True(ravage.ActiveAt(120));     // before the reset: still the old cooldown
        Assert.False(ravage.ActiveAt(130));    // reset: ready
        Assert.False(ravage.ActiveAt(200));
        Assert.Equal(250, ravage.ReadyAtAsOf(129));
        Assert.Equal(150, ravage.Length);      // as it started
    }

    [Fact]
    public void Reduction_MovesTheReadyTimeEarlier()
    {
        var activity = Activity([new CooldownStart(1, 100, "x", 200)], [new CooldownCorrection(1, 110, "x", 5, 1, 180)]);

        var cooldown = Assert.Single(activity.AbilityCooldowns!);
        Assert.True(cooldown.ActiveAt(179));
        Assert.False(cooldown.ActiveAt(180));
    }

    [Fact]
    public void Ambiguous_OrUnmatched_AreKeptApart()
    {
        var activity = Activity(
            [new CooldownStart(1, 100, "item_magic_wand", 113), new CooldownStart(1, 101, "item_magic_wand", 114)],  // two copies
            [new CooldownCorrection(1, 105, "item_magic_wand", 7, 1, 105), new CooldownCorrection(2, 105, "x", 8, 1, 105)]);

        Assert.All(activity.AbilityCooldowns!, cooldown => Assert.Empty(cooldown.Corrections));
        Assert.Equal(2, activity.AbilityCooldownChanges!.Count);
        Assert.True(activity.AbilityCooldowns![0].ActiveAt(110));
    }

    [Fact]
    public void LaterCooldown_IsNotCorrectedByAnEarlierChange()
    {
        var activity = Activity([new CooldownStart(1, 100, "x", 120), new CooldownStart(1, 200, "x", 260)],
            [new CooldownCorrection(1, 110, "x", 5, 1, 110)]);

        Assert.Single(activity.AbilityCooldowns![0].Corrections);
        Assert.Empty(activity.AbilityCooldowns[1].Corrections);
        Assert.True(activity.AbilityCooldowns[1].ActiveAt(230));
    }
}
