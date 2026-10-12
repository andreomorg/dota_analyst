package main

import (
	"bytes"
	"compress/gzip"
	"encoding/json"
	"fmt"
	"log"
	"math"
	"os"
	"path/filepath"
	"runtime"
	"sort"
	"strings"
	"time"

	"github.com/dotabuff/manta"
	"github.com/dotabuff/manta/dota"
)

// Spike code (sprint 10, card 01): -measure parses the whole replay with roughly what the /v1/match collectors will
// hook (tick clock, combat log, entities, chat), measures time and memory, and builds candidate JSON sections at
// several granularities to see how big each one gets. It also prints numbers to compare with OpenDota. The real
// collectors are cards 03-06; nothing here is the contract.

type serverTime struct {
	at          float64 // server seconds (NetTick/30 scale), as in the combat log timestamp
	pausedTicks float64 // m_nTotalPausedTicks when it happened
}

type measured struct {
	parser *manta.Parser

	// last values of the game rules; m_flGameStartTime is 0 until the horn, so times are kept in server seconds and
	// turned into the game clock only at the end.
	gameStart   float64
	pausedTicks float64
	lastSecond  int

	heroSlot  map[string]int // npc_dota_hero_x -> player slot
	steamIDs  [10]uint64
	heroIDs   [10]int
	heroNames [10]string

	positions [10][][3]int // every second of game clock: [t, x, y] of a living hero
	minutes   [10][]minuteStats
	ups       [10][]abilityUpgrade
	levels    map[uint64]int // ability entity handle -> last level seen

	damage    []combatEvent
	heals     []combatEvent
	deaths    []combatEvent
	purchases []combatEvent
	itemUses  []combatEvent
	abilities []combatEvent
	buildings []combatEvent
	buybacks  []combatEvent
	modifiers int // MODIFIER_ADD on real heroes (counted only)
	stuns     []combatEvent

	wards     map[int32]*ward
	wardsDone []*ward
	chat      []chatLine
	events    []chatLine // ChatEvent: runes, roshan, aegis, glyph, scans, smokes...

	peakHeap uint64
}

type minuteStats struct {
	T          int `json:"t"`
	Gold       int `json:"gold"` // total earned
	XP         int `json:"xp"`
	NetWorth   int `json:"netWorth"`
	LastHits   int `json:"lastHits"`
	Denies     int `json:"denies"`
	Level      int `json:"level"`
	Kills      int `json:"kills"`
	Deaths     int `json:"deaths"`
	Assists    int `json:"assists"`
	HeroDamage int `json:"heroDamage"`
}

type abilityUpgrade struct {
	T       float64 `json:"t"`
	Ability string  `json:"ability"`
	Level   int     `json:"level"`
}

type combatEvent struct {
	when                serverTime
	Attacker, Target    string
	Inflictor           string
	Value               int
	AttackerIllusion    bool
	TargetIllusion      bool
	AttackerHero        bool
	TargetHero          bool
	Assists             []int32
	X, Y                int
	StunSeconds         float64
	TargetTeam, AttTeam int
}

type ward struct {
	Sentry   bool
	Team     int
	X, Y     int
	Owner    int
	placed   serverTime
	removed  serverTime
	gone     bool
	KilledBy string
}

type chatLine struct {
	when   serverTime
	Player int
	Kind   string
	Text   string
	Value  int
}

