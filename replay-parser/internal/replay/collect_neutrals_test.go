package replay

import (
	"context"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"os"
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

func TestMadstoneSparseRule(t *testing.T) {
	for _, test := range []struct {
		name        string
		seen        bool
		before, now [4]int
		want        bool
	}{
		{name: "first zero", want: true},
		{name: "same zero", seen: true},
		{name: "held spent", seen: true, before: [4]int{6, 6, 9, 100}, now: [4]int{0, 6, 9, 100}, want: true},
		{name: "acquired", seen: true, now: [4]int{0, 1, 0, 0}, want: true},
		{name: "found", seen: true, now: [4]int{0, 0, 1, 0}, want: true},
		{name: "gold", seen: true, now: [4]int{0, 0, 0, 1}, want: true},
	} {
		t.Run(test.name, func(t *testing.T) {
			if got := madstonesChanged(test.seen, test.before, test.now); got != test.want {
				t.Fatalf("got %v want %v", got, test.want)
			}
		})
	}
}

func TestMatchNeutralCraft(t *testing.T) {
	level := 2
	message := neutralCraftMessage{at: ServerTime{Seconds: 100}, tier: 3}
	observations := []neutralItemObservation{
		{at: ServerTime{Seconds: 99.5}, name: "item_stormcrafter", slot: 16, level: &level},
		{at: ServerTime{Seconds: 100}, name: "item_enhancement_alert", slot: 17, level: &level},
		{at: ServerTime{Seconds: 110}, name: "item_wrong", slot: 16},
	}
	craft, ok := matchNeutralCraft(message, observations)
	if !ok || craft.Artifact != "item_stormcrafter" || craft.Tier != 3 || craft.ArtifactLevel == nil || *craft.ArtifactLevel != 2 || craft.Enhancement == nil || *craft.Enhancement != "item_enhancement_alert" {
		t.Fatalf("bad craft %+v", craft)
	}
	if _, ok := matchNeutralCraft(message, observations[2:]); ok {
		t.Fatal("paired distant purchase")
	}
	craft, ok = matchNeutralCraft(message, observations[:1])
	if !ok || craft.Enhancement != nil || craft.EnhancementLevel != nil {
		t.Fatalf("missing enhancement must stay null: %+v", craft)
	}
}

// The local 7.41 fixture has ten T1 crafts and one T2 craft, including transient purchase times.
func TestNeutralCrafts741Replay(t *testing.T) {
	file, err := os.Open("../../../work/replays/9042407280.dem")
	if os.IsNotExist(err) {
		t.Skip("local 7.41 replay absent")
	}
	if err != nil {
		t.Fatal(err)
	}
	defer file.Close()
	m, err := analyzeMatch(context.Background(), file, []Collector{&neutralItemsCollector{}})
	if err != nil {
		t.Fatal(err)
	}
	crafts, samples := 0, 0
	if len(m.Items.NeutralCrafts) != 10 || len(m.Timelines.Madstones) != 10 {
		t.Fatal("missing player lists")
	}
	for _, p := range m.Items.NeutralCrafts {
		for i, c := range p.Crafts {
			crafts++
			if c.Tier < 1 || c.Tier > 5 || c.Enhancement == nil || c.ArtifactLevel == nil || c.EnhancementLevel == nil || i > 0 && c.T < p.Crafts[i-1].T {
				t.Fatalf("bad craft %+v", c)
			}
		}
	}
	for _, p := range m.Timelines.Madstones {
		samples += len(p.Samples)
		for i, s := range p.Samples {
			if s.T%2 != 0 || i > 0 && (s.T <= p.Samples[i-1].T || s.Held == p.Samples[i-1].Held && s.Acquired == p.Samples[i-1].Acquired && s.Found == p.Samples[i-1].Found && s.NeutralGold == p.Samples[i-1].NeutralGold) {
				t.Fatalf("non sparse sample %+v", s)
			}
		}
	}
	if crafts != 11 || samples != 309 {
		t.Fatalf("crafts=%d samples=%d", crafts, samples)
	}
	second := m.Items.NeutralCrafts[0].Crafts[1]
	if second.Tier != 2 || second.Artifact != "item_mana_draught" || second.EnhancementLevel == nil || *second.EnhancementLevel != 2 {
		t.Fatalf("tier conversion or transient artifact lost: %+v", second)
	}
}

func TestNeutralCraftsEmptyPlayersAndEnd(t *testing.T) {
	players := &Players{}
	for i := 0; i < 10; i++ {
		players.list = append(players.list, PlayerInfo{Index: i})
	}
	end := ServerTime{Seconds: 50}
	collector := &neutralItemsCollector{session: &Session{Players: players, Clock: &Clock{gameEnd: &end}}, messages: []neutralCraftMessage{{at: ServerTime{Seconds: 51}, player: 0, tier: 1}}}
	collector.observed[0] = []neutralItemObservation{{at: ServerTime{Seconds: 51}, name: "item_chipped_vest", slot: 16}}
	m := matchdata.New()
	if err := collector.Fill(&m); err != nil {
		t.Fatal(err)
	}
	if len(m.Items.NeutralCrafts) != 10 || len(m.Timelines.Madstones) != 10 {
		t.Fatal("every player must have a list")
	}
	for i, p := range m.Items.NeutralCrafts {
		if p.Player != i || len(p.Crafts) != 0 {
			t.Fatalf("post-end craft retained: %+v", p)
		}
	}
}
