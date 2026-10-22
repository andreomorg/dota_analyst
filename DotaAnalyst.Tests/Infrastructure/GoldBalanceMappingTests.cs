using System.Text.Json;
using DotaAnalyst.Infrastructure.Coaching;
using DotaAnalyst.Infrastructure.ReplayParser;

namespace DotaAnalyst.Tests.Infrastructure;

public class GoldBalanceMappingTests
{
    private static DotaAnalyst.Domain.Replays.ReplayAdditionalFacts Read(string family)
    {
        var response = JsonSerializer.Deserialize<ReplayParserMatchResponse>($$"""
            { "players": [{ "player":0,"heroId":1 }], "timelines": { {{family}} } }
            """, CoachingJson.Options)!;
        return ReplayParserMappings.ReadFacts(response);
    }

    [Fact]
    public void AbsentEmptyAndPartialComponentsRemainDistinct()
    {
        Assert.Null(Read("").GoldBalances);
        Assert.Empty(Read("\"goldBalances\":[]").GoldBalances!);
        var facts = Read("\"goldBalances\":[[2,0,30,null],[0,0,40,0]]");
        Assert.Empty(facts.InvalidFamilies);
        Assert.Equal(0, facts.GoldBalances![0].Seconds);
        Assert.Equal(40, facts.GoldBalances[0].Total);
        Assert.Equal(1, facts.GoldBalances[1].HeroId);
        Assert.Null(facts.GoldBalances[1].Total);
        Assert.Equal(30, facts.GoldBalances[1].Reliable);
    }

    [Theory]
    [InlineData("[[0,0,-1,0]]")]
    [InlineData("[[0,0,0,-1]]")]
    [InlineData("[[-1,0,0,0]]")]
    [InlineData("[[0,0,0]]")]
    [InlineData("[[0,9,0,0]]")]
    [InlineData("[[0,0,0,0],[0,0,1,0]]")]
    [InlineData("{}")]
    public void MalformedBalancesAreUnavailableInsteadOfZero(string value)
    {
        var facts = Read("\"goldBalances\":" + value);
        Assert.Null(facts.GoldBalances);
        Assert.Contains("goldBalances", facts.InvalidFamilies);
    }
}
