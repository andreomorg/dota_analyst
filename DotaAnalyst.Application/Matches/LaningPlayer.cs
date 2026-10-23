using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>One hero's numbers at the end of the laning phase.</summary>
/// <param name="HeroId">Hero played.</param>
/// <param name="HeroName">Hero name, or "Unknown hero".</param>
/// <param name="LastHits">Last hits (creeps killed).</param>
/// <param name="Denies">Own creeps denied.</param>
/// <param name="NetWorth">Gold plus the value of the items.</param>
/// <param name="Experience">Experience points.</param>
/// <param name="Deaths">Deaths.</param>
public record LaningPlayer(
    int HeroId,
    string HeroName,
    int LastHits,
    int Denies,
    int NetWorth,
    int Experience,
    int Deaths);
