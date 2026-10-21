package replay

import (
	"math"
	"sort"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta/dota"
)

type abilityTargetEvent struct {
	at                 ServerTime
	hero, name, target string
	x, y               *int
}
type castOrder struct {
	at     ServerTime
	steam  uint64
	name   string
	target int
	x, y   *int
}
type abilityTargetsCollector struct {
	session *Session
	events  []abilityTargetEvent
	orders  []castOrder
}

func (c *abilityTargetsCollector) Register(s *Session) {
	c.session = s
	s.OnCombatLog(func(e CombatEntry) {
		if s.ended || !e.Raw.GetIsAttackerHero() || e.Raw.GetIsAttackerIllusion() || e.Inflictor == "" {
			return
		}
		v := abilityTargetEvent{at: e.At, hero: e.Attacker, name: e.Inflictor}
		if e.Raw.GetIsTargetHero() && !e.Raw.GetIsTargetIllusion() {
			v.target = e.Target
		}
		if e.Raw.LocationX != nil && e.Raw.LocationY != nil {
			x, y := int(math.Round(float64(e.Raw.GetLocationX()))), int(math.Round(float64(e.Raw.GetLocationY())))
			v.x, v.y = &x, &y
		}
		c.events = append(c.events, v)
	}, dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_ABILITY)
	s.Parser.Callbacks.OnCDOTAUserMsg_SpectatorPlayerUnitOrders(func(m *dota.CDOTAUserMsg_SpectatorPlayerUnitOrders) error {
		if s.ended || m.GetQueue() || m.AbilityId == nil || m.Entindex == nil {
			return nil
		}
		order := dota.DotaunitorderT(m.GetOrderType())
		if order != dota.DotaunitorderT_DOTA_UNIT_ORDER_CAST_POSITION && order != dota.DotaunitorderT_DOTA_UNIT_ORDER_CAST_TARGET {
			return nil
		}
		controller := s.Parser.FindEntity(m.GetEntindex())
		ability := s.Parser.FindEntity(m.GetAbilityId())
		if controller == nil || controller.GetClassName() != "CDOTAPlayerController" || ability == nil {
			return nil
		}
		steam, ok := controller.GetUint64("m_steamID")
		if !ok || steam == 0 {
			return nil
		}
		handle, ok := controller.GetUint64("m_hAssignedHero")
		if !ok {
			return nil
		}
		hero := s.Parser.FindEntityByHandle(handle)
		if hero == nil {
			return nil
		}
		// Orders to summons are not evidence of the main hero's cast destination.
		owns := false
		for _, unit := range m.GetUnits() {
			if unit == hero.GetIndex() {
				owns = true
			}
		}
		if !owns {
			return nil
		}
		name := s.EntityName(ability)
		if name == "" {
			return nil
		}
		v := castOrder{at: s.Clock.Now(), steam: steam, name: name, target: -1}
		if order == dota.DotaunitorderT_DOTA_UNIT_ORDER_CAST_POSITION && m.Position != nil && m.Position.X != nil && m.Position.Y != nil {
			x, y := int(math.Round(float64(m.Position.GetX()))), int(math.Round(float64(m.Position.GetY())))
			v.x, v.y = &x, &y
		}
		if order == dota.DotaunitorderT_DOTA_UNIT_ORDER_CAST_TARGET && m.TargetIndex != nil {
			target := s.Parser.FindEntity(m.GetTargetIndex())
			if target != nil {
				if p, ok := s.Players.ByHeroEntity(target); ok && s.Players.Hero(p) == target {
					v.target = p
				}
			}
		}
		c.orders = append(c.orders, v)
		return nil
	})
}

// uniqueCastOrder rejects repeated/ambiguous orders instead of attaching a plausible wrong target.
func uniqueCastOrder(at ServerTime, steam uint64, name string, orders []castOrder, used map[int]bool) int {
	found := -1
	for i, o := range orders {
		if used[i] || o.steam != steam || o.name != name || math.Abs(o.at.Seconds-at.Seconds) > 0.25 {
			continue
		}
		if found != -1 {
			return -1
		}
		found = i
	}
	return found
}
func (c *abilityTargetsCollector) Fill(m *matchdata.Match) error {
	var events [playerCount][]abilityTargetEvent
	for _, e := range c.events {
		p, ok := c.session.Players.ByHeroName(e.hero)
		if ok && c.session.Clock.Game(e.at) <= float64(m.Match.DurationSeconds) {
			events[p] = append(events[p], e)
		}
	}
	used := map[int]bool{}
	for i := range m.Abilities.Uses {
		uses := &m.Abilities.Uses[i]
		list := events[uses.Player]
		sort.SliceStable(list, func(i, j int) bool { return list[i].at.Seconds < list[j].at.Seconds })
		if len(list) != len(uses.Abilities) {
			continue
		}
		// Refuse metadata if the original use collector's ordering or filters change later.
		aligned := true
		for j, e := range list {
			if uses.Abilities[j].Name != e.name || uses.Abilities[j].T != c.session.Clock.Seconds(e.at) {
				aligned = false
				break
			}
		}
		if !aligned {
			continue
		}
		player, ok := c.session.Players.Get(uses.Player)
		if !ok {
			continue
		}
		for _, e := range list {
			v := matchdata.AbilityTarget{}
			if p, ok := c.session.Players.ByHeroName(e.target); ok {
				p := p
				v.Player = &p
			}
			v.X, v.Y = e.x, e.y
			if v.X != nil {
				source := 0
				v.PointSource = &source
			}
			if order := uniqueCastOrder(e.at, player.SteamID, e.name, c.orders, used); order >= 0 {
				o := c.orders[order]
				// The same order must not be a candidate for multiple successful uses either.
				candidates := 0
				for _, other := range list {
					if other.name == e.name && math.Abs(other.at.Seconds-o.at.Seconds) <= 0.25 {
						candidates++
					}
				}
				if candidates != 1 {
					uses.Targets = append(uses.Targets, v)
					continue
				}
				used[order] = true
				if v.Player == nil && o.target >= 0 {
					p := o.target
					v.Player = &p
				}
				if o.x != nil {
					v.X, v.Y = o.x, o.y
					source := 1
					v.PointSource = &source
				}
			}
			uses.Targets = append(uses.Targets, v)
		}
	}
	return nil
}
