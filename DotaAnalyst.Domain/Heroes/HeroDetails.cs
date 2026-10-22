using System.Collections.Frozen;

namespace DotaAnalyst.Domain.Heroes;

public enum HeroAttribute { Strength, Agility, Intelligence, Universal }

public enum HeroAttackType { Melee, Ranged }

public enum HeroRole { Carry, Support, Nuker, Disabler, Initiator, Escape, Durable, Pusher, Jungler }

// Draft facts belong to the catalog, independently of a hero's role in a particular match.
public record HeroDetails
{
    public HeroDetails(HeroAttribute primaryAttribute, HeroAttackType attackType, IReadOnlySet<HeroRole> roles)
    {
        if (!Enum.IsDefined(primaryAttribute))
        {
            throw new ArgumentOutOfRangeException(nameof(primaryAttribute));
        }
        if (!Enum.IsDefined(attackType))
        {
            throw new ArgumentOutOfRangeException(nameof(attackType));
        }
        ArgumentNullException.ThrowIfNull(roles);
        if (roles.Any(role => !Enum.IsDefined(role)))
        {
            throw new ArgumentException("Roles must be known hero roles.", nameof(roles));
        }

        PrimaryAttribute = primaryAttribute;
        AttackType = attackType;
        Roles = roles.ToFrozenSet();
    }

    public HeroAttribute PrimaryAttribute { get; }
    public HeroAttackType AttackType { get; }
    public IReadOnlySet<HeroRole> Roles { get; }

    public HeroProfile? Profile { get; init; }

    public HeroDescription? Description => HeroDescription.From(this);

    // Set order and collection identity do not change the hero's profile.
    public virtual bool Equals(HeroDetails? other) => other is not null
        && PrimaryAttribute == other.PrimaryAttribute && AttackType == other.AttackType
        && Roles.SetEquals(other.Roles) && Profile == other.Profile;

    public override int GetHashCode() => HashCode.Combine(PrimaryAttribute, AttackType,
        Roles.Aggregate(0, (hash, role) => hash ^ role.GetHashCode()), Profile);
}
