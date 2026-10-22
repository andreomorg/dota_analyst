using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Domain.Situations;

// Domain service: the HeroSituation of any hero at any second of one match. Everything it reads happened at or before
// that second: positions at that second, deaths, wards and objectives up to it, and the last minute already turned.
//
// A partial class: this file has the state, the entry points and the shared helpers; each part of the situation is
// read in a file of its own (HeroSituationBuilder.Activity.cs, .Readiness.cs, .Map.cs, .Waves.cs, .Combat.cs,
// .Vision.cs, .Intent.cs, .Kit.cs, .Spells.cs, .Neutrals.cs...), with the constants it uses.
public sealed partial class HeroSituationBuilder
{
    // Same radii as the metrics: an ally this close is nearby; this close to a fight is in it.
    public const int NearRadius = MatchMetricsCalculator.AllyRadius;
    public const int FightRange = MatchMetricsCalculator.FightPresenceRadius;

    // Positions come every 2 s: a living hero always has a sample at this second or the one before.
    private const int SampleTolerance = 1;

    private readonly ReplayAnalysis replay;
    private readonly MatchStatTimeline stats;
    private readonly ReplayActivity? activity;
    // Wave fronts by lane and second (null without waves in the document).
    private readonly Dictionary<(Lane Lane, int Seconds), WaveFront>? wavesAt;
    private readonly IReadOnlyDictionary<int, HeroPositions> positionsOf;
    private readonly IReadOnlyDictionary<int, Team> teamOf;
    private readonly IReadOnlyDictionary<int, HeroMetrics> metricsOf;
    // The heroes' catalog profile (roles, attack type); null when the catalog was not given.
    private readonly IReadOnlyDictionary<int, HeroDetails>? heroDetails;
    // The items' catalog data by key (without item_), for the big-item spikes; null when not given.
    private readonly IReadOnlyDictionary<string, ItemDetails>? itemDetails;
    // The abilities' effects by key and each hero's kit (the KIT part); null when not given.
    private readonly Dictionary<string, IReadOnlySet<AbilityEffect>>? abilityEffects;
    private readonly IReadOnlyDictionary<int, HeroKit>? kits;
    // The abilities' full data (damage, healing, mana per level) for SpellPower; null when not given.
    private readonly IReadOnlyDictionary<string, AbilityDetails>? abilityDetails;
    // The build's neutral item rules (tiers, costs); null with the current OpenDota catalogs.
    private readonly NeutralRules? neutralRules;

    private HeroSituationBuilder(
        ReplayAnalysis replay, MatchStatTimeline stats, IReadOnlyList<HeroPositions> positions, ReplayActivity? activity,
        GameCatalog catalog)
    {
        itemDetails = catalog.Items;
        heroDetails = catalog.Heroes;
        kits = catalog.Kits;
        abilityDetails = catalog.Abilities;
        neutralRules = catalog.Neutrals;
        abilityEffects = catalog.Abilities?.ToDictionary(ability => ability.Key, ability => AbilityEffects.Of(ability.Value));
        this.replay = replay;
        this.stats = stats;
        this.activity = activity;
        wavesAt = activity?.Waves?.DistinctBy(wave => (wave.Lane, wave.Seconds)).ToDictionary(wave => (wave.Lane, wave.Seconds));
        positionsOf = positions.DistinctBy(hero => hero.HeroId).ToDictionary(hero => hero.HeroId);
        teamOf = replay.Players.ToDictionary(player => player.HeroId, player => player.Team);
        metricsOf = (replay.Metrics ?? []).DistinctBy(hero => hero.HeroId).ToDictionary(hero => hero.HeroId);
    }

    // Null for analyses without what a situation needs (timeline, deaths, wards, objectives): they are outdated.
    // activity: null when the document has none (the recent activity then comes out as RecentActivity.Unknown).
    // catalog: the current patch's game data (hero profiles, items, abilities, kits); missing parts leave what depends on
    // them unknown.
    public static HeroSituationBuilder? From(
        ReplayAnalysis replay, IReadOnlyList<HeroPositions> positions, ReplayActivity? activity = null, GameCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(positions);
        if (replay.Deaths is null || replay.Wards is null || replay.Objectives is null
            || MatchStatTimeline.From(replay) is not { } stats)
        {
            return null;
        }

        return new HeroSituationBuilder(replay, stats, positions, activity, catalog ?? GameCatalog.Empty);
    }

    // Whether the replay's activity was read: without it, RecentActivity is Unknown (not "nothing was used").
    public bool HasActivity => activity is not null;

