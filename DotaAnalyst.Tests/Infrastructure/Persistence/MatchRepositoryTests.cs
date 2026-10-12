using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.OpenDota;
using DotaAnalyst.Infrastructure.Persistence;
using DotaAnalyst.Infrastructure.Persistence.Matches;
using DotaAnalyst.Tests.Domain;
using Microsoft.EntityFrameworkCore;

namespace DotaAnalyst.Tests.Infrastructure.Persistence;

[Collection(PostgresCollection.Name)]
public class MatchRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset SavedAt = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SaveThenFind_KeepsTheWholeMatch()
    {
        // Arrange: a real OpenDota match (ten players with benchmarks and items)
        var match = OpenDotaMatchMappingsTests.LoadFixture().ToMatchDetails();
        var repository = new MatchRepository(postgres.DbContextFactory, new StorageGuard(TimeProvider.System));

        // Act
        await repository.SaveAsync(match, SavedAt, CancellationToken.None);
        var found = await repository.FindAsync(match.MatchId, CancellationToken.None);

        // Assert: Equivalent compares every property, including the lists inside
        Assert.NotNull(found);
        Assert.Equivalent(match, found.Match, strict: true);
        Assert.Equal(SavedAt, found.SavedAt);
    }

    [Fact]
    public async Task SaveThenFind_KeepsUnknownResultAndAnonymousPlayers()
    {
        var match = CreateMatch(isParsed: true, winningTeam: null, MatchPlayerBuilder.Create(accountId: null));
        var repository = new MatchRepository(postgres.DbContextFactory, new StorageGuard(TimeProvider.System));

        await repository.SaveAsync(match, SavedAt, CancellationToken.None);
        var found = await repository.FindAsync(match.MatchId, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Null(found.Match.WinningTeam);
        Assert.Null(Assert.Single(found.Match.Players).AccountId);
        Assert.Equivalent(match, found.Match, strict: true);
    }

    [Fact]
    public async Task Find_RowSavedBeforeTheReplayUrlExisted_IsStillRead()
    {
        // Rows saved before "replayUrl" was part of the format don't have the key at all.
        var match = OpenDotaMatchMappingsTests.LoadFixture().ToMatchDetails();
        Assert.NotNull(match.ReplayUrl);
        var repository = new MatchRepository(postgres.DbContextFactory, new StorageGuard(TimeProvider.System));
        await repository.SaveAsync(match, SavedAt, CancellationToken.None);
        await using (var db = await postgres.DbContextFactory.CreateDbContextAsync())
        {
            // jsonb "-" removes a key.
            await db.Database.ExecuteSqlRawAsync("UPDATE matches SET details = details - 'replayUrl'");
        }

        var found = await repository.FindAsync(match.MatchId, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Null(found.Match.ReplayUrl);
        Assert.Equivalent(match with { ReplayUrl = null }, found.Match, strict: true);
    }

    [Fact]
    public async Task Find_UnknownMatch_ReturnsNull()
    {
        var repository = new MatchRepository(postgres.DbContextFactory, new StorageGuard(TimeProvider.System));

        Assert.Null(await repository.FindAsync(MatchIdOf(123), CancellationToken.None));
    }

    [Fact]
    public async Task Save_UnparsedMatch_IsReplacedByParsedOne()
    {
        var repository = new MatchRepository(postgres.DbContextFactory, new StorageGuard(TimeProvider.System));

        await repository.SaveAsync(CreateMatch(isParsed: false, radiantScore: 1), SavedAt, CancellationToken.None);
        await repository.SaveAsync(CreateMatch(isParsed: true, radiantScore: 2), SavedAt, CancellationToken.None);

        var found = await repository.FindAsync(MatchIdOf(42), CancellationToken.None);
        Assert.True(found!.Match.IsParsed);
        Assert.Equal(2, found.Match.RadiantScore);
    }

    [Fact]
    public async Task Save_ParsedMatch_IsNeverOverwritten()
    {
        var repository = new MatchRepository(postgres.DbContextFactory, new StorageGuard(TimeProvider.System));

        await repository.SaveAsync(CreateMatch(isParsed: true, radiantScore: 1), SavedAt, CancellationToken.None);
        await repository.SaveAsync(CreateMatch(isParsed: false, radiantScore: 2), SavedAt, CancellationToken.None);

        var found = await repository.FindAsync(MatchIdOf(42), CancellationToken.None);
        Assert.True(found!.Match.IsParsed);
        Assert.Equal(1, found.Match.RadiantScore);
    }

    [Fact]
    public async Task Save_ParsedMatch_ReplacesAnUnreadableSavedRow()
    {
        // A saved row that can't be read anymore (format changed, damaged data) must not block the match forever.
        var repository = new MatchRepository(postgres.DbContextFactory, new StorageGuard(TimeProvider.System));
        await repository.SaveAsync(CreateMatch(isParsed: true, radiantScore: 1), SavedAt, CancellationToken.None);
        await using (var db = await postgres.DbContextFactory.CreateDbContextAsync())
        {
            const string unreadable = """{"version": 99}""";
            await db.Database.ExecuteSqlAsync($"UPDATE matches SET details = CAST({unreadable} AS jsonb)");
        }

        await repository.SaveAsync(CreateMatch(isParsed: true, radiantScore: 2), SavedAt, CancellationToken.None);

        var found = await repository.FindAsync(MatchIdOf(42), CancellationToken.None);
        Assert.Equal(2, found!.Match.RadiantScore);
    }

    [Fact]
    public async Task Save_SameMatchManyTimesAtOnce_DoesNotFail()
    {
        var match = CreateMatch(isParsed: false);
        var repository = new MatchRepository(postgres.DbContextFactory, new StorageGuard(TimeProvider.System));

        // Requests saving the same match at the same moment: none of them fails on the duplicate key.
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => repository.SaveAsync(match, SavedAt, CancellationToken.None)));

        Assert.NotNull(await repository.FindAsync(match.MatchId, CancellationToken.None));
    }

    private static MatchDetails CreateMatch(
        bool isParsed, Team? winningTeam = Team.Dire, MatchPlayer? player = null, int radiantScore = 10) =>
        new(MatchIdOf(42), TimeSpan.FromSeconds(1800.5), new DateTimeOffset(2026, 10, 11, 20, 0, 0, TimeSpan.FromHours(-3)),
            winningTeam, radiantScore, 20, isParsed, player is null ? [] : [player]);

    private static MatchId MatchIdOf(long value)
    {
        MatchId.TryCreate(value, out var matchId);
        return matchId;
    }
}
