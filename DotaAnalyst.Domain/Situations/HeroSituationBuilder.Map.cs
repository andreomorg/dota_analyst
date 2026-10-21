using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Situations;

// The state of the map: Roshan and the Aegis, the towers lost per lane, day or night.
public sealed partial class HeroSituationBuilder
{
    // An Aegis lasts 5 minutes.
    public const int AegisSeconds = 300;

    private MapState MapAt(int heroId, Team team, int seconds)
    {
        var roshan = replay.Objectives!.Where(objective => objective.Kind == ObjectiveKind.Roshan && objective.Seconds <= seconds).ToList();
        // An Aegis is held for AegisSeconds after Roshan, unless its holder reincarnated with it since.
        var holder = roshan.LastOrDefault() is { AegisHeroId: { } aegisHero } last
            && seconds - last.Seconds < AegisSeconds
            && !replay.Deaths!.Any(death => death.HeroId == aegisHero && death.Reincarnated
                && death.Seconds >= last.Seconds && death.Seconds <= seconds)
                ? aegisHero
                : (int?)null;
        var holderTeam = holder is { } id ? teamOf.GetValueOrDefault(id) : (Team?)null;
        return new MapState(
            SecondsSinceRoshanKill: roshan.Count == 0 ? null : seconds - roshan[^1].Seconds,
            HasAegis: holder == heroId,
            AllyHasAegis: holder is not null && holder != heroId && holderTeam == team,
            EnemyHasAegis: holder is not null && holderTeam != team,
            OwnTowersLost: TowersLostByLane(team, seconds),
            EnemyTowersLost: TowersLostByLane(team == Team.Radiant ? Team.Dire : Team.Radiant, seconds),
            IsNight: IsNightAt(seconds));
    }

    // The last change at or before this second (the reading at 0:00 can still be the pre-game's: it lasts a second).
    private bool? IsNightAt(int seconds) =>
        activity?.DayNight is { } changes
            ? changes.LastOrDefault(change => change.Seconds <= seconds) is { } change ? !change.IsDay : null
            : null;

    private LaneTowers TowersLostByLane(Team owner, int seconds)
    {
        int Lost(Lane lane) => replay.Objectives!.Count(objective => objective.Kind == ObjectiveKind.Tower
            && objective.Owner == owner && objective.Lane == lane && objective.Seconds <= seconds);
        return new LaneTowers(Lost(Lane.Top), Lost(Lane.Middle), Lost(Lane.Bottom));
    }
}
