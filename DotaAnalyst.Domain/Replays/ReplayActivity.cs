using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Replays;

// A recorded activation (item or ability) with the replay's own time, not rounded: rounding 100.4 to 100 would let a use
// after a second leak into it. It says something was used, not that it was ready, hit, or worked.
// TargetHeroId/TargetX/TargetY (abilities only, X27): the hero it was cast on and the point it was cast at, when the
// replay links them (null: unknown, not "no target").
public sealed record RecordedUse(int HeroId, double Seconds, string Name, int? TargetHeroId = null, int? TargetX = null, int? TargetY = null);

public enum EconomyResource
{
    Gold,
    Experience,
}

// What a gold/XP change came from, grouped from Valve's raw reason (EDOTA_ModifyGold_Reason / ModifyXP_Reason): only
// the reasons whose meaning is stable across patches; the rest is Other.
public enum EconomySource
{
    Creeps,
    Neutrals,
    Heroes,
    Objectives,
    BountyRune,
    Death,
    Other,
}

// Gold or XP a hero got (or lost: negative) for one reason in the EconomyBucket.Length seconds starting at Seconds.
public sealed record EconomyBucket(int HeroId, int Seconds, EconomyResource Resource, EconomySource Source, int Amount)
{
    public const int Length = 2;

    public bool EndsBy(int second) => Seconds + Length <= second;

    // Gold: 1 death, 11 building, 12 hero kill, 13 creep, 14 neutral, 15 Roshan, 17 bounty rune. XP: 1 hero, 2 creep,
    // 3 Roshan. Neutral creeps give XP as creeps (reason 2), so XP has no Neutrals.
    public static EconomySource SourceOf(EconomyResource resource, int? reason) => (resource, reason) switch
    {
        (EconomyResource.Gold, 1) => EconomySource.Death,
        (EconomyResource.Gold, 11) or (EconomyResource.Gold, 15) => EconomySource.Objectives,
        (EconomyResource.Gold, 12) => EconomySource.Heroes,
        (EconomyResource.Gold, 13) => EconomySource.Creeps,
        (EconomyResource.Gold, 14) => EconomySource.Neutrals,
        (EconomyResource.Gold, 17) => EconomySource.BountyRune,
        (EconomyResource.Experience, 1) => EconomySource.Heroes,
        (EconomyResource.Experience, 2) => EconomySource.Creeps,
        (EconomyResource.Experience, 3) => EconomySource.Objectives,
        _ => EconomySource.Other,
    };
}

// Hero damage of one hero by damage type in the DamageBucket.Length seconds starting at Seconds (X30; damage without a
// type only counts in DamageBucket), and the healing it received and did then (self-healing included).
public sealed record TypedDamageBucket(
    int HeroId, int Seconds, int PhysicalTo, int MagicalTo, int PureTo, int PhysicalFrom, int MagicalFrom, int PureFrom)
{
    public bool EndsBy(int second) => Seconds + DamageBucket.Length <= second;
}

public sealed record HealingBucket(int HeroId, int Seconds, int Received, int Done)
{
    public bool EndsBy(int second) => Seconds + DamageBucket.Length <= second;
}

// One courier of a team over the match (sparse readings: alive, position).
public sealed record CourierTrack(string Id, Team Team, IReadOnlyList<(int Seconds, bool Alive, int X, int Y)> Samples)
{
    // Alive at the last reading up to `second`; null before the first.
    public bool? AliveAt(int second)
    {
        bool? alive = null;
        foreach (var sample in Samples)
        {
            if (sample.Seconds > second)
            {
                break;
            }

            alive = sample.Alive;
        }

        return alive;
    }
}

// A ping on the map by a player (raw type kept by the parser), at a point.
public sealed record MapPing(int HeroId, Team Team, double Seconds, int X, int Y, int Type);

// A rune that appeared (Present) or vanished (!Present) on the map, read every 2 s (RunePickup.IsPower's types).
public sealed record RuneOnMap(int Seconds, int RuneType, int X, int Y, bool Present);

// An inventory slot of a hero that changed at a second of the game clock (read every 2 s); Item null = it emptied.
// Slots: 0-5 the inventory, TeleportSlot and NeutralSlot.
public sealed record SlotChange(int HeroId, int Seconds, int Slot, string? Item)
{
    public const int TeleportSlot = 15;
    public const int NeutralSlot = 16;
    public const int InventorySlots = 6;
}

