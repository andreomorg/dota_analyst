using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>A whole match, analyzed from the point of view of one player.</summary>
/// <param name="MatchId">Dota match ID.</param>
/// <param name="DurationSeconds">Match duration, in seconds.</param>
/// <param name="StartTime">When the match started (UTC).</param>
/// <param name="RadiantWon">True when Radiant won, false when Dire won; null when the result is not known.</param>
/// <param name="RadiantScore">Radiant kills.</param>
/// <param name="DireScore">Dire kills.</param>
/// <param name="IsParsed">True when OpenDota parsed the replay (detailed data available).</param>
/// <param name="Radiant">The Radiant players.</param>
/// <param name="Dire">The Dire players.</param>
/// <param name="Performance">How the requested player did; null when they are not in the match (or have an anonymous profile).</param>
/// <param name="ReplayStatus">Where our own reading of the replay stands (every match with a replay; notNeeded is no longer used). While processing, ask again in a few seconds.</param>
/// <param name="Laning">The player's laning phase, read from the replay; null unless replayStatus is ready and the player is in the match.</param>
/// <param name="Timeline">Net worth and experience during the whole match, read from the replay; null unless replayStatus is ready (and for replays read before the timeline existed).</param>
/// <param name="Deaths">Where and when the player died, read from the replay, in time order; null unless replayStatus is ready and the player is in the match.</param>
/// <param name="Wards">Every ward of both teams, read from the replay, in the order they were placed; null unless replayStatus is ready.</param>
/// <param name="Damage">The player's damage to and from enemy heroes and by ability/item, read from the replay; null unless replayStatus is ready and the player is in the match.</param>
/// <param name="Fights">The teamfights of the match (3+ deaths at most 15 s apart), in time order, with the player's part; null unless replayStatus is ready.</param>
/// <param name="Objectives">Towers, barracks and Ancients that fell and Roshan/Tormentor kills of both teams, read from the replay, in time order (the buildings of the base besides towers 4 left out); null unless replayStatus is ready.</param>
/// <param name="SmartStats">Numbers that show where the player could improve (positioning, economy, vision), compared with the enemy of the same role and the match average, and what to improve; null unless replayStatus is ready and the player is in the match, and for matches without role queue (Turbo, unranked).</param>
/// <param name="ItemBuild">The player's starting items and when each item of the build was ready (approximate: items sold or given away still count); null unless replayStatus is ready and the player is in the match, or when the item catalog is unavailable.</param>
/// <param name="Heatmaps">Where each of the ten heroes spent its time alive, per game phase, read from the replay; null unless replayStatus is ready (and for replays read before heatmaps existed).</param>
/// <param name="AbilityBuild">The player's ability level-ups in replay order; null unless replayStatus is ready and the player is in the match.</param>
/// <param name="Stats">Every statistic of every hero minute by minute, read from the replay, so any moment or period can be shown; null unless replayStatus is ready (and for replays read before the timeline existed).</param>
/// <param name="OpenDotaProcessing">External processing status, independent of replayStatus; absent for older API responses.</param>
/// <param name="OpenDotaExtras">Optional external aggregates for the selected player, with explicit source and scope.</param>
/// <param name="Draft">Catalog composition and observed enemy interactions; null without a player or any known drafted hero profile.</param>
/// <param name="Neutrals">Neutral items crafted by every hero (from our replay reading); null for replays read before neutral crafts were.</param>
/// <param name="Catalog">Source and availability of the calculation rules, separate from display names.</param>
public record MatchAnalysis(
    long MatchId,
    int DurationSeconds,
    DateTimeOffset StartTime,
    bool? RadiantWon,
    int RadiantScore,
    int DireScore,
    bool IsParsed,
    IReadOnlyList<MatchPlayerSummary> Radiant,
    IReadOnlyList<MatchPlayerSummary> Dire,
    PlayerPerformance? Performance,
    ReplayStatus ReplayStatus,
    LaningPhase? Laning,
    MatchTimelineChart? Timeline = null,
    IReadOnlyList<PlayerDeath>? Deaths = null,
    IReadOnlyList<MapWard>? Wards = null,
    PlayerDamage? Damage = null,
    IReadOnlyList<FightSummary>? Fights = null,
    PlayerItemBuild? ItemBuild = null,
    IReadOnlyList<MapObjectiveSummary>? Objectives = null,
    SmartStats? SmartStats = null,
    HeatmapSet? Heatmaps = null,
    PlayerAbilityBuild? AbilityBuild = null,
    MatchStats? Stats = null,
    OpenDotaProcessing? OpenDotaProcessing = null,
    OpenDotaPlayerExtras? OpenDotaExtras = null,
    MatchDraft? Draft = null,
    MatchNeutrals? Neutrals = null,
    CatalogProvenance? Catalog = null);
