using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Items;

namespace DotaAnalyst.Tests.Domain;

// Excerpts of the OpenDota constants of patch 7.41 (work/agents/samples/).
public class AbilityEffectsTests
{
    private static AbilityDetails Ability(
        string key, string description, string[] behavior, AbilityTargetTeam team, AbilityDamageType damage, params (string Header, double[] Levels)[] values) =>
        new(key, key, description, behavior, team, ["Hero", "Basic"], damage, false, Dispellable.StrongOnly, [14, 13, 12, 11], [90, 110, 130, 150],
            values.Select(value => new AbilityValue(value.Header.ToLowerInvariant().Replace(' ', '_'), value.Header + ":", value.Levels)).ToList());

    [Fact]
    public void EarthSpike_StunsAndDamages()
    {
        var impale = Ability("lion_impale",
            "Rock spikes burst from the earth along a straight path. Enemy units are hurled into the air, then are stunned and take damage when they fall.",
            ["Point Target", "Unit Target"], AbilityTargetTeam.Enemy, AbilityDamageType.Magical,
            ("DAMAGE", [105, 170, 235, 300]), ("STUN DURATION", [1.3, 1.6, 1.9, 2.2]));

        Assert.Equal(new HashSet<AbilityEffect> { AbilityEffect.Stun, AbilityEffect.Nuke }, AbilityEffects.Of(impale));
        Assert.True(AbilityEffects.IsDisable(AbilityEffect.Stun));
    }

    [Fact]
    public void Hex_IsKnownByTheKeyWhenTheTextSaysCritter()
    {
        var voodoo = Ability("lion_voodoo", "Transforms an enemy unit into a harmless critter, disabling all of its abilities.",
            ["Unit Target"], AbilityTargetTeam.Enemy, AbilityDamageType.None, ("DURATION", [2.5, 3, 3.5, 4]));

        Assert.Contains(AbilityEffect.Hex, AbilityEffects.Of(voodoo));
        Assert.DoesNotContain(AbilityEffect.Nuke, AbilityEffects.Of(voodoo));
    }

    [Fact]
    public void APassiveWithDamage_IsNotANuke_AndResistancesAreNotDebuffs()
    {
        var passive = Ability("some_passive", "Grants bonus damage. Does not work on illusions.", ["Passive"], AbilityTargetTeam.None,
            AbilityDamageType.Physical, ("BONUS DAMAGE", [10, 20, 30, 40]), ("SLOW RESISTANCE", [10, 20, 30, 40]));

        Assert.Empty(AbilityEffects.Of(passive));
    }

    [Fact]
    public void Items_ReadTheActiveText_AndTheKnownList()
    {
        var bkb = new ItemDetails("black_king_bar", 4050, ["mithril_hammer", "ogre_axe"], false)
        {
            Abilities = [new ItemAbility(ItemAbilityKind.Active, "Avatar",
                "Applies a basic dispel. Grants 60% magic resistance and immunity to reflected and pure damage.")],
            Behavior = ["Instant Cast", "No Target"],
            Cooldown = 95,
            ManaCost = 50,
        };
        var blink = new ItemDetails("blink", 2250, null, false)
        {
            Abilities = [new ItemAbility(ItemAbilityKind.Active, "Blink", "Teleport to a target point up to 1200 units away.")],
        };

        Assert.Equal(new HashSet<AbilityEffect> { AbilityEffect.SpellImmunity, AbilityEffect.Dispel }, AbilityEffects.Of(bkb));
        Assert.Contains(AbilityEffect.Escape, AbilityEffects.Of(blink));
        Assert.True(bkb.HasActive);
    }

    [Fact]
    public void AbilityValue_ReadsTheLevel_TheLastOneBeyondTheList()
    {
        var value = new AbilityValue("duration", "STUN DURATION:", [1.3, 1.6, 1.9, 2.2]);

        Assert.Equal((1.3, 2.2, 2.2), (value.At(1)!.Value, value.At(4)!.Value, value.At(9)!.Value));
        Assert.Null(new AbilityValue("text", null, [], "Yes").At(1));
    }
}
