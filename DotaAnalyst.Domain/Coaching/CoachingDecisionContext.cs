using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Coaching;

public sealed record CoachingPositionSnapshot(int HeroId, Team Team, int SampleSeconds, int X, int Y);
public sealed record CoachingEconomySnapshot(int SampleSeconds, int NetWorth, int? LastHits);

// Information at or before a fixed review anchor, not a reconstruction of what the player could see.
public sealed record CoachingDecisionFacts(
    int AnchorSeconds, IReadOnlyList<CoachingPositionSnapshot> Positions, int MissingHeroPositions,
    CoachingEconomySnapshot? Economy,
    IReadOnlyList<ItemPurchase>? RecentPurchases,
    IReadOnlyList<AbilityUpgrade>? RecentAbilityUpgrades,
    IReadOnlyList<MapObjective>? RecentObjectives);

// The episode and its economics are retrospective outcomes, separated from the earlier input facts.
public sealed record CoachingDecisionScene(CoachingEpisode RetrospectiveEpisode, CoachingDecisionFacts AtOrBeforeAnchor);

public sealed record CoachingDecisionContext(
    int SchemaVersion, string MatchId, int HeroId, CoachingRole Role, CoachingPatch Patch,
    string Perspective, IReadOnlyList<string> Limitations, IReadOnlyList<CoachingDecisionScene> Scenes);
