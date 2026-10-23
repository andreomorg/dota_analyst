using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>One hero's net worth and experience at each point of the timeline.</summary>
/// <param name="HeroId">Hero played.</param>
/// <param name="HeroName">Hero name, or "Unknown hero".</param>
/// <param name="Side">player (the point of view), ally or opponent.</param>
/// <param name="NetWorth">Net worth at each point.</param>
/// <param name="Experience">Experience at each point.</param>
public record TimelineHero(
    int HeroId,
    string HeroName,
    LaneSide Side,
    IReadOnlyList<int> NetWorth,
    IReadOnlyList<int> Experience);
