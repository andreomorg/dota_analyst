using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Heroes;

namespace DotaAnalyst.Application.Catalogs;

public sealed class GetHeroProfileHandler(IGameBuildCatalog builds, IHeroCatalog heroes, IHeroProfileStore stored, TimeProvider clock)
{
    public async Task<HeroProfileResponse?> HandleAsync(int heroId, DateTimeOffset? at, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heroId);
        var selected = await builds.ForMatchAsync(at ?? clock.GetUtcNow(), cancellationToken);
        // A hero absent from a known historical catalog must not be introduced from today's roster.
        if (selected is { } known && known.Catalog.Heroes is { } roster && !roster.ContainsKey(heroId)) return null;
        if (selected is { } snapshot
            && snapshot.Catalog.Heroes?.GetValueOrDefault(heroId) is { } details)
        {
            try { details = await stored.FindAsync(snapshot.Build.Id, heroId, cancellationToken) ?? details; }
            catch (StorageUnavailableException) { /* Game files still supply the same build. */ }
            var keys = snapshot.Catalog.Kits?.GetValueOrDefault(heroId)?.Abilities ?? [];
            var mechanics = keys.Select(k => DefenseMechanic.For(k, snapshot.Catalog.FileVersions)).OfType<DefenseMechanic>().ToArray();
            return new(heroId, snapshot.Build, "gameBuild", details, mechanics);
        }
        var fallback = (await heroes.GetHeroesAsync(cancellationToken)).GetValueOrDefault(heroId)?.Details;
        return fallback is null ? null : new(heroId, null, "currentFallback", fallback, []);
    }
}

/// <summary>A nominal hero profile and factual description, never the effective HUD state of a replay.</summary>
/// <param name="HeroId">Hero identifier.</param>
/// <param name="Build">Source build; null for the explicitly identified current fallback.</param>
/// <param name="CatalogScope">gameBuild or currentFallback; a fallback is not historical data.</param>
/// <param name="Details">Classification, nullable base configuration with origins, and versioned Portuguese description.</param>
/// <param name="ReviewedMechanics">Only description-level mechanics reviewed against the same version of the hero's game file; absence means unknown.</param>
public sealed record HeroProfileResponse(int HeroId, GameBuild? Build, string CatalogScope, HeroDetails Details,
    IReadOnlyList<DefenseMechanic> ReviewedMechanics);
