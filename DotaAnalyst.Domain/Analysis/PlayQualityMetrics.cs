using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// Retrospective heuristics, not proof that a cast caused survival or a modifier removal.
// SkillshotUses/Hits: null without activity, without a listed ability in the supplied kit, or without a known kit
// and any recorded listed cast. Uses can be zero for a known kit. Hits is null without debuffs, team identity for
// a matching effect, or the full post-cast window; zero means all casts were evaluated without an enemy effect.
// LowHealthAllyUses/Saves: null without activity/buffs, or if any listed save buff from this hero has unknown team,
// pre-buff health or nearby-enemy coverage. Self/enemy targets are excluded. Zero means no qualifying buffs.
// Saves additionally needs deaths and the full survival window; zero means all qualifying allies died.
// DispelledDebuffs: null without activity/debuffs or when an active disable at a relevant self cast has unknown end
// or incomplete follow-up, or without buffs for targeted dispels. Targeted dispels use buff Start on this hero;
// self-only dispels use item activations. Item uses without targets do not invalidate totals. Zero means none ended in time.
// A counted dispel only means "ended shortly after": Debuff has no expected duration or removal reason.
public sealed record PlayQualityMetrics(
    int? SkillshotUses, int? SkillshotHits, int? LowHealthAllyUses, int? Saves, int? DispelledDebuffs);

public static class PlayQualityMetricsCalculator
{
    public const double SkillshotWindow = 5;
    public const double LowHealthShare = 0.35;
    public const int EnemyRadius = 1200;
    public const int SampleTolerance = 2;
    public const int SurvivalSeconds = 10;
    public const double DispelWindow = 0.5;

    // Short, deliberately debuff-observable list: directed Hook pulls/stuns, directed Sacred Arrow stuns.
    // Five seconds allows projectile travel; it is an attribution tolerance, not a patch-specific duration.
    // Powershot/Shockwave are excluded: damage rows lack time, damage buckets and HeroHit lack inflictor.
    private static readonly Dictionary<string, string> Skillshots = new()
    {
        ["pudge_meat_hook"] = "modifier_pudge_meat_hook",
        ["mirana_arrow"] = "modifier_mirana_arrow",
    };

    // Protection/escape for an explicitly targeted ally: magic protection, displacement, delayed death,
    // death prevention, or temporary banishment. Eul's is not an ally save; Wind Waker is.
    // Item uses have no target in replay documents. Buffs alone are the save event source (including abilities),
    // so an ability cast and its modifier cannot count twice. BuffKind.Save alone is too broad (includes self saves).
    private static readonly HashSet<string> SaveModifiers =
    ["modifier_item_glimmer_cape_fade", "modifier_item_forcestaff_active", "modifier_wind_waker",
        "modifier_oracle_false_promise_timer", "modifier_dazzle_shallow_grave",
        "modifier_shadow_demon_disruption", "modifier_obsidian_destroyer_astral_imprisonment_prison"];

    // Self basic dispels only; no inference about whether a particular disable was dispellable.
    private static readonly HashSet<string> SelfDispels = ["item_manta", "item_black_king_bar", "item_satanic"];
    private static readonly HashSet<string> TargetedDispelModifiers =
        ["modifier_eul_cyclone", "modifier_wind_waker", "modifier_item_lotus_orb_active"];

