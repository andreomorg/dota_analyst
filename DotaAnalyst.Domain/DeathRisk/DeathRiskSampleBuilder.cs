using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Situations;

namespace DotaAnalyst.Domain.DeathRisk;

// One example for the death-risk model: a living hero's situation and whether it died soon after.
// DiedAtSeconds is the death inside the horizon (null when it lived), kept to try other horizons later.
public sealed record DeathRiskSample(HeroSituation Situation, bool DiesSoon, int? DiedAtSeconds);

// Domain service: the death-risk examples of one match. Every SampleEverySeconds of the game clock from 0:00, each living
// hero gives one example; the label says whether it died in the next HorizonSeconds. The label is the only thing that
// looks ahead, and only inside that window.
public static class DeathRiskSampleBuilder
{
    // Neighbouring samples 2 s apart are nearly the same situation: they would add volume, not information.
    public const int SampleEverySeconds = 10;
    public const int HorizonSeconds = 15;

    // Null when the analysis lacks what a situation needs (HeroSituationBuilder.From).
    public static IReadOnlyList<DeathRiskSample>? Build(
        ReplayAnalysis replay, IReadOnlyList<HeroPositions> positions, ReplayActivity? activity = null,
        GameCatalog? catalog = null, ReplayAdditionalFacts? facts = null, int featureVersion = 2)
    {
        if (HeroSituationBuilder.From(replay, positions, activity, catalog, facts, featureVersion) is not { } situations)
        {
            return null;
        }

        var deathsOf = replay.Deaths!.ToLookup(death => death.HeroId);
        var heroIds = situations.HeroIds.Order().ToList();
        var samples = new List<DeathRiskSample>();
        // The last window must end by the end of the match, or "lived" would just mean "the match was over".
        for (var second = 0; second + HorizonSeconds <= situations.MatchEndsAtSeconds; second += SampleEverySeconds)
        {
            foreach (var heroId in heroIds)
            {
                // Dying at this very second (the situation may come from the sample 1 s before): neither "lived" nor a
                // situation to learn from.
                if (situations.At(heroId, second) is not { } situation
                    || deathsOf[heroId].Any(death => death.Seconds >= second - 1 && death.Seconds <= second))
                {
                    continue;
                }

                // A reincarnation counts: the hero lost the fight even if it came back.
                var death = deathsOf[heroId].FirstOrDefault(death => death.Seconds > second && death.Seconds <= second + HorizonSeconds);
                samples.Add(new DeathRiskSample(situation, death is not null, death?.Seconds));
            }
        }

        return samples;
    }
}
