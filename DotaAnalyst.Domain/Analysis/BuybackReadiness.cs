using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

public static class BuybackReadiness
{
    // A replicated running cooldown disproves readiness. No final engine price/eligibility is captured, so never
    // certify readiness by comparing a stale minute net worth with an assumed formula or a raw custom override.
    public static bool? At(int heroId, int seconds, IReadOnlyList<EconomyObservation>? states)
    {
        var state = states?.FirstOrDefault(s => s.HeroId == heroId && s.Seconds == seconds);
        return state?.CooldownRemaining is > 0 ? false : null;
    }
}
