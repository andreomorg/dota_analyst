using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;

namespace DotaAnalyst.Application.Matches;

// Use case: analyze a match from the point of view of one player.
public class GetMatchAnalysisHandler(
    IReplayAnalyzer replayAnalyzer,
    IHeroCatalog heroCatalog,
    IItemCatalog itemCatalog)
{
    public const string UnknownHeroName = GetRecentMatchesHandler.UnknownHeroName;
    public const string UnknownItemName = "Unknown item";

    /// <returns>The analysis, or null when the match does not exist.</returns>
    /// <exception cref="DataSourceUnavailableException">The match could not be loaded.</exception>
    public async Task<MatchAnalysis?> HandleAsync(
        MatchId matchId, AccountId accountId, CancellationToken cancellationToken)
    {
        // All three run at the same time; heroes and items usually come from the cache.
        var matchTask = replayAnalyzer.GetMatchDetailsAsync(matchId, cancellationToken);
        var heroesTask = heroCatalog.GetHeroesAsync(cancellationToken);
        var itemsTask = itemCatalog.GetItemsAsync(cancellationToken);
        await Task.WhenAll(matchTask, heroesTask, itemsTask);

        if (matchTask.Result is not { } match)
        {
            return null;
        }

        var names = new NameLookup(heroesTask.Result, itemsTask.Result);
        var currentPlayer = match.FindPlayer(accountId);

        IReadOnlyList<MatchPlayerSummary> TeamOf(Team team) => match.Players
            .Where(player => player.Team == team)
            .Select(player => ToSummary(player, ReferenceEquals(player, currentPlayer), names))
            .ToList();

        return new MatchAnalysis(
            MatchId: match.MatchId.Value,
            DurationSeconds: (int)match.Duration.TotalSeconds,
            StartTime: match.StartTime,
            RadiantWon: MatchResult.Won(Team.Radiant, match.WinningTeam),
            RadiantScore: match.RadiantScore,
            DireScore: match.DireScore,
            IsParsed: match.IsParsed,
            Radiant: TeamOf(Team.Radiant),
            Dire: TeamOf(Team.Dire),
            Performance: currentPlayer is null ? null : ToPerformance(match, currentPlayer, names));
    }

    private static MatchPlayerSummary ToSummary(MatchPlayer player, bool isCurrentPlayer, NameLookup names) =>
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
            NeutralItem: player.NeutralItemId is { } neutralItemId ? names.Item(neutralItemId) : null);

    private static PlayerPerformance ToPerformance(MatchDetails match, MatchPlayer player, NameLookup names)
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

    private sealed class NameLookup(IReadOnlyDictionary<int, Hero> heroes, IReadOnlyDictionary<int, Item> items)
    {
        public string Hero(int heroId) => heroes.GetValueOrDefault(heroId)?.Name ?? UnknownHeroName;

        public string Item(int itemId) => items.GetValueOrDefault(itemId)?.Name ?? UnknownItemName;
    }
}