func runMeasure(parser *manta.Parser, sizesOut string) {
	started := time.Now()
	m := &measured{parser: parser, heroSlot: map[string]int{}, levels: map[uint64]int{}, wards: map[int32]*ward{}, lastSecond: math.MinInt}
	parser.Callbacks.OnCNETMsg_Tick(func(*dota.CNETMsg_Tick) error { m.onTick(); return nil })
	parser.Callbacks.OnCMsgDOTACombatLogEntry(func(e *dota.CMsgDOTACombatLogEntry) error { m.onCombatLog(e); return nil })
	parser.Callbacks.OnCDOTAUserMsg_ChatMessage(func(c *dota.CDOTAUserMsg_ChatMessage) error {
		m.chat = append(m.chat, chatLine{when: m.now(), Player: int(c.GetSourcePlayerId()), Kind: fmt.Sprint(c.GetChannelType()), Text: c.GetMessageText()})
		return nil
	})
	parser.Callbacks.OnCDOTAUserMsg_ChatWheel(func(c *dota.CDOTAUserMsg_ChatWheel) error {
		m.chat = append(m.chat, chatLine{when: m.now(), Player: int(c.GetPlayerId()), Kind: "wheel", Value: int(c.GetChatMessageId())})
		return nil
	})
	parser.Callbacks.OnCDOTAUserMsg_ChatEvent(func(c *dota.CDOTAUserMsg_ChatEvent) error {
		switch c.GetType() {
		case dota.DOTA_CHAT_MESSAGE_CHAT_MESSAGE_HERO_KILL, dota.DOTA_CHAT_MESSAGE_CHAT_MESSAGE_STREAK_KILL,
			dota.DOTA_CHAT_MESSAGE_CHAT_MESSAGE_ITEM_PURCHASE, dota.DOTA_CHAT_MESSAGE_CHAT_MESSAGE_DISCONNECT:
			return nil // already in the combat log, or noise
		}
		m.events = append(m.events, chatLine{when: m.now(), Player: int(c.GetPlayerid_1()), Kind: strings.TrimPrefix(c.GetType().String(), "CHAT_MESSAGE_"), Value: int(c.GetValue())})
		return nil
	})
	parser.OnEntity(func(e *manta.Entity, op manta.EntityOp) error { m.onEntity(e, op); return nil })

	err := parser.Start()
	elapsed := time.Since(started)
	var memory runtime.MemStats
	runtime.ReadMemStats(&memory)
	fmt.Printf("net tick %d, elapsed %s, total allocated %d MB, peak heap in use %d MB, heap from OS %d MB, sys %d MB\n",
		parser.NetTick, elapsed.Round(time.Millisecond), memory.TotalAlloc>>20, m.peakHeap>>20, memory.HeapSys>>20, memory.Sys>>20)
	if err != nil {
		log.Fatal(err)
	}
	fmt.Printf("game start at server %.2f s, paused ticks %.0f, last second of game clock %d\n", m.gameStart, m.pausedTicks, m.lastSecond)
	m.printCrossCheck()
	m.printSizes(sizesOut)
}

// now is the current moment in server seconds, the scale of the combat log timestamps.
func (m *measured) now() serverTime {
	return serverTime{at: float64(m.parser.NetTick) / ticksPerSecond, pausedTicks: m.pausedTicks}
}

// clock turns a server time into the game clock, with the horn time known at the end of the replay.
func (m *measured) clock(t serverTime) float64 {
	return math.Round((t.at-m.gameStart-t.pausedTicks/ticksPerSecond)*10) / 10
}

func (m *measured) onTick() {
	rules := findByClass(m.parser, "CDOTAGamerulesProxy")
	resource := findByClass(m.parser, "CDOTA_PlayerResource")
	if rules == nil || resource == nil {
		return
	}
	m.pausedTicks = number(rules, "m_pGameRules.m_nTotalPausedTicks")
	now, running := gameClock(m.parser, rules)
	if !running {
		return
	}
	m.gameStart = number(rules, "m_pGameRules.m_flGameStartTime")
	second := int(math.Floor(now))
	if second == m.lastSecond {
		return
	}
	if m.lastSecond == math.MinInt {
		m.learnPlayers(resource)
	}
	m.lastSecond = second
	m.sampleSecond(resource, second)
	if second%60 == 0 {
		m.sampleMinute(resource, second)
		var memory runtime.MemStats
		runtime.ReadMemStats(&memory)
		m.peakHeap = max(m.peakHeap, memory.HeapInuse)
	}
}

