package laning

import (
	"reflect"
	"testing"
)

// A point well inside each zone.
var points = map[Zone][2]float64{
	ZoneTop:    {-6450, 0},
	ZoneMiddle: {0, 0},
	ZoneBottom: {0, -6250},
	ZoneJungle: {1500, -3800},
	ZoneBase:   {-7456, -6938},
}

func TestAggregatorLane(t *testing.T) {
	tests := []struct {
		name    string
		seconds map[Zone]int // seconds spent in each zone, from 1:00 on
		want    Lane
	}{
		{"always in one lane", map[Zone]int{ZoneBottom: 540}, Bottom},
		{"most of the time in the middle", map[Zone]int{ZoneMiddle: 300, ZoneJungle: 100, ZoneTop: 100}, Middle},
		{"exactly half in a lane", map[Zone]int{ZoneTop: 270, ZoneJungle: 135, ZoneMiddle: 135}, Top},
		{"jungle most of the time", map[Zone]int{ZoneJungle: 400, ZoneBottom: 140}, Jungle},
		{"split between lanes", map[Zone]int{ZoneTop: 200, ZoneMiddle: 180, ZoneBottom: 160}, Roaming},
		{"half and half", map[Zone]int{ZoneTop: 100, ZoneBottom: 100}, Roaming},
		{"the base is not counted", map[Zone]int{ZoneBase: 400, ZoneMiddle: 100, ZoneJungle: 40}, Middle},
		{"only in the base", map[Zone]int{ZoneBase: 540}, Roaming},
		{"no samples", map[Zone]int{}, Roaming},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			var aggregator Aggregator
			second := SamplesFromSeconds
			for zone, seconds := range test.seconds {
				for range seconds {
					aggregator.AddSample(second, 3, points[zone][0], points[zone][1])
					second++
				}
			}
			if got := aggregator.Lane(3); got != test.want {
				t.Errorf("Lane = %q, want %q", got, test.want)
			}
		})
	}
}

func TestAggregatorIgnoresSamplesOutsideTheLaningPhase(t *testing.T) {
	var aggregator Aggregator
	middle, top := points[ZoneMiddle], points[ZoneTop]
	for second := range SamplesFromSeconds { // walking to the lane: ignored
		aggregator.AddSample(second, 0, top[0], top[1])
	}
	for second := EndsAtSeconds; second < EndsAtSeconds+100; second++ { // after 10:00: ignored
		aggregator.AddSample(second, 0, top[0], top[1])
	}
	aggregator.AddSample(SamplesFromSeconds, 0, middle[0], middle[1])
	aggregator.AddSample(EndsAtSeconds-1, 0, middle[0], middle[1])

	if got := aggregator.Lane(0); got != Middle {
		t.Errorf("Lane = %q, want %q", got, Middle)
	}
}

func TestAggregatorResult(t *testing.T) {
	var aggregator Aggregator
	top, bottom := points[ZoneTop], points[ZoneBottom]
	for second := SamplesFromSeconds; second < 300; second++ {
		aggregator.AddSample(second, 0, top[0], top[1])
		aggregator.AddSample(second, 7, bottom[0], bottom[1])
	}

	got := aggregator.Result(300, []PlayerAtEnd{
		{Slot: 7, PlayerLaning: PlayerLaning{HeroID: 28, SteamID: 2, LastHits: 10, Lane: Middle}}, // Lane is replaced
		{Slot: 0, PlayerLaning: PlayerLaning{HeroID: 32, SteamID: 1, IsRadiant: true, Denies: 4, NetWorth: 900, XP: 1200, Deaths: 1}},
		{Slot: 4, PlayerLaning: PlayerLaning{HeroID: 31, IsRadiant: true}}, // no samples (dead or in base the whole time)
	})

	want := Result{EndsAtSeconds: 300, Players: []PlayerLaning{
		{HeroID: 28, SteamID: 2, LastHits: 10, Lane: Bottom},
		{HeroID: 32, SteamID: 1, IsRadiant: true, Lane: Top, Denies: 4, NetWorth: 900, XP: 1200, Deaths: 1},
		{HeroID: 31, IsRadiant: true, Lane: Roaming},
	}}
	if !reflect.DeepEqual(got, want) {
		t.Errorf("Result =\n%+v\nwant\n%+v", got, want)
	}
}

func TestAggregatorResultWithoutPlayers(t *testing.T) {
	var aggregator Aggregator
	got := aggregator.Result(EndsAtSeconds, nil)
	if got.EndsAtSeconds != EndsAtSeconds || got.Players == nil || len(got.Players) != 0 {
		t.Errorf("Result = %+v, want no players (an empty list, not nil, so the JSON has [])", got)
	}
}
