namespace DotaAnalyst.Domain.Heroes;

// Unknown profiles contribute no counts; missing catalog data must not look like a draft fact.
public record DraftProfile(
    IReadOnlyDictionary<HeroRole, int> Roles,
    IReadOnlyDictionary<HeroAttackType, int> AttackTypes,
    IReadOnlyDictionary<HeroAttribute, int> Attributes)
{
    public static DraftProfile From(IEnumerable<HeroDetails?> profiles)
    {
        var known = profiles.OfType<HeroDetails>().ToList();
        return new(
            Enum.GetValues<HeroRole>().ToDictionary(role => role, role => known.Count(hero => hero.Roles.Contains(role))),
            Enum.GetValues<HeroAttackType>().ToDictionary(type => type, type => known.Count(hero => hero.AttackType == type)),
            Enum.GetValues<HeroAttribute>().ToDictionary(attribute => attribute, attribute => known.Count(hero => hero.PrimaryAttribute == attribute)));
    }
}
