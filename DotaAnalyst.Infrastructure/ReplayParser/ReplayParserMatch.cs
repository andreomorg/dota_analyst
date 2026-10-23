using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Infrastructure.ReplayParser;

// Body of POST /v1/match (and of POST /v1/laning, which the API no longer calls).
internal sealed record ReplayParserRequest(string ReplayUrl);

// The part of the Go parser's POST /v1/match document the API reads today (contract: replay-parser/internal/matchdata).
// The whole document is kept as it came (IReplayDocumentArchive) for the analyses still to come. Everything is
// nullable so a missing field becomes "unreadable answer" in the mapping below instead of a silent zero.
internal sealed record ReplayParserMatchResponse(
    int? SchemaVersion,
    ReplayParserMatchInfo? Match,
    IReadOnlyList<ReplayParserMatchPlayer?>? Players,
    ReplayParserTimelines? Timelines,
    ReplayParserLaning? Laning,
    IReadOnlyList<ReplayParserKill?>? Kills,
    ReplayParserVision? Vision,
    ReplayParserCombat? Combat,
    ReplayParserItems? Items,
    ReplayParserObjectives? Objectives,
    ReplayParserAbilities? Abilities,
    // Read only for the prediction models' samples.
    IReadOnlyList<ReplayParserBuyback?>? Buybacks = null);

internal sealed record ReplayParserBuyback(double? T, int? Player, int? Cost = null);

// A scan or a glyph: when and which team.
internal sealed record ReplayParserTeamEvent(double? T, string? Team);

internal sealed record ReplayParserRune(double? T, int? Player, int? RuneType);

// uses: one entry per player with every cast as [t, "ability_name"] (read only for the prediction models' samples).
internal sealed record ReplayParserAbilities(
    IReadOnlyList<ReplayParserPlayerAbilityLevels?>? Upgrades, IReadOnlyList<ReplayParserPlayerAbilities?>? Uses = null,
    IReadOnlyList<ReplayParserPlayerCooldowns?>? Cooldowns = null,
    IReadOnlyList<ReplayParserPlayerCooldownChanges?>? CooldownChanges = null, JsonElement? Stolen = null);

// One player's cooldowns that started, as [t, "name", readyAt] (newer documents).
internal sealed record ReplayParserPlayerCooldowns(int? Player, IReadOnlyList<JsonElement>? Cooldowns);

// One player's cooldown drops (DATA-D15), as [t, "name", entity, serial, readyAt].
internal sealed record ReplayParserPlayerCooldownChanges(int? Player, IReadOnlyList<JsonElement>? Changes);

// One player's inventory changes, as [t, slot, "item_name" | null] (newer documents).
internal sealed record ReplayParserPlayerSlots(int? Player, IReadOnlyList<JsonElement>? Changes);

// Targets (X27): parallel to Abilities, [targetPlayer | null, x | null, y | null, pointSource | null].
internal sealed record ReplayParserPlayerAbilities(int? Player, IReadOnlyList<JsonElement>? Abilities, IReadOnlyList<JsonElement>? Targets = null);

internal sealed record ReplayParserPlayerAbilityLevels(int? Player, IReadOnlyList<JsonElement>? Levels);

internal sealed record ReplayParserObjectives(
    IReadOnlyList<ReplayParserBuildingKill?>? Buildings,
    IReadOnlyList<ReplayParserCreatureKill?>? Roshan,
    IReadOnlyList<ReplayParserCreatureKill?>? Tormentors,
    IReadOnlyList<ReplayParserRune?>? Runes = null, IReadOnlyList<ReplayParserBuildingHealth?>? BuildingHealth = null,
    IReadOnlyList<ReplayParserNeutralCamp?>? NeutralCamps = null, IReadOnlyList<JsonElement>? RunesOnMap = null,
    JsonElement? OutpostCaptures = null, JsonElement? Outposts = null,
    JsonElement? TormentorStates = null, JsonElement? Couriers = null, JsonElement? BossTracks = null);

// One tower/barracks/Ancient: its combat-log name, owner, kind, lane/tier (null for the Ancient and tier 4s), where it
// stands (X15b) and its sparse [t, health, maxHealth] readings.
internal sealed record ReplayParserBuildingHealth(
    string? Building, string? Team, string? Kind, string? Lane, int? Tier, int? X, int? Y, IReadOnlyList<JsonElement>? Health);

// building is the combat log name (npc_dota_badguys_tower1_mid), team its owner; killer a player index.
internal sealed record ReplayParserBuildingKill(double? T, string? Building, string? Team, int? Killer, bool? Denied);

// A Roshan or Tormentor kill; team is the team that killed it. aegisPlayer only for Roshan.
internal sealed record ReplayParserCreatureKill(double? T, string? Team, int? Killer, int? AegisPlayer);

// uses: every item activated, same shape as the purchases (read only for the prediction models' samples).
internal sealed record ReplayParserItems(
    IReadOnlyList<ReplayParserPlayerItems?>? Purchases, IReadOnlyList<ReplayParserPlayerItems?>? Uses = null,
    IReadOnlyList<ReplayParserPlayerSlots?>? Slots = null, IReadOnlyList<ReplayParserPlayerCooldowns?>? Cooldowns = null,
    IReadOnlyList<ReplayParserPlayerCrafts?>? NeutralCrafts = null, JsonElement? Final = null,
    IReadOnlyList<ReplayParserPlayerCooldownChanges?>? CooldownChanges = null);

// One player's purchases as [t, "item_name"] tuples (JSON arrays, read one by one in the mapping).
internal sealed record ReplayParserPlayerItems(int? Player, IReadOnlyList<JsonElement>? Items);

internal sealed record ReplayParserVision(
    IReadOnlyList<ReplayParserWard?>? Wards, IReadOnlyList<ReplayParserSmoke?>? Smokes = null,
    IReadOnlyList<ReplayParserTeamEvent?>? Scans = null, IReadOnlyList<ReplayParserTeamEvent?>? Glyphs = null,
    IReadOnlyList<JsonElement>? Pings = null, JsonElement? ChatWheel = null, JsonElement? Lanterns = null);

// A Smoke of Deceit: when and which player used it (x/y not read).
internal sealed record ReplayParserSmoke(double? T, int? Player);

// Times are game-clock seconds with one decimal; player and destroyedBy are player indexes.
internal sealed record ReplayParserWard(
    string? Type, int? Player, string? Team, int? X, int? Y, double? PlacedAt, double? RemovedAt, bool? Destroyed,
    int? DestroyedBy);

// One hero death; killer is a player index (null when no player dealt the blow), killerUnit the combat log name.
internal sealed record ReplayParserKill(
    double? T, int? Victim, int? Killer, string? KillerUnit, int? X, int? Y, bool? Reincarnated,
    IReadOnlyList<int>? Assists);

// buckets (newer documents): the hero damage of perMinute every 2 s, sparse, for the prediction models' samples only.
// heroHits ([t, attacker, target, amount] per 2 s) and creepKills ([t, player, kind, x, y]): newer documents, for the
// prediction models only.
internal sealed record ReplayParserCombat(
    IReadOnlyList<ReplayParserDamage?>? Damage, IReadOnlyList<ReplayParserCombatMinutes?>? PerMinute,
    IReadOnlyList<ReplayParserCombatBuckets?>? Buckets = null, IReadOnlyList<JsonElement>? HeroHits = null,
    IReadOnlyList<JsonElement>? CreepKills = null, IReadOnlyList<JsonElement>? Debuffs = null,
    IReadOnlyList<JsonElement>? Buffs = null, IReadOnlyList<JsonElement>? LaneCreepDeaths = null,
    IReadOnlyList<JsonElement>? Economy = null, JsonElement? Healing = null, JsonElement? GoldEvents = null);

// One player's [t, amount] buckets of damage dealt to and taken from enemy heroes.
// ByType (X30) = [t, physicalTo, magicalTo, pureTo, physicalFrom, magicalFrom, pureFrom]; Healing = [t, received, done].
internal sealed record ReplayParserCombatBuckets(
    int? Player, IReadOnlyList<JsonElement>? ToHeroes, IReadOnlyList<JsonElement>? FromHeroes,
    IReadOnlyList<JsonElement>? ByType = null, IReadOnlyList<JsonElement>? Healing = null);

// A courier (X28): its id, team and sparse [t, alive, x, y, flying | null] readings.
internal sealed record ReplayParserCourier(string? Id, string? Team, IReadOnlyList<JsonElement>? Samples);

// One player's damage and healing in each minute (index i = minute i, from i:00 to i:59; one per started minute).
internal sealed record ReplayParserCombatMinutes(
    int? Player,
    IReadOnlyList<int>? DamageToHeroes,
    IReadOnlyList<int>? DamageFromHeroes,
    IReadOnlyList<int>? DamageTaken = null,
    IReadOnlyList<int>? Healing = null);

// Aggregated damage from one source to one target with one inflictor; sourcePlayer/targetPlayer are player indexes
// (null for a non-hero unit, then named by its kind).
internal sealed record ReplayParserDamage(
    int? SourcePlayer, string? SourceKind, int? TargetPlayer, string? TargetKind, string? Inflictor, int? Amount);

// startedAt: Unix seconds of the horn (null when the replay does not say): picks the patch's map for the terrain metrics.
internal sealed record ReplayParserMatchInfo(double? DurationSeconds, uint? EngineBuild = null,
    string? ProducerVersion = null, bool? MatchEnded = null, long? StartedAt = null);

// role: role-queue position 1-5; null without role queue (Turbo, unranked).
// final: the end-of-match numbers (read by ReplayParserFacts as a final fact). players[].facet is not read: 7.41 has no
// facet to choose (always null in the local replays).
internal sealed record ReplayParserMatchPlayer(int? Player, int? HeroId, string? Team, int? Role, JsonElement? Final = null);

// waves (newer documents): per lane, [t, radiant, dire] creep fronts (x + y) every 2 s, for the prediction models only.
internal sealed record ReplayParserTimelines(
    IReadOnlyList<ReplayParserPlayerMinutes?>? PerMinute, IReadOnlyList<ReplayParserPlayerPositions?>? Positions,
    IReadOnlyList<ReplayParserLaneWaves?>? Waves = null, IReadOnlyList<JsonElement>? DayNight = null,
    IReadOnlyList<JsonElement>? OffLaneCreeps = null, IReadOnlyList<JsonElement>? CreepHeroDamage = null,
    IReadOnlyList<ReplayParserCourier?>? Couriers = null, IReadOnlyList<ReplayParserPlayerMadstones?>? Madstones = null,
    ReplayParserTrees? Trees = null, IReadOnlyList<JsonElement>? VisionUnits = null, JsonElement? TempTrees = null,
    JsonElement? GoldBalances = null, JsonElement? EconomyStates = null, JsonElement? GoldLossChanges = null,
    JsonElement? FarmSnapshots = null);

