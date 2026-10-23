using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Application.Coaching;

/// <summary>Coaching decision context v5: the v1 decision context unchanged (every scene) plus, for each scene, what the
/// team could see at its anchor and the match's heroes by catalog name. Experimental, local training data only.</summary>
/// <param name="SchemaVersion">Always 5.</param>
/// <param name="Decision">The decision context as the v1 contract builds it.</param>
/// <param name="Heroes">The ten heroes: name and roles from the catalog (empty when the catalog is unavailable).</param>
/// <param name="Situations">One per scene, same order as Decision.Scenes.</param>
public sealed record SituatedDecisionContext(
    int SchemaVersion, CoachingDecisionContext Decision, IReadOnlyList<SituatedHero> Heroes,
    IReadOnlyList<SituatedAnchor> Situations);

/// <summary>A hero of the match, named by the catalog (never by a model's memory of the ID).</summary>
public sealed record SituatedHero(int HeroId, string Name, Team Team, IReadOnlyList<HeroRole> Roles);

/// <summary>The situation at a scene's anchor; null when the hero had no sample there or was dead.</summary>
public sealed record SituatedAnchor(int AnchorSeconds, CoachingSituationSnapshot? Situation);
