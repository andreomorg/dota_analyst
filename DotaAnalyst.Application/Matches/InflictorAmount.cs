using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>An amount for one ability or item.</summary>
/// <param name="Inflictor">Internal name of the ability or item (lich_frost_nova, item_dagon_5); null for basic attacks.</param>
/// <param name="Amount">The amount.</param>
/// <param name="ItemId">The item's ID when the inflictor is an item the catalog knows (for its icon); null otherwise.</param>
/// <param name="ItemName">The item's name in the catalog; null when it isn't an item the catalog knows.</param>
public record InflictorAmount(string? Inflictor, int Amount, int? ItemId = null, string? ItemName = null);
