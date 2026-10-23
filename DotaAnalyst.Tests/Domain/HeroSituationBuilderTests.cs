using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Situations;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Tests.Domain;

public class HeroSituationBuilderTests
{
    [Fact]
    public void BuybackReadsReplicatedCooldownInsteadOfAnAssumedPrice()
    {
        var activity = new ReplayActivity([], [], []) { EconomyStates = [new(1, 100, 13000, 5000, 5000, 50, 0)] };
        Assert.False(Builder([Path(1, 100)], activity: activity).At(1, 100)!.Surroundings.BuybackReady);
    }
    // Radiant: heroes 1 and 2; Dire: hero 6. Points at 0:00, 1:00, 2:00 and the end (3:00).
    private static readonly LaningStats[] Players =
    [
        new(1, Team.Radiant, Lane.Bottom, 10, 0, 1000, 1000, 0),
        new(2, Team.Radiant, Lane.Bottom, 2, 0, 800, 900, 0),
        new(6, Team.Dire, Lane.Top, 8, 0, 900, 950, 0),
    ];

    private static readonly MatchTimeline Timeline = new(
        [0, 60, 120, 180],
        [
            Hero(1, Team.Radiant, netWorth: [600, 900, 1500, 2000], experience: [0, 200, 500, 800], level: [1, 2, 4, 5], damageFromHeroes: [0, 0, 300, 400]),
            Hero(2, Team.Radiant, netWorth: [600, 700, 900, 1000], experience: [0, 150, 300, 400], level: [1, 2, 3, 3], damageFromHeroes: [0, 0, 0, 0]),
            Hero(6, Team.Dire, netWorth: [600, 1000, 1200, 1800], experience: [0, 250, 450, 700], level: [1, 3, 3, 4], damageFromHeroes: [0, 50, 100, 150]),
        ]);

    [Fact]
    public void NumbersPerMinute_AreTheLastMinuteAlreadyTurned()
    {
        var builder = Builder([Path(1, 118, 119, 120)]);

        var before = builder.At(1, 119)!;
        var at = builder.At(1, 120)!;

        Assert.Equal((900, 2), (before.NetWorth, before.Level));
        Assert.Equal((1500, 4), (at.NetWorth, at.Level));
        // Minute 1 (point 2 minus point 1) is the last minute turned at 2:00.
        Assert.Equal(300, at.DamageFromHeroesLastMinute);
        // Team leads: Radiant (1500 + 900) - Dire 1200; experience (500 + 300) - 450.
        Assert.Equal((1200, 350), (at.TeamNetWorthLead, at.TeamExperienceLead));
        Assert.Equal(4 - 3, at.LevelLeadOverEnemies);
    }

    [Fact]
    public void DeadHero_HasNoSituation_AndTheNextSampleIsNotUsed()
    {
        var builder = Builder([Path(1, 100, 112)]);

        Assert.NotNull(builder.At(1, 100));
        Assert.NotNull(builder.At(1, 101));
        // Only a sample 1 s later: that is the future, so no situation.
        Assert.Null(builder.At(1, 111));
        Assert.Null(builder.At(1, 105));
        Assert.Null(builder.At(99, 100));
    }

    [Fact]
    public void AlliesAndEnemies_AreCountedByDistance_TheEnemiesKeptApart()
    {
        var builder = Builder(
        [
            Path(1, (100, 0, 0)),
            Path(2, (100, 1200, 0)),
            Path(6, (100, 0, 2000)),
        ]);

        var situation = builder.At(1, 100)!;

        Assert.Equal((1200, 1, 1, 1, 1), (situation.NearestAllyDistance, situation.AlliesNear, situation.AlliesWithinFightRange, situation.AlliesAlive, situation.EnemiesAlive));
        // Hero 1 stands in the middle lane, the enemy is not there; no enemy ward sees it.
        Assert.Equal(new HiddenEnemies(2000, EnemiesNear: 0, EnemiesWithinFightRange: 1, EnemiesInMyLane: 0, InEnemyObserverVision: false),
            situation.Hidden);
    }

    [Fact]
    public void Alone_HasNoNearestAlly()
    {
        var situation = Builder([Path(1, (100, 0, 0))]).At(1, 100)!;

        Assert.Null(situation.NearestAllyDistance);
        Assert.Equal((0, 0), (situation.AlliesAlive, situation.EnemiesAlive));
        Assert.Null(situation.Hidden.NearestEnemyDistance);
    }

    [Fact]
    public void Observers_CountOnlyTheTeamsOnTheMapAtThatSecond()
    {
        WardPlacement Ward(Team team, WardKind kind, int placedAt, int? removedAt) =>
            new(kind, team, placedByHeroId: null, 0, 0, placedAt, removedAt, destroyed: removedAt is not null, destroyedByHeroId: null);

        var builder = Builder([Path(1, 100)], wards:
        [
            Ward(Team.Radiant, WardKind.Observer, 10, null),
            Ward(Team.Radiant, WardKind.Observer, 10, 100), // destroyed at that second: gone
            Ward(Team.Radiant, WardKind.Observer, 101, null), // placed after
            Ward(Team.Radiant, WardKind.Sentry, 10, null),
            Ward(Team.Dire, WardKind.Observer, 10, null),
        ]);

        Assert.Equal(1, builder.At(1, 100)!.TeamObserversActive);
    }

    [Fact]
    public void TowersAndDeaths_CountOnlyUpToThatSecond()
    {
        var objectives = new[]
        {
            new MapObjective(ObjectiveKind.Tower, 50, Team.Dire, null, owner: Team.Radiant, towerTier: 1, lane: Lane.Bottom),
            new MapObjective(ObjectiveKind.Tower, 150, Team.Radiant, null, owner: Team.Dire, towerTier: 1, lane: Lane.Top),
        };
        var deaths = new[]
        {
            new HeroDeath(1, 40, 0, 0, KillerKind.Hero, 6, reincarnated: false),
            new HeroDeath(1, 90, 0, 0, KillerKind.Hero, 6, reincarnated: true), // Aegis: not a death
            new HeroDeath(1, 130, 0, 0, KillerKind.Hero, 6, reincarnated: false),
        };
        var builder = Builder([Path(1, 100)], objectives: objectives, deaths: deaths);

        var situation = builder.At(1, 100)!;

        Assert.Equal((1, 0), (situation.OwnTowersLost, situation.EnemyTowersLost));
        Assert.Equal(new LaneTowers(Top: 0, Middle: 0, Bottom: 1), situation.Map.OwnTowersLost);
        // The Dire top tower falls at 150: after the second.
        Assert.Equal(new LaneTowers(0, 0, 0), situation.Map.EnemyTowersLost);
        Assert.Equal(60, situation.SecondsSinceLastDeath);
    }

    [Fact]
    public void NeverDied_HasNoSecondsSinceLastDeath()
    {
        Assert.Null(Builder([Path(1, 100)]).At(1, 100)!.SecondsSinceLastDeath);
    }

    [Fact]
    public void Role_IsTheRoleQueuesPosition_OrEstimatedAtThatSecond()
    {
        static HeroMetrics Metrics(bool inferred) =>
            new(1, Team.Radiant, Role: 3, 0, 0, 0, 0, 0, null, null, 0, 0, 0, 0, null, 0, 0, RoleInferred: inferred);

        var roleQueue = Builder([Path(1, 100)], metrics: [Metrics(inferred: false)]).At(1, 100)!;
        // The 10:00 net worth role of a pro match is not used: it is the future at 1:40.
        var pro = Builder([Path(1, 100)], metrics: [Metrics(inferred: true)]).At(1, 100)!;
        var turbo = Builder([Path(1, 100)]).At(1, 100)!;

        Assert.Equal((3, false, (double?)null), (roleQueue.Role, roleQueue.RoleInferred, roleQueue.RoleConfidence));
        Assert.All(new[] { pro, turbo }, situation =>
        {
            Assert.InRange(situation.Role!.Value, 1, 5);
            Assert.True(situation.RoleInferred);
            Assert.NotNull(situation.RoleConfidence);
        });
    }

    [Fact]
    public void Region_AndEnemySide_ComeFromTheMap()
    {
        var situation = Builder([Path(1, (100, 5000, 5000))]).At(1, 100)!;

        Assert.Equal(MapRegions.RegionOf(Team.Radiant, 5000, 5000), situation.Region);
        Assert.True(situation.OnEnemySide);
    }

    [Fact]
    public void RecentActivity_CountsOnlyUsesUpToThatSecond()
    {
        var activity = new ReplayActivity(
            smokes:
            [
                new SmokeUse(2, Team.Radiant, 70.5),
                new SmokeUse(6, Team.Dire, 90),
                new SmokeUse(6, Team.Dire, 100.4), // after the second 100: the future
            ],
            itemUses:
            [
                new RecordedUse(1, 30, "item_tpscroll"),
                new RecordedUse(1, 85.2, "item_blink"),
                new RecordedUse(1, 95, "item_magic_wand"),
                new RecordedUse(1, 100.1, "item_black_king_bar"), // future
                new RecordedUse(2, 99, "item_tpscroll"),          // another hero
            ],
            abilityUses: [new RecordedUse(1, 60, "lina_laguna_blade"), new RecordedUse(1, 75, "lina_dragon_slave")]);

        var situation = Builder([Path(1, 100)], activity: activity).At(1, 100)!;

        Assert.Equal(new RecentActivity(OwnSmokeSecondsAgo: 29, TeleportSecondsAgo: 70, EscapeItemSecondsAgo: 14,
            ItemUsesLast30Seconds: 2, AbilityUsesLast30Seconds: 1), situation.Recent);
        Assert.Equal(10, situation.Hidden.EnemySmokeSecondsAgo);
    }

