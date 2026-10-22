using System.Text.Json;
using DotaAnalyst.Domain.Heroes;

namespace DotaAnalyst.Tests.Domain;

public sealed class HeroProfileTests
{
    [Fact]
    public void NumericComponentsPreserveNegativeZeroAndOriginThroughJson()
    {
        var number = new HeroNumber(-1, HeroValueOrigin.HeroBase);
        Assert.Equal(number, JsonSerializer.Deserialize<HeroNumber>(JsonSerializer.Serialize(number)));
        Assert.NotEqual(number, new HeroNumber(-1, HeroValueOrigin.HeroFile));
        Assert.Equal(0, new HeroNumber(0, HeroValueOrigin.HeroFile).Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeroNumber(double.NaN, HeroValueOrigin.HeroFile));
    }

    [Fact]
    public void LegacyHeroDetailsKeepSetEqualityAndOptionalProfile()
    {
        var a = new HeroDetails(HeroAttribute.Strength, HeroAttackType.Melee, new HashSet<HeroRole> { HeroRole.Carry, HeroRole.Durable });
        var b = new HeroDetails(HeroAttribute.Strength, HeroAttackType.Melee, new HashSet<HeroRole> { HeroRole.Durable, HeroRole.Carry });
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Null(a.Profile);
    }

    [Fact]
    public void FullProfileRoundTripsAndParticipatesInEquality()
    {
        var profile = new HeroProfile("npc_dota_hero_test", true,
            new(new(25, HeroValueOrigin.HeroFile), null, null, null, null, null),
            new(null, null, null, null, new(1.45, HeroValueOrigin.HeroFile), null, null, null, null),
            new(new(-1, HeroValueOrigin.HeroBase), null), new(null, null, null, null), new(null, null, null, null));
        var copy = JsonSerializer.Deserialize<HeroProfile>(JsonSerializer.Serialize(profile));
        Assert.Equal(profile, copy);
        var legacy = new HeroDetails(HeroAttribute.Strength, HeroAttackType.Melee, new HashSet<HeroRole>());
        Assert.NotEqual(legacy, legacy with { Profile = profile });
        Assert.Equal(legacy with { Profile = profile }, legacy with { Profile = copy });
        var description = (legacy with { Profile = profile }).Description!;
        Assert.Equal("pt-BR", description.Language);
        Assert.Contains("1,45 s", description.Text);
        Assert.Contains("não informado", description.Text);
        Assert.Equal(description, (legacy with { Profile = copy }).Description);
    }
}
