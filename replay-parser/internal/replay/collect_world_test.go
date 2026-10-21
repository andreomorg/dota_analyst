package replay

import (
	"context"
	"testing"
)

// The local All Pick replay starts in daylight and spans several natural day/night cycles.
func TestWorldCollectorAllPick(t *testing.T) {
	match, err := analyzeMatch(context.Background(), allPickReplay.open(t), []Collector{&matchCollector{}, &worldCollector{}})
	if err != nil {
		t.Fatal(err)
	}
	changes := match.Timelines.DayNight
	if len(changes) < 4 {
		t.Fatalf("%d day/night states, want several cycles", len(changes))
	}
	if changes[0].T != 0 || !changes[0].IsDay {
		t.Errorf("first state %+v, want [0, true]", changes[0])
	}
	// The session samples the previously replicated entity snapshot: allow one
	// second of replication delay at the first natural night, never a pre-game night.
	if changes[1].IsDay || changes[1].T < 300 || changes[1].T > 301 {
		t.Errorf("first night %+v, want 300..301 seconds", changes[1])
	}
	for i := 1; i < len(changes); i++ {
		if changes[i].T <= changes[i-1].T || changes[i].IsDay == changes[i-1].IsDay ||
			float64(changes[i].T) > float64(match.Match.DurationSeconds) {
			t.Errorf("invalid successive states %+v, %+v", changes[i-1], changes[i])
		}
	}
}

func TestNaturalDayQuantizedBoundaries(t *testing.T) {
	for _, test := range []struct {
		name             string
		time, day, night float64
		want             bool
	}{
		{"before dawn", 16382, .25, .75, false},
		{"dawn at horn", 16383, .25, .75, true},
		{"before dusk", 49150, .25, .75, true},
		{"dusk", 49151, .25, .75, false},
		{"wrapped day", 0, .75, .25, true},
		{"wrapped night", 16383, .75, .25, false},
		{"wrapped dawn", 49151, .75, .25, true},
	} {
		t.Run(test.name, func(t *testing.T) {
			if got := naturalDay(test.time, test.day, test.night); got != test.want {
				t.Errorf("naturalDay(%v, %v, %v) = %v, want %v", test.time, test.day, test.night, got, test.want)
			}
		})
	}
}
