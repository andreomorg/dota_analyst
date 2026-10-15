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
        var timeline = replay.Timeline;
        var series = timeline?.FindHero(heroId);
        return episodes.Where(e => e.HeroId == heroId).Select(episode =>
        {
            var anchor = Math.Max(0, episode.EventSeconds - ReviewLeadSeconds);
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
            var facts = new CoachingDecisionFacts(anchor, observed, replay.Players.Count - observed.Count, economy,
                replay.Purchases?.Where(p => p.HeroId == heroId && p.Seconds >= anchor - PurchaseAndUpgradeWindowSeconds && p.Seconds <= anchor).ToArray(),
                replay.AbilityUpgrades?.Where(p => p.HeroId == heroId && p.Seconds >= anchor - PurchaseAndUpgradeWindowSeconds && p.Seconds <= anchor).ToArray(),
                replay.Objectives?.Where(o => o.Seconds >= Math.Max(0, anchor - ObjectiveWindowSeconds) && o.Seconds <= anchor).ToArray());
            return new CoachingDecisionScene(episode, facts);
        }).ToArray();
    }
}
