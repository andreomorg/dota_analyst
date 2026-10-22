namespace DotaAnalyst.Domain.Replays;

// Which of the map's trees stand during the match (timelines.trees, MP3): Index is the tree's index in the map of the
// patch (GameMap.Trees). DeadAtStart: down at the first reading (0:00); Changes: every tree that went down or grew back.
public sealed record TreeTimeline
{
    public TreeTimeline(IReadOnlyList<int> deadAtStart, IReadOnlyList<TreeChange> changes)
    {
        ArgumentNullException.ThrowIfNull(deadAtStart);
        ArgumentNullException.ThrowIfNull(changes);
        if (deadAtStart.Any(index => index < 0) || changes.Any(change => change is null || change.Index < 0))
        {
            throw new ArgumentException("Tree indexes are ≥ 0.");
        }

        DeadAtStart = deadAtStart;
        Changes = changes.OrderBy(change => change.Seconds).ToList();
    }

    public IReadOnlyList<int> DeadAtStart { get; }

    public IReadOnlyList<TreeChange> Changes { get; }

    // The trees down at a second (changes up to it included).
    public IReadOnlySet<int> DownAt(int second)
    {
        var down = new HashSet<int>(DeadAtStart);
        foreach (var change in Changes.TakeWhile(change => change.Seconds <= second))
        {
            if (change.Alive)
            {
                down.Remove(change.Index);
            }
            else
            {
                down.Add(change.Index);
            }
        }

        return down;
    }
}

// A tree went down (Alive false) or grew back (true) at a second.
public sealed record TreeChange(int Seconds, int Index, bool Alive);

// A circle of a team's moving vision (timelines.visionUnits, MP4): creeps, summons, illusions in one 128-unit cell, with
// the units' own day and night ranges.
public sealed record VisionCircle(int X, int Y, int Day, int Night);

// Each team's moving vision at a second (every 2 s).
public sealed record VisionUnitsSample(int Seconds, IReadOnlyList<VisionCircle> Radiant, IReadOnlyList<VisionCircle> Dire);

// A temporary tree (Iron Branch, Sprout...) appearing (Present) or going away at a moment (timelines.tempTrees, MP3).
public sealed record TempTreeChange(double Seconds, int X, int Y, bool Present);

// One Roshan or Tormentor unit over the match (objectives.bossTracks, MP3): Kind is the document's own ("roshan",
// "tormentor"; anything else kept as is). Samples are sparse — only when the unit moved more than 200 or lived/died —
// so between two samples the unit is somewhere unknown on the way: never interpolate.
public sealed record BossTrack(string Kind, IReadOnlyList<BossSample> Samples)
{
    // The last sample at or before the second (never a later one); null before the first.
    public BossSample? LastAt(int second) => Samples.LastOrDefault(sample => sample.Seconds <= second);
}

public sealed record BossSample(int Seconds, int X, int Y, bool Alive);

// A Watcher activated by a hero (vision.lanterns, MP3). The log has no location: X/Y are the hero's at that moment (null
// when unknown). An activation says nothing about how long the team keeps it.
public sealed record LanternUse(int HeroId, double Seconds, int? X, int? Y);

// One gold change of a hero with where it happened (combat.goldEvents, MP3): signed amount (losses negative), the raw
// gold reason (null when absent) and the log's location (null when absent). The location does not reclassify the reason.
public sealed record GoldEvent(int HeroId, double Seconds, long Amount, uint? Reason, int? X, int? Y);
