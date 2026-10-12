using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class ReplayAvailabilityTests
{
    private static readonly MatchDetails Match = new(
        ReplayBuilder.MatchIdOf(10),
        Duration: TimeSpan.FromMinutes(40),
        StartTime: new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero),
        WinningTeam: Team.Radiant,
        RadiantScore: 30,
        DireScore: 20,
        IsParsed: false,
        Players: []);

    [Fact]
    public void EndedAt_IsStartPlusDuration()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 12, 40, 0, TimeSpan.Zero), Match.EndedAt);
    }

    [Theory]
    [InlineData(-5, false)] // clocks disagree: certainly not expired
    [InlineData(0, false)]
    [InlineData(24 * 60 - 1, false)] // may not be uploaded yet
    [InlineData(24 * 60, true)]
    [InlineData(30 * 24 * 60, true)]
    public void IsExpired_OnlyWhenMissingLongEnoughAfterTheMatch(int missingMinutesAfterTheEnd, bool expected)
    {
        var missingAt = Match.EndedAt.AddMinutes(missingMinutesAfterTheEnd);

        Assert.Equal(expected, ReplayAvailability.IsExpired(Match, missingAt));
    }
}