    [Fact]
    public void RecentHits_UseOnlyBucketsThatEndedByThatSecond()
    {
        var activity = new ReplayActivity([], [], [], damageBuckets:
        [
            new DamageBucket(1, 92, ToHeroes: 999, FromHeroes: 999), // before the 6 s window
            new DamageBucket(1, 94, ToHeroes: 10, FromHeroes: 100),
            new DamageBucket(1, 98, ToHeroes: 20, FromHeroes: 200),  // ends at 100: still the past
            new DamageBucket(1, 100, ToHeroes: 999, FromHeroes: 999), // [100, 102): the future
            new DamageBucket(2, 96, ToHeroes: 999, FromHeroes: 999),  // another hero
        ]);

        var recent = Builder([Path(1, 100)], activity: activity).At(1, 100)!.Recent;

        Assert.Equal((300, 30), (recent.DamageFromHeroesRecently, recent.DamageToHeroesRecently));
    }

    [Fact]
    public void Readiness_ComesFromTheSlotsAndTheCooldownsUpToThatSecond()
    {
        var activity = new ReplayActivity([], [], [],
            slotChanges:
            [
                new SlotChange(1, 0, SlotChange.TeleportSlot, "item_tpscroll"),
                new SlotChange(1, 40, 0, "item_blink"),
                new SlotChange(1, 40, 1, "item_black_king_bar"),
                new SlotChange(1, 120, 1, null), // later: the future
            ],
            itemCooldowns:
            [
                new CooldownStart(1, 90, "item_tpscroll", 170),  // teleport on cooldown at 100
                new CooldownStart(1, 95, "item_blink", 110),     // blink too, but the BKB is ready
            ],
            abilityCooldowns:
            [
                new CooldownStart(1, 20, "lina_laguna_blade", 90),  // the longest: the ultimate; ready again at 90
                new CooldownStart(1, 98, "lina_dragon_slave", 106),
                new CooldownStart(1, 99.5, "lina_light_strike_array", 106),
                new CooldownStart(1, 100.5, "lina_laguna_blade", 170), // after the second 100
            ]);

        var readiness = Builder([Path(1, 100)], activity: activity).At(1, 100)!.Readiness;

        Assert.Equal(new Readiness(TeleportReady: false, HasEscapeItem: true, EscapeItemReady: true, UltimateReady: null,
            AbilitiesOnCooldown: 2), readiness);
    }

    [Fact]
    public void Readiness_IsUnknownWithoutInventories_AndTheUltimateWithoutCatalog()
    {
        var noInventories = Builder([Path(1, 100)], activity: new ReplayActivity([], [], [])).At(1, 100)!.Readiness;
        var notUsedYet = Builder([Path(1, 100)], activity: new ReplayActivity([], [], [], slotChanges: [], itemCooldowns: [],
            abilityCooldowns: [])).At(1, 100)!.Readiness;

        Assert.Equal(Readiness.Unknown, noInventories);
        Assert.Null(notUsedYet.UltimateReady);
        Assert.Equal((false, false, 0), (notUsedYet.TeleportReady, notUsedYet.HasEscapeItem, notUsedYet.AbilitiesOnCooldown));
    }

    [Theory]
    [InlineData(49, false, null, "normal")]
    [InlineData(50, false, true, "normal")]
    [InlineData(100, false, true, "normal")]
    [InlineData(100, true, null, "normal")]
    [InlineData(100, false, false, "cooling")]
    [InlineData(100, false, null, "partial")]
    [InlineData(100, false, null, "passive")]
    public void UltimateReadiness_UsesLearnedCatalogClassification(int seconds, bool variant, bool? expected, string mode)
    {
        static AbilityDetails Ability(string name, bool ultimate) => new(name, name, null, ["No Target"],
            AbilityTargetTeam.None, [], AbilityDamageType.Magical, null, Dispellable.No, [10], [], [],
            Profile: new AbilityProfile([], [], SpellImmunity.Unknown, ultimate, false, 3,
                false, false, false, false, [], [], [], [], [], [], [], null, null, [], null, null));
        var catalog = new GameCatalog(Abilities: new Dictionary<string, AbilityDetails>
        {
            ["ultimate"] = Ability("ultimate", true),
            ["variant"] = Ability("variant", true),
            ["ordinary"] = Ability("ordinary", false),
        });
        var upgrades = new List<AbilityUpgrade> { new(1, 0, "ordinary", 1), new(1, 50, "ultimate", 1) };
        if (variant) upgrades.Add(new(1, 60, "variant", 1));
        if (mode == "partial") upgrades.Add(new(1, 60, "unknown_variant", 1));
        if (mode == "passive") catalog = catalog with
        {
            Abilities = new Dictionary<string, AbilityDetails>(catalog.Abilities!)
            {
                ["ultimate"] = Ability("ultimate", true) with { Behavior = ["Passive"] },
            },
        };
        var replay = new ReplayAnalysis(Match(), 180, Players, Timeline, [], [], objectives: [], abilityUpgrades: upgrades);
        var cooldowns = new List<CooldownStart> { new(1, 20, "ordinary", 170) };
        if (mode == "cooling") cooldowns.Add(new(1, 90, "ultimate", 120));
        var activity = new ReplayActivity([], [], [], slotChanges: [], itemCooldowns: [], abilityCooldowns: cooldowns);
        var readiness = HeroSituationBuilder.From(replay, [Path(1, seconds)], activity, catalog)!.At(1, seconds)!.Readiness;

        Assert.Equal(expected, readiness.UltimateReady);
    }

    [Fact]
    public void BuybackGlyphScanAndRunes_CountWithTheirOwnWindows()
    {
        var activity = new ReplayActivity([], [], [],
            buybacks: [new BuybackUse(1, 50)],                                   // 450 s ago at 500: no buyback yet
            scans: [new TeamAction(Team.Radiant, 470), new TeamAction(Team.Dire, 495), new TeamAction(Team.Dire, 501)],
            glyphs: [new TeamAction(Team.Radiant, 150)],                          // 350 s ago: back already
            runes: [new RunePickup(1, Team.Radiant, 480, RuneType: 0), new RunePickup(1, Team.Radiant, 490, RuneType: 5),
                    new RunePickup(6, Team.Dire, 460, RuneType: 1)]);

        var situation = Builder([Path(1, 500)], activity: activity).At(1, 500)!;

        Assert.Equal((450, (int?)null, 30, 20), (situation.Recent.OwnBuybackSecondsAgo, situation.Recent.OwnGlyphSecondsAgo,
            situation.Recent.OwnScanSecondsAgo, situation.Recent.OwnPowerRuneSecondsAgo));
        // The enemy scan at 501 is after the second; the bounty rune (5) is not a power rune.
        Assert.Equal((5, 40), (situation.Hidden.EnemyScanSecondsAgo, situation.Hidden.EnemyPowerRuneSecondsAgo));
    }

    [Fact]
    public void Aegis_IsHeldForFiveMinutes_UnlessItsHolderReincarnated()
    {
        var roshan = new MapObjective(ObjectiveKind.Roshan, 100, Team.Dire, killerHeroId: 6, aegisHeroId: 6);
        var held = Builder([Path(1, 150)], objectives: [roshan]).At(1, 150)!.Map;
        var used = Builder([Path(1, 150)], objectives: [roshan],
            deaths: [new HeroDeath(6, 140, 0, 0, KillerKind.Hero, 1, reincarnated: true)]).At(1, 150)!.Map;
        var expired = Builder([Path(1, 450)], objectives: [roshan]).At(1, 450)!.Map;

        Assert.Equal((50, false, false, true), (held.SecondsSinceRoshanKill, held.HasAegis, held.AllyHasAegis, held.EnemyHasAegis));
        Assert.False(used.EnemyHasAegis);
        Assert.Equal((350, false), (expired.SecondsSinceRoshanKill, expired.EnemyHasAegis));
    }

    [Fact]
    public void RecentHits_AreUnknownWithoutBuckets()
    {
        var recent = Builder([Path(1, 100)], activity: new ReplayActivity([], [], [])).At(1, 100)!.Recent;

        Assert.Null(recent.DamageFromHeroesRecently);
    }

    [Fact]
    public void RecentActivity_IsUnknownWithoutTheReplaysActivity_AndForgetsOldUses()
    {
        var unknown = Builder([Path(1, 100)]);
        var old = Builder([Path(1, 300)], activity: new ReplayActivity([], [new RecordedUse(1, 100, "item_tpscroll")], []));

        Assert.False(unknown.HasActivity);
        Assert.Equal(RecentActivity.Unknown, unknown.At(1, 100)!.Recent);
        Assert.Null(unknown.At(1, 100)!.Hidden.EnemySmokeSecondsAgo);
        // 200 s ago: older than the recent window.
        Assert.Null(old.At(1, 300)!.Recent.TeleportSecondsAgo);
    }

