package matchdata

import (
	"encoding/json"
	"math"
	"strconv"
)

// List is a JSON list that is never null: a nil List is written as [] (a plain nil slice would be null). Every list in
// the contract uses it, so "nothing happened" is always [] and only fields documented as nullable can be null. It is
// an ordinary slice otherwise: append, len, range and indexing work as usual.
type List[T any] []T

// MarshalJSON writes the list, or [] when it is nil.
func (l List[T]) MarshalJSON() ([]byte, error) {
	if l == nil {
		return []byte("[]"), nil
	}
	return json.Marshal([]T(l))
}

// Seconds is a moment (or a span) on the game clock: seconds since the horn (0:00), pauses left out, negative before
// the horn. It is written with at most one decimal place (0.1 s is finer than anything the replay measures reliably).
type Seconds float64

// MarshalJSON writes the value rounded to one decimal place (1725.53 -> 1725.5, 600 -> 600).
func (s Seconds) MarshalJSON() ([]byte, error) {
	rounded := math.Round(float64(s)*10) / 10
	if rounded == 0 {
		rounded = 0 // no "-0"
	}
	return strconv.AppendFloat(nil, rounded, 'f', -1, 64), nil
}

// Sample is a hero's position at a whole second of the game clock, written as the tuple [t, x, y] (about 3x smaller
// than an object with field names, and there are thousands of them).
type Sample struct {
	// T is the second of the game clock.
	T int
	// X and Y are map coordinates (about -8200..8200, Radiant bottom-left), rounded to whole units.
	X, Y int
}

// MarshalJSON writes [t, x, y].
func (s Sample) MarshalJSON() ([]byte, error) {
	return json.Marshal([3]int{s.T, s.X, s.Y})
}

// Vitals is a hero's state at the moment of the Sample with the same index, written as the tuple
// [health, maxHealth, mana, maxMana, visibleToEnemies, unitState, gold]. Each value is null when the replay does not
// have that property. visibleToEnemies is 1 when the enemy team saw the hero (its bit in the enemy CDOTA_DataRadiant/
// Dire m_bNPCVisibleState: the game's own fog of war), 0 otherwise. unitState is the raw low 32 bits of m_nUnitState64
// (stunned, silenced, invisible...; which bit is which is not interpreted here). Integer reads preserve bits
// above float64 precision before masking; this is not a complete 64-bit state. gold is gold in hand.
type Vitals struct {
	Health, MaxHealth, Mana, MaxMana, VisibleToEnemies, UnitState, Gold *int
}

// DayNight is [t, isDay], starting with the first known state at or after the horn.
type DayNight struct {
	T     int
	IsDay bool
}

// MarshalJSON writes [t, isDay].
func (d DayNight) MarshalJSON() ([]byte, error) {
	return json.Marshal([2]any{d.T, d.IsDay})
}

// CombatStats is [armor, magicResist, moveSpeed, damageMin, damageMax, strength, agility, intellect],
// parallel to Sample. Missing properties are null; values are rounded to one decimal (magicResist in percent,
// moveSpeed in map units/second). Armor, resistance and speed are replicated base values, not effective totals
// with items/buffs. Damage uses m_iDamageMin/Max (includes the primary attribute, but full bonus damage is
// unverified); attributes use the replicated Total properties, including bonuses.
type CombatStats struct {
	Armor, MagicResist, MoveSpeed, DamageMin, DamageMax, Strength, Agility, Intellect *float64
}

// MarshalJSON writes the eight combat attributes without changing the Vitals tuple.
func (s CombatStats) MarshalJSON() ([]byte, error) {
	return json.Marshal([8]*float64{s.Armor, s.MagicResist, s.MoveSpeed, s.DamageMin, s.DamageMax, s.Strength, s.Agility, s.Intellect})
}

// MarshalJSON writes [health, maxHealth, mana, maxMana, visibleToEnemies, unitState, gold].
func (v Vitals) MarshalJSON() ([]byte, error) {
	return json.Marshal([7]*int{v.Health, v.MaxHealth, v.Mana, v.MaxMana, v.VisibleToEnemies, v.UnitState, v.Gold})
}

// Bucket is an amount in the BucketSeconds starting at T (whole seconds of the game clock, a multiple of
// BucketSeconds, negative before the horn), written as the tuple [t, amount]. It covers [T, T + BucketSeconds): a reader
// at second s may only use buckets with T + BucketSeconds <= s.
type Bucket struct {
	T, Amount int
}

