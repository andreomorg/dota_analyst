using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Items;

namespace DotaAnalyst.Application.Catalogs;

/// <summary>What Aghanim's Scepter or Shard does for the hero.</summary>
/// <param name="Description">Game description of the upgrade.</param>
/// <param name="AbilityName">Display name of the ability it upgrades or grants, when given.</param>
/// <param name="IsNewAbility">Whether it grants a new ability.</param>
public sealed record KitAghanimUpgrade(string Description, string? AbilityName, bool IsNewAbility);
