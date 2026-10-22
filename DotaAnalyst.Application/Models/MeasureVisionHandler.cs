using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Maps;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Models;

// Use case: how well VisionModel predicts the replay's real fog of war on one kept replay (work/agents/plan.md MP4).
// For every living hero every 2 s whose visibility the replay knows (HeroVitals.VisibleToEnemies), the enemy team's
// vision sources at that second — its living heroes, observer wards, standing buildings, and in some variants a creep at
// each lane's wave front, or the document's moving vision (timelines.visionUnits: creeps, summons, illusions) and the
// fountain — say whether it should be seen; each variant turns obstacles on or off. Reveals (attacks from the fog,
// abilities, scans, Dust, Gem) only appear as "seen but not predicted". Trees are the map's at the start, or (last
// variant) with the ones cut by then (timelines.trees). Reads a kept document or one handed in: no parse, no training.
public sealed class MeasureVisionHandler(IArchivedReplaySource replays, GameCatalogLoader catalogs)
{
    // The fountain's vision (npc_units.txt has no entry; the replay's CDOTA_Unit_Fountain says 1800 day and night).
    private const double FountainRadius = 1800;
    // Vision ranges of patch 7.41 (scripts/npc/npc_units.txt, read on 22/10/2026): day / night.
    public const double ObserverWardRadius = 1600;
    public const double CreepRadius = 750;
    private const double HeroDayDefault = 1800;
    private const double HeroNightDefault = 800;
    // Sentry wards reveal invisible units around them (approximation of the true-sight radius).
    private const double SentryTrueSightRadius = 900;
    // A hero this close to an enemy wave front counts as "near enemy creeps" in the breakdown.
    private const double NearCreepsDistance = 900;

    // The variant whose misses are broken down by the rule that caused them.
    private const string MainVariant = "terrainTreesVisionUnitsCutTrees";

    // Enemy modifiers by what they do, measured on the two local replays (22/10: share of samples with the modifier
    // where the hero was seen): Corrosive Haze shows the hero wherever it is (35/35 seen); Dust of Appearance, Track and
    // Haze give true sight (an invisible hero is seen if in vision) but no vision of their own — Dust 151/331 and Track
    // 135/249 seen, so counting them as reveals was wrong. The Track facet's modifier_bounty_hunter_big_game_hunter_tracks
    // does neither (1,350/2,911).
    private static readonly HashSet<string> RevealingModifiers = ["modifier_slardar_amplify_damage"];

    private static readonly HashSet<string> TrueSightModifiers =
    [
        "modifier_slardar_amplify_damage", "modifier_item_dustofappearance", "modifier_bounty_hunter_track",
    ];

    // Extra sources of each variant: a creep at each wave front, or the document's moving vision; cut trees or not.
    private enum Extra { None, CreepFronts, VisionUnits }

    private static readonly (string Name, VisionRules Rules, Extra Extra, bool CutTrees)[] Variants =
    [
        ("radiusOnly", VisionRules.RadiusOnly, Extra.None, false),
        ("terrain", new VisionRules(Terrain: true, Trees: false, FogBlockers: false), Extra.None, false),
        ("terrainTrees", VisionRules.All, Extra.None, false),
        ("terrainTreesCreepFronts", VisionRules.All, Extra.CreepFronts, false),
        ("radiusOnlyVisionUnits", VisionRules.RadiusOnly, Extra.VisionUnits, false),
        ("terrainTreesVisionUnits", VisionRules.All, Extra.VisionUnits, false),
        ("terrainTreesVisionUnitsCutTrees", VisionRules.All, Extra.VisionUnits, true),
        // Tuning (moving vision and cut trees), 22/10: tree radius 32-96 and height steps 64/96 changed nothing (kept in
        // the plan's record); now obstacles next to either end, and the target's terrain at the foot of a cliff.
        // Clearing obstacles next to the ends only traded misses for wrong "seen" one for one; the lowest level around the
        // target gave +0.1 point. Now the edge of the ranges.
        ("slack64", new VisionRules(TargetSlack: 64), Extra.VisionUnits, true),
        ("slack64 range+32", new VisionRules(TargetSlack: 64, RadiusBonus: 32), Extra.VisionUnits, true),
        ("slack64 range+64", new VisionRules(TargetSlack: 64, RadiusBonus: 64), Extra.VisionUnits, true),
        ("slack64 range+128", new VisionRules(TargetSlack: 64, RadiusBonus: 128), Extra.VisionUnits, true),
        ("slack64 range-64", new VisionRules(TargetSlack: 64, RadiusBonus: -64), Extra.VisionUnits, true),
        // The full model: moving vision, cut trees, the target's terrain at the foot of a cliff, true sight and reveals.
        (FullVariant, new VisionRules(TargetSlack: 64), Extra.VisionUnits, true),
        // The same, ignoring invisibility (does the replay's fog mark an invisible hero inside vision as seen?).
        (NoInvisibilityVariant, new VisionRules(TargetSlack: 64), Extra.VisionUnits, true),
        // The full model, also counting a hero the model saw at the previous sample (2 s before): does the fog linger?
        (LingerVariant, new VisionRules(TargetSlack: 64), Extra.VisionUnits, true),
    ];

