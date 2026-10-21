package replay

import (
	"context"
	"testing"
)

func TestWaveCreepKind(t *testing.T) {
	for name, want := range map[string]int{
		"npc_dota_creep_goodguys_melee": 0, "npc_dota_creep_badguys_ranged": 1,
		"npc_dota_goodguys_siege": 2, "npc_dota_badguys_siege": 2,
		"npc_dota_creep_badguys_flagbearer": 3, "npc_dota_creep_goodguys_flagbearer_upgraded": 4,
		"npc_dota_creep_goodguys_melee_upgraded_mega": 5, "npc_dota_neutral_kobold": -1, "": -1,
	} {
		if got := waveCreepKind(name); got != want {
			t.Errorf("%s: %d want %d", name, got, want)
		}
	}
}

func TestWaveCompositionRealReplay(t *testing.T) {
	m, err := analyzeMatch(context.Background(), allPickReplay.open(t), []Collector{&matchCollector{}, &wavesCollector{}})
	if err != nil {
		t.Fatal(err)
	}
	for _, lane := range m.Timelines.Waves {
		if len(lane.Composition) != len(lane.Fronts) {
			t.Fatal("unaligned composition")
		}
		for i, c := range lane.Composition {
			s := lane.Sizes[i]
			r, d := 0, 0
			for k := 0; k < 6; k++ {
				r += c.Radiant[k]
				d += c.Dire[k]
			}
			if c.T != lane.Fronts[i].T || r != s.RadiantInLane || d != s.DireInLane || c.Radiant[6] > s.RadiantAtFront || c.Dire[6] > s.DireAtFront {
				t.Fatalf("%s bad composition %+v sizes %+v", lane.Lane, c, s)
			}
		}
	}
}
