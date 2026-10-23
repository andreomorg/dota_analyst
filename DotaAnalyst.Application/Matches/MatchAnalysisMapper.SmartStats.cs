using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Baseline;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Application.Matches;

internal static partial class MatchAnalysisMapper
{
    // Null without metrics for the player or an enemy of the same role, and for a match without role queue (its
    // roles are only inferred: good for the comparison base, not to judge the player's own match — Turbo is left out).
    internal static SmartStats? ToSmartStats(IReadOnlyList<HeroMetrics> metrics, MatchPlayer player, NameLookup names)
    {
        if (metrics.FirstOrDefault(hero => hero.HeroId == player.HeroId) is not { RoleInferred: false } mine
            || metrics.FirstOrDefault(hero => hero.Team != mine.Team && hero.Role == mine.Role) is not { } counterpart)
        {
            return null;
        }

        // Over the heroes that have the number (no net worth at 20:00 in a shorter match).
        static double? Average(IEnumerable<int?> values)
        {
            var known = values.OfType<int>().ToList();
            return known.Count == 0 ? null : known.Average();
        }
        static double? AverageKnown(IEnumerable<double?> values)
        {
            var known = values.OfType<double>().ToList();
            return known.Count == 0 ? null : known.Average();
        }

        var matchAverage = new MetricsSummary(
            Deaths: metrics.Average(hero => hero.Deaths),
            IsolatedDeaths: metrics.Average(hero => hero.IsolatedDeaths),
            DeathsOnEnemySide: metrics.Average(hero => hero.DeathsOnEnemySide),
            EnemySideShare: metrics.Average(hero => hero.EnemySideShare),
            AloneOnEnemySideShare: metrics.Average(hero => hero.AloneOnEnemySideShare),
            NetWorthAt10: Average(metrics.Select(hero => hero.NetWorthAt10)),
            NetWorthAt20: Average(metrics.Select(hero => hero.NetWorthAt20)),
            LastHitsAt10: metrics.Average(hero => hero.LastHitsAt10),
            NetWorthShare: metrics.Average(hero => hero.NetWorthShare),
            ObserversPlaced: metrics.Average(hero => hero.ObserversPlaced),
            SentriesPlaced: metrics.Average(hero => hero.SentriesPlaced),
            ObserverAverageLifetimeSeconds: Average(metrics.Select(hero => hero.ObserverAverageLifetimeSeconds)),
            WardsLostToEnemy: metrics.Average(hero => hero.WardsLostToEnemy),
            EnemyWardsDestroyed: metrics.Average(hero => hero.EnemyWardsDestroyed),
            // Each hero's own/enemy regions: "own jungle" averages every hero's own jungle.
            TimeByRegion: AverageByRegion(metrics.Select(hero => hero.TimeByRegion)),
            DeathsByRegion: AverageByRegion(metrics.Select(hero => hero.DeathsByRegion?.Select(count => (double)count))),
            EstimatedMovementDistance: AverageKnown(metrics.Select(hero => hero.EstimatedMovementDistance)),
            MovementSampleSegments: AverageKnown(metrics.Select(hero =>
                hero.MovementSampleSegments is { } count ? (double?)count : null)),
            EstimatedMovementByRegion: AverageKnownByRegion(metrics.Select(hero => hero.EstimatedMovementByRegion)),
            TimeByRegionByPhase: AverageByPhase(metrics.Select(hero => hero.TimeByRegionByPhase)),
            DeathsByRegionByPhase: AverageByPhase(metrics.Select(hero =>
                hero.DeathsByRegionByPhase?.Select(regionCounts => regionCounts.Select(count => (double)count)))),
            TeamFights: AverageKnown(metrics.Select(hero => (double?)hero.TeamFights)),
            FightsNearby: AverageKnown(metrics.Select(hero => (double?)hero.FightsNearby)),
            KillParticipation: AverageKnown(metrics.Select(hero => hero.KillParticipation)),
            QuickRedeaths: AverageKnown(metrics.Select(hero => (double?)hero.QuickRedeaths)),
            LaneDamageDealt: AverageKnown(metrics.Select(hero => (double?)hero.LaneDamageDealt)),
            LaneDamageTaken: AverageKnown(metrics.Select(hero => (double?)hero.LaneDamageTaken)),
            TeamObserverUptime: AverageKnown(metrics.Select(hero => hero.TeamObserverUptime)),
            LeadLostPerDeath: AverageKnown(metrics.Select(hero => hero.LeadLostPerDeath)),
            NetWorthGainByRegion: AverageKnownByRegion(metrics.Select(hero => hero.NetWorthGainByRegion)),
            MinutesByRegion: AverageKnownByRegion(metrics.Select(hero => hero.MinutesByRegion?.Select(count => (double)count))),
            FirstBigItemSeconds: Average(metrics.Select(hero =>
                hero.FirstBigItemSeconds is { } seconds ? (int?)seconds : null)));

        return new SmartStats(
            Role: mine.Role,
            CounterpartHeroId: counterpart.HeroId,
            CounterpartHeroName: names.Hero(counterpart.HeroId),
            Player: ToSummary(mine),
            Counterpart: ToSummary(counterpart),
            MatchAverage: matchAverage,
            Insights: InsightGenerator.Generate(mine, counterpart)
                .Select(insight => new InsightSummary(insight.Kind, insight.Value, insight.Counterpart))
                .ToList());
    }

