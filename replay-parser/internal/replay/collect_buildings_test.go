package replay

import (
	"context"
	"math"
	"testing"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
)

// TestBuildingHealthCollector checks the real entity names against the independent combat log.
func TestBuildingHealthCollector(t *testing.T) {
	m, err := analyzeMatch(context.Background(), allPickReplay.open(t), []Collector{&matchCollector{}, &visionCollector{}, &buildingsCollector{}})
	if err != nil {
		t.Fatal(err)
	}
	counts := map[string]int{}
	matched := map[int]bool{}
	t4 := map[matchdata.Team][][2]int{}
	for _, b := range m.Objectives.BuildingHealth {
		counts[b.Kind]++
		if b.X < -9472 || b.X > 9472 || b.Y < -9472 || b.Y > 9472 || b.X == 0 && b.Y == 0 {
			t.Errorf("invalid building position: %+v", b)
		}
		if b.Tier != nil && *b.Tier == 4 {
			t4[b.Team] = append(t4[b.Team], [2]int{b.X, b.Y})
		}
		if len(b.Health) == 0 {
			t.Fatalf("%s: no first reading", b.Building)
		}
		if b.Team != matchdata.Radiant && b.Team != matchdata.Dire {
			t.Errorf("invalid team: %+v", b)
		}
		for i, h := range b.Health {
			if h.T < 0 || h.T%matchdata.PositionIntervalSeconds != 0 || h.Health < 0 || h.Health > h.MaxHealth || h.MaxHealth <= 0 {
				t.Errorf("%s: invalid reading %+v", b.Building, h)
			}
			if i > 0 {
				previous := b.Health[i-1]
				if previous.T >= h.T || previous.Health == 0 || previous.Health == h.Health && previous.MaxHealth == h.MaxHealth {
					t.Errorf("%s: non-sparse or unordered readings", b.Building)
				}
			}
		}
		last := b.Health[len(b.Health)-1]
		if last.Health != 0 {
			continue
		}
		found := false
		for i, death := range m.Objectives.Buildings {
			if !matched[i] && death.Building == b.Building && death.Team == b.Team && math.Abs(float64(death.T)-float64(last.T)) <= matchdata.PositionIntervalSeconds {
				matched[i] = true
				found = true
				break
			}
		}
		if !found {
			t.Errorf("%s: zero without matching death at %d", b.Building, last.T)
		}
	}
	for _, team := range []matchdata.Team{matchdata.Radiant, matchdata.Dire} {
		points := t4[team]
		if len(points) != 2 || points[0] == points[1] {
			t.Errorf("%s: expected two distinct T4 positions, got %v", team, points)
		}
	}
	if counts["tower"] != 22 || counts["barracks"] != 12 || counts["ancient"] != 2 {
		t.Fatalf("building counts: %v", counts)
	}
	for i, death := range m.Objectives.Buildings {
		if _, valid := buildingIdentity(death.Building, death.Team); valid && !matched[i] {
			t.Errorf("death without terminal zero: %+v", death)
		}
	}
}

func TestCanonicalBuildingName(t *testing.T) {
	for name, want := range map[string]string{
		"dota_goodguys_tower2_mid": "npc_dota_goodguys_tower2_mid",
		"bad_rax_range_bot":        "npc_dota_badguys_range_rax_bot",
		"dota_badguys_fort":        "npc_dota_badguys_fort",
		"":                         "",
	} {
		if got := canonicalBuildingName(name); got != want {
			t.Errorf("%q: got %q, want %q", name, got, want)
		}
	}
	for _, name := range []string{"npc_dota_badguys_fillers", "npc_dota_goodguys_effigy", "npc_dota_watch_tower"} {
		if _, valid := buildingIdentity(name, matchdata.Radiant); valid {
			t.Errorf("unexpected building %q", name)
		}
	}
}
