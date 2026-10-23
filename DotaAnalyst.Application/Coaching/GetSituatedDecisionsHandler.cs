using System.Globalization;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Models;
using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Situations;

namespace DotaAnalyst.Application.Coaching;

// Use case: the v5 coaching decision context of one hero in a kept replay, for the local coach corpus (coach-ml). The
// scenes are the v1 ones; the situation at each anchor comes from the same builder and columns as the prediction models.
public sealed class GetSituatedDecisionsHandler(
    IArchivedReplaySource replays, GameCatalogLoader catalogs, IHeroCatalog heroes, IMatchRepository matches)
{
    public const int SchemaVersion = 5;

    public async Task<SituatedDecisionContext?> HandleAsync(MatchId matchId, int heroId, CancellationToken cancellationToken)
    {
        if (await replays.FindAsync(matchId, cancellationToken) is not { } replay
            || replay.Analysis.Players.All(player => player.HeroId != heroId))
        {
            return null;
        }

        var decision = BuildCoachingDecisionContext.Build(replay.Analysis, heroId, await PatchOfAsync(matchId, cancellationToken),
            replay.Positions);
        var (catalog, _) = await catalogs.LoadForMatchAsync(replay.StartedAt, cancellationToken);
        var builder = HeroSituationBuilder.From(replay.Analysis, replay.Positions, replay.Activity, catalog, replay.Facts);
        var situations = decision.Scenes.Select(scene => scene.AtOrBeforeAnchor.AnchorSeconds).Select(anchor =>
            new SituatedAnchor(anchor, builder?.At(heroId, anchor) is { } situation
                ? CoachingSituationSnapshot.From(situation, replay.Activity is not null)
                : null)).ToList();
        return new(SchemaVersion, decision, await HeroesOfAsync(replay, cancellationToken), situations);
    }

    private async Task<IReadOnlyList<SituatedHero>> HeroesOfAsync(ArchivedReplay replay, CancellationToken cancellationToken)
    {
        var names = await heroes.GetHeroesAsync(cancellationToken);
        if (names.Count == 0)
        {
            return [];
        }

        return replay.Analysis.Players.Select(player => names.GetValueOrDefault(player.HeroId) is { } hero
                ? new SituatedHero(player.HeroId, hero.Name, player.Team, hero.Details?.Roles.Order().ToList() ?? [])
                : new SituatedHero(player.HeroId, "Unknown hero", player.Team, []))
            .ToList();
    }

    // The same patch metadata as the v1 coach source: the stored match's patch ID, unverified mechanics.
    private async Task<CoachingPatch> PatchOfAsync(MatchId matchId, CancellationToken cancellationToken)
    {
        string? patchId = null;
        try
        {
            patchId = (await matches.FindAsync(matchId, cancellationToken))?.Match.PatchId?.ToString(CultureInfo.InvariantCulture);
        }
        catch (StorageUnavailableException)
        {
            // The replay remains usable without patch metadata.
        }

        return new CoachingPatch(patchId, patchId is null ? "unknown:stored-metadata-unavailable" : "stored-match:patch-id", null, false);
    }
}
