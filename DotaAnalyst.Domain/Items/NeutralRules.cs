using DotaAnalyst.Domain.Heroes;

namespace DotaAnalyst.Domain.Items;

// A neutral item is crafted from two parts (patch 7.38+): an artifact (the "trinket", neutral slot) and an enhancement
// (enhancement slot).
public enum NeutralKind
{
    Artifact,
    Enhancement,
}

// An enhancement as a tier offers it: the item (key without item_) and its level in that tier (brawny 1..4).
public sealed record NeutralEnhancement(string Key, int Level);

// One neutral tier: when it opens (game clock), how many artifacts and enhancements a craft offers, how many Madstones
// a craft costs, the experience bonus, its artifacts, and its enhancements — the ones every hero is offered and the
// ones by primary attribute.
public sealed record NeutralTier(
    int Tier,
    int OpensAtSeconds,
    int ArtifactOptions,
    int EnhancementOptions,
    int CraftCost,
    double ExperienceBonus,
    IReadOnlyList<string> Artifacts,
    IReadOnlyList<NeutralEnhancement> GlobalEnhancements,
    IReadOnlyDictionary<HeroAttribute, IReadOnlyList<NeutralEnhancement>> EnhancementsByAttribute)
{
    // What a hero of this primary attribute can be offered: the global ones and its attribute's.
    public IReadOnlyList<NeutralEnhancement> EnhancementsFor(HeroAttribute attribute) =>
        GlobalEnhancements.Concat(EnhancementsByAttribute.GetValueOrDefault(attribute) ?? []).DistinctBy(enhancement => enhancement.Key).ToList();
}

// Value object: the neutral item rules of a game build (scripts/npc/neutral_items.txt): neutrals are not dropped ready,
// creeps drop Madstones and each opened tier lets a hero craft one artifact plus one enhancement with them. Also the
// Madstone drop per tier band (primary: the hero who gets the camp; secondary: the others) and when the Madstone limit
// ends.
public sealed record NeutralRules(
    IReadOnlyList<NeutralTier> Tiers,
    int? MadstoneNoLimitAtSeconds,
    IReadOnlyList<double> MadstonePrimary,
    IReadOnlyList<double> MadstoneSecondary)
{
    // The highest tier opened at this second (game clock); null before the first opens.
    public NeutralTier? OpenAt(int seconds) => Tiers.Where(tier => tier.OpensAtSeconds <= seconds).MaxBy(tier => tier.Tier);

    // The next tier to open after this second; null when every tier is open.
    public NeutralTier? NextAfter(int seconds) => Tiers.Where(tier => tier.OpensAtSeconds > seconds).MinBy(tier => tier.OpensAtSeconds);

    // The tier of an artifact or the lowest tier that offers an enhancement (key with or without item_); null when not
    // a neutral of these rules.
    public int? TierOf(string itemKey)
    {
        var key = Key(itemKey);
        return Tiers.FirstOrDefault(tier => tier.Artifacts.Contains(key))?.Tier
            ?? Tiers.FirstOrDefault(tier => AllEnhancements(tier).Any(enhancement => enhancement.Key == key))?.Tier;
    }

    public NeutralKind? KindOf(string itemKey)
    {
        var key = Key(itemKey);
        return Tiers.Any(tier => tier.Artifacts.Contains(key)) ? NeutralKind.Artifact
            : Tiers.Any(tier => AllEnhancements(tier).Any(enhancement => enhancement.Key == key)) ? NeutralKind.Enhancement
            : null;
    }

    private static IEnumerable<NeutralEnhancement> AllEnhancements(NeutralTier tier) =>
        tier.GlobalEnhancements.Concat(tier.EnhancementsByAttribute.Values.SelectMany(list => list));

    private static string Key(string itemKey) => itemKey.StartsWith("item_", StringComparison.Ordinal) ? itemKey["item_".Length..] : itemKey;
}
