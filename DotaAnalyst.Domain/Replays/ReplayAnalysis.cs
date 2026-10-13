using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Replays;

// What we read from a match replay ourselves: the laning phase of every hero and how net worth and experience
// changed during the whole match. It complements OpenDota (which has the percentiles); it never replaces it.
public record ReplayAnalysis
{
    // The laning phase ends at 10:00 of the game clock (pauses left out).
    public const int LaningPhaseEndsAtSeconds = 600;

    public ReplayAnalysis(
        MatchId matchId,
        int endsAtSeconds,
        IReadOnlyList<LaningStats> players,
        MatchTimeline? timeline = null,
        IReadOnlyList<HeroDeath>? deaths = null,
        IReadOnlyList<WardPlacement>? wards = null,
        IReadOnlyList<DamageRow>? damage = null,
        IReadOnlyList<ItemPurchase>? purchases = null,
        IReadOnlyList<MapObjective>? objectives = null,
        IReadOnlyList<HeroMetrics>? metrics = null,
        IReadOnlyList<HeroHeatmap>? heatmaps = null,
        IReadOnlyList<AbilityUpgrade>? abilityUpgrades = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(matchId.Value, nameof(matchId));
        ArgumentOutOfRangeException.ThrowIfNegative(endsAtSeconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(endsAtSeconds, LaningPhaseEndsAtSeconds);
        ArgumentNullException.ThrowIfNull(players);
        if (players.Any(player => player is null))
        {
            throw new ArgumentException("A replay analysis can't have an empty player.", nameof(players));
        }

        // Players are matched by hero ID, so it must be unique.
        if (players.DistinctBy(player => player.HeroId).Count() != players.Count)
        {
            throw new ArgumentException("Each hero can appear only once in a match.", nameof(players));
        }

        MatchId = matchId;
        EndsAtSeconds = endsAtSeconds;
        // A copy, so the caller can't change the list afterwards.
        Players = players.ToList();
        Timeline = timeline;
        if (deaths is not null && deaths.Any(death => death is null))
        {
            throw new ArgumentException("A replay analysis can't have an empty death.", nameof(deaths));
        }

        Deaths = deaths?.OrderBy(death => death.Seconds).ToList();
        if (wards is not null && wards.Any(ward => ward is null))
        {
            throw new ArgumentException("A replay analysis can't have an empty ward.", nameof(wards));
        }

        Wards = wards?.OrderBy(ward => ward.PlacedAt).ToList();
        if (damage is not null && damage.Any(row => row is null))
        {
            throw new ArgumentException("A replay analysis can't have an empty damage row.", nameof(damage));
        }

        Damage = damage?.ToList();
        if (purchases is not null && purchases.Any(purchase => purchase is null))
        {
            throw new ArgumentException("A replay analysis can't have an empty purchase.", nameof(purchases));
        }

        // OrderBy is stable: purchases in the same second keep the replay's order.
        Purchases = purchases?.OrderBy(purchase => purchase.Seconds).ToList();
        if (objectives is not null && objectives.Any(objective => objective is null))
        {
            throw new ArgumentException("A replay analysis can't have an empty objective.", nameof(objectives));
        }

        Objectives = objectives?.OrderBy(objective => objective.Seconds).ToList();
        if (metrics is not null && metrics.Any(hero => hero is null))
        {
            throw new ArgumentException("A replay analysis can't have empty metrics.", nameof(metrics));
        }

        Metrics = metrics?.ToList();
        if (heatmaps is not null && (heatmaps.Any(heatmap => heatmap is null)
            || heatmaps.DistinctBy(heatmap => heatmap.HeroId).Count() != heatmaps.Count))
        {
            throw new ArgumentException("A replay analysis has an empty or repeated heatmap.", nameof(heatmaps));
        }

        Heatmaps = heatmaps?.ToList();
        if (abilityUpgrades is not null && abilityUpgrades.Any(upgrade => upgrade is null))
        {
            throw new ArgumentException("A replay analysis can't have an empty ability upgrade.", nameof(abilityUpgrades));
        }

        AbilityUpgrades = abilityUpgrades?.OrderBy(upgrade => upgrade.Seconds).ToList();
    }

    // Where each hero spent its time alive (every match with positions, Turbo included); null for analyses made
    // before heatmaps existed.
    public IReadOnlyList<HeroHeatmap>? Heatmaps { get; }

    // Every ability level-up from the replay, in time order; null for analyses saved before skill upgrades were read.
    public IReadOnlyList<AbilityUpgrade>? AbilityUpgrades { get; }

    // The metrics of every hero (MatchMetricsCalculator); empty for a match without role queue (Turbo, unranked: no
    // metrics, by choice), null for analyses made before metrics existed.
    public IReadOnlyList<HeroMetrics>? Metrics { get; }

    // Buildings that fell and Roshan/Tormentor kills, in time order; null for analyses made before they were read.
    public IReadOnlyList<MapObjective>? Objectives { get; }

    // Every item purchase of every hero, in time order; null for analyses made before purchases were read.
    public IReadOnlyList<ItemPurchase>? Purchases { get; }

    // Damage of the whole match with a hero on at least one side; null for analyses made before it was read.
    public IReadOnlyList<DamageRow>? Damage { get; }

    // Every ward of both teams, in the order they were placed; null for analyses made before wards were read.
    public IReadOnlyList<WardPlacement>? Wards { get; }

    // Null for analyses made before the timeline existed (they only have the laning phase).
    public MatchTimeline? Timeline { get; }

    // Every hero death of the match, in time order; null for analyses made before deaths were read.
    public IReadOnlyList<HeroDeath>? Deaths { get; }

    // Made before everything the analysis page shows was read from the replay: worth parsing again.
    // (Analyses without damage were also read before deaths had assists.)
    public bool IsOutdated =>
        Timeline is null || Deaths is null || Wards is null || Damage is null || Purchases is null || Objectives is null
        || Heatmaps is null || Heatmaps.Any(heatmap => heatmap.Proximity is null) || AbilityUpgrades is null
        || Metrics is null || Metrics.Any(hero =>
            hero.TimeByRegion is null || hero.DeathsByRegion is null || hero.MovementSampleSegments is null
            || hero.TimeByRegionByPhase is null || hero.DeathsByRegionByPhase is null || hero.FightsNearby is null || hero.LaneDamageTaken is null || hero.TeamObserverUptime is null || hero.MinutesByRegion is null);

    public MatchId MatchId { get; }

    // Game clock at which the numbers were taken: LaningPhaseEndsAtSeconds, or less when the match ended earlier.
    public int EndsAtSeconds { get; }

    public IReadOnlyList<LaningStats> Players { get; }

    public LaningStats? FindHero(int heroId) => Players.FirstOrDefault(player => player.HeroId == heroId);
}
