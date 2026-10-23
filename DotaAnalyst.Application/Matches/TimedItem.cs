using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>An item of the build and when it was ready.</summary>
/// <param name="ItemId">Item.</param>
/// <param name="Name">Item name.</param>
/// <param name="Seconds">Game clock when it got into the inventory (for an assembled item, when its parts came together).</param>
/// <param name="Assembled">Assembled from parts (true) or bought whole (false).</param>
public record TimedItem(int ItemId, string Name, int Seconds, bool Assembled);
