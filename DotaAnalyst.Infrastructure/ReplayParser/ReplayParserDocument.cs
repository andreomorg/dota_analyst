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

// startedAt: Unix seconds of the horn (null when the replay does not say): picks the patch's map for the terrain metrics.
internal sealed record ReplayParserMatchInfo(double? DurationSeconds, uint? EngineBuild = null,
    string? ProducerVersion = null, bool? MatchEnded = null, long? StartedAt = null);

// role: role-queue position 1-5; null without role queue (Turbo, unranked).
// final: the end-of-match numbers (read by ReplayParserFacts as a final fact). players[].facet is not read: 7.41 has no
// facet to choose (always null in the local replays).
internal sealed record ReplayParserMatchPlayer(int? Player, int? HeroId, string? Team, int? Role, JsonElement? Final = null);

internal sealed record ReplayParserLaning(int? EndsAtSeconds, IReadOnlyList<ReplayParserPlayerLane?>? Lanes);

internal sealed record ReplayParserPlayerLane(int? Player, string? Lane);

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
