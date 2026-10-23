using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Maps;

namespace DotaAnalyst.Domain.Situations;

// Movement, buyback, smoke and ward vision around the hero.
public sealed partial class HeroSituationBuilder
{
    // More than this in one 2 s step is a teleport or a blink, not running.
    private const int MaxRunPerSample = 1200;
    // Observer wards see this far (day and night in the current patch).
    public const int ObserverVisionRadius = 1600;
    private Surroundings SurroundingsOf(int heroId, Team team, PositionSample self, int seconds)
    {
        var (speed, advance) = MovementOf(heroId, team, self);
        var smoked = SmokedHeroes(seconds);
        return new Surroundings(
            MoveSpeed: speed,
            AdvanceSpeed: advance,
            BuybackReady: BuybackReady(heroId, seconds),
            UnderSmoke: smoked?.Contains(heroId),
            AlliesUnderSmoke: smoked?.Count(hero => hero != heroId && teamOf.GetValueOrDefault(hero) == team && PositionAt(hero, seconds) is not null),
            InOwnObserverVision: InObserverVision(team, self, seconds),
            AlliesWithTeleportReady: TeleportsReady(team, seconds, except: heroId),
            FacingEnemyBase: self.Facing is { } yaw
                ? Math.Round((Math.Cos(Radians(yaw)) + Math.Sin(Radians(yaw))) / Math.Sqrt(2) * (team == Team.Radiant ? 1 : -1), 3)
                : null,
            FacingNearestSeenEnemy: FacingNearestSeenEnemy(team, self, seconds),
            StashItems: activity?.SlotChanges is { } changes
                ? changes.Where(change => change.HeroId == heroId && change.Seconds <= seconds && change.Slot is >= FirstStashSlot and <= LastStashSlot)
                    .GroupBy(change => change.Slot).Count(slot => slot.Last().Item is not null)
                : null,
            OwnCouriersAlive: activity?.Couriers?.Count(courier => courier.Team == team && courier.AliveAt(seconds) == true),
            EnemyCouriersAlive: activity?.Couriers?.Count(courier => courier.Team != team && courier.AliveAt(seconds) == true),
            PhysicalTakenLast6: Typed(heroId, seconds, bucket => bucket.PhysicalFrom),
            MagicalTakenLast6: Typed(heroId, seconds, bucket => bucket.MagicalFrom),
            PureTakenLast6: Typed(heroId, seconds, bucket => bucket.PureFrom),
            HealingReceivedLast6: Healed(heroId, seconds, bucket => bucket.Received),
            HealingDoneLast6: Healed(heroId, seconds, bucket => bucket.Done));
    }

    // The stash's slots in the replay's inventory (0-5 inventory, 6-8 backpack, 9-14 stash, 15 TP, 16 neutral).
    private const int FirstStashSlot = 9;
    private const int LastStashSlot = 14;

    private static double Radians(int degrees) => degrees * Math.PI / 180;

    private double? FacingNearestSeenEnemy(Team team, PositionSample self, int seconds)
    {
        if (self.Facing is not { } yaw)
        {
            return null;
        }

        PositionSample? nearest = null;
        foreach (var enemy in teamOf.Where(hero => hero.Value != team).Select(hero => hero.Key))
        {
            if (PositionAt(enemy, seconds) is { Vitals.VisibleToEnemies: true } seen && (nearest is null || Distance(self, seen) < Distance(self, nearest.Value)))
            {
                nearest = seen;
            }
        }

        if (nearest is not { } target || Distance(self, target) == 0)
        {
            return null;
        }

        var (dx, dy) = (target.X - self.X, target.Y - self.Y);
        var length = Math.Sqrt(dx * dx + dy * dy);
        return Math.Round((Math.Cos(Radians(yaw)) * dx + Math.Sin(Radians(yaw)) * dy) / length, 3);
    }

    // The hero's buckets of the last HitWindowSeconds that ended by this second (null without them).
    private int? Typed(int heroId, int seconds, Func<TypedDamageBucket, int> amount) =>
        activity?.TypedDamage?.Where(bucket => bucket.HeroId == heroId && bucket.EndsBy(seconds) && bucket.Seconds >= seconds - HitWindowSeconds)
            .Sum(amount);

    private int? Healed(int heroId, int seconds, Func<HealingBucket, int> amount) =>
        activity?.Healing?.Where(bucket => bucket.HeroId == heroId && bucket.EndsBy(seconds) && bucket.Seconds >= seconds - HitWindowSeconds)
            .Sum(amount);

    // Living heroes of the team (but `except`) with a teleport ready; null without inventories.
    private int? TeleportsReady(Team team, int seconds, int except = 0)
    {
        if (activity?.SlotChanges is null)
        {
            return null;
        }

        return teamOf.Where(hero => hero.Value == team && hero.Key != except && PositionAt(hero.Key, seconds) is not null)
            .Count(hero => ReadinessOf(hero.Key, seconds).TeleportReady == true);
    }

