using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Application.Matches;

/// <summary>Catalog composition and observed enemy interactions from the player's perspective.</summary>
/// <param name="Teams">Player's team first, then the enemy team.</param>
/// <param name="Enemies">Enemy heroes ordered by kills on the player, then damage to the player; empty without a ready replay.</param>
public record MatchDraft(IReadOnlyList<DraftTeam> Teams, IReadOnlyList<DraftEnemy> Enemies);

/// <summary>One team's catalog composition.</summary>
/// <param name="Team">Radiant or Dire.</param>
/// <param name="IsPlayersTeam">Whether this is the requested player's team.</param>
/// <param name="Heroes">Heroes including unknown profiles.</param>
/// <param name="Summary">Counts over known profiles only; roles can overlap.</param>
public record DraftTeam(Team Team, bool IsPlayersTeam, IReadOnlyList<DraftHero> Heroes, DraftSummary Summary);

/// <summary>A drafted hero and its optional catalog profile.</summary>
/// <param name="HeroId">Hero ID.</param>
/// <param name="HeroName">Catalog name or the usual unknown hero fallback.</param>
/// <param name="Attribute">Primary attribute; null without a profile.</param>
/// <param name="AttackType">Melee or ranged; null without a profile.</param>
/// <param name="Roles">Catalog roles, unrelated to role queue position; null without a profile.</param>
public record DraftHero(int HeroId, string HeroName, HeroAttribute? Attribute, HeroAttackType? AttackType, IReadOnlyList<HeroRole>? Roles);

/// <summary>Team composition counts, excluding unknown profiles.</summary>
/// <param name="Roles">Hero counts for every catalog role.</param>
/// <param name="AttackTypes">Melee and ranged hero counts.</param>
/// <param name="Attributes">Hero counts for every primary attribute.</param>
public record DraftSummary(IReadOnlyList<DraftRoleCount> Roles, IReadOnlyList<DraftAttackCount> AttackTypes, IReadOnlyList<DraftAttributeCount> Attributes);

/// <summary>Count for one role.</summary>
/// <param name="Role">Catalog role.</param>
/// <param name="Count">Heroes with this role.</param>
public record DraftRoleCount(HeroRole Role, int Count);

/// <summary>Count for one attack type.</summary>
/// <param name="AttackType">Catalog attack type.</param>
/// <param name="Count">Heroes with this attack type.</param>
public record DraftAttackCount(HeroAttackType AttackType, int Count);

/// <summary>Count for one primary attribute.</summary>
/// <param name="Attribute">Catalog primary attribute.</param>
/// <param name="Count">Heroes with this attribute.</param>
public record DraftAttributeCount(HeroAttribute Attribute, int Count);

/// <summary>Observed interaction with one enemy; reincarnations do not count as kills.</summary>
/// <param name="HeroId">Enemy hero ID.</param>
/// <param name="HeroName">Catalog name or the usual unknown hero fallback.</param>
/// <param name="KillsOnPlayer">Final blows credited to the enemy on the player.</param>
/// <param name="DeathsByPlayer">Final blows credited to the player on the enemy.</param>
/// <param name="DamageToPlayer">Enemy damage to the player's hero; null without replay damage, zero when no rows exist.</param>
/// <param name="DamageFromPlayer">Player damage to the enemy hero; null without replay damage, zero when no rows exist.</param>
public record DraftEnemy(int HeroId, string HeroName, int KillsOnPlayer, int DeathsByPlayer, int? DamageToPlayer, int? DamageFromPlayer);
