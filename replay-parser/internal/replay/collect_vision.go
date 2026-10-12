package replay

import (
	"math"
	"strings"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
	"github.com/dotabuff/manta/dota"
)

// visionCollector fills the vision and objectives sections.
//
// Sources (checked against OpenDota and the DataRadiant/DataDire/CDOTATeam counters on two real replays):
//   - wards: the entities CDOTA_NPC_Observer_Ward and CDOTA_NPC_Observer_Ward_TrueSight (sentry). Created = placed
//     (position, m_iTeamNum); the owner is m_hOwnerEntity: the hero for sentries, the CDOTAPlayerController for
//     observers (m_steamID, or m_hAssignedHero for bots). The end is the combat log DEATH of npc_dota_observer_wards /
//     npc_dota_sentry_wards, about 6 s before the entity is deleted (death animation): attacker = the ward itself
//     means it expired, anything else destroyed it. target_source_name is the owner's hero, used to pair the DEATH
//     with the right entity.
//   - smokes, scans, glyphs, runes, Aegis: CDOTAUserMsg_ChatEvent. SMOKE_ACTIVATED playerid_1 = player;
//     SCAN_USED value = team number; GLYPH_USED playerid_1 = team number (not a player); RUNE_PICKUP/RUNE_BOTTLE
//     playerid_1 = player, value = rune type (table below); AEGIS/AEGIS_STOLEN playerid_1 = player. The combat log
//     type PICKUP_RUNE never appears.
//   - buildings, Roshan, Tormentors, couriers: combat log DEATH (is_target_building; npc_dota_roshan;
//     npc_dota_miniboss; npc_dota_courier, whose target_source_name is the owner's hero). A summon's kill counts for
//     its owner through damage_source_name (Warlock's golem -> Warlock).
//   - outposts: CDOTAUserMsg_OutpostCaptured (team only: the message does not say who captured it).
//
// Cheese and Aghanim's shard drops from Roshan are not here: the replay has no event for picking them up (the items
// collector sees them in the inventories).
type visionCollector struct {
	session *Session

	wards       []*wardState
	openWards   map[int32]*wardState // entity index -> ward still on the map
	wardDeaths  []wardDeath
	smokes      []smokeEvent
	scans       []teamEvent
	glyphs      []teamEvent
	runes       []runeEvent
	aegis       []runeEvent // player who picked the Aegis up (runeType unused)
	roshanTeams []teamEvent // ROSHAN_KILL announcements (team), when the combat log has no attacker team
	deaths      []unitDeath // buildings, Roshan, Tormentors, couriers
	courierLoss []courierAlert
	outposts    []teamEvent
}

// Rune types (RunePickup.RuneType) are Valve's DOTA_RUNES enum, which the manta protos do not have:
//
//	0 double damage, 1 haste, 2 illusion, 3 invisibility, 4 regeneration, 5 bounty, 6 arcane, 7 water,
//	8 wisdom, 9 shield.
//
// Checked on the replays: bounty (5) at 0:00, water (7) at 2:00/4:00, wisdom (8) from 7:00, as OpenDota's runes_log.

type wardState struct {
	kind    matchdata.WardType
	team    int
	x, y    int
	placed  ServerTime
	deleted *ServerTime
	player  int        // -1 until known
	death   *wardDeath // the combat log DEATH paired in Fill
}

type wardDeath struct {
	at        ServerTime
	kind      matchdata.WardType
	team      int    // the ward's team
	owner     string // the owner's hero (target_source_name)
	destroyed bool   // false: it expired
	by        int    // player who destroyed it, -1
	byTeam    int    // team of the unit that destroyed it
}

type smokeEvent struct {
	at     ServerTime
	player int
	x, y   *int
}

type teamEvent struct {
	at   ServerTime
	team int
}

type runeEvent struct {
	at       ServerTime
	player   int
	runeType int
}

