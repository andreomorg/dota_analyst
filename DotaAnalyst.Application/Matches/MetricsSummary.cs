using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>A hero's numbers (or the match average) for the smart stats.</summary>
/// <param name="Deaths">Deaths (Aegis deaths included).</param>
/// <param name="IsolatedDeaths">Deaths with no ally hero within 1500 map units.</param>
/// <param name="DeathsOnEnemySide">Deaths on the enemy's side of the map (past the river).</param>
/// <param name="EnemySideShare">Share (0..1) of the time alive from 0:00 on the enemy's side; mostly shows which team was winning.</param>
/// <param name="AloneOnEnemySideShare">Share (0..1) of the time alive on the enemy's side with no ally nearby.</param>
/// <param name="NetWorthAt10">Net worth at 10:00; null when the match ended before.</param>
/// <param name="NetWorthAt20">Net worth at 20:00; null when the match ended before.</param>
/// <param name="LastHitsAt10">Last hits at the end of the laning phase.</param>
/// <param name="NetWorthShare">Share (0..1) of the team's net worth at the end.</param>
/// <param name="ObserversPlaced">Observer wards placed.</param>
/// <param name="SentriesPlaced">Sentry wards placed.</param>
/// <param name="ObserverAverageLifetimeSeconds">How long the observers lived on average (360 at most); null without observers.</param>
/// <param name="WardsLostToEnemy">Own wards the enemy destroyed.</param>
/// <param name="EnemyWardsDestroyed">Enemy wards destroyed.</param>
/// <param name="TimeByRegion">Share (0..1) of the time alive from 0:00 in each region of the map; null for metrics computed before the regions.</param>
/// <param name="DeathsByRegion">Deaths in each region of the map; null for metrics computed before the regions.</param>
/// <param name="EstimatedMovementDistance">Estimated straight-line displacement between adjacent position samples, in map units; null when no continuous samples were available.</param>
/// <param name="MovementSampleSegments">Number of adjacent position-sample pairs used for the distance estimate; an average when this summarizes a team.</param>
/// <param name="EstimatedMovementByRegion">Estimated displacement assigned to the region containing each segment midpoint; null when no continuous samples were available.</param>
/// <param name="TimeByRegionByPhase">Share of alive position samples in each map region, split into 0-10, 10-20, and 20+ minute phases; null for older metrics.</param>
/// <param name="DeathsByRegionByPhase">Deaths in each map region, split into 0-10, 10-20, and 20+ minute phases; null for older metrics.</param>
/// <param name="TeamFights">Teamfights of the match (3+ deaths at most 15 s apart); null for older metrics.</param>
/// <param name="FightsNearby">Teamfights the hero was within 2500 map units of (center = average of the fight's deaths); null for older metrics.</param>
/// <param name="KillParticipation">(Kills + combat-log assists) / enemy hero deaths, 0..1; null when no enemy died or for older metrics.</param>
/// <param name="QuickRedeaths">Deaths within 90 s of coming back from the previous death; null for older metrics.</param>
/// <param name="LaneDamageDealt">Damage dealt to enemy heroes in minutes 0-9; null for older metrics.</param>
/// <param name="LaneDamageTaken">Damage taken from enemy heroes in minutes 0-9; null for older metrics.</param>
/// <param name="TeamObserverUptime">Share (0..1) of the match with at least one observer of the hero's team on the map; null for older metrics.</param>
/// <param name="LeadLostPerDeath">Average fall of the team's net worth lead from the minute of each death to the next (positive = lost; correlation, not cause); null without such deaths or for older metrics.</param>
/// <param name="NetWorthGainByRegion">Net worth gained in the minutes spent mostly in each region (divide by MinutesByRegion for a rate per minute); null for older metrics.</param>
/// <param name="MinutesByRegion">Minutes spent mostly in each region (minutes dead all along left out); null for older metrics.</param>
/// <param name="FirstBigItemSeconds">Game-clock time when the first build item worth at least 2000 gold was ready; null when none was recorded.</param>
/// <param name="Terrain">Where the hero stood and died by the real terrain; null for older analyses, without the patch's map, and in the match average.</param>
/// <param name="Vision">What the fog of war showed around the hero; null for older analyses, without visibility, and in the match average.</param>
/// <param name="Objectives">Runes, shrines, lotuses, Watchers, outposts and presence at Roshan/Tormentor/towers; null for older analyses and in the match average.</param>
/// <param name="Teleports">Teleports done, cancelled and into fights; null for older analyses, without buffs, and in the match average.</param>
/// <param name="Plays">Skillshots hit, allies saved, disables dispelled; null for older analyses, without activity, and in the match average.</param>
/// <param name="DeathContext">Each death at its moment (escape item / BKB ready, 3+ attackers, disabled); null for older analyses and in the match average.</param>
/// <param name="ItemUsage">Fight items held ready and used in team fights; null for older analyses and in the match average.</param>
/// <param name="FightInitiation">Who started the team fights the hero was in; null for older analyses and in the match average.</param>
/// <param name="LanePressure">Where the waves met in the hero's lane in the laning phase; null for older analyses, roaming/jungle heroes, and in the match average.</param>
/// <param name="DayNight">Kills and deaths by night; null for older analyses, without the day/night cycle, and in the match average.</param>
/// <param name="Smokes">Smokes used and followed by a kill; null for older analyses, without activity, and in the match average.</param>
/// <param name="Farm">Lane creeps, neutrals and denies; null for older analyses, without creep kills, and in the match average.</param>
public record MetricsSummary(
    double Deaths,
    double IsolatedDeaths,
    double DeathsOnEnemySide,
    double EnemySideShare,
    double AloneOnEnemySideShare,
    double? NetWorthAt10,
    double? NetWorthAt20,
    double LastHitsAt10,
    double NetWorthShare,
    double ObserversPlaced,
    double SentriesPlaced,
    double? ObserverAverageLifetimeSeconds,
    double WardsLostToEnemy,
    double EnemyWardsDestroyed,
    RegionValues? TimeByRegion = null,
    RegionValues? DeathsByRegion = null,
    double? EstimatedMovementDistance = null,
    double? MovementSampleSegments = null,
    RegionValues? EstimatedMovementByRegion = null,
    RegionPhaseValues? TimeByRegionByPhase = null,
    RegionPhaseValues? DeathsByRegionByPhase = null,
    double? TeamFights = null,
    double? FightsNearby = null,
    double? KillParticipation = null,
    double? QuickRedeaths = null,
    double? LaneDamageDealt = null,
    double? LaneDamageTaken = null,
    double? TeamObserverUptime = null,
    double? LeadLostPerDeath = null,
    RegionValues? NetWorthGainByRegion = null,
    RegionValues? MinutesByRegion = null,
    double? FirstBigItemSeconds = null,
    TerrainSummary? Terrain = null,
    VisionSummary? Vision = null,
    ObjectivesSummary? Objectives = null,
    TeleportsSummary? Teleports = null,
    PlaysSummary? Plays = null,
    DeathContextSummary? DeathContext = null,
    ItemUsageSummary? ItemUsage = null,
    FightInitiationSummary? FightInitiation = null,
    LanePressureSummary? LanePressure = null,
    DayNightSummary? DayNight = null,
    SmokesSummary? Smokes = null,
    FarmSummary? Farm = null);
