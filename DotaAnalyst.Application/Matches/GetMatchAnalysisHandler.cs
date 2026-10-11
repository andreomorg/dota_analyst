using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Application.Matches;

// Use case: analyze a match from the point of view of one player.
public class GetMatchAnalysisHandler(
    IReplayAnalyzer replayAnalyzer,
    IMatchRepository matchRepository,
    IHeroCatalog heroCatalog,
    IItemCatalog itemCatalog,
    TimeProvider timeProvider,
    ILogger<GetMatchAnalysisHandler> logger)
{
    public const string UnknownHeroName = GetRecentMatchesHandler.UnknownHeroName;
    public const string UnknownItemName = "Unknown item";

    // How long a saved unparsed match is used before OpenDota is asked again (its replay may get parsed).
    public static readonly TimeSpan UnparsedRefreshInterval = TimeSpan.FromMinutes(15);

    /// <returns>The analysis, or null when the match does not exist.</returns>
    /// <exception cref="DataSourceUnavailableException">The match could not be loaded.</exception>
    public async Task<MatchAnalysis?> HandleAsync(
        MatchId matchId, AccountId accountId, CancellationToken cancellationToken)
    {
        // All three run at the same time; heroes and items usually come from the cache.
        var matchTask = LoadMatchAsync(matchId, cancellationToken);
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

    // Our database first, then OpenDota:
    // - a saved parsed match is final: OpenDota is never asked again;
    // - a saved unparsed match is used for a while, then OpenDota is asked again (the replay may have been
    //   parsed since), and it is still used if OpenDota is down;
    // - a database failure only costs the shortcut: OpenDota still answers.
    private async Task<MatchDetails?> LoadMatchAsync(MatchId matchId, CancellationToken cancellationToken)
    {
        var saved = await FindSavedAsync(matchId, cancellationToken);
        if (saved is not null
            && (saved.Match.IsParsed || timeProvider.GetUtcNow() - saved.SavedAt < UnparsedRefreshInterval))
        {
            return saved.Match;
        }

        MatchDetails? match;
        try
        {
            match = await replayAnalyzer.GetMatchDetailsAsync(matchId, cancellationToken);
        }
        catch (DataSourceUnavailableException ex) when (saved is not null)
        {
            logger.LogWarning(ex, "OpenDota is unavailable; using the saved copy of match {MatchId}", matchId);
            return saved.Match;
        }

        // Only finished matches (known result) are saved.
        if (match is { WinningTeam: not null })
        {
            await TrySaveAsync(match, cancellationToken);
        }

        return match ?? saved?.Match;
    }

    private async Task<SavedMatch?> FindSavedAsync(MatchId matchId, CancellationToken cancellationToken)
    {
        try
        {
            return await matchRepository.FindAsync(matchId, cancellationToken);
        }
        catch (StorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not read match {MatchId} from the database", matchId);
            return null;
        }
    }

    private async Task TrySaveAsync(MatchDetails match, CancellationToken cancellationToken)
    {
        try
        {
            await matchRepository.SaveAsync(match, timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (StorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not save match {MatchId} to the database", match.MatchId);
        }
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