func (m *measured) learnPlayers(resource *manta.Entity) {
	for slot := range 10 {
		m.steamIDs[slot], _ = resource.GetUint64(fmt.Sprintf("m_vecPlayerData.%04d.m_iPlayerSteamID", slot))
		m.heroIDs[slot] = int(number(resource, fmt.Sprintf("m_vecPlayerTeamData.%04d.m_nSelectedHeroID", slot)))
		if hero := heroOf(m.parser, resource, slot); hero != nil {
			name := entityName(m.parser, hero)
			m.heroNames[slot] = name
			m.heroSlot[name] = slot
		}
	}
}

func entityName(parser *manta.Parser, e *manta.Entity) string {
	index, ok := e.GetInt32("m_pEntity.m_nameStringTableIndex")
	if !ok {
		return ""
	}
	name, _ := parser.LookupStringByIndex("EntityNames", index)
	return name
}

func (m *measured) sampleSecond(resource *manta.Entity, second int) {
	for slot := range 10 {
		hero := heroOf(m.parser, resource, slot)
		if hero == nil {
			continue
		}
		if number(hero, "m_lifeState") == 0 {
			x, y := position(hero)
			m.positions[slot] = append(m.positions[slot], [3]int{second, int(x), int(y)})
		}
		for i := range 35 {
			handle := number(hero, fmt.Sprintf("m_vecAbilities.%04d", i))
			if math.IsNaN(handle) {
				continue
			}
			ability := m.parser.FindEntityByHandle(uint64(handle))
			if ability == nil {
				continue
			}
			level := int(number(ability, "m_iLevel"))
			if level > m.levels[uint64(handle)] {
				m.levels[uint64(handle)] = level
				m.ups[slot] = append(m.ups[slot], abilityUpgrade{T: float64(second), Ability: entityName(m.parser, ability), Level: level})
			}
		}
	}
}

func (m *measured) sampleMinute(resource *manta.Entity, second int) {
	teams := map[float64]*manta.Entity{2: findByClass(m.parser, "CDOTA_DataRadiant"), 3: findByClass(m.parser, "CDOTA_DataDire")}
	for slot := range 10 {
		team := number(resource, fmt.Sprintf("m_vecPlayerData.%04d.m_iPlayerTeam", slot))
		data := teams[team]
		if data == nil {
			continue
		}
		prefix := ""
		for i := range 5 {
			if id, _ := data.GetUint64(fmt.Sprintf("m_vecDataTeam.%04d.m_iPlayerSteamID", i)); id == m.steamIDs[slot] {
				prefix = fmt.Sprintf("m_vecDataTeam.%04d.", i)
			}
		}
		if prefix == "" {
			continue
		}
		pt := fmt.Sprintf("m_vecPlayerTeamData.%04d.", slot)
		m.minutes[slot] = append(m.minutes[slot], minuteStats{
			T: second, Gold: int(number(data, prefix+"m_iTotalEarnedGold")), XP: int(number(data, prefix+"m_iTotalEarnedXP")),
			NetWorth: int(number(data, prefix+"m_iNetWorth")), LastHits: int(number(data, prefix+"m_iLastHitCount")),
			Denies: int(number(data, prefix+"m_iDenyCount")), Level: int(number(resource, pt+"m_iLevel")),
			Kills: int(number(resource, pt+"m_iKills")), Deaths: int(number(resource, pt+"m_iDeaths")),
			Assists: int(number(resource, pt+"m_iAssists")), HeroDamage: int(number(data, prefix+"m_flHeroDamage")),
		})
	}
}

func (m *measured) name(index uint32) string {
	name, _ := m.parser.LookupStringByIndex("CombatLogNames", int32(index))
	return name
}

