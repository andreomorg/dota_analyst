using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Baseline;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Tests.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotaAnalyst.Tests.Application;

public class CollectBaselineHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri ReplayUrl = new("http://replay.valve.net/570/1_2.dem.bz2");

    [Fact]
    public async Task ProMatchesFirst_SkippingKnownOnes_SavesTheTenHeroes()
    {
        var repository = new FakeBaseline { Known = { 1 } };
        var parser = new FakeParser();
        var handler = CreateHandler(repository, parser, pro: [1, 2], highRank: [3]);

        var result = await handler.CollectNextAsync(CancellationToken.None);

        Assert.Equal(CollectResult.Progressed, result);
        var saved = Assert.Single(repository.Saved);
        Assert.Equal((2L, BaselineSource.Pro, BaselineMatchStatus.Collected), (saved.MatchId.Value, saved.Source, saved.Status));
        Assert.Single(saved.Metrics);
        Assert.Equal([2L], parser.Parsed);
        // And each hero's statistics minute by minute, for the comparison at any moment.
        var history = Assert.Single(saved.Histories!);
        Assert.Equal((31, 40), (history.Key, history.Value.ValueAt(DotaAnalyst.Domain.Stats.StatKey.LastHits, 1200)));
    }

    [Fact]
    public async Task CollectingMatch_AddsFirstBigItemTimingToBaselineMetrics()
    {
        var parser = new FakeParser
        {
            Purchases = [new ItemPurchase(31, 900, "blink")],
        };
        var items = new FakeItemCatalog(
            new Item(1, "Blink Dagger")
            {
                Details = new ItemDetails("blink", 2250, [], isConsumable: false),
            });
        var repository = new FakeBaseline();
        var handler = CreateHandler(repository, parser, pro: [2], itemCatalog: items);

        await handler.CollectNextAsync(CancellationToken.None);

        Assert.Equal(900, Assert.Single(Assert.Single(repository.Saved).Metrics).FirstBigItemSeconds);
    }

    [Fact]
    public async Task WhenProMatchesAreAllKnown_HighRankComesNext()
    {
        var repository = new FakeBaseline { Known = { 1 } };
        var handler = CreateHandler(repository, new FakeParser(), pro: [1], highRank: [3]);

        await handler.CollectNextAsync(CancellationToken.None);

        Assert.Equal(BaselineSource.HighRank, Assert.Single(repository.Saved).Source);
    }

    [Fact]
    public async Task WithoutAReplay_IsSavedAsNoReplay_WithoutParsing()
    {
        var repository = new FakeBaseline();
        var parser = new FakeParser();
        var handler = CreateHandler(repository, parser, pro: [2], withReplay: false);

        await handler.CollectNextAsync(CancellationToken.None);

        Assert.Equal(BaselineMatchStatus.NoReplay, Assert.Single(repository.Saved).Status);
        Assert.Empty(parser.Parsed);
    }

    [Fact]
    public async Task ParserBusy_SavesNothing_SoTheMatchIsTriedAgain()
    {
        var repository = new FakeBaseline();
        var handler = CreateHandler(repository, new FakeParser { Throw = new ReplayParserBusyException("busy") }, pro: [2]);

        Assert.Equal(CollectResult.SourceUnavailable, await handler.CollectNextAsync(CancellationToken.None));
        Assert.Empty(repository.Saved);
    }

    [Fact]
    public async Task TheDailyLimit_StopsTheCollection()
    {
        var handler = CreateHandler(new FakeBaseline { CollectedToday = 300 }, new FakeParser(), pro: [2]);

        Assert.Equal(CollectResult.LimitReached, await handler.CollectNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task NothingNew_WhenEveryCandidateIsKnown()
    {
        var handler = CreateHandler(new FakeBaseline { Known = { 1, 3 } }, new FakeParser(), pro: [1], highRank: [3]);

        Assert.Equal(CollectResult.NothingNew, await handler.CollectNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task HeroesWithFewMatches_HighRankComesFirst_TheMatchOfTheHeroWithTheFewest()
    {
        // The focus account played heroes 31, 5 and 2 recently. The base has 10 of 31 (enough), 4 of 5, 1 of 2.
        var repository = new FakeBaseline { Known = { 1 }, HighRankCounts = { [31] = 10, [5] = 4, [2] = 1 } };
        var source = new FakeSource([1, 2], [])
        {
            HighRankHeroes = { [10] = [31, 7], [11] = [5, 8], [12] = [2, 9], [13] = [31, 2] },
        };
        var handler = CreateHandler(repository, new FakeParser(), source, recentHeroes: [31, 5, 2]);

        await handler.CollectNextAsync(CancellationToken.None);

        // Only the needy heroes are asked for, and match 12 (hero 2, 1 match) beats 11 (hero 5, 4) and the newer 10.
        Assert.Equal(new HashSet<int> { 5, 2 }, source.AskedHeroes[BaselineSource.HighRank]);
        var saved = Assert.Single(repository.Saved);
        Assert.Equal((12L, BaselineSource.HighRank), (saved.MatchId.Value, saved.Source));
    }

    [Fact]
    public async Task EveryHeroHasEnoughMatches_ProMatchesFirstAgain()
    {
        var repository = new FakeBaseline { HighRankCounts = { [31] = 10 } };
        var source = new FakeSource([2], []) { HighRankHeroes = { [10] = [31] } };
        var handler = CreateHandler(repository, new FakeParser(), source, recentHeroes: [31]);

        await handler.CollectNextAsync(CancellationToken.None);

        Assert.Equal(BaselineSource.Pro, Assert.Single(repository.Saved).Source);
    }

    private static CollectBaselineHandler CreateHandler(
        FakeBaseline repository, FakeParser parser, long[] pro, long[]? highRank = null, bool withReplay = true,
        IItemCatalog? itemCatalog = null) =>
        CreateHandler(repository, parser, new FakeSource(pro, highRank ?? []), withReplay: withReplay, itemCatalog: itemCatalog);

    private static CollectBaselineHandler CreateHandler(
        FakeBaseline repository, FakeParser parser, FakeSource source, int[]? recentHeroes = null, bool withReplay = true,
        IItemCatalog? itemCatalog = null) =>
        new(
            source,
            repository,
            new FakeAnalyzer(withReplay ? ReplayUrl : null),
            parser,
            new FakeMatches(recentHeroes ?? []),
            itemCatalog ?? new FakeItemCatalog(),
            new BaselineCollectorSettings(300, recentHeroes is null ? [] : [FocusAccount]),
            new FixedTimeProvider(Now),
            NullLogger<CollectBaselineHandler>.Instance);

    private sealed class FakeSource(long[] pro, long[] highRank) : IBaselineMatchSource
    {
        // High-rank matches with their heroes; the source returns those with a hero that was asked for.
        public Dictionary<long, int[]> HighRankHeroes { get; } = [];

        public Dictionary<BaselineSource, IReadOnlySet<int>> AskedHeroes { get; } = [];

        public Task<IReadOnlyList<BaselineCandidate>> GetCandidatesAsync(
            BaselineSource source, IReadOnlySet<int> focusHeroes, CancellationToken cancellationToken)
        {
            AskedHeroes[source] = focusHeroes;
            var candidates = source == BaselineSource.Pro
                ? pro.Select(id => new BaselineCandidate(ReplayBuilder.MatchIdOf(id), source))
                : highRank.Select(id => new BaselineCandidate(ReplayBuilder.MatchIdOf(id), source))
                    .Concat(HighRankHeroes
                        .Where(entry => entry.Value.Any(focusHeroes.Contains))
                        .Select(entry => new BaselineCandidate(ReplayBuilder.MatchIdOf(entry.Key), source, entry.Value)));
            return Task.FromResult<IReadOnlyList<BaselineCandidate>>(candidates.ToList());
        }
    }

    private sealed class FakeBaseline : IBaselineRepository
    {
        public HashSet<long> Known { get; } = [];

        public int CollectedToday { get; init; }

        public List<BaselineMatch> Saved { get; } = [];

        public Task<IReadOnlySet<long>> FindKnownAsync(IReadOnlyCollection<MatchId> matchIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<long>>(Known);

        public Task SaveAsync(BaselineMatch match, CancellationToken cancellationToken)
        {
            Saved.Add(match);
            return Task.CompletedTask;
        }

        public Task<int> CountCollectedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult(CollectedToday);

        public Dictionary<int, int> HighRankCounts { get; } = [];

        public Task<IReadOnlyDictionary<int, int>> CountMatchesByHeroAsync(
            BaselineSource source, DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<int, int>>(
                source == BaselineSource.HighRank ? HighRankCounts : new Dictionary<int, int>());

        public Task<IReadOnlyList<(int HeroId, int Role, BaselineSource Source)>> GetGroupsAsync(
            DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<(int, int, BaselineSource)>>([]);

        public Task<IReadOnlyList<BaselineSample>> GetSamplesAsync(
            int heroId, int role, BaselineSource source, DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BaselineSample>>([]);
    }

    private sealed class FakeAnalyzer(Uri? replayUrl) : IReplayAnalyzer
    {
        public Task<MatchDetails?> GetMatchDetailsAsync(MatchId matchId, CancellationToken cancellationToken) =>
            Task.FromResult<MatchDetails?>(new MatchDetails(
                matchId, TimeSpan.FromMinutes(35), Now.AddHours(-2), Team.Radiant, 30, 20, IsParsed: false, [])
            {
                ReplayUrl = replayUrl,
            });
    }

    private sealed class FakeParser : IReplayParser
    {
        public Exception? Throw { get; init; }

        public IReadOnlyList<ItemPurchase> Purchases { get; init; } = [];

        public List<long> Parsed { get; } = [];

        public Task<ReplayAnalysis> ParseAsync(MatchId matchId, Uri replayUrl, CancellationToken cancellationToken)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            Parsed.Add(matchId.Value);
            var player = ReplayBuilder.Hero(31, Team.Radiant, Lane.Bottom);
            return Task.FromResult(new ReplayAnalysis(
                matchId, ReplayAnalysis.LaningPhaseEndsAtSeconds, [player], ReplayBuilder.Timeline([player]),
                deaths: [], wards: [], damage: [], purchases: Purchases, objectives: [],
                metrics: [new HeroMetrics(31, Team.Radiant, 5, 1, 0, 0, 0, 0, 2000, null, 10, 1, 0, 0, null, 0, 0, RoleInferred: true)]));
        }
    }

    private sealed class FakeItemCatalog(params Item[] items) : IItemCatalog
    {
        public Task<IReadOnlyDictionary<int, Item>> GetItemsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<int, Item>>(items.ToDictionary(item => item.Id));
    }

    private static AccountId FocusAccount => AccountId.TryCreate(99517715, out var account) ? account : default;

    // The focus account's recent matches, one per hero.
    private sealed class FakeMatches(int[] heroes) : IMatchProvider
    {
        public Task<IReadOnlyList<PlayerMatch>> GetRecentMatchesAsync(AccountId accountId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlayerMatch>>(heroes
                .Select((hero, index) => new PlayerMatch(
                    index + 1, hero, Team.Radiant, Team.Radiant, new Kda(1, 1, 1), TimeSpan.FromMinutes(30), Now.AddDays(-1)))
                .ToList());
    }
}
