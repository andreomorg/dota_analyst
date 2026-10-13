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

    private static OpenDotaBaselineMatchSource CreateSource(string body) =>
        new(new OpenDotaClient(new HttpClient(new Stub(body)) { BaseAddress = new Uri("https://api.opendota.com/api/") }),
            new FixedTimeProvider(Now));

    private sealed class Stub(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