// A cooldown of a hero's item or ability that started at Seconds and ends at ReadyAt (the replay's own times).
public sealed record CooldownStart(int HeroId, double Seconds, string Name, double ReadyAt)
{
    public bool ActiveAt(int second) => Seconds <= second && ReadyAt > second;

    public double Length => ReadyAt - Seconds;
}

// Something a team did at a moment: a scan, a glyph.
public sealed record TeamAction(Team Team, double Seconds);

// How far each team's lane creeps reached in a lane at a second (sampled every 2 s): the progress x + y of the creep
// furthest towards the enemy (it grows from the Radiant base to the Dire base along every lane). Null: none there.
public sealed record WaveFront(
    Lane Lane, int Seconds, int? Radiant, int? Dire, WaveCounts? Counts = null, WaveStrength? Strength = null,
    WaveComposition? Composition = null);

// One team's lane creeps in the lane by type (disjoint: melee, ranged, siege, flagbearer, super, mega) and how many of
// the front ones have at most a quarter of their health (last hits to take, or denies).
public sealed record TeamWave(int Melee, int Ranged, int Siege, int Flagbearer, int Super, int Mega, int LowAtFront);

// Both teams' wave composition in a lane at a second (W5). Null on a WaveFront: a document from before it.
public sealed record WaveComposition(TeamWave Radiant, TeamWave Dire);

// Lane creep hits on a hero in the 2 s ending at Seconds (the combat log: hits, not distinct creeps nor their intent).
public sealed record CreepHeroHits(int Seconds, Lane Lane, Team CreepTeam, int TargetHeroId, int Hits);

// A neutral camp (its spawner's position and type) and its living wild neutrals over the match (sparse readings).
public sealed record NeutralCamp(string Id, int X, int Y, string? Type, IReadOnlyList<(int Seconds, int Alive)> Alive)
{
    // The last reading up to `second` (sorted by time); null before the first.
    public int? AliveAt(int second)
    {
        int? alive = null;
        foreach (var reading in Alive)
        {
            if (reading.Seconds > second)
            {
                break;
            }

            alive = reading.Alive;
        }

        return alive;
    }

    // A stacked camp: at least 1.5 × its most common count so far (neutrals spawn as one group; a stack adds another).
    public bool StackedAt(int second)
    {
        var seen = Alive.Where(reading => reading.Seconds <= second && reading.Alive > 0).ToList();
        if (seen.Count == 0 || AliveAt(second) is not { } now)
        {
            return false;
        }

        var usual = seen.GroupBy(reading => reading.Alive).MaxBy(group => (group.Count(), -group.Key))!.Key;
        return now >= 1.5 * usual;
    }
}

// How strong each team's wave in the lane is at that second: the creeps' summed health in the lane and at the front
// (the same creeps WaveCounts counts there), whether a siege creep is alive in the lane, and the mean x + y of the front
// creeps (null when the team has none). Null on a WaveFront: a document from before W1.
public sealed record WaveStrength(
    int RadiantHealth, int DireHealth, int RadiantHealthAtFront, int DireHealthAtFront, bool RadiantSiege, bool DireSiege,
    int? RadiantCenter, int? DireCenter);

// Lane creeps outside every lane at a second (pulled into the jungle or dragged away), per team, and where they are on
// average (null when the team has none).
public sealed record OffLaneCreeps(int Seconds, int RadiantCount, int DireCount, int? RadiantX, int? RadiantY, int? DireX, int? DireY);

public enum CreepKiller
{
    Hero,
    Creep,
    Tower,
    Neutral,
    Other,
}

// A lane creep that died: when, in which lane (null in the jungle), whose it was, what killed it (a hero's own units and
// illusions count as Hero; a deny is a Hero of the creep's team) and where (null when never sampled).
public sealed record LaneCreepDeath(double Seconds, Lane? Lane, Team CreepTeam, CreepKiller Killer, int? KillerHeroId, int? X, int? Y);

// The living lane creeps of each team in the lane at that second, and how many of them were within 1,200 of the team's
// leading creep (the wave that is pushing). Null on a WaveFront: a document from before the counts.
public sealed record WaveCounts(int RadiantInLane, int DireInLane, int RadiantAtFront, int DireAtFront);

// From Seconds on, it is day (IsDay) or night (a temporary night counts as night).
public sealed record DayNightChange(int Seconds, bool IsDay);

