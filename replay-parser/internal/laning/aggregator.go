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

// MinLaneShare is the smallest share of the counted seconds the most frequent zone needs to be the player's lane.
// Measured on 12/10/2026 against OpenDota's lanes for 80 players of 8 parsed matches: in every case where a 50%
// rule said "roaming" but OpenDota named a lane, our most frequent zone was that lane (with 39–49% of the time:
// heroes in a lane still spend a lot of it in the river and jungle), and OpenDota's own roamers had 44–54%, so no
// threshold separates them. The most frequent zone matched 72 of 80 against 67 with 50%; below 35% (four zones would
// give 25% each by chance) the player really had no lane.
const MinLaneShare = 0.35

// Lane is the zone where the player in slot spent the most counted seconds (the base is not counted), if it holds at
// least MinLaneShare of them: one of the three lanes or the jungle. Otherwise (a tie for first, too spread out, or no
// counted second) it is Roaming.
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
	// A tie for first (e.g. 50/50) means no zone wins.
	if counted == 0 || tie || float64(bestSeconds) < MinLaneShare*float64(counted) {
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