func (m *measured) onCombatLog(e *dota.CMsgDOTACombatLogEntry) {
	event := combatEvent{
		when:     serverTime{at: float64(e.GetTimestamp()), pausedTicks: m.pausedTicks},
		Attacker: m.name(e.GetAttackerName()), Target: m.name(e.GetTargetName()), Inflictor: m.name(e.GetInflictorName()),
		Value: int(e.GetValue()), AttackerIllusion: e.GetIsAttackerIllusion(), TargetIllusion: e.GetIsTargetIllusion(),
		AttackerHero: e.GetIsAttackerHero(), TargetHero: e.GetIsTargetHero(), Assists: e.GetAssistPlayers(),
		TargetTeam: int(e.GetTargetTeam()), AttTeam: int(e.GetAttackerTeam()), StunSeconds: float64(e.GetStunDuration()),
	}
	switch e.GetType() {
	case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_DAMAGE, dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_CRITICAL_DAMAGE:
		if event.AttackerHero || event.TargetHero {
			m.damage = append(m.damage, event)
		}
	case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_HEAL:
		if event.TargetHero && !event.TargetIllusion {
			m.heals = append(m.heals, event)
		}
	case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_DEATH:
		switch {
		case event.TargetHero && !event.TargetIllusion:
			// The combat log has no location for deaths: the victim's entity still has it on this tick.
			if slot, ok := m.heroSlot[event.Target]; ok {
				if resource := findByClass(m.parser, "CDOTA_PlayerResource"); resource != nil {
					if hero := heroOf(m.parser, resource, slot); hero != nil {
						x, y := position(hero)
						event.X, event.Y = int(x), int(y)
					}
				}
			}
			m.deaths = append(m.deaths, event)
		case e.GetIsTargetBuilding() || strings.Contains(event.Target, "roshan") || strings.Contains(event.Target, "miniboss") || strings.Contains(event.Target, "courier"):
			m.buildings = append(m.buildings, event)
		case strings.Contains(event.Target, "_wards"):
			m.buildings = append(m.buildings, event) // ward kills, matched to the ward entities in a real collector
		}
	case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_PURCHASE:
		event.Inflictor = m.name(e.GetValue()) // the item name index is in value
		m.purchases = append(m.purchases, event)
	case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_ITEM:
		m.itemUses = append(m.itemUses, event)
	case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_ABILITY:
		m.abilities = append(m.abilities, event)
	case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_BUYBACK:
		m.buybacks = append(m.buybacks, event)
	case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_MODIFIER_ADD:
		if event.TargetHero && !event.TargetIllusion {
			m.modifiers++
			if event.StunSeconds > 0 {
				m.stuns = append(m.stuns, event)
			}
		}
	}
}

func (m *measured) onEntity(e *manta.Entity, op manta.EntityOp) {
	class := e.GetClassName()
	if !strings.HasPrefix(class, "CDOTA_NPC_Observer_Ward") {
		return
	}
	switch {
	case op.Flag(manta.EntityOpCreated):
		x, y := position(e)
		w := &ward{Sentry: strings.HasSuffix(class, "TrueSight"), Team: int(number(e, "m_iTeamNum")), X: int(x), Y: int(y), placed: m.now(), Owner: -1}
		m.wardOwner(e, w)
		m.wards[e.GetIndex()] = w
	case op.Flag(manta.EntityOpDeleted):
		if w, ok := m.wards[e.GetIndex()]; ok {
			if w.Owner < 0 {
				m.wardOwner(e, w)
			}
			w.removed, w.gone = m.now(), true
			m.wardsDone = append(m.wardsDone, w)
			delete(m.wards, e.GetIndex())
		}
	}
}

// wardOwner finds who placed the ward. m_hOwnerEntity is the hero (sentries) or the CDOTAPlayerController
// (observers, in these replays). Entity player IDs (hero m_iPlayerID, controller m_nPlayerID) are twice the player
// slot in this patch, so the link is made by the hero handle or the controller's steam ID instead.
func (m *measured) wardOwner(e *manta.Entity, w *ward) {
	handle := number(e, "m_hOwnerEntity")
	if math.IsNaN(handle) {
		return
	}
	owner := m.parser.FindEntityByHandle(uint64(handle))
	if owner == nil {
		return
	}
	resource := findByClass(m.parser, "CDOTA_PlayerResource")
	for slot := range 10 {
		if hero := heroOf(m.parser, resource, slot); hero != nil && hero.GetIndex() == owner.GetIndex() {
			w.Owner = slot
			return
		}
		if steamID, ok := owner.GetUint64("m_steamID"); ok && steamID != 0 && steamID == m.steamIDs[slot] {
			w.Owner = slot
			return
		}
	}
}