    private const string LingerVariant = "full + seen 2 s before";

    private const string NoInvisibilityVariant = "full without invisibility";

    private const string FullVariant = "full: units + cut trees + slack64 + true sight + haze";

    public async Task<VisionMeasureResult> HandleAsync(MatchId matchId, CancellationToken cancellationToken) =>
        await replays.FindAsync(matchId, cancellationToken) is { } replay
            ? await MeasureAsync(matchId, replay, cancellationToken)
            : new(ModelSamplesStatus.NoDocument, null);

    // A replay document handed in (a fresh parse that is not kept): measured, nothing saved.
    public async Task<VisionMeasureResult> MeasureAsync(MatchId matchId, ArchivedReplay replay, CancellationToken cancellationToken)
    {
        var (catalog, _) = await catalogs.LoadForMatchAsync(replay.StartedAt, cancellationToken);
        if (catalog.Map is not { } map)
        {
            return new(ModelSamplesStatus.Outdated, null);
        }

        var teams = replay.Analysis.Players.ToDictionary(player => player.HeroId, player => player.Team);
        var counts = Variants.ToDictionary(variant => variant.Name, _ => new VisionCounts());
        var samples = 0;
        var visionUnits = replay.Activity?.VisionUnits?.ToDictionary(sample => sample.Seconds);
        var downTrees = new Dictionary<int, IReadOnlySet<int>>();
        // Every enemy modifier active on a sampled hero: in how many samples it was seen or not (a reveal is ~always seen).
        var modifierSeen = new Dictionary<string, (int Seen, int NotSeen)>();
        // The full model's answer at each hero's previous sample.
        var lastFull = new Dictionary<int, (int Second, bool Predicted)>();
        foreach (var target in replay.Positions.Where(track => teams.ContainsKey(track.HeroId)))
        {
            var enemyTeam = teams[target.HeroId] == Team.Radiant ? Team.Dire : Team.Radiant;
            foreach (var sample in target.Samples)
            {
                if (sample.Vitals?.VisibleToEnemies is not { } seen || sample.Seconds < 0)
                {
                    continue;
                }

                samples++;
                var t = sample.Seconds;
                var night = IsNight(replay.Activity, t);
                var sources = Sources(replay, catalog, map, teams, enemyTeam, t, night);
                var creeps = CreepFronts(replay.Activity, map, enemyTeam, t).ToList();
                var nearCreeps = creeps.Any(creep => Distance(creep.X, creep.Y, sample.X, sample.Y) <= NearCreepsDistance);
                var hidden = sample.Vitals.Invisible == true && !SentryNear(replay.Analysis, enemyTeam, t, sample);
                foreach (var modifier in replay.Activity?.Debuffs?.Where(debuff => debuff.TargetHeroId == target.HeroId && debuff.ActiveAt(t))
                             .Select(debuff => debuff.Modifier).Distinct() ?? [])
                {
                    var (wasSeen, notSeen) = modifierSeen.GetValueOrDefault(modifier);
                    modifierSeen[modifier] = seen ? (wasSeen + 1, notSeen) : (wasSeen, notSeen + 1);
                }

                var active = replay.Activity?.Debuffs?.Where(debuff => debuff.TargetHeroId == target.HeroId && debuff.ActiveAt(t))
                    .Select(debuff => debuff.Modifier).ToHashSet() ?? [];
                var revealed = active.Overlaps(RevealingModifiers);
                // The full model: true sight from the target's modifiers undoes invisibility too.
                var hiddenFull = hidden && !active.Overlaps(TrueSightModifiers);
                var units = visionUnits?.GetValueOrDefault(t) is { } moving
                    ? (enemyTeam == Team.Radiant ? moving.Radiant : moving.Dire)
                        .Select(circle => new VisionSource(circle.X, circle.Y, night ? circle.Night : circle.Day))
                        .Concat(map.Landmarks.Where(landmark => landmark.Kind == LandmarkKind.Fountain && landmark.Team == enemyTeam)
                            .Select(fountain => new VisionSource(fountain.X, fountain.Y, FountainRadius)))
                        .ToList()
                    : null;
                IReadOnlySet<int>? down = null;
                if (replay.Activity?.Trees is { } trees && !downTrees.TryGetValue(t, out down))
                {
                    downTrees[t] = down = trees.DownAt(t);
                }

                var previousFull = lastFull.TryGetValue(target.HeroId, out var last) ? last : ((int Second, bool Predicted)?)null;
                foreach (var (name, rules, extra, cutTrees) in Variants)
                {
                    // Variants with the document's moving vision only count where the document has it.
                    if (extra == Extra.VisionUnits && units is null || cutTrees && down is null)
                    {
                        continue;
                    }

                    var all = sources.Select(source => source.Source);
                    all = extra switch
                    {
                        Extra.CreepFronts => all.Concat(creeps),
                        Extra.VisionUnits => all.Concat(units!),
                        _ => all,
                    };
                    var sourceList = all.ToList();
                    var full = name is FullVariant or NoInvisibilityVariant or LingerVariant;
                    var invisible = name == NoInvisibilityVariant ? false : full ? hiddenFull : hidden;
                    var predicted = full && revealed
                        || !invisible && sourceList.Any(source => VisionModel.Sees(map, source, sample.X, sample.Y, cutTrees ? down : null, rules));
                    if (name == FullVariant)
                    {
                        lastFull[target.HeroId] = (t, predicted);
                    }

                    if (name == LingerVariant && !predicted && previousFull is { Second: var before, Predicted: true } && t - before <= 2)
                    {
                        predicted = true;
                    }

                    counts[name].Add(predicted, seen, nearCreeps);
                    // For the main variant: which rule hid a hero the game showed (the one whose removal shows it).
                    if (name == MainVariant && seen && !predicted && !hidden)
                    {
                        bool SeenWithout(VisionRules relaxed) =>
                            sourceList.Any(source => VisionModel.Sees(map, source, sample.X, sample.Y, down, relaxed));
                        counts[name].Blocked(
                            SeenWithout(rules with { Trees = false }), SeenWithout(rules with { Terrain = false }),
                            SeenWithout(rules with { FogBlockers = false }), SeenWithout(VisionRules.RadiusOnly),
                            replay.Activity?.Debuffs?.Where(debuff => debuff.TargetHeroId == target.HeroId && debuff.ActiveAt(t))
                                .Select(debuff => debuff.Modifier) ?? [],
                            sourceList.Count == 0 ? double.PositiveInfinity
                                : sourceList.Min(source => Distance(source.X, source.Y, sample.X, sample.Y) - source.Radius),
                            target.HeroId, t);
                    }
                }
            }
        }

        return new(ModelSamplesStatus.Found, new VisionMeasureSet(matchId.ToString(), samples,
            Variants.Where(variant => counts[variant.Name].Total > 0).Select(variant => counts[variant.Name].ToResult(variant.Name)).ToList(),
            modifierSeen.Where(pair => pair.Value.Seen + pair.Value.NotSeen >= 10)
                .OrderByDescending(pair => (double)pair.Value.Seen / (pair.Value.Seen + pair.Value.NotSeen))
                .ToDictionary(pair => pair.Key, pair => new[] { pair.Value.Seen, pair.Value.NotSeen })));
    }

