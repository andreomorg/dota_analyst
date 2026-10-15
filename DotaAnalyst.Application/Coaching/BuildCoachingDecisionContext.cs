using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Coaching;

public static class BuildCoachingDecisionContext
{
    public static CoachingDecisionContext Build(ReplayAnalysis replay, int heroId, CoachingPatch patch,
        IReadOnlyList<HeroPositions>? positions = null)
    {
        var covered = BuildCoachingContext.Build(replay, heroId, patch, positions);
        return new(1, covered.MatchId, heroId, covered.Role, patch, "omniscient-retrospective",
            ["Anchor is fixed ten seconds before the event, not the proven time of a player decision.",
             "Positions are omniscient; no player visibility, attention or intent is reconstructed.",
             "Facts at or before the anchor exclude later samples; retrospective episode is a separate outcome.",
             "Recent purchases do not reconstruct inventory, delivery or item availability.",
             "Ability upgrades do not prove cooldown or mana availability.",
             "Economy is an earlier sampled snapshot, not current gold carried.",
             "Objective events do not describe objective health, pressure or causal trade value.",
             "Unknown and empty collections are different; there is no tactical verdict."],
            CoachingDecisionSceneBuilder.Build(replay, heroId, covered.Episodes!, positions));
    }
}