    [Fact]
    public void Condition_ComesFromTheVitals_TheEnemiesHealthStaysHidden()
    {
        static HeroPositions WithVitals(int heroId, int x, int health, int maxHealth) =>
            new(heroId, [new PositionSample(100, x, 0, new HeroVitals(health, maxHealth, 100, 400))]);

        var builder = Builder(
        [
            WithVitals(1, 0, 300, 1000),
            WithVitals(2, 2000, 450, 900),   // ally within fight range
            WithVitals(6, 1000, 800, 1000),  // enemy within fight range
        ]);

        var situation = builder.At(1, 100)!;

        Assert.Equal(new Condition(300, 0.3, 0.25, 0.5), situation.Condition);
        Assert.Equal(0.8, situation.Hidden.EnemiesHealthShare);
    }

    [Fact]
    public void GoldStatesAndTheEnemiesTheTeamSaw_ComeFromTheVitals()
    {
        static PositionSample At(int x, bool? seen, int state = 0) =>
            new(100, x, 0, new HeroVitals(500, 1000, 100, 400, VisibleToEnemies: seen, UnitStateMask: state, Gold: 2500));

        // Radiant hero 1 (stunned and invisible), Dire hero 6 seen by Radiant at 1000.
        var situation = Builder([new HeroPositions(1, [At(0, seen: false, state: (1 << 5) | (1 << 7))]),
            new HeroPositions(6, [At(1000, seen: true)])]).At(1, 100)!;

        Assert.Equal((2500, true, false, true), (situation.Condition.Gold, situation.Condition.Stunned,
            situation.Condition.Silenced, situation.Condition.Invisible));
        Assert.Equal((1, 1, 1, 1000), (situation.Condition.VisibleEnemies, situation.Condition.VisibleEnemiesNear,
            situation.Condition.VisibleEnemiesWithinFightRange, situation.Condition.NearestVisibleEnemyDistance));
        Assert.False(situation.Hidden.SeenByEnemies);

        // The same enemy in the fog: no visible enemy, while the hidden distance still knows it.
        var fogged = Builder([new HeroPositions(1, [At(0, seen: false)]), new HeroPositions(6, [At(1000, seen: false)])]).At(1, 100)!;
        Assert.Equal((0, (int?)null, 1000), (fogged.Condition.VisibleEnemies, fogged.Condition.NearestVisibleEnemyDistance,
            fogged.Hidden.NearestEnemyDistance));
    }

    [Fact]
    public void Waves_SayHowFarPastItsOwnCreepsTheHeroStands_FromEachTeamsSide()
    {
        var activity = new ReplayActivity([], [], [], waves:
        [
            new WaveFront(Lane.Middle, 100, Radiant: -1000, Dire: 600, new WaveCounts(7, 5, 4, 3)),
            new WaveFront(Lane.Top, 98, Radiant: -4000, Dire: null), // the reading 2 s before
        ]);
        // Radiant hero 1 and Dire hero 6, both in the middle lane at x + y = 0.
        var builder = Builder([Path(1, (100, 0, 0)), Path(6, (100, 0, 0))], activity: activity);

        var radiant = builder.At(1, 100)!;
        var dire = builder.At(6, 100)!;

        // Radiant: own creeps reach -1000, the hero is at 0: 1000 past them; the Dire creeps are 600 ahead.
        Assert.Equal(new WavePosition(AheadOfOwnWave: 1000, OwnFrontTop: -4000, OwnFrontMiddle: -1000, OwnFrontBottom: null,
                OwnCreepsInLane: 7, OwnCreepsAtFront: 4, SeenEnemiesInMyLane: null, ClashPoint: -200,
                // 7 creeps at 1:40, one wave being 4: two waves merged.
                OwnWaveStacked: true),
            radiant.Waves);
        Assert.Equal(600, radiant.Hidden.EnemyWaveAhead);
        Assert.Equal((5, 3), (dire.Waves.OwnCreepsInLane, radiant.Hidden.EnemyCreepsAtFront));
        Assert.Equal(4, dire.Hidden.EnemyCreepsAtFront);
        // Dire, from its own side: its creeps reach x + y = 600 (-600 from its side); the hero at 0 is 600 past them.
        Assert.Equal((600, -600), (dire.Waves.AheadOfOwnWave, dire.Waves.OwnFrontMiddle));
        Assert.Equal(1000, dire.Hidden.EnemyWaveAhead);
    }

    [Fact]
    public void FightFarmNightAndBody_ComeFromTheNewSections_OnlyUpToThatSecond()
    {
        var activity = new ReplayActivity([], [], [],
            dayNight: [new DayNightChange(0, IsDay: true), new DayNightChange(90, IsDay: false), new DayNightChange(101, IsDay: true)],
            heroHits:
            [
                new HeroHit(90, AttackerHeroId: 1, TargetHeroId: 6, Amount: 80),   // hero 1 strikes first (in the last 10 s)
                new HeroHit(94, AttackerHeroId: 6, TargetHeroId: 1, Amount: 120),
                new HeroHit(96, AttackerHeroId: 1, TargetHeroId: 6, Amount: 40),
                new HeroHit(100, AttackerHeroId: 6, TargetHeroId: 1, Amount: 999), // [100, 102): the future
            ],
            creepKills:
            [
                new CreepKill(1, 30, CreepKillKind.Lane, 0, 0),     // more than a minute ago
                new CreepKill(1, 50, CreepKillKind.Lane, 0, 0),
                new CreepKill(1, 70, CreepKillKind.Deny, 0, 0),
                new CreepKill(1, 85.5, CreepKillKind.Neutral, 0, 0),
                new CreepKill(1, 100.2, CreepKillKind.Lane, 0, 0),  // after the second
                new CreepKill(6, 99, CreepKillKind.Lane, 0, 0),     // another hero
            ]);
        var hero = new HeroPositions(1, [new PositionSample(100, 0, 0,
            Stats: new HeroCombatStats(Armor: 1, MagicResistance: 25, MoveSpeed: 300, DamageMin: 50, DamageMax: 54,
                Strength: 20, Agility: 18, Intellect: 15))]);

        var situation = Builder([hero], activity: activity).At(1, 100)!;

        Assert.Equal(new Fight(EnemiesHittingMe: 1, EnemiesIHit: 1, IStruckFirst: true), situation.Fight);
        Assert.Equal(new Farm(LaneLastHits: 1, Neutrals: 1, Denies: 1, SecondsSinceLastCreepKill: 14), situation.Farm);
        Assert.True(situation.Map.IsNight);
        Assert.Equal((1 + 18 / 6.0, 52.0, 300), (situation.Body.EstimatedArmor!.Value, situation.Body.AverageDamage!.Value,
            situation.Body.MoveSpeed!.Value));
    }

    [Fact]
    public void Intent_AddsTheOrdersOfTheLastTenSeconds_AndReadsTheLastMoveDestination()
    {
        static PositionSample At(int seconds, HeroOrders orders) => new(seconds, 0, 0, Orders: orders);

        var hero = new HeroPositions(1,
        [
            At(90, new HeroOrders(9000, 9000, Moves: 50, Attacks: 50, Casts: 50)), // 10 s before: outside the window
            At(92, new HeroOrders(3000, 1000, Moves: 3, Attacks: 0, Casts: 1)),
            At(96, new HeroOrders(null, null, Moves: 0, Attacks: 2, Casts: 0)),
            At(100, new HeroOrders(null, null, Moves: 1, Attacks: 0, Casts: 0)),
        ]);

        var intent = Builder([hero]).At(1, 100)!.Intent;

        // The last destination is (3000, 1000) for a Radiant hero at (0, 0): 3162 away, 4000 forward, on the enemy side.
        Assert.Equal(new Intent(Moves: 4, Attacks: 2, Casts: 1, DestinationDistance: 3162, DestinationAdvance: 4000,
            DestinationOnEnemySide: true), intent);
    }

    [Fact]
    public void MissingEnemies_AreTheOnesUnseenForTenSeconds_WithTheNearestLastSighting()
    {
        static PositionSample Seen(int seconds, int x, bool seen) =>
            new(seconds, x, 0, new HeroVitals(500, 1000, 100, 400, VisibleToEnemies: seen));

        var radiant = new HeroPositions(1, [Seen(100, 0, seen: true)]);
        var radiantAlly = new HeroPositions(2, [Seen(100, 0, seen: true)]);
        // Dire hero 6: seen at 80 (at x = 1500), unseen since 82 -> missing for 20 s at 100.
        var missing = new HeroPositions(6, Enumerable.Range(40, 31).Select(i => Seen(i * 2, 1500, seen: i * 2 <= 80)).ToList());

        var situation = Builder([radiant, radiantAlly, missing]).At(1, 100)!;

        Assert.Equal(new MissingEnemies(Count: 1, NearestLastSeenDistance: 1500, NearestLastSeenSecondsAgo: 20), situation.Missing);
    }

    [Fact]
    public void AnEnemyJustOutOfSight_IsNotMissing_AndNoVisibilityIsUnknown()
    {
        static PositionSample Seen(int seconds, bool seen) => new(seconds, 0, 0, new HeroVitals(500, 1000, 100, 400, VisibleToEnemies: seen));

        var recent = Builder([new HeroPositions(1, [Seen(100, true)]),
            new HeroPositions(6, [Seen(94, true), Seen(96, false), Seen(98, false), Seen(100, false)])]).At(1, 100)!;
        var unknown = Builder([Path(1, 100), Path(6, 100)]).At(1, 100)!;

        Assert.Equal(new MissingEnemies(0, null, null), recent.Missing);
        Assert.Equal(MissingEnemies.Unknown, unknown.Missing);
    }