type unitDeath struct {
	at           ServerTime
	target       string
	targetTeam   int
	attacker     string
	attackerTeam int
	killer       int    // player, -1
	owner        string // target_source_name (a courier's owner hero)
	building     bool
}

type courierAlert struct {
	at    ServerTime
	owner int
}

const (
	observerWardUnit = "npc_dota_observer_wards"
	sentryWardUnit   = "npc_dota_sentry_wards"
	roshanUnit       = "npc_dota_roshan"
	tormentorUnit    = "npc_dota_miniboss"
	courierUnit      = "npc_dota_courier"
)

func (c *visionCollector) Register(s *Session) {
	c.session = s
	c.openWards = map[int32]*wardState{}
	s.OnEntity("CDOTA_NPC_Observer_Ward", c.onWard)
	s.OnCombatLog(c.onDeath, dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_DEATH)
	s.OnChatEvent(c.onChatEvent)
	s.Parser.Callbacks.OnCDOTAUserMsg_CourierKilledAlert(func(m *dota.CDOTAUserMsg_CourierKilledAlert) error {
		c.courierLoss = append(c.courierLoss, courierAlert{at: s.Clock.Now(), owner: int(m.GetOwningPlayerId())})
		return nil
	})
	s.Parser.Callbacks.OnCDOTAUserMsg_OutpostCaptured(func(m *dota.CDOTAUserMsg_OutpostCaptured) error {
		c.outposts = append(c.outposts, teamEvent{at: s.Clock.Now(), team: int(m.GetTeamId())})
		return nil
	})
}

func (c *visionCollector) onWard(e *manta.Entity, op manta.EntityOp) {
	switch {
	case op.Flag(manta.EntityOpCreated):
		kind := matchdata.Observer
		if strings.HasSuffix(e.GetClassName(), "_TrueSight") {
			kind = matchdata.Sentry
		}
		team, _ := number(e, "m_iTeamNum")
		w := &wardState{kind: kind, team: int(team), placed: c.session.Clock.Now(), player: -1}
		if x, y, ok := position(e); ok {
			w.x, w.y = int(math.Round(x)), int(math.Round(y))
		}
		w.player = c.wardOwner(e)
		c.wards = append(c.wards, w)
		c.openWards[e.GetIndex()] = w
	case op.Flag(manta.EntityOpDeleted):
		if w, ok := c.openWards[e.GetIndex()]; ok {
			if w.player < 0 {
				w.player = c.wardOwner(e)
			}
			now := c.session.Clock.Now()
			w.deleted = &now
			delete(c.openWards, e.GetIndex())
		}
	default:
		// Before all players are known (wards placed early in the pre-game) the owner may not resolve yet.
		if w, ok := c.openWards[e.GetIndex()]; ok && w.player < 0 {
			w.player = c.wardOwner(e)
		}
	}
}

// wardOwner finds the player who placed the ward, or -1. Entity player IDs are twice the player index, so the link
// is the hero entity or the controller's steam ID (or, for bots, the controller's hero).
func (c *visionCollector) wardOwner(e *manta.Entity) int {
	s := c.session
	handle, ok := number(e, "m_hOwnerEntity")
	if !ok {
		return -1
	}
	owner := s.Parser.FindEntityByHandle(uint64(handle))
	if owner == nil {
		return -1
	}
	if index, ok := s.Players.ByHeroEntity(owner); ok {
		return index
	}
	if steamID, ok := owner.GetUint64("m_steamID"); ok {
		if index, ok := s.Players.BySteamID(steamID); ok {
			return index
		}
	}
	if hero, ok := number(owner, "m_hAssignedHero"); ok {
		if index, ok := s.Players.ByHeroEntity(s.Parser.FindEntityByHandle(uint64(hero))); ok {
			return index
		}
	}
	return -1
}

