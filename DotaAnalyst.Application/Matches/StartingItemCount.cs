using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>A starting item and how many were bought.</summary>
/// <param name="ItemId">Item.</param>
/// <param name="Name">Item name.</param>
/// <param name="Count">How many were bought before the horn.</param>
public record StartingItemCount(int ItemId, string Name, int Count);
