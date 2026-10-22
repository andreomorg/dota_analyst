using System.Text.Json;
using DotaAnalyst.Domain.Abilities;
using Microsoft.EntityFrameworkCore;

namespace DotaAnalyst.Infrastructure.Persistence.GameData;

// A build kept in the database: the build and the blob hash of each file path it needs.
public sealed record StoredGameBuild(GameBuild Build, IReadOnlyDictionary<string, string> Files);

// The kept game builds and files. Same rules as the repositories: one short-lived DbContext per operation, everything
// through StorageGuard (which turns database failures into StorageUnavailableException).
public interface IGameDataStore
{
    Task<IReadOnlyList<StoredGameBuild>> LoadBuildsAsync(CancellationToken cancellationToken);

    // The blob hashes among these that are already kept.
    Task<IReadOnlySet<string>> KeptFilesAsync(IReadOnlyCollection<string> blobShas, CancellationToken cancellationToken);

    Task KeepFileAsync(string blobSha, string content, CancellationToken cancellationToken);

    Task KeepBuildAsync(StoredGameBuild build, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, string>> LoadFilesAsync(IReadOnlyCollection<string> blobShas, CancellationToken cancellationToken);
}

public sealed class GameDataStore(IDbContextFactory<AppDbContext> dbContextFactory, StorageGuard storageGuard, TimeProvider timeProvider)
    : IGameDataStore
{
    public Task<IReadOnlyList<StoredGameBuild>> LoadBuildsAsync(CancellationToken cancellationToken) =>
        storageGuard.RunAsync("reading the game builds", async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var rows = await db.GameBuilds.AsNoTracking().OrderBy(build => build.ReleasedAt).ToListAsync(cancellationToken);
            return (IReadOnlyList<StoredGameBuild>)rows.Select(row => new StoredGameBuild(
                new GameBuild(row.CommitSha, row.ClientVersion, row.ReleasedAt),
                JsonSerializer.Deserialize<Dictionary<string, string>>(row.Files) ?? [])).ToList();
        });

    public Task<IReadOnlySet<string>> KeptFilesAsync(IReadOnlyCollection<string> blobShas, CancellationToken cancellationToken) =>
        storageGuard.RunAsync("reading the kept game files", async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var kept = await db.GameFiles.AsNoTracking().Where(file => blobShas.Contains(file.BlobSha))
                .Select(file => file.BlobSha).ToListAsync(cancellationToken);
            return (IReadOnlySet<string>)kept.ToHashSet();
        });

    public Task KeepFileAsync(string blobSha, string content, CancellationToken cancellationToken) =>
        storageGuard.RunAsync($"keeping game file {blobSha}", async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            // The same hash is always the same content: a second writer changes nothing.
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO game_files (blob_sha, content) VALUES ({blobSha}, {content})
                ON CONFLICT (blob_sha) DO NOTHING
                """,
                cancellationToken);
        });

    public Task KeepBuildAsync(StoredGameBuild build, CancellationToken cancellationToken) =>
        storageGuard.RunAsync($"keeping game build {build.Build.Id}", async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var files = JsonSerializer.Serialize(build.Files);
            var releasedAt = build.Build.ReleasedAt.ToUniversalTime();
            var savedAt = timeProvider.GetUtcNow().ToUniversalTime();
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO game_builds (commit_sha, client_version, released_at, files, saved_at)
                VALUES ({build.Build.Id}, {build.Build.ClientVersion}, {releasedAt}, CAST({files} AS jsonb), {savedAt})
                ON CONFLICT (commit_sha) DO UPDATE SET
                    client_version = EXCLUDED.client_version,
                    released_at = EXCLUDED.released_at,
                    files = EXCLUDED.files,
                    saved_at = EXCLUDED.saved_at
                """,
                cancellationToken);
        });

    public Task<IReadOnlyDictionary<string, string>> LoadFilesAsync(IReadOnlyCollection<string> blobShas, CancellationToken cancellationToken) =>
        storageGuard.RunAsync("reading game files", async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var rows = await db.GameFiles.AsNoTracking().Where(file => blobShas.Contains(file.BlobSha)).ToListAsync(cancellationToken);
            return (IReadOnlyDictionary<string, string>)rows.ToDictionary(row => row.BlobSha, row => row.Content);
        });
}
