using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Infrastructure.Persistence.Catalogs;

namespace DotaAnalyst.Infrastructure.GameData;

// Explicit local catalog maintenance, never invoked by a GET or by replay parsing. The caller owns the database lock.
public sealed class HeroProfileCatalogWriter(IGameBuildCatalog builds, ICatalogStore store, TimeProvider clock)
{
    public async Task<HeroProfileSaveResult> SaveForAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (await builds.ForMatchAsync(at, cancellationToken) is not { } snapshot || snapshot.Catalog.Heroes is not { } heroes)
            return new(null, 0, false);
        cancellationToken.ThrowIfCancellationRequested();
        if (heroes.Any(h => h.Value.Profile is not { Enabled: not null } p || !p.HasValidShape()))
            throw new InvalidDataException("Cannot save an incomplete hero profile snapshot.");
        var valid = heroes.Where(h => h.Value.Profile is { Enabled: true } p && p.HasValidShape()).OrderBy(h => h.Key).ToArray();
        if (valid.Length == 0) return new(snapshot.Build.Id, 0, false);
        var name = HeroProfileCatalogKey.For(snapshot.Build.Id);
        var names = valid.ToDictionary(h => h.Key, h => h.Value.Profile!.Key);
        var details = valid.ToDictionary(h => h.Key, h => JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            build = snapshot.Build,
            heroId = h.Key,
            primaryAttribute = h.Value.PrimaryAttribute,
            attackType = h.Value.AttackType,
            roles = h.Value.Roles.OrderBy(role => role).ToArray(),
            profile = h.Value.Profile,
            description = h.Value.Description,
        }, HeroProfileCatalogKey.JsonOptions));
        var saved = await store.LoadAsync(name, cancellationToken);
        if (saved is not null && saved.Details.Values.Any(json => !BelongsToBuild(json, snapshot.Build.Id)))
            throw new InvalidDataException("Stored profile identity is incomplete or belongs to another build.");
        if (saved is not null && saved.Names.Keys.Any(id => !names.ContainsKey(id)))
            throw new InvalidDataException("Cannot replace a hero profile snapshot with lower coverage.");
        if (saved is not null && saved.Names.Count == names.Count && saved.Details.Count == details.Count
            && names.All(p => saved.Names.GetValueOrDefault(p.Key) == p.Value)
            && details.All(p => saved.Details.TryGetValue(p.Key, out var old) && SameJson(old, p.Value)))
            return new(snapshot.Build.Id, names.Count, false);
        await store.ReplaceAsync(name, names, details, clock.GetUtcNow(), cancellationToken);
        return new(snapshot.Build.Id, names.Count, true);
    }

    private static bool SameJson(string a, string b)
    {
        try
        {
            using var left = JsonDocument.Parse(a);
            using var right = JsonDocument.Parse(b);
            // PostgreSQL jsonb normalizes whitespace and object-key order.
            return JsonElement.DeepEquals(left.RootElement, right.RootElement);
        }
        catch (JsonException) { return false; }
    }

    private static bool BelongsToBuild(string json, string buildId)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("build", out var build) && build.ValueKind == JsonValueKind.Object
                && build.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == buildId;
        }
        catch (JsonException) { return false; }
    }
}

public sealed record HeroProfileSaveResult(string? BuildId, int Heroes, bool Changed);
