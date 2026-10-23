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
    internal static MatchDraft? ToDraft(MatchDetails match, MatchPlayer player,
        IReadOnlyDictionary<int, HeroDetails>? catalog, NameLookup names, ReplayAnalysis? replay, PlayerDamage? damage)
    {
        HeroDetails? Profile(int id) => catalog?.GetValueOrDefault(id);
        if (!match.Players.Any(hero => Profile(hero.HeroId) is not null))
        {
            return null;
        }

        DraftTeam TeamOf(Team team)
        {
            var heroes = match.Players.Where(hero => hero.Team == team).ToList();
            var profile = DraftProfile.From(heroes.Select(hero => Profile(hero.HeroId)));
            return new(team, team == player.Team, heroes.Select(hero => new DraftHero(
                hero.HeroId, names.Hero(hero.HeroId), Profile(hero.HeroId)?.PrimaryAttribute,
                Profile(hero.HeroId)?.AttackType, Profile(hero.HeroId)?.Roles.Order().ToList())).ToList(),
                new DraftSummary(
                    profile.Roles.Select(row => new DraftRoleCount(row.Key, row.Value)).ToList(),
                    profile.AttackTypes.Select(row => new DraftAttackCount(row.Key, row.Value)).ToList(),
                    profile.Attributes.Select(row => new DraftAttributeCount(row.Key, row.Value)).ToList()));
        }

        // Reincarnations are visible on the death map but do not award scoreboard kills.
        var deaths = replay?.Deaths?.Where(death => !death.Reincarnated).ToList();
        var enemies = replay is null ? [] : match.Players.Where(hero => hero.Team != player.Team)
            .Select(hero => new DraftEnemy(hero.HeroId, names.Hero(hero.HeroId),
                deaths?.Count(death => death.HeroId == player.HeroId && death.KillerHeroId == hero.HeroId) ?? 0,
                deaths?.Count(death => death.HeroId == hero.HeroId && death.KillerHeroId == player.HeroId) ?? 0,
                damage is null ? null : damage.FromHeroes.FirstOrDefault(row => row.HeroId == hero.HeroId)?.Amount ?? 0,
                damage is null ? null : damage.ToHeroes.FirstOrDefault(row => row.HeroId == hero.HeroId)?.Amount ?? 0))
            .OrderByDescending(hero => hero.KillsOnPlayer).ThenByDescending(hero => hero.DamageToPlayer)
            .ThenBy(hero => hero.HeroId).ToList();
        return new([TeamOf(player.Team), TeamOf(player.Team == Team.Radiant ? Team.Dire : Team.Radiant)], enemies);
    }

    internal static MatchPlayerSummary ToSummary(MatchPlayer player, bool isCurrentPlayer, NameLookup names) =>
        new(
            AccountId: player.AccountId?.Value,
            IsCurrentPlayer: isCurrentPlayer,
            HeroId: player.HeroId,
            HeroName: names.Hero(player.HeroId),
            Kills: player.Kda.Kills,
            Deaths: player.Kda.Deaths,
            Assists: player.Kda.Assists,
            LastHits: player.LastHits,
            Denies: player.Denies,
            GoldPerMinute: player.GoldPerMinute,
            ExperiencePerMinute: player.ExperiencePerMinute,
            NetWorth: player.NetWorth,
            HeroDamage: player.HeroDamage,
            TowerDamage: player.TowerDamage,
            HeroHealing: player.HeroHealing,
            Level: player.Level,
            Items: player.ItemIds.Select(names.Item).ToList(),
            NeutralItem: player.NeutralItemId is { } neutralItemId ? names.Item(neutralItemId) : null,
            ItemIds: player.ItemIds,
            NeutralItemId: player.NeutralItemId);

    internal static PlayerPerformance ToPerformance(MatchDetails match, MatchPlayer player, NameLookup names)
    {
        var evaluation = PerformanceEvaluator.Evaluate(player);
        return new PlayerPerformance(
            HeroId: player.HeroId,
            HeroName: names.Hero(player.HeroId),
            IsRadiant: player.Team == Team.Radiant,
            Won: match.Won(player),
            Metrics: evaluation.Metrics
                .Select(m => new MetricPerformance(m.Metric, m.Value, m.Percentile, m.Rating))
                .ToList(),
            Strengths: evaluation.Strengths.Select(m => m.Metric).ToList(),
            Weaknesses: evaluation.Weaknesses.Select(m => m.Metric).ToList());
    }
}
