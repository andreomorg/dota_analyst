using System.Net;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Infrastructure.OpenDota;

namespace DotaAnalyst.Tests.Infrastructure;

public class OpenDotaBaselineMatchSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    // Started 3 h ago and lasted 40 min: its replay is up. Started 30 min ago: not yet.
    private static readonly long Old = Now.AddHours(-3).ToUnixTimeSeconds();
    private static readonly long Recent = Now.AddMinutes(-30).ToUnixTimeSeconds();

    [Fact]
    public async Task HighRank_OnlyRankedAllPick_WithRanks_Finished_AndAFocusHero()
    {
        var page = $$"""
            [
              { "match_id": 1, "start_time": {{Old}}, "duration": 2400, "lobby_type": 7, "game_mode": 22, "avg_rank_tier": 75, "num_rank_tier": 5, "radiant_team": [31, 2], "dire_team": [3] },
              { "match_id": 2, "start_time": {{Old}}, "duration": 2400, "lobby_type": 7, "game_mode": 23, "avg_rank_tier": 75, "num_rank_tier": 5, "radiant_team": [31], "dire_team": [] },
              { "match_id": 3, "start_time": {{Old}}, "duration": 2400, "lobby_type": 0, "game_mode": 22, "avg_rank_tier": 75, "num_rank_tier": 5, "radiant_team": [31], "dire_team": [] },
              { "match_id": 4, "start_time": {{Old}}, "duration": 2400, "lobby_type": 7, "game_mode": 22, "avg_rank_tier": 75, "num_rank_tier": 1, "radiant_team": [31], "dire_team": [] },
              { "match_id": 5, "start_time": {{Recent}}, "duration": 900, "lobby_type": 7, "game_mode": 22, "avg_rank_tier": 75, "num_rank_tier": 5, "radiant_team": [31], "dire_team": [] },
              { "match_id": 6, "start_time": {{Old}}, "duration": 2400, "lobby_type": 7, "game_mode": 22, "avg_rank_tier": 75, "num_rank_tier": 5, "radiant_team": [7], "dire_team": [8] }
            ]
            """;

        var candidates = await CreateSource(page).GetCandidatesAsync(BaselineSource.HighRank, new HashSet<int> { 31 }, CancellationToken.None);

        Assert.Equal([1L], candidates.Select(candidate => candidate.MatchId.Value));
        Assert.Equal([31, 2, 3], candidates[0].HeroIds);
    }

    [Fact]
    public async Task Pro_AllFinishedMatches_AndNoHighRankWithoutFocusHeroes()
    {
        var pros = $$"""[ { "match_id": 10, "start_time": {{Old}}, "duration": 2400 }, { "match_id": 11, "start_time": {{Recent}}, "duration": 600 } ]""";
        var source = CreateSource(pros);

        var candidates = await source.GetCandidatesAsync(BaselineSource.Pro, new HashSet<int>(), CancellationToken.None);

        Assert.Equal([10L], candidates.Select(candidate => candidate.MatchId.Value));
        Assert.Empty(await source.GetCandidatesAsync(BaselineSource.HighRank, new HashSet<int>(), CancellationToken.None));
    }

    [Fact]
    public async Task HighRank_AtBusyHours_JumpsBackToMatchesWhoseReplaysAreUp()
    {
        // The first page only has matches of the last 10 minutes (IDs 100000..100100, one ID per 6 s): none ready.
        // The source then asks for matches below the ID of about 110 minutes ago (1,100 IDs earlier), where they are.
        var newest = Now.ToUnixTimeSeconds();
        var tenMinutesAgo = Now.AddMinutes(-10).ToUnixTimeSeconds();
        var first = $$"""
            [ { "match_id": 100100, "start_time": {{newest}}, "duration": 60, "lobby_type": 7, "game_mode": 22, "num_rank_tier": 5, "radiant_team": [31], "dire_team": [] },
              { "match_id": 100000, "start_time": {{tenMinutesAgo}}, "duration": 60, "lobby_type": 7, "game_mode": 22, "num_rank_tier": 5, "radiant_team": [31], "dire_team": [] } ]
            """;
        var older = $$"""
            [ { "match_id": 98990, "start_time": {{Old}}, "duration": 2400, "lobby_type": 7, "game_mode": 22, "num_rank_tier": 5, "radiant_team": [31], "dire_team": [] } ]
            """;
        var openDota = new Pages(first, older);
        var source = new OpenDotaBaselineMatchSource(
            new OpenDotaClient(new HttpClient(openDota) { BaseAddress = new Uri("https://api.opendota.com/api/") }),
            new FixedTimeProvider(Now));

        var candidates = await source.GetCandidatesAsync(BaselineSource.HighRank, new HashSet<int> { 31 }, CancellationToken.None);

        Assert.Equal([98990L], candidates.Select(candidate => candidate.MatchId.Value));
        Assert.EndsWith("less_than_match_id=99000", openDota.Requests[1]);
    }
    [Fact]
    public void EstimateIdAt_FromThePagesRate()
    {
        // 100 IDs in 600 s; 30 minutes before the newest start → 300 IDs earlier.
        var page = new[]
        {
            new OpenDotaPublicMatch(10_000, 1_000_600, 60, 7, 22, 75, 5, [], []),
            new OpenDotaPublicMatch(9_900, 1_000_000, 60, 7, 22, 75, 5, [], []),
        };

        Assert.Equal(9_700, OpenDotaBaselineMatchSource.EstimateIdAt(page, DateTimeOffset.FromUnixTimeSeconds(1_000_600 - 1_800)));
        Assert.Null(OpenDotaBaselineMatchSource.EstimateIdAt(page[..1], DateTimeOffset.FromUnixTimeSeconds(0)));
    }

    // Answers the first request with the first page, every later one with the second; remembers the URLs.
    private sealed class Pages(string first, string later) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Requests.Count == 1 ? first : later) });
        }
    }

    private static OpenDotaBaselineMatchSource CreateSource(string body) =>
        new(new OpenDotaClient(new HttpClient(new Stub(body)) { BaseAddress = new Uri("https://api.opendota.com/api/") }),
            new FixedTimeProvider(Now));

    private sealed class Stub(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
