package replay

import (
	"context"
	"encoding/json"
	"testing"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
)

func TestEconomyPrecisionTuple(t *testing.T) {
	gold, seen := 30, 1
	b, err := json.Marshal(matchdata.FarmUnit{ID: "100:4", Name: "neutral", Team: 4, X: 1, Y: 2, Health: 100, MinGold: &gold, SeenRadiant: &seen})
	if err != nil || string(b) != `["100:4","neutral",4,1,2,100,30,null,1,null]` {
		t.Fatalf("%s %v", b, err)
	}
}

func TestEconomyPrecisionLocalReplays(t *testing.T) {
	for _, r := range []realReplay{allPickReplay, {path: "../../../work/replays/9030042976.dem"}, {path: "../../../work/replays/9042407280.dem"}} {
		t.Run(r.path, func(t *testing.T) {
			m, err := (MatchAnalyzer{}).Analyze(context.Background(), r.open(t))
			if err != nil {
				t.Fatal(err)
			}
			if len(m.Timelines.EconomyStates) == 0 || m.Timelines.GoldLossChanges == nil || len(m.Timelines.FarmSnapshots) == 0 {
				t.Fatal("missing observation families")
			}
			for _, p := range m.Players {
				loss := 0
				for _, c := range *m.Timelines.GoldLossChanges {
					if c.Player == p.Player {
						loss += c.After - c.Before
					}
				}
				if loss != p.Final.GoldLostToDeath {
					t.Fatalf("player%d counter loss%d final%d", p.Player, loss, p.Final.GoldLostToDeath)
				}
			}
			resources := 0
			for _, snapshot := range m.Timelines.FarmSnapshots {
				seen := map[string]bool{}
				if snapshot.T%10 != 0 || float64(snapshot.T) > float64(m.Match.DurationSeconds) {
					t.Fatal("invalid snapshot time")
				}
				for _, u := range snapshot.Units {
					if seen[u.ID] || u.Health <= 0 {
						t.Fatal("duplicate/dead resource")
					}
					seen[u.ID] = true
					resources++
				}
			}
			b, err := json.Marshal(m)
			if err != nil {
				t.Fatal(err)
			}
			if len(b) > 16*1024*1024 {
				t.Fatalf("document exceeds HTTP cap: %d", len(b))
			}
			t.Logf("states=%d changes=%d snapshots=%d resourceRows=%d jsonBytes=%d", len(m.Timelines.EconomyStates), len(*m.Timelines.GoldLossChanges), len(m.Timelines.FarmSnapshots), resources, len(b))
		})
	}
}