    [Fact]
    public void Afflictions_CountWhatIsOnTheHeroNow_AndTheUnionOfDisablesInTheLastTenSeconds()
    {
        var activity = new ReplayActivity([], [], [], debuffs:
        [
            new Debuff(80, 85, 6, 1, "modifier_stunned", DebuffKind.Stun),                  // before the window
            new Debuff(92, 95, 6, 1, "modifier_lion_impale", DebuffKind.Stun),             // 3 s
            new Debuff(94, 96, 6, 1, "modifier_silence", DebuffKind.Silence),              // overlaps: +1 s
            new Debuff(98, 104, 6, 1, "modifier_bashed", DebuffKind.Stun),                 // still on: cut at 100 -> +2 s
            new Debuff(97, 103, 6, 1, "modifier_slow", DebuffKind.Slow),
            new Debuff(99, null, 6, 1, "modifier_radiance", DebuffKind.DamageOverTime),     // no end: not active at 100
            new Debuff(101, 105, 6, 1, "modifier_hex", DebuffKind.Hex),                     // the future
            new Debuff(95, 99, 6, 2, "modifier_stunned", DebuffKind.Stun),                  // another hero
        ]);

        var afflictions = Builder([Path(1, 100)], activity: activity).At(1, 100)!.Afflictions;

        Assert.Equal(new Afflictions(Disables: 1, Slows: 1, Others: 0, DisabledSeconds: 6, EnemiesAfflicting: 1), afflictions);
    }

    [Theory]
    [InlineData("stun", "modifier_whatever", DebuffKind.Stun)]
    [InlineData("other", "modifier_orb_of_corrosion_debuff", DebuffKind.ArmorReduction)]
    [InlineData("other", "modifier_item_radiance_debuff", DebuffKind.DamageOverTime)]
    [InlineData("other", "modifier_storm_spirit_overload_debuff", DebuffKind.Slow)]
    [InlineData("other", "modifier_sheepstick_debuff", DebuffKind.Hex)]
    [InlineData("other", "modifier_unknown_thing", DebuffKind.Other)]
    public void DebuffKinds_RefineTheParsersOtherByName(string parserKind, string modifier, DebuffKind expected)
    {
        Assert.Equal(expected, DebuffKinds.Of(parserKind, modifier));
    }

    [Fact]
    public void Boons_ReadTheBuffsOnTheHeroNow_AndTheAlliesThatBuffedItRecently()
    {
        var activity = new ReplayActivity([], [], [], buffs:
        [
            new Buff(98, 101, 1, 1, "modifier_teleporting", BuffKind.Teleport),
            new Buff(99, 105, 2, 1, "modifier_item_glimmer_cape_fade", BuffKind.Save),       // an ally's save, still on
            new Buff(95, 120, 1, 1, "modifier_flask_healing", BuffKind.Heal),
            new Buff(96, 99, 3, 1, "modifier_mekansm_heal", BuffKind.Heal),                  // over: counts only as an ally
            new Buff(85, 90, 4, 1, "modifier_item_phase_boots_active", BuffKind.Speed),     // before the window
            new Buff(101, 108, 1, 1, "modifier_black_king_bar_immune", BuffKind.MagicImmune), // the future
            new Buff(99, 104, 1, 2, "modifier_teleporting", BuffKind.Teleport),             // another hero
        ]);

        var boons = Builder([Path(1, 100)], activity: activity).At(1, 100)!.Boons;
        var unknown = Builder([Path(1, 100)], activity: new ReplayActivity([], [], [])).At(1, 100)!.Boons;

        Assert.Equal(new Boons(Teleporting: true, MagicImmune: false, Saved: true, Heals: 1, SpeedBuffs: 0, AlliesBuffing: 2), boons);
        Assert.Equal(Boons.Unknown, unknown);
    }

    [Fact]
    public void Waves_SayWhereTheLaneIsAgainstTheTowers_AndWhereItIsGoing()
    {
        static BuildingHealth MidTower(string name, Team team, int x, int y) =>
            new(name, team, BuildingKind.Tower, Lane.Middle, 1, x, y, [new BuildingHealthReading(0, 1800, 1800)]);
        var activity = new ReplayActivity([], [], [],
            waves:
            [
                new WaveFront(Lane.Middle, 70, Radiant: -1600, Dire: 900),
                new WaveFront(Lane.Middle, 90, Radiant: -1300, Dire: 700),
                new WaveFront(Lane.Middle, 100, Radiant: -1000, Dire: 600),
            ],
            buildings:
            [
                MidTower("npc_dota_goodguys_tower1_mid", Team.Radiant, -1544, -1408), // progress -2952
                MidTower("npc_dota_badguys_tower1_mid", Team.Dire, 524, 652),         // progress 1176
            ]);

        var situation = Builder([Path(1, (100, 0, 0))], activity: activity).At(1, 100)!;
        var waves = situation.Waves;

        // The whole map: only the middle lane has waves, neither is at a tower, and the next waves spawn at 2:00.
        Assert.Equal(new MapWaves(null, -200, null, null, 600, null, EnemyWavesAtOwnTowers: 0, OwnWavesAtEnemyTowers: 0,
            SecondsToNextWave: 20), situation.MapWaves);

        // The waves meet at (-1000 + 600) / 2 = -200: 2,752 past the Radiant tower, 1,376 short of the Dire one; the
        // Radiant front moved 300 in 10 s and 600 in 30 s (pushing).
        Assert.Equal((-200, 2752, 1376, 300, 600),
            (waves.ClashPoint, waves.ClashPastOwnTower, waves.ClashShortOfEnemyTower, waves.OwnFrontChangeLast10, waves.OwnFrontChangeLast30));
    }

    [Fact]
    public void Waves_ReadTheWavesStrength_ThePulledCreeps_AndWhoKilledTheLanesCreeps()
    {
        var activity = new ReplayActivity([], [], [],
            waves:
            [
                new WaveFront(Lane.Middle, 100, Radiant: -1000, Dire: 600,
                    Strength: new WaveStrength(2400, 1900, 1500, 1100, RadiantSiege: true, DireSiege: false, -1100, 700)),
            ],
            offLaneCreeps: [new OffLaneCreeps(98, RadiantCount: 2, DireCount: 0, -3000, -500, null, null)],
            laneCreepDeaths:
            [
                new LaneCreepDeath(80.5, Lane.Middle, Team.Dire, CreepKiller.Hero, 1, 300, 300),    // the team farming
                new LaneCreepDeath(90, Lane.Middle, Team.Dire, CreepKiller.Hero, 2, 300, 300),
                new LaneCreepDeath(91, Lane.Middle, Team.Radiant, CreepKiller.Hero, 6, -900, -900), // an enemy hero
                new LaneCreepDeath(95, Lane.Middle, Team.Radiant, CreepKiller.Tower, null, 0, 0),   // the Dire tower
                new LaneCreepDeath(96, Lane.Middle, Team.Radiant, CreepKiller.Hero, 1, 0, 0),       // a deny: not enemy
                new LaneCreepDeath(60, Lane.Middle, Team.Dire, CreepKiller.Hero, 1, 0, 0),          // more than 30 s ago
                new LaneCreepDeath(99, Lane.Top, Team.Dire, CreepKiller.Hero, 1, 0, 0),             // another lane
                new LaneCreepDeath(101, Lane.Middle, Team.Dire, CreepKiller.Hero, 1, 0, 0),         // the future
            ]);

        var situation = Builder([Path(1, (100, 0, 0))], activity: activity).At(1, 100)!;
        var waves = situation.Waves;

        Assert.Equal((1500, true, 2), (waves.OwnHealthAtFront, waves.OwnSiegeInLane, waves.OwnCreepsOffLane));
        Assert.Equal((2, 1, 1, 0), (waves.EnemyCreepsKilledByTeam, waves.OwnCreepsKilledByEnemyHeroes,
            waves.OwnCreepsKilledByEnemyTower, waves.EnemyCreepsKilledByOwnTower));
        Assert.Equal(400, situation.Hidden.WaveHealthBalanceAtFront);
    }

