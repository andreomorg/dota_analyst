using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Infrastructure.Persistence.Catalogs;

namespace DotaAnalyst.Infrastructure.GameData;

public sealed class HeroProfileCatalogReader(ICatalogStore store) : IHeroProfileStore
{
    public async Task<HeroDetails?> FindAsync(string buildId, int heroId, CancellationToken cancellationToken)
    {
        var saved = await store.LoadAsync(HeroProfileCatalogKey.For(buildId), cancellationToken);
        if (saved?.Details.GetValueOrDefault(heroId) is not { } json) return null;
        try
        {
            var entry = JsonSerializer.Deserialize<Entry>(json, HeroProfileCatalogKey.JsonOptions);
            if (entry is not { SchemaVersion: 1, Profile: { Enabled: true } p, Roles: not null,
                    PrimaryAttribute: { } attribute, AttackType: { } attack }
                || entry.HeroId != heroId || entry.Build?.Id != buildId || !p.HasValidShape()) return null;
            // Recompute deterministic text for the current description version, never trust a stale/malformed string.
            return new(attribute, attack, entry.Roles.ToHashSet()) { Profile = p };
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { return null; }
    }

    private sealed record Entry(int SchemaVersion, GameBuild? Build, int HeroId, HeroAttribute? PrimaryAttribute,
        HeroAttackType? AttackType, IReadOnlyList<HeroRole>? Roles, HeroProfile? Profile);
}
