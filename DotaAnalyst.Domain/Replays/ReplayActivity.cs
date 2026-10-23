using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Replays;

// What each hero did during the match, read from the kept replay document (items.uses, abilities.uses, vision.smokes).
// Like the positions, it is never saved with the analysis: only the prediction models' samples read it.
public sealed record ReplayActivity
{
    public IReadOnlyList<EconomyObservation>? EconomyStates { get; init; }
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
        IReadOnlyList<CourierTrack>? couriers = null,
        IReadOnlyList<NeutralCraft>? neutralCrafts = null,
        IReadOnlyList<MadstoneTrack>? madstones = null,
        TreeTimeline? trees = null,
        IReadOnlyList<VisionUnitsSample>? visionUnits = null,
        IReadOnlyList<CooldownCorrection>? itemCooldownChanges = null,
        IReadOnlyList<CooldownCorrection>? abilityCooldownChanges = null)
    {
        // Each correction goes to the one cooldown of that hero and name running when it happened; with none or more
        // than one running (two copies of an item: the legacy cooldowns have no entity), it is kept apart, unused.
        ItemCooldownChanges = itemCooldownChanges?.OrderBy(change => change.Seconds).ToList();
        AbilityCooldownChanges = abilityCooldownChanges?.OrderBy(change => change.Seconds).ToList();
        itemCooldowns = WithCorrections(itemCooldowns, ItemCooldownChanges);
        abilityCooldowns = WithCorrections(abilityCooldowns, AbilityCooldownChanges);

        if (visionUnits?.Any(sample => sample is null || sample.Radiant is null || sample.Dire is null) == true)
        {
            throw new ArgumentException("Vision unit samples need both teams' lists.", nameof(visionUnits));
        }

        Trees = trees;
        VisionUnits = visionUnits?.OrderBy(sample => sample.Seconds).ToList();

        if (neutralCrafts?.Any(craft => craft is null || craft.HeroId <= 0 || craft.Tier is < 1 or > 5) == true
            || madstones?.Any(track => track is null || track.HeroId <= 0) == true)
        {
            throw new ArgumentException("Neutral crafts and Madstones need a hero (and crafts a tier 1-5).");
        }

        NeutralCrafts = neutralCrafts?.OrderBy(craft => craft.Seconds).ToList();
        Madstones = madstones?.Select(track => track with { Readings = track.Readings.OrderBy(reading => reading.Seconds).ToList() }).ToList();

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
                || change.Slot is < 0 or > SlotChange.EnhancementSlot))
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

    // Neutral crafts and Madstone counters (N2); null for documents from before them (unknown, not "none").
    public IReadOnlyList<NeutralCraft>? NeutralCrafts { get; }

    public IReadOnlyList<MadstoneTrack>? Madstones { get; }

    // The map's trees (MP3) and each team's moving vision (MP4); null for documents from before them (unknown).
    public TreeTimeline? Trees { get; }

    public IReadOnlyList<VisionUnitsSample>? VisionUnits { get; }

    // The cooldown drops read from the document (null for documents before DATA-D15), as read: those matched to a
    // cooldown are also in its CooldownStart.Corrections.
    public IReadOnlyList<CooldownCorrection>? ItemCooldownChanges { get; }

    public IReadOnlyList<CooldownCorrection>? AbilityCooldownChanges { get; }

    private static IReadOnlyList<CooldownStart>? WithCorrections(IReadOnlyList<CooldownStart>? cooldowns, IReadOnlyList<CooldownCorrection>? changes)
    {
        if (cooldowns is null || changes is not { Count: > 0 })
        {
            return cooldowns;
        }

        var corrected = cooldowns.ToList();
        var byIndex = new Dictionary<int, List<CooldownCorrection>>();
        foreach (var change in changes)
        {
            var running = Enumerable.Range(0, corrected.Count)
                .Where(i => corrected[i].HeroId == change.HeroId && corrected[i].Name == change.Name && corrected[i].Seconds <= change.Seconds
                    && ReadyAtWith(corrected[i], byIndex.GetValueOrDefault(i), change.Seconds) > change.Seconds)
                .ToList();
            if (running.Count != 1)
            {
                continue;
            }

            if (!byIndex.TryGetValue(running[0], out var list))
            {
                byIndex[running[0]] = list = [];
            }

            list.Add(change);
        }

        foreach (var (index, list) in byIndex)
        {
            corrected[index] = corrected[index] with { Corrections = list };
        }

        return corrected;
    }

    private static double ReadyAtWith(CooldownStart cooldown, List<CooldownCorrection>? changes, double second) =>
        changes?.LastOrDefault(change => change.Seconds <= second)?.ReadyAt ?? cooldown.ReadyAt;
}
