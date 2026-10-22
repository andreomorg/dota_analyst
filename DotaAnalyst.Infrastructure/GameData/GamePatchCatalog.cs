using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Infrastructure.GameData;

// Valve's list of patches (datafeed/patchnoteslist): { "patches": [{ "patch_name": "7.41c", "patch_timestamp": unix }] }.
public static class PatchNotesList
{
    private const string Resource = "DotaAnalyst.Patches.patchnoteslist.json";

    // The copy kept with the application (GameData/Patches/patchnoteslist.json, saved from Valve on 22/10/2026).
    public static IReadOnlyList<GamePatch> Embedded { get; } = ReadEmbedded();

    // Throws InvalidDataException when the JSON is not the list.
    public static IReadOnlyList<GamePatch> Read(Stream json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("patches").EnumerateArray()
                .Select(patch => new GamePatch(
                    patch.GetProperty("patch_name").GetString() is { Length: > 0 } name ? name : throw new InvalidDataException("A patch has no name."),
                    DateTimeOffset.FromUnixTimeSeconds(patch.GetProperty("patch_timestamp").GetInt64())))
                .OrderBy(patch => patch.ReleasedAt)
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("The patch list is not Valve's format.", ex);
        }
    }

    private static IReadOnlyList<GamePatch> ReadEmbedded()
    {
        using var stream = typeof(PatchNotesList).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"{Resource} is not embedded.");
        return Read(stream);
    }
}

// Only the copy kept with the application: production and tests never call Valve for this.
public sealed class EmbeddedPatchCatalog : IGamePatchCatalog
{
    public Task<IReadOnlyList<GamePatch>> GetPatchesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PatchNotesList.Embedded);
}

// Valve's live list (GameData:SyncEnabled, the owner's machine), kept for 6 hours; when Valve fails or answers
// something else, the kept copy, tried again after 15 minutes.
public sealed class ValvePatchCatalog(HttpClient httpClient, IMemoryCache cache, ILogger<ValvePatchCatalog> logger) : IGamePatchCatalog
{
    public const string Url = "https://www.dota2.com/datafeed/patchnoteslist?language=english";
    private const string CacheKey = "game_patches";
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(6);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(15);

    public async Task<IReadOnlyList<GamePatch>> GetPatchesAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<GamePatch>? patches) && patches is not null)
        {
            return patches;
        }

        try
        {
            using var response = await httpClient.GetAsync(Url, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var json = await response.Content.ReadAsStreamAsync(cancellationToken);
            patches = PatchNotesList.Read(json);
            if (patches.Count == 0)
            {
                throw new InvalidDataException("Valve answered an empty patch list.");
            }

            cache.Set(CacheKey, patches, CacheEntries.Catalog(KeepFor));
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException
            or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            logger.LogWarning(ex, "Could not read Valve's patch list; using the copy kept with the application");
            patches = PatchNotesList.Embedded;
            cache.Set(CacheKey, patches, CacheEntries.Catalog(RetryAfter));
        }

        return patches;
    }
}