    // The game clock at the end of the match (the timeline's last point).
    public int MatchEndsAtSeconds => stats.Seconds[^1];

    public IEnumerable<int> HeroIds => teamOf.Keys;

    // Null when the hero is dead (no position then) or unknown.
    public HeroSituation? At(int heroId, int seconds)
    {
        if (!teamOf.TryGetValue(heroId, out var team) || PositionAt(heroId, seconds) is not { } self)
        {
            return null;
        }

        var allies = Living(seconds, hero => hero != heroId && teamOf[hero] == team);
        var enemies = Living(seconds, hero => teamOf[hero] != team);
        var allyDistances = allies.Select(ally => Distance(self, ally)).ToList();
        var enemyDistances = enemies.Select(enemy => Distance(self, enemy)).ToList();

        var level = Value(heroId, StatKey.Level, seconds);
        var enemyLevels = teamOf.Where(hero => hero.Value != team).Select(hero => Value(hero.Key, StatKey.Level, seconds)).ToList();
        var (role, roleEstimated, roleConfidence) = RoleOf(heroId, seconds);
        var lastDeath = replay.Deaths!.LastOrDefault(death => death.HeroId == heroId && !death.Reincarnated && death.Seconds <= seconds);
        var enemyCooldowns = EnemyCooldownsNear(team, self, seconds);
        var enemyStates = EnemyStates(team, seconds);

        return new HeroSituation(
            HeroId: heroId,
            Team: team,
            Seconds: seconds,
            Role: role,
            RoleInferred: roleEstimated,
            RoleConfidence: roleConfidence,
            X: self.X,
            Y: self.Y,
            Region: MapRegions.RegionOf(team, self.X, self.Y),
            OnEnemySide: MatchMetricsCalculator.IsEnemySide(team, self.X, self.Y),
            NearestAllyDistance: allyDistances.Count == 0 ? null : allyDistances.Min(),
            AlliesNear: allyDistances.Count(distance => distance <= NearRadius),
            AlliesWithinFightRange: allyDistances.Count(distance => distance <= FightRange),
            AlliesAlive: allies.Count,
            EnemiesAlive: enemies.Count,
            Level: level,
            NetWorth: Value(heroId, StatKey.NetWorth, seconds),
            LevelLeadOverEnemies: enemyLevels.Count == 0 ? 0 : level - enemyLevels.Average(),
            TeamNetWorthLead: TeamLead(team, StatKey.NetWorth, seconds),
            TeamExperienceLead: TeamLead(team, StatKey.Experience, seconds),
            DamageFromHeroesLastMinute: stats.Between(heroId, StatKey.DamageFromHeroes, seconds - 60, seconds) ?? 0,
            TeamObserversActive: replay.Wards!.Count(ward => ward.Team == team && ward.Kind == WardKind.Observer
                && ward.PlacedAt <= seconds && (ward.RemovedAt ?? int.MaxValue) > seconds),
            OwnTowersLost: TowersLost(team, seconds),
            EnemyTowersLost: TowersLost(team == Team.Radiant ? Team.Dire : Team.Radiant, seconds),
            SecondsSinceLastDeath: lastDeath is null ? null : seconds - lastDeath.Seconds,
            Recent: Recent(heroId, team, seconds),
            Readiness: ReadinessOf(heroId, seconds),
            Map: MapAt(heroId, team, seconds),
            Condition: self.Vitals is { } vitals ? ConditionOf(self, vitals, allies, enemies) : Condition.Unknown,
            // The enemies in the lane come from positions, not waves: known even for documents without waves.
            Waves: WavesFor(heroId, team, self, seconds) with { SeenEnemiesInMyLane = EnemiesInLane(team, self, enemies, seenOnly: true) },
            Body: self.Stats is { } combat
                ? new Body(combat.EstimatedArmor, combat.MagicResistance, combat.MoveSpeed, combat.AverageDamage, combat.Strength,
                    combat.Agility, combat.Intellect)
                : Body.Unknown,
            Fight: FightOf(heroId, seconds),
            Farm: FarmOf(heroId, team, seconds),
            Intent: IntentOf(heroId, team, self, seconds),
            Missing: MissingOf(team, self, seconds),
            Afflictions: AfflictionsOf(heroId, seconds),
            Boons: BoonsOf(heroId, seconds),
            Buildings: BuildingsAround(team, self, seconds),
            MapWaves: MapWavesFor(team, seconds),
            Surroundings: SurroundingsOf(heroId, team, self, seconds),
            Vision: CoverageOf(team, seconds),
            Signals: SignalsOf(heroId, team, self, seconds),
            Momentum: MomentumOf(heroId, team, seconds),
            Kit: KitOf(heroId, team, seconds),
            Spells: SpellsOf(heroId, team, self, seconds),
            Neutrals: NeutralsOf(heroId, team, self, seconds),
            Threat: ThreatOf(heroId, team, self, seconds),
            Lane: LaneOpponentOf(heroId, team, self, seconds),
            Hidden: new HiddenEnemies(
                NearestEnemyDistance: enemyDistances.Count == 0 ? null : enemyDistances.Min(),
                EnemiesNear: enemyDistances.Count(distance => distance <= NearRadius),
                EnemiesWithinFightRange: enemyDistances.Count(distance => distance <= FightRange),
                EnemySmokeSecondsAgo: activity is null ? null
                    : SecondsAgo(activity.Smokes.Where(smoke => smoke.Team != team).Select(smoke => smoke.Seconds), seconds),
                EnemiesHealthShare: AverageShare(enemies.Where(enemy => Distance(self, enemy) <= FightRange)),
                EnemyScanSecondsAgo: activity?.Scans is { } scans
                    ? SecondsAgo(scans.Where(scan => scan.Team != team).Select(scan => scan.Seconds), seconds)
                    : null,
                EnemyPowerRuneSecondsAgo: activity?.Runes is { } runes
                    ? SecondsAgo(runes.Where(rune => rune.Team != team && rune.IsPower).Select(rune => rune.Seconds), seconds, PowerRuneWindowSeconds)
                    : null,
                EnemyWaveAhead: EnemyWaveAhead(team, self, seconds),
                EnemyCreepsAtFront: EnemyCreepsAtFront(team, self, seconds),
                WaveHealthBalanceAtFront: WaveHealthBalance(team, self, seconds),
                EnemyWaveStacked: EnemyWaveStacked(team, self, seconds),
                EnemyCampsStacked: Camps(team, MapRegion.EnemyJungle, seconds).Stacked,
                EnemyBuildingsUnderAttack: BuildingsUnderAttack(team == Team.Radiant ? Team.Dire : Team.Radiant, seconds),
                EnemiesInMyLane: EnemiesInLane(team, self, enemies, seenOnly: false),
                InEnemyObserverVision: InObserverVision(team == Team.Radiant ? Team.Dire : Team.Radiant, self, seconds),
                EnemiesUnderSmoke: enemyStates.UnderSmoke,
                EnemiesWithBuyback: enemyStates.WithBuyback,
                EnemiesWithTeleportReady: TeleportsReady(team == Team.Radiant ? Team.Dire : Team.Radiant, seconds),
                PowerRuneOnMap: PowerRuneOnMap(seconds),
                EnemyBigItemsLast2Minutes: EnemyBigItems(team, seconds),
                EnemiesWithDisableReadyNear: EnemiesWithDisableReadyNear(team, self, seconds),
                EnemiesUltimateReadyNear: enemyCooldowns.UltimatesReady,
                EnemiesBlackKingBarReadyNear: enemyCooldowns.BlackKingBarsReady,
                SeenByEnemies: self.Vitals?.VisibleToEnemies));
    }

