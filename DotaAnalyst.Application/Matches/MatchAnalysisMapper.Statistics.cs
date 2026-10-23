using DotaAnalyst.Domain.Analysis;

namespace DotaAnalyst.Application.Matches;

// The replay statistics of a hero (HeroMetrics blocks) -> their API summaries, field by field. A block is null when the
// analysis was computed before it or the replay lacks what it needs; inside a block, an unknown number stays null, never 0.
internal static partial class MatchAnalysisMapper
{
    internal static TerrainSummary? ToSummary(TerrainMetrics? terrain) => terrain is null ? null : new(
        terrain.OwnHighGroundShare, terrain.EnemyHighGroundShare, terrain.RiverLevelShare, terrain.NarrowGroundShare,
        terrain.DeepEnemyJungleShare, terrain.DeathsOnEnemyHighGround, terrain.DeathsBelowEnemyHighGround,
        terrain.DeathsInNarrowGround, terrain.DeathsInDeepEnemyJungle);

    internal static VisionSummary? ToSummary(VisionMetrics? vision) => vision is null ? null : new(
        vision.SeenByEnemiesShare, vision.DeathsWhileTracked, vision.DeathsUnseenBefore, vision.DeathsToKillerFromFog,
        vision.KillsFromFog, vision.ObserverSeenShare);

    internal static ObjectivesSummary? ToSummary(MapObjectiveMetrics? objectives) => objectives is null ? null : new(
        objectives.Counts is { } counts
            ? new ObjectiveCountsSummary(counts.BountyRunes, counts.PowerRunes, counts.WaterRunes, counts.WisdomShrines,
                counts.Lotuses, counts.Watchers, counts.Outposts, counts.TormentorKills, counts.CampsStacked)
            : null,
        objectives.RoshanKills, objectives.RoshanKillsNearby, objectives.TormentorKills, objectives.TormentorKillsNearby,
        objectives.EnemyTowersTaken, objectives.EnemyTowersTakenNearby, objectives.OwnShrinesLost, objectives.EnemyShrinesTaken);

    internal static TeleportsSummary? ToSummary(TeleportMetrics? teleports) =>
        teleports is null ? null : new(teleports.Teleports, teleports.Cancelled, teleports.IntoFights);

    internal static PlaysSummary? ToSummary(PlayQualityMetrics? plays) => plays is null ? null : new(
        plays.SkillshotUses, plays.SkillshotHits, plays.LowHealthAllyUses, plays.Saves, plays.DispelledDebuffs);

    internal static DeathContextSummary? ToSummary(DeathContextMetrics? deaths) => deaths is null ? null : new(
        deaths.Deaths, deaths.EscapeItemReady, deaths.BkbReady, deaths.ToThreeOrMore, deaths.WhileDisabled,
        deaths.DisabledSecondsBefore);

    internal static ItemUsageSummary? ToSummary(ItemUsageMetrics? items) =>
        items is null ? null : new(items.Fights, items.KeyItemReady, items.KeyItemUsed, items.BkbReady, items.BkbUsed);

    internal static FightInitiationSummary? ToSummary(FightInitiationMetrics? fights) =>
        fights is null ? null : new(fights.Fights, fights.TeamStarted, fights.HeroStarted);

    internal static LanePressureSummary? ToSummary(LanePressureMetrics? lane) =>
        lane is null ? null : new(lane.Samples, lane.PushedShare, lane.AverageMeetingPoint);

    internal static DayNightSummary? ToSummary(DayNightMetrics? cycle) => cycle is null ? null : new(
        cycle.NightShare, cycle.Kills, cycle.KillsAtNight, cycle.Deaths, cycle.DeathsAtNight);

    internal static SmokesSummary? ToSummary(SmokeMetrics? smokes) =>
        smokes is null ? null : new(smokes.Smokes, smokes.SmokesWithKill);

    internal static FarmSummary? ToSummary(FarmMetrics? farm) => farm is null ? null : new(
        farm.LaneCreeps, farm.Neutrals, farm.Denies, farm.LaneCreepsAfterLaning, farm.NeutralsAfterLaning);
}
