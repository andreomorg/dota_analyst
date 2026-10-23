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
    internal static OpenDotaPlayerExtras ToSupplemental(SupplementalPlayerStats stats, IReadOnlyDictionary<int, Item> items)
    {
        IReadOnlyList<ExternalStatTotal>? Rows(IReadOnlyDictionary<string, int>? values) => values?
            .OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                var key = pair.Key.StartsWith("item_", StringComparison.Ordinal) ? pair.Key[5..] : pair.Key;
                var item = items.Values.FirstOrDefault(candidate => candidate.Details?.Key == key);
                return new ExternalStatTotal(pair.Key, item?.Name ?? pair.Key, pair.Value, item?.Id);
            }).ToList();
        return new("opendota", Rows(stats.DamageReceived), Rows(stats.AbilityUses), Rows(stats.ItemUses),
            stats.ActionsPerMinute, Rows(stats.Actions), Rows(stats.GoldSources), Rows(stats.ExperienceSources));
    }

    internal static MatchStats ToStats(MatchStatTimeline stats, MatchDetails match, MatchPlayer? player, NameLookup names) =>
        new(
            StatCatalog.All
                .Select(stat => new StatDefinitionView(stat.Key, stat.Source, stat.Kind, stat.Unit, stat.Better))
                .ToList(),
            stats.Seconds,
            stats.Heroes
                .Where(hero => match.Players.Any(candidate => candidate.HeroId == hero.HeroId))
                .OrderBy(hero => hero.Team == Team.Radiant ? 0 : 1)
                .Select(hero => new HeroStats(
                    hero.HeroId,
                    names.Hero(hero.HeroId),
                    hero.Team,
                    player is not null && hero.HeroId == player.HeroId,
                    // In the catalog's order, so every hero lists them the same way.
                    StatCatalog.All
                        .Where(stat => hero.Series.ContainsKey(stat.Key))
                        .Select(stat => new StatSeries(stat.Key, hero.Series[stat.Key]))
                        .ToList()))
                .ToList());
}
