package replay

import (
	"math"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
	"github.com/dotabuff/manta/dota"
)

// ordersCollector records player intent, including orders to controlled units (not just the hero).
// Steam IDs link controller entities to player slots; controller m_nPlayerID is not a slot.
type ordersCollector struct {
	session *Session
	pending map[uint64]matchdata.Orders
	samples map[int]map[int]matchdata.Orders
	seen    bool
}

func (c *ordersCollector) Register(s *Session) {
	c.session = s
	c.pending = make(map[uint64]matchdata.Orders)
	c.samples = make(map[int]map[int]matchdata.Orders)
	s.Parser.Callbacks.OnCDOTAUserMsg_SpectatorPlayerUnitOrders(func(m *dota.CDOTAUserMsg_SpectatorPlayerUnitOrders) error {
		c.seen = true
		if s.ended || m.Entindex == nil || m.OrderType == nil {
			return nil
		}
		controller := s.Parser.FindEntity(m.GetEntindex())
		if controller == nil || controller.GetClassName() != "CDOTAPlayerController" {
			return nil
		}
		steamID, ok := controller.GetUint64("m_steamID")
		if !ok || steamID == 0 {
			return nil
		}
		// Follow the controller's assigned hero even before Players has learned all ten players.
		handle, ok := controller.GetUint64("m_hAssignedHero")
		if !ok {
			return nil
		}
		hero := s.Parser.FindEntityByHandle(handle)
		if hero == nil {
			return nil
		}
		if life, ok := number(hero, "m_lifeState"); !ok || life != 0 {
			delete(c.pending, steamID)
			return nil
		}
		moves, attacks, casts := orderGroup(dota.DotaunitorderT(m.GetOrderType()))
		entry := c.pending[steamID]
		entry.Moves += moves
		entry.Attacks += attacks
		entry.Casts += casts
		if m.GetOrderType() == int32(dota.DotaunitorderT_DOTA_UNIT_ORDER_MOVE_TO_POSITION) && m.Position != nil && m.Position.X != nil && m.Position.Y != nil {
			x, y := int(math.Round(float64(m.Position.GetX()))), int(math.Round(float64(m.Position.GetY())))
			entry.MoveX, entry.MoveY = &x, &y
		}
		c.pending[steamID] = entry
		return nil
	})
	// Reset at the death tick, including deaths and respawns between sampled seconds.
	s.OnEntity("CDOTA_Unit_Hero_", func(hero *manta.Entity, _ manta.EntityOp) {
		if life, ok := number(hero, "m_lifeState"); ok && life != 0 {
			if index, ok := s.Players.ByHeroEntity(hero); ok {
				if player, ok := s.Players.Get(index); ok {
					delete(c.pending, player.SteamID)
				}
			}
		}
	})
	s.OnSecond(func(second int) {
		for _, player := range s.Players.All() {
			hero := s.Players.Hero(player.Index)
			if hero == nil {
				delete(c.pending, player.SteamID)
				continue
			}
			if life, ok := number(hero, "m_lifeState"); !ok || life != 0 {
				delete(c.pending, player.SteamID)
				continue
			}
			if second%matchdata.PositionIntervalSeconds != 0 {
				continue
			}
			if _, _, ok := position(hero); !ok {
				continue
			}
			if c.samples[player.Index] == nil {
				c.samples[player.Index] = make(map[int]matchdata.Orders)
			}
			c.samples[player.Index][second] = c.pending[player.SteamID]
			delete(c.pending, player.SteamID)
		}
	})
}

// orderGroup counts movement (position/target/direction/patrol/relative), attacks (move/target),
// and casts (position/target/tree/no-target/toggle/autocast/rune/alternate toggle).
// Vector-target setup/cancel, continue, stop, hold, purchases and all other orders are excluded.
// These are issued commands, including item casts and queued commands, not successful actions.
func orderGroup(order dota.DotaunitorderT) (moves, attacks, casts int) {
	switch order {
	case dota.DotaunitorderT_DOTA_UNIT_ORDER_MOVE_TO_POSITION, dota.DotaunitorderT_DOTA_UNIT_ORDER_MOVE_TO_TARGET,
		dota.DotaunitorderT_DOTA_UNIT_ORDER_MOVE_TO_DIRECTION, dota.DotaunitorderT_DOTA_UNIT_ORDER_PATROL, dota.DotaunitorderT_DOTA_UNIT_ORDER_MOVE_RELATIVE:
		return 1, 0, 0
	case dota.DotaunitorderT_DOTA_UNIT_ORDER_ATTACK_MOVE, dota.DotaunitorderT_DOTA_UNIT_ORDER_ATTACK_TARGET:
		return 0, 1, 0
	case dota.DotaunitorderT_DOTA_UNIT_ORDER_CAST_POSITION, dota.DotaunitorderT_DOTA_UNIT_ORDER_CAST_TARGET,
		dota.DotaunitorderT_DOTA_UNIT_ORDER_CAST_TARGET_TREE, dota.DotaunitorderT_DOTA_UNIT_ORDER_CAST_NO_TARGET,
		dota.DotaunitorderT_DOTA_UNIT_ORDER_CAST_TOGGLE, dota.DotaunitorderT_DOTA_UNIT_ORDER_CAST_TOGGLE_AUTO,
		dota.DotaunitorderT_DOTA_UNIT_ORDER_CAST_RUNE, dota.DotaunitorderT_DOTA_UNIT_ORDER_CAST_TOGGLE_ALT:
		return 0, 0, 1
	}
	return 0, 0, 0
}

func (c *ordersCollector) Fill(m *matchdata.Match) error {
	if !c.seen {
		return nil
	} // Absence of the message is unknown, not evidence of zero orders.
	// Players fills first. Use its actual sample seconds to guarantee parallel lists without changing that collector.
	for i := range m.Timelines.Positions {
		positions := &m.Timelines.Positions[i]
		positions.Orders = make(matchdata.List[matchdata.Orders], 0, len(positions.Samples))
		for _, sample := range positions.Samples {
			positions.Orders = append(positions.Orders, c.samples[positions.Player][sample.T])
		}
	}
	return nil
}
