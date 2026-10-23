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
    internal static HeatmapSet ToHeatmaps(
        IReadOnlyList<HeroHeatmap> heatmaps, MatchDetails match, MatchPlayer? player, NameLookup names)
    {
        static IReadOnlyList<int> Flat(IReadOnlyList<HeatCell> cells) =>
            cells.SelectMany(cell => new[] { cell.Column, cell.Row, cell.Samples }).ToList();

        static PositionIndicatorsView? View(PositionIndicators? indicators) => indicators is null
            ? null
            : new PositionIndicatorsView(indicators.AliveSeconds, indicators.Advance, indicators.Coverage,
                indicators.Concentration, indicators.EnemySide, indicators.NearAlly, indicators.Grouped);

        static PhaseIndicators IndicatorsOf(HeroHeatmap heatmap, Team team) => new(
            View(HeatmapIndicators.Calculate(heatmap, team)),
            View(HeatmapIndicators.Calculate(heatmap, team, GamePhase.EarlyGame)),
            View(HeatmapIndicators.Calculate(heatmap, team, GamePhase.MidGame)),
            View(HeatmapIndicators.Calculate(heatmap, team, GamePhase.LateGame)));

        var heroes = heatmaps
            .Select(heatmap => (heatmap, hero: match.Players.FirstOrDefault(candidate => candidate.HeroId == heatmap.HeroId)))
            .Where(entry => entry.hero is not null)
            .OrderBy(entry => entry.hero!.Team == Team.Radiant ? 0 : 1)
            .Select(entry => new HeroHeatmapView(
                HeroId: entry.heatmap.HeroId,
                HeroName: names.Hero(entry.heatmap.HeroId),
                Team: entry.hero!.Team,
                IsCurrentPlayer: player is not null && entry.heatmap.HeroId == player.HeroId,
                EarlyGame: Flat(entry.heatmap.Phases.EarlyGame),
                MidGame: Flat(entry.heatmap.Phases.MidGame),
                LateGame: Flat(entry.heatmap.Phases.LateGame),
                Indicators: IndicatorsOf(entry.heatmap, entry.hero.Team)))
            .ToList();

        // The average of the heroes that have the period (each indicator over the heroes that have it).
        static PositionIndicatorsView? Average(IReadOnlyList<PositionIndicatorsView?> all)
        {
            var present = all.OfType<PositionIndicatorsView>().ToList();
            if (present.Count == 0)
            {
                return null;
            }

            double? Mean(Func<PositionIndicatorsView, double?> value) =>
                present.Select(value).OfType<double>().DefaultIfEmpty(double.NaN).Average() is var mean && double.IsNaN(mean)
                    ? null
                    : mean;
            return new PositionIndicatorsView(
                (int)Math.Round(present.Average(view => view.AliveSeconds)),
                Mean(view => view.Advance)!.Value,
                Mean(view => view.Coverage)!.Value,
                Mean(view => view.Concentration)!.Value,
                Mean(view => view.EnemySide)!.Value,
                Mean(view => view.NearAlly),
                Mean(view => view.Grouped));
        }

        var indicators = heroes.Select(hero => hero.Indicators!).ToList();
        var matchAverage = new PhaseIndicators(
            Average(indicators.Select(phases => phases.All).ToList()),
            Average(indicators.Select(phases => phases.EarlyGame).ToList()),
            Average(indicators.Select(phases => phases.MidGame).ToList()),
            Average(indicators.Select(phases => phases.LateGame).ToList()));
        return new HeatmapSet(
            HeroHeatmap.GridSize, HeroHeatmap.MapMin, HeroHeatmap.MapMax, HeatmapIndicators.SecondsPerSample, heroes, matchAverage);
    }

    internal static IReadOnlyList<MapWard> ToWards(
        IReadOnlyList<WardPlacement> wards, MatchPlayer? player, NameLookup names) =>
        wards
            .Select(ward => new MapWard(
                Kind: ward.Kind,
                Team: ward.Team,
                IsPlayersTeam: player is null ? null : ward.Team == player.Team,
                PlacedByHeroName: ward.PlacedByHeroId is { } placedBy ? names.Hero(placedBy) : null,
                X: ward.X,
                Y: ward.Y,
                PlacedAt: ward.PlacedAt,
                RemovedAt: ward.RemovedAt,
                Destroyed: ward.Destroyed,
                DestroyedByHeroName: ward.DestroyedByHeroId is { } destroyedBy ? names.Hero(destroyedBy) : null,
                InLaningPhase: ward.InLaningPhase))
            .ToList();

    // The point-of-view player's deaths, in time order.
    internal static IReadOnlyList<PlayerDeath> ToDeaths(
        IReadOnlyList<HeroDeath> deaths, MatchPlayer player, NameLookup names) =>
        deaths
            .Where(death => death.HeroId == player.HeroId)
            .Select(death => new PlayerDeath(
                Seconds: death.Seconds,
                X: death.X,
                Y: death.Y,
                Killer: death.Killer,
                KillerHeroId: death.KillerHeroId,
                KillerHeroName: death.KillerHeroId is { } heroId ? names.Hero(heroId) : null,
                Reincarnated: death.Reincarnated,
                InLaningPhase: death.InLaningPhase))
            .ToList();
}
