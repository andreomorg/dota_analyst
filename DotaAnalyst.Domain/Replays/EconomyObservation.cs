using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Replays;

// No final price is replicated here. CustomCost is raw, not proof that an override is enabled.
public sealed record EconomyObservation(int HeroId, int Seconds, int? NetWorth, int? Reliable, int? Unreliable,
    double? CooldownRemaining, int? CustomCost);
public sealed record GoldLossObservation(int HeroId, double Seconds, int Before, int After,
    int? BeforeUnreliable, int? AfterUnreliable, double PreviousAt)
{
    public int Lost => After - Before;
}
public sealed record FarmResource(string Id, string Name, int RawTeam, int X, int Y, int Health,
    int? BaseBountyMin, int? BaseBountyMax, bool? SeenRadiant, bool? SeenDire)
{
    public bool? SeenBy(Team team) => team == Team.Radiant ? SeenRadiant : SeenDire;
}
public sealed record FarmResourceSnapshot(int Seconds, IReadOnlyList<FarmResource> Units);
