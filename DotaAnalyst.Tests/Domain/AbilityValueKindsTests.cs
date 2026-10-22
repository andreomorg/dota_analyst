using DotaAnalyst.Domain.Abilities;

namespace DotaAnalyst.Tests.Domain;

// Headers and keys copied from the 7.41 constants (OpenDota).
public sealed class AbilityValueKindsTests
{
    [Theory]
    [InlineData("damage", "DAMAGE:", AbilityValueKind.Damage)]
    [InlineData("damage_per_second", "DAMAGE PER SECOND:", AbilityValueKind.Damage)]
    [InlineData("bonus_damage", "BONUS DAMAGE:", AbilityValueKind.Damage)]
    [InlineData("max_health_damage", "MAX HEALTH DAMAGE:", AbilityValueKind.Damage)]
    [InlineData("heal", "HEAL:", AbilityValueKind.Healing)]
    [InlineData("lifesteal_percent", "LIFESTEAL:", AbilityValueKind.Healing)]
    [InlineData("hp_regen", "HP REGEN:", AbilityValueKind.Healing)]
    [InlineData("max_hp_restore", "MAX HP RESTORE:", AbilityValueKind.Healing)]
    [InlineData("health_cost", "HEALTH COST:", AbilityValueKind.HealthCost)]
    [InlineData("self_damage", "SELF DAMAGE:", AbilityValueKind.HealthCost)]
    [InlineData("hp_cost_per_second", "HP PER SECOND COST/DAMAGE:", AbilityValueKind.HealthCost)]
    [InlineData("tooltip_health_cost_percent", "DAMAGE TAKEN:", AbilityValueKind.HealthCost)]
    [InlineData("bonus_health", "BONUS HEALTH:", AbilityValueKind.Other)]
    [InlineData("heal_amp", "HEAL AMPLIFICATION:", AbilityValueKind.Other)]
    [InlineData("damage_reduction", "DAMAGE REDUCTION:", AbilityValueKind.Other)]
    [InlineData("damage_absorb", "DAMAGE ABSORB:", AbilityValueKind.Other)]
    [InlineData("tick_rate", "DAMAGE TICK RATE:", AbilityValueKind.Other)]
    [InlineData("illusion_damage", "ILLUSION DAMAGE:", AbilityValueKind.Other)]
    [InlineData("damage_window", "DAMAGE WINDOW:", AbilityValueKind.Other)]
    [InlineData("charge_restore_time", "CHARGE RESTORE TIME:", AbilityValueKind.Other)]
    [InlineData("duration", "STUN DURATION:", AbilityValueKind.Other)]
    public void Headers_AreClassified(string key, string header, AbilityValueKind expected) =>
        Assert.Equal(expected, new AbilityValue(key, header, [1]).Kind);

    [Fact]
    public void WithoutHeader_TheKeyIsRead()
    {
        Assert.Equal(AbilityValueKind.Damage, new AbilityValue("burn_damage", null, [10]).Kind);
        Assert.Equal(AbilityValueKind.Healing, new AbilityValue("heal_per_second", null, [10]).Kind);
    }

    [Fact]
    public void Ability_GroupsItsNumbersByKind()
    {
        var ability = new AbilityDetails("huskar_inner_fire", "Inner Fire", null, ["No Target"], AbilityTargetTeam.Enemy,
            [], AbilityDamageType.Magical, false, Dispellable.Yes, [17, 15, 13, 11], [],
            [new("damage", "DAMAGE:", [100, 170, 240, 310]), new("health_cost", "HEALTH COST:", [10], IsPercent: true),
             new("radius", "RADIUS:", [500])]);
        Assert.Equal("damage", Assert.Single(ability.ValuesOf(AbilityValueKind.Damage)).Key);
        Assert.Equal("health_cost", Assert.Single(ability.ValuesOf(AbilityValueKind.HealthCost)).Key);
        Assert.Empty(ability.ValuesOf(AbilityValueKind.Healing));
        Assert.Null(ability.ManaCostAt(1));
    }
}
