package replay

import (
	"context"
	"testing"
)

func TestNeutralCampsRealReplay(t *testing.T) {
	m, err := analyzeMatch(context.Background(), allPickReplay.open(t), []Collector{&matchCollector{}, &neutralsCollector{}})
	if err != nil {
		t.Fatal(err)
	}
	if len(m.Objectives.NeutralCamps) != 28 {
		t.Fatalf("%d camps", len(m.Objectives.NeutralCamps))
	}
	ids := map[string]bool{}
	zeros, positive := 0, 0
	for _, camp := range m.Objectives.NeutralCamps {
		if ids[camp.ID] || camp.Type == nil || camp.SpawnerType == nil || len(camp.Alive) == 0 {
			t.Fatalf("bad identity %+v", camp)
		}
		ids[camp.ID] = true
		for i, row := range camp.Alive {
			if row.T%2 != 0 || row.Alive < 0 || i > 0 && (row.T <= camp.Alive[i-1].T || row.Alive == camp.Alive[i-1].Alive) {
				t.Fatalf("bad sparse reading %+v", row)
			}
			if row.Alive == 0 {
				zeros++
			} else {
				positive++
			}
		}
	}
	if zeros == 0 || positive == 0 {
		t.Fatal("missing clears/spawns")
	}
}
