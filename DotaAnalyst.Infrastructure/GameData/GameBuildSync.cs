using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Infrastructure.Persistence.GameData;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Infrastructure.GameData;

// Keeps every build since GameData:Since in the local database: for each new commit, the blob hash of every needed
// file (three API calls) and the text of the files not kept yet (a file that did not change is not downloaded again).
// Builds are kept in release order, so an interruption (rate limit, network) resumes where it stopped. A build kept
// before a path joined GameFiles.CommonPaths (neutral_items.txt, 21/10/2026) is listed again and only the new file is
// downloaded.
public sealed class GameBuildSynchronizer(D2vpkrClient client, IGameDataStore store, IOptions<GameDataOptions> options,
    ILogger<GameBuildSynchronizer> logger)
{
    // Returns how many builds were added.
    public async Task<int> SyncAsync(CancellationToken cancellationToken)
    {
        var kept = (await store.LoadBuildsAsync(cancellationToken))
            .Where(build => GameFiles.CommonPaths.All(build.Files.ContainsKey))
            .Select(build => build.Build.Id).ToHashSet();
        var added = 0;
        foreach (var commit in await client.ListBuildsAsync(options.Value.Since, cancellationToken))
        {
            if (kept.Contains(commit.Sha))
            {
                continue;
            }

            var files = await client.ListFilesAsync(commit.Sha, cancellationToken);
            var blobs = files.Values.Distinct().ToList();
            var missing = blobs.Except(await store.KeptFilesAsync(blobs, cancellationToken)).ToHashSet();
            foreach (var (path, blob) in files.Where(file => missing.Remove(file.Value)))
            {
                await store.KeepFileAsync(blob, await client.DownloadAsync(commit.Sha, path, cancellationToken), cancellationToken);
            }

            await store.KeepBuildAsync(new StoredGameBuild(new GameBuild(commit.Sha, commit.ClientVersion, commit.Date), files), cancellationToken);
            added++;
            logger.LogInformation("Kept game build {Build} (Client {Client}, {ReleasedAt:u}): {Files} files",
                commit.Sha[..8], commit.ClientVersion, commit.Date, files.Count);
        }

        return added;
    }
}

// Runs the synchronizer at startup and every GameData:SyncEvery; waits for the rate limit when GitHub says so. Only
// with GameData:SyncEnabled and a database: it runs on the owner's machine (heavy work stays there).
public sealed class GameBuildSyncService(GameBuildSynchronizer synchronizer, IOptions<GameDataOptions> options, TimeProvider timeProvider,
    ILogger<GameBuildSyncService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = options.Value.SyncEvery;
            try
            {
                var added = await synchronizer.SyncAsync(stoppingToken);
                logger.LogInformation("Game builds synchronized: {Added} new", added);
            }
            catch (GitHubRateLimitedException ex)
            {
                wait = ex.ResetAt - timeProvider.GetUtcNow() + TimeSpan.FromMinutes(1);
                logger.LogInformation("GitHub rate limit reached; game builds resume at {ResetAt:u}", ex.ResetAt);
            }
            catch (Exception ex) when (ex is HttpRequestException or StorageUnavailableException or TaskCanceledException
                && !stoppingToken.IsCancellationRequested)
            {
                wait = RetryAfterFailure;
                logger.LogWarning(ex, "Could not synchronize the game builds; trying again in {Wait}", wait);
            }

            await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.FromMinutes(1), timeProvider, stoppingToken);
        }
    }
}