    internal static MetricsSummary ToSummary(HeroMetrics hero) =>
        new(hero.Deaths, hero.IsolatedDeaths, hero.DeathsOnEnemySide, hero.EnemySideShare, hero.AloneOnEnemySideShare,
            hero.NetWorthAt10, hero.NetWorthAt20, hero.LastHitsAt10, hero.NetWorthShare, hero.ObserversPlaced,
            hero.SentriesPlaced, hero.ObserverAverageLifetimeSeconds, hero.WardsLostToEnemy, hero.EnemyWardsDestroyed,
            hero.TimeByRegion is { } time ? ToRegionValues(time) : null,
            hero.DeathsByRegion is { } deaths ? ToRegionValues(deaths.Select(count => (double)count)) : null,
            hero.EstimatedMovementDistance,
            hero.MovementSampleSegments,
            hero.EstimatedMovementByRegion is { } movement ? ToRegionValues(movement) : null,
            hero.TimeByRegionByPhase is { } timeByPhase ? ToRegionPhaseValues(timeByPhase) : null,
            hero.DeathsByRegionByPhase is { } deathsByPhase
                ? ToRegionPhaseValues(deathsByPhase.Select(regionCounts => regionCounts.Select(count => (double)count)))
                : null,
            hero.TeamFights, hero.FightsNearby, hero.KillParticipation, hero.QuickRedeaths, hero.LaneDamageDealt,
            hero.LaneDamageTaken, hero.TeamObserverUptime, hero.LeadLostPerDeath,
            hero.NetWorthGainByRegion is { } gain ? ToRegionValues(gain) : null,
            hero.MinutesByRegion is { } minutes ? ToRegionValues(minutes.Select(count => (double)count)) : null,
            hero.FirstBigItemSeconds,
            ToSummary(hero.Terrain), ToSummary(hero.Vision), ToSummary(hero.Objectives), ToSummary(hero.Teleports),
            ToSummary(hero.Plays), ToSummary(hero.DeathContext), ToSummary(hero.ItemUsage), ToSummary(hero.FightInitiation),
            ToSummary(hero.LanePressure), ToSummary(hero.DayNight), ToSummary(hero.Smokes), ToSummary(hero.Farm));

    internal static RegionValues ToRegionValues(PerRegion<double> values) =>
        new(values.OwnBase, values.EnemyBase, values.TopLane, values.MiddleLane, values.BottomLane, values.River,
            values.OwnJungle, values.EnemyJungle);

    // Null when any hero lacks the regions (metrics computed before them).
    internal static RegionValues? AverageByRegion(IEnumerable<PerRegion<double>?> heroes) =>
        PerRegionAverages.OfAll(heroes) is { } average ? ToRegionValues(average) : null;

    internal static RegionValues? AverageKnownByRegion(IEnumerable<PerRegion<double>?> heroes) =>
        PerRegionAverages.OfKnown(heroes) is { } average ? ToRegionValues(average) : null;

    internal static RegionPhaseValues ToRegionPhaseValues(PerGamePhase<PerRegion<double>> values) =>
        new(ToRegionValues(values.EarlyGame), ToRegionValues(values.MidGame), ToRegionValues(values.LateGame));

    internal static RegionPhaseValues? AverageByPhase(IEnumerable<PerGamePhase<PerRegion<double>>?> heroes) =>
        PerRegionAverages.ByPhaseOfAll(heroes) is { } average ? ToRegionPhaseValues(average) : null;
}
