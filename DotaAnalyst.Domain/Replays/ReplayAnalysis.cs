using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Replays;

// What we read from a match replay ourselves: the laning phase of every hero and how net worth and experience
// changed during the whole match. It complements OpenDota (which has the percentiles); it never replaces it.
public record ReplayAnalysis
{
    // The laning phase ends at 10:00 of the game clock (pauses left out).
    public const int LaningPhaseEndsAtSeconds = 600;

    public ReplayAnalysis(
        MatchId matchId, int endsAtSeconds, IReadOnlyList<LaningStats> players, MatchTimeline? timeline = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(matchId.Value, nameof(matchId));
        ArgumentOutOfRangeException.ThrowIfNegative(endsAtSeconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(endsAtSeconds, LaningPhaseEndsAtSeconds);
        ArgumentNullException.ThrowIfNull(players);
        if (players.Any(player => player is null))
        {
            throw new ArgumentException("A replay analysis can't have an empty player.", nameof(players));
        }

        // Players are matched by hero ID, so it must be unique.
        if (players.DistinctBy(player => player.HeroId).Count() != players.Count)
        {
            throw new ArgumentException("Each hero can appear only once in a match.", nameof(players));
        }

        MatchId = matchId;
        EndsAtSeconds = endsAtSeconds;
        // A copy, so the caller can't change the list afterwards.
        Players = players.ToList();
        Timeline = timeline;
    }

    // Null for analyses made before the timeline existed (they only have the laning phase).
    public MatchTimeline? Timeline { get; }

    public MatchId MatchId { get; }

    // Game clock at which the numbers were taken: LaningPhaseEndsAtSeconds, or less when the match ended earlier.
    public int EndsAtSeconds { get; }

    public IReadOnlyList<LaningStats> Players { get; }

    public LaningStats? FindHero(int heroId) => Players.FirstOrDefault(player => player.HeroId == heroId);
}
