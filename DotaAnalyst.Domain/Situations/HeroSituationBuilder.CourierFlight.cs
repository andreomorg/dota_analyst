using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Situations;

public sealed partial class HeroSituationBuilder
{
    private SituationCourierFlight CourierFlightOf(Team team, int seconds) =>
        new(CouriersAliveFlying(team, seconds), CouriersAliveFlying(team == Team.Radiant ? Team.Dire : Team.Radiant, seconds));

    private bool? CouriersAliveFlying(Team owner, int seconds)
    {
        if (activity?.Couriers is not { } couriers)
        {
            return null;
        }

        var unknown = false;
        foreach (var courier in couriers.Where(courier => courier.Team == owner))
        {
            var alive = courier.AliveAt(seconds);
            // Sparse flight changes are states, not position samples: persist until the next change.
            var flying = courier.Flight?.Where(change => change.Seconds <= seconds)
                .MaxBy(change => change.Seconds)?.Flying;
            if (alive == true && flying == true)
            {
                return true;
            }

            if (alive != false && flying != false)
            {
                unknown = true;
            }
        }

        return unknown ? null : false;
    }
}
