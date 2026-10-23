using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Infrastructure.ReplayParser;

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

// One hero death; killer is a player index (null when no player dealt the blow), killerUnit the combat log name.
internal sealed record ReplayParserKill(
    double? T, int? Victim, int? Killer, string? KillerUnit, int? X, int? Y, bool? Reincarnated,
    IReadOnlyList<int>? Assists);

internal sealed record ReplayParserBuyback(double? T, int? Player, int? Cost = null);
