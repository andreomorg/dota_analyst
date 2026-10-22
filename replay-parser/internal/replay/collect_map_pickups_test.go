package replay

import (
	"context"
	"encoding/json"
	"os"
	"reflect"
	"testing"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
)

func TestMapPickupsLocalReplays(t *testing.T) {
	for _, tt := range []struct {
		id            string
		lotus, wisdom int
		complete      bool
	}{
		{"9030042976", 28, 14, true}, {"9042407280", 8, 4, true}, {"9045593156", 0, 0, false},
	} {
		t.Run(tt.id, func(t *testing.T) {
			f, err := os.Open("../../../work/replays/" + tt.id + ".dem")
			if os.IsNotExist(err) {
				t.Skip("local replay unavailable")
			}
			if err != nil {
				t.Fatal(err)
			}
			defer f.Close()
			m, err := (MatchAnalyzer{}).Analyze(context.Background(), f)
			if err != nil {
				t.Fatal(err)
			}
			totals := map[string]int{}
			byPlayer := map[mapPickupKey]int{}
			for _, p := range m.Objectives.MapPickups {
				totals[p.Kind]++
				byPlayer[mapPickupKey{p.Player, p.Kind}]++
				if p.T < 0 || p.T > m.Match.DurationSeconds {
					t.Fatal(p)
				}
			}
			if totals["lotus"] != tt.lotus || totals["wisdom"] != tt.wisdom {
				t.Fatalf("totals=%v", totals)
			}
			if tt.complete {
				for _, p := range m.Players {
					if byPlayer[mapPickupKey{p.Player, "lotus"}] != p.Final.LotusesTaken || byPlayer[mapPickupKey{p.Player, "wisdom"}] != p.Final.WisdomShrinesTaken {
						t.Fatalf("counter mismatch player %d", p.Player)
					}
				}
			} else if len(m.Objectives.MapPickups) != 0 {
				t.Fatal("late counter change must withhold the family")
			}
		})
	}
}

func TestMapPickupsCounterEvidence(t *testing.T) {
	for _, tt := range []struct {
		name   string
		values []int
		times  []float64
		final  int
		want   []float64
	}{
		{"repeated snapshots and batched increment", []int{0, 0, 1, 1, 3}, []float64{0, 1, 2, 3, 4}, 3, []float64{2, 4, 4}},
		{"missing zero baseline", []int{2, 3}, []float64{2, 4}, 3, nil},
		{"counter reset invalidates series", []int{0, 1, 0, 1}, []float64{0, 2, 3, 4}, 1, nil},
		{"final mismatch invalidates series", []int{0, 1}, []float64{0, 2}, 2, nil},
		{"pre horn increment cannot be dated", []int{0, 1}, []float64{-2, -1}, 1, nil},
		{"post game increment cannot be dated", []int{0, 1}, []float64{0, 11}, 1, nil},
		{"exact end allowed", []int{0, 1}, []float64{0, 10}, 1, []float64{10}},
	} {
		t.Run(tt.name, func(t *testing.T) {
			c := &mapPickupsCollector{session: &Session{Clock: &Clock{}}, series: map[mapPickupKey]*mapPickupSeries{}}
			for i, v := range tt.values {
				c.observe(mapPickupKey{2, "lotus"}, v, ServerTime{Seconds: tt.times[i]})
			}
			m := matchdata.New()
			m.Match.DurationSeconds = 10
			m.Players = append(m.Players, matchdata.Player{Player: 2, Final: matchdata.PlayerFinal{LotusesTaken: tt.final}})
			if err := c.Fill(&m); err != nil {
				t.Fatal(err)
			}
			var got []float64
			for _, p := range m.Objectives.MapPickups {
				got = append(got, float64(p.T))
				if p.Player != 2 || p.Kind != "lotus" {
					t.Fatal(p)
				}
			}
			if !reflect.DeepEqual(got, tt.want) {
				t.Fatalf("got %v want %v", got, tt.want)
			}
		})
	}
}

func TestMapPickupsJSONAndOrdering(t *testing.T) {
	c := &mapPickupsCollector{session: &Session{Clock: &Clock{}}, series: map[mapPickupKey]*mapPickupSeries{}}
	for _, k := range []mapPickupKey{{2, "wisdom"}, {2, "lotus"}, {1, "wisdom"}} {
		c.observe(k, 0, ServerTime{})
		c.observe(k, 1, ServerTime{Seconds: 4.26})
	}
	m := matchdata.New()
	m.Match.DurationSeconds = 10
	m.Players = append(m.Players, matchdata.Player{Player: 2, Final: matchdata.PlayerFinal{LotusesTaken: 1, WisdomShrinesTaken: 1}}, matchdata.Player{Player: 1, Final: matchdata.PlayerFinal{WisdomShrinesTaken: 1}})
	if err := c.Fill(&m); err != nil {
		t.Fatal(err)
	}
	b, err := json.Marshal(m.Objectives.MapPickups)
	if err != nil {
		t.Fatal(err)
	}
	if string(b) != `[[4.3,1,"wisdom"],[4.3,2,"lotus"],[4.3,2,"wisdom"]]` {
		t.Fatal(string(b))
	}
}

func TestMapPickupsIncompleteFamilyIsOmitted(t *testing.T) {
	c := &mapPickupsCollector{session: &Session{Clock: &Clock{}}, series: map[mapPickupKey]*mapPickupSeries{}}
	for _, k := range []mapPickupKey{{1, "lotus"}, {2, "wisdom"}} {
		c.observe(k, 0, ServerTime{})
		c.observe(k, 1, ServerTime{Seconds: 4})
	}
	m := matchdata.New()
	m.Match.DurationSeconds = 10
	m.Players = append(m.Players, matchdata.Player{Player: 1, Final: matchdata.PlayerFinal{LotusesTaken: 1}}, matchdata.Player{Player: 2, Final: matchdata.PlayerFinal{WisdomShrinesTaken: 2}})
	if err := c.Fill(&m); err != nil {
		t.Fatal(err)
	}
	b, err := json.Marshal(m.Objectives)
	if err != nil {
		t.Fatal(err)
	}
	if len(m.Objectives.MapPickups) != 0 {
		t.Fatal("partial family must not be published")
	}
	var fields map[string]json.RawMessage
	if err = json.Unmarshal(b, &fields); err != nil {
		t.Fatal(err)
	}
	if _, ok := fields["mapPickups"]; ok {
		t.Fatal("incomplete family must be absent, not an empty coverage claim")
	}
}