// killerOf is the player who got the kill: the attacking hero (or illusion), or the owner of a summon
// (damage_source_name); -1 when no player did.
func (c *visionCollector) killerOf(e CombatEntry) int {
	if e.AttackerPlayer >= 0 {
		return e.AttackerPlayer
	}
	if e.Source != "" && e.Source != e.Attacker && strings.HasPrefix(e.Source, "npc_dota_hero_") {
		if index, ok := c.session.Players.ByHeroName(e.Source); ok {
			return index
		}
	}
	return -1
}

func (c *visionCollector) onDeath(e CombatEntry) {
	raw := e.Raw
	switch e.Target {
	case observerWardUnit, sentryWardUnit:
		kind := matchdata.Observer
		if e.Target == sentryWardUnit {
			kind = matchdata.Sentry
		}
		death := wardDeath{
			at:     e.At,
			kind:   kind,
			team:   int(raw.GetTargetTeam()),
			owner:  c.session.optionalName(raw.TargetSourceName),
			by:     -1,
			byTeam: -1,
		}
		if e.Attacker != e.Target { // a ward that expires "kills itself"
			death.destroyed = true
			death.by = c.killerOf(e)
			death.byTeam = int(raw.GetAttackerTeam())
		}
		c.wardDeaths = append(c.wardDeaths, death)
		return
	}
	building := raw.GetIsTargetBuilding()
	if !building && e.Target != roshanUnit && e.Target != tormentorUnit && e.Target != courierUnit {
		return
	}
	c.deaths = append(c.deaths, unitDeath{
		at:           e.At,
		target:       e.Target,
		targetTeam:   int(raw.GetTargetTeam()),
		attacker:     e.Attacker,
		attackerTeam: int(raw.GetAttackerTeam()),
		killer:       c.killerOf(e),
		owner:        c.session.optionalName(raw.TargetSourceName),
		building:     building,
	})
}

func (c *visionCollector) onChatEvent(event *dota.CDOTAUserMsg_ChatEvent, at ServerTime) {
	switch event.GetType() {
	case dota.DOTA_CHAT_MESSAGE_CHAT_MESSAGE_SMOKE_ACTIVATED:
		smoke := smokeEvent{at: at, player: int(event.GetPlayerid_1())}
		if hero := c.session.Players.Hero(smoke.player); hero != nil {
			if x, y, ok := position(hero); ok {
				ix, iy := int(math.Round(x)), int(math.Round(y))
				smoke.x, smoke.y = &ix, &iy
			}
		}
		c.smokes = append(c.smokes, smoke)
	case dota.DOTA_CHAT_MESSAGE_CHAT_MESSAGE_SCAN_USED:
		c.scans = append(c.scans, teamEvent{at: at, team: int(event.GetValue())})
	case dota.DOTA_CHAT_MESSAGE_CHAT_MESSAGE_GLYPH_USED:
		c.glyphs = append(c.glyphs, teamEvent{at: at, team: int(event.GetPlayerid_1())})
	case dota.DOTA_CHAT_MESSAGE_CHAT_MESSAGE_RUNE_PICKUP, dota.DOTA_CHAT_MESSAGE_CHAT_MESSAGE_RUNE_BOTTLE:
		c.runes = append(c.runes, runeEvent{at: at, player: int(event.GetPlayerid_1()), runeType: int(event.GetValue())})
	case dota.DOTA_CHAT_MESSAGE_CHAT_MESSAGE_AEGIS, dota.DOTA_CHAT_MESSAGE_CHAT_MESSAGE_AEGIS_STOLEN:
		c.aegis = append(c.aegis, runeEvent{at: at, player: int(event.GetPlayerid_1())})
	case dota.DOTA_CHAT_MESSAGE_CHAT_MESSAGE_ROSHAN_KILL:
		c.roshanTeams = append(c.roshanTeams, teamEvent{at: at, team: int(event.GetPlayerid_1())})
	}
}

