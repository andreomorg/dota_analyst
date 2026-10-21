package replay

import (
	"context"
	"testing"
)

func TestCreepDamageIntervalEnd(t *testing.T) {
	for _, tc := range []struct {
		t    float64
		want int
	}{{0, 0}, {.1, 2}, {2, 2}, {2.001, 4}, {5.9, 6}} {
		if got := creepDamageSecond(tc.t); got != tc.want {
			t.Errorf("%g: %d want %d", tc.t, got, tc.want)
		}
	}
}

func TestCreepDamageRealReplay(t *testing.T) {
	m, err := analyzeMatch(context.Background(), allPickReplay.open(t), []Collector{&matchCollector{}, &creepDamageCollector{}})
	if err != nil {
		t.Fatal(err)
	}
	if len(m.Timelines.CreepHeroDamage) == 0 {
		t.Fatal("no logged creep hits")
	}
	for i, row := range m.Timelines.CreepHeroDamage {
		if row.T%2 != 0 || float64(row.T) > float64(m.Match.DurationSeconds) || row.Hits <= 0 || row.TargetPlayer < 0 || row.TargetPlayer >= 10 || row.Lane != "top" && row.Lane != "middle" && row.Lane != "bottom" {
			t.Fatalf("bad hit %+v", row)
		}
		if row.CreepTeam != "radiant" && row.CreepTeam != "dire" {
			t.Fatal(row)
		}
		if i > 0 && row.T < m.Timelines.CreepHeroDamage[i-1].T {
			t.Fatal("unordered hits")
		}
	}
}
