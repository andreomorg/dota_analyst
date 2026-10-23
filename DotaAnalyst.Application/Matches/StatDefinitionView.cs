using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>One statistic of the catalog.</summary>
/// <param name="Key">Its name, as used in the heroes' series.</param>
/// <param name="Source">replayMinutes: read from the replay each minute; replayEvents: counted from events of the replay (wards...).</param>
/// <param name="Kind">total: adds up during the match (the difference between two points is the count in the period); state: the value at that moment (net worth, level).</param>
/// <param name="Unit">What the number counts.</param>
/// <param name="Better">Whether more (higher) or less (lower) is better; neither when it depends on the hero.</param>
public record StatDefinitionView(StatKey Key, StatSource Source, StatKind Kind, StatUnit Unit, BetterWhen Better);
