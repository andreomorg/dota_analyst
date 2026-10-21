using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;

namespace DotaAnalyst.Domain.Abilities;

// The game data of the current patch a situation can read: hero profiles (roles, attack type), items by key (without
// item_), abilities by key and each hero's kit. Any part may be missing (an unavailable catalog): what depends on it is
// then unknown, never guessed.
public sealed record GameCatalog(
    IReadOnlyDictionary<int, HeroDetails>? Heroes = null,
    IReadOnlyDictionary<string, ItemDetails>? Items = null,
    IReadOnlyDictionary<string, AbilityDetails>? Abilities = null,
    IReadOnlyDictionary<int, HeroKit>? Kits = null)
{
    public static GameCatalog Empty { get; } = new();
}
