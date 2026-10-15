using DotaAnalyst.Infrastructure.Baseline;
using DotaAnalyst.Infrastructure.Persistence;

namespace DotaAnalyst.Tests.Infrastructure.Persistence;

[Collection(PostgresCollection.Name)]
public class CollectorStorageBudgetTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(long.MaxValue, true)]
    public async Task BudgetStopsCollectionWithoutRemovingData(long limit, bool expected)
    {
        var budget = new CollectorStorageBudget(postgres.DbContextFactory,
            new StorageGuard(TimeProvider.System), new CollectorStorageLimits(limit));
        Assert.Equal(expected, await budget.CanCollectAsync(CancellationToken.None));
    }
}
