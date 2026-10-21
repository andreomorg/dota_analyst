package matchdata

import "encoding/json"

// WaveComposition is [t, rMelee, rRanged, rSiege, rFlagbearer, rSuper, rMega, rLowAtFront,
// dMelee, dRanged, dSiege, dFlagbearer, dSuper, dMega, dLowAtFront]. Categories use unit
// names and are mutually exclusive (mega/super take precedence); LowAtFront overlaps them.
// LowAtFront requires known positive max health and current health <= 25% of it.
type WaveComposition struct {
	T             int
	Radiant, Dire [7]int
}

// MarshalJSON preserves a flat, fixed-width tuple alongside Fronts.
func (w WaveComposition) MarshalJSON() ([]byte, error) {
	values := [15]int{w.T}
	copy(values[1:8], w.Radiant[:])
	copy(values[8:], w.Dire[:])
	return json.Marshal(values)
}