// The damage one hero (its illusions and summons too) dealt to an enemy hero in the DamageBucket.Length seconds starting
// at Seconds (it covers [Seconds, Seconds + Length)).
public sealed record HeroHit(int Seconds, int AttackerHeroId, int TargetHeroId, int Amount)
{
    public bool EndsBy(int second) => Seconds + DamageBucket.Length <= second;
}

public enum CreepKillKind
{
    Lane,
    Deny,
    Neutral,
}

// A lane creep or neutral a hero got the last hit on, where the hero was then (null: not on the map).
public sealed record CreepKill(int HeroId, double Seconds, CreepKillKind Kind, int? X, int? Y);

// What a debuff does to its target. Disables (stun, hex, silence, root) take control away.
public enum DebuffKind
{
    Stun,
    Hex,
    Silence,
    Root,
    Slow,
    DamageOverTime,
    ArmorReduction,
    Other,
}

// A modifier an enemy player's unit put on a hero, from Start to End (the replay's own times; End null when the replay
// does not say). SourceHeroId null: the replay does not say who.
public sealed record Debuff(double Start, double? End, int? SourceHeroId, int TargetHeroId, string Modifier, DebuffKind Kind)
{
    public bool IsDisable => Kind is DebuffKind.Stun or DebuffKind.Hex or DebuffKind.Silence or DebuffKind.Root;

    // On the hero at this second: started and not removed yet. Only "not removed yet" is read from End, never when it
    // ends (the future); a debuff without an end counts only in the second it started.
    public bool ActiveAt(int second) => Start <= second && (End is { } end ? end > second : second - Start < 1);
}

// What a buff the hero or an ally put on a hero does for it.
public enum BuffKind
{
    Teleport,
    Smoke,
    Invisible,
    MagicImmune,
    // Untargetable or protected from death for a moment (Glimmer fade, Eul's, Aeon Disk, Shallow Grave...).
    Save,
    Heal,
    Speed,
    Other,
}

// A modifier the hero itself or an allied player's unit put on a hero, from Start to End (same reading as Debuff).
public sealed record Buff(double Start, double? End, int? SourceHeroId, int TargetHeroId, string Modifier, BuffKind Kind)
{
    // Put by another hero of the team (null source: the replay does not say, so not an ally's).
    public bool FromAlly => SourceHeroId is { } source && source != TargetHeroId;

    public bool ActiveAt(int second) => Start <= second && (End is { } end ? end > second : second - Start < 1);
}

public enum BuildingKind
{
    Tower,
    Barracks,
    Ancient,
}

// One reading of a building's health on the game clock (the parser writes one only when it changes).
public sealed record BuildingHealthReading(int Seconds, int Health, int MaxHealth);

// A tower, barracks or Ancient where it stands, its owner and its health over the match (sorted, ending at 0 when it
// fell). Lane is null for the Ancient and tier-4 towers; Tier is null outside towers.
public sealed record BuildingHealth(
    string Building, Team Team, BuildingKind Kind, Lane? Lane, int? Tier, int X, int Y,
    IReadOnlyList<BuildingHealthReading> Readings)
{
    // The last reading up to `second` (never a later one); null before the first.
    public BuildingHealthReading? At(int second) => Readings.LastOrDefault(reading => reading.Seconds <= second);

    public bool StandingAt(int second) => At(second) is { Health: > 0 };
}

// A buyback of a hero.
public sealed record BuybackUse(int HeroId, double Seconds);

// A rune a hero picked up; RuneType is Valve's DOTA_RUNES number (replay-parser collect_vision.go).
public sealed record RunePickup(int HeroId, Team Team, double Seconds, int RuneType)
{
    // Double damage, haste, illusion, invisibility, regeneration, arcane, shield: the ones that change a fight.
    private static readonly HashSet<int> PowerRunes = [0, 1, 2, 3, 4, 6, 9];

    public bool IsPower => IsPowerType(RuneType);

    public static bool IsPowerType(int runeType) => PowerRunes.Contains(runeType);
}

// A Smoke of Deceit used, and the team of the hero who used it.
public sealed record SmokeUse(int HeroId, Team Team, double Seconds);

// Hero damage of one hero in the DamageBucket.Length seconds starting at Seconds (it covers [Seconds, Seconds + Length)):
// dealt to enemy heroes and taken from them.
public sealed record DamageBucket(int HeroId, int Seconds, int ToHeroes, int FromHeroes)
{
    public const int Length = 2;