    [Fact]
    public void Surroundings_ReadMovementBuybackSmokeAndWardVision()
    {
        static PositionSample At(int seconds, int x, int y, int gold) =>
            new(seconds, x, y, new HeroVitals(500, 1000, 100, 400, VisibleToEnemies: true, Gold: gold));
        var activity = new ReplayActivity([], [], [], buybacks: [], buffs:
        [
            new Buff(95, 130, 1, 1, "modifier_smoke_of_deceit", BuffKind.Smoke),
            new Buff(95, 130, 1, 2, "modifier_smoke_of_deceit", BuffKind.Smoke),
        ]);
        WardPlacement[] wards =
        [
            new(WardKind.Observer, Team.Radiant, 2, 500, 500, 50, null, false, null),
            new(WardKind.Observer, Team.Dire, 6, 1000, 1000, 60, 99, true, 1), // gone at 99
            new(WardKind.Observer, Team.Dire, 6, -900, 900, 70, null, false, null),
        ];
        // Radiant hero 1 walks from (0, 0) to (300, 300) in 2 s; ally 2 next to it; Dire hero 6 with no gold.
        var situation = Builder(
        [
            new HeroPositions(1, [At(98, 0, 0, 3000), At(100, 300, 300, 3000)]),
            new HeroPositions(2, [At(100, 100, 100, 0)]),
            new HeroPositions(6, [At(100, 3000, 3000, 0)]),
        ], wards: wards, activity: activity).At(1, 100)!;

        // 424 units in 2 s; x + y went from 0 to 600. No replicated price: gold alone cannot certify buyback.
        Assert.Equal(new Surroundings(MoveSpeed: 212, AdvanceSpeed: 300, BuybackReady: null, UnderSmoke: true, AlliesUnderSmoke: 1,
            InOwnObserverVision: true), situation.Surroundings);
        Assert.Equal((true, 0, (int?)null), (situation.Hidden.InEnemyObserverVision, situation.Hidden.EnemiesUnderSmoke,
            situation.Hidden.EnemiesWithBuyback));
        // The Radiant ward at (500, 500) stands on the river: part of it seen, no jungle; nothing without wards.
        Assert.True(situation.Vision.River > 0);
        Assert.Equal(new VisionCoverage(0, 0, 0, 0), Builder([Path(1, 100)]).At(1, 100)!.Vision);
    }

    [Fact]
    public void Waves_ReadLastHitsOnOffer_SuperCreeps_CreepAggro_AndTheJungleCamps()
    {
        var activity = new ReplayActivity([], [], [],
            waves:
            [
                new WaveFront(Lane.Middle, 100, Radiant: -1000, Dire: 600,
                    Composition: new WaveComposition(new TeamWave(3, 1, 0, 1, 2, 0, LowAtFront: 1), new TeamWave(4, 2, 1, 1, 0, 0, LowAtFront: 3))),
            ],
            creepHeroHits:
            [
                new CreepHeroHits(92, Lane.Middle, Team.Dire, 1, 3),
                new CreepHeroHits(100, Lane.Middle, Team.Dire, 1, 2),
                new CreepHeroHits(88, Lane.Middle, Team.Dire, 1, 9),  // more than 10 s ago
                new CreepHeroHits(102, Lane.Middle, Team.Dire, 1, 9), // the future
            ],
            neutralCamps:
            [
                // Radiant jungle (its side of the river): stacked now (6 against a usual 3) and an empty one.
                new NeutralCamp("a", -4000, 500, "large", [(30, 3), (60, 3), (90, 6)]),
                new NeutralCamp("b", -1454, -3356, "large", [(30, 2), (95, 0)]), // a real camp position (W3)
                // Dire jungle: stacked.
                new NeutralCamp("c", 4000, -300, "medium", [(30, 2), (60, 2), (99, 4)]),
            ]);

        var situation = Builder([Path(1, (100, 0, 0))], activity: activity).At(1, 100)!;

        Assert.Equal((3, 1, true, 5), (situation.Waves.LastHitsAvailable, situation.Waves.DeniesAvailable,
            situation.Waves.OwnSuperCreeps, situation.Waves.CreepHitsOnMeLast10));
        Assert.Equal((1, 1), (situation.Farm.OwnCampsStacked, situation.Farm.OwnCampsEmpty));
        Assert.Equal(1, situation.Hidden.EnemyCampsStacked);
    }

    [Fact]
    public void Signals_ReadIncomeBySource_Pings_SpellsTraded_AndTheRuneClock()
    {
        var activity = new ReplayActivity([], [],
            abilityUses:
            [
                new RecordedUse(6, 95, "lion_impale", TargetHeroId: 1),
                new RecordedUse(1, 97, "lich_frost_nova", TargetHeroId: 6),
                new RecordedUse(1, 98, "lich_frost_shield", TargetHeroId: 2), // an ally
                new RecordedUse(6, 80, "lion_finger", TargetHeroId: 1),       // more than 10 s ago
            ],
            economy:
            [
                new EconomyBucket(1, 50, EconomyResource.Gold, EconomySource.Creeps, 120),
                new EconomyBucket(1, 90, EconomyResource.Gold, EconomySource.Creeps, 40),
                new EconomyBucket(1, 96, EconomyResource.Gold, EconomySource.Heroes, 300),
                new EconomyBucket(1, 98, EconomyResource.Gold, EconomySource.Creeps, 999), // ends at 100: counts
                new EconomyBucket(1, 99, EconomyResource.Gold, EconomySource.Creeps, 999), // ends after 100: the future
                new EconomyBucket(1, 30, EconomyResource.Gold, EconomySource.Creeps, 999), // more than a minute before
                new EconomyBucket(1, 60, EconomyResource.Experience, EconomySource.Creeps, 200),
            ],
            pings:
            [
                new MapPing(2, Team.Radiant, 95, 500, 0, 0),    // an ally, near
                new MapPing(2, Team.Radiant, 96, 6000, 6000, 0), // an ally, far
                new MapPing(1, Team.Radiant, 99, 0, 0, 0),       // its own
                new MapPing(6, Team.Dire, 99, 0, 0, 0),          // an enemy's
            ],
            runesOnMap: [new RuneOnMap(90, 0, -1700, 1100, true), new RuneOnMap(92, 5, 3000, 3000, true)]);

        var situation = Builder([Path(1, (100, 0, 0))], activity: activity).At(1, 100)!;

        // 1:40: the first power rune spawns at 6:00.
        Assert.Equal(new Signals(GoldFromCreeps: 1159, GoldFromNeutrals: 0, GoldFromHeroes: 300, GoldFromObjectives: 0,
            ExperienceGained: 200, AllyPingsNear: 1, OwnPings: 1, EnemySpellsOnMe: 1, MySpellsOnEnemies: 1, SecondsToNextPowerRune: 260),
            situation.Signals);
        Assert.True(situation.Hidden.PowerRuneOnMap);
        Assert.Equal(EconomySource.Neutrals, EconomyBucket.SourceOf(EconomyResource.Gold, 14));
        Assert.Equal(EconomySource.Other, EconomyBucket.SourceOf(EconomyResource.Gold, null));
    }

    [Fact]
    public void Momentum_ReadsTheStreakAndTheRecentKills()
    {
        HeroDeath[] deaths =
        [
            new(6, 30, 0, 0, KillerKind.Hero, killerHeroId: 1, reincarnated: false), // before hero 1 died: not in its streak
            new(1, 40, 0, 0, KillerKind.Hero, killerHeroId: 6, reincarnated: false),
            new(2, 50, 0, 0, KillerKind.Hero, killerHeroId: 6, reincarnated: false), // the enemy's kill in the last minute
            new(6, 70, 0, 0, KillerKind.Hero, killerHeroId: 1, reincarnated: false),
            new(6, 95, 0, 0, KillerKind.Hero, killerHeroId: 2, reincarnated: false),
            new(6, 120, 0, 0, KillerKind.Hero, killerHeroId: 1, reincarnated: false), // the future
        ];

        var momentum = Builder([Path(1, 100)], deaths: deaths).At(1, 100)!.Momentum;

        // Hero 6 is level 3 at 1:40: no enemy has an ultimate level yet.
        Assert.Equal(new Momentum(KillStreak: 1, RecentDeaths: 1, TeamKillsLastMinute: 2, EnemyKillsLastMinute: 1, EnemiesWithUltimateLevel: 0),
            momentum);
    }

    [Fact]
    public void BigItems_AreTheHerosSpikes_AndTheEnemiesRecentOnes()
    {
        var items = new Dictionary<string, ItemDetails>
        {
            ["black_king_bar"] = new("black_king_bar", 4050, ["ogre_axe", "mithril_hammer", "recipe_black_king_bar"], false),
            ["blink"] = new("blink", 2250, null, false),
            ["recipe_black_king_bar"] = new("recipe_black_king_bar", 2500, null, false), // a recipe: never
            ["ward_observer"] = new("ward_observer", 0, null, true),
            ["magic_wand"] = new("magic_wand", 450, ["magic_stick", "branches", "branches"], false),
        };
        ItemPurchase[] purchases =
        [
            new(1, 40, "blink"), new(1, 70, "magic_wand"), new(1, 75, "recipe_black_king_bar"),
            new(6, 90, "black_king_bar"), new(6, 101, "blink"), // the future
        ];
        var replay = new ReplayAnalysis(Match(), 180, Players, Timeline, [], [], objectives: [], purchases: purchases);

        var situation = HeroSituationBuilder.From(replay, [Path(1, 100)], catalog: new GameCatalog(Items: items))!.At(1, 100)!;
        var withoutCatalog = HeroSituationBuilder.From(replay, [Path(1, 100)])!.At(1, 100)!;

        Assert.Equal((60, 1), (situation.Momentum.SecondsSinceMyBigItem, situation.Hidden.EnemyBigItemsLast2Minutes));
        Assert.Equal((null, null), (withoutCatalog.Momentum.SecondsSinceMyBigItem, withoutCatalog.Hidden.EnemyBigItemsLast2Minutes));
    }

