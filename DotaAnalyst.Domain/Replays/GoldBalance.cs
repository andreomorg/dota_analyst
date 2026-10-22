namespace DotaAnalyst.Domain.Replays;

// Point-in-time sampled player resource, including while dead. Null components were not observed.
// It is neither total earned gold nor net worth. Never extrapolate it through an unobserved gap.
public sealed record GoldBalance(int HeroId, int Seconds, int? Reliable, int? Unreliable)
{
    public long? Total => Reliable is { } reliable && Unreliable is { } unreliable ? (long)reliable + unreliable : null;
}