    // Only the sample at this second or just before: HeroPositions.At would also look 1 s ahead (the future).
    private PositionSample? PositionAt(int heroId, int seconds) =>
        positionsOf.GetValueOrDefault(heroId) is { } hero
            ? hero.At(seconds, tolerance: 0) ?? hero.At(seconds - SampleTolerance, tolerance: 0)
            : null;

    private List<PositionSample> Living(int seconds, Func<int, bool> which) =>
        teamOf.Keys.Where(which).Select(hero => PositionAt(hero, seconds)).OfType<PositionSample>().ToList();

    // A hero without the statistic (an old analysis) counts as 0, like a hero missing from the timeline.
    private int Value(int heroId, StatKey key, int seconds) => stats.ValueAt(heroId, key, seconds) ?? 0;

    private int TeamLead(Team team, StatKey key, int seconds) =>
        teamOf.Sum(hero => (hero.Value == team ? 1 : -1) * Value(hero.Key, key, seconds));

    private int TowersLost(Team owner, int seconds) =>
        replay.Objectives!.Count(objective => objective.Kind == ObjectiveKind.Tower && objective.Owner == owner && objective.Seconds <= seconds);

    // Map progress (x + y) seen from the team's own base: positive = towards the enemy.
    private static int FromOwnSide(Team team, int progress) => team == Team.Radiant ? progress : -progress;

    private static int Distance(PositionSample a, PositionSample b) =>
        (int)Math.Round(Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2)));
}
