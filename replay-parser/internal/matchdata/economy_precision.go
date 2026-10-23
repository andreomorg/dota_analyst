package matchdata

import "encoding/json"

// EconomyState is an observed player resource sample, including while dead. CustomCost is a raw override,
// NOT the final engine price (zero does not mean a free buyback).
type EconomyState struct {
	T                 int      `json:"t"`
	Player            int      `json:"player"`
	NetWorth          *int     `json:"netWorth"`
	Reliable          *int     `json:"reliable"`
	Unreliable        *int     `json:"unreliable"`
	CooldownRemaining *float64 `json:"cooldownRemaining"`
	CustomCost        *int     `json:"customCost"`
}

// GoldLossChange is an increment of the replicated loss counter, not a causal death attribution.
type GoldLossChange struct {
	T                Seconds `json:"t"`
	Player           int     `json:"player"`
	Before           int     `json:"before"`
	After            int     `json:"after"`
	BeforeUnreliable *int    `json:"beforeUnreliable"`
	AfterUnreliable  *int    `json:"afterUnreliable"`
	PreviousAt       Seconds `json:"previousAt"`
}

type FarmSnapshot struct {
	T     int            `json:"t"`
	Units List[FarmUnit] `json:"units"`
}

// Compact complete live-unit snapshot. ID includes entity serial; bounty is the replicated BASE range, not a
// promised reward. Null visibility is unknown. Snapshots are spaced 10 s; never carry a unit forward as still alive.
type FarmUnit struct {
	ID, Name              string
	Team, X, Y, Health    int
	MinGold, MaxGold      *int
	SeenRadiant, SeenDire *int
}

func (u FarmUnit) MarshalJSON() ([]byte, error) {
	return json.Marshal([10]any{u.ID, u.Name, u.Team, u.X, u.Y, u.Health, u.MinGold, u.MaxGold, u.SeenRadiant, u.SeenDire})
}