    private static List<(VisionSource Source, bool Building)> Sources(ArchivedReplay replay, GameCatalog catalog, GameMap map,
        Dictionary<int, Team> teams, Team team, int t, bool night)
    {
        var sources = new List<(VisionSource, bool)>();
        foreach (var hero in replay.Positions.Where(track => teams.GetValueOrDefault(track.HeroId) == team))
        {
            if (hero.At(t, 0) is not { } at)
            {
                continue;
            }

            var movement = catalog.Heroes?.GetValueOrDefault(hero.HeroId)?.Profile?.Movement;
            var radius = night ? movement?.NightVision?.Value ?? HeroNightDefault : movement?.DayVision?.Value ?? HeroDayDefault;
            sources.Add((new VisionSource(at.X, at.Y, radius), false));
        }

        foreach (var ward in replay.Analysis.Wards ?? [])
        {
            if (ward.Kind == WardKind.Observer && ward.Team == team && ward.PlacedAt <= t && (ward.RemovedAt is not { } gone || gone > t))
            {
                sources.Add((new VisionSource(ward.X, ward.Y, ObserverWardRadius), false));
            }
        }

        foreach (var building in replay.Activity?.Buildings ?? [])
        {
            if (building.Team == team && building.StandingAt(t))
            {
                sources.Add((new VisionSource(building.X, building.Y, BuildingRadius(building, night)), true));
            }
        }

        return sources;
    }