    [Fact]
    public void Surroundings_ReadFacingStashCouriersAndTheDamageTakenByType()
    {
        // Radiant hero 1 at (0, 0) facing 45° (up-right: straight at the Dire base); Dire hero 6 seen at (0, 1000).
        var hero = new HeroPositions(1, [new PositionSample(100, 0, 0, new HeroVitals(500, 1000, 100, 400, VisibleToEnemies: true), Facing: 45)]);
        var enemy = new HeroPositions(6, [new PositionSample(100, 0, 1000, new HeroVitals(500, 1000, 100, 400, VisibleToEnemies: true))]);
        var activity = new ReplayActivity([], [], [],
            slotChanges:
            [
                new SlotChange(1, 40, 9, "item_ogre_axe"), new SlotChange(1, 60, 10, "item_circlet"), new SlotChange(1, 80, 10, null),
                new SlotChange(1, 50, 0, "item_tango"), // the inventory: not the stash
            ],
            typedDamage:
            [
                new TypedDamageBucket(1, 94, 0, 0, 0, PhysicalFrom: 100, MagicalFrom: 250, PureFrom: 0),
                new TypedDamageBucket(1, 98, 0, 0, 0, PhysicalFrom: 50, MagicalFrom: 0, PureFrom: 30),
                new TypedDamageBucket(1, 99, 0, 0, 0, PhysicalFrom: 999, MagicalFrom: 0, PureFrom: 0), // ends after 100
            ],
            healing: [new HealingBucket(1, 96, Received: 120, Done: 40)],
            couriers:
            [
                new CourierTrack("r1", Team.Radiant, [(0, true, 0, 0)]),
                new CourierTrack("r2", Team.Radiant, [(0, true, 0, 0), (90, false, 0, 0)]),
                new CourierTrack("d1", Team.Dire, [(0, true, 0, 0)]),
            ]);

        var surroundings = Builder([hero, enemy], activity: activity).At(1, 100)!.Surroundings;

        Assert.Equal((1.0, 0.707), (surroundings.FacingEnemyBase, surroundings.FacingNearestSeenEnemy));
        Assert.Equal((1, 1, 1), (surroundings.StashItems, surroundings.OwnCouriersAlive, surroundings.EnemyCouriersAlive));
        Assert.Equal((150, 250, 30, 120, 40), (surroundings.PhysicalTakenLast6, surroundings.MagicalTakenLast6, surroundings.PureTakenLast6,
            surroundings.HealingReceivedLast6, surroundings.HealingDoneLast6));
    }

    [Fact]
    public void Kit_ReadsTheEnemiesDraftAndWhatIsReadyNow()
    {
        static AbilityDetails Spell(string key, string header) =>
            new(key, key, null, ["Unit Target"], AbilityTargetTeam.Enemy, ["Hero"], AbilityDamageType.None, false, Dispellable.StrongOnly,
                [14], [100], [new AbilityValue("duration", header, [2])]);
        var abilities = new Dictionary<string, AbilityDetails>
        {
            ["lion_impale"] = Spell("lion_impale", "STUN DURATION:"),
            ["lion_voodoo"] = Spell("lion_voodoo", "DURATION:"), // a hex by its key
            ["antimage_blink"] = new("antimage_blink", "Blink", "Short distance teleportation.", ["Point Target"], AbilityTargetTeam.None,
                [], AbilityDamageType.None, null, Dispellable.Unknown, [10], [50], []),
        };
        var kits = new Dictionary<int, HeroKit>
        {
            [6] = new(6, ["lion_impale", "lion_voodoo"], [], []),
            [1] = new(1, ["antimage_blink"], [], []),
        };
        var replay = new ReplayAnalysis(Match(), 180, Players, Timeline, [], [], objectives: [], abilityUpgrades:
        [
            new AbilityUpgrade(1, 30, "antimage_blink", 1),
            new AbilityUpgrade(6, 20, "lion_impale", 1),
            new AbilityUpgrade(6, 40, "lion_voodoo", 1),
        ]);
        var activity = new ReplayActivity([], [], [], slotChanges: [], itemCooldowns: [], abilityCooldowns:
        [
            new CooldownStart(1, 95, "antimage_blink", 105), // hero 1's blink is cooling at 100
            new CooldownStart(6, 90, "lion_impale", 104),    // Lion's stun too, but the hex is ready
        ]);
        var catalog = new GameCatalog(Abilities: abilities, Kits: kits, Items: new Dictionary<string, ItemDetails>());

        var situation = HeroSituationBuilder.From(replay, [Path(1, (100, 0, 0)), Path(6, (100, 1000, 0))], activity, catalog)!.At(1, 100)!;

        Assert.Equal(new KitState(EnemiesWithStun: 1, EnemiesWithSilenceOrHex: 1, EnemiesWithEscape: 0, EnemiesWithInvisibility: 0,
            MyDisableReady: false, MyEscapeReady: false), situation.Kit);
        Assert.Equal(1, situation.Hidden.EnemiesWithDisableReadyNear);
    }

    [Fact]
    public void Spells_SumReadyDamageHealingAndMana_AtTheLearnedLevels()
    {
        static AbilityDetails Spell(string key, double[] mana, params AbilityValue[] values) =>
            new(key, key, null, ["Unit Target"], AbilityTargetTeam.Enemy, ["Hero"], AbilityDamageType.Magical, false,
                Dispellable.Yes, [10], mana, values);
        var abilities = new Dictionary<string, AbilityDetails>
        {
            ["lion_impale"] = Spell("lion_impale", [90, 110, 130, 150], new AbilityValue("damage", "DAMAGE:", [105, 170, 235, 300])),
            ["lion_finger_of_death"] = Spell("lion_finger_of_death", [200, 420, 650], new AbilityValue("damage", "DAMAGE:", [600, 725, 850])),
            ["lion_to_hell_and_back"] = new("lion_to_hell_and_back", "To Hell and Back", null, ["Passive"], AbilityTargetTeam.None, [],
                AbilityDamageType.None, null, Dispellable.No, [], [], [new AbilityValue("damage", "DAMAGE:", [999])], IsInnate: true),
            ["dazzle_shadow_wave"] = Spell("dazzle_shadow_wave", [80], new AbilityValue("damage", "DAMAGE:", [80]),
                new AbilityValue("heal", "HEAL:", [80])),
            ["zuus_arc_lightning"] = Spell("zuus_arc_lightning", [80], new AbilityValue("arc_damage", "DAMAGE:", [90])),
        };
        var replay = new ReplayAnalysis(Match(), 180, Players, Timeline, [], [], objectives: [], abilityUpgrades:
        [
            new AbilityUpgrade(1, 10, "lion_impale", 1),
            new AbilityUpgrade(1, 50, "lion_impale", 2),
            new AbilityUpgrade(1, 90, "lion_finger_of_death", 1),
            new AbilityUpgrade(1, 95, "lion_to_hell_and_back", 1),  // passive: left out
            new AbilityUpgrade(1, 120, "lion_impale", 3),           // the future: level 2 at 100
            new AbilityUpgrade(2, 30, "dazzle_shadow_wave", 1),
            new AbilityUpgrade(6, 30, "zuus_arc_lightning", 1),
        ]);
        var activity = new ReplayActivity([], [], [], abilityCooldowns: [new CooldownStart(6, 98, "zuus_arc_lightning", 104)]);
        HeroPositions At(int hero, int x, int mana) => new(hero, [new PositionSample(100, x, 0, new HeroVitals(500, 1000, mana, 1000))]);
        var catalog = new GameCatalog(Abilities: abilities);

        var spells = HeroSituationBuilder.From(replay, [At(1, 0, 300), At(2, 500, 200), At(6, 1000, 400)], activity, catalog)!
            .At(1, 100)!.Spells;

        // Earth Spike level 2 (170 damage, 110 mana) and Finger level 1 (600, 200) each fit in 300 mana on its own, not
        // both: 10 short. The ally's Shadow Wave heals 80; Zeus's Arc Lightning (90) is cooling, so nothing is ready near.
        Assert.Equal((770, 0, 310, -10, 80, 90, 0, 0), (spells.MyReadyDamage, spells.MyReadyHealing, spells.MyReadyManaCost,
            spells.MyManaAfterReadySpells, spells.AlliesReadyHealingNear, spells.EnemyKitDamageAlive,
            spells.HiddenEnemiesReadyDamageNear, spells.HiddenEnemiesReadyHealingNear));
    }

