using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Baseline;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Application.Matches;

internal static partial class MatchAnalysisMapper
{
    internal sealed class NameLookup(IReadOnlyDictionary<int, Hero> heroes, IReadOnlyDictionary<int, Item> items)
    {
        // Items by internal key (blink, dagon_5), built on first use: only the damage section needs it.
        private Dictionary<string, Item>? byKey;

        public string Hero(int heroId) => heroes.GetValueOrDefault(heroId)?.Name ?? GetMatchAnalysisHandler.UnknownHeroName;

        public string Item(int itemId) => items.GetValueOrDefault(itemId)?.Name ?? GetMatchAnalysisHandler.UnknownItemName;

        // The item behind a damage inflictor ("item_dagon_5"); null for abilities, basic attacks and items the catalog
        // doesn't know (or has no details for).
        public Item? ItemOfInflictor(string? inflictor)
        {
            const string Prefix = "item_";
            if (inflictor is null || !inflictor.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return null;
            }

            byKey ??= items.Values
                .Where(item => item.Details is not null)
                .DistinctBy(item => item.Details!.Key)
                .ToDictionary(item => item.Details!.Key);
            return byKey.GetValueOrDefault(inflictor[Prefix.Length..]);
        }
    }
}
