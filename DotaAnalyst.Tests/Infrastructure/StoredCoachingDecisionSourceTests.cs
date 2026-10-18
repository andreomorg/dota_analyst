using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.Coaching;
using DotaAnalyst.Infrastructure.ReplayParser;

namespace DotaAnalyst.Tests.Infrastructure;

public class StoredCoachingDecisionSourceTests
{
    [Fact]
    public async Task MissingReplayAndUnknownHeroDoNotReadMetadata()
    {
        MatchId.TryCreate(9034320024, out var id);
        var matches = new Matches();
        Assert.Null(await new StoredCoachingDecisionSource(new Archive(null), matches).FindAsync(id, 31, CancellationToken.None));
        Assert.Null(await new StoredCoachingDecisionSource(new Archive(ReplayParserClientTests.MatchJson()), matches)
            .FindAsync(id, 999999, CancellationToken.None));
        Assert.Equal(0, matches.Reads);
    }

    [Fact]
    public async Task StoredReplayProducesBoundedContextWithoutFetchingOrWritingAnything()
    {
        MatchId.TryCreate(9034320024, out var id);
        var matches = new Matches();
        var context = await new StoredCoachingDecisionSource(new Archive(ReplayParserClientTests.MatchJson()), matches)
            .FindAsync(id, 31, CancellationToken.None);
        Assert.NotNull(context);
        Assert.Equal(id.ToString(), context.MatchId);
        Assert.Equal(31, context.HeroId);
        Assert.InRange(context.Scenes.Count, 0, 3);
        Assert.Null(context.Patch.MatchPatch);
        Assert.False(context.Patch.MechanicsVerified);
        Assert.Equal(1, matches.Reads);
    }

    private sealed class Archive(string? document) : IReplayDocumentArchive
    {
        public Task<string?> FindAsync(MatchId matchId, CancellationToken cancellationToken) => Task.FromResult(document);
        public Task KeepAsync(MatchId matchId, int schemaVersion, string value, CancellationToken cancellationToken)
            => throw new InvalidOperationException("No writes expected.");
    }

    private sealed class Matches : IMatchRepository
    {
        public int Reads { get; private set; }
        public Task<SavedMatch?> FindAsync(MatchId matchId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<SavedMatch?>(null);
        }
        public Task SaveAsync(MatchDetails match, DateTimeOffset savedAt, CancellationToken cancellationToken)
            => throw new InvalidOperationException("No writes expected.");
    }
}