// MP3: { deadAtStart: [index], changes: [[t, index, alive]] } — index = the tree in the map of the patch.
internal sealed record ReplayParserTrees(IReadOnlyList<int>? DeadAtStart, IReadOnlyList<JsonElement>? Changes);

// N2: one player's Madstone counters as [t, inHand, acquired, found, neutralGold], kept when one changes.
internal sealed record ReplayParserPlayerMadstones(int? Player, IReadOnlyList<JsonElement>? Samples);

// N2: one player's neutral crafts as [t, "item_artifact", artifactLevel | null, tier 1-5, "item_enhancement" | null,
// enhancementLevel | null].
internal sealed record ReplayParserPlayerCrafts(int? Player, IReadOnlyList<JsonElement>? Crafts);

// Sizes: parallel to Fronts (same t, same order); absent in documents from before X16.
internal sealed record ReplayParserLaneWaves(
    string? Lane, IReadOnlyList<JsonElement>? Fronts, IReadOnlyList<JsonElement>? Sizes = null, IReadOnlyList<JsonElement>? Strength = null,
    IReadOnlyList<JsonElement>? Composition = null);

// A neutral camp (W3): its spawner's position and type, and sparse [t, aliveNeutrals] readings.
internal sealed record ReplayParserNeutralCamp(string? Id, int? X, int? Y, string? Type, IReadOnlyList<JsonElement>? Alive, int? SpawnerType = null);

// One player's hero positions as [t, x, y] tuples (JSON arrays, read one by one in the mapping), while alive; vitals
// (newer documents) is [health, maxHealth, mana, maxMana, visibility] for the sample with the same index.
internal sealed record ReplayParserPlayerPositions(
    int? Player, IReadOnlyList<JsonElement>? Samples, IReadOnlyList<JsonElement>? Vitals = null,
    IReadOnlyList<JsonElement>? Stats = null, IReadOnlyList<JsonElement>? Orders = null, IReadOnlyList<JsonElement>? Facing = null,
    IReadOnlyList<JsonElement>? CombatDetails = null);

// One value per point: every minute of the game clock from 0:00, then the end of the match. Totals so far (gold
// earned, last hits...), except net worth and level (the value at that moment).
internal sealed record ReplayParserPlayerMinutes(
    int? Player,
    IReadOnlyList<int>? NetWorth,
    IReadOnlyList<int>? Xp,
    IReadOnlyList<int>? LastHits,
    IReadOnlyList<int>? Denies,
    IReadOnlyList<int>? Deaths,
    IReadOnlyList<int>? Gold = null,
    IReadOnlyList<int>? Level = null,
    IReadOnlyList<int>? Kills = null,
    IReadOnlyList<int>? Assists = null,
    IReadOnlyList<int>? HeroDamage = null);

internal sealed record ReplayParserLaning(int? EndsAtSeconds, IReadOnlyList<ReplayParserPlayerLane?>? Lanes);

internal sealed record ReplayParserPlayerLane(int? Player, string? Lane);

// The parser's format -> the domain model, like OpenDotaMappings does for OpenDota.
internal static partial class ReplayParserMappings
{
    internal static ReplayMetadata ReadMetadata(ReplayParserMatchResponse response)
    {
        var info = response.Match ?? throw new InvalidDataException("match is missing.");
        if (info.DurationSeconds is not { } duration || !double.IsFinite(duration) || duration < 0)
            throw new InvalidDataException("Invalid match duration.");
        return new(duration, info.EngineBuild is > 0 ? info.EngineBuild : null, info.ProducerVersion, info.MatchEnded);
    }
    // The /v1/match schema this mapping understands; a newer one only adds fields (a breaking change is /v2/).
    public const int SupportedSchemaVersion = 1;

    /// <exception cref="InvalidDataException">The answer is incomplete or breaks a domain rule.</exception>
    public static ReplayAnalysis ToReplayAnalysis(this ReplayParserMatchResponse response, MatchId matchId)
        => response.ToMappedReplay(matchId).Analysis;

    internal sealed record MappedReplay(ReplayAnalysis Analysis, IReadOnlyList<HeroPositions> Positions);

    internal static MappedReplay ToMappedReplay(this ReplayParserMatchResponse response, MatchId matchId)
    {
        try
        {
            return Map(response, matchId);
        }
        catch (ArgumentException ex)
        {
            // The domain refused the values (negative numbers, the same hero twice, 700 s of laning phase...), or the
            // same player index came twice.
            throw new InvalidDataException($"The replay analysis breaks a rule: {ex.Message}", ex);
        }
    }

    private static MappedReplay Map(ReplayParserMatchResponse response, MatchId matchId)
    {
        if (response.SchemaVersion is not >= SupportedSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported schemaVersion {response.SchemaVersion}.");
        }

        var duration = response.Match?.DurationSeconds ?? throw new InvalidDataException("match.durationSeconds is missing.");
        var laning = response.Laning ?? throw new InvalidDataException("laning is missing.");
        var endsAt = laning.EndsAtSeconds ?? throw new InvalidDataException("laning.endsAtSeconds is missing.");
        var players = Required(response.Players, "players").Select(player => player ?? throw Empty("player")).ToList();
        var minutes = Required(response.Timelines?.PerMinute, "timelines.perMinute")
            .Select(entry => entry ?? throw Empty("perMinute entry"))
            .ToDictionary(entry => entry.Player ?? throw Missing("perMinute.player"));
        var lanes = Required(laning.Lanes, "laning.lanes")
            .Select(entry => entry ?? throw Empty("lane"))
            .ToDictionary(entry => entry.Player ?? throw Missing("lanes.player"), entry => ToLane(entry.Lane));

        var points = minutes.Values.Select(entry => Required(entry.NetWorth, "netWorth").Count).Distinct().ToList();
        if (points is not [var count and > 0])
        {
            throw new InvalidDataException("Every player needs the same number of points.");
        }

        // The numbers at the end of the laning phase are its point in the timeline: minute 10 when it lasted the
        // whole 10:00, otherwise the last point (the end of the match).
        var at = endsAt >= ReplayAnalysis.LaningPhaseEndsAtSeconds && count > 11 ? 10 : count - 1;

        var heroOf = players.ToDictionary(
            player => player.Player ?? throw Missing("players.player"),
            player => player.HeroId ?? throw Missing("players.heroId"));

        var combatMinutes = (response.Combat?.PerMinute ?? [])
            .Select(entry => entry ?? throw Empty("combat.perMinute entry"))
            .ToDictionary(entry => entry.Player ?? throw Missing("perMinute.player"));

        var laningStats = new List<LaningStats>();
        var heroes = new List<HeroTimeline>();
        foreach (var player in players)
        {
            var index = player.Player ?? throw Missing("players.player");
            var heroId = player.HeroId ?? throw Missing("players.heroId");
            var team = ToTeam(player.Team);
            var series = minutes.GetValueOrDefault(index) ?? throw Missing($"perMinute of player {index}");
            var lane = lanes.TryGetValue(index, out var found) ? found : throw Missing($"lane of player {index}");

            laningStats.Add(new LaningStats(
                heroId, team, lane,
                lastHits: Value(series.LastHits, "lastHits", at),
                denies: Value(series.Denies, "denies", at),
                netWorth: Value(series.NetWorth, "netWorth", at),
                experience: Value(series.Xp, "xp", at),
                deaths: Value(series.Deaths, "deaths", at)));
            heroes.Add(new HeroTimeline(
                heroId, team, Required(series.NetWorth, "netWorth"), Required(series.Xp, "xp"),
                OtherSeries(series, combatMinutes.GetValueOrDefault(index), count)));
        }

        var deaths = Required(response.Kills, "kills")
            .Select(kill => ToDeath(kill ?? throw Empty("kill"), heroOf))
            .ToList();

        var wards = Required(response.Vision?.Wards, "vision.wards")
            .Select(ward => ToWard(ward ?? throw Empty("ward"), heroOf))
            .ToList();

        int? HeroOfPlayer(int? index) => index is { } i
            ? heroOf.TryGetValue(i, out var heroId) ? heroId : throw new InvalidDataException($"Unknown player {i}.")
            : null;

        var damage = Required(response.Combat?.Damage, "combat.damage")
            .Select(row => row ?? throw Empty("damage row"))
            // Only rows with a hero on a side (the parser groups the rest by unit kind on both sides).
            .Where(row => row.SourcePlayer is not null || row.TargetPlayer is not null)
            .Select(row => new DamageRow(
                sourceHeroId: HeroOfPlayer(row.SourcePlayer),
                sourceKind: row.SourceKind ?? throw Missing("damage.sourceKind"),
                targetHeroId: HeroOfPlayer(row.TargetPlayer),
                targetKind: row.TargetKind ?? throw Missing("damage.targetKind"),
                inflictor: row.Inflictor,
                amount: row.Amount ?? throw Missing("damage.amount")))
            .ToList();

        var purchases = Required(response.Items?.Purchases, "items.purchases")
            .Select(entry => entry ?? throw Empty("purchases entry"))
            .SelectMany(entry =>
            {
                var heroId = HeroOfPlayer(entry.Player ?? throw Missing("purchases.player"))!.Value;
                return Required(entry.Items, "purchases.items").Select(tuple => ToPurchase(heroId, tuple));
            })
            .ToList();

        var objectives = Required(response.Objectives, "objectives");
        var mapObjectives = Required(objectives.Buildings, "objectives.buildings")
            .Select(building => ToBuildingObjective(building ?? throw Empty("building"), HeroOfPlayer))
            .OfType<MapObjective>()
            .Concat(Required(objectives.Roshan, "objectives.roshan")
                .Select(kill => ToCreatureObjective(ObjectiveKind.Roshan, kill ?? throw Empty("roshan"), HeroOfPlayer)))
            .Concat(Required(objectives.Tormentors, "objectives.tormentors")
                .Select(kill => ToCreatureObjective(ObjectiveKind.Tormentor, kill ?? throw Empty("tormentor"), HeroOfPlayer)))
            .ToList();

        var abilityUpgrades = Required(response.Abilities?.Upgrades, "abilities.upgrades")
            .Select(entry => entry ?? throw Empty("ability upgrades entry"))
            .SelectMany(entry =>
            {
                var heroId = HeroOfPlayer(entry.Player ?? throw Missing("ability upgrades.player"))!.Value;
                return Required(entry.Levels, "ability upgrades.levels")
                    .Select(tuple => ToAbilityUpgrade(heroId, tuple));
            })
            .ToList();

        var timeline = new MatchTimeline(Seconds(count, duration), heroes);
        var positions = ReadPositions(response);
        // Role queue when every player has a role; otherwise (pro matches, Turbo, unranked) the roles are inferred,
        // and the metrics say so (they then only feed the comparison base, never the player's own analysis).
        var queueRoles = players.All(player => player.Role is not null)
            ? players.ToDictionary(player => player.HeroId!.Value, player => player.Role!.Value)
            : null;
        // Optional in the document as far as the metrics go: without it the lane damage just stays unknown.
        var damageMinutes = (response.Combat?.PerMinute ?? [])
            .Select(entry => entry ?? throw Empty("combat.perMinute entry"))
            .Select(entry => new HeroDamageMinutes(
                HeroOfPlayer(entry.Player ?? throw Missing("perMinute.player"))!.Value,
                Required(entry.DamageToHeroes, "perMinute.damageToHeroes"),
                Required(entry.DamageFromHeroes, "perMinute.damageFromHeroes")))
            .ToList();
        // The terrain metrics need the map of the match's patch (none for another patch: Terrain stays unknown).
        var map = GameData.EmbeddedGameMaps.ForMatch(response.Match?.StartedAt is { } started ? DateTimeOffset.FromUnixTimeSeconds(started) : null);
        var metrics = MatchMetricsCalculator.Calculate(
            laningStats, timeline, deaths, wards, positions, queueRoles ?? RoleInference.Infer(laningStats),
            rolesInferred: queueRoles is null, damageMinutes, map, map is null ? null : ReadTrees(response),
            mapObjectives, ObjectivePlaces.Locate(mapObjectives, ReadFacts(response).BossTracks, map),
            ObjectiveCounts(players),
            response.Objectives?.Runes?
                .Where(rune => rune is { RuneType: MapObjectiveMetricsCalculator.WisdomRuneType, T: not null, Player: not null })
                .Select(rune => (HeroOfPlayer(rune!.Player)!.Value, (int)rune.T!.Value))
                .ToList(),
            response.Combat?.Buffs?.Where(EndsAfterStart).Select(tuple => ToBuff(tuple, player => HeroOfPlayer(player)!.Value)).ToList(),
            ActivityForMetrics(response),
            // Each hero's kit as far as the replay shows it: the abilities it learned (talents included, harmless).
            abilityUpgrades.GroupBy(upgrade => upgrade.HeroId)
                .ToDictionary(group => group.Key, group => (IReadOnlyCollection<string>)group.Select(upgrade => upgrade.AbilityName).ToHashSet())) ?? [];

        // N2: absent in documents from before the neutral crafts (the analysis then has no neutral section).
        var neutralCrafts = response.Items?.NeutralCrafts?.Select(entry => entry ?? throw Empty("neutral crafts entry"))
            .SelectMany(entry => Required(entry.Crafts, "neutralCrafts.crafts")
                .Select(tuple => ToNeutralCraft(HeroOfPlayer(entry.Player ?? throw Missing("neutralCrafts.player"))!.Value, tuple)))
            .ToList();
        var neutralProgress = response.Timelines?.Madstones?.Select(entry => entry ?? throw Empty("madstones entry"))
            .Select(entry => NeutralProgress.From(new MadstoneTrack(
                HeroOfPlayer(entry.Player ?? throw Missing("madstones.player"))!.Value,
                Required(entry.Samples, "madstones.samples").Select(ToMadstoneReading).ToList())))
            .OfType<NeutralProgress>()
            .ToList();

        var analysis = new ReplayAnalysis(
            matchId, endsAt, laningStats, timeline, deaths, wards, damage, purchases, mapObjectives, metrics,
            positions.Select(hero => HeroHeatmap.FromPositions(hero, AlliesOf(hero))).ToList(), abilityUpgrades)
        {
            NeutralCrafts = neutralCrafts,
            NeutralProgress = neutralProgress,
        };
        return new(analysis, positions);

        // The positions of the hero's teammates (the laning stats say each hero's team).
        IReadOnlyList<HeroPositions> AlliesOf(HeroPositions hero)
        {
            var team = laningStats.FirstOrDefault(player => player.HeroId == hero.HeroId)?.Team;
            return positions
                .Where(other => other.HeroId != hero.HeroId
                    && team is not null
                    && laningStats.FirstOrDefault(player => player.HeroId == other.HeroId)?.Team == team)
                .ToList();
        }
    }

