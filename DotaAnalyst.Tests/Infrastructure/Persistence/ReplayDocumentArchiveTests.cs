using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.Persistence;
using DotaAnalyst.Infrastructure.Persistence.Replays;
using DotaAnalyst.Tests.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotaAnalyst.Tests.Infrastructure.Persistence;

[Collection(PostgresCollection.Name)]
public class ReplayDocumentArchiveTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly MatchId Match = ReplayBuilder.MatchIdOf(9034320024);

    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Keep_StoresTheDocumentAndReplacesAnEarlierOne()
    {
        var archive = new ReplayDocumentArchive(
            postgres.DbContextFactory, new StorageGuard(TimeProvider.System), TimeProvider.System,
            NullLogger<ReplayDocumentArchive>.Instance);

        await archive.KeepAsync(Match, 1, """{"schemaVersion": 1, "kills": []}""", CancellationToken.None);
        await archive.KeepAsync(Match, 1, """{"schemaVersion": 1, "kills": [{"t": 12.5}]}""", CancellationToken.None);

        await using var db = await postgres.DbContextFactory.CreateDbContextAsync();
        var row = await db.ReplayDocuments.AsNoTracking().SingleAsync();
        Assert.Equal((Match.Value, 1), (row.MatchId, row.SchemaVersion));
        // jsonb keeps the content, not the text: compare parsed.
        Assert.Equal(12.5, System.Text.Json.JsonDocument.Parse(row.Document).RootElement.GetProperty("kills")[0].GetProperty("t").GetDouble());
    }

    [Fact]
    public async Task Find_ReturnsTheKeptDocument_OrNull()
    {
        var archive = new ReplayDocumentArchive(
            postgres.DbContextFactory, new StorageGuard(TimeProvider.System), TimeProvider.System,
            NullLogger<ReplayDocumentArchive>.Instance);

        Assert.Null(await archive.FindAsync(Match, CancellationToken.None));

        await archive.KeepAsync(Match, 1, """{"schemaVersion": 1, "kills": [{"t": 12.5}]}""", CancellationToken.None);
        var found = await archive.FindAsync(Match, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(12.5, System.Text.Json.JsonDocument.Parse(found).RootElement.GetProperty("kills")[0].GetProperty("t").GetDouble());
    }

    [Fact]
    public async Task KeepAndFind_WithTheDatabaseDown_DoNotThrow()
    {
        var archive = new ReplayDocumentArchive(
            new BrokenFactory(), new StorageGuard(TimeProvider.System), TimeProvider.System,
            NullLogger<ReplayDocumentArchive>.Instance);

        await archive.KeepAsync(Match, 1, "{}", CancellationToken.None);
        Assert.Null(await archive.FindAsync(Match, CancellationToken.None));
    }

    private sealed class BrokenFactory : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => throw new Npgsql.NpgsqlException("connection refused");
    }
}