    // Units per second over the last position step, and the part of it towards the enemy base (negative: retreating).
    private (int? Speed, int? Advance) MovementOf(int heroId, Team team, PositionSample now)
    {
        var track = positionsOf[heroId];
        var before = track.At(now.Seconds - 2, tolerance: 0) ?? track.At(now.Seconds - 3, tolerance: 0);
        if (before is not { } then || now.Seconds <= then.Seconds)
        {
            return (null, null);
        }

        var distance = Distance(now, then);
        if (distance > MaxRunPerSample)
        {
            return (null, null);
        }

        var elapsed = now.Seconds - then.Seconds;
        return (distance / elapsed, (FromOwnSide(team, now.X + now.Y) - FromOwnSide(team, then.X + then.Y)) / elapsed);
    }

    // Read only a recent replicated state; no inferred price or guarantee from minute-based net worth.
    private bool? BuybackReady(int heroId, int seconds) =>
        BuybackReadiness.At(heroId, seconds, activity?.EconomyStates);

    // The heroes with a smoke buff on at this second; null without buffs.
    private HashSet<int>? SmokedHeroes(int seconds) =>
        activity?.Buffs?.Where(buff => buff.Kind == BuffKind.Smoke && buff.ActiveAt(seconds)).Select(buff => buff.TargetHeroId).ToHashSet();

    // Within ObserverVisionRadius of a standing observer of the team (placed by then; RemovedAt only says it is still
    // there, as for TeamObserversActive).
    private bool InObserverVision(Team team, PositionSample at, int seconds) =>
        replay.Wards!.Any(ward => ward.Team == team && ward.Kind == WardKind.Observer && ward.PlacedAt <= seconds
            && (ward.RemovedAt ?? int.MaxValue) > seconds
            && new MapPoint(ward.X, ward.Y).IsWithin(new MapPoint(at.X, at.Y), ObserverVisionRadius));

    // The map as a grid of cells this wide (the heatmap's 32 × 32 over the minimap square), each cell in one region.
    private const int VisionCellSize = 592;
    private const int MapMin = -9472;
    private static readonly Dictionary<Team, ILookup<MapRegion, (int X, int Y)>> CellsByRegion = new()
    {
        [Team.Radiant] = Cells(Team.Radiant),
        [Team.Dire] = Cells(Team.Dire),
    };

    private static ILookup<MapRegion, (int X, int Y)> Cells(Team team) =>
        Enumerable.Range(0, 32).SelectMany(column => Enumerable.Range(0, 32).Select(row =>
                (X: MapMin + column * VisionCellSize + VisionCellSize / 2, Y: MapMin + row * VisionCellSize + VisionCellSize / 2)))
            .ToLookup(cell => MapRegions.RegionOf(team, cell.X, cell.Y));

    // The share of a region's cells (regions seen from `side`) whose center a standing observer of `owner` sees.
    private double ObserverCoverage(Team owner, Team side, MapRegion region, int seconds)
    {
        var observers = replay.Wards!.Where(ward => ward.Team == owner && ward.Kind == WardKind.Observer && ward.PlacedAt <= seconds
            && (ward.RemovedAt ?? int.MaxValue) > seconds).ToList();
        var cells = CellsByRegion[side][region].ToList();
        if (cells.Count == 0)
        {
            return 0;
        }

        var seen = cells.Count(cell => observers.Any(ward =>
            new MapPoint(ward.X, ward.Y).IsWithin(new MapPoint(cell.X, cell.Y), ObserverVisionRadius)));
        return Math.Round((double)seen / cells.Count, 3);
    }

    private VisionCoverage CoverageOf(Team team, int seconds)
    {
        if (coverageAt.TryGetValue((team, seconds), out var cached))
        {
            return cached;
        }

        var enemy = team == Team.Radiant ? Team.Dire : Team.Radiant;
        var coverage = new VisionCoverage(
            ObserverCoverage(team, team, MapRegion.OwnJungle, seconds),
            ObserverCoverage(team, team, MapRegion.EnemyJungle, seconds),
            ObserverCoverage(team, team, MapRegion.River, seconds),
            ObserverCoverage(enemy, team, MapRegion.OwnJungle, seconds));
        coverageAt[(team, seconds)] = coverage;
        return coverage;
    }

    private readonly Dictionary<(Team Team, int Seconds), VisionCoverage> coverageAt = [];

    // Living enemies with a smoke on / able to buy back now (the player knows neither).
    private (int? UnderSmoke, int? WithBuyback) EnemyStates(Team team, int seconds)
    {
        var enemies = teamOf.Where(hero => hero.Value != team).Select(hero => hero.Key).ToList();
        var smoked = SmokedHeroes(seconds);
        var buybacks = enemies.Select(enemy => PositionAt(enemy, seconds) is not null ? BuybackReady(enemy, seconds) : null).ToList();
        return (
            smoked is null ? null : enemies.Count(enemy => smoked.Contains(enemy) && PositionAt(enemy, seconds) is not null),
            buybacks.All(ready => ready is not null) ? buybacks.Count(ready => ready == true) : null);
    }
}