    // teams: the complete match roster; kit: internal ability names, including uncast abilities (null = unknown).
    // matchEndSeconds: actual full match duration, NOT ReplayAnalysis.EndsAtSeconds (the laning phase limit).
    // Null families must stay null at the caller; an empty observed family means measured absence.
    public static PlayQualityMetrics Calculate(int heroId, ReplayActivity? activity,
        IReadOnlyDictionary<int, Team> teams, IReadOnlyDictionary<int, HeroPositions> positions,
        IReadOnlyList<HeroDeath>? deaths, double matchEndSeconds, IReadOnlyCollection<string>? kit = null)
    {
        if (activity is null)
        {
            return new(null, null, null, null, null);
        }

        var casts = activity.AbilityUses.Where(u => u.HeroId == heroId && u.Seconds >= 0
            && u.Seconds <= matchEndSeconds && Skillshots.ContainsKey(u.Name)).ToList();
        var hasSkillshot = casts.Count > 0 || kit?.Any(Skillshots.ContainsKey) == true;
        int? hits = hasSkillshot && activity.Debuffs is not null ? 0 : null;
        var consumed = new HashSet<Debuff>();
        foreach (var cast in casts.OrderBy(u => u.Seconds))
        {
            var effects = (activity.Debuffs ?? []).Where(d => d.SourceHeroId == heroId
                && d.Start >= cast.Seconds && d.Start <= cast.Seconds + SkillshotWindow
                && d.Modifier == Skillshots[cast.Name] && !consumed.Contains(d)).ToList();
            if (cast.Seconds + SkillshotWindow > matchEndSeconds
                || effects.Any(d => !teams.ContainsKey(d.TargetHeroId)) || !teams.ContainsKey(heroId))
            {
                hits = null;
            }
            else if (effects.FirstOrDefault(d => teams[d.TargetHeroId] != teams[heroId]) is { } effect)
            {
                hits++;
                consumed.Add(effect);
            }
        }

        int? lowUses = activity.Buffs is null ? null : 0, saves = lowUses;
        foreach (var buff in (activity.Buffs ?? []).Where(b => b.SourceHeroId == heroId
            && b.Start >= 0 && b.Start <= matchEndSeconds && SaveModifiers.Contains(b.Modifier)))
        {
            if (buff.TargetHeroId == heroId)
            {
                continue;
            }
            var target = buff.TargetHeroId;
            if (!teams.TryGetValue(target, out var targetTeam)
                || !teams.TryGetValue(heroId, out var ownTeam))
            {
                lowUses = saves = null;
                continue;
            }
            if (targetTeam != ownTeam)
            {
                continue;
            }
            var sample = Before(target, buff.Start, positions, deaths);
            if (sample?.Vitals?.HealthShare is not { } health || sample.Value.Vitals!.Health is <= 0)
            {
                lowUses = saves = null;
                continue;
            }
            if (health > LowHealthShare)
            {
                continue;
            }
            var enemies = teams.Where(p => p.Value != ownTeam).Select(p =>
                (Id: p.Key, Sample: Before(p.Key, buff.Start, positions, deaths))).ToList();
            var near = enemies.Any(e => e.Sample is { } enemy && enemy.Vitals?.Health is not <= 0
                && Math.Pow(enemy.X - sample.Value.X, 2) + Math.Pow(enemy.Y - sample.Value.Y, 2)
                    <= (double)EnemyRadius * EnemyRadius);
            if (!near && (enemies.Count == 0 || enemies.Any(e => e.Sample is null
                && !DeadAt(e.Id, buff.Start, positions, deaths))))
            {
                lowUses = saves = null;
                continue;
            }
            if (!near)
            {
                continue;
            }
            lowUses++;
            if (deaths is null || buff.Start + SurvivalSeconds > matchEndSeconds)
            {
                saves = null;
            }
            else if (!deaths.Any(d => d.HeroId == target && d.Seconds >= buff.Start
                && d.Seconds <= buff.Start + SurvivalSeconds))
            {
                saves++;
            }
        }

        int? dispels = activity.Debuffs is null || activity.Buffs is null ? null : 0;
        var removed = new HashSet<Debuff>();
        var dispelTimes = activity.ItemUses.Where(u => u.HeroId == heroId && SelfDispels.Contains(u.Name))
            .Select(u => u.Seconds)
            .Concat((activity.Buffs ?? []).Where(b => b.TargetHeroId == heroId
                && TargetedDispelModifiers.Contains(b.Modifier)).Select(b => b.Start));
        foreach (var seconds in dispelTimes.Where(t => t >= 0 && t <= matchEndSeconds))
        {
            foreach (var debuff in (activity.Debuffs ?? []).Where(d => d.TargetHeroId == heroId && d.IsDisable
                && d.Start < seconds && (d.End is null || d.End >= seconds)))
            {
                if (debuff.End is null || seconds + DispelWindow > matchEndSeconds)
                {
                    dispels = null;
                }
                else if (debuff.End <= seconds + DispelWindow && removed.Add(debuff))
                {
                    dispels++;
                }
            }
        }
        return new(hasSkillshot ? casts.Count : null, hits, lowUses, saves, dispels);
    }

    // Strictly before the cast, no future sample or interpolation, and never across a death.
    private static PositionSample? Before(int heroId, double seconds, IReadOnlyDictionary<int, HeroPositions> positions,
        IReadOnlyList<HeroDeath>? deaths)
    {
        if (!positions.TryGetValue(heroId, out var track))
        {
            return null;
        }
        var samples = track.Samples.Where(s => s.Seconds < seconds && s.Seconds >= seconds - SampleTolerance)
            .OrderByDescending(s => s.Seconds);
        foreach (var sample in samples)
        {
            return deaths?.Any(d => d.HeroId == heroId && d.Seconds >= sample.Seconds && d.Seconds < seconds) == true
                ? null : sample;
        }
        return null;
    }

    private static bool DeadAt(int heroId, double seconds, IReadOnlyDictionary<int, HeroPositions> positions,
        IReadOnlyList<HeroDeath>? deaths) => deaths?.Where(d => d.HeroId == heroId && d.Seconds <= seconds)
        .MaxBy(d => d.Seconds) is { } last && positions.TryGetValue(heroId, out var track)
        && !track.Samples.Any(s => s.Seconds > last.Seconds && s.Seconds <= seconds);
}
