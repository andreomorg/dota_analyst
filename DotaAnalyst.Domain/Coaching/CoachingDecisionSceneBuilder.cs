using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Domain.Coaching;

public static class CoachingDecisionSceneBuilder
{
    public const int ReviewLeadSeconds = 10;
    public const int PositionMaxAgeSeconds = 2;
    public const int PurchaseAndUpgradeWindowSeconds = 90;
    public const int ObjectiveWindowSeconds = 60;

    public static IReadOnlyList<CoachingDecisionScene> Build(ReplayAnalysis replay, int heroId,
        IReadOnlyList<CoachingEpisode> episodes, IReadOnlyList<HeroPositions>? positions)
    {
        var byHero = (positions ?? []).DistinctBy(p => p.HeroId).ToDictionary(p => p.HeroId);
        return episodes.Where(e => e.HeroId == heroId).Select(episode =>
        {
            var anchor = Math.Max(0, episode.EventSeconds - ReviewLeadSeconds);
            return new CoachingDecisionScene(episode, ExtractFacts(replay, heroId, anchor, byHero));
        }).ToArray();
    }

    // Control windows have no retrospective episode; a recorded timeline bounds their anchors.
    public static CoachingDecisionFacts AtAnchor(ReplayAnalysis replay, int heroId, int anchorSeconds,
        IReadOnlyList<HeroPositions>? positions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(anchorSeconds);
        if (!replay.Players.Any(player => player.HeroId == heroId))
            throw new ArgumentException("Expected a hero present in the replay.", nameof(heroId));
        var timeline = replay.Timeline ?? throw new ArgumentException("Control anchors require a recorded timeline.", nameof(replay));
        if (anchorSeconds > timeline.Seconds[^1])
            throw new ArgumentOutOfRangeException(nameof(anchorSeconds), "Anchor exceeds recorded replay timeline.");
        var byHero = (positions ?? []).DistinctBy(p => p.HeroId).ToDictionary(p => p.HeroId);
        return ExtractFacts(replay, heroId, anchorSeconds, byHero);
    }

    private static CoachingDecisionFacts ExtractFacts(ReplayAnalysis replay, int heroId, int anchor,
        IReadOnlyDictionary<int, HeroPositions> byHero)
    {
        var timeline = replay.Timeline;
        var series = timeline?.FindHero(heroId);
        var observed = new List<CoachingPositionSnapshot>();
        foreach (var player in replay.Players)
        {
            // A future sample must never fill a missing earlier position, even within tolerance.
            var sample = byHero.GetValueOrDefault(player.HeroId)?.Samples
                .Where(p => p.Seconds >= Math.Max(0, anchor - PositionMaxAgeSeconds) && p.Seconds <= anchor)
                .Select(p => (PositionSample?)p).LastOrDefault();
            if (sample is { } p)
                observed.Add(new(player.HeroId, player.Team, p.Seconds, p.X, p.Y));
        }
        CoachingEconomySnapshot? economy = null;
        var index = timeline?.Seconds.Select((seconds, i) => (seconds, i))
            .Where(point => point.seconds <= anchor).Select(point => (int?)point.i).LastOrDefault();
        if (index is { } at && series is not null)
            economy = new(timeline!.Seconds[at], series.NetWorth[at],
                series.Series.TryGetValue(StatKey.LastHits, out var hits) ? hits[at] : null);
        return new CoachingDecisionFacts(anchor, observed, replay.Players.Count - observed.Count, economy,
            replay.Purchases?.Where(p => p.HeroId == heroId && p.Seconds >= anchor - PurchaseAndUpgradeWindowSeconds && p.Seconds <= anchor).ToArray(),
            replay.AbilityUpgrades?.Where(p => p.HeroId == heroId && p.Seconds >= anchor - PurchaseAndUpgradeWindowSeconds && p.Seconds <= anchor).ToArray(),
            replay.Objectives?.Where(o => o.Seconds >= Math.Max(0, anchor - ObjectiveWindowSeconds) && o.Seconds <= anchor).ToArray());
    }
}
