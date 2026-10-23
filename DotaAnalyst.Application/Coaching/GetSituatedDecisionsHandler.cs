using System.Globalization;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Models;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Situations;

namespace DotaAnalyst.Application.Coaching;

// Use case: the v5 coaching decision context of one hero in a kept replay, for the local coach corpus (coach-ml). The
// scenes are the v1 ones; the situation at each anchor comes from the same builder and columns as the prediction models.
// The patch is the official sub-patch of the match and the game build whose files were read (as for the model
// samples); mechanics count as verified only when that build belongs to that sub-patch.
public sealed class GetSituatedDecisionsHandler(
    IArchivedReplaySource replays, GameCatalogLoader catalogs, IHeroCatalog heroes, IMatchRepository matches,
    IGamePatchCatalog? patchCatalog = null)
{
    public const int SchemaVersion = 5;

    public async Task<SituatedDecisionContext?> HandleAsync(MatchId matchId, int heroId, CancellationToken cancellationToken)
    {
        if (await replays.FindAsync(matchId, cancellationToken) is not { } replay
            || replay.Analysis.Players.All(player => player.HeroId != heroId))
        {
            return null;
        }

        var (catalog, build) = await catalogs.LoadForMatchAsync(replay.StartedAt, cancellationToken);
        var patch = await PatchOfAsync(matchId, replay.StartedAt, build, cancellationToken);
        var decision = BuildCoachingDecisionContext.Build(replay.Analysis, heroId, patch, replay.Positions);
        var builder = HeroSituationBuilder.From(replay.Analysis, replay.Positions, replay.Activity, catalog, replay.Facts);
        var situations = decision.Scenes.Select(scene => scene.AtOrBeforeAnchor.AnchorSeconds).Select(anchor =>
            new SituatedAnchor(anchor, builder?.At(heroId, anchor) is { } situation
                ? CoachingSituationSnapshot.From(situation, replay.Activity is not null)
                : null,
                builder?.EscapeAt(heroId, anchor) is { } escape ? CoachingEscapeSnapshot.From(escape) : null)).ToList();
        return new(SchemaVersion, decision, await HeroesOfAsync(replay, catalog, cancellationToken), situations);
    }

    private async Task<IReadOnlyList<SituatedHero>> HeroesOfAsync(ArchivedReplay replay, GameCatalog catalog,
        CancellationToken cancellationToken)
    {
        var names = await heroes.GetHeroesAsync(cancellationToken);
        if (names.Count == 0)
        {
            return [];
        }

        return replay.Analysis.Players.Select(player => names.GetValueOrDefault(player.HeroId) is { } hero
                ? new SituatedHero(player.HeroId, hero.Name, player.Team, hero.Details?.Roles.Order().ToList() ?? [],
                    KitEffectsOf(catalog, player.HeroId))
                : new SituatedHero(player.HeroId, "Unknown hero", player.Team, [], KitEffectsOf(catalog, player.HeroId)))
            .ToList();
    }

    // Every effect some ability of the hero's kit has in the build (null when the kit or any of its abilities is unknown:
    // a partial list would read as "cannot").
    private static List<AbilityEffect>? KitEffectsOf(GameCatalog catalog, int heroId)
    {
        if (catalog.Kits?.GetValueOrDefault(heroId) is not { } kit || catalog.Abilities is not { } abilities
            || kit.Abilities.Any(ability => !abilities.ContainsKey(ability)))
        {
            return null;
        }

        return kit.Abilities.SelectMany(ability => AbilityEffects.Of(abilities[ability])).Distinct().Order().ToList();
    }

    private async Task<CoachingPatch> PatchOfAsync(MatchId matchId, DateTimeOffset? startedAt, GameBuild? build,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<GamePatch> patches = patchCatalog is null ? [] : await patchCatalog.GetPatchesAsync(cancellationToken);
        if (GamePatch.For(patches, startedAt) is { } official)
        {
            return new CoachingPatch(official.Name, build is null ? "official-patch:no-game-build" : $"game-build:{build.Id}",
                null, build is not null && GamePatch.BuildBelongsTo(build, official, patches));
        }

        // No official patch list: the stored match's patch ID, as the v1 coach source, unverified mechanics.
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
