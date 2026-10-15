namespace DotaAnalyst.Domain.Coaching;

public enum CoachingEpisodeKind
{
    Death,
    DetectedFight
}

// Checks describe either allies at a death or seconds in a fight window, not unique replay rows.
// Missing samples never establish that a hero was far away or dead.
public sealed record CoachingPositionCoverage(int ExpectedChecks, int ObservedChecks)
{
    public bool IsComplete => ExpectedChecks > 0 && ObservedChecks == ExpectedChecks;
}

// Consecutive actual timeline points. The interval may straddle the event; net worth change is not farm.
public sealed record CoachingEconomyInterval(
    int FromSeconds, int ToSeconds, int NetWorthChange, int? LastHitsChange);

public sealed record CoachingEpisode(
    string Id, CoachingEpisodeKind Kind, int HeroId, int EventSeconds,
    int FromSeconds, int ToSeconds, CoachingPositionCoverage PositionCoverage,
    bool? IsolatedDeath, bool? NearFight, bool Reincarnated,
    CoachingEconomyInterval? Economy);