    // The statistics of the catalog saved per minute besides net worth and experience, one value per point. A list
    // missing (or empty) in the document is left out: the analysis then counts as outdated and is read again.
    private static Dictionary<StatKey, IReadOnlyList<int>> OtherSeries(
        ReplayParserPlayerMinutes minutes, ReplayParserCombatMinutes? combat, int count)
    {
        var series = new Dictionary<StatKey, IReadOnlyList<int>>();

        void Totals(StatKey key, IReadOnlyList<int>? values)
        {
            if (values is { Count: > 0 })
            {
                series[key] = values.Count == count
                    ? values
                    : throw new InvalidDataException($"perMinute {key} needs one value per point.");
            }
        }

        // combat.perMinute has the amount of each minute: point i (minute i:00) is the sum of minutes 0 to i-1 and the
        // last point (the end) the sum of all of them.
        void PerMinute(StatKey key, IReadOnlyList<int>? values)
        {
            if (values is { Count: > 0 })
            {
                series[key] = Enumerable.Range(0, count)
                    .Select(point => values.Take(point == count - 1 ? values.Count : point).Sum())
                    .ToList();
            }
        }

        Totals(StatKey.Gold, minutes.Gold);
        Totals(StatKey.LastHits, minutes.LastHits);
        Totals(StatKey.Denies, minutes.Denies);
        Totals(StatKey.Level, minutes.Level);
        Totals(StatKey.Kills, minutes.Kills);
        Totals(StatKey.Deaths, minutes.Deaths);
        Totals(StatKey.Assists, minutes.Assists);
        Totals(StatKey.HeroDamage, minutes.HeroDamage);
        PerMinute(StatKey.DamageFromHeroes, combat?.DamageFromHeroes);
        PerMinute(StatKey.DamageTaken, combat?.DamageTaken);
        PerMinute(StatKey.Healing, combat?.Healing);
        return series;
    }

    // [t, "ability_name", level] -> one ability upgrade.
    private static AbilityUpgrade ToAbilityUpgrade(int heroId, JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 3
            || tuple[0].ValueKind != JsonValueKind.Number
            || tuple[1].ValueKind != JsonValueKind.String
            || tuple[2].ValueKind != JsonValueKind.Number)
        {
            throw new InvalidDataException("An ability upgrade is not [t, ability, level].");
        }

        var abilityName = tuple[1].GetString();
        if (string.IsNullOrWhiteSpace(abilityName) || !tuple[2].TryGetInt32(out var level))
        {
            throw new InvalidDataException("An ability upgrade has an invalid ability or level.");
        }