// printCrossCheck prints numbers to compare with OpenDota's /matches/{id}: K/D/A, gold and XP at 10:00, first purchases.
func (m *measured) printCrossCheck() {
	fmt.Println("\ncross-check (OpenDota: kills/deaths/assists, gold_t[10], xp_t[10], purchase_log)")
	heroDeaths := map[string]int{}
	for _, d := range m.deaths {
		heroDeaths[d.Target]++
	}
	for slot := range 10 {
		stats := m.minutes[slot]
		if len(stats) == 0 {
			continue
		}
		final := stats[len(stats)-1]
		at10 := minuteStats{}
		for _, s := range stats {
			if s.T == 600 {
				at10 = s
			}
		}
		fmt.Printf("slot %d %-32s steam %d: K/D/A at %d s %d/%d/%d (combat log deaths %d); at 10:00 gold %d xp %d lh %d nw %d\n",
			slot, m.heroNames[slot], m.steamIDs[slot], final.T, final.Kills, final.Deaths, final.Assists, heroDeaths[m.heroNames[slot]],
			at10.Gold, at10.XP, at10.LastHits, at10.NetWorth)
		first := []string{}
		for _, p := range m.purchases {
			if p.Target == m.heroNames[slot] && len(first) < 8 {
				first = append(first, fmt.Sprintf("%s@%.0f", strings.TrimPrefix(p.Inflictor, "item_"), m.clock(p.when)))
			}
		}
		fmt.Printf("    first purchases: %s\n", strings.Join(first, " "))
	}
}

