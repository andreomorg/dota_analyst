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
    internal static IReadOnlyList<MapObjectiveSummary> ToObjectives(
        IReadOnlyList<MapObjective> objectives, MatchPlayer? player, NameLookup names) =>
        objectives
            .Select(objective => new MapObjectiveSummary(
                Kind: objective.Kind,
                Seconds: objective.Seconds,
                TakenBy: objective.TakenBy,
                IsPlayersTeam: player is null ? null : objective.TakenBy == player.Team,
                Owner: objective.Owner,
                TowerTier: objective.TowerTier,
                Lane: objective.Lane,
                MeleeBarracks: objective.MeleeBarracks,
                Denied: objective.Denied,
                KillerHeroName: objective.KillerHeroId is { } killer ? names.Hero(killer) : null,
                AegisHeroName: objective.AegisHeroId is { } aegis ? names.Hero(aegis) : null))
            .ToList();

    internal static PlayerDamage ToDamage(
        IReadOnlyList<DamageRow> damage, MatchDetails match, MatchPlayer player, NameLookup names)
    {
        var enemies = match.Players.Where(other => other.Team != player.Team).Select(other => other.HeroId).ToHashSet();

        IReadOnlyList<HeroAmount> ByHero(IEnumerable<(int HeroId, int Amount)> rows) => rows
            .GroupBy(row => row.HeroId)
            .Select(group => new HeroAmount(group.Key, names.Hero(group.Key), group.Sum(row => row.Amount)))
            .OrderByDescending(hero => hero.Amount)
            .ToList();

        // Only damage taken by the heroes themselves; the source may be the hero, its illusions or its summons.
        var dealt = damage
            .Where(row => row.SourceHeroId == player.HeroId && row.TargetIsHero && enemies.Contains(row.TargetHeroId!.Value))
            .ToList();
        return new PlayerDamage(
            ToHeroes: ByHero(dealt.Select(row => (row.TargetHeroId!.Value, row.Amount))),
            FromHeroes: ByHero(damage
                .Where(row => row.TargetHeroId == player.HeroId && row.TargetIsHero
                    && row.SourceHeroId is { } source && enemies.Contains(source))
                .Select(row => (row.SourceHeroId!.Value, row.Amount))),
            ByInflictor: dealt
                .GroupBy(row => row.Inflictor)
                .Select(group => names.ItemOfInflictor(group.Key) is { } item
                    ? new InflictorAmount(group.Key, group.Sum(row => row.Amount), item.Id, item.Name)
                    : new InflictorAmount(group.Key, group.Sum(row => row.Amount)))
                .OrderByDescending(inflictor => inflictor.Amount)
                .ToList());
    }

    internal static IReadOnlyList<FightSummary> ToFights(
        IReadOnlyList<HeroDeath> deaths, MatchDetails match, MatchPlayer? player)
    {
        // DistinctBy: a custom game with the same hero twice must not break the analysis.
        var teamOf = match.Players.DistinctBy(other => other.HeroId).ToDictionary(other => other.HeroId, other => other.Team);
        return TeamfightDetector.Detect(deaths, teamOf)
            .Select(fight => new FightSummary(
                StartSeconds: fight.StartSeconds,
                EndSeconds: fight.EndSeconds,
                RadiantDeaths: fight.RadiantDeaths,
                DireDeaths: fight.DireDeaths,
                Winner: fight.Winner,
                PlayersTeamWon: player is null || fight.Winner is null ? null : fight.Winner == player.Team,
                Died: player is not null && fight.Deaths.Any(death => death.HeroId == player.HeroId),
                Kills: player is null ? 0 : fight.Deaths.Count(death => death.KillerHeroId == player.HeroId),
                Assists: player is null ? 0 : fight.Deaths.Count(death => death.AssistHeroIds.Contains(player.HeroId)),
                X: (int)Math.Round(fight.Deaths.Average(death => death.X)),
                Y: (int)Math.Round(fight.Deaths.Average(death => death.Y))))
            .ToList();
    }
}
