package replay

import (
	"context"
	"testing"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta/dota"
)

func TestOrderGroup(t *testing.T) {
	for _, tc := range []struct {
		order                 dota.DotaunitorderT
		moves, attacks, casts int
	}{
		{dota.DotaunitorderT_DOTA_UNIT_ORDER_MOVE_TO_POSITION, 1, 0, 0},
		{dota.DotaunitorderT_DOTA_UNIT_ORDER_MOVE_TO_TARGET, 1, 0, 0},
		{dota.DotaunitorderT_DOTA_UNIT_ORDER_ATTACK_MOVE, 0, 1, 0},
		{dota.DotaunitorderT_DOTA_UNIT_ORDER_ATTACK_TARGET, 0, 1, 0},
		{dota.DotaunitorderT_DOTA_UNIT_ORDER_CAST_POSITION, 0, 0, 1},
		{dota.DotaunitorderT_DOTA_UNIT_ORDER_CAST_TOGGLE_ALT, 0, 0, 1},
		{dota.DotaunitorderT_DOTA_UNIT_ORDER_STOP, 0, 0, 0},
		{dota.DotaunitorderT_DOTA_UNIT_ORDER_PURCHASE_ITEM, 0, 0, 0},
		{dota.DotaunitorderT(999), 0, 0, 0},
	} {
		t.Run(tc.order.String(), func(t *testing.T) {
			m, a, c := orderGroup(tc.order)
			if m != tc.moves || a != tc.attacks || c != tc.casts {
				t.Fatalf("got %d/%d/%d, want %d/%d/%d", m, a, c, tc.moves, tc.attacks, tc.casts)
			}
		})
	}
}

func TestOrdersCollectorAllPick(t *testing.T) {
	match, err := analyzeMatch(context.Background(), allPickReplay.open(t), []Collector{&playersCollector{}, &ordersCollector{}})
	if err != nil {
		t.Fatal(err)
	}
	if len(match.Timelines.Positions) != 10 {
		t.Fatalf("got %d players", len(match.Timelines.Positions))
	}
	for _, p := range match.Timelines.Positions {
		if len(p.Orders) != len(p.Samples) || len(p.Orders) == 0 {
			t.Fatalf("player %d: %d orders, %d samples", p.Player, len(p.Orders), len(p.Samples))
		}
		moves, targets := 0, 0
		for i, o := range p.Orders {
			if o.Moves < 0 || o.Attacks < 0 || o.Casts < 0 {
				t.Errorf("player %d sample %d: negative counts", p.Player, i)
			}
			if (o.MoveX == nil) != (o.MoveY == nil) {
				t.Errorf("player %d sample %d: partial target", p.Player, i)
			}
			if o.MoveX != nil {
				targets++
				if o.Moves == 0 {
					t.Errorf("player %d sample %d: target without move", p.Player, i)
				}
			}
			moves += o.Moves
		}
		if moves == 0 || targets == 0 {
			t.Errorf("player %d: no moves/targets", p.Player)
		}
	}
}

// Fill follows actual position samples, including gaps caused by deaths or missing positions.
func TestOrdersFillAlignment(t *testing.T) {
	c := ordersCollector{seen: true, samples: map[int]map[int]matchdata.Orders{
		3: {0: {Moves: 2}, 2: {Attacks: 1}, 20: {Casts: 1}},
	}}
	m := matchdata.New()
	m.Timelines.Positions = matchdata.List[matchdata.PlayerPositions]{{Player: 3, Samples: matchdata.List[matchdata.Sample]{{T: 0}, {T: 20}, {T: 22}}}}
	if err := c.Fill(&m); err != nil {
		t.Fatal(err)
	}
	orders := m.Timelines.Positions[0].Orders
	if len(orders) != 3 || orders[0].Moves != 2 || orders[1].Casts != 1 || orders[2] != (matchdata.Orders{}) {
		t.Fatalf("wrong alignment: %+v", orders)
	}
	c.seen = false
	m.Timelines.Positions[0].Orders = nil
	if err := c.Fill(&m); err != nil {
		t.Fatal(err)
	}
	if m.Timelines.Positions[0].Orders != nil {
		t.Fatal("absent messages must remain unknown")
	}
}