// printSizes builds candidate JSON sections and prints their sizes (raw and gzipped).
func (m *measured) printSizes(outDir string) {
	type section struct {
		name  string
		value any
	}
	sections := []section{}
	add := func(name string, value any) { sections = append(sections, section{name, value}) }

	for _, every := range []int{1, 2, 5, 10} {
		positions := make([][][3]int, 10)
		for slot := range 10 {
			for _, p := range m.positions[slot] {
				if p[0]%every == 0 {
					positions[slot] = append(positions[slot], p)
				}
			}
		}
		add(fmt.Sprintf("positions every %ds [t,x,y]", every), positions)
	}
	add("minute stats (objects)", m.minutes)

	type damageJSON struct {
		T         float64 `json:"t"`
		Attacker  string  `json:"attacker"`
		Target    string  `json:"target"`
		Inflictor string  `json:"inflictor,omitempty"`
		Value     int     `json:"value"`
	}
	perEvent := []damageJSON{}
	heroToHero := []damageJSON{}
	type pairKey struct{ attacker, target, inflictor string }
	pairs := map[pairKey]*[2]int{}
	perMinute := map[string]map[int]*[2]int{} // hero -> minute -> [dealt to heroes, taken from heroes]
	for _, d := range m.damage {
		j := damageJSON{T: m.clock(d.when), Attacker: d.Attacker, Target: d.Target, Inflictor: d.Inflictor, Value: d.Value}
		perEvent = append(perEvent, j)
		realHeroes := d.AttackerHero && d.TargetHero && !d.TargetIllusion && !d.AttackerIllusion
		if realHeroes {
			heroToHero = append(heroToHero, j)
		}
		if d.AttackerHero && !d.AttackerIllusion || d.TargetHero && !d.TargetIllusion {
			k := pairKey{d.Attacker, d.Target, d.Inflictor}
			if pairs[k] == nil {
				pairs[k] = &[2]int{}
			}
			pairs[k][0] += d.Value
			pairs[k][1]++
		}
		if realHeroes {
			minute := int(math.Floor(j.T / 60))
			for i, hero := range []string{d.Attacker, d.Target} {
				if perMinute[hero] == nil {
					perMinute[hero] = map[int]*[2]int{}
				}
				if perMinute[hero][minute] == nil {
					perMinute[hero][minute] = &[2]int{}
				}
				perMinute[hero][minute][i] += d.Value
			}
		}
	}
	add("damage per event (hero involved)", perEvent)
	add("damage per event (hero -> hero only)", heroToHero)
	type pairJSON struct {
		Attacker  string `json:"attacker"`
		Target    string `json:"target"`
		Inflictor string `json:"inflictor,omitempty"`
		Damage    int    `json:"damage"`
		Hits      int    `json:"hits"`
	}
	pairList := []pairJSON{}
	for k, v := range pairs {
		pairList = append(pairList, pairJSON{k.attacker, k.target, k.inflictor, v[0], v[1]})
	}
	sort.Slice(pairList, func(i, j int) bool { return pairList[i].Damage > pairList[j].Damage })
	add("damage aggregated attacker->target->inflictor", pairList)
	add("damage per minute per hero [dealt,taken]", perMinute)
	add("damage aggregated, other units grouped by kind", aggregateByKind(m.damage))
	add("heals aggregated, other units grouped by kind", aggregateByKind(m.heals))

	type eventJSON struct {
		T         float64 `json:"t"`
		Who       string  `json:"who,omitempty"`
		Target    string  `json:"target,omitempty"`
		What      string  `json:"what,omitempty"`
		Value     int     `json:"value,omitempty"`
		Assists   []int32 `json:"assists,omitempty"`
		X         int     `json:"x,omitempty"`
		Y         int     `json:"y,omitempty"`
		StunUntil float64 `json:"stun,omitempty"`
	}
	events := func(list []combatEvent, who func(combatEvent) string, value bool) []eventJSON {
		out := make([]eventJSON, 0, len(list))
		for _, e := range list {
			j := eventJSON{T: m.clock(e.when), Who: who(e), Target: e.Target, What: e.Inflictor, Assists: e.Assists, X: e.X, Y: e.Y, StunUntil: e.StunSeconds}
			if value {
				j.Value = e.Value
			}
			out = append(out, j)
		}
		return out
	}
	attacker := func(e combatEvent) string { return e.Attacker }
	add("kills (hero deaths with killer, assists, x/y)", events(m.deaths, attacker, false))
	add("heals on heroes per event", events(m.heals, attacker, true))
	add("purchases", events(m.purchases, func(e combatEvent) string { return e.Target }, false))
	add("item uses", events(m.itemUses, attacker, false))
	add("ability uses per event", events(m.abilities, attacker, false))
	abilityCounts := map[string]map[string]int{}
	for _, a := range m.abilities {
		if abilityCounts[a.Attacker] == nil {
			abilityCounts[a.Attacker] = map[string]int{}
		}
		abilityCounts[a.Attacker][a.Inflictor]++
	}
	add("ability uses aggregated per hero", abilityCounts)
	add("ability + item uses as [t, name] tuples per hero", usesPerHero(m, append(append([]combatEvent{}, m.abilities...), m.itemUses...)))
	add("ability upgrades", m.ups)
	add("stuns on heroes (modifier add with stun_duration)", events(m.stuns, attacker, false))
	add("buildings/roshan/couriers/ward kills", events(m.buildings, attacker, false))
	add("buybacks", events(m.buybacks, attacker, true))

	type wardJSON struct {
		Type     string  `json:"type"`
		Team     int     `json:"team"`
		Player   int     `json:"player"`
		X        int     `json:"x"`
		Y        int     `json:"y"`
		PlacedAt float64 `json:"placedAt"`
		Removed  float64 `json:"removedAt"`
	}
	wards := []wardJSON{}
	for _, w := range append(m.wardsDone, mapValues(m.wards)...) {
		kind := "observer"
		if w.Sentry {
			kind = "sentry"
		}
		wards = append(wards, wardJSON{kind, w.Team, w.Owner, w.X, w.Y, m.clock(w.placed), m.clock(w.removed)})
	}
	add("wards", wards)
	type chatJSON struct {
		T      float64 `json:"t"`
		Player int     `json:"player"`
		Kind   string  `json:"kind"`
		Text   string  `json:"text,omitempty"`
		Value  int     `json:"value,omitempty"`
	}
	lines := func(list []chatLine) []chatJSON {
		out := []chatJSON{}
		for _, c := range list {
			out = append(out, chatJSON{m.clock(c.when), c.Player, c.Kind, c.Text, c.Value})
		}
		return out
	}
	add("chat + chat wheel", lines(m.chat))
	add("chat events (runes, roshan, aegis, glyph, scan, smoke, wards killed...)", lines(m.events))

	fmt.Printf("\ncounts: damage events with a hero %d, heals %d, hero deaths %d, purchases %d, item uses %d, ability uses %d, modifiers added on heroes %d (with stun %d), wards %d, chat %d, chat events %d\n",
		len(m.damage), len(m.heals), len(m.deaths), len(m.purchases), len(m.itemUses), len(m.abilities), m.modifiers, len(m.stuns), len(wards), len(m.chat), len(m.events))
	fmt.Printf("\n%-75s %10s %10s\n", "section", "JSON KB", "gzip KB")
	for _, s := range sections {
		raw, err := json.Marshal(s.value)
		if err != nil {
			log.Fatal(err)
		}
		fmt.Printf("%-75s %10.1f %10.1f\n", s.name, float64(len(raw))/1024, float64(gzipSize(raw))/1024)
		if outDir != "" {
			file := strings.Map(func(r rune) rune {
				if r >= 'a' && r <= 'z' || r >= '0' && r <= '9' {
					return r
				}
				return '-'
			}, strings.ToLower(s.name))
			if err := os.WriteFile(filepath.Join(outDir, file+".json"), raw, 0o644); err != nil {
				log.Fatal(err)
			}
		}
	}
}