    [Fact]
    public void Spells_ReadRangeAreaAndWhatReachesTheHero_WithTheLearnedTalent()
    {
        // Lion's Earth Spike: 650 range (+600 with special_bonus_unique_lion_2), width 140, a stun by its header.
        var impale = new AbilityDetails("lion_impale", "Earth Spike", null, ["Point Target", "Unit Target"], AbilityTargetTeam.Enemy,
            ["Hero"], AbilityDamageType.Magical, false, Dispellable.StrongOnly, [12], [100],
            [
                new AbilityValue("AbilityCastRange", null, [650], Rules: new ValueRules(
                    [new ValueBonus("special_bonus_unique_lion_2", BonusOperation.Add, [600])], false, false, false)),
                new AbilityValue("width", "WIDTH:", [140]),
                new AbilityValue("duration", "STUN DURATION:", [2]),
                new AbilityValue("damage", "DAMAGE:", [105]),
            ]);
        // Zeus's Lightning Bolt: single target, 700 range, a stun that goes through spell immunity, undispellable.
        var bolt = new AbilityDetails("zuus_lightning_bolt", "Lightning Bolt", null, ["Unit Target"], AbilityTargetTeam.Enemy,
            ["Hero"], AbilityDamageType.Magical, true, Dispellable.No, [6], [100],
            [new AbilityValue("AbilityCastRange", null, [700]), new AbilityValue("ministun_duration", "STUN DURATION:", [0.3]),
             new AbilityValue("damage", "DAMAGE:", [125])]);
        var catalog = new GameCatalog(Abilities: new Dictionary<string, AbilityDetails> { [impale.Key] = impale, [bolt.Key] = bolt });
        var replay = new ReplayAnalysis(Match(), 180, Players, Timeline, [], [], objectives: [], abilityUpgrades:
        [
            new AbilityUpgrade(1, 10, "lion_impale", 1),
            new AbilityUpgrade(1, 50, "special_bonus_unique_lion_2", 1),
            new AbilityUpgrade(6, 10, "zuus_lightning_bolt", 1),
        ]);
        var activity = new ReplayActivity([], [], [], abilityCooldowns: []);
        HeroPositions At(int hero, int x, bool seen) => new(hero, [new PositionSample(100, x, 0, new HeroVitals(500, 1000, 400, 1000, seen))]);

        // Zeus stands 1,200 away, seen by Lion's team: Earth Spike reaches 1,250 + 140 with the talent; the bolt does not reach Lion.
        var spells = HeroSituationBuilder.From(replay, [At(1, 0, false), At(6, 1200, true)], activity, catalog)!.At(1, 100)!.Spells;

        Assert.Equal((1, 0, 1250, 1), (spells.MyReadyAreaSpells, spells.MyReadySingleTargetSpells, spells.MyReadySpellMaxRange,
            spells.MyReadyDisablesReachingSeenEnemy));
        Assert.Equal((700, 1, 1), (spells.EnemyKitDisableMaxRange, spells.EnemyKitUndispellableDisables, spells.EnemyKitImmunityPiercingDisables));
        Assert.Equal((0, 0, 0), (spells.HiddenEnemyReadySpellsReachingMe, spells.HiddenEnemyReadyDisablesReachingMe,
            spells.HiddenEnemyReadyDamageReachingMe));

        // At 600 away the bolt reaches him: one ready disable with 125 damage.
        var near = HeroSituationBuilder.From(replay, [At(1, 0, false), At(6, 600, true)], activity, catalog)!.At(1, 100)!.Spells;
        Assert.Equal((1, 1, 125), (near.HiddenEnemyReadySpellsReachingMe, near.HiddenEnemyReadyDisablesReachingMe,
            near.HiddenEnemyReadyDamageReachingMe));
    }

    [Fact]
    public void Neutrals_ReadTheOpenTierTheArtifactMadstonesAndCrafts_UpToTheSecond()
    {
        var rules = new NeutralRules(
            [
                new NeutralTier(1, 0, 4, 4, 6, 0, ["occult_bracelet"], [new NeutralEnhancement("enhancement_vital", 1)],
                    new Dictionary<HeroAttribute, IReadOnlyList<NeutralEnhancement>>()),
                new NeutralTier(2, 900, 5, 5, 10, 0, ["pogo_stick"], [], new Dictionary<HeroAttribute, IReadOnlyList<NeutralEnhancement>>()),
                new NeutralTier(3, 1500, 5, 5, 10, 0, ["serrated_shiv"], [], new Dictionary<HeroAttribute, IReadOnlyList<NeutralEnhancement>>()),
            ], 4200, [2, 3, 4], [1, 2, 3]);
        // Pogo Stick has an active (a hop); the Occult Bracelet is passive.
        var items = new Dictionary<string, ItemDetails>
        {
            ["pogo_stick"] = new("pogo_stick", 0, null, false)
                { Abilities = [new ItemAbility(ItemAbilityKind.Active, "Hop", "Leaps forward.")] },
            ["occult_bracelet"] = new("occult_bracelet", 0, null, false)
                { Abilities = [new ItemAbility(ItemAbilityKind.Passive, "Rites of Eloshar", null)] },
        };
        var activity = new ReplayActivity([], [], [],
            slotChanges:
            [
                new SlotChange(1, 300, SlotChange.NeutralSlot, "item_occult_bracelet"),
                new SlotChange(6, 950, SlotChange.NeutralSlot, "item_pogo_stick"),
                new SlotChange(1, 1100, SlotChange.NeutralSlot, "item_pogo_stick"), // the future at 1000
            ],
            itemCooldowns: [],
            neutralCrafts:
            [
                new NeutralCraft(1, 299.5, "item_occult_bracelet", 1, 1, "item_enhancement_vital", 1),
                new NeutralCraft(6, 949.0, "item_pogo_stick", 1, 2, "item_enhancement_brawny", 2),
            ],
            madstones:
            [
                new MadstoneTrack(1, [new MadstoneReading(0, 0, 0, 0, 0), new MadstoneReading(980, 11, 17, 9, 300)]),
                new MadstoneTrack(6, [new MadstoneReading(0, 0, 0, 0, 0), new MadstoneReading(990, 1, 16, 8, 250)]),
            ]);
        var catalog = new GameCatalog(Items: items, Neutrals: rules);
        HeroPositions At(int hero, int x) => new(hero, [new PositionSample(1000, x, 0)]);

        var replay = new ReplayAnalysis(Match(), 180, Players, Timeline, [], [], objectives: []);
        var situation = HeroSituationBuilder.From(replay, [At(1, 0), At(6, 800)], activity, catalog)!.At(1, 1000)!;

        // Tier 2 is open (15:00) and tier 3 opens in 500 s; Lion still has the tier 1 bracelet, 11 Madstones (cost 10).
        Assert.Equal(new NeutralState(TierOpen: 2, SecondsToNextTier: 500, MyArtifactTier: 1, MyTiersBehind: 1,
            MyArtifactActiveReady: false, MyEnhancementLevel: 1, MyMadstones: 11, CanCraftNow: true, SecondsSinceLastCraft: 701,
            HiddenEnemyArtifactActivesReadyNear: 1, HiddenEnemyMadstones: 1, HiddenEnemyTiersBehind: 0), situation.Neutrals);
    }

    [Fact]
    public void Neutrals_WithoutTheBuildRulesOrTheNewSections_AreUnknown()
    {
        var neutrals = Builder([Path(1, 100)]).At(1, 100)!.Neutrals;
        Assert.Equal((null, null, null, null, null, null), (neutrals.TierOpen, neutrals.MyArtifactTier, neutrals.MyMadstones,
            neutrals.CanCraftNow, neutrals.MyEnhancementLevel, neutrals.HiddenEnemyMadstones));
    }

    [Fact]
    public void Spells_WithoutTheAbilitiesData_AreUnknown() =>
        Assert.Equal(SpellPower.Unknown, Builder([Path(1, 100)]).At(1, 100)!.Spells);

    [Fact]
    public void Buildings_GiveTheNearestStandingTowers_AndTheBuildingsLosingHealth()
    {
        static BuildingHealth Tower(string name, Team team, int x, int y, params (int T, int Health)[] readings) =>
            new(name, team, BuildingKind.Tower, Lane.Middle, 1, x, y,
                readings.Select(reading => new BuildingHealthReading(reading.T, reading.Health, 2000)).ToList());
        var activity = new ReplayActivity([], [], [], buildings:
        [
            Tower("npc_dota_goodguys_tower1_mid", Team.Radiant, -300, -400, (0, 2000), (94, 1500)),      // 500 away, under attack
            Tower("npc_dota_goodguys_tower2_mid", Team.Radiant, -1000, -1000, (0, 2000), (80, 0)),      // fell before
            Tower("npc_dota_badguys_tower1_mid", Team.Dire, 600, 800, (0, 2000), (60, 1000), (102, 500)), // 1000 away; the drop is the future
            Tower("npc_dota_badguys_tower2_mid", Team.Dire, 2000, 2000, (0, 2000), (95, 1800)),
        ]);
        // Radiant hero 1 at (0, 0).
        var situation = Builder([Path(1, (100, 0, 0))], activity: activity).At(1, 100)!;
        var unknown = Builder([Path(1, (100, 0, 0))], activity: new ReplayActivity([], [], [])).At(1, 100)!;

        Assert.Equal(new BuildingPressure(500, 0.75, 1000, 0.5, OwnBuildingsUnderAttack: 1), situation.Buildings);
        Assert.Equal(1, situation.Hidden.EnemyBuildingsUnderAttack);
        Assert.Equal(BuildingPressure.Unknown, unknown.Buildings);
        Assert.Null(unknown.Hidden.EnemyBuildingsUnderAttack);
    }

