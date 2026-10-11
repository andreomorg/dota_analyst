using DotaAnalyst.Infrastructure.Persistence;
using DotaAnalyst.Infrastructure.Persistence.Catalogs;

namespace DotaAnalyst.Tests.Infrastructure.Persistence;

[Collection(PostgresCollection.Name)]
public class CatalogStoreTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset LoadedAt = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Load_NothingSaved_ReturnsNull()
    {
        var store = new CatalogStore(postgres.DbContextFactory, new StorageGuard(TimeProvider.System));

        Assert.Null(await store.LoadAsync("heroes", CancellationToken.None));
    }

    [Fact]
    public async Task Replace_ThenLoad_ReturnsNamesAndTime()
    {
        var store = new CatalogStore(postgres.DbContextFactory, new StorageGuard(TimeProvider.System));

        await store.ReplaceAsync("heroes", Names((31, "Lich"), (75, "Silencer")), LoadedAt, CancellationToken.None);
        var saved = await store.LoadAsync("heroes", CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal(Names((31, "Lich"), (75, "Silencer")), saved.Names);
        Assert.Equal(LoadedAt, saved.UpdatedAt);
    }

    [Fact]
    public async Task Replace_RemovesEntriesThatAreGone()
    {
        var store = new CatalogStore(postgres.DbContextFactory, new StorageGuard(TimeProvider.System));

        await store.ReplaceAsync("heroes", Names((31, "Lich"), (75, "Silencer")), LoadedAt, CancellationToken.None);
        await store.ReplaceAsync("heroes", Names((31, "Lich (new)")), LoadedAt.AddDays(1), CancellationToken.None);
        var saved = await store.LoadAsync("heroes", CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal(Names((31, "Lich (new)")), saved.Names);
        Assert.Equal(LoadedAt.AddDays(1), saved.UpdatedAt);
    }

    [Fact]
    public async Task Catalogs_AreKeptApart()
    {
        var store = new CatalogStore(postgres.DbContextFactory, new StorageGuard(TimeProvider.System));

        await store.ReplaceAsync("heroes", Names((1, "Anti-Mage")), LoadedAt, CancellationToken.None);
        await store.ReplaceAsync("items", Names((1, "Blink Dagger")), LoadedAt, CancellationToken.None);

        Assert.Equal("Anti-Mage", (await store.LoadAsync("heroes", CancellationToken.None))!.Names[1]);
        Assert.Equal("Blink Dagger", (await store.LoadAsync("items", CancellationToken.None))!.Names[1]);
    }

    private static Dictionary<int, string> Names(params (int Id, string Name)[] entries) =>
        entries.ToDictionary(entry => entry.Id, entry => entry.Name);
}