    // npc_units.txt (7.41): towers 1900 by day, 800 (tier 1) or 1100 at night; barracks 900/600; the Ancient 2600.
    private static double BuildingRadius(BuildingHealth building, bool night) => building.Kind switch
    {
        BuildingKind.Ancient => 2600,
        BuildingKind.Barracks => night ? 600 : 900,
        _ => night ? (building.Tier == 1 ? 800 : 1100) : 1900,
    };

    // One creep at each lane's wave front of the team: the point of the team's creep path whose progress x + y is the
    // front's.
    private static IEnumerable<VisionSource> CreepFronts(ReplayActivity? activity, GameMap map, Team team, int t)
    {
        foreach (var front in activity?.Waves?.Where(wave => wave.Seconds == t) ?? [])
        {
            if ((team == Team.Radiant ? front.Radiant : front.Dire) is not { } progress
                || map.CreepPaths.FirstOrDefault(path => path.Lane == front.Lane && path.Team == team) is not { } path)
            {
                continue;
            }

            for (var i = 1; i < path.Points.Count; i++)
            {
                var (a, b) = (path.Points[i - 1], path.Points[i]);
                double from = a.X + a.Y, to = b.X + b.Y;
                if (progress >= Math.Min(from, to) && progress <= Math.Max(from, to) && from != to)
                {
                    var share = (progress - from) / (to - from);
                    yield return new VisionSource(a.X + (b.X - a.X) * share, a.Y + (b.Y - a.Y) * share, CreepRadius);
                    break;
                }
            }
        }
    }

    private static bool SentryNear(ReplayAnalysis analysis, Team team, int t, PositionSample sample) =>
        (analysis.Wards ?? []).Any(ward => ward.Kind == WardKind.Sentry && ward.Team == team && ward.PlacedAt <= t
            && (ward.RemovedAt is not { } gone || gone > t) && Distance(ward.X, ward.Y, sample.X, sample.Y) <= SentryTrueSightRadius);

    // Unknown day/night counts as day.
    private static bool IsNight(ReplayActivity? activity, int t) =>
        activity?.DayNight?.LastOrDefault(change => change.Seconds <= t) is { IsDay: false };

