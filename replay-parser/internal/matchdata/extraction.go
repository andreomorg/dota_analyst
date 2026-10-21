package matchdata

import "encoding/json"

// AbilityTarget adds metadata without changing the existing [t,name] use tuple.
type AbilityTarget struct {
	Player, X, Y *int
	PointSource  *int
}

// MarshalJSON writes the documented flat tuple to keep the additive metadata compact.
func (v AbilityTarget) MarshalJSON() ([]byte, error) {
	return json.Marshal([4]any{v.Player, v.X, v.Y, v.PointSource})
}

// RuneOnMap is [t, runeType, x, y, appeared], appeared 1=present, 0=gone.
type RuneOnMap struct{ T, RuneType, X, Y, Appeared int }

// MarshalJSON writes the documented flat tuple to keep the additive metadata compact.
func (v RuneOnMap) MarshalJSON() ([]byte, error) {
	return json.Marshal([5]int{v.T, v.RuneType, v.X, v.Y, v.Appeared})
}

// EconomyBucket retains raw reasons so patch-dependent categories are not guessed.
type EconomyBucket struct {
	T                int
	Player, Resource int
	Reason           *uint32
	Amount           int64
}

// MarshalJSON writes the documented flat tuple to keep the additive metadata compact.
func (v EconomyBucket) MarshalJSON() ([]byte, error) {
	return json.Marshal([5]any{v.T, v.Player, v.Resource, v.Reason, v.Amount})
}

// MapPing is [t, player, x, y, rawType] in world coordinates.
type MapPing struct {
	T            Seconds
	Player, X, Y int
	Type         *uint32
}

// MarshalJSON writes the documented flat tuple to keep the additive metadata compact.
func (v MapPing) MarshalJSON() ([]byte, error) {
	return json.Marshal([5]any{v.T, v.Player, v.X, v.Y, v.Type})
}

// ChatWheel is [t, player, phraseId]; it intentionally cannot contain free text.
type ChatWheel struct {
	T      Seconds
	Player int
	Phrase uint32
}

// MarshalJSON writes the documented flat tuple to keep the additive metadata compact.
func (v ChatWheel) MarshalJSON() ([]byte, error) {
	return json.Marshal([3]any{v.T, v.Player, v.Phrase})
}
