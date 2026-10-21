package replay

import (
	"context"
	"math"
	"testing"
)

func TestRuneChangesIdentityAndSparsity(t *testing.T) {
	old := map[entityKey]runeState{{index: 1, serial: 1}: {kind: 5, x: 10, y: 20}}
	if got := runeChanges(2, old, old); len(got) != 0 {
		t.Fatalf("unchanged rune emitted %+v", got)
	}
	current := map[entityKey]runeState{{index: 1, serial: 2}: {kind: 5, x: 10, y: 20}}
	got := runeChanges(4, old, current)
	if len(got) != 2 || got[0].Appeared != 0 || got[1].Appeared != 1 {
		t.Fatalf("serial replacement %+v", got)
	}
	if got := runeChanges(6, current, nil); len(got) != 1 || got[0].Appeared != 0 {
		t.Fatalf("missing removal %+v", got)
	}
}

func TestUniqueCastOrder(t *testing.T) {
	base := castOrder{at: ServerTime{Seconds: 100}, steam: 42, name: "spell", target: 0}
	tests := []struct {
		name   string
		orders []castOrder
		used   map[int]bool
		want   int
	}{
		{"unique", []castOrder{base}, nil, 0},
		{"ambiguous", []castOrder{base, base}, nil, -1},
		{"already consumed", []castOrder{base}, map[int]bool{0: true}, -1},
		{"different player", []castOrder{{at: base.at, steam: 43, name: base.name}}, nil, -1},
		{"different ability", []castOrder{{at: base.at, steam: 42, name: "other"}}, nil, -1},
		{"too late", []castOrder{{at: ServerTime{Seconds: 100.251}, steam: 42, name: base.name}}, nil, -1},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			if got := uniqueCastOrder(base.at, 42, "spell", tt.orders, tt.used); got != tt.want {
				t.Fatalf("got %d want %d", got, tt.want)
			}
		})
	}
}

// Real replay checks preserve totals and alignment instead of merely repeating tuple construction.
func TestExtractionAllPick(t *testing.T) {
	economy := &economyCollector{}
	targets := &abilityTargetsCollector{}
	m, err := analyzeMatch(context.Background(), allPickReplay.open(t), []Collector{&matchCollector{}, &itemsCollector{}, &visionCollector{}, &communicationCollector{}, economy, &runesCollector{}, targets})
	if err != nil {
		t.Fatal(err)
	}
	if len(m.Vision.Pings) != 128 || len(m.Vision.ChatWheel) != 33 {
		t.Fatalf("communication counts %d/%d", len(m.Vision.Pings), len(m.Vision.ChatWheel))
	}
	raw := map[economyKey]int64{}
	for _, e := range economy.events {
		p, ok := economy.session.Players.ByHeroName(e.hero)
		if !ok {
			continue
		}
		sec := economy.session.Clock.Game(e.at)
		if sec > float64(m.Match.DurationSeconds) {
			continue
		}
		k := economyKey{t: int(math.Floor(sec/2)) * 2, player: p, resource: e.resource, known: e.reason != nil}
		if e.reason != nil {
			k.reason = *e.reason
		}
		raw[k] += e.amount
	}
	if len(raw) != len(m.Combat.Economy) {
		t.Fatalf("raw keys %d buckets %d", len(raw), len(m.Combat.Economy))
	}
	var deathGold int64
	for _, b := range m.Combat.Economy {
		if b.Resource == 0 && b.Reason != nil && *b.Reason == 1 {
			deathGold += b.Amount
		}
		k := economyKey{t: b.T, player: b.Player, resource: b.Resource, known: b.Reason != nil}
		if b.Reason != nil {
			k.reason = *b.Reason
		}
		if want, ok := raw[k]; !ok || want != b.Amount {
			t.Fatalf("bucket not conserved %+v", b)
		}
	}
	if deathGold != -9021 {
		t.Fatalf("signed gold delta %d, want -9021", deathGold)
	}
	active := map[[3]int]bool{}
	appeared, gone := 0, 0
	for _, r := range m.Objectives.RunesOnMap {
		if r.T%2 != 0 {
			t.Fatalf("non 2s rune %+v", r)
		}
		key := [3]int{r.RuneType, r.X, r.Y}
		if (r.Appeared == 1) == active[key] {
			t.Fatalf("not sparse %+v", r)
		}
		active[key] = r.Appeared == 1
		if r.Appeared == 1 {
			appeared++
		} else {
			gone++
		}
	}
	if appeared != 28 || gone != 23 {
		t.Fatalf("rune changes %d/%d", appeared, gone)
	}
	casts, heroTargets, points := 0, 0, 0
	for _, u := range m.Abilities.Uses {
		if len(u.Targets) != len(u.Abilities) {
			t.Fatalf("unaligned player %d", u.Player)
		}
		for _, v := range u.Targets {
			casts++
			if v.Player != nil {
				heroTargets++
				if *v.Player < 0 || *v.Player >= playerCount {
					t.Fatal("invalid hero")
				}
			}
			if (v.X == nil) != (v.Y == nil) || (v.X == nil) != (v.PointSource == nil) {
				t.Fatal("partial point")
			}
			if v.X != nil {
				points++
			}
		}
	}
	if casts != 1058 || heroTargets == 0 || points == 0 {
		t.Fatalf("cast coverage %d/%d/%d", casts, heroTargets, points)
	}
}
