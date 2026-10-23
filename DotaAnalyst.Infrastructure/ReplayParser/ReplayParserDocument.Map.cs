using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Infrastructure.ReplayParser;

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

// A scan or a glyph: when and which team.
internal sealed record ReplayParserTeamEvent(double? T, string? Team);

internal sealed record ReplayParserRune(double? T, int? Player, int? RuneType);
