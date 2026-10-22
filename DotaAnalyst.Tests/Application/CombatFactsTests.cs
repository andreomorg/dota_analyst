using DotaAnalyst.Application.Replays;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Application;

public class CombatFactsTests
{
    private static ReplayActivity Activity(IReadOnlyList<TypedDamageBucket>? typed, IReadOnlyList<HealingBucket>? healing) =>
        new([], [], [], typedDamage: typed, healing: healing);

    [Fact]
    public void Window_OnlyBucketsThatEndedInside()
    {
        var activity = Activity(
            [
                new TypedDamageBucket(1, 96, 10, 0, 0, 0, 0, 0),    // starts before the window
                new TypedDamageBucket(1, 100, 20, 30, 5, 1, 2, 3),
                new TypedDamageBucket(1, 108, 7, 0, 0, 0, 0, 0),    // 108-110: still running at 109
                new TypedDamageBucket(2, 100, 999, 0, 0, 0, 0, 0),  // another hero
            ],
            [new HealingBucket(1, 102, 50, 80)]);

        var window = CombatFacts.Window(activity, 1, 100, 109);

        Assert.Equal((20, 30, 5), (window.PhysicalDealt!.Value, window.MagicalDealt!.Value, window.PureDealt!.Value));
        Assert.Equal((1, 2, 3), (window.PhysicalTaken!.Value, window.MagicalTaken!.Value, window.PureTaken!.Value));
        Assert.Equal((50, 80), (window.HealingReceived!.Value, window.HealingDone!.Value));
        Assert.Equal(FactAvailability.Available, window.Damage);
    }

    [Fact]
    public void Window_SparseBucketsAreZero_MissingFamilyIsUnknown()
    {
        var empty = CombatFacts.Window(Activity([], null), 1, 0, 60);
        Assert.Equal(0, empty.PhysicalDealt);
        Assert.Null(empty.HealingReceived);
        Assert.Equal(FactAvailability.Unavailable, empty.Healing);

        var none = CombatFacts.Window(null, 1, 0, 60);
        Assert.Equal(FactAvailability.Unavailable, none.Damage);
        Assert.Null(none.PhysicalDealt);
    }

    [Fact]
    public void Window_RefusesAnAnchorBeforeTheStart()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatFacts.Window(null, 1, 60, 30));
    }

    [Fact]
    public void FinalHealing_SelfOnce_AlliesAndOthersApart()
    {
        var facts = new ReplayAdditionalFacts
        {
            Healing =
            [
                new HealingRow(1, "hero", 1, "hero", "item_magic_wand", 100, 1),      // self
                new HealingRow(1, "hero", 2, "hero", "witch_doctor_voodoo_restoration", 300, 30),
                new HealingRow(1, "hero", 2, "illusion", "witch_doctor_voodoo_restoration", 40, 4),
                new HealingRow(1, "hero", 6, "hero", "item_mekansm", 15, 1),          // an enemy (e.g. under a shared effect)
                new HealingRow(2, "hero", 1, "hero", "item_mekansm", 200, 1),         // from an ally
                new HealingRow(null, "buildings", 1, "hero", null, 50, 5),            // the fountain
            ],
        };

        var healing = CombatFacts.FinalHealing(facts, 1, new HashSet<int> { 1, 2, 3, 4, 5 });

        Assert.Equal(FactAvailability.Available, healing.Availability);
        Assert.Equal(100, healing.SelfHealing);
        Assert.Equal(300, healing.DoneToAllies);
        Assert.Equal(55, healing.DoneToOthers);
        Assert.Equal(250, healing.ReceivedFromOthers);
        Assert.Equal("witch_doctor_voodoo_restoration", healing.ByInflictor[0].Inflictor);
        Assert.Equal(340, healing.ByInflictor[0].Amount);
    }

    [Fact]
    public void FinalHealing_WithoutTheFamilyIsUnknown()
    {
        var healing = CombatFacts.FinalHealing(new ReplayAdditionalFacts(), 1, new HashSet<int>());

        Assert.Equal(FactAvailability.Unavailable, healing.Availability);
        Assert.Null(healing.SelfHealing);
    }
}
