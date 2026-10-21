package matchdata

import "encoding/json"

// CreepHeroDamage is [t, lane, creepTeam, targetPlayer, hits]. T closes (t-2,t];
// readers may use it only at/after T. Lane is the victim's observed lane at the hit.
// Rows with zero hits are omitted. This does not measure attack orders or aggro.
type CreepHeroDamage struct {
	T                  int
	Lane               string
	CreepTeam          Team
	TargetPlayer, Hits int
}

// MarshalJSON keeps the fallback's semantics distinct from entity target counts.
func (d CreepHeroDamage) MarshalJSON() ([]byte, error) {
	return json.Marshal([5]any{d.T, d.Lane, d.CreepTeam, d.TargetPlayer, d.Hits})
}