// MarshalJSON writes [t, amount].
func (b Bucket) MarshalJSON() ([]byte, error) {
	return json.Marshal([2]int{b.T, b.Amount})
}

// WaveFront is where each team's creeps reach in a lane at second T, written as [t, radiant, dire] (LaneWaves).
type WaveFront struct {
	T             int
	Radiant, Dire *int
}

// WaveSize counts living lane/siege creeps, parallel to WaveFront. AtFront counts
// creeps within 1,200 world units of their team's leading creep (zero if absent).
type WaveSize struct {
	T                                                      int
	RadiantInLane, DireInLane, RadiantAtFront, DireAtFront int
}

// MarshalJSON keeps counts separate so existing front tuple readers remain valid.
func (w WaveSize) MarshalJSON() ([]byte, error) {
	return json.Marshal([5]int{w.T, w.RadiantInLane, w.DireInLane, w.RadiantAtFront, w.DireAtFront})
}

// MarshalJSON writes [t, radiant, dire].
func (w WaveFront) MarshalJSON() ([]byte, error) {
	return json.Marshal([3]any{w.T, w.Radiant, w.Dire})
}

// HeroHit is the damage one player (hero, illusions, summons) dealt to one enemy player's hero in the BucketSeconds
// starting at T, written as [t, attacker, target, amount]. Like Bucket, it covers [T, T + BucketSeconds).
type HeroHit struct {
	T, Attacker, Target, Amount int
}

// MarshalJSON writes [t, attacker, target, amount].
func (h HeroHit) MarshalJSON() ([]byte, error) {
	return json.Marshal([4]int{h.T, h.Attacker, h.Target, h.Amount})
}

// CreepKind is what kind of creep a CreepKill was.
type CreepKind string

const (
	CreepLane    CreepKind = "lane"    // an enemy lane creep (a last hit)
	CreepDeny    CreepKind = "deny"    // a lane creep of the player's own team
	CreepNeutral CreepKind = "neutral" // a neutral creep
)

// CreepKill is a creep a player's unit got the last hit on, written as [t, player, kind, x, y]. The combat log has no
// location: x/y are where the player's hero was at that moment (close by: last hits are within attack range, or a
// summon's), null when the hero was not on the map.
type CreepKill struct {
	T      Seconds
	Player int
	Kind   CreepKind
	X, Y   *int
}

// MarshalJSON writes [t, player, kind, x, y].
func (c CreepKill) MarshalJSON() ([]byte, error) {
	return json.Marshal([5]any{c.T, c.Player, c.Kind, c.X, c.Y})
}

// SlotChange is [t, slot, "item_name"|null], observed every two seconds: 0-5 inventory,
// 15 teleport, 16 neutral. Backpack 6-8 and enhancement 17 are only in the final inventory.
// Changes between samples may be missed; a null clears the observed slot, not the player's ownership.
type SlotChange struct {
	T, Slot int
	Name    *string
}

// MarshalJSON writes [t, slot, name].
func (s SlotChange) MarshalJSON() ([]byte, error) {
	return json.Marshal([3]any{s.T, s.Slot, s.Name})
}

// Cooldown is [t, "name", readyAt], a detected rise in remaining cooldown and its estimated end.
// Later resets/reductions do not rewrite this estimate; it is not a guaranteed cast or ready event.
type Cooldown struct {
	T       Seconds
	Name    string
	ReadyAt Seconds
}

// MarshalJSON writes [t, name, readyAt].
func (c Cooldown) MarshalJSON() ([]byte, error) {
	return json.Marshal([3]any{c.T, c.Name, c.ReadyAt})
}

// TimedName is something that happened to a named item or ability, written as the tuple [t, "name"] (e.g.
// [34.1, "item_circlet"]).
type TimedName struct {
	// T is when it happened (game clock).
	T Seconds
	// Name is the internal name (item_blink, riki_smoke_screen).
	Name string
}

// MarshalJSON writes [t, "name"].
func (t TimedName) MarshalJSON() ([]byte, error) {
	return json.Marshal([2]any{t.T, t.Name})
}

// AbilityLevel is an ability upgrade, written as the tuple [t, "name", level] (e.g. [110, "riki_smoke_screen", 1]).
type AbilityLevel struct {
	// T is when the point was spent (game clock; negative for the point spent before the horn).
	T Seconds
	// Name is the internal ability name (talents are special_bonus_*).
	Name string
	// Level is the ability's level after the upgrade (1 for the first point).
	Level int
}

