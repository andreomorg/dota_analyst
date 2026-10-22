using System.Text.Json;
using DotaAnalyst.Infrastructure.Coaching;
using DotaAnalyst.Infrastructure.ReplayParser;

namespace DotaAnalyst.Tests.Infrastructure;

public class BuybackCostMappingTests
{
    private static ReplayParserMatchResponse Read(string buybacks) => JsonSerializer.Deserialize<ReplayParserMatchResponse>($$"""
        { "players":[{"player":0,"heroId":1,"team":"radiant"}],
          "vision":{"smokes":[]},"items":{"uses":[]},"abilities":{"uses":[]},"buybacks":{{buybacks}} }
        """, CoachingJson.Options)!;

    [Fact]
    public void RecordedAndLegacyCostsStayDistinct()
    {
        var activity = ReplayParserMappings.ReadActivity(Read("[{\"t\":1.5,\"player\":0,\"cost\":637},{\"t\":2,\"player\":0}]"))!;
        Assert.Equal(637, activity.Buybacks![0].Cost);
        Assert.Null(activity.Buybacks[1].Cost);
    }
    [Fact]
    public void NegativeRecordedCostIsNotAccepted() => Assert.Throws<InvalidDataException>(() =>
        ReplayParserMappings.ReadActivity(Read("[{\"t\":1,\"player\":0,\"cost\":-1}]")));
}