func (c *visionCollector) Fill(m *matchdata.Match) error {
	clock := c.session.Clock
	end, ended := clock.GameEnd()
	// afterEnd: things that happen after the Ancient fell are not part of the match.
	afterEnd := func(t ServerTime) bool { return ended && t.Seconds > end.Seconds }

	c.pairWardDeaths()
	vision := matchdata.Vision{}
	for _, w := range c.wards {
		if afterEnd(w.placed) {
			continue
		}
		team := teamOf(w.team)
		if team == "" {
			continue
		}
		ward := matchdata.Ward{Type: w.kind, Team: team, X: w.x, Y: w.y, PlacedAt: clock.Seconds(w.placed)}
		if w.player >= 0 {
			ward.Player = visionInt(w.player)
		}
		if death := w.death; death != nil && !afterEnd(death.at) {
			ward.RemovedAt = visionSeconds(clock.Seconds(death.at))
			if death.destroyed {
				ward.Destroyed = true
				if death.by >= 0 {
					ward.DestroyedBy = visionInt(death.by)
				}
				if byTeam := teamOf(death.byTeam); byTeam != "" {
					ward.DestroyedByTeam = &byTeam
				}
			}
		} else if w.death == nil && w.deleted != nil && !afterEnd(*w.deleted) {
			ward.RemovedAt = visionSeconds(clock.Seconds(*w.deleted))
		}
		vision.Wards = append(vision.Wards, ward)
	}
	for _, smoke := range c.smokes {
		if visionIsPlayer(smoke.player) && !afterEnd(smoke.at) {
			vision.Smokes = append(vision.Smokes, matchdata.Smoke{T: clock.Seconds(smoke.at), Player: smoke.player, X: smoke.x, Y: smoke.y})
		}
	}
	vision.Scans = c.teamEvents(c.scans, afterEnd)
	vision.Glyphs = c.teamEvents(c.glyphs, afterEnd)
	m.Vision = vision

	objectives := matchdata.Objectives{}
	for _, d := range c.deaths {
		if afterEnd(d.at) {
			continue
		}
		t := clock.Seconds(d.at)
		killer := visionPlayer(d.killer)
		switch {
		case d.building:
			team := teamOf(d.targetTeam)
			if team == "" {
				continue
			}
			objectives.Buildings = append(objectives.Buildings, matchdata.BuildingKill{
				T: t, Building: d.target, Team: team, Killer: killer, KillerUnit: d.attacker,
				Denied: d.attackerTeam == d.targetTeam,
			})
		case d.target == roshanUnit:
			team := teamOf(d.attackerTeam)
			if team == "" {
				team = teamOf(visionNearestTeam(c.roshanTeams, d.at))
			}
			if team == "" {
				continue
			}
			objectives.Roshan = append(objectives.Roshan, matchdata.RoshanKill{T: t, Team: team, Killer: killer})
		case d.target == tormentorUnit:
			if team := teamOf(d.attackerTeam); team != "" {
				objectives.Tormentors = append(objectives.Tormentors, matchdata.TormentorKill{T: t, Team: team, Killer: killer})
			}
		case d.target == courierUnit:
			victim, ok := c.session.Players.ByHeroName(d.owner)
			if !ok {
				victim, ok = c.courierOwner(d.at)
			}
			if ok {
				objectives.Couriers = append(objectives.Couriers, matchdata.CourierKill{T: t, Victim: victim, Killer: killer})
			}
		}
	}
	c.assignAegis(objectives.Roshan, clock)
	for _, r := range c.runes {
		if visionIsPlayer(r.player) && !afterEnd(r.at) {
			objectives.Runes = append(objectives.Runes, matchdata.RunePickup{T: clock.Seconds(r.at), Player: r.player, RuneType: r.runeType})
		}
	}
	for _, o := range c.outposts {
		if team := teamOf(o.team); team != "" && !afterEnd(o.at) {
			objectives.Outposts = append(objectives.Outposts, matchdata.OutpostCapture{T: clock.Seconds(o.at), Team: team})
		}
	}
	m.Objectives = objectives
	return nil
}