// MarshalJSON writes [t, "name", level].
func (a AbilityLevel) MarshalJSON() ([]byte, error) {
	return json.Marshal([3]any{a.T, a.Name, a.Level})
}

// Orders is [moveX, moveY, moves, attacks, casts] since the preceding living hero sample.
// The first sample includes orders from replay start; death resets the interval. Coordinates
// are the last move-to-position destination, rounded world units, or both null if absent.
// Counts are player-issued orders (also controlled units/items), not completed actions.
type Orders struct {
	MoveX, MoveY          *int
	Moves, Attacks, Casts int
}

// MarshalJSON writes the five-element intent tuple.
func (o Orders) MarshalJSON() ([]byte, error) {
	return json.Marshal([5]any{o.MoveX, o.MoveY, o.Moves, o.Attacks, o.Casts})
}

// Debuff is [start, end, source, target, modifier, kind]. Times are game-clock seconds; end is the matching
// REMOVE, otherwise start + reported nonnegative duration, otherwise null. Source is a nullable player index;
// the collector omits unknown owners because neither enemy nor self/ally application can be established. Target is a
// real hero's player index. Buffs reuses this tuple for self/ally sources with kind
// teleport/smoke/invisible/magicImmune/save/heal/other. Debuff kind is stun/silence/root/hex/slow/other, a conservative name heuristic, not proof
// of effective disable state. Reapplications/stacking with the same target/name/author pair FIFO.
type Debuff struct {
	Start    Seconds
	End      *Seconds
	Source   *int
	Target   int
	Modifier string
	Kind     string
}

// MarshalJSON writes [start, end, source, target, modifier, kind].
func (d Debuff) MarshalJSON() ([]byte, error) {
	return json.Marshal([6]any{d.Start, d.End, d.Source, d.Target, d.Modifier, d.Kind})
}

// BuildingHealthSample is a sparse [t, health, maxHealth] reading on the game clock.
type BuildingHealthSample struct{ T, Health, MaxHealth int }

// MarshalJSON keeps health timelines compact because buildings may regenerate every sample.
func (s BuildingHealthSample) MarshalJSON() ([]byte, error) {
	return json.Marshal([3]int{s.T, s.Health, s.MaxHealth})
}

// WaveStrength sums current health; centers are mean x+y within the front radius.
type WaveStrength struct {
	T                                                                  int
	RadiantHealth, DireHealth, RadiantHealthAtFront, DireHealthAtFront int
	RadiantSiege, DireSiege                                            int
	RadiantCenter, DireCenter                                          *int
}

// MarshalJSON preserves the nine-field strength tuple alongside the existing fronts and sizes.
func (w WaveStrength) MarshalJSON() ([]byte, error) {
	return json.Marshal([9]any{w.T, w.RadiantHealth, w.DireHealth, w.RadiantHealthAtFront, w.DireHealthAtFront, w.RadiantSiege, w.DireSiege, w.RadiantCenter, w.DireCenter})
}

// OffLaneCreeps counts living lane/siege creeps outside all lane regions, every two seconds.
type OffLaneCreeps struct {
	T, RadiantCount, DireCount                       int
	RadiantMeanX, RadiantMeanY, DireMeanX, DireMeanY *int
}

// MarshalJSON keeps absent teams distinguishable from a real mean coordinate of zero.
func (o OffLaneCreeps) MarshalJSON() ([]byte, error) {
	return json.Marshal([7]any{o.T, o.RadiantCount, o.DireCount, o.RadiantMeanX, o.RadiantMeanY, o.DireMeanX, o.DireMeanY})
}

// LaneCreepDeath keeps the creep's sampled position, never the killer's position.
// Lane is null off lane or when unknown; KillerPlayer is null for unowned killers.
type LaneCreepDeath struct {
	T            Seconds
	Lane         *string
	CreepTeam    Team
	KillerKind   string
	KillerPlayer *int
	X, Y         *int
}

// MarshalJSON preserves null identity and position fields without borrowing killer coordinates.
func (d LaneCreepDeath) MarshalJSON() ([]byte, error) {
	return json.Marshal([7]any{d.T, d.Lane, d.CreepTeam, d.KillerKind, d.KillerPlayer, d.X, d.Y})
}
