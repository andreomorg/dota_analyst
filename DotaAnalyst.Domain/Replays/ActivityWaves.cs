using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Replays;

// How far each team's lane creeps reached in a lane at a second (sampled every 2 s): the progress x + y of the creep
// furthest towards the enemy (it grows from the Radiant base to the Dire base along every lane). Null: none there.
public sealed record WaveFront(
    Lane Lane, int Seconds, int? Radiant, int? Dire, WaveCounts? Counts = null, WaveStrength? Strength = null,
    WaveComposition? Composition = null);

// One team's lane creeps in the lane by type (disjoint: melee, ranged, siege, flagbearer, super, mega) and how many of
// the front ones have at most a quarter of their health (last hits to take, or denies).
public sealed record TeamWave(int Melee, int Ranged, int Siege, int Flagbearer, int Super, int Mega, int LowAtFront);

// Both teams' wave composition in a lane at a second (W5). Null on a WaveFront: a document from before it.
public sealed record WaveComposition(TeamWave Radiant, TeamWave Dire);

// How strong each team's wave in the lane is at that second: the creeps' summed health in the lane and at the front
// (the same creeps WaveCounts counts there), whether a siege creep is alive in the lane, and the mean x + y of the front
// creeps (null when the team has none). Null on a WaveFront: a document from before W1.
public sealed record WaveStrength(
    int RadiantHealth, int DireHealth, int RadiantHealthAtFront, int DireHealthAtFront, bool RadiantSiege, bool DireSiege,
    int? RadiantCenter, int? DireCenter);

// The living lane creeps of each team in the lane at that second, and how many of them were within 1,200 of the team's
// leading creep (the wave that is pushing). Null on a WaveFront: a document from before the counts.
public sealed record WaveCounts(int RadiantInLane, int DireInLane, int RadiantAtFront, int DireAtFront);

// Lane creep hits on a hero in the 2 s ending at Seconds (the combat log: hits, not distinct creeps nor their intent).
public sealed record CreepHeroHits(int Seconds, Lane Lane, Team CreepTeam, int TargetHeroId, int Hits);

// Lane creeps outside every lane at a second (pulled into the jungle or dragged away), per team, and where they are on
// average (null when the team has none).
public sealed record OffLaneCreeps(int Seconds, int RadiantCount, int DireCount, int? RadiantX, int? RadiantY, int? DireX, int? DireY);

public enum CreepKiller
{
    Hero,
    Creep,
    Tower,
    Neutral,
    Other,
}

// A lane creep that died: when, in which lane (null in the jungle), whose it was, what killed it (a hero's own units and
// illusions count as Hero; a deny is a Hero of the creep's team) and where (null when never sampled).
public sealed record LaneCreepDeath(double Seconds, Lane? Lane, Team CreepTeam, CreepKiller Killer, int? KillerHeroId, int? X, int? Y);
