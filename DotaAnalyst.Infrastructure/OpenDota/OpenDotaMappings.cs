using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Infrastructure.OpenDota;

// Translates OpenDota's format into the domain model.
public static class OpenDotaMappings
{
    public static PlayerMatch ToPlayerMatch(this OpenDotaRecentMatch match) =>
        new(
            MatchId: match.MatchId,
            HeroId: match.HeroId,
            Team: PlayerSlot.ToTeam(match.PlayerSlot),
            WinningTeam: match.RadiantWin switch
            {
                true => Team.Radiant,
                false => Team.Dire,
                null => null,
            },
            Kda: new Kda(match.Kills, match.Deaths, match.Assists),
            Duration: TimeSpan.FromSeconds(match.Duration),
            StartTime: DateTimeOffset.FromUnixTimeSeconds(match.StartTime));
}