// unitKind groups the units that are not heroes, so an aggregate keeps one row per hero and kind instead of one per
// creep name.
func unitKind(name string, hero, illusion bool) string {
	switch {
	case hero && illusion:
		return "illusion"
	case hero:
		return name
	case strings.HasPrefix(name, "npc_dota_creep"):
		return "lane creeps"
	case strings.HasPrefix(name, "npc_dota_neutral"):
		return "neutrals"
	case strings.Contains(name, "tower") || strings.Contains(name, "rax") || strings.Contains(name, "fort") || strings.Contains(name, "filler") || strings.Contains(name, "watch_tower"):
		return "buildings"
	case strings.Contains(name, "roshan"):
		return "roshan"
	case strings.Contains(name, "miniboss"):
		return "tormentor"
	default:
		return "other units"
	}
}

func aggregateByKind(events []combatEvent) []map[string]any {
	type key struct{ source, target, inflictor string }
	totals := map[key]*[2]int{}
	for _, e := range events {
		k := key{unitKind(e.Attacker, e.AttackerHero, e.AttackerIllusion), unitKind(e.Target, e.TargetHero, e.TargetIllusion), e.Inflictor}
		if totals[k] == nil {
			totals[k] = &[2]int{}
		}
		totals[k][0] += e.Value
		totals[k][1]++
	}
	rows := make([]map[string]any, 0, len(totals))
	for k, v := range totals {
		rows = append(rows, map[string]any{"source": k.source, "target": k.target, "inflictor": k.inflictor, "amount": v[0], "count": v[1]})
	}
	return rows
}

func usesPerHero(m *measured, events []combatEvent) map[string][][2]any {
	uses := map[string][][2]any{}
	for _, e := range events {
		uses[e.Attacker] = append(uses[e.Attacker], [2]any{m.clock(e.when), e.Inflictor})
	}
	return uses
}

func mapValues[K comparable, V any](m map[K]V) []V {
	values := make([]V, 0, len(m))
	for _, v := range m {
		values = append(values, v)
	}
	return values
}

func gzipSize(raw []byte) int {
	var buffer bytes.Buffer
	writer := gzip.NewWriter(&buffer)
	_, _ = writer.Write(raw)
	_ = writer.Close()
	return buffer.Len()
}
