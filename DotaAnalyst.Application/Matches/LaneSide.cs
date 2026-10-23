using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>Which side of the player's lane a hero was on.</summary>
public enum LaneSide
{
    Player,
    Ally,
    Opponent,
}
