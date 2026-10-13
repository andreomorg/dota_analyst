using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Stats;
using DotaAnalyst.Infrastructure.Baseline;
using DotaAnalyst.Infrastructure.Persistence;
using DotaAnalyst.Infrastructure.Persistence.Baseline;
using DotaAnalyst.Infrastructure.ReplayParser;
using DotaAnalyst.Tests.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotaAnalyst.Tests.Infrastructure.Persistence;

[Collection(PostgresCollection.Name)]
public class BaselineHistoryBackfillTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static HeroMetrics Hero(int heroId, Team team) =>
        new(heroId, team, 1, 0, 0, 0, 0, 0, 2000, null, 10, 0.5, 0, 0, null, 0, 0, RoleInferred: true);

    [Fact]
    public async Task RowsWithoutStatistics_GetThemFromTheKeptDocument_Once()
    {
        var repository = new BaselineRepository(postgres.DbContextFactory, new StorageGuard(TimeProvider.System), NullLogger<BaselineRepository>.Instance);
        // Match 9034320024 is the one of the test document (Lich 31 and Slardar 28); match 7 has no kept document.
        await repository.SaveAsync(Collected(9034320024, Hero(31, Team.Radiant), Hero(28, Team.Dire)), CancellationToken.None);
        await repository.SaveAsync(Collected(7, Hero(5, Team.Radiant)), CancellationToken.None);
        var backfill = new BaselineHistoryBackfill(
            postgres.DbContextFactory, new StorageGuard(TimeProvider.System), new OneDocument(9034320024),
            NullLogger<BaselineHistoryBackfill>.Instance);

        Assert.Equal(1, await backfill.RunAsync(CancellationToken.None));
        Assert.Equal(0, await backfill.RunAsync(CancellationToken.None));

        var lich = Assert.Single(await repository.GetSamplesAsync(31, 1, BaselineSource.Pro, Now.AddDays(-60), CancellationToken.None));
        Assert.Equal(12, lich.History!.ValueAt(StatKey.LastHits, 600));
        var other = Assert.Single(await repository.GetSamplesAsync(5, 1, BaselineSource.Pro, Now.AddDays(-60), CancellationToken.None));
        Assert.Null(other.History);
    }

    private static BaselineMatch Collected(long matchId, params HeroMetrics[] heroes) =>
        new(ReplayBuilder.MatchIdOf(matchId), BaselineSource.Pro, Now.AddDays(-1), 690, BaselineMatchStatus.Collected, heroes, Now);

    private sealed class OneDocument(long matchId) : IReplayDocumentArchive
    {
        public Task KeepAsync(MatchId id, int schemaVersion, string document, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string?> FindAsync(MatchId id, CancellationToken cancellationToken) =>
            Task.FromResult(id.Value == matchId ? ReplayParserClientTests.MatchJson() : null);
    }
}
