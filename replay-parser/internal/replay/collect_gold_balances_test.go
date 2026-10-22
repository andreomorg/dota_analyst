package replay

import (
	"context"
	"encoding/json"
	"testing"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
)

func TestGoldBalancesOptionalComponentsAndBounds(t *testing.T) {
	reliable := 40
	b, err := json.Marshal(matchdata.GoldBalance{T: 2, Player: 3, Reliable: &reliable})
	if err != nil || string(b) != "[2,3,40,null]" {
		t.Fatalf("json=%s err=%v", b, err)
	}
	c := goldBalancesCollector{samples: []matchdata.GoldBalance{{T: 0}, {T: 2}, {T: 4}}}
	m := matchdata.New()
	m.Match.DurationSeconds = 2.4
	if err := c.Fill(&m); err != nil || len(m.Timelines.GoldBalances) != 2 {
		t.Fatalf("samples=%v err=%v", m.Timelines.GoldBalances, err)
	}
}

func TestGoldBalancesLocalReplay(t *testing.T) {
	m, err := analyzeMatch(context.Background(), allPickReplay.open(t), []Collector{
		&matchCollector{}, &playersCollector{}, &goldBalancesCollector{},
	})
	if err != nil {
		t.Fatal(err)
	}
	if len(m.Timelines.GoldBalances) == 0 {
		t.Fatal("no balances captured")
	}
	vitals := map[[2]int]*int{}
	for _, p := range m.Timelines.Positions {
		for i, sample := range p.Samples {
			vitals[[2]int{p.Player, sample.T}] = p.Vitals[i].Gold
		}
	}
	compared, withoutPosition := 0, 0
	seen := map[[2]int]bool{}
	for _, g := range m.Timelines.GoldBalances {
		key := [2]int{g.Player, g.T}
		if seen[key] || g.T < 0 || float64(g.T) > float64(m.Match.DurationSeconds) || g.T%2 != 0 {
			t.Fatalf("invalid balance %v", g)
		}
		seen[key] = true
		if g.Reliable == nil || g.Unreliable == nil || *g.Reliable < 0 || *g.Unreliable < 0 {
			t.Fatalf("missing/negative component %v", g)
		}
		if gold, ok := vitals[key]; ok && gold != nil {
			if *g.Reliable+*g.Unreliable != *gold {
				t.Fatalf("balance does not match vitals at %v", key)
			}
			compared++
		} else {
			withoutPosition++
		}
	}
	if compared == 0 || withoutPosition == 0 {
		t.Fatalf("compared=%d withoutPosition=%d", compared, withoutPosition)
	}
	t.Logf("balances=%d comparedToVitals=%d withoutPosition=%d", len(m.Timelines.GoldBalances), compared, withoutPosition)
}