        return new AbilityUpgrade(
            heroId, (int)Math.Round(tuple[0].GetDouble()), abilityName, level);
    }

    // Shared by replay analysis and opt-in coaching: one adapter for the Go position contract.
    internal static IReadOnlyList<HeroPositions> ReadPositions(ReplayParserMatchResponse response)
    {
        var heroOf = Required(response.Players, "players").Select(p => p ?? throw Empty("player"))
            .ToDictionary(p => p.Player ?? throw Missing("players.player"), p => p.HeroId ?? throw Missing("players.heroId"));
        return Required(response.Timelines?.Positions, "timelines.positions")
            .Select(entry => entry ?? throw Empty("positions entry"))
            .Select(entry => new HeroPositions(
                heroOf.TryGetValue(entry.Player ?? throw Missing("positions.player"), out var hero)
                    ? hero : throw new InvalidDataException($"Unknown player {entry.Player}."),
                WithCombatDetails(WithFacing(WithOrders(WithStats(WithVitals(Required(entry.Samples, "positions.samples").Select(ToPositionSample).ToList(),
                    entry.Vitals), entry.Stats), entry.Orders), entry.Facing), entry.CombatDetails)))
            .ToArray();
    }

    private static List<PositionSample> WithCombatDetails(List<PositionSample> samples, IReadOnlyList<JsonElement>? details)
    {
        if (details is null) return samples;
        if (details.Count != samples.Count) throw new InvalidDataException("positions.combatDetails needs one entry per sample.");
        return samples.Zip(details, (sample, tuple) =>
        {
            if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 12
                || tuple[11].ValueKind != JsonValueKind.Null
                || tuple.EnumerateArray().Any(v => v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Number)
                    || (v.ValueKind == JsonValueKind.Number && (!v.TryGetDouble(out var d) || !double.IsFinite(d)))))
                throw new InvalidDataException("Invalid combatDetails component tuple.");
            double? At(int i) => tuple[i].ValueKind == JsonValueKind.Null ? null : tuple[i].GetDouble();
            int? capability = null;
            if (tuple[8].ValueKind != JsonValueKind.Null)
            {
                if (!tuple[8].TryGetInt32(out var raw)) throw new InvalidDataException("Attack capability must be an integer.");
                capability = raw;
            }
            return sample with { CombatDetails = new(At(0), At(1), At(2), At(3), At(4), At(5), At(6), At(7), capability, At(9), At(10)) };
        }).ToList();
    }

    // X29: integer yaw degrees (or null) parallel to the samples; older documents have none.
    private static List<PositionSample> WithFacing(List<PositionSample> samples, IReadOnlyList<JsonElement>? facing)
    {
        if (facing is null)
        {
            return samples;
        }

        if (facing.Count != samples.Count)
        {
            throw new InvalidDataException("positions.facing needs one entry per sample.");
        }

        return samples.Zip(facing, (sample, yaw) => yaw.ValueKind switch
        {
            JsonValueKind.Null => sample,
            JsonValueKind.Number when yaw.TryGetInt32(out var degrees) && degrees is >= 0 and < 360 => sample with { Facing = degrees },
            _ => throw new InvalidDataException("A facing is not an integer 0–359 or null."),
        }).ToList();
    }

    // Like WithVitals: no orders (older documents, or a replay without them: the field is left out) keeps the samples.
    private static List<PositionSample> WithOrders(List<PositionSample> samples, IReadOnlyList<JsonElement>? orders)
    {
        if (orders is null)
        {
            return samples;
        }

        if (orders.Count != samples.Count)
        {
            throw new InvalidDataException("positions.orders needs one entry per sample.");
        }

        return samples.Zip(orders, (sample, tuple) => sample with { Orders = ToOrders(tuple) }).ToList();
    }

    // [moveX | null, moveY | null, moves, attacks, casts].
    private static HeroOrders ToOrders(JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 5
            || tuple[0].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)
            || tuple[1].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)
            || !tuple[2].TryGetInt32(out var moves) || !tuple[3].TryGetInt32(out var attacks) || !tuple[4].TryGetInt32(out var casts)
            || moves < 0 || attacks < 0 || casts < 0)
        {
            throw new InvalidDataException("An orders entry is not [moveX, moveY, moves, attacks, casts].");
        }

        int? At(int index) => tuple[index].ValueKind == JsonValueKind.Null ? null : tuple[index].GetInt32();
        return new HeroOrders(At(0), At(1), moves, attacks, casts);
    }

    // Like WithVitals: older documents have no stats; newer ones have one per sample, in the same order.
    private static List<PositionSample> WithStats(List<PositionSample> samples, IReadOnlyList<JsonElement>? stats)
    {
        if (stats is null)
        {
            return samples;
        }

        if (stats.Count != samples.Count)
        {
            throw new InvalidDataException("positions.stats needs one entry per sample.");
        }

        return samples.Zip(stats, (sample, tuple) => sample with { Stats = ToStats(tuple) }).ToList();
    }

    // [armor, magicResist, moveSpeed, damageMin, damageMax, strength, agility, intellect], each a number or null.
    private static HeroCombatStats ToStats(JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 8
            || tuple.EnumerateArray().Any(value => value.ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)))
        {
            throw new InvalidDataException(
                "A stats entry is not [armor, magicResist, moveSpeed, damageMin, damageMax, strength, agility, intellect].");
        }

        double? At(int index) => tuple[index].ValueKind == JsonValueKind.Null ? null : tuple[index].GetDouble();
        int? Whole(int index) => At(index) is { } value ? (int)Math.Round(value) : null;
        return new HeroCombatStats(At(0), At(1), Whole(2), Whole(3), Whole(4), At(5), At(6), At(7));
    }

    // Older documents have no vitals: the samples stay as they are. Newer ones have one per sample, in the same order.
    private static List<PositionSample> WithVitals(List<PositionSample> samples, IReadOnlyList<JsonElement>? vitals)
    {
        if (vitals is null)
        {
            return samples;
        }

        if (vitals.Count != samples.Count)
        {
            throw new InvalidDataException("positions.vitals needs one entry per sample.");
        }

        return samples.Zip(vitals, (sample, tuple) => sample with { Vitals = ToVitals(tuple) }).ToList();
    }

    // [health, maxHealth, mana, maxMana, visibleToEnemies (0/1), unitState, gold], each a whole number or null.
    private static HeroVitals ToVitals(JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 7
            || tuple.EnumerateArray().Any(value => value.ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)))
        {
            throw new InvalidDataException(
                "A vitals entry is not [health, maxHealth, mana, maxMana, visibleToEnemies, unitState, gold].");
        }

        int? At(int index) => tuple[index].ValueKind == JsonValueKind.Null ? null : (int)Math.Round(tuple[index].GetDouble());
        if (At(0) is < 0 || At(1) is < 0 || At(2) is < 0 || At(3) is < 0 || At(6) is < 0 || At(4) is not (null or 0 or 1))
        {
            throw new InvalidDataException("Health, mana and gold can't be negative; visibleToEnemies is 0 or 1.");
        }

        return new HeroVitals(At(0), At(1), At(2), At(3), At(4) is { } seen ? seen == 1 : null, At(5), At(6));
    }

    // Whether the document has every section the prediction models read (added on 20/10/2026): vitals next to the
    // positions, waves, combat buckets, inventory changes and cooldowns. Older documents are parsed again to get them.
    // 21/10/2026 (X1/X2/X4): also day/night, combat attributes next to the positions, hero hits, creep kills and the
    // debuffs. The orders (X3) are not required: a replay may lack them (the field is then left out), so they can't
    // mark a document as old. X8/X16: the buffs and the wave sizes too (a lane without creeps has no sizes).
    internal static bool HasModelSections(ReplayParserMatchResponse response) =>
        response.Timelines?.Positions is { Count: > 0 } positions && positions.All(entry => entry?.Vitals is not null && entry.Stats is not null)
        // W1/W2: wave strength next to the sizes, the off-lane creeps (always written, [] when none) and the lane creep
        // deaths.
        && response.Timelines.Waves is { } waves
        && waves.All(lane => lane?.Fronts is not { Count: > 0 }
            || (lane.Sizes is not null && lane.Strength is not null && lane.Composition is not null))
        // X25/X26: gold/XP by reason and runes on the map (pings and ability targets may be empty: not required).
        && response.Combat?.Economy is { Count: > 0 } && response.Objectives?.RunesOnMap is { Count: > 0 }
        // X28/X30: couriers and typed damage (some player always has it).
        && response.Timelines.Couriers is { Count: > 0 }
        && response.Combat.Buckets is { } typedBuckets && typedBuckets.Any(entry => entry?.ByType is not null)
        // W3/W6: neutral camps and creep hits on heroes.
        && response.Objectives?.NeutralCamps is { Count: > 0 } && response.Timelines.CreepHeroDamage is { Count: > 0 }
        && response.Timelines.OffLaneCreeps is not null
        && response.Combat?.LaneCreepDeaths is { Count: > 0 }
        && response.Timelines.DayNight is not null && response.Combat?.Buckets is not null
        && response.Combat.HeroHits is not null && response.Combat.CreepKills is not null && response.Combat.Debuffs is not null
        && response.Combat.Buffs is not null
        // X15/X15b: building health, with positions.
        && response.Objectives?.BuildingHealth is { Count: > 0 } buildings && buildings.All(entry => entry?.X is not null && entry.Y is not null)
        && response.Items?.Slots is not null && response.Items.Cooldowns is not null && response.Abilities?.Cooldowns is not null
        // N2: neutral crafts and Madstones (always present in a new document, [] when nothing happened).
        && response.Items.NeutralCrafts is not null && response.Timelines.Madstones is not null;

    // Smokes and item/ability uses, for the prediction models' samples only (never saved with the analysis). Null when
    // the document lacks one of the three sections: unknown activity is not "nothing was used".
    internal static ReplayActivity? ReadActivity(ReplayParserMatchResponse response)
    {
        if (response.Vision?.Smokes is not { } smokes || response.Items?.Uses is not { } itemUses
            || response.Abilities?.Uses is not { } abilityUses)
        {
            return null;
        }

        var players = Required(response.Players, "players").Select(p => p ?? throw Empty("player"))
            .ToDictionary(p => p.Player ?? throw Missing("players.player"));
        ReplayParserMatchPlayer PlayerOf(int? index) =>
            players.TryGetValue(index ?? throw Missing("player"), out var player)
                ? player : throw new InvalidDataException($"Unknown player {index}.");

        IEnumerable<RecordedUse> Uses(int? player, IReadOnlyList<JsonElement>? tuples, string name) =>
            Required(tuples, name).Select(tuple => ToRecordedUse(PlayerOf(player).HeroId ?? throw Missing("players.heroId"), tuple));

        return new ReplayActivity(
            smokes.Select(smoke => smoke ?? throw Empty("smoke"))
                .Select(smoke => new SmokeUse(
                    PlayerOf(smoke.Player).HeroId ?? throw Missing("players.heroId"), ToTeam(PlayerOf(smoke.Player).Team),
                    smoke.T ?? throw Missing("smoke.t")))
                .ToList(),
            itemUses.Select(entry => entry ?? throw Empty("item uses entry"))
                .SelectMany(entry => Uses(entry.Player, entry.Items, "item uses.items")).ToList(),
            abilityUses.Select(entry => entry ?? throw Empty("ability uses entry"))
                .SelectMany(entry => WithTargets(Uses(entry.Player, entry.Abilities, "ability uses.abilities").ToList(), entry.Targets))
                .ToList(),
            response.Combat?.Buckets is { } buckets
                ? buckets.Select(entry => entry ?? throw Empty("combat buckets entry"))
                    .SelectMany(entry => ToDamageBuckets(HeroOf(entry.Player), entry))
                    .ToList()
                : null,
            response.Items?.Slots is { } slots
                ? slots.Select(entry => entry ?? throw Empty("slots entry"))
                    .SelectMany(entry => Required(entry.Changes, "slots.changes").Select(tuple => ToSlotChange(HeroOf(entry.Player), tuple)))
                    .ToList()
                : null,
            Cooldowns(response.Items?.Cooldowns, "item cooldowns"),
            Cooldowns(response.Abilities?.Cooldowns, "ability cooldowns"),
            response.Buybacks?.Select(buyback => buyback ?? throw Empty("buyback"))
                .Select(buyback => new BuybackUse(HeroOf(buyback.Player), buyback.T ?? throw Missing("buyback.t"),
                    buyback.Cost is < 0 ? throw new InvalidDataException("Invalid buyback cost.") : buyback.Cost))
                .ToList(),
            TeamActions(response.Vision?.Scans, "scan"),
            TeamActions(response.Vision?.Glyphs, "glyph"),
            response.Objectives?.Runes?.Select(rune => rune ?? throw Empty("rune"))
                .Select(rune => new RunePickup(HeroOf(rune.Player), ToTeam(PlayerOf(rune.Player).Team), rune.T ?? throw Missing("rune.t"),
                    rune.RuneType ?? throw Missing("rune.runeType")))
                .ToList(),
            response.Timelines?.Waves?.Select(lane => lane ?? throw Empty("waves entry"))
                .SelectMany(lane => ToWaveFronts(lane.Lane switch
                {
                    "top" => Lane.Top,
                    "middle" => Lane.Middle,
                    "bottom" => Lane.Bottom,
                    _ => throw new InvalidDataException($"Unknown wave lane '{lane.Lane}'."),
                }, Required(lane.Fronts, "waves.fronts"), lane.Sizes, lane.Strength, lane.Composition))
                .ToList(),
            response.Timelines?.DayNight?.Select(ToDayNight).ToList(),
            response.Combat?.HeroHits?.Select(tuple => ToHeroHit(tuple, HeroOf)).ToList(),
            response.Combat?.CreepKills?.Select(tuple => ToCreepKill(tuple, HeroOf)).ToList(),
            response.Combat?.Debuffs?.Where(EndsAfterStart).Select(tuple => ToDebuff(tuple, HeroOf)).ToList(),
            response.Combat?.Buffs?.Where(EndsAfterStart).Select(tuple => ToBuff(tuple, HeroOf)).ToList(),
            response.Objectives?.BuildingHealth?.Select(entry => ToBuildingHealth(entry ?? throw Empty("building health"))).ToList(),
            response.Timelines?.OffLaneCreeps?.Select(ToOffLaneCreeps).ToList(),
            response.Combat?.LaneCreepDeaths?.Select(tuple => ToLaneCreepDeath(tuple, HeroOf)).ToList(),
            response.Timelines?.CreepHeroDamage?.Select(tuple => ToCreepHeroHits(tuple, HeroOf)).ToList(),
            response.Objectives?.NeutralCamps?.Select(camp => ToNeutralCamp(camp ?? throw Empty("neutral camp"))).ToList(),
            response.Combat?.Economy?.Select(tuple => ToEconomyBucket(tuple, HeroOf)).ToList(),
            response.Vision?.Pings?.Select(tuple => ToMapPing(tuple, player => (HeroOf(player), ToTeam(PlayerOf(player).Team)))).ToList(),
            response.Objectives?.RunesOnMap?.Select(ToRuneOnMap).ToList(),
            // Sparse and left out for a player with nothing: one player with the list means the document has them.
            response.Combat?.Buckets is { } typedBuckets && typedBuckets.Any(entry => entry?.ByType is not null)
                ? typedBuckets.SelectMany(entry => (entry!.ByType ?? []).Select(tuple => ToTypedDamage(HeroOf(entry.Player), tuple))).ToList()
                : null,
            response.Combat?.Buckets is { } healingBuckets && healingBuckets.Any(entry => entry?.Healing is not null)
                ? healingBuckets.SelectMany(entry => (entry!.Healing ?? []).Select(tuple => ToHealing(HeroOf(entry.Player), tuple))).ToList()
                : null,
            response.Timelines?.Couriers?.Select(courier => ToCourier(courier ?? throw Empty("courier"))).ToList(),
            response.Items?.NeutralCrafts?.Select(entry => entry ?? throw Empty("neutral crafts entry"))
                .SelectMany(entry => Required(entry.Crafts, "neutralCrafts.crafts").Select(tuple => ToNeutralCraft(HeroOf(entry.Player), tuple)))
                .ToList(),
            response.Timelines?.Madstones?.Select(entry => entry ?? throw Empty("madstones entry"))
                .Select(entry => new MadstoneTrack(HeroOf(entry.Player),
                    Required(entry.Samples, "madstones.samples").Select(ToMadstoneReading).ToList()))
                .ToList(),
            ReadTrees(response),
            response.Timelines?.VisionUnits?.Select(ToVisionUnits).ToList(),
            CooldownChanges(response.Items?.CooldownChanges, "item cooldown changes"),
            CooldownChanges(response.Abilities?.CooldownChanges, "ability cooldown changes"))
        { EconomyStates = ReadEconomyStates(response) };

        List<CooldownCorrection>? CooldownChanges(IReadOnlyList<ReplayParserPlayerCooldownChanges?>? entries, string name) =>
            entries?.Select(entry => entry ?? throw Empty(name))
                .SelectMany(entry => Required(entry.Changes, name).Select(tuple => ToCooldownChange(HeroOf(entry.Player), tuple)))
                .ToList();

        // Targets[i] is the target of uses[i]; a different length means the lists are not parallel.
        List<RecordedUse> WithTargets(List<RecordedUse> uses, IReadOnlyList<JsonElement>? targets)
        {
            if (targets is null)
            {
                return uses;
            }

            if (targets.Count != uses.Count)
            {
                throw new InvalidDataException("Ability targets are not parallel to the uses.");
            }

            return uses.Select((use, index) =>
            {
                var target = targets[index];
                if (target.ValueKind != JsonValueKind.Array || target.GetArrayLength() is not (3 or 4)
                    || Enumerable.Range(0, target.GetArrayLength()).Any(i => target[i].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)))
                {
                    throw new InvalidDataException("An ability target is not [player, x, y, source].");
                }

                int? At(int i) => target[i].ValueKind == JsonValueKind.Null ? null : (int)Math.Round(target[i].GetDouble());
                if (target.GetArrayLength() == 4 && target[3].ValueKind != JsonValueKind.Null && !target[3].TryGetInt32(out _))
                    throw new InvalidDataException("Ability target source is not an integer.");
                return use with
                {
                    TargetHeroId = At(0) is { } player ? HeroOf(player) : null,
                    TargetX = At(1),
                    TargetY = At(2),
                    PointSource = target.GetArrayLength() == 4 ? At(3) : null
                };
            }).ToList();
        }

        int HeroOf(int? player) => PlayerOf(player).HeroId ?? throw Missing("players.heroId");

        static List<TeamAction>? TeamActions(IReadOnlyList<ReplayParserTeamEvent?>? events, string name) =>
            events?.Select(action => action ?? throw Empty(name))
                .Select(action => new TeamAction(ToTeam(action.Team), action.T ?? throw Missing($"{name}.t")))
                .ToList();

        List<CooldownStart>? Cooldowns(IReadOnlyList<ReplayParserPlayerCooldowns?>? entries, string name) =>
            entries?.Select(entry => entry ?? throw Empty($"{name} entry"))
                .SelectMany(entry => Required(entry.Cooldowns, $"{name}.cooldowns").Select(tuple => ToCooldown(HeroOf(entry.Player), tuple)))
                .ToList();
    }

    private static BuildingHealth ToBuildingHealth(ReplayParserBuildingHealth entry) => new(
        entry.Building ?? throw Missing("buildingHealth.building"),
        ToTeam(entry.Team),
        entry.Kind switch
        {
            "tower" => BuildingKind.Tower,
            "barracks" => BuildingKind.Barracks,
            "ancient" => BuildingKind.Ancient,
            _ => throw new InvalidDataException($"Unknown building kind '{entry.Kind}'."),
        },
        entry.Lane switch
        {
            null => null,
            "top" => Lane.Top,
            "middle" => Lane.Middle,
            "bottom" => Lane.Bottom,
            _ => throw new InvalidDataException($"Unknown building lane '{entry.Lane}'."),
        },
        entry.Tier,
        entry.X ?? throw Missing("buildingHealth.x"),
        entry.Y ?? throw Missing("buildingHealth.y"),
        Required(entry.Health, "buildingHealth.health").Select(tuple =>
            tuple.ValueKind == JsonValueKind.Array && tuple.GetArrayLength() == 3 && tuple[0].TryGetInt32(out var t)
                && tuple[1].TryGetInt32(out var health) && tuple[2].TryGetInt32(out var maxHealth)
                ? new BuildingHealthReading(t, health, maxHealth)
                : throw new InvalidDataException("A building health reading is not [t, health, maxHealth].")).ToList());

    // A debuff or a buff, its kind refined in the Domain (DebuffKinds, BuffKinds).
    private static Debuff ToDebuff(JsonElement tuple, Func<int?, int> heroOf)
    {
        var m = ReadModifier(tuple, heroOf, "debuff");
        return new Debuff(m.Start, m.End, m.Source, m.Target, m.Modifier, DebuffKinds.Of(m.Kind, m.Modifier));
    }

    private static Buff ToBuff(JsonElement tuple, Func<int?, int> heroOf)
    {
        var m = ReadModifier(tuple, heroOf, "buff");
        return new Buff(m.Start, m.End, m.Source, m.Target, m.Modifier, BuffKinds.Of(m.Kind, m.Modifier));
    }

    // A modifier applied during a pause gets a start too late: the Go clock counts the pause only when it ends, so the
    // removal can land before the application (0.14% of the rows, in the matches with pauses). Those times are not
    // trustworthy and the row is dropped; a malformed tuple still fails in ReadModifier.
    private static bool EndsAfterStart(JsonElement tuple) =>
        tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() < 2
        || tuple[0].ValueKind != JsonValueKind.Number || tuple[1].ValueKind != JsonValueKind.Number
        || tuple[1].GetDouble() >= tuple[0].GetDouble();

    // [start, end | null, source | null, target, "modifier", "kind"] (player indexes; the same tuple for both lists).
    private static (double Start, double? End, int? Source, int Target, string Modifier, string Kind) ReadModifier(
        JsonElement tuple, Func<int?, int> heroOf, string name)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 6 || tuple[0].ValueKind != JsonValueKind.Number
            || tuple[1].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)
            || tuple[2].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)
            || !tuple[3].TryGetInt32(out var target) || tuple[4].ValueKind != JsonValueKind.String
            || tuple[5].ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"A {name} is not [start, end, source, target, modifier, kind].");
        }

        return (tuple[0].GetDouble(),
            tuple[1].ValueKind == JsonValueKind.Null ? null : tuple[1].GetDouble(),
            tuple[2].ValueKind == JsonValueKind.Null ? null : heroOf(tuple[2].GetInt32()),
            heroOf(target),
            tuple[4].GetString()!,
            tuple[5].GetString()!);
    }

    // [t, isDay] -> a day/night change.
    private static DayNightChange ToDayNight(JsonElement tuple) =>
        tuple.ValueKind == JsonValueKind.Array && tuple.GetArrayLength() == 2 && tuple[0].TryGetInt32(out var seconds)
        && tuple[1].ValueKind is JsonValueKind.True or JsonValueKind.False
            ? new DayNightChange(seconds, tuple[1].GetBoolean())
            : throw new InvalidDataException("A day/night change is not [t, isDay].");

    // [t, index, alive] -> a tree going down or growing back.
    // Each hero's counters of the map's objectives from players[].final; null unless every player has all of them.
    private static Dictionary<int, MapObjectiveCounts>? ObjectiveCounts(IReadOnlyList<ReplayParserMatchPlayer> players)
    {
        var counts = new Dictionary<int, MapObjectiveCounts>();
        foreach (var player in players)
        {
            if (player is not { HeroId: { } heroId, Final: { ValueKind: JsonValueKind.Object } block }
                || block.Deserialize<ReplayParserPlayerFinal>(Coaching.CoachingJson.Options) is not
                {
                    BountyRunes: >= 0 and var bounty, PowerRunes: >= 0 and var power, WaterRunes: >= 0 and var water,
                    WisdomShrinesTaken: >= 0 and var wisdom, LotusesTaken: >= 0 and var lotuses, WatchersTaken: >= 0 and var watchers,
                    OutpostsCaptured: >= 0 and var outposts, TormentorKills: >= 0 and var tormentors, CampsStacked: >= 0 and var stacked,
                })
            {
                return null;
            }

            counts[heroId] = new MapObjectiveCounts(bounty, power, water, wisdom, lotuses, watchers, outposts, tormentors, stacked);
        }

        return counts;
    }

    // The activity for the play quality metrics: a malformed activity section only leaves them unknown, it never fails
    // an analysis that did not need it before.
    private static ReplayActivity? ActivityForMetrics(ReplayParserMatchResponse response)
    {
        try
        {
            return ReadActivity(response);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    // timelines.trees (MP3); null in documents from before it.
    private static TreeTimeline? ReadTrees(ReplayParserMatchResponse response) =>
        response.Timelines?.Trees is { } trees
            ? new TreeTimeline(Required(trees.DeadAtStart, "trees.deadAtStart"),
                Required(trees.Changes, "trees.changes").Select(ToTreeChange).ToList())
            : null;

    private static TreeChange ToTreeChange(JsonElement tuple) =>
        tuple.ValueKind == JsonValueKind.Array && tuple.GetArrayLength() == 3 && tuple[0].TryGetInt32(out var seconds)
        && tuple[1].TryGetInt32(out var index) && tuple[2].ValueKind is JsonValueKind.True or JsonValueKind.False
            ? new TreeChange(seconds, index, tuple[2].GetBoolean())
            : throw new InvalidDataException("A tree change is not [t, index, alive].");

    // [t, [[x, y, day, night]...radiant], [[...]...dire]] -> each team's moving vision at a second.
    private static VisionUnitsSample ToVisionUnits(JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 3 || !tuple[0].TryGetInt32(out var seconds)
            || tuple[1].ValueKind != JsonValueKind.Array || tuple[2].ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("A vision units sample is not [t, radiant, dire].");
        }

        static List<VisionCircle> Circles(JsonElement list) => list.EnumerateArray().Select(circle =>
            circle.ValueKind == JsonValueKind.Array && circle.GetArrayLength() == 4 && circle[0].TryGetInt32(out var x)
            && circle[1].TryGetInt32(out var y) && circle[2].TryGetInt32(out var day) && circle[3].TryGetInt32(out var night)
                ? new VisionCircle(x, y, day, night)
                : throw new InvalidDataException("A vision circle is not [x, y, day, night].")).ToList();

        return new VisionUnitsSample(seconds, Circles(tuple[1]), Circles(tuple[2]));
    }

    // [t, attacker, target, amount] (player indexes) -> a hero hit.
    private static HeroHit ToHeroHit(JsonElement tuple, Func<int?, int> heroOf) =>
        tuple.ValueKind == JsonValueKind.Array && tuple.GetArrayLength() == 4 && tuple[0].TryGetInt32(out var seconds)
        && tuple[1].TryGetInt32(out var attacker) && tuple[2].TryGetInt32(out var target)
        && tuple[3].TryGetInt32(out var amount) && amount >= 0
            ? new HeroHit(seconds, heroOf(attacker), heroOf(target), amount)
            : throw new InvalidDataException("A hero hit is not [t, attacker, target, amount].");

    // [t, player, "lane" | "deny" | "neutral", x | null, y | null] -> a creep last hit.
    private static CreepKill ToCreepKill(JsonElement tuple, Func<int?, int> heroOf)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 5 || tuple[0].ValueKind != JsonValueKind.Number
            || !tuple[1].TryGetInt32(out var player) || tuple[2].ValueKind != JsonValueKind.String
            || tuple[3].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)
            || tuple[4].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null))
        {
            throw new InvalidDataException("A creep kill is not [t, player, kind, x, y].");
        }

        var kind = tuple[2].GetString() switch
        {
            "lane" => CreepKillKind.Lane,
            "deny" => CreepKillKind.Deny,
            "neutral" => CreepKillKind.Neutral,
            var other => throw new InvalidDataException($"Unknown creep kill kind '{other}'."),
        };
        int? At(int index) => tuple[index].ValueKind == JsonValueKind.Null ? null : tuple[index].GetInt32();
        return new CreepKill(heroOf(player), tuple[0].GetDouble(), kind, At(3), At(4));
    }

    // A lane's fronts with their counts: sizes[i] is [t, radiantInLane, direInLane, radiantAtFront, direAtFront] for
    // fronts[i]. A different length or t means the lists are not parallel: unreadable, never matched by guessing.
    // strength[i] (W1) is [t, radiantHealth, direHealth, radiantHealthAtFront, direHealthAtFront, radiantSiege, direSiege,
    // radiantCenter | null, direCenter | null], parallel the same way.
    // composition[i] (W5) is [t, then for Radiant and Dire: melee, ranged, siege, flagbearer, super, mega, lowAtFront].
    private static IEnumerable<WaveFront> ToWaveFronts(
        Lane lane, IReadOnlyList<JsonElement> fronts, IReadOnlyList<JsonElement>? sizes, IReadOnlyList<JsonElement>? strength = null,
        IReadOnlyList<JsonElement>? composition = null)
    {
        if ((sizes is not null && sizes.Count != fronts.Count) || (strength is not null && strength.Count != fronts.Count)
            || (composition is not null && composition.Count != fronts.Count))
        {
            throw new InvalidDataException("Wave sizes, strength or composition are not parallel to the fronts.");
        }

        return fronts.Select((tuple, index) =>
        {
            var front = ToWaveFront(lane, tuple);
            if (sizes is not null)
            {
                var size = sizes[index];
                if (size.ValueKind != JsonValueKind.Array || size.GetArrayLength() != 5
                    || Enumerable.Range(0, 5).Any(i => !size[i].TryGetInt32(out var value) || value < 0)
                    || size[0].GetInt32() != front.Seconds)
                {
                    throw new InvalidDataException("A wave size is not [t, radiantInLane, direInLane, radiantAtFront, direAtFront] of its front.");
                }

                front = front with { Counts = new WaveCounts(size[1].GetInt32(), size[2].GetInt32(), size[3].GetInt32(), size[4].GetInt32()) };
            }

            if (strength is not null)
            {
                var s = strength[index];
                if (s.ValueKind != JsonValueKind.Array || s.GetArrayLength() != 9
                    || Enumerable.Range(0, 7).Any(i => !s[i].TryGetInt32(out var value) || value < 0)
                    || s[0].GetInt32() != front.Seconds || s[5].GetInt32() > 1 || s[6].GetInt32() > 1
                    || s[7].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)
                    || s[8].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null))
                {
                    throw new InvalidDataException("A wave strength is not [t, health ×4, siege ×2, center ×2] of its front.");
                }

                int? Center(int i) => s[i].ValueKind == JsonValueKind.Null ? null : s[i].GetInt32();
                front = front with
                {
                    Strength = new WaveStrength(s[1].GetInt32(), s[2].GetInt32(), s[3].GetInt32(), s[4].GetInt32(),
                        s[5].GetInt32() == 1, s[6].GetInt32() == 1, Center(7), Center(8)),
                };
            }

            if (composition is not null)
            {
                var c = composition[index];
                if (c.ValueKind != JsonValueKind.Array || c.GetArrayLength() != 15
                    || Enumerable.Range(0, 15).Any(i => !c[i].TryGetInt32(out var value) || value < 0) || c[0].GetInt32() != front.Seconds)
                {
                    throw new InvalidDataException("A wave composition is not [t, 7 Radiant counts, 7 Dire counts] of its front.");
                }

                TeamWave Team(int from) => new(c[from].GetInt32(), c[from + 1].GetInt32(), c[from + 2].GetInt32(), c[from + 3].GetInt32(),
                    c[from + 4].GetInt32(), c[from + 5].GetInt32(), c[from + 6].GetInt32());
                front = front with { Composition = new WaveComposition(Team(1), Team(8)) };
            }

            return front;
        });
    }

    // [bucketStart, player, resource (0 gold, 1 XP), rawReason | null, amount] -> gold/XP by source in [t, t + 2).
    private static EconomyBucket ToEconomyBucket(JsonElement tuple, Func<int?, int> heroOf)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 5 || !tuple[0].TryGetInt32(out var t)
            || !tuple[1].TryGetInt32(out var player) || !tuple[2].TryGetInt32(out var resource) || resource is not (0 or 1)
            || tuple[3].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null) || !tuple[4].TryGetInt32(out var amount))
        {
            throw new InvalidDataException("An economy bucket is not [t, player, resource, reason, amount].");
        }

        var kind = resource == 0 ? EconomyResource.Gold : EconomyResource.Experience;
        int? reason = tuple[3].ValueKind == JsonValueKind.Null ? null : tuple[3].GetInt32();
        return new EconomyBucket(heroOf(player), t, kind, EconomyBucket.SourceOf(kind, reason), amount);
    }

    // [t, player, x, y, type] -> a map ping.
    private static MapPing ToMapPing(JsonElement tuple, Func<int, (int HeroId, Team Team)> heroOf)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 5 || tuple[0].ValueKind != JsonValueKind.Number
            || !tuple[1].TryGetInt32(out var player) || tuple[2].ValueKind != JsonValueKind.Number
            || tuple[3].ValueKind != JsonValueKind.Number || !tuple[4].TryGetInt32(out var type))
        {
            throw new InvalidDataException("A ping is not [t, player, x, y, type].");
        }

        var (heroId, team) = heroOf(player);
        return new MapPing(heroId, team, tuple[0].GetDouble(), (int)Math.Round(tuple[2].GetDouble()), (int)Math.Round(tuple[3].GetDouble()), type);
    }

    // A tuple of a fixed length whose items are all non-negative integers (after the leading t, which may be negative).
    private static int[] Integers(JsonElement tuple, int length, string what)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != length
            || Enumerable.Range(0, length).Any(i => !tuple[i].TryGetInt32(out var value) || (i > 0 && value < 0)))
        {
            throw new InvalidDataException($"A {what} entry is not {length} integers.");
        }

        return Enumerable.Range(0, length).Select(i => tuple[i].GetInt32()).ToArray();
    }

    private static TypedDamageBucket ToTypedDamage(int heroId, JsonElement tuple)
    {
        var v = Integers(tuple, 7, "buckets.byType");
        return new TypedDamageBucket(heroId, v[0], v[1], v[2], v[3], v[4], v[5], v[6]);
    }

    private static HealingBucket ToHealing(int heroId, JsonElement tuple)
    {
        var v = Integers(tuple, 3, "buckets.healing");
        return new HealingBucket(heroId, v[0], v[1], v[2]);
    }

    private static CourierTrack ToCourier(ReplayParserCourier courier) => new(
        courier.Id ?? throw Missing("couriers.id"),
        ToTeam(courier.Team),
        Required(courier.Samples, "couriers.samples").Select(tuple =>
            tuple.ValueKind == JsonValueKind.Array && tuple.GetArrayLength() is 4 or 5
                && Enumerable.Range(0, 4).All(i => tuple[i].ValueKind == JsonValueKind.Number) && tuple[0].TryGetInt32(out var t)
                && tuple[1].TryGetInt32(out var alive) && alive is 0 or 1 && tuple[2].TryGetInt32(out var x) && tuple[3].TryGetInt32(out var y)
                ? (t, alive == 1, x, y)
                : throw new InvalidDataException("A courier reading is not [t, alive, x, y, flying].")).ToList())
    {
        Flight = Required(courier.Samples, "couriers.samples").Select(tuple => new CourierFlight(tuple[0].GetInt32(),
            tuple.GetArrayLength() < 5 || tuple[4].ValueKind == JsonValueKind.Null ? null
                : tuple[4].ValueKind == JsonValueKind.True ? 1 : tuple[4].ValueKind == JsonValueKind.False ? 0
                : tuple[4].ValueKind == JsonValueKind.Number && tuple[4].TryGetInt32(out var raw) ? raw
                : throw new InvalidDataException("Courier flying is not a boolean, integer or null."))).ToList(),
    };

    // [t, runeType, x, y, appeared (1) / vanished (0)] -> a rune change on the map.
    private static RuneOnMap ToRuneOnMap(JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 5 || !tuple[0].TryGetInt32(out var t)
            || !tuple[1].TryGetInt32(out var type) || tuple[2].ValueKind != JsonValueKind.Number
            || tuple[3].ValueKind != JsonValueKind.Number || !tuple[4].TryGetInt32(out var present) || present is not (0 or 1))
        {
            throw new InvalidDataException("A rune on the map is not [t, type, x, y, appeared].");
        }

        return new RuneOnMap(t, type, (int)Math.Round(tuple[2].GetDouble()), (int)Math.Round(tuple[3].GetDouble()), present == 1);
    }

    // [t, lane, creepTeam, targetPlayer, hits] -> lane creep hits on a hero in the 2 s ending at t.
    private static CreepHeroHits ToCreepHeroHits(JsonElement tuple, Func<int?, int> heroOf)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 5 || !tuple[0].TryGetInt32(out var t)
            || tuple[1].ValueKind != JsonValueKind.String || tuple[2].ValueKind != JsonValueKind.String
            || !tuple[3].TryGetInt32(out var player) || !tuple[4].TryGetInt32(out var hits))
        {
            throw new InvalidDataException("A creep hero damage entry is not [t, lane, team, player, hits].");
        }

        return new CreepHeroHits(t, tuple[1].GetString() switch
        {
            "top" => Lane.Top,
            "middle" => Lane.Middle,
            "bottom" => Lane.Bottom,
            var other => throw new InvalidDataException($"Unknown lane '{other}'."),
        }, ToTeam(tuple[2].GetString()), heroOf(player), hits);
    }

    private static NeutralCamp ToNeutralCamp(ReplayParserNeutralCamp camp) => new(
        camp.Id ?? throw Missing("neutralCamps.id"),
        camp.X ?? throw Missing("neutralCamps.x"),
        camp.Y ?? throw Missing("neutralCamps.y"),
        camp.Type,
        Required(camp.Alive, "neutralCamps.alive").Select(tuple =>
            tuple.ValueKind == JsonValueKind.Array && tuple.GetArrayLength() == 2 && tuple[0].TryGetInt32(out var t)
                && tuple[1].TryGetInt32(out var alive)
                ? (t, alive)
                : throw new InvalidDataException("A camp reading is not [t, alive].")).ToList())
    { SpawnerType = camp.SpawnerType };

    // [t, radiantCount, direCount, radiantX | null, radiantY | null, direX | null, direY | null] -> off-lane creeps.
    private static OffLaneCreeps ToOffLaneCreeps(JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 7 || !tuple[0].TryGetInt32(out var t)
            || !tuple[1].TryGetInt32(out var radiant) || !tuple[2].TryGetInt32(out var dire)
            || Enumerable.Range(3, 4).Any(i => tuple[i].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)))
        {
            throw new InvalidDataException("An off-lane creeps entry is not [t, radiant, dire, x, y, x, y].");
        }

        int? At(int i) => tuple[i].ValueKind == JsonValueKind.Null ? null : (int)Math.Round(tuple[i].GetDouble());
        return new OffLaneCreeps(t, radiant, dire, At(3), At(4), At(5), At(6));
    }

    // [t, lane | null, creepTeam, killerKind, killerPlayer | null, x | null, y | null] -> a lane creep death.
    private static LaneCreepDeath ToLaneCreepDeath(JsonElement tuple, Func<int?, int> heroOf)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 7 || tuple[0].ValueKind != JsonValueKind.Number
            || tuple[1].ValueKind is not (JsonValueKind.String or JsonValueKind.Null) || tuple[2].ValueKind != JsonValueKind.String
            || tuple[3].ValueKind != JsonValueKind.String
            || Enumerable.Range(4, 3).Any(i => tuple[i].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)))
        {
            throw new InvalidDataException("A lane creep death is not [t, lane, team, killer, player, x, y].");
        }

        int? At(int i) => tuple[i].ValueKind == JsonValueKind.Null ? null : (int)Math.Round(tuple[i].GetDouble());
        return new LaneCreepDeath(
            tuple[0].GetDouble(),
            tuple[1].ValueKind == JsonValueKind.Null ? null : tuple[1].GetString() switch
            {
                "top" => Lane.Top,
                "middle" => Lane.Middle,
                "bottom" => Lane.Bottom,
                var other => throw new InvalidDataException($"Unknown lane '{other}'."),
            },
            ToTeam(tuple[2].GetString()),
            tuple[3].GetString() switch
            {
                "hero" => CreepKiller.Hero,
                "creep" => CreepKiller.Creep,
                "tower" => CreepKiller.Tower,
                "neutral" => CreepKiller.Neutral,
                _ => CreepKiller.Other,
            },
            tuple[4].ValueKind == JsonValueKind.Null ? null : heroOf(tuple[4].GetInt32()),
            At(5), At(6));
    }

    // [t, radiant, dire] -> a lane's creep fronts at a second.
    private static WaveFront ToWaveFront(Lane lane, JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 3 || !tuple[0].TryGetInt32(out var seconds)
            || tuple[1].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)
            || tuple[2].ValueKind is not (JsonValueKind.Number or JsonValueKind.Null))
        {
            throw new InvalidDataException("A wave front is not [t, radiant, dire].");
        }

        int? At(int index) => tuple[index].ValueKind == JsonValueKind.Null ? null : tuple[index].GetInt32();
        return new WaveFront(lane, seconds, At(1), At(2));
    }

    // [t, slot, "item_name" | null] -> a slot change.
    private static SlotChange ToSlotChange(int heroId, JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 3
            || !tuple[0].TryGetInt32(out var seconds) || !tuple[1].TryGetInt32(out var slot)
            || tuple[2].ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            throw new InvalidDataException("A slot change is not [t, slot, name].");
        }

        return new SlotChange(heroId, seconds, slot, tuple[2].ValueKind == JsonValueKind.Null ? null : tuple[2].GetString());
    }

    // [t, "item_artifact", artifactLevel | null, tier, "item_enhancement" | null, enhancementLevel | null] -> a craft.
    private static NeutralCraft ToNeutralCraft(int heroId, JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 6
            || !tuple[0].TryGetDouble(out var seconds) || tuple[1].ValueKind != JsonValueKind.String
            || !tuple[3].TryGetInt32(out var tier)
            || tuple[4].ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
            || new[] { tuple[2], tuple[5] }.Any(level => level.ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)))
        {
            throw new InvalidDataException("A neutral craft is not [t, artifact, level, tier, enhancement, level].");
        }

        int? Level(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();
        return new NeutralCraft(heroId, seconds, tuple[1].GetString()!, Level(tuple[2]), tier,
            tuple[4].ValueKind == JsonValueKind.Null ? null : tuple[4].GetString(), Level(tuple[5]));
    }

    // [t, inHand, acquired, found, neutralGold] -> a Madstone reading.
    private static MadstoneReading ToMadstoneReading(JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 5
            || Enumerable.Range(0, 5).Any(index => tuple[index].ValueKind != JsonValueKind.Number))
        {
            throw new InvalidDataException("A Madstone reading is not [t, inHand, acquired, found, neutralGold].");
        }

        int At(int index) => (int)Math.Round(tuple[index].GetDouble());
        return new MadstoneReading(At(0), At(1), At(2), At(3), At(4));
    }

    // [t, "name", readyAt] -> a cooldown that started.
    // [t, "name", entity, serial, readyAt] -> a cooldown drop.
    private static CooldownCorrection ToCooldownChange(int heroId, JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 5 || tuple[0].ValueKind != JsonValueKind.Number
            || tuple[1].ValueKind != JsonValueKind.String || !tuple[2].TryGetInt32(out var entity) || !tuple[3].TryGetInt32(out var serial)
            || tuple[4].ValueKind != JsonValueKind.Number)
        {
            throw new InvalidDataException("A cooldown change is not [t, name, entity, serial, readyAt].");
        }

        return new CooldownCorrection(heroId, tuple[0].GetDouble(), tuple[1].GetString()!, entity, serial, tuple[4].GetDouble());
    }

    private static CooldownStart ToCooldown(int heroId, JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 3
            || tuple[0].ValueKind != JsonValueKind.Number || tuple[1].ValueKind != JsonValueKind.String
            || tuple[2].ValueKind != JsonValueKind.Number)
        {
            throw new InvalidDataException("A cooldown is not [t, name, readyAt].");
        }

        return new CooldownStart(heroId, tuple[0].GetDouble(), tuple[1].GetString()!, tuple[2].GetDouble());
    }

    // The to/from lists of one player merged into one bucket per start second.
    private static IEnumerable<DamageBucket> ToDamageBuckets(int heroId, ReplayParserCombatBuckets entry)
    {
        static IEnumerable<(int Seconds, int Amount)> Read(IReadOnlyList<JsonElement>? tuples, string name) =>
            Required(tuples, name).Select(tuple =>
                tuple.ValueKind == JsonValueKind.Array && tuple.GetArrayLength() == 2
                && tuple[0].TryGetInt32(out var seconds) && tuple[1].TryGetInt32(out var amount) && amount >= 0
                    ? (seconds, amount)
                    : throw new InvalidDataException("A combat bucket is not [t, amount]."));

        var to = Read(entry.ToHeroes, "buckets.toHeroes").ToDictionary(bucket => bucket.Seconds, bucket => bucket.Amount);
        var from = Read(entry.FromHeroes, "buckets.fromHeroes").ToDictionary(bucket => bucket.Seconds, bucket => bucket.Amount);
        return to.Keys.Union(from.Keys).Order()
            .Select(seconds => new DamageBucket(heroId, seconds, to.GetValueOrDefault(seconds), from.GetValueOrDefault(seconds)));
    }

    // [t, "name"] -> a recorded use, with the replay's fractional time.
    private static RecordedUse ToRecordedUse(int heroId, JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 2
            || tuple[0].ValueKind != JsonValueKind.Number || tuple[1].ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(tuple[1].GetString()))
        {
            throw new InvalidDataException("A use is not [t, \"name\"].");
        }

        return new RecordedUse(heroId, tuple[0].GetDouble(), tuple[1].GetString()!);
    }

    // [t, x, y] -> a position sample.
    private static PositionSample ToPositionSample(JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 3
            || tuple.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.Number))
        {
            throw new InvalidDataException("A position is not [t, x, y].");
        }

        return new PositionSample(
            (int)Math.Round(tuple[0].GetDouble()), (int)Math.Round(tuple[1].GetDouble()), (int)Math.Round(tuple[2].GetDouble()));
    }

    // npc_dota_goodguys_tower2_mid, npc_dota_badguys_tower4, npc_dota_goodguys_melee_rax_bot, npc_dota_badguys_fort.
    private static readonly Regex BuildingName = new(
        "^npc_dota_(?:goodguys|badguys)_(?:tower(?<tier>[1-4])(?:_(?<towerLane>top|mid|bot))?|(?<rax>melee|range)_rax_(?<raxLane>top|mid|bot)|(?<fort>fort))$",
        RegexOptions.CultureInvariant);

    // Null for a building that isn't an objective we show (fillers, or a name from a newer patch): skipped, the
    // replay is still readable.
    private static MapObjective? ToBuildingObjective(ReplayParserBuildingKill kill, Func<int?, int?> heroOf)
    {
        var match = BuildingName.Match(kill.Building ?? throw Missing("building.building"));
        if (!match.Success)
        {
            return null;
        }

        var owner = ToTeam(kill.Team);
        var denied = kill.Denied ?? false;
        var takenBy = denied ? owner : owner == Team.Radiant ? Team.Dire : Team.Radiant;
        var seconds = (int)Math.Round(kill.T ?? throw Missing("building.t"));
        var killer = heroOf(kill.Killer);

        if (match.Groups["fort"].Success)
        {
            return new MapObjective(ObjectiveKind.Ancient, seconds, takenBy, killer, owner, denied: denied);
        }

        if (match.Groups["rax"].Success)
        {
            return new MapObjective(
                ObjectiveKind.Barracks, seconds, takenBy, killer, owner,
                lane: ToBuildingLane(match.Groups["raxLane"].Value),
                meleeBarracks: match.Groups["rax"].Value == "melee",
                denied: denied);
        }

        var tier = int.Parse(match.Groups["tier"].Value, CultureInfo.InvariantCulture);
        var towerLane = match.Groups["towerLane"];
        // Towers 1-3 stand in a lane, tier 4 in the base: any other combination is a name we don't know.
        if (towerLane.Success != (tier < 4))
        {
            return null;
        }

        return new MapObjective(
            ObjectiveKind.Tower, seconds, takenBy, killer, owner, towerTier: tier,
            lane: towerLane.Success ? ToBuildingLane(towerLane.Value) : null,
            denied: denied);
    }

    private static MapObjective ToCreatureObjective(ObjectiveKind kind, ReplayParserCreatureKill kill, Func<int?, int?> heroOf) =>
        new(
            kind,
            (int)Math.Round(kill.T ?? throw Missing($"{kind}.t")),
            ToTeam(kill.Team),
            heroOf(kill.Killer),
            aegisHeroId: kind == ObjectiveKind.Roshan ? heroOf(kill.AegisPlayer) : null);

    private static Lane ToBuildingLane(string lane) => lane switch
    {
        "top" => Lane.Top,
        "mid" => Lane.Middle,
        _ => Lane.Bottom,
    };

    // [t, "item_name"] -> a purchase keyed like the item catalog (without "item_").
    private static ItemPurchase ToPurchase(int heroId, JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 2
            || tuple[0].ValueKind != JsonValueKind.Number || tuple[1].ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("A purchase is not [t, \"item_name\"].");
        }

        var name = tuple[1].GetString()!;
        const string Prefix = "item_";
        return new ItemPurchase(
            heroId,
            (int)Math.Round(tuple[0].GetDouble()),
            name.StartsWith(Prefix, StringComparison.Ordinal) ? name[Prefix.Length..] : name);
    }

    private static WardPlacement ToWard(ReplayParserWard ward, Dictionary<int, int> heroOf)
    {
        int? HeroOf(int? index) => index is { } i
            ? heroOf.TryGetValue(i, out var heroId) ? heroId : throw new InvalidDataException($"Unknown player {i}.")
            : null;

        var placedAt = (int)Math.Round(ward.PlacedAt ?? throw Missing("ward.placedAt"));
        return new WardPlacement(
            kind: ward.Type switch
            {
                "observer" => WardKind.Observer,
                "sentry" => WardKind.Sentry,
                _ => throw new InvalidDataException($"Unknown ward type '{ward.Type}'."),
            },
            team: ToTeam(ward.Team),
            placedByHeroId: HeroOf(ward.Player),
            x: ward.X ?? throw Missing("ward.x"),
            y: ward.Y ?? throw Missing("ward.y"),
            placedAt: placedAt,
            // Rounded separately, so a ward gone a fraction of a second after it was placed can't end before it.
            removedAt: ward.RemovedAt is { } removedAt ? Math.Max(placedAt, (int)Math.Round(removedAt)) : null,
            destroyed: ward.Destroyed ?? false,
            destroyedByHeroId: HeroOf(ward.DestroyedBy));
    }

    private static HeroDeath ToDeath(ReplayParserKill kill, Dictionary<int, int> heroOf)
    {
        int HeroOf(int index) =>
            heroOf.TryGetValue(index, out var heroId) ? heroId : throw new InvalidDataException($"Unknown player {index}.");

        var killer = kill.Killer is not null ? KillerKind.Hero : ToKillerKind(kill.KillerUnit);
        return new HeroDeath(
            heroId: HeroOf(kill.Victim ?? throw Missing("kill.victim")),
            seconds: (int)Math.Round(kill.T ?? throw Missing("kill.t")),
            x: kill.X ?? throw Missing("kill.x"),
            y: kill.Y ?? throw Missing("kill.y"),
            killer: killer,
            killerHeroId: kill.Killer is { } index ? HeroOf(index) : null,
            reincarnated: kill.Reincarnated ?? false,
            assistHeroIds: (kill.Assists ?? []).Select(HeroOf).ToList());
    }

    // Combat log names of what dealt the blow when it wasn't a player (contract: Kill.KillerUnit). A hero name
    // without a player (e.g. a kill the replay does not attribute) is still a hero.
    private static KillerKind ToKillerKind(string? unit) => unit switch
    {
        null or "" => KillerKind.Other,
        _ when unit.StartsWith("npc_dota_hero_", StringComparison.Ordinal) => KillerKind.Hero,
        _ when unit.Contains("tower", StringComparison.Ordinal) || unit.Contains("fort", StringComparison.Ordinal)
            || unit.Contains("rax", StringComparison.Ordinal) || unit.Contains("fountain", StringComparison.Ordinal) => KillerKind.Tower,
        _ when unit.StartsWith("npc_dota_creep", StringComparison.Ordinal) || unit.Contains("siege", StringComparison.Ordinal) => KillerKind.Creeps,
        _ when unit.StartsWith("npc_dota_neutral", StringComparison.Ordinal) => KillerKind.Neutrals,
        _ when unit.StartsWith("npc_dota_roshan", StringComparison.Ordinal) => KillerKind.Roshan,
        _ => KillerKind.Other,
    };

    // Point i is minute i; the last one is the end of the match (kept after the previous point even when the match
    // ended exactly on a minute).
    private static List<int> Seconds(int count, double durationSeconds)
    {
        var seconds = Enumerable.Range(0, count).Select(minute => minute * 60).ToList();
        if (count > 1)
        {
            seconds[^1] = Math.Max((int)Math.Round(durationSeconds), seconds[^2] + 1);
        }

        return seconds;
    }

    private static int Value(IReadOnlyList<int>? values, string name, int at) =>
        Required(values, name) is { } list && at < list.Count ? list[at] : throw Missing(name);

    private static T Required<T>(T? value, string name) where T : class => value ?? throw Missing(name);

    private static InvalidDataException Missing(string name) => new($"{name} is missing.");

    private static InvalidDataException Empty(string name) => new($"A {name} is empty.");

    private static Team ToTeam(string? team) => team switch
    {
        "radiant" => Team.Radiant,
        "dire" => Team.Dire,
        _ => throw new InvalidDataException($"Unknown team '{team}'."),
    };

    // Spelled out instead of Enum.Parse, which would also accept numbers and other casings.
    private static Lane ToLane(string? lane) => lane switch
    {
        "top" => Lane.Top,
        "middle" => Lane.Middle,
        "bottom" => Lane.Bottom,
        "jungle" => Lane.Jungle,
        "roaming" => Lane.Roaming,
        _ => throw new InvalidDataException($"Unknown lane '{lane}'."),
    };
}
