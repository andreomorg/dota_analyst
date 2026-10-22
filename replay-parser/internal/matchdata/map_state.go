package matchdata

import "encoding/json"

// ----- map state (MAPA MP3) -----

// TreeTimeline is which of the map's trees stand during the match: m_bWorldTreeState of CDOTA_DataRadiant has one bit
// per tree of the map file, in the map file's order (checked on 22/10/2026: 2,475 bits on = the 2,475 trees of the 7.41
// map, and cut trees have a hero next to them only in that order). Index = word*64 + bit. Read every
// PositionIntervalSeconds from 0:00; DeadAtStart is the trees already down at the first reading, Changes every bit that
// flips afterwards, in time order. Always present in new documents (empty lists when unknown); absent in older ones.
type TreeTimeline struct {
	DeadAtStart List[int]        `json:"deadAtStart"`
	Changes     List[TreeChange] `json:"changes"`
}

// TreeChange is [t, index, alive]: tree index stood up (true, regrown) or went down (false) at second t.
type TreeChange struct {
	T, Index int
	Alive    bool
}

// MarshalJSON writes [t, index, alive].
func (c TreeChange) MarshalJSON() ([]byte, error) {
	return json.Marshal([3]any{c.T, c.Index, c.Alive})
}

// TempTree is [t, x, y, present]: a temporary tree (Iron Branch, Sprout...) appeared (true) or went away (false).
type TempTree struct {
	T       Seconds
	X, Y    int
	Present bool
}

// MarshalJSON writes [t, x, y, present].
func (t TempTree) MarshalJSON() ([]byte, error) {
	return json.Marshal([4]any{t.T, t.X, t.Y, t.Present})
}

// BossTrack is one Roshan or Tormentor unit (a new unit after a respawn is a new track), in the order first seen.
// Samples are read every PositionIntervalSeconds and kept only when the unit moved more than 200 or lived/died (the
// first always): which pit Roshan is in, and his walks between pits.
type BossTrack struct {
	// Kind is "roshan" or "tormentor".
	Kind    string           `json:"kind"`
	Samples List[BossSample] `json:"samples"`
}

// BossSample is [t, x, y, alive].
type BossSample struct {
	T, X, Y int
	Alive   bool
}

// MarshalJSON writes [t, x, y, alive].
func (b BossSample) MarshalJSON() ([]byte, error) {
	return json.Marshal([4]any{b.T, b.X, b.Y, b.Alive})
}

// LanternUse is [t, player, x, y]: a Watcher (lantern) activated (combat log ability_lamp_use on npc_dota_lantern).
// The log has no location: x/y are the player's hero at that moment, null when unknown.
type LanternUse struct {
	T      Seconds
	Player int
	X, Y   *int
}

// MarshalJSON writes [t, player, x, y].
func (l LanternUse) MarshalJSON() ([]byte, error) {
	return json.Marshal([4]any{l.T, l.Player, l.X, l.Y})
}

// VisionUnits is the moving vision of both teams at second t (every PositionIntervalSeconds), besides what other
// sections already have (players' heroes, wards, towers, barracks, Ancients) and the fixed buildings: lane and siege
// creeps, summons, illusions, controlled neutrals and any other living unit of a team with a vision range. Units in the
// same 128-unit cell with the same ranges are one circle (a wave of creeps is a few), at the cell's centre. Written
// [t, [[x, y, day, night]...radiant], [[...]...dire]]; ranges are the unit's own (m_iDayTimeVisionRange /
// m_iNightTimeVisionRange). Always present in new documents (MP4); absent in older ones.
type VisionUnits struct {
	T             int
	Radiant, Dire List[VisionCircle]
}

// MarshalJSON writes [t, radiant, dire].
func (v VisionUnits) MarshalJSON() ([]byte, error) {
	return json.Marshal([3]any{v.T, v.Radiant, v.Dire})
}

// VisionCircle is [x, y, day, night].
type VisionCircle struct{ X, Y, Day, Night int }

// MarshalJSON writes [x, y, day, night].
func (c VisionCircle) MarshalJSON() ([]byte, error) {
	return json.Marshal([4]int{c.X, c.Y, c.Day, c.Night})
}

// GoldEvent is [t, player, amount, reason, x, y]: one GOLD entry of the combat log with where it happened. Amount is
// signed (losses negative, as in Economy); reason is the raw gold_reason, null when absent; x/y null when the entry has
// no location.
type GoldEvent struct {
	T      Seconds
	Player int
	Amount int64
	Reason *uint32
	X, Y   *int
}

// MarshalJSON writes [t, player, amount, reason, x, y].
func (g GoldEvent) MarshalJSON() ([]byte, error) {
	return json.Marshal([6]any{g.T, g.Player, g.Amount, g.Reason, g.X, g.Y})
}