    // Over by `second`: only these may describe that second without looking ahead.
    public bool EndsBy(int second) => Seconds + Length <= second;
}

// What each hero did during the match, read from the kept replay document (items.uses, abilities.uses, vision.smokes).
// Like the positions, it is never saved with the analysis: only the prediction models' samples read it.
public sealed record ReplayActivity
{
    // damageBuckets: null for documents read before combat buckets existed (unknown, not "no damage").
    public ReplayActivity(
        IReadOnlyList<SmokeUse> smokes, IReadOnlyList<RecordedUse> itemUses, IReadOnlyList<RecordedUse> abilityUses,
        IReadOnlyList<DamageBucket>? damageBuckets = null,
        IReadOnlyList<SlotChange>? slotChanges = null,
        IReadOnlyList<CooldownStart>? itemCooldowns = null,
        IReadOnlyList<CooldownStart>? abilityCooldowns = null,
        IReadOnlyList<BuybackUse>? buybacks = null,
        IReadOnlyList<TeamAction>? scans = null,
        IReadOnlyList<TeamAction>? glyphs = null,
        IReadOnlyList<RunePickup>? runes = null,
        IReadOnlyList<WaveFront>? waves = null,
        IReadOnlyList<DayNightChange>? dayNight = null,
        IReadOnlyList<HeroHit>? heroHits = null,
        IReadOnlyList<CreepKill>? creepKills = null,
        IReadOnlyList<Debuff>? debuffs = null,
        IReadOnlyList<Buff>? buffs = null,
        IReadOnlyList<BuildingHealth>? buildings = null,
        IReadOnlyList<OffLaneCreeps>? offLaneCreeps = null,
        IReadOnlyList<LaneCreepDeath>? laneCreepDeaths = null,
        IReadOnlyList<CreepHeroHits>? creepHeroHits = null,
        IReadOnlyList<NeutralCamp>? neutralCamps = null,
        IReadOnlyList<EconomyBucket>? economy = null,
        IReadOnlyList<MapPing>? pings = null,
        IReadOnlyList<RuneOnMap>? runesOnMap = null,
        IReadOnlyList<TypedDamageBucket>? typedDamage = null,
        IReadOnlyList<HealingBucket>? healing = null,
        IReadOnlyList<CourierTrack>? couriers = null)
    {
        if (typedDamage?.Any(bucket => bucket is null || bucket.HeroId <= 0) == true
            || healing?.Any(bucket => bucket is null || bucket.HeroId <= 0) == true || couriers?.Any(courier => courier is null) == true)
        {
            throw new ArgumentException("Typed damage and healing need a hero; couriers a value.");
        }

        TypedDamage = typedDamage?.OrderBy(bucket => bucket.Seconds).ToList();
        Healing = healing?.OrderBy(bucket => bucket.Seconds).ToList();
        Couriers = couriers?.Select(courier => courier with { Samples = courier.Samples.OrderBy(sample => sample.Seconds).ToList() }).ToList();
        if (economy?.Any(bucket => bucket is null || bucket.HeroId <= 0) == true
            || pings?.Any(ping => ping is null || ping.HeroId <= 0) == true || runesOnMap?.Any(rune => rune is null) == true)
        {
            throw new ArgumentException("Economy buckets and pings need a hero; runes a value.");
        }

        Economy = economy?.OrderBy(bucket => bucket.Seconds).ToList();
        Pings = pings?.OrderBy(ping => ping.Seconds).ToList();
        RunesOnMap = runesOnMap?.OrderBy(rune => rune.Seconds).ToList();
        if (creepHeroHits?.Any(hits => hits is null || hits.TargetHeroId <= 0 || hits.Hits < 0) == true
            || neutralCamps?.Any(camp => camp is null || camp.Alive.Any(reading => reading.Alive < 0)) == true)
        {
            throw new ArgumentException("Creep hits need a hero and a count ≥ 0; camps counts ≥ 0.");
        }

        CreepHeroHits = creepHeroHits?.OrderBy(hits => hits.Seconds).ToList();
        NeutralCamps = neutralCamps?.Select(camp => camp with { Alive = camp.Alive.OrderBy(reading => reading.Seconds).ToList() }).ToList();
        if (offLaneCreeps?.Any(sample => sample is null || sample.RadiantCount < 0 || sample.DireCount < 0) == true
            || laneCreepDeaths?.Any(death => death is null || !double.IsFinite(death.Seconds) || death.KillerHeroId is <= 0) == true)
        {
            throw new ArgumentException("Off-lane creeps need counts ≥ 0; lane creep deaths a finite time.");
        }

        OffLaneCreeps = offLaneCreeps?.OrderBy(sample => sample.Seconds).ToList();
        LaneCreepDeaths = laneCreepDeaths?.OrderBy(death => death.Seconds).ToList();
        if (buildings?.Any(building => building is null || string.IsNullOrWhiteSpace(building.Building)
                || building.Readings.Any(reading => reading.Health < 0 || reading.MaxHealth <= 0 || reading.Health > reading.MaxHealth)
                || building.Readings.Zip(building.Readings.Skip(1)).Any(pair => pair.Second.Seconds <= pair.First.Seconds)) == true)
        {
            throw new ArgumentException("Building health readings need 0 ≤ health ≤ max health, in time order.", nameof(buildings));
        }

        Buildings = buildings;
        if (buffs?.Any(buff => buff is null || buff.TargetHeroId <= 0 || buff.SourceHeroId is <= 0
                || !double.IsFinite(buff.Start) || buff.End < buff.Start || string.IsNullOrWhiteSpace(buff.Modifier)) == true)
        {
            throw new ArgumentException("Buffs need a target, a modifier and an end after their start.", nameof(buffs));
        }

        Buffs = buffs?.OrderBy(buff => buff.Start).ToList();
        if (debuffs?.Any(debuff => debuff is null || debuff.TargetHeroId <= 0 || debuff.SourceHeroId is <= 0
                || !double.IsFinite(debuff.Start) || debuff.End < debuff.Start || string.IsNullOrWhiteSpace(debuff.Modifier)) == true)
        {
            throw new ArgumentException("Debuffs need a target, a modifier and an end after their start.", nameof(debuffs));
        }

        Debuffs = debuffs?.OrderBy(debuff => debuff.Start).ToList();
        if (heroHits?.Any(hit => hit is null || hit.AttackerHeroId <= 0 || hit.TargetHeroId <= 0 || hit.Amount < 0) == true
            || creepKills?.Any(kill => kill is null || kill.HeroId <= 0 || !double.IsFinite(kill.Seconds)) == true
            || dayNight?.Any(change => change is null) == true)
        {
            throw new ArgumentException("Hero hits, creep kills and day/night changes need heroes, amounts and times.");
        }

        DayNight = dayNight?.OrderBy(change => change.Seconds).ToList();
        HeroHits = heroHits?.OrderBy(hit => hit.Seconds).ToList();
        CreepKills = creepKills?.OrderBy(kill => kill.Seconds).ToList();
        if (waves?.Any(wave => wave is null || wave.Lane is not (Lane.Top or Lane.Middle or Lane.Bottom)) == true)
        {
            throw new ArgumentException("Wave fronts are in the top, middle or bottom lane.", nameof(waves));
        }

        Waves = waves?.OrderBy(wave => wave.Seconds).ToList();
        if (buybacks?.Any(buyback => buyback is null || buyback.HeroId <= 0 || !double.IsFinite(buyback.Seconds)) == true
            || new[] { scans, glyphs }.Any(list => list?.Any(action => action is null || !double.IsFinite(action.Seconds)) == true)
            || runes?.Any(rune => rune is null || rune.HeroId <= 0 || !double.IsFinite(rune.Seconds)) == true)
        {
            throw new ArgumentException("Buybacks, scans, glyphs and runes need a finite time (and a hero).");
        }

        Buybacks = buybacks?.OrderBy(buyback => buyback.Seconds).ToList();
        Scans = scans?.OrderBy(action => action.Seconds).ToList();
        Glyphs = glyphs?.OrderBy(action => action.Seconds).ToList();
        Runes = runes?.OrderBy(rune => rune.Seconds).ToList();
        if (slotChanges is not null && slotChanges.Any(change => change is null || change.HeroId <= 0
                || change.Slot is < 0 or > SlotChange.NeutralSlot))
        {
            throw new ArgumentException("Slot changes need a hero and a known slot.", nameof(slotChanges));
        }

        if (new[] { itemCooldowns, abilityCooldowns }.Any(list => list is not null && list.Any(cooldown => cooldown is null
                || cooldown.HeroId <= 0 || string.IsNullOrWhiteSpace(cooldown.Name) || !double.IsFinite(cooldown.Seconds)
                || !double.IsFinite(cooldown.ReadyAt) || cooldown.ReadyAt <= cooldown.Seconds)))
        {
            throw new ArgumentException("Cooldowns need a hero, a name and an end after their start.");
        }

        SlotChanges = slotChanges?.OrderBy(change => change.Seconds).ToList();
        ItemCooldowns = itemCooldowns?.OrderBy(cooldown => cooldown.Seconds).ToList();
        AbilityCooldowns = abilityCooldowns?.OrderBy(cooldown => cooldown.Seconds).ToList();
        if (damageBuckets is not null && damageBuckets.Any(bucket => bucket is null || bucket.HeroId <= 0
                || bucket.ToHeroes < 0 || bucket.FromHeroes < 0 || bucket.Seconds % DamageBucket.Length != 0))
        {
            throw new ArgumentException("Damage buckets need a hero, non-negative amounts and a bucket start.", nameof(damageBuckets));
        }

        DamageBuckets = damageBuckets?.OrderBy(bucket => bucket.Seconds).ToList();
        ArgumentNullException.ThrowIfNull(smokes);
        ArgumentNullException.ThrowIfNull(itemUses);
        ArgumentNullException.ThrowIfNull(abilityUses);
        if (smokes.Any(smoke => smoke is null || smoke.HeroId <= 0 || !double.IsFinite(smoke.Seconds))
            || itemUses.Concat(abilityUses).Any(use => use is null || use.HeroId <= 0 || !double.IsFinite(use.Seconds)
                || string.IsNullOrWhiteSpace(use.Name)))
        {
            throw new ArgumentException("Every use needs a hero, a finite time and a name.");
        }

        Smokes = smokes.OrderBy(smoke => smoke.Seconds).ToList();
        ItemUses = itemUses.OrderBy(use => use.Seconds).ToList();
        AbilityUses = abilityUses.OrderBy(use => use.Seconds).ToList();
    }

