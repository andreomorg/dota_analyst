using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>The player's item build, read from the replay purchases.</summary>
/// <param name="StartingItems">Items bought before the horn, in purchase order.</param>
/// <param name="Items">Items of the build in the order they were ready: assembled items (also the ones that later became part of another, like Mekansm) and items of 600+ gold bought whole that never became a part; no consumables.</param>
public record PlayerItemBuild(IReadOnlyList<StartingItemCount> StartingItems, IReadOnlyList<TimedItem> Items);
