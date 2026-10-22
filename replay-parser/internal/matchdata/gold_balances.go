package matchdata

import "encoding/json"

// GoldBalance is [game second, player, reliable gold | null, unreliable gold | null].
// Components are independent optional observations, not reconstructed from earned gold/net worth.
type GoldBalance struct {
	T, Player            int
	Reliable, Unreliable *int
}

func (g GoldBalance) MarshalJSON() ([]byte, error) {
	return json.Marshal([4]any{g.T, g.Player, g.Reliable, g.Unreliable})
}
