using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Models;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Heroes;

// Internal read-only query over a kept replay; never triggers parsing, refreshing or inference.
public sealed class GetHeroStateHandler(IArchivedReplaySource replays, GameCatalogLoader catalogs)
{
    public async Task<HeroStateResult> HandleAsync(MatchId matchId, int heroId, int seconds, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heroId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(matchId.Value);
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        var replay = await replays.FindAsync(matchId, cancellationToken);
        if (replay is null) return new("noDocument", heroId, seconds, null, null, null);
        if (!replay.Analysis.Players.Any(p => p.HeroId == heroId)) return new("unknownHero", heroId, seconds, null, null, null);
        var (catalog, build) = await catalogs.LoadForMatchAsync(replay.StartedAt, cancellationToken);
        return Evaluate(replay, heroId, seconds, catalog, build);
    }

    // The same state from a replay and catalog already read (one read per request: GetReplayFactsHandler).
    public static HeroStateResult Evaluate(ArchivedReplay replay, int heroId, int seconds, GameCatalog catalog, GameBuild? build)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!replay.Analysis.Players.Any(p => p.HeroId == heroId)) return new("unknownHero", heroId, seconds, null, null, null);
        if (replay.Analysis.Timeline is not { Seconds.Count: > 0 } timeline)
            return new("unknownDuration", heroId, seconds, null, null, null);
        if (seconds > timeline.Seconds[^1]) return new("outOfRange", heroId, seconds, null, null, null);
        // The archive's Domain contract enforces unique hero IDs per match. Reject ambiguous position tracks too;
        // this is not a public endpoint accepting an arbitrary account/player mapping.
        var tracks = replay.Positions.Where(p => p.HeroId == heroId).ToArray();
        if (tracks.Length > 1) return new("ambiguousHero", heroId, seconds, null, null, null);
        var positions = tracks.SingleOrDefault();
        var profile = catalog.Heroes?.GetValueOrDefault(heroId);
        // No tolerance: never return a living sample carried forward beyond a death, or a future sample.
        var sample = positions?.At(seconds, 0);
        if (replay.Analysis.Deaths?.Any(d => d.HeroId == heroId && d.Seconds == seconds && !d.Reincarnated) == true)
            sample = null;
        return new(sample is null ? "noExactSample" : "found", heroId, seconds, sample, profile, build);
    }
}

public sealed record HeroStateResult(string Status, int HeroId, int Seconds, PositionSample? Sample,
    HeroDetails? Nominal, GameBuild? Build)
{
    public string CatalogScope => Build is not null ? "matchBuild" : Nominal is null ? "unavailable" : "currentFallback";
}
