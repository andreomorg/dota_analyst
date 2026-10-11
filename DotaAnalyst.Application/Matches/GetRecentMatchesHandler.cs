using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Application.Matches;

// Use case: list the recent matches of a player, with hero names.
public class GetRecentMatchesHandler(
    IMatchProvider matchProvider,
    IHeroCatalog heroCatalog,
    IPlayerSearchRepository playerSearches,
    TimeProvider timeProvider,
    ILogger<GetRecentMatchesHandler> logger)
{
    public const string UnknownHeroName = "Unknown hero";

    /// <exception cref="DataSourceUnavailableException">Matches could not be loaded.</exception>
    public async Task<IReadOnlyList<MatchSummary>> HandleAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        // Both run at the same time; the hero list usually comes from the cache.
        var matchesTask = matchProvider.GetRecentMatchesAsync(accountId, cancellationToken);
        var heroesTask = heroCatalog.GetHeroesAsync(cancellationToken);
        await Task.WhenAll(matchesTask, heroesTask);

        // Only reached when the search worked: failed searches (exceptions above) are not recorded.
        await RecordSearchAsync(accountId, cancellationToken);

        var heroes = heroesTask.Result;
        return matchesTask.Result.Select(match => ToSummary(match, heroes)).ToList();
    }

    // The search history is a nice-to-have: a database failure must not cost the user their matches.
    private async Task RecordSearchAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        try
        {
            await playerSearches.RecordSearchAsync(accountId, timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (StorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not record the search for account {AccountId}", accountId);
        }
    }

    private static MatchSummary ToSummary(PlayerMatch match, IReadOnlyDictionary<int, Hero> heroes) =>
        new(
            MatchId: match.MatchId,
            HeroId: match.HeroId,
            HeroName: heroes.GetValueOrDefault(match.HeroId)?.Name ?? UnknownHeroName,
            Kills: match.Kda.Kills,
            Deaths: match.Kda.Deaths,
            Assists: match.Kda.Assists,
            DurationSeconds: (int)match.Duration.TotalSeconds,
            StartTime: match.StartTime,
            IsRadiant: match.Team == Team.Radiant,
            Won: match.Won);
}