    [Fact]
    public void Threat_ReadsWhatTheNearestSeenEnemyDidSoFar_AndItsCooldownsAsHidden()
    {
        static PositionSample Seen(int x) => new(100, x, 0, new HeroVitals(500, 1000, 100, 400, VisibleToEnemies: true));
        HeroDeath[] deaths =
        [
            new(1, 50, 0, 0, KillerKind.Hero, killerHeroId: 6, reincarnated: false),
            new(2, 70, 0, 0, KillerKind.Hero, killerHeroId: 6, reincarnated: false),
            new(1, 120, 0, 0, KillerKind.Hero, killerHeroId: 6, reincarnated: false), // the future
        ];
        var activity = new ReplayActivity([], [], [],
            slotChanges: [new SlotChange(6, 50, 0, "item_black_king_bar")],
            itemCooldowns: [],
            abilityCooldowns:
            [
                new CooldownStart(6, 10, "enemy_ultimate", 40),  // the longest cooldown seen: the ultimate, ready again
                new CooldownStart(6, 90, "enemy_nuke", 98),
            ],
            debuffs:
            [
                new Debuff(60, 62, 6, 2, "modifier_stunned", DebuffKind.Stun),
                new Debuff(65, 70, 6, 1, "modifier_slow", DebuffKind.Slow),     // not a disable
                new Debuff(101, 103, 6, 1, "modifier_stunned", DebuffKind.Stun), // the future
            ]);

        // Radiant hero 1 at (0, 0) at level 2 (the minute-1 point); Dire hero 6 seen 1,000 away at level 3.
        var situation = Builder([new HeroPositions(1, [Seen(0)]), new HeroPositions(6, [Seen(1000)])],
            deaths: deaths, activity: activity).At(1, 100)!;

        Assert.Equal(new EnemyThreat(MostKillsOnMeByOneEnemy: 1, NearestVisibleEnemyKillsOnMe: 1, NearestVisibleEnemyKillsOnTeam: 2,
            NearestVisibleEnemyLevelLead: 1, NearestVisibleEnemyDisablesOnTeam: 1), situation.Threat);
        Assert.Null(situation.Hidden.EnemiesUltimateReadyNear);
        Assert.Equal(1, situation.Hidden.EnemiesBlackKingBarReadyNear);
    }

    [Fact]
    public void Threat_CountsTheLivingEnemiesByCatalogRole_OnlyWithEveryProfile()
    {
        var lion = new HeroDetails(HeroAttribute.Intelligence, HeroAttackType.Ranged,
            new HashSet<HeroRole> { HeroRole.Support, HeroRole.Disabler, HeroRole.Nuker, HeroRole.Initiator });
        var positions = new[] { Path(1, 100), Path(6, 100) };

        var known = Builder(positions, heroDetails: new Dictionary<int, HeroDetails> { [6] = lion }).At(1, 100)!.Threat;
        var missing = Builder(positions, heroDetails: new Dictionary<int, HeroDetails>()).At(1, 100)!.Threat;

        Assert.Equal((1, 1, 1, 0, 0), (known.EnemyDisablersAlive, known.EnemyInitiatorsAlive, known.EnemyNukersAlive,
            known.EnemyEscapesAlive, known.EnemyMeleeAlive));
        Assert.Null(missing.EnemyDisablersAlive);
    }

    [Fact]
    public void Lane_ComparesTheHeroWithItsOpposingRole_AndCountsTheEnemiesInItsLaneNow()
    {
        static HeroMetrics Metrics(int heroId, Team team, int role, bool inferred = false) =>
            new(heroId, team, Role: role, 0, 0, 0, 0, 0, null, null, 0, 0, 0, 0, null, 0, 0, RoleInferred: inferred);
        static PositionSample Seen(int seconds, int x, bool seen) =>
            new(seconds, x, 0, new HeroVitals(500, 1000, 100, 400, VisibleToEnemies: seen));
        var melee = new HeroDetails(HeroAttribute.Strength, HeroAttackType.Melee, new HashSet<HeroRole>());
        var ranged = new HeroDetails(HeroAttribute.Agility, HeroAttackType.Ranged, new HashSet<HeroRole>());
        HeroDeath[] deaths = [new(1, 40, 0, 0, KillerKind.Hero, killerHeroId: 6, reincarnated: false)];
        var activity = new ReplayActivity([], [], [], heroHits:
        [
            new HeroHit(80, 1, 6, 120),
            new HeroHit(90, 6, 1, 200),
            new HeroHit(98, 6, 1, 999), // ends at 100: still counts
            new HeroHit(30, 6, 1, 500), // more than a minute before
        ]);
        // Radiant hero 1 (offlane, 3) against Dire hero 6 (safe lane, 1), 800 apart; hero 6 unseen at 100.
        var positions = new[] { new HeroPositions(1, [Seen(100, 0, true)]), new HeroPositions(6, [Seen(100, 800, false)]) };

        var lane = Builder(positions, deaths: deaths, metrics: [Metrics(1, Team.Radiant, 3), Metrics(6, Team.Dire, 1)],
            activity: activity, heroDetails: new Dictionary<int, HeroDetails> { [1] = melee, [6] = ranged }).At(1, 100)!.Lane;
        // Both in the bottom lane (Radiant at x = 2000, Dire at x = 2800, y = -6250); the Dire hero unseen.
        var inLane = Builder([new HeroPositions(1, [Seen(100, 2000, true) with { Y = -6250 }]),
            new HeroPositions(6, [Seen(100, 2800, false) with { Y = -6250 }])]).At(1, 100)!;

        // Levels at 100 (the minute-1 point): 2 against 3; net worth 900 against 1000.
        Assert.Equal(new LaneOpponent(HeroId: 6, IAmMelee: true, OpponentMelee: false, LevelLead: -1, KillsOnMe: 1, MyKillsOnThem: 0,
            SeenDistance: null, DamageToThem: 120, DamageFromThem: 1199, HiddenDistance: 800, HiddenNetWorthLead: -100), lane);
        Assert.Equal((0, 1), (inLane.Waves.SeenEnemiesInMyLane, inLane.Hidden.EnemiesInMyLane));
    }

    [Theory]
    [InlineData("teleport", "modifier_whatever", BuffKind.Teleport)]
    [InlineData("other", "modifier_black_king_bar_immune", BuffKind.MagicImmune)]
    [InlineData("other", "modifier_eul_cyclone", BuffKind.Save)]
    [InlineData("other", "modifier_item_phase_boots_active", BuffKind.Speed)]
    [InlineData("other", "modifier_bottle_regeneration", BuffKind.Heal)]
    [InlineData("save", "modifier_fountain_invulnerability", BuffKind.Other)]
    [InlineData("other", "modifier_item_ring_of_basilius_effect", BuffKind.Other)]
    public void BuffKinds_RefineTheParsersOtherByName(string parserKind, string modifier, BuffKind expected)
    {
        Assert.Equal(expected, BuffKinds.Of(parserKind, modifier));
    }

    [Fact]
    public void FightFarmNightAndBody_AreUnknownForOlderDocuments()
    {
        var situation = Builder([Path(1, 100)], activity: new ReplayActivity([], [], [])).At(1, 100)!;

        Assert.Equal((Fight.Unknown, Farm.Unknown, Body.Unknown), (situation.Fight, situation.Farm, situation.Body));
        Assert.Null(situation.Map.IsNight);
    }

    [Fact]
    public void Condition_IsUnknownForDocumentsWithoutVitals()
    {
        Assert.Equal(Condition.Unknown, Builder([Path(1, 100)]).At(1, 100)!.Condition);
    }

    [Fact]
    public void OutdatedAnalysis_HasNoBuilder()
    {
        var withoutTimeline = new ReplayAnalysis(Match(), 180, Players, deaths: [], wards: [], objectives: []);
        var withoutObjectives = new ReplayAnalysis(Match(), 180, Players, Timeline, deaths: [], wards: []);

        Assert.Null(HeroSituationBuilder.From(withoutTimeline, []));
        Assert.Null(HeroSituationBuilder.From(withoutObjectives, []));
    }

    private static HeroSituationBuilder Builder(
        IReadOnlyList<HeroPositions> positions,
        IReadOnlyList<WardPlacement>? wards = null,
        IReadOnlyList<MapObjective>? objectives = null,
        IReadOnlyList<HeroDeath>? deaths = null,
        IReadOnlyList<HeroMetrics>? metrics = null,
        ReplayActivity? activity = null,
        IReadOnlyDictionary<int, HeroDetails>? heroDetails = null,
        GameCatalog? catalog = null) =>
        HeroSituationBuilder.From(
            new ReplayAnalysis(Match(), 180, Players, Timeline, deaths ?? [], wards ?? [], objectives: objectives ?? [], metrics: metrics),
            positions, activity, catalog ?? (heroDetails is null ? null : new GameCatalog(heroDetails)))!;

    private static MatchId Match() => MatchId.TryCreate(1, out var id) ? id : throw new InvalidOperationException();

    private static HeroTimeline Hero(
        int heroId, Team team, int[] netWorth, int[] experience, int[] level, int[] damageFromHeroes) =>
        new(heroId, team, netWorth, experience, new Dictionary<StatKey, IReadOnlyList<int>>
        {
            [StatKey.Level] = level,
            [StatKey.DamageFromHeroes] = damageFromHeroes,
        });

    // Samples at the given seconds, all at the map's center.
    private static HeroPositions Path(int heroId, params int[] seconds) =>
        new(heroId, seconds.Select(second => new PositionSample(second, 0, 0)).ToList());

    private static HeroPositions Path(int heroId, params (int Seconds, int X, int Y)[] samples) =>
        new(heroId, samples.Select(sample => new PositionSample(sample.Seconds, sample.X, sample.Y)).ToList());
}
