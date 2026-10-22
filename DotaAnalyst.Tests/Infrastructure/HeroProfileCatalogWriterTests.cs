using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Infrastructure.GameData;
using DotaAnalyst.Infrastructure.Persistence.Catalogs;

namespace DotaAnalyst.Tests.Infrastructure;

public sealed class HeroProfileCatalogWriterTests
{
    [Fact]
    public async Task PartialProfileCannotReplaceSavedSnapshotAndCancellationIsObserved()
    {
        var catalog = new GameCatalog(Heroes: new Dictionary<int, HeroDetails>
        { [1] = new(HeroAttribute.Strength, HeroAttackType.Melee, new HashSet<HeroRole>()) });
        var store = new Store();
        var writer = new HeroProfileCatalogWriter(new Builds(catalog), store, TimeProvider.System);
        await Assert.ThrowsAsync<InvalidDataException>(() => writer.SaveForAsync(DateTimeOffset.UtcNow, default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.SaveForAsync(DateTimeOffset.UtcNow, new CancellationToken(true)));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task SavingSameBuildTwiceDoesNotRewriteOrIncludeDisabledUnits()
    {
        var profile = new HeroProfile("npc_dota_hero_test", true, new(null,null,null,null,null,null),
            new(null,null,null,null,null,null,null,null,null), new(null,null), new(null,null,null,null), new(null,null,null,null));
        var details = new HeroDetails(HeroAttribute.Strength, HeroAttackType.Melee, new HashSet<HeroRole>()) { Profile = profile };
        var builds = new Builds(new GameCatalog(Heroes: new Dictionary<int, HeroDetails> { [1] = details,
            [127] = details with { Profile = profile with { Enabled = false } } }));
        var store = new Store();
        var writer = new HeroProfileCatalogWriter(builds, store, TimeProvider.System);
        var first = await writer.SaveForAsync(DateTimeOffset.UtcNow, default);
        var second = await writer.SaveForAsync(DateTimeOffset.UtcNow, default);
        Assert.True(first.Changed);
        Assert.False(second.Changed);
        Assert.Equal(1, second.Heroes);
        Assert.Equal(1, store.Writes);
        Assert.Contains("description", store.Saved!.Details[1]);
        Assert.Contains("pt-BR", store.Saved.Details[1]);
        // Enums as text: reordering one never changes what a saved row means.
        Assert.Contains("\"strength\"", store.Saved.Details[1]);
        Assert.Contains("\"melee\"", store.Saved.Details[1]);
        var read = await new HeroProfileCatalogReader(store).FindAsync("test-build", 1, default);
        Assert.Equal(details, read);
        // A row saved before, with numbers, still reads.
        var numeric = System.Text.Json.Nodes.JsonNode.Parse(store.Saved.Details[1])!.AsObject();
        numeric["primaryAttribute"] = 0;
        numeric["attackType"] = 0;
        var text = store.Saved;
        store.Saved = store.Saved with { Details = new Dictionary<int, string> { [1] = numeric.ToJsonString() } };
        Assert.Equal(details, await new HeroProfileCatalogReader(store).FindAsync("test-build", 1, default));
        store.Saved = text;
        Assert.Null(await new HeroProfileCatalogReader(store).FindAsync("wrong-build", 1, default));
        store.Saved = store.Saved with { Names = new Dictionary<int,string> { [1] = profile.Key, [999] = "previous-valid-hero" } };
        await Assert.ThrowsAsync<InvalidDataException>(() => writer.SaveForAsync(DateTimeOffset.UtcNow, default));
        var partial = System.Text.Json.Nodes.JsonNode.Parse(store.Saved.Details[1])!;
        partial.AsObject().Remove("primaryAttribute");
        store.Saved = store.Saved with { Details = new Dictionary<int,string> { [1] = partial.ToJsonString() } };
        Assert.Null(await new HeroProfileCatalogReader(store).FindAsync("test-build", 1, default));
    }

    private sealed class Builds(GameCatalog catalog) : IGameBuildCatalog
    {
        public Task<(GameBuild Build, GameCatalog Catalog)?> ForMatchAsync(DateTimeOffset? at, CancellationToken ct) =>
            Task.FromResult<(GameBuild, GameCatalog)?>((new("test-build", 6962, DateTimeOffset.UnixEpoch), catalog));
    }

    private sealed class Store : ICatalogStore
    {
        public SavedCatalog? Saved;
        public int Writes;
        public Task<SavedCatalog?> LoadAsync(string catalog, CancellationToken ct) => Task.FromResult(Saved);
        public Task ReplaceAsync(string catalog, IReadOnlyDictionary<int,string> names, IReadOnlyDictionary<int,string> details,
            DateTimeOffset at, CancellationToken ct)
        {
            Writes++;
            Saved = new(names, at) { Details = details.ToDictionary(p => p.Key, p =>
                System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonDocument.Parse(p.Value).RootElement,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true })) };
            return Task.CompletedTask;
        }
    }
}
