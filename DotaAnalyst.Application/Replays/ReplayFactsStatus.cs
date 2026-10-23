using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Heroes;
using DotaAnalyst.Application.Models;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Replays;

public enum ReplayFactsStatus
{
    Found,

    // No kept document for the match (or no storage on this machine).
    NoDocument,

    // The request does not fit the match (hero not in it, a second after the end, a final query not at the end).
    Invalid,
}
