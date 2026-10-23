using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

// DeathContextMetrics: how each death looked at its moment (escape item / BKB ready, 3+ attackers, disabled).
public class DeathContextMetricsTests
{
    private const int Hero = 1;

    private static HeroDeath Death(int seconds, int? killer = 6, params int[] assists) =>
        new(Hero, seconds, 0, 0, killer is null ? KillerKind.Tower : KillerKind.Hero, killer, reincarnated: false, assists);

    private static ReplayActivity Activity(
        IReadOnlyList<SlotChange>? slots = null, IReadOnlyList<CooldownStart>? cooldowns = null, IReadOnlyList<Debuff>? debuffs = null) =>
        new([], [], [], slotChanges: slots, itemCooldowns: cooldowns, debuffs: debuffs);

    [Fact]
    public void ReadyEscapeItem_AndBkb_CountWhenHeldWithoutCooldown()
    {
        var slots = new List<SlotChange> { new(Hero, 10, 0, "item_blink"), new(Hero, 10, 1, "item_black_king_bar") };
        // Blink on cooldown at the second death (used at 290, ready at 302), ready at the first.
        var cooldowns = new List<CooldownStart> { new(Hero, 290, "item_blink", 302) };

        var metrics = DeathContextMetricsCalculator.Calculate(Hero, [Death(100), Death(300)], Activity(slots, cooldowns));

        Assert.Equal(2, metrics.Deaths);
        // BKB ready at both: both deaths had a way out unused.
        Assert.Equal(2, metrics.EscapeItemReady);
        Assert.Equal(2, metrics.BkbReady);
    }

    [Fact]
    public void ItemBoughtAfterTheDeath_DoesNotCount_NorTheBackpack()
    {
        var slots = new List<SlotChange> { new(Hero, 200, 0, "item_blink"), new(Hero, 50, 6, "item_force_staff") };

        var metrics = DeathContextMetricsCalculator.Calculate(Hero, [Death(100)], Activity(slots, []));

        Assert.Equal(0, metrics.EscapeItemReady);
    }

    [Fact]
    public void ThreeOrMoreAttackers_CountTheKillerAndTheAssists()
    {
        var deaths = new[] { Death(100, 6, 7, 8), Death(200, 6, 7), Death(300, null, 6, 7, 8) };

        var metrics = DeathContextMetricsCalculator.Calculate(Hero, deaths, null);

        // 6+7+8, 6+7 (two), tower with 6/7/8 assisting (three heroes).
        Assert.Equal(2, metrics.ToThreeOrMore);
    }

    [Fact]
    public void Disables_BeforeTheDeath_AreAUnion_CutToTheWindow()
    {
        var debuffs = new List<Debuff>
        {
            new(95, 98, 6, Hero, "modifier_stunned", DebuffKind.Stun),
            // Overlaps the stun: 97..99 adds only 98..99.
            new(97, 99, 7, Hero, "modifier_silence", DebuffKind.Silence),
            // Started before the window (100 - 10 = 90): only 90..92 counts.
            new(80, 92, 6, Hero, "modifier_rooted", DebuffKind.Root),
            // A slow is not a disable.
            new(91, 100, 6, Hero, "modifier_slow", DebuffKind.Slow),
            // Still on at the death.
            new(99.5, 101, 7, Hero, "modifier_hexed", DebuffKind.Hex),
        };

        var metrics = DeathContextMetricsCalculator.Calculate(Hero, [Death(100)], Activity(debuffs: debuffs));

        Assert.Equal(6.5, metrics.DisabledSecondsBefore);
        Assert.Equal(1, metrics.WhileDisabled);
    }

    [Fact]
    public void WithoutTheActivity_TheCountsAreUnknown_NotZero()
    {
        var metrics = DeathContextMetricsCalculator.Calculate(Hero, [Death(100)], null);

        Assert.Null(metrics.EscapeItemReady);
        Assert.Null(metrics.BkbReady);
        Assert.Null(metrics.WhileDisabled);
        Assert.Null(metrics.DisabledSecondsBefore);
        Assert.Equal(1, metrics.Deaths);
    }
}
