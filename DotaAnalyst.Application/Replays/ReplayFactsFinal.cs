using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Heroes;
using DotaAnalyst.Application.Models;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Replays;

/// <summary>Whole-match facts of the hero, only in a final query: the replay's own scoreboard, its items at the end and its
/// healing over the match.</summary>
public sealed record ReplayFactsFinal(FinalHeroStats? Stats, FinalInventory? Items, HealingFinalFacts Healing);
