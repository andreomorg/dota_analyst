package laning

// SamplesFromSeconds is the game clock from which positions say which lane a hero played: before 1:00 heroes are
// still walking to their lanes or stacking/pulling in the jungle.
const SamplesFromSeconds = 60

// Aggregator counts, for each player, the seconds spent in each zone during the laning phase and turns them into
// lanes. The zero value is ready to use. It is not safe for concurrent use (one per parse).
type Aggregator struct {
	seconds map[int]map[Zone]int // player slot -> zone -> seconds
}

// AddSample records where the player in slot was at second of the game clock. Call it once per second and only
// for living heroes (a dead hero's position says nothing about the lane); seconds outside 1:00..10:00 are ignored.
func (a *Aggregator) AddSample(second, slot int, x, y float64) {
	if second < SamplesFromSeconds || second >= EndsAtSeconds {
		return
	}
	if a.seconds == nil {
		a.seconds = map[int]map[Zone]int{}
	}
	zones := a.seconds[slot]
	if zones == nil {
		zones = map[Zone]int{}
		a.seconds[slot] = zones
	}
	zones[ZoneOf(x, y)]++
}

// Lane is the zone where the player in slot spent at least half of the counted seconds (the base is not counted):
// one of the three lanes or the jungle. Without such a zone, or without any counted second, it is Roaming.
func (a *Aggregator) Lane(slot int) Lane {
	zones := a.seconds[slot] // reading a nil map is fine
	counted, best, bestSeconds, tie := 0, Roaming, 0, false
	// A fixed order keeps the result the same on every run (map iteration order is random).
	for _, candidate := range []struct {
		zone Zone
		lane Lane
	}{{ZoneTop, Top}, {ZoneMiddle, Middle}, {ZoneBottom, Bottom}, {ZoneJungle, Jungle}} {
		seconds := zones[candidate.zone]
		counted += seconds
		switch {
		case seconds > bestSeconds:
			best, bestSeconds, tie = candidate.lane, seconds, false
		case seconds == bestSeconds && seconds > 0:
			tie = true
		}
	}
	// An exact 50/50 split is a tie: no zone wins.
	if counted == 0 || tie || bestSeconds*2 < counted {
		return Roaming
	}
	return best
}

// PlayerAtEnd is one player's numbers when the laning phase ended, as read from the replay. Slot links the numbers
// to the position samples; PlayerLaning.Lane is ignored (Result fills it in).
type PlayerAtEnd struct {
	Slot int
	PlayerLaning
}

// Result builds the laning phase of the match from the players' numbers taken at endsAtSeconds of the game clock and
// the samples recorded so far. Players keep the given order.
func (a *Aggregator) Result(endsAtSeconds int, players []PlayerAtEnd) Result {
	result := Result{EndsAtSeconds: endsAtSeconds, Players: make([]PlayerLaning, 0, len(players))}
	for _, player := range players {
		laning := player.PlayerLaning
		laning.Lane = a.Lane(player.Slot)
		result.Players = append(result.Players, laning)
	}
	return result
}
