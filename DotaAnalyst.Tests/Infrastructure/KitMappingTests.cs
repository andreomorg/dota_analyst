using System.Text.Json;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Infrastructure.OpenDota;

namespace DotaAnalyst.Tests.Infrastructure;

public sealed class KitMappingTests
{
    [Fact]
    public void LionAbilities_MapListsTargetsAndValues()
    {
        var source = JsonSerializer.Deserialize<Dictionary<string, OpenDotaAbility>>(KitSamples.Abilities, OpenDotaClient.JsonOptions)!;
        var impale = source["lion_impale"].ToDetails("lion_impale");
        Assert.Equal("Earth Spike", impale.Name);
        Assert.Equal(["Point Target", "Unit Target"], impale.Behavior);
        Assert.Equal(AbilityTargetTeam.Enemy, impale.TargetTeam);
        Assert.Equal(["Hero", "Basic"], impale.TargetTypes);
        Assert.Equal(AbilityDamageType.Magical, impale.DamageType);
        Assert.False(impale.PiercesSpellImmunity);
        Assert.Equal(Dispellable.StrongOnly, impale.Dispellable);
        Assert.Equal([14d, 13, 12, 11], impale.Cooldown);
        Assert.Equal([90d, 110, 130, 150], impale.ManaCost);
        Assert.Equal([105d, 170, 235, 300], impale.Value("damage")!.Levels);
        var hex = source["lion_voodoo"].ToDetails("lion_voodoo");
        Assert.Equal(["Unit Target"], hex.Behavior);
        Assert.Equal([2d, 2.4, 2.8, 3.2], hex.Value("duration")!.Levels);
        Assert.Equal(AbilityDamageType.None, hex.DamageType);
    }

    [Fact]
    public void LionKit_PreservesOrderTalentTiersAndDeprecatedFacets()
    {
        var kits = JsonSerializer.Deserialize<Dictionary<string, OpenDotaHeroKit>>(KitSamples.Kits, OpenDotaClient.JsonOptions)!;
        var kit = kits["npc_dota_hero_lion"].ToKit(26);
        Assert.Equal(26, kit.HeroId);
        Assert.Equal("lion_impale", kit.Abilities[0]);
        Assert.DoesNotContain("generic_hidden", kit.Abilities);
        Assert.Equal(5, kit.Abilities.Count);
        Assert.Equal(new Talent("special_bonus_unique_lion_6", 1), kit.Talents[0]);
        Assert.Equal(8, kit.Talents.Count);
        Assert.Equal("Essence Eater", kit.Facets[0].Title);
        Assert.All(kit.Facets, f => Assert.True(f.Deprecated));
    }

    [Fact]
    public void Items_MapNewFieldsWithoutChangingBuildData()
    {
        var source = JsonSerializer.Deserialize<Dictionary<string, OpenDotaItem>>(KitSamples.Items, OpenDotaClient.JsonOptions)!;
        var items = OpenDotaMappings.ToItems(new Dictionary<int, string> { [116] = "black_king_bar", [1] = "blink" }, source);
        var bkb = items[116].Details!;
        Assert.Equal(4050, bkb.Cost);
        Assert.Equal(["mithril_hammer", "ogre_axe"], bkb.Components);
        Assert.Equal(ItemAbilityKind.Active, Assert.Single(bkb.Abilities).Kind);
        Assert.Equal("Avatar", bkb.Abilities[0].Title);
        Assert.Equal(["Instant Cast", "No Target"], bkb.Behavior);
        Assert.Equal(95, bkb.Cooldown);
        Assert.Equal(50, bkb.ManaCost);
        Assert.Equal([9d, 8, 7], bkb.Values.Single(v => v.Key == "duration").Levels);
        Assert.Equal("+ {value} Strength", bkb.Values[0].Header);
        Assert.False(bkb.HasCharges);
        Assert.Null(items[1].Details!.ManaCost);
        Assert.Equal(15, items[1].Details!.Cooldown);
        Assert.Equal(["Point Target"], items[1].Details!.Behavior);
    }

    [Fact]
    public void OddFields_AreLocalUnknownsAndTextIsPreserved()
    {
        using var json = JsonDocument.Parse("""
        {"behavior":42,"target_team":{"unexpected":true},"target_type":false,"dmg_type":"Future",
         "bkbpierce":[],"dispellable":"Future","cd":["1","oops"],"mc":17,
         "attrib":[{"key":"text","value":"Depends on distance"},{"key":"bad","value":["1","oops"]},
                   {"key":"levels","value":"9 / 8 / 7"}]}
        """);
        var a = OpenDotaKitMappings.ToAbility("odd", json.RootElement);
        Assert.Empty(a.Behavior);
        Assert.Empty(a.TargetTypes);
        Assert.Equal(AbilityTargetTeam.None, a.TargetTeam);
        Assert.Equal(AbilityDamageType.None, a.DamageType);
        Assert.Null(a.PiercesSpellImmunity);
        Assert.Equal(Dispellable.Unknown, a.Dispellable);
        Assert.Empty(a.Cooldown);
        Assert.Equal([17d], a.ManaCost);
        Assert.Equal("Depends on distance", a.Value("text")!.Text);
        Assert.Empty(a.Value("bad")!.Levels);
        Assert.Equal([9d, 8, 7], a.Value("levels")!.Levels);
    }

    [Theory]
    [InlineData("Friendly", AbilityTargetTeam.Ally)]
    [InlineData("Both", AbilityTargetTeam.Both)]
    [InlineData("Custom", AbilityTargetTeam.Custom)]
    public void TargetTeams_Map(string team, AbilityTargetTeam expected)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { target_team = team }));
        Assert.Equal(expected, OpenDotaKitMappings.ToAbility("a", json.RootElement).TargetTeam);
    }

    [Fact]
    public void CombinedTeams_AndEmptyKitEntries()
    {
        using var json = JsonDocument.Parse("""{"target_team":["Enemy","Friendly"],"abilities":["","generic_hidden","lion_impale"]}""");
        Assert.Equal(AbilityTargetTeam.Both, OpenDotaKitMappings.ToAbility("a", json.RootElement).TargetTeam);
        Assert.Equal(["lion_impale"], OpenDotaKitMappings.ToKit(26, json.RootElement).Abilities);
    }
}
