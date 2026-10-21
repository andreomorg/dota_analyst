package replay

import (
	"context"
	"testing"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
)

func TestWaveLane(t *testing.T) {
	tests := []struct {
		x, y float64
		want string
	}{
		{-6450, 0, "top"},        // Radiant side of the top lane
		{0, 5950, "top"},         // Dire side of the top lane
		{0, -6250, "bottom"},     // the bottom lane
		{100, -100, "middle"},    // the middle of the map
		{-3000, 1000, ""},        // the jungle
		{-5000, -5000, "middle"}, // the Radiant base, on the diagonal
		{-7000, -4500, "top"},    // the Radiant base, top side
		{6000, 4000, "bottom"},   // the Dire base, bottom side
	}
	for _, test := range tests {
		if got := waveLane(test.x, test.y); got != test.want {
			t.Errorf("waveLane(%v, %v) = %q, want %q", test.x, test.y, got, test.want)
		}
	}
}

// TestWavesCollector reads real replays: every lane has fronts during the match, in time order, on the sampling
// seconds, with progress values inside the map (x + y within about ±2 x 8200).
func TestWavesCollector(t *testing.T) {
	for _, replay := range []realReplay{allPickReplay, turboReplay} {
		t.Run(replay.path, func(t *testing.T) {
			match, err := analyzeMatch(context.Background(), replay.open(t), []Collector{&matchCollector{}, &wavesCollector{}})
			if err != nil {
				t.Fatal(err)
			}
			if len(match.Timelines.Waves) != 3 {
				t.Fatalf("%d lanes", len(match.Timelines.Waves))
			}
			for i, off := range match.Timelines.OffLaneCreeps {
				if off.T%matchdata.PositionIntervalSeconds != 0 || i > 0 && off.T <= match.Timelines.OffLaneCreeps[i-1].T || off.RadiantCount < 0 || off.DireCount < 0 || off.RadiantCount+off.DireCount == 0 || (off.RadiantMeanX == nil) != (off.RadiantCount == 0) || (off.RadiantMeanY == nil) != (off.RadiantCount == 0) || (off.DireMeanX == nil) != (off.DireCount == 0) || (off.DireMeanY == nil) != (off.DireCount == 0) {
					t.Errorf("bad off-lane sample %+v", off)
				}
			}
			for i, lane := range match.Timelines.Waves {
				if len(lane.Strength) != len(lane.Fronts) {
					t.Fatalf("lane %q: strength/front length mismatch", lane.Lane)
				}
				if len(lane.Sizes) != len(lane.Fronts) {
					t.Fatalf("lane %q: %d sizes, %d fronts", lane.Lane, len(lane.Sizes), len(lane.Fronts))
				}
				if lane.Lane != waveLanes[i] || len(lane.Fronts) < 100 {
					t.Errorf("lane %q: %d fronts", lane.Lane, len(lane.Fronts))
				}
				for j, front := range lane.Fronts {
					size := lane.Sizes[j]
					strength := lane.Strength[j]
					if strength.T != front.T || strength.RadiantHealth < 0 || strength.DireHealth < 0 || strength.RadiantHealthAtFront < 0 || strength.DireHealthAtFront < 0 || strength.RadiantHealthAtFront > strength.RadiantHealth || strength.DireHealthAtFront > strength.DireHealth || (strength.RadiantCenter == nil) != (size.RadiantAtFront == 0) || (strength.DireCenter == nil) != (size.DireAtFront == 0) || strength.RadiantSiege < 0 || strength.RadiantSiege > 1 || strength.DireSiege < 0 || strength.DireSiege > 1 {
						t.Errorf("lane %s: inconsistent strength %+v", lane.Lane, strength)
					}
					if size.T != front.T || size.RadiantAtFront < 0 || size.DireAtFront < 0 ||
						size.RadiantAtFront > size.RadiantInLane || size.DireAtFront > size.DireInLane ||
						front.Radiant != nil && size.RadiantInLane <= 0 || front.Dire != nil && size.DireInLane <= 0 ||
						front.Radiant == nil && (size.RadiantInLane != 0 || size.RadiantAtFront != 0) ||
						front.Dire == nil && (size.DireInLane != 0 || size.DireAtFront != 0) {
						t.Errorf("lane %q: inconsistent size %+v for front %+v", lane.Lane, size, front)
					}
					inMap := func(v *int) bool { return v == nil || *v > -17000 && *v < 17000 }
					if front.T%matchdata.PositionIntervalSeconds != 0 || j > 0 && front.T <= lane.Fronts[j-1].T ||
						front.Radiant == nil && front.Dire == nil || !inMap(front.Radiant) || !inMap(front.Dire) {
						t.Errorf("lane %q: bad front %+v", lane.Lane, front)
						break
					}
				}
			}
		})
	}
}
