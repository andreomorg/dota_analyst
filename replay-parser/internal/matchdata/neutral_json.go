package matchdata

import "encoding/json"

// CampCount is [t, aliveNeutrals]; only the first reading and count changes are stored.
type CampCount struct{ T, Alive int }

// MarshalJSON keeps frequent readings compact without conflating zero with missing data.
func (s CampCount) MarshalJSON() ([]byte, error) { return json.Marshal([2]int{s.T, s.Alive}) }
