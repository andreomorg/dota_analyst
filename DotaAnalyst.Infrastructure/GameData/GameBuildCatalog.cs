using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Infrastructure.Caching;
using DotaAnalyst.Infrastructure.Catalogs;
using DotaAnalyst.Infrastructure.Persistence.GameData;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Infrastructure.GameData;

// The game data of a match's build (IGameBuildCatalog): picks the build (GameBuild.For, with the match's official
// sub-patch), reads its files from the local database and maps them (GameFileMappings), keeping the mapped build in
// memory. Builds with the same files share one mapped catalog. A database failure or an unreadable file gives null
// (the current catalogs are used).
public sealed class GameBuildCatalog(IGameDataStore store, IMemoryCache cache, CatalogLoadLocks locks,
    IOptions<GameDataOptions> options, IGamePatchCatalog patches, ILogger<GameBuildCatalog> logger) : IGameBuildCatalog
{
    private const string BuildsKey = "game_builds";
    private static readonly TimeSpan BuildsFor = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CatalogFor = TimeSpan.FromHours(6);

    public async Task<(GameBuild Build, GameCatalog Catalog)?> ForMatchAsync(DateTimeOffset? startedAt, CancellationToken cancellationToken)
    {
        try
        {
            var builds = await BuildsAsync(cancellationToken);
            var patch = GamePatch.For(await patches.GetPatchesAsync(cancellationToken), startedAt);
            if (GameBuild.For(builds.Select(stored => stored.Build), startedAt, options.Value.Since, patch) is not { } build)
            {
                return null;
            }

            var stored = builds.First(candidate => candidate.Build == build);
            return await CatalogAsync(stored, cancellationToken) is { } catalog ? (build, catalog) : null;
        }
        catch (StorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not read the game builds; using the current catalogs");
            return null;
        }
    }

    private async Task<IReadOnlyList<StoredGameBuild>> BuildsAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(BuildsKey, out IReadOnlyList<StoredGameBuild>? builds) && builds is not null)
        {
            return builds;
        }

        builds = await store.LoadBuildsAsync(cancellationToken);
        cache.Set(BuildsKey, builds, CacheEntries.Catalog(BuildsFor));
        return builds;
    }

    private async Task<GameCatalog?> CatalogAsync(StoredGameBuild stored, CancellationToken cancellationToken)
    {
        // The same files make the same catalog, whatever the commit.
        var key = "game_build:" + string.Join(',', stored.Files.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => file.Value));
        if (cache.TryGetValue(key, out GameCatalog? catalog) && catalog is not null)
        {
            return catalog;
        }

        // One mapping at a time per build: mapping reads ~6 MB of text.
        var buildLock = locks.For(key);
        await buildLock.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(key, out catalog) && catalog is not null)
            {
                return catalog;
            }

            var contents = await store.LoadFilesAsync(stored.Files.Values.Distinct().ToList(), cancellationToken);
            if (stored.Files.Values.Any(blob => !contents.ContainsKey(blob)))
            {
                logger.LogWarning("Game build {Build} is missing files; using the current catalogs", stored.Build.Id);
                return null;
            }

            catalog = GameFileMappings.ToCatalog(new GameFiles(stored.Files.ToDictionary(file => file.Key, file => contents[file.Value])))
                with
            { FileVersions = stored.Files };
            cache.Set(key, catalog, CacheEntries.Catalog(CatalogFor));
            return catalog;
        }
        catch (InvalidDataException ex)
        {
            logger.LogWarning(ex, "Game build {Build} has an unreadable file; using the current catalogs", stored.Build.Id);
            return null;
        }
        finally
        {
            buildLock.Release();
        }
    }
}

// Without a database there is nowhere to keep the builds.
public sealed class NoGameBuilds : IGameBuildCatalog
{
    public Task<(GameBuild Build, GameCatalog Catalog)?> ForMatchAsync(DateTimeOffset? startedAt, CancellationToken cancellationToken) =>
        Task.FromResult<(GameBuild Build, GameCatalog Catalog)?>(null);
}
