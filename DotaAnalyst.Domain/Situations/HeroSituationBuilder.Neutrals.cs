using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Situations;

// The neutral items (NeutralState): the tier open by the game clock (the build's NeutralRules), the artifact in the
// neutral slot and its active, the enhancement level and the Madstones of the crafts/Madstone readings — only what
// happened up to this second.
public sealed partial class HeroSituationBuilder
{
    private const string NeutralItemPrefix = "item_";

    private NeutralState NeutralsOf(int heroId, Team team, PositionSample self, int seconds)
    {
        var open = neutralRules?.OpenAt(seconds);
        var lastCraft = activity?.NeutralCrafts?.LastOrDefault(craft => craft.HeroId == heroId && craft.Seconds <= seconds);
        var myTier = ArtifactTier(heroId, seconds);
        var madstones = Madstones(heroId, seconds);
        var enemies = teamOf.Where(hero => hero.Value != team && PositionAt(hero.Key, seconds) is not null).Select(hero => hero.Key).ToList();
        var enemyTiers = enemies.Select(enemy => ArtifactTier(enemy, seconds)).ToList();
        var enemyMadstones = enemies.Select(enemy => Madstones(enemy, seconds)).ToList();
        var enemyActives = enemies.Where(enemy => Distance(self, PositionAt(enemy, seconds)!.Value) <= FightRange)
            .Select(enemy => ArtifactActiveReady(enemy, seconds)).ToList();

        return new NeutralState(
            TierOpen: neutralRules is null ? null : open?.Tier ?? 0,
            SecondsToNextTier: neutralRules?.NextAfter(seconds) is { } next ? next.OpensAtSeconds - seconds : null,
            MyArtifactTier: myTier,
            MyTiersBehind: open is not null && myTier is { } tier ? Math.Max(0, open.Tier - tier) : null,
            MyArtifactActiveReady: ArtifactActiveReady(heroId, seconds),
            MyEnhancementLevel: activity?.NeutralCrafts is null ? null : lastCraft?.EnhancementLevel ?? 0,
            MyMadstones: madstones,
            CanCraftNow: open is null || madstones is not { } inHand || activity?.NeutralCrafts is null ? null
                : inHand >= open.CraftCost && (lastCraft?.Tier ?? 0) < open.Tier,
            SecondsSinceLastCraft: lastCraft is null ? null : seconds - (int)lastCraft.Seconds,
            HiddenEnemyArtifactActivesReadyNear: enemyActives.Any(ready => ready is null) ? null : enemyActives.Count(ready => ready == true),
            // Unknown without Madstone readings, even with no enemy alive (an empty sum is not "no Madstones").
            HiddenEnemyMadstones: activity?.Madstones is null || enemyMadstones.Any(count => count is null) ? null
                : enemyMadstones.Sum(count => count!.Value),
            HiddenEnemyTiersBehind: open is null || enemyTiers.Count == 0 || enemyTiers.Any(tier => tier is null) ? null
                : Math.Round(enemyTiers.Average(tier => Math.Max(0, open.Tier - tier!.Value)), 2));
    }

    // The tier of the artifact in the neutral slot now (0 = empty or not a neutral of this build); null without the rules
    // or the slots.
    private int? ArtifactTier(int heroId, int seconds) =>
        neutralRules is null || ItemsAt(heroId, seconds) is not { } items ? null
            : items.Slots.GetValueOrDefault(SlotChange.NeutralSlot) is { } artifact ? neutralRules.TierOf(artifact) ?? 0 : 0;

    // The artifact has an active part and no cooldown running; false with no artifact or a passive one.
    private bool? ArtifactActiveReady(int heroId, int seconds)
    {
        if (itemDetails is null || ItemsAt(heroId, seconds) is not { } items)
        {
            return null;
        }

        return items.Slots.GetValueOrDefault(SlotChange.NeutralSlot) is { } artifact && !items.Cooling.Contains(artifact)
            && itemDetails.TryGetValue(artifact.StartsWith(NeutralItemPrefix, StringComparison.Ordinal) ? artifact[NeutralItemPrefix.Length..] : artifact,
                out var details)
            && details.HasActive;
    }

    // Madstones in hand at this second (0 before the first reading); null for documents without Madstone readings.
    private int? Madstones(int heroId, int seconds) =>
        activity?.Madstones is not { } tracks ? null
            : tracks.FirstOrDefault(track => track.HeroId == heroId)?.At(seconds)?.InHand ?? 0;
}
