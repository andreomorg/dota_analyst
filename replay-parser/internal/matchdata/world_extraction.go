package matchdata

import "encoding/json"

// DamageTypeBucket retains damage direction and type without changing existing damage tuples.
type DamageTypeBucket [7]int

// HealingBucket includes self-healing in both directions; targets that are illusions are excluded.
type HealingBucket [3]int

// OutpostCaptureEvent is [t,outpostEntityIndex,team]; the index is local to this replay.
type OutpostCaptureEvent struct {
	T    Seconds
	ID   int
	Team Team
}

// MarshalJSON keeps the additive event compact without changing the legacy outpost objects.
func (v OutpostCaptureEvent) MarshalJSON() ([]byte, error) {
	return json.Marshal([3]any{v.T, v.ID, v.Team})
}

// TormentorState locates an observed site; Side is the spawner's replicated team, or null without a spawner.
// Samples is [t,alive], 1=alive, 0=dying/dead, null=missing or ambiguous. No scheduled spawn is assumed.
type TormentorState struct {
	Side    *Team                 `json:"side"`
	X       int                   `json:"x"`
	Y       int                   `json:"y"`
	Samples List[TormentorSample] `json:"samples"`
}
type TormentorSample struct {
	T     int
	Alive *int
}

// MarshalJSON preserves the distinction between observed death and an unobserved entity.
func (v TormentorSample) MarshalJSON() ([]byte, error) { return json.Marshal([2]any{v.T, v.Alive}) }

// CourierTimeline identifies one courier, rather than merging the team's five trajectories.
type CourierTimeline struct {
	ID      string              `json:"id"`
	Team    Team                `json:"team"`
	Samples List[CourierSample] `json:"samples"`
}

// CourierSample is [t,alive,x,y,flying]; flying is null if the property is absent.
type CourierSample struct {
	T, Alive, X, Y int
	Flying         *bool
}

// MarshalJSON keeps sparse world samples smaller than repeated field names.
func (v CourierSample) MarshalJSON() ([]byte, error) {
	return json.Marshal([5]any{v.T, v.Alive, v.X, v.Y, v.Flying})
}
