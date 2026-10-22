namespace DotaAnalyst.Domain.Replays;

// One position of a hero: game clock and map coordinates (like HeroDeath: about -8200..8200, Radiant bottom-left), and
// its health and mana at that moment when the replay document has them (newer documents only). Facing: the hero's yaw in
// degrees (0 = towards +x, counterclockwise, like the game's angles), null when unknown.
public readonly record struct PositionSample(
    int Seconds, int X, int Y, HeroVitals? Vitals = null, HeroCombatStats? Stats = null, HeroOrders? Orders = null,
    int? Facing = null, HeroCombatDetails? CombatDetails = null);

// Replicated components, not reconstructed effective HUD totals. Think regen is not the combat-log HEAL sum.
public sealed record HeroCombatDetails(double? StrengthBase, double? AgilityBase, double? IntellectBase,
    double? DamageBaseMin, double? DamageBaseMax, double? DamageBonus, double? BaseAttackTime,
    double? AttackRange, int? AttackCapability, double? HealthThinkRegen, double? ManaThinkRegen);

// The orders the player issued since the hero's previous sample (newer documents): how many move, attack and cast
// orders (for the hero and the units it controls; issued, not necessarily done) and where the last "move to position"
// pointed (null: none). It is the player's intent, which the position alone does not say.
public sealed record HeroOrders(int? MoveX, int? MoveY, int Moves, int Attacks, int Casts);

// A hero's combat attributes at one position sample (newer documents); each null when the replay lacks it. Armor, magic
// resistance (percent) and move speed are the replicated base values, without items and buffs (checked on a replay:
// armor 0 for a hero with 94 agility); damage and the three attributes include bonuses.
public sealed record HeroCombatStats(
    double? Armor, double? MagicResistance, int? MoveSpeed, int? DamageMin, int? DamageMax, double? Strength,
    double? Agility, double? Intellect)
{
    // Each point of agility gives about 1/6 armor: added to the base armor (still without items or buffs).
    private const double ArmorPerAgility = 1.0 / 6;

    public double? EstimatedArmor => Armor is { } armor && Agility is { } agility ? armor + agility * ArmorPerAgility : null;

    public double? AverageDamage => DamageMin is { } min && DamageMax is { } max ? (min + max) / 2.0 : null;
}

// A hero's state at one position sample; each null when the replay did not have it. VisibleToEnemies: the enemy team saw
// the hero (the game's own fog of war). UnitStateMask: the game's unit states (DOTA modifier states, one bit each). Gold:
// gold in hand.
public sealed record HeroVitals(
    int? Health, int? MaxHealth, int? Mana, int? MaxMana, bool? VisibleToEnemies = null, int? UnitStateMask = null,
    int? Gold = null)
{
    // Bits of the game's modifier states. Invisible was checked on a replay (Riki, permanently invisible, has it in
    // 330 of its samples); silenced and stunned follow the same enum and are plausible, not checked one by one.
    private const int SilencedBit = 1 << 3;
    private const int StunnedBit = 1 << 5;
    private const int InvisibleBit = 1 << 7;

    public bool? Silenced => UnitStateMask is { } mask ? (mask & SilencedBit) != 0 : null;

    public bool? Stunned => UnitStateMask is { } mask ? (mask & StunnedBit) != 0 : null;

    public bool? Invisible => UnitStateMask is { } mask ? (mask & InvisibleBit) != 0 : null;

    // 0..1 of the maximum; null when either is unknown.
    public double? HealthShare => Share(Health, MaxHealth);

    public double? ManaShare => Share(Mana, MaxMana);

    private static double? Share(int? value, int? max) =>
        value is { } part && max is > 0 and { } whole ? Math.Clamp((double)part / whole, 0, 1) : null;
}

// Where one hero was during the match, read from the replay: a sample every few seconds while it was alive (a dead
// hero has none). Used to compute the metrics in the parse and never saved: it is most of the replay's data.
public sealed class HeroPositions
{
    private readonly Dictionary<int, PositionSample> bySecond;

    public HeroPositions(int heroId, IReadOnlyList<PositionSample> samples)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heroId);
        ArgumentNullException.ThrowIfNull(samples);

        HeroId = heroId;
        Samples = samples.OrderBy(sample => sample.Seconds).ToList();
        // The first sample of a second wins (a second never has two in practice).
        bySecond = Samples.DistinctBy(sample => sample.Seconds).ToDictionary(sample => sample.Seconds);
    }

    public int HeroId { get; }

    public IReadOnlyList<PositionSample> Samples { get; }

    // Where the hero was around this second (the closest sample at most `tolerance` seconds away); null when it was
    // dead (or not sampled) then.
    public PositionSample? At(int seconds, int tolerance)
    {
        for (var offset = 0; offset <= tolerance; offset++)
        {
            if (bySecond.TryGetValue(seconds - offset, out var before))
            {
                return before;
            }

            if (bySecond.TryGetValue(seconds + offset, out var after))
            {
                return after;
            }
        }

        return null;
    }
}
