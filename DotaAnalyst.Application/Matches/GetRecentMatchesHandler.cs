using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;

namespace DotaAnalyst.Application.Matches;

// Use case: list the recent matches of a player, with hero names.
public class GetRecentMatchesHandler(IMatchProvider matchProvider, IHeroCatalog heroCatalog)
{
    public const string UnknownHeroName = "Unknown hero";

    /// <exception cref="DataSourceUnavailableException">Matches could not be loaded.</exception>
    public async Task<IReadOnlyList<MatchSummary>> HandleAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        // Both run at the same time; the hero list usually comes from the cache.
        var matchesTask = matchProvider.GetRecentMatchesAsync(accountId, cancellationToken);
        var heroesTask = heroCatalog.GetHeroesAsync(cancellationToken);
        await Task.WhenAll(matchesTask, heroesTask);

        var heroes = heroesTask.Result;
        return matchesTask.Result.Select(match => ToSummary(match, heroes)).ToList();
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
