using DotaAnalyst.Domain.Heroes;

namespace DotaAnalyst.Tests.Domain;

public class DraftProfileTests
{
    [Fact]
    public void CountsOverlappingRolesAndIgnoresUnknownProfiles()
    {
        var profile = DraftProfile.From([
            new HeroDetails(HeroAttribute.Universal, HeroAttackType.Melee, new HashSet<HeroRole> { HeroRole.Carry, HeroRole.Escape }),
            new HeroDetails(HeroAttribute.Agility, HeroAttackType.Ranged, new HashSet<HeroRole> { HeroRole.Carry }),
            null,
        ]);

        Assert.Equal(2, profile.Roles[HeroRole.Carry]);
        Assert.Equal(1, profile.Roles[HeroRole.Escape]);
        Assert.Equal(0, profile.Roles[HeroRole.Support]);
        Assert.Equal(1, profile.AttackTypes[HeroAttackType.Melee]);
        Assert.Equal(1, profile.AttackTypes[HeroAttackType.Ranged]);
        Assert.Equal(1, profile.Attributes[HeroAttribute.Universal]);
        Assert.Equal(1, profile.Attributes[HeroAttribute.Agility]);
        Assert.Equal(2, profile.Attributes.Values.Sum());
    }
}