    private static double Distance(double x0, double y0, double x1, double y1) => Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));

    private sealed class VisionCounts
    {
        private int truePositive, falsePositive, falseNegative, trueNegative, missedNearCreeps;

        public int Total => truePositive + falsePositive + falseNegative + trueNegative;

        private int byTrees, byTerrain, byFogBlockers, byCombination, noSourceInRange;

        // A miss of the main variant: seen once trees, terrain or blockers are left out (or only with all of them out,
        // or not even then: no source in range).
        // Enemy modifiers on the hero when no source was in range: candidates for reveals (Track, Dust...).
        private readonly Dictionary<string, int> debuffsWithoutSource = [];

        // Misses with no source in range, by how far outside the nearest range the hero was.
        private int outsideBy100, outsideBy300;

        // Misses with no source in range, by the hero seen.
        private readonly Dictionary<int, int> withoutSourceByHero = [];

        // ...and by 5-minute bucket of the game clock (a misaligned clock after a pause would pile them up).
        private readonly SortedDictionary<int, int> withoutSourceByMinute = [];

        public void Blocked(bool withoutTrees, bool withoutTerrain, bool withoutFogBlockers, bool radiusOnly, IEnumerable<string> debuffs,
            double nearestMargin, int heroId, int second)
        {
            if (!radiusOnly && !withoutTrees && !withoutTerrain && !withoutFogBlockers)
            {
                withoutSourceByHero[heroId] = withoutSourceByHero.GetValueOrDefault(heroId) + 1;
                var minutes = second / 300 * 5;
                withoutSourceByMinute[minutes] = withoutSourceByMinute.GetValueOrDefault(minutes) + 1;
            }

            if (!radiusOnly && nearestMargin <= 100) outsideBy100++;
            else if (!radiusOnly && nearestMargin <= 300) outsideBy300++;

            if (withoutTrees) byTrees++;
            if (withoutTerrain) byTerrain++;
            if (withoutFogBlockers) byFogBlockers++;
            if (!withoutTrees && !withoutTerrain && !withoutFogBlockers)
            {
                if (radiusOnly)
                {
                    byCombination++;
                }
                else
                {
                    noSourceInRange++;
                    foreach (var debuff in debuffs.Distinct())
                    {
                        debuffsWithoutSource[debuff] = debuffsWithoutSource.GetValueOrDefault(debuff) + 1;
                    }
                }
            }
        }

        public void Add(bool predicted, bool seen, bool nearCreeps)
        {
            switch (predicted, seen)
            {
                case (true, true): truePositive++; break;
                case (true, false): falsePositive++; break;
                case (false, true):
                    falseNegative++;
                    if (nearCreeps)
                    {
                        missedNearCreeps++;
                    }

                    break;
                default: trueNegative++; break;
            }
        }

        public VisionVariantResult ToResult(string name) =>
            new(name, truePositive, falsePositive, falseNegative, trueNegative, missedNearCreeps,
                name == MainVariant
                    ? new VisionMissCauses(byTrees, byTerrain, byFogBlockers, byCombination, noSourceInRange,
                        debuffsWithoutSource.OrderByDescending(pair => pair.Value).Take(15).ToDictionary(), outsideBy100, outsideBy300,
                        withoutSourceByHero.OrderByDescending(pair => pair.Value).ToDictionary(),
                        withoutSourceByMinute.ToDictionary())
                    : null);
    }
}

/// <summary>Why the main variant missed heroes the game showed: the rule whose removal shows them (a miss can count for
/// more than one), more than one rule together, or no source in range at all (reveals the document does not have).</summary>
public sealed record VisionMissCauses(int Trees, int Terrain, int FogBlockers, int Combination, int NoSourceInRange,
    IReadOnlyDictionary<string, int> DebuffsWithoutSource, int OutsideRangeBy100, int OutsideRangeBy100To300,
    IReadOnlyDictionary<int, int> WithoutSourceByHero, IReadOnlyDictionary<int, int> WithoutSourceByMinute);

public sealed record VisionMeasureResult(ModelSamplesStatus Status, VisionMeasureSet? Measure);

/// <summary>How the vision model did on one match (internal: for measuring it).</summary>
/// <param name="MatchId">Match ID as text.</param>
/// <param name="Samples">Hero samples whose real visibility the replay knows.</param>
/// <param name="Variants">One result per variant of the model (obstacles and sources on or off).</param>
/// <param name="ModifierSeen">Enemy modifiers active on sampled heroes (≥ 10 samples): [seen, not seen].</param>
public sealed record VisionMeasureSet(string MatchId, int Samples, IReadOnlyList<VisionVariantResult> Variants,
    IReadOnlyDictionary<string, int[]> ModifierSeen);

/// <summary>One variant: predicted × real counts. MissedNearCreeps: seen but not predicted, within 900 of an enemy wave front.</summary>
public sealed record VisionVariantResult(string Name, int SeenPredicted, int PredictedNotSeen, int SeenNotPredicted,
    int NeitherSeenNorPredicted, int MissedNearCreeps, VisionMissCauses? MissCauses = null);
