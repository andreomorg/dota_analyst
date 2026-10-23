using DotaAnalyst.Domain.Abilities;
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
/// <param name="HeroId">Hero ID.</param>
/// <param name="Name">Catalog name ("Unknown hero" when the catalog lacks it).</param>
/// <param name="Team">The hero's team.</param>
/// <param name="Roles">Catalog roles (empty when unknown).</param>
/// <param name="KitEffects">What the hero's abilities can do in the match's own game build (stun, silence, escape...,
/// AbilityEffects); null when the build's kit or abilities are unknown.</param>
public sealed record SituatedHero(int HeroId, string Name, Team Team, IReadOnlyList<HeroRole> Roles,
    IReadOnlyList<AbilityEffect>? KitEffects = null);

/// <summary>The situation at a scene's anchor.</summary>
/// <param name="AnchorSeconds">The scene's anchor (game clock).</param>
/// <param name="Situation">The team-visible situation; null when the hero had no sample there or was dead.</param>
/// <param name="Escape">v5.1: ground routes from the hero to its nearest standing tower (or fountain) and nearest
/// ally, with what the team saw along them; null when the hero had no sample, a route is null when unknown.</param>
public sealed record SituatedAnchor(int AnchorSeconds, CoachingSituationSnapshot? Situation,
    CoachingEscapeSnapshot? Escape = null);

/// <summary>The visible part of the escape routes at an anchor (never the hidden enemies near the path).</summary>
public sealed record CoachingEscapeSnapshot(CoachingRouteSnapshot? ToTower, CoachingRouteSnapshot? ToAlly)
{
    public static CoachingEscapeSnapshot From(Domain.Situations.SituationEscape escape) =>
        new(Route(escape.ToOwnTowerOrFountain), Route(escape.ToNearestAlly));

    private static CoachingRouteSnapshot? Route(Domain.Situations.EscapeRoute? route) => route is null ? null : new(
        (int)Math.Round(route.PathLength), (int)Math.Round(route.StraightDistance),
        route.Detour is { } detour ? Math.Round(detour, 2) : null,
        route.SeenShare is { } seen ? Math.Round(seen, 3) : null,
        route.NearestSeenEnemyToPath is { } enemy ? (int)Math.Round(enemy) : null);
}

/// <summary>One ground route (map units): its length, the straight distance, their ratio, the share of it the team
/// saw, and how close it passes to an enemy the team saw (null: none seen).</summary>
public sealed record CoachingRouteSnapshot(int PathLength, int StraightDistance, double? Detour, double? SeenShare,
    int? NearestSeenEnemyToPath);
