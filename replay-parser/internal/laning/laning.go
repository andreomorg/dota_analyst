// Package laning holds the rules of the laning phase (the first 10 minutes of the game clock): which lane each hero
// played and their numbers when it ended. It only uses the standard library: no HTTP, no manta, no Valve.
package laning

import "errors"

// EndsAtSeconds is the game clock (seconds after the horn, pauses left out) at which the laning phase ends.
const EndsAtSeconds = 600

// Lane is where a hero spent the laning phase.
type Lane string

const (
	Top     Lane = "top"
	Middle  Lane = "middle"
	Bottom  Lane = "bottom"
	Jungle  Lane = "jungle"
	Roaming Lane = "roaming"
)

// PlayerLaning is one hero's laning phase.
type PlayerLaning struct {
	HeroID    int
	SteamID   uint64 // 0 for bots and players without a Steam account
	IsRadiant bool
	Lane      Lane
	LastHits  int
	Denies    int
	NetWorth  int
	XP        int
	Deaths    int
}

// Result is the laning phase of a whole match.
type Result struct {
	// EndsAtSeconds is the game clock at which the numbers were taken: EndsAtSeconds, or less when the match ended first.
	EndsAtSeconds int
	Players       []PlayerLaning
}

// ErrUnreadableReplay means the replay could not be read (corrupt, truncated or from an unknown format).
var ErrUnreadableReplay = errors.New("unreadable replay")
