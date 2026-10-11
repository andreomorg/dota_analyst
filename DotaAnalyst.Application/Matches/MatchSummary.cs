namespace DotaAnalyst.Application.Matches;

// What the API returns for each match (shape of the JSON sent to the React client).
public record MatchSummary(
    long MatchId,
    int HeroId,
    string HeroName,
    int Kills,
    int Deaths,
    int Assists,
    int DurationSeconds,
    DateTimeOffset StartTime,
    bool IsRadiant,
    bool? Won);
