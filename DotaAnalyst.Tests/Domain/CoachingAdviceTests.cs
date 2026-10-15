using DotaAnalyst.Domain.Coaching;

namespace DotaAnalyst.Tests.Domain;

public class CoachingAdviceTests
{
    private static readonly CoachingContext Context = new(1, "1", 1, new(1, "replay", false),
        new(null, "unknown", null, false), 1200, "omniscient-retrospective",
        [new("e1", "Deaths", 3, "count", 0, 1200, "replay", "No causal verdict.")], []);

    [Fact]
    public void FabricatedEvidenceReferenceIsRejected()
    {
        var advice = new CoachingAdvice([new("Review", "Observation", "Review", "Practice", ["invented"])]);
        Assert.Contains(CoachingAdviceValidator.Validate(Context, advice), error => error.Contains("references"));
    }

    [Fact]
    public void AbstentionIsAccepted()
    {
        Assert.Empty(CoachingAdviceValidator.Validate(Context, new([])));
    }

    [Fact]
    public void MoreThanThreePrioritiesAreRejected()
    {
        var suggestion = new CoachingSuggestion("Review", "Observation", "Review", "Practice", ["e1"]);
        Assert.NotEmpty(CoachingAdviceValidator.Validate(Context, new(Enumerable.Repeat(suggestion, 4).ToArray())));
    }
}
