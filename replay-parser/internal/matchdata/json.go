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
