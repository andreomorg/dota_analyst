package replay

import (
	"encoding/json"
	"testing"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
)

func TestTreeReadings_LoadingThenFirstReadingThenFlips(t *testing.T) {
	c := &mapCollector{}
	// Still loading: all zero, nothing known yet.
	c.addTreeReading([]uint64{0, 0}, 0)
	if c.treeWords != nil || c.treeCount != 0 {
		t.Fatalf("an all-zero reading must be ignored, got count %d", c.treeCount)
	}
	// 70 trees (bits 0..69); tree 3 is already down.
	first := []uint64{^uint64(0) &^ (1 << 3), (1 << 6) - 1}
	c.addTreeReading(first, 2)
	if c.treeCount != 70 {
		t.Fatalf("tree count = %d, want 70", c.treeCount)
	}
	if got := c.trees.DeadAtStart; len(got) != 1 || got[0] != 3 {
		t.Fatalf("deadAtStart = %v, want [3]", got)
	}
	// Tree 10 is cut and tree 3 grows back; a bit beyond the trees (70) is ignored.
	c.addTreeReading([]uint64{^uint64(0) &^ (1 << 10), (1 << 6) - 1 | 1<<6}, 4)
	want := []matchdata.TreeChange{{T: 4, Index: 3, Alive: true}, {T: 4, Index: 10, Alive: false}}
	if len(c.trees.Changes) != len(want) {
		t.Fatalf("changes = %v, want %v", c.trees.Changes, want)
	}
	for i := range want {
		if c.trees.Changes[i] != want[i] {
			t.Fatalf("change %d = %v, want %v", i, c.trees.Changes[i], want[i])
		}
	}
}

func TestVisionCell_CentreOfThe128Cell(t *testing.T) {
	for _, c := range []struct {
		in   float64
		want int
	}{{0, 64}, {127.9, 64}, {128, 192}, {-1, -64}, {-128, -64}, {-129, -192}} {
		if got := visionCell(c.in); got != c.want {
			t.Errorf("visionCell(%v) = %d, want %d", c.in, got, c.want)
		}
	}
}

func TestBossChanged_MoveOrLife(t *testing.T) {
	last := matchdata.BossSample{T: 10, X: 0, Y: 0, Alive: true}
	if bossChanged(last, matchdata.BossSample{T: 12, X: 150, Y: 100, Alive: true}) {
		t.Fatal("a move of 180 is not a change")
	}
	if !bossChanged(last, matchdata.BossSample{T: 12, X: 150, Y: 150, Alive: true}) {
		t.Fatal("a move of 212 is a change")
	}
	if !bossChanged(last, matchdata.BossSample{T: 12, Alive: false}) {
		t.Fatal("dying is a change")
	}
}

func TestMapStateJSON_Contract(t *testing.T) {
	x, y := 10, -20
	reason := uint32(13)
	cases := []struct {
		value any
		want  string
	}{
		{matchdata.TreeTimeline{}, `{"deadAtStart":[],"changes":[]}`},
		{matchdata.TreeChange{T: 4, Index: 10, Alive: false}, `[4,10,false]`},
		{matchdata.TempTree{T: 61.25, X: -619, Y: -770, Present: true}, `[61.3,-619,-770,true]`},
		{matchdata.BossTrack{Kind: "roshan", Samples: matchdata.List[matchdata.BossSample]{{T: 2, X: -3194, Y: 2395, Alive: true}}}, `{"kind":"roshan","samples":[[2,-3194,2395,true]]}`},
		{matchdata.LanternUse{T: 300, Player: 4}, `[300,4,null,null]`},
		{matchdata.GoldEvent{T: 12.5, Player: 1, Amount: -150, Reason: &reason, X: &x, Y: &y}, `[12.5,1,-150,13,10,-20]`},
		{matchdata.VisionUnits{T: 600, Radiant: matchdata.List[matchdata.VisionCircle]{{X: -64, Y: 192, Day: 750, Night: 750}}}, `[600,[[-64,192,750,750]],[]]`},
	}
	for _, c := range cases {
		raw, err := json.Marshal(c.value)
		if err != nil {
			t.Fatal(err)
		}
		if string(raw) != c.want {
			t.Errorf("%T = %s, want %s", c.value, raw, c.want)
		}
	}
}
