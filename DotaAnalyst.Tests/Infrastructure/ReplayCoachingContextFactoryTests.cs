using System.Text.Json;
using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.Coaching;

namespace DotaAnalyst.Tests.Infrastructure;

public class ReplayCoachingContextFactoryTests
{
    private static readonly CoachingPatch Patch = new("7.41", "test", null, false);

    [Fact]
    public void ReplayAdapterProducesEpisodesWithoutPersistingRawFieldsInTheContext()
    {
        MatchId.TryCreate(9034320024, out var matchId);
        var document = ReplayParserClientTests.MatchJson().Replace("\"kills\": []", """
            "kills": [{"t":330,"victim":0,"killer":5,"killerUnit":"npc_dota_hero_slardar",
                "x":0,"y":0,"reincarnated":true,"assists":[]}]
            """);
        var context = ReplayCoachingContextFactory.Create(document, matchId, 31, Patch);
        Assert.Equal(4, context.SchemaVersion);
        Assert.Equal(Patch, context.Patch);
        var death = Assert.Single(context.Episodes!);
        Assert.True(death.Reincarnated);
        Assert.Null(death.IsolatedDeath);
        Assert.Equal(new CoachingEconomyInterval(300, 360, 200, 1), death.Economy);
        Assert.DoesNotContain("\"positions\"", JsonSerializer.Serialize(context));
        Assert.DoesNotContain("\"name\"", JsonSerializer.Serialize(context));
    }

    [Fact]
    public void WrongMatchCannotReuseAnotherReplaysEpisodes()
    {
        MatchId.TryCreate(1, out var wrong);
        Assert.Throws<InvalidDataException>(() => ReplayCoachingContextFactory.Create(
            ReplayParserClientTests.MatchJson(), wrong, 31, Patch));
    }

    [Fact]
    public void SharedPositionAdapterStillRejectsUnknownPlayers()
    {
        MatchId.TryCreate(9034320024, out var matchId);
        var document = ReplayParserClientTests.MatchJson().Replace("\"positions\": []",
            "\"positions\": [{\"player\":99,\"samples\":[[330,0,0]]}]");
        Assert.Throws<InvalidDataException>(() => ReplayCoachingContextFactory.Create(document, matchId, 31, Patch));
    }
}
