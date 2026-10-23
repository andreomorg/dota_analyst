using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Situations;

// One example for the "where do good players go" model: a living hero's situation and where it was HorizonSeconds later
// (null = dead then). The label is what the hero did, not what it should have done: trained on high-level players only,
// it says what they did in situations like this.
public sealed record NextRegionSample(HeroSituation Situation, MapRegion? RegionLater);

// Domain service: the next-region examples of one match, every SampleEverySeconds of the game clock from 0:00, for each
// living hero. The label is the only thing that looks ahead: the position HorizonSeconds later.
public static class NextRegionSampleBuilder
{
    public const int SampleEverySeconds = 10;
    public const int HorizonSeconds = 30;

    // The label may use the sample just around the target second (it is the future anyway): positions come every 2 s.
    private const int LabelTolerance = 1;

    // Null when the analysis lacks what a situation needs (HeroSituationBuilder.From).
    public static IReadOnlyList<NextRegionSample>? Build(
        ReplayAnalysis replay, IReadOnlyList<HeroPositions> positions, ReplayActivity? activity = null,
        GameCatalog? catalog = null, ReplayAdditionalFacts? facts = null, int featureVersion = 2)
    {
        if (HeroSituationBuilder.From(replay, positions, activity, catalog, facts, featureVersion) is not { } situations)
        {
            return null;
        }

        var positionsOf = positions.DistinctBy(hero => hero.HeroId).ToDictionary(hero => hero.HeroId);
        var heroIds = situations.HeroIds.Order().ToList();
        var samples = new List<NextRegionSample>();
        // The target second must be inside the match: after the end there is no "later".
        for (var second = 0; second + HorizonSeconds <= situations.MatchEndsAtSeconds; second += SampleEverySeconds)
        {
            foreach (var heroId in heroIds)
            {
                if (situations.At(heroId, second) is not { } situation)
                {
                    continue;
                }

                // No position then = dead then (a living hero always has a sample within a second).
                var later = positionsOf.GetValueOrDefault(heroId)?.At(second + HorizonSeconds, LabelTolerance);
                samples.Add(new NextRegionSample(
                    situation, later is { } at ? MapRegions.RegionOf(situation.Team, at.X, at.Y) : null));
            }
        }

        return samples;
    }
}