    public static ReplayActivity None { get; } = new([], [], []);

    public IReadOnlyList<SmokeUse> Smokes { get; }

    public IReadOnlyList<RecordedUse> ItemUses { get; }

    public IReadOnlyList<RecordedUse> AbilityUses { get; }

    public IReadOnlyList<DamageBucket>? DamageBuckets { get; }

    // Null for documents read before inventories and cooldowns existed (unknown, not empty).
    public IReadOnlyList<SlotChange>? SlotChanges { get; }

    public IReadOnlyList<CooldownStart>? ItemCooldowns { get; }

    public IReadOnlyList<CooldownStart>? AbilityCooldowns { get; }

    // Null when the document does not have them (unknown, not "none happened").
    public IReadOnlyList<BuybackUse>? Buybacks { get; }

    public IReadOnlyList<TeamAction>? Scans { get; }

    public IReadOnlyList<TeamAction>? Glyphs { get; }

    public IReadOnlyList<RunePickup>? Runes { get; }

    public IReadOnlyList<WaveFront>? Waves { get; }

    // Null for documents read before they existed (unknown, not "none").
    public IReadOnlyList<DayNightChange>? DayNight { get; }

    public IReadOnlyList<HeroHit>? HeroHits { get; }

    public IReadOnlyList<CreepKill>? CreepKills { get; }

    public IReadOnlyList<Debuff>? Debuffs { get; }

    public IReadOnlyList<Buff>? Buffs { get; }

    public IReadOnlyList<BuildingHealth>? Buildings { get; }

    // Only seconds with creeps off the lanes (sparse); null when the document does not have them.
    public IReadOnlyList<OffLaneCreeps>? OffLaneCreeps { get; }

    public IReadOnlyList<LaneCreepDeath>? LaneCreepDeaths { get; }

    public IReadOnlyList<CreepHeroHits>? CreepHeroHits { get; }

    public IReadOnlyList<NeutralCamp>? NeutralCamps { get; }

    public IReadOnlyList<EconomyBucket>? Economy { get; }

    public IReadOnlyList<MapPing>? Pings { get; }

    public IReadOnlyList<RuneOnMap>? RunesOnMap { get; }

    public IReadOnlyList<TypedDamageBucket>? TypedDamage { get; }

    public IReadOnlyList<HealingBucket>? Healing { get; }

    public IReadOnlyList<CourierTrack>? Couriers { get; }
}
