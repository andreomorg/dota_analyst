using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Maps;

namespace DotaAnalyst.Domain.Abilities;

// The game data of the current patch a situation can read: hero profiles (roles, attack type), items by key (without
// item_), abilities by key, each hero's kit, the neutral item rules (only from the game files of a build) and the map
// of the match's patch. Any part may be missing (an unavailable catalog): what depends on it is then unknown, never
// guessed. FileVersions: the version (git blob) of each game file a build's catalog was read from, by path (e.g.
// scripts/npc/heroes/npc_dota_hero_slardar.txt); null for the current catalogs. A rule reviewed against one file
// holds while that file is the same.
public sealed record GameCatalog(
    IReadOnlyDictionary<int, HeroDetails>? Heroes = null,
    IReadOnlyDictionary<string, ItemDetails>? Items = null,
    IReadOnlyDictionary<string, AbilityDetails>? Abilities = null,
    IReadOnlyDictionary<int, HeroKit>? Kits = null,
    NeutralRules? Neutrals = null,
    GameMap? Map = null,
    IReadOnlyDictionary<string, string>? FileVersions = null)
{
    public static GameCatalog Empty { get; } = new();
}
