using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Heroes;
using DotaAnalyst.Application.Models;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Application;

public sealed class GetHeroStateHandlerTests
{
    [Theory]
    [InlineData(10, "found")]
    [InlineData(11, "noExactSample")]
    [InlineData(61, "outOfRange")]
    public async Task StateUsesExactGameSecondAndReportsKnownBuild(int seconds, string status)
    {
        MatchId.TryCreate(123, out var id);
        var analysis = new ReplayAnalysis(id, 60, [new(1, Team.Radiant, Lane.Bottom, 0,0,0,0,0)],
            new MatchTimeline([0,60], [new(1, Team.Radiant, [0,0], [0,0])]));
        var source = new Archive(new(analysis, [new HeroPositions(1, [new PositionSample(10, 1, 2)])], StartedAt: DateTimeOffset.UnixEpoch));
        var catalogs = new Catalogs();
        var handler = new GetHeroStateHandler(source, new GameCatalogLoader(catalogs,catalogs,catalogs,catalogs));
        var result = await handler.HandleAsync(id, 1, seconds, default);
        Assert.Equal(status, result.Status);
        if (status == "found") { Assert.Equal(10, result.Sample!.Value.Seconds); Assert.Equal("matchBuild", result.CatalogScope); }
    }

    [Fact]
    public async Task NoDocumentDoesNotRequestCatalogAndDeathDoesNotReturnLivingSample()
    {
        MatchId.TryCreate(123, out var id);
        var handler = new GetHeroStateHandler(new Archive(null), null!);
        Assert.Equal("noDocument", (await handler.HandleAsync(id,1,0,default)).Status);
        var analysis = new ReplayAnalysis(id,60,[new(1,Team.Radiant,Lane.Bottom,0,0,0,0,0)],
            new MatchTimeline([0,60],[new(1,Team.Radiant,[0,0],[0,0])]),
            [new HeroDeath(1,10,0,0,KillerKind.Other,null,false)]);
        var catalogs=new Catalogs();
        handler=new(new Archive(new(analysis,[new HeroPositions(1,[new PositionSample(10,0,0)])],StartedAt:DateTimeOffset.UnixEpoch)),
            new(catalogs,catalogs,catalogs,catalogs));
        Assert.Equal("noExactSample",(await handler.HandleAsync(id,1,10,default)).Status);
    }

    private sealed class Archive(ArchivedReplay? replay) : IArchivedReplaySource
    { public Task<ArchivedReplay?> FindAsync(MatchId id, CancellationToken ct) => Task.FromResult(replay); }

    private sealed class Catalogs : IHeroCatalog, IItemCatalog, IAbilityCatalog, IGameBuildCatalog
    {
        public Task<IReadOnlyDictionary<int,Hero>> GetHeroesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<int,Hero>>(new Dictionary<int,Hero>());
        public Task<IReadOnlyDictionary<int,Item>> GetItemsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<int,Item>>(new Dictionary<int,Item>());
        public Task<IReadOnlyDictionary<string,AbilityDetails>> GetAbilitiesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<string,AbilityDetails>>(new Dictionary<string,AbilityDetails>());
        public Task<IReadOnlyDictionary<int,HeroKit>> GetKitsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<int,HeroKit>>(new Dictionary<int,HeroKit>());
        public Task<string?> GetPatchAsync(CancellationToken ct) => Task.FromResult<string?>(null);
        public Task<(GameBuild Build,GameCatalog Catalog)?> ForMatchAsync(DateTimeOffset? at,CancellationToken ct) =>
            Task.FromResult<(GameBuild,GameCatalog)?>((new("build",6962,DateTimeOffset.UnixEpoch),GameCatalog.Empty));
    }
}
