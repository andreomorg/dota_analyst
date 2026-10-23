namespace DotaAnalyst.Domain.Heroes;

// A number from the hero's configuration, not an effective HUD total. Null at the containing property means unknown.
public enum HeroValueOrigin { HeroFile, HeroBase }

public sealed record HeroNumber
{
    public HeroNumber(double value, HeroValueOrigin origin)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (!Enum.IsDefined(origin)) throw new ArgumentOutOfRangeException(nameof(origin));
        Value = value;
        Origin = origin;
    }

    public double Value { get; }
    public HeroValueOrigin Origin { get; }
}

public sealed record HeroAttributes(HeroNumber? Strength, HeroNumber? StrengthGain, HeroNumber? Agility,
    HeroNumber? AgilityGain, HeroNumber? Intelligence, HeroNumber? IntelligenceGain);

// BAT and attack point are seconds; speed/range are configuration values, never measured attacks per second.
public sealed record HeroAttack(HeroNumber? DamageMin, HeroNumber? DamageMax, string? DamageType,
    HeroNumber? BaseAttackSpeed, HeroNumber? BaseAttackTime, HeroNumber? AttackPoint, HeroNumber? Range,
    HeroNumber? RangeBuffer, HeroNumber? ProjectileSpeed);

// Magic resistance is percent (25, not .25). Health/mana are base components, not level-one totals.
public sealed record HeroDefense(HeroNumber? Armor, HeroNumber? MagicResistance);
public sealed record HeroBaseVitals(HeroNumber? Health, HeroNumber? Mana, HeroNumber? HealthRegen, HeroNumber? ManaRegen);
public sealed record HeroMovement(HeroNumber? Speed, HeroNumber? TurnRate, HeroNumber? DayVision, HeroNumber? NightVision);

// Build identity belongs to the catalog envelope. This value is immutable and compares by content, including origins.
public sealed record HeroProfile(string Key, bool? Enabled, HeroAttributes Attributes, HeroAttack Attack,
    HeroDefense Defense, HeroBaseVitals Vitals, HeroMovement Movement)
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Instance property is part of the serialized JSON contract.")]
    public int SchemaVersion => 1;

    public bool HasValidShape() => !string.IsNullOrWhiteSpace(Key) && Attributes is not null && Attack is not null
        && Defense is not null && Vitals is not null && Movement is not null;
}