// pairWardDeaths links each ward DEATH of the combat log to its entity: same type and team, already placed, not
// paired yet; the owner's hero (target_source_name) must match when both are known; among those, the ward deleted
// soonest after the death (the entity goes ~6 s later), then the oldest still on the map.
func (c *visionCollector) pairWardDeaths() {
	const tolerance = 0.5 // seconds: a DEATH stamped a tick before the entity's own tick
	for i := range c.wardDeaths {
		death := &c.wardDeaths[i]
		owner, ownerKnown := c.session.Players.ByHeroName(death.owner)
		var best *wardState
		for _, w := range c.wards {
			if w.death != nil || w.kind != death.kind || w.team != death.team || w.placed.Seconds > death.at.Seconds+tolerance {
				continue
			}
			if w.deleted != nil && w.deleted.Seconds < death.at.Seconds-tolerance {
				continue
			}
			if ownerKnown && w.player >= 0 && w.player != owner {
				continue
			}
			if best == nil || wardRemovedEarlier(w, best) {
				best = w
			}
		}
		if best == nil {
			continue
		}
		best.death = death
		if best.player < 0 && ownerKnown {
			best.player = owner
		}
	}
}

// earlierRemoval orders the candidates of pairWardDeaths: deleted first (soonest), then the oldest placed.
func wardRemovedEarlier(a, b *wardState) bool {
	switch {
	case a.deleted != nil && b.deleted != nil:
		return a.deleted.Seconds < b.deleted.Seconds
	case a.deleted != nil:
		return true
	case b.deleted != nil:
		return false
	default:
		return a.placed.Seconds < b.placed.Seconds
	}
}

// assignAegis gives each Roshan kill the first player who picked the Aegis up after it (and before the next kill).
func (c *visionCollector) assignAegis(kills []matchdata.RoshanKill, clock *Clock) {
	for _, pickup := range c.aegis {
		if !visionIsPlayer(pickup.player) {
			continue
		}
		t := clock.Seconds(pickup.at)
		for i := len(kills) - 1; i >= 0; i-- {
			if kills[i].T <= t+0.5 {
				if kills[i].AegisPlayer == nil {
					kills[i].AegisPlayer = visionInt(pickup.player)
				}
				break
			}
		}
	}
}

// courierOwner is the owner of the courier whose CourierKilledAlert is closest (within 1 s) to the DEATH.
func (c *visionCollector) courierOwner(at ServerTime) (int, bool) {
	best, found := -1, false
	bestGap := 1.0
	for _, alert := range c.courierLoss {
		if gap := math.Abs(alert.at.Seconds - at.Seconds); gap <= bestGap && visionIsPlayer(alert.owner) {
			best, bestGap, found = alert.owner, gap, true
		}
	}
	return best, found
}

// nearestTeam is the team of the event closest (within 2 s) to at, or 0.
func visionNearestTeam(events []teamEvent, at ServerTime) int {
	team, bestGap := 0, 2.0
	for _, e := range events {
		if gap := math.Abs(e.at.Seconds - at.Seconds); gap <= bestGap {
			team, bestGap = e.team, gap
		}
	}
	return team
}

func (c *visionCollector) teamEvents(events []teamEvent, afterEnd func(ServerTime) bool) matchdata.List[matchdata.TeamEvent] {
	var list matchdata.List[matchdata.TeamEvent]
	for _, e := range events {
		if team := teamOf(e.team); team != "" && !afterEnd(e.at) {
			list = append(list, matchdata.TeamEvent{T: c.session.Clock.Seconds(e.at), Team: team})
		}
	}
	return list
}

func visionIsPlayer(index int) bool { return index >= 0 && index < playerCount }

func visionPlayer(index int) *int {
	if !visionIsPlayer(index) {
		return nil
	}
	return visionInt(index)
}

func visionInt(v int) *int { return &v }

func visionSeconds(v matchdata.Seconds) *matchdata.Seconds { return &v }
