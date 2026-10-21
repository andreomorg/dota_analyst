using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Situations;

// What the hero and its team did just before: smokes, teleports, escape items, uses, hero damage, buyback, glyph, scan
// and power runes (ReplayActivity).
public sealed partial class HeroSituationBuilder
{
    // Uses older than this say nothing about the moment any more (a smoke lasts 45 s at most).
    public const int RecentWindowSeconds = 120;
    public const int BusyWindowSeconds = 30;
    // "Being hit right now": the last three finished 2 s buckets.
    public const int HitWindowSeconds = 6;
    // How long each thing says something about the moment: a buyback is back after 480 s, a glyph after 300 s, a power
    // rune lasts up to about a minute.
    public const int BuybackCooldownSeconds = 480;
    public const int GlyphCooldownSeconds = 300;
    public const int PowerRuneWindowSeconds = 60;

    private RecentActivity Recent(int heroId, Team team, int seconds)
    {
        if (activity is null)
        {
            return RecentActivity.Unknown;
        }

        var items = activity.ItemUses.Where(use => use.HeroId == heroId).ToList();
        var abilities = activity.AbilityUses.Where(use => use.HeroId == heroId);
        // Uses are compared with their own (fractional) time: a use at 100.4 is after the second 100.
        bool Busy(RecordedUse use) => use.Seconds <= seconds && use.Seconds > seconds - BusyWindowSeconds;
        // Finished buckets that started in the window: [seconds - 6, seconds).
        var hits = activity.DamageBuckets?
            .Where(bucket => bucket.HeroId == heroId && bucket.EndsBy(seconds) && bucket.Seconds >= seconds - HitWindowSeconds)
            .ToList();
        return new RecentActivity(
            DamageFromHeroesRecently: hits?.Sum(bucket => bucket.FromHeroes),
            DamageToHeroesRecently: hits?.Sum(bucket => bucket.ToHeroes),
            OwnBuybackSecondsAgo: activity.Buybacks is { } buybacks
                ? SecondsAgo(buybacks.Where(buyback => buyback.HeroId == heroId).Select(buyback => buyback.Seconds), seconds, BuybackCooldownSeconds)
                : null,
            OwnGlyphSecondsAgo: activity.Glyphs is { } glyphs
                ? SecondsAgo(glyphs.Where(glyph => glyph.Team == team).Select(glyph => glyph.Seconds), seconds, GlyphCooldownSeconds)
                : null,
            OwnScanSecondsAgo: activity.Scans is { } scans
                ? SecondsAgo(scans.Where(scan => scan.Team == team).Select(scan => scan.Seconds), seconds)
                : null,
            OwnPowerRuneSecondsAgo: activity.Runes is { } runes
                ? SecondsAgo(runes.Where(rune => rune.HeroId == heroId && rune.IsPower).Select(rune => rune.Seconds), seconds, PowerRuneWindowSeconds)
                : null,
            OwnSmokeSecondsAgo: SecondsAgo(activity.Smokes.Where(smoke => smoke.Team == team).Select(smoke => smoke.Seconds), seconds),
            TeleportSecondsAgo: SecondsAgo(items.Where(use => TeleportItems.Contains(use.Name)).Select(use => use.Seconds), seconds),
            EscapeItemSecondsAgo: SecondsAgo(items.Where(use => EscapeItems.Contains(use.Name)).Select(use => use.Seconds), seconds),
            ItemUsesLast30Seconds: items.Count(Busy),
            AbilityUsesLast30Seconds: abilities.Count(Busy));
    }

    // Whole seconds since the last of these times at or before `seconds`; null when none is within the window.
    private static int? SecondsAgo(IEnumerable<double> times, int seconds, int window = RecentWindowSeconds)
    {
        // Max of nullables is null for an empty sequence.
        var last = times.Where(time => time <= seconds && time > seconds - window).Select(time => (double?)time).Max();
        return last is { } time ? (int)Math.Floor(seconds - time) : null;
    }
}
