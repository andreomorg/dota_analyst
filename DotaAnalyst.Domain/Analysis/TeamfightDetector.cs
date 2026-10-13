using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// A teamfight: deaths close together in time.
public record Teamfight(
    int StartSeconds,
    int EndSeconds,
    // Deaths of each team in the fight (Aegis deaths included: the hero still lost the fight).
    int RadiantDeaths,
    int DireDeaths,
    // The team with fewer deaths; null on a tie.
    Team? Winner,
    IReadOnlyList<HeroDeath> Deaths);

// Domain service: finds the teamfights of a match from its hero deaths, the way OpenDota does — deaths at most
// MaxGapSeconds apart belong to the same fight, and a fight needs at least MinDeaths deaths (fewer is a pick-off).
public static class TeamfightDetector
{
    public const int MaxGapSeconds = 15;
    public const int MinDeaths = 3;

    // teamOf tells each hero's team (deaths only know the hero).
    public static IReadOnlyList<Teamfight> Detect(IReadOnlyList<HeroDeath> deaths, IReadOnlyDictionary<int, Team> teamOf)
    {
        var fights = new List<Teamfight>();
        var current = new List<HeroDeath>();

        void Close()
        {
            if (current.Count >= MinDeaths)
            {
                var radiant = current.Count(death => teamOf.GetValueOrDefault(death.HeroId) == Team.Radiant);
                var dire = current.Count(death => teamOf.GetValueOrDefault(death.HeroId) == Team.Dire);
                fights.Add(new Teamfight(
                    current[0].Seconds,
                    current[^1].Seconds,
                    radiant,
                    dire,
                    radiant == dire ? null : radiant < dire ? Team.Radiant : Team.Dire,
                    current.ToList()));
            }

            current.Clear();
        }

        foreach (var death in deaths.OrderBy(death => death.Seconds))
        {
            if (current.Count > 0 && death.Seconds - current[^1].Seconds > MaxGapSeconds)
            {
                Close();
            }

            current.Add(death);
        }

        Close();
        return fights;
    }
}
