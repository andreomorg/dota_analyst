package main

import (
	"fmt"
	"math"
	"reflect"
	"regexp"
	"sort"
	"strings"
	"time"

	"github.com/dotabuff/manta"
	"github.com/dotabuff/manta/dota"
	"google.golang.org/protobuf/encoding/prototext"
	"google.golang.org/protobuf/proto"
	"google.golang.org/protobuf/reflect/protoreflect"
)

// Spike code (sprint 10, card 01): -catalog reads the whole replay and prints an inventory of what it carries, so the
// /v1/match contract can be designed from real data: every combat log type (counts, fields set, samples with names
// resolved), every user message and game event (counts and samples), the entity classes (counts, the properties of
// the interesting ones) and the end-of-match state (per-player stats, items, abilities, draft, CDemoFileInfo).

// skippedMessages are the callbacks the catalog does not hook: the parser's hot internal messages (hooking them turns
// off manta's fast paths and only says what we already know) and the ones handled on their own below.
var skippedMessages = map[string]bool{
	"OnCDemoPacket": true, "OnCDemoSignonPacket": true, "OnCDemoFullPacket": true, "OnCDemoSendTables": true,
	"OnCDemoClassInfo": true, "OnCDemoStringTables": true, "OnCSVCMsg_PacketEntities": true,
	"OnCSVCMsg_UpdateStringTable": true, "OnCSVCMsg_CreateStringTable": true, "OnCNETMsg_Tick": true,
	"OnCMsgSource1LegacyGameEvent": true, "OnCMsgSource1LegacyGameEventList": true,
	"OnCMsgDOTACombatLogEntry": true, "OnCDemoFileInfo": true,
}

// interestingClasses are the entity classes whose properties are printed when the first instance appears.
var interestingClasses = regexp.MustCompile(`Ward|Tower|Barracks|Fort|Roshan|Rune|Courier|Miniboss|Watch|Lotus|Outpost|Healer|Tormentor|Aegis|Twin|Effigy|Filler`)

const samplesPerKind = 3

type sampled struct {
	count   int
	samples []string
}

func (s *sampled) add(sample func() string) {
	s.count++
	if len(s.samples) < samplesPerKind {
		s.samples = append(s.samples, sample())
	}
}

type combatLogType struct {
	sampled
	fields      map[string]int
	withPos     int
	lateSamples []string // samples after 15:00, when the early ones are all pre-game noise
}

type catalog struct {
	parser *manta.Parser

	clock      float64 // last game clock read (seconds since the horn; negative before it)
	clockKnown bool
	gameStart  float64
	pausedTick float64

	messages   map[string]*sampled
	gameEvents map[string]*sampled
	eventTypes map[int32]*dota.CMsgSource1LegacyGameEventListDescriptorT

	combatLog       map[string]*combatLogType
	timestampDiff   []float64 // combat log timestamp - net tick time, in seconds
	clockFromStamp  []float64 // game clock from the timestamp - game clock from the tick
	classCreated    map[string]int
	classAlive      map[string]int
	classMaxAlive   map[string]int
	dumpedClass     map[string]bool
	wardLifetimes   []string
	wardBorn        map[int32]float64
	fileInfo        *dota.CDemoFileInfo
	metadata        *dota.CDOTAMatchMetadataFile
	ticksWithPaused int
	snapshotDone    bool

	// kinds counts the values of a message's "kind" field (ChatEvent.type, OverheadEvent.message_type, ...) and some
	// combat log breakdowns (gold/xp reasons, hero deaths), keyed by message, then by value.
	kinds map[string]map[string]int
}

// kindFields are the fields that say what kind of message it is, counted per value.
var kindFields = []protoreflect.Name{"type", "message_type", "order_type", "event_type", "channel_type", "event_id", "chat_message_id"}

func (c *catalog) countKind(message, kind string) {
	if c.kinds[message] == nil {
		c.kinds[message] = map[string]int{}
	}
	c.kinds[message][kind]++
}

func runCatalog(parser *manta.Parser) {
	started := time.Now()
	c := &catalog{
		parser:        parser,
		messages:      map[string]*sampled{},
		gameEvents:    map[string]*sampled{},
		eventTypes:    map[int32]*dota.CMsgSource1LegacyGameEventListDescriptorT{},
		combatLog:     map[string]*combatLogType{},
		classCreated:  map[string]int{},
		classAlive:    map[string]int{},
		classMaxAlive: map[string]int{},
		dumpedClass:   map[string]bool{},
		wardBorn:      map[int32]float64{},
		kinds:         map[string]map[string]int{},
	}
	parser.Callbacks.OnCDOTAMatchMetadataFile(func(m *dota.CDOTAMatchMetadataFile) error { c.metadata = m; return nil })
	c.hookAllMessages()
	parser.Callbacks.OnCNETMsg_Tick(func(*dota.CNETMsg_Tick) error { c.onTick(); return nil })
	parser.Callbacks.OnCMsgSource1LegacyGameEventList(func(m *dota.CMsgSource1LegacyGameEventList) error {
		for _, d := range m.GetDescriptors() {
			c.eventTypes[d.GetEventid()] = d
		}
		return nil
	})
	parser.Callbacks.OnCMsgSource1LegacyGameEvent(func(m *dota.CMsgSource1LegacyGameEvent) error {
		c.onGameEvent(m)
		return nil
	})
	parser.Callbacks.OnCMsgDOTACombatLogEntry(func(e *dota.CMsgDOTACombatLogEntry) error {
		c.onCombatLog(e)
		return nil
	})
	parser.Callbacks.OnCDemoFileInfo(func(m *dota.CDemoFileInfo) error { c.fileInfo = m; return nil })
	parser.OnEntity(func(e *manta.Entity, op manta.EntityOp) error {
		c.onEntity(e, op)
		return nil
	})

	err := parser.Start()
	fmt.Printf("parsed to net tick %d in %s (catalog hooks make it slower than a real parse)\n",
		parser.NetTick, time.Since(started).Round(time.Millisecond))
	if err != nil {
		fmt.Printf("parse error: %v\n", err)
	}
	c.print()
}

// hookAllMessages registers a counting callback on every OnX method of manta's Callbacks, through reflection: each
// method takes a func(*dota.X) error with its own X, so reflect.MakeFunc builds one of the right type per method.
func (c *catalog) hookAllMessages() {
	callbacks := reflect.ValueOf(c.parser.Callbacks)
	errorType := reflect.TypeFor[error]()
	for i := range callbacks.NumMethod() {
		method := callbacks.Type().Method(i)
		if !strings.HasPrefix(method.Name, "On") || skippedMessages[method.Name] {
			continue
		}
		fnType := method.Type.In(1) // In(0) is the receiver
		name := strings.TrimPrefix(method.Name, "On")
		handler := reflect.MakeFunc(fnType, func(args []reflect.Value) []reflect.Value {
			message, _ := args[0].Interface().(proto.Message)
			c.countMessage(name, message)
			return []reflect.Value{reflect.Zero(errorType)}
		})
		callbacks.Method(i).Call([]reflect.Value{handler})
	}
}

func (c *catalog) countMessage(name string, message proto.Message) {
	s := c.messages[name]
	if s == nil {
		s = &sampled{}
		c.messages[name] = s
	}
	s.add(func() string { return c.stamp() + " " + shortText(message, 400) })
	if message == nil {
		return
	}
	reflected := message.ProtoReflect()
	for _, field := range kindFields {
		if fd := reflected.Descriptor().Fields().ByName(field); fd != nil && !fd.IsList() && fd.Message() == nil {
			value := reflected.Get(fd)
			kind := fmt.Sprint(value.Interface())
			if fd.Enum() != nil {
				if enumValue := fd.Enum().Values().ByNumber(value.Enum()); enumValue != nil {
					kind = string(enumValue.Name())
				}
			}
			c.countKind(name, string(field)+"="+kind)
		}
	}
}

func (c *catalog) stamp() string {
	if !c.clockKnown {
		return fmt.Sprintf("[tick %d]", c.parser.NetTick)
	}
	return fmt.Sprintf("[%s]", clockText(c.clock))
}

func clockText(seconds float64) string {
	sign := ""
	if seconds < 0 {
		sign, seconds = "-", -seconds
	}
	return fmt.Sprintf("%s%d:%04.1f", sign, int(seconds)/60, math.Mod(seconds, 60))
}

func shortText(message proto.Message, limit int) string {
	if message == nil {
		return "<nil>"
	}
	text := strings.Join(strings.Fields(prototext.MarshalOptions{}.Format(message)), " ")
	if len(text) > limit {
		text = text[:limit] + "..."
	}
	return text
}

// onTick keeps the game clock. Unlike gameClock it also runs before the horn (negative clock) so pre-game messages
// get a time too.
func (c *catalog) onTick() {
	rules := findByClass(c.parser, "CDOTAGamerulesProxy")
	if rules == nil {
		return
	}
	gameStart := number(rules, "m_pGameRules.m_flGameStartTime")
	if math.IsNaN(gameStart) || gameStart <= 0 {
		return
	}
	if paused, _ := rules.GetBool("m_pGameRules.m_bGamePaused"); paused {
		c.ticksWithPaused++
	}
	c.gameStart = gameStart
	c.pausedTick = number(rules, "m_pGameRules.m_nTotalPausedTicks")
	c.clock = float64(c.parser.NetTick)/ticksPerSecond - gameStart - c.pausedTick/ticksPerSecond
	c.clockKnown = true
	if !c.snapshotDone && c.clock >= snapshotAt {
		c.snapshotDone = true
		c.printHeroes()
	}
}

func (c *catalog) onGameEvent(m *dota.CMsgSource1LegacyGameEvent) {
	descriptor := c.eventTypes[m.GetEventid()]
	name := fmt.Sprintf("event#%d", m.GetEventid())
	if descriptor != nil {
		name = descriptor.GetName()
	}
	s := c.gameEvents[name]
	if s == nil {
		s = &sampled{}
		c.gameEvents[name] = s
	}
	s.add(func() string {
		parts := []string{c.stamp()}
		for i, key := range m.GetKeys() {
			keyName := fmt.Sprintf("k%d", i)
			if descriptor != nil && i < len(descriptor.GetKeys()) {
				keyName = descriptor.GetKeys()[i].GetName()
			}
			parts = append(parts, keyName+"="+gameEventValue(key))
		}
		return strings.Join(parts, " ")
	})
}

func gameEventValue(key *dota.CMsgSource1LegacyGameEventKeyT) string {
	switch key.GetType() {
	case 1:
		return fmt.Sprintf("%q", key.GetValString())
	case 2:
		return fmt.Sprint(key.GetValFloat())
	case 3:
		return fmt.Sprint(key.GetValLong())
	case 4:
		return fmt.Sprint(key.GetValShort())
	case 5:
		return fmt.Sprint(key.GetValByte())
	case 6:
		return fmt.Sprint(key.GetValBool())
	case 7:
		return fmt.Sprint(key.GetValUint64())
	default:
		return "?"
	}
}

// nameFields are the combat log fields that are indexes into the CombatLogNames string table.
var nameFields = map[string]bool{
	"target_name": true, "target_source_name": true, "attacker_name": true, "damage_source_name": true,
	"inflictor_name": true, "modifier_purge_ability": true, "modifier_purge_npc": true,
}

func (c *catalog) onCombatLog(e *dota.CMsgDOTACombatLogEntry) {
	typeName := strings.TrimPrefix(e.GetType().String(), "DOTA_COMBATLOG_")
	t := c.combatLog[typeName]
	if t == nil {
		t = &combatLogType{fields: map[string]int{}}
		c.combatLog[typeName] = t
	}
	e.ProtoReflect().Range(func(fd protoreflect.FieldDescriptor, _ protoreflect.Value) bool {
		t.fields[string(fd.Name())]++
		return true
	})
	if e.LocationX != nil || e.LocationY != nil {
		t.withPos++
	}
	switch e.GetType() {
	case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_GOLD:
		c.countKind("combat log GOLD", fmt.Sprintf("gold_reason=%d", e.GetGoldReason()))
	case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_XP:
		c.countKind("combat log XP", fmt.Sprintf("xp_reason=%d", e.GetXpReason()))
	case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_DEATH:
		target, _ := c.parser.LookupStringByIndex("CombatLogNames", int32(e.GetTargetName()))
		switch {
		case e.GetIsTargetHero() && !e.GetIsTargetIllusion():
			c.countKind("combat log DEATH", "hero "+target)
		case e.GetIsTargetHero():
			c.countKind("combat log DEATH", "illusion")
		case e.GetIsTargetBuilding():
			c.countKind("combat log DEATH", "building "+target)
		case strings.Contains(target, "roshan") || strings.Contains(target, "miniboss") || strings.Contains(target, "ward") || strings.Contains(target, "courier"):
			c.countKind("combat log DEATH", target)
		}
	case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_DAMAGE:
		switch {
		case e.GetIsAttackerHero() && e.GetIsTargetHero() && !e.GetIsTargetIllusion() && !e.GetIsAttackerIllusion():
			c.countKind("combat log DAMAGE", "hero -> hero")
		case e.GetIsAttackerHero() || e.GetIsTargetHero():
			c.countKind("combat log DAMAGE", "hero involved, other")
		default:
			c.countKind("combat log DAMAGE", "no hero")
		}
	case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_MODIFIER_ADD:
		if e.GetIsTargetHero() && !e.GetIsTargetIllusion() {
			c.countKind("combat log MODIFIER_ADD", "on a hero")
		} else {
			c.countKind("combat log MODIFIER_ADD", "other")
		}
	}
	t.add(func() string { return c.describe(e) })
	if c.clockKnown && c.clock >= 900 && len(t.lateSamples) < samplesPerKind {
		t.lateSamples = append(t.lateSamples, c.describe(e))
	}
	// The timestamp is in server seconds, the same scale as NetTick/30 and m_flGameStartTime.
	if e.Timestamp != nil && c.clockKnown {
		c.timestampDiff = append(c.timestampDiff, float64(e.GetTimestamp())-float64(c.parser.NetTick)/ticksPerSecond)
		fromStamp := float64(e.GetTimestamp()) - c.gameStart - c.pausedTick/ticksPerSecond
		c.clockFromStamp = append(c.clockFromStamp, fromStamp-c.clock)
	}
}

// describe prints the fields set on a combat log entry, with the name indexes resolved.
func (c *catalog) describe(e *dota.CMsgDOTACombatLogEntry) string {
	parts := []string{}
	if e.Timestamp != nil {
		parts = append(parts, "["+clockText(float64(e.GetTimestamp())-c.gameStart-c.pausedTick/ticksPerSecond)+"]")
	}
	e.ProtoReflect().Range(func(fd protoreflect.FieldDescriptor, v protoreflect.Value) bool {
		name := string(fd.Name())
		switch {
		case name == "type" || name == "timestamp":
			return true
		case nameFields[name] || (name == "value" && e.GetType() == dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_PURCHASE):
			// PURCHASE keeps the item name's index in value.
			resolved, _ := c.parser.LookupStringByIndex("CombatLogNames", int32(v.Uint()))
			parts = append(parts, fmt.Sprintf("%s=%s", name, resolved))
		case fd.IsList():
			list := v.List()
			items := make([]string, list.Len())
			for i := range list.Len() {
				items[i] = fmt.Sprint(list.Get(i).Interface())
			}
			parts = append(parts, fmt.Sprintf("%s=[%s]", name, strings.Join(items, ",")))
		default:
			parts = append(parts, fmt.Sprintf("%s=%v", name, v.Interface()))
		}
		return true
	})
	return strings.Join(parts, " ")
}

func (c *catalog) onEntity(e *manta.Entity, op manta.EntityOp) {
	class := e.GetClassName()
	switch {
	case op.Flag(manta.EntityOpCreated):
		c.classCreated[class]++
		c.classAlive[class]++
		if c.classAlive[class] > c.classMaxAlive[class] {
			c.classMaxAlive[class] = c.classAlive[class]
		}
		if strings.Contains(class, "Observer_Ward") {
			c.wardBorn[e.GetIndex()] = c.clock
		}
		if interestingClasses.MatchString(class) && !c.dumpedClass[class] {
			c.dumpedClass[class] = true
			fmt.Printf("\n=== first %s at %s\n", class, c.stamp())
			printProperties(e, nil)
		}
	case op.Flag(manta.EntityOpDeleted):
		c.classAlive[class]--
		if born, ok := c.wardBorn[e.GetIndex()]; ok && strings.Contains(class, "Observer_Ward") {
			delete(c.wardBorn, e.GetIndex())
			if len(c.wardLifetimes) < 8 {
				x, y := position(e)
				owner, _ := e.GetUint64("m_hOwnerEntity")
				team := number(e, "m_iTeamNum")
				c.wardLifetimes = append(c.wardLifetimes, fmt.Sprintf("%s team %.0f at (%.0f,%.0f) owner handle %d: %s -> %s (%.0f s)",
					class, team, x, y, owner, clockText(born), clockText(c.clock), c.clock-born))
			}
		}
	}
}

func printProperties(e *manta.Entity, keys *regexp.Regexp) {
	properties := e.Map()
	names := make([]string, 0, len(properties))
	for name := range properties {
		if keys == nil || keys.MatchString(name) {
			names = append(names, name)
		}
	}
	sort.Strings(names)
	for _, name := range names {
		fmt.Printf("  %s = %v\n", name, properties[name])
	}
}

func (c *catalog) print() {
	fmt.Printf("\n##### combat log (%d types)\n", len(c.combatLog))
	for _, name := range sortedKeys(c.combatLog) {
		t := c.combatLog[name]
		fmt.Printf("\n--- %s: %d entries, %d with location\n  fields: %s\n", name, t.count, t.withPos, fieldCounts(t.fields))
		for _, s := range t.samples {
			fmt.Println("  early:", s)
		}
		for _, s := range t.lateSamples {
			fmt.Println("  late: ", s)
		}
	}
	fmt.Printf("\ncombat log timestamp - NetTick/30: %s\n", spread(c.timestampDiff))
	fmt.Printf("game clock from timestamp - game clock from tick: %s\n", spread(c.clockFromStamp))
	fmt.Printf("ticks with m_bGamePaused: %d, total paused ticks at the end: %.0f\n", c.ticksWithPaused, c.pausedTick)

	fmt.Printf("\n##### user and other messages (%d types)\n", len(c.messages))
	for _, name := range sortedKeys(c.messages) {
		s := c.messages[name]
		fmt.Printf("\n--- %s: %d\n", name, s.count)
		for _, sample := range s.samples {
			fmt.Println("  ", sample)
		}
	}

	fmt.Printf("\n##### game events (%d types seen, %d declared)\n", len(c.gameEvents), len(c.eventTypes))
	for _, name := range sortedKeys(c.gameEvents) {
		s := c.gameEvents[name]
		fmt.Printf("\n--- %s: %d\n", name, s.count)
		for _, sample := range s.samples {
			fmt.Println("  ", sample)
		}
	}

	fmt.Printf("\n##### entity classes (created / most alive at once)\n")
	for _, name := range sortedKeys(c.classCreated) {
		fmt.Printf("  %-55s %6d %5d\n", name, c.classCreated[name], c.classMaxAlive[name])
	}
	fmt.Println("\nobserver/sentry wards (first ones that died):")
	for _, w := range c.wardLifetimes {
		fmt.Println("  ", w)
	}

	c.printEndState()

	fmt.Println("\n##### kinds (per message, per value)")
	for _, message := range sortedKeys(c.kinds) {
		values := c.kinds[message]
		parts := make([]string, 0, len(values))
		for _, value := range sortedKeys(values) {
			parts = append(parts, fmt.Sprintf("%s:%d", value, values[value]))
		}
		fmt.Printf("  %s: %s\n", message, strings.Join(parts, "  "))
	}

	c.printMetadata()

	if c.fileInfo != nil {
		fmt.Printf("\n##### CDemoFileInfo\n%s\n", prototext.MarshalOptions{Multiline: true}.Format(c.fileInfo))
	} else {
		fmt.Println("\n##### CDemoFileInfo: not seen")
	}
}

// printEndState prints, at the last tick, everything one player has in the resource entities, the hero's items and
// abilities (with names from the EntityNames string table) and the draft properties of the game rules.
func (c *catalog) printEndState() {
	resource := findByClass(c.parser, "CDOTA_PlayerResource")
	rules := findByClass(c.parser, "CDOTAGamerulesProxy")
	if resource == nil || rules == nil {
		fmt.Println("\nno player resource / game rules at the end")
		return
	}
	fmt.Println("\n##### end state: CDOTA_PlayerResource, player 0")
	printProperties(resource, regexp.MustCompile(`^m_vec(PlayerData|PlayerTeamData)\.0000\.`))
	for _, className := range []string{"CDOTA_DataRadiant", "CDOTA_DataDire", "CDOTA_DataSpectator"} {
		if data := findByClass(c.parser, className); data != nil {
			fmt.Printf("\n##### end state: %s, entry 0 and team-wide\n", className)
			printProperties(data, regexp.MustCompile(`^m_vecDataTeam\.0000\.|^m_[^v]`))
		}
	}
	for _, className := range []string{"CDOTATeam", "CDOTA_DataCustomTeam"} {
		for _, team := range c.parser.FilterEntity(func(e *manta.Entity) bool { return e.GetClassName() == className }) {
			fmt.Printf("\n##### end state: %s index %d\n", className, team.GetIndex())
			printProperties(team, nil)
		}
	}
	fmt.Println("\n##### end state: game rules (draft, time, winner)")
	printProperties(rules, regexp.MustCompile(`(?i)pick|ban|draft|captain|winn|time|state|mode|net_?worth|roshan|tormentor|glyph|scan`))

	hero := heroOf(c.parser, resource, 0)
	if hero == nil {
		fmt.Println("\nno hero for player 0 at the end")
		return
	}
	fmt.Printf("\n##### end state: hero of player 0 (%s)\n", hero.GetClassName())
	printProperties(hero, regexp.MustCompile(`^m_(i|fl|n|b|h)[A-Z]|^m_vecAbilities|^m_hItems|^CBodyComponent\.m_(cell|vec)`))
}

// printMetadata prints the match metadata the server writes at the end (CDOTAMatchMetadataFile): per team the graphs,
// and the first player in full except cosmetics, so it is clear which fields come filled.
func (c *catalog) printMetadata() {
	if c.metadata == nil || c.metadata.GetMetadata() == nil {
		fmt.Println("\n##### CDOTAMatchMetadataFile: not seen")
		return
	}
	fmt.Printf("\n##### CDOTAMatchMetadataFile (version %d, match %d)\n", c.metadata.GetVersion(), c.metadata.GetMatchId())
	for _, team := range c.metadata.GetMetadata().GetTeams() {
		fmt.Printf("team %d: %d players, graph_experience %d points, graph_gold_earned %d, graph_net_worth %d, kills %d\n",
			team.GetDotaTeam(), len(team.GetPlayers()), len(team.GetGraphExperience()), len(team.GetGraphGoldEarned()),
			len(team.GetGraphNetWorth()), len(team.GetKills()))
		for _, player := range team.GetPlayers() {
			fmt.Printf("  slot %d game player id %d: %d ability upgrades, %d kills entries, %d item purchases, %d inventory snapshots, %d level-up times, graph_net_worth %d, graph_hero_damage %d\n",
				player.GetPlayerSlot(), player.GetGamePlayerId(), len(player.GetAbilityUpgrades()), len(player.GetKills()), len(player.GetItems()),
				len(player.GetInventorySnapshot()), len(player.GetLevelUpTimes()), len(player.GetGraphNetWorth()), len(player.GetGraphHeroDamage()))
		}
	}
	teams := c.metadata.GetMetadata().GetTeams()
	if len(teams) == 0 || len(teams[0].GetPlayers()) == 0 {
		return
	}
	team := proto.Clone(teams[0]).(*dota.CDOTAMatchMetadata_Team)
	player := team.GetPlayers()[0]
	team.Players = nil
	fmt.Printf("\nteam %d without players:\n%s\n", team.GetDotaTeam(), prototext.MarshalOptions{Multiline: true}.Format(team))
	player.EquippedEconItems, player.ContractProgress, player.StrangeGemProgress = nil, nil, nil
	fmt.Printf("first player (cosmetics left out):\n%s\n", prototext.MarshalOptions{Multiline: true}.Format(player))
	rest := proto.Clone(c.metadata.GetMetadata()).(*dota.CDOTAMatchMetadata)
	rest.Teams = nil
	fmt.Printf("metadata without teams:\n%s\n", shortText(rest, 3000))
}

// snapshotAt is the game clock at which the heroes' items and abilities are printed: at the end of the replay the
// item and ability entities are already gone (the handles no longer resolve).
const snapshotAt = 1200

// printHeroes prints every hero's inventory and abilities, and all properties of player 0's first item and ability.
func (c *catalog) printHeroes() {
	resource := findByClass(c.parser, "CDOTA_PlayerResource")
	if resource == nil {
		return
	}
	fmt.Printf("\n##### heroes at %s\n", c.stamp())
	for slot := range 10 {
		hero := heroOf(c.parser, resource, slot)
		if hero == nil {
			continue
		}
		fmt.Printf("\n--- player %d: %s level %.0f, xp %.0f, hp %.0f/%.0f, alive %v\n", slot, hero.GetClassName(),
			number(hero, "m_iCurrentLevel"), number(hero, "m_iCurrentXP"), number(hero, "m_iHealth"),
			number(hero, "m_iMaxHealth"), number(hero, "m_lifeState") == 0)
		c.printHandles(hero, "m_hItems", 25, slot == 0)
		c.printHandles(hero, "m_vecAbilities", 35, slot == 0)
	}
}

// printHandles resolves an entity's handle array (m_hItems.0000...) and prints each entity's name and main properties.
func (c *catalog) printHandles(owner *manta.Entity, array string, max int, dumpFirst bool) {
	fmt.Printf("%s:\n", array)
	first := dumpFirst
	for i := range max {
		// Not GetUint64: these handles come as uint32, unlike m_hSelectedHero (uint64).
		handle := number(owner, fmt.Sprintf("%s.%04d", array, i))
		if math.IsNaN(handle) {
			continue
		}
		e := c.parser.FindEntityByHandle(uint64(handle))
		if e == nil {
			continue
		}
		name := "?"
		if index, ok := e.GetInt32("m_pEntity.m_nameStringTableIndex"); ok {
			name, _ = c.parser.LookupStringByIndex("EntityNames", index)
		}
		if number(e, "m_iLevel") == 0 && strings.Contains(e.GetClassName(), "Special_Bonus") {
			continue // talents not taken: 8 per hero, only noise here
		}
		fmt.Printf("  %02d %-40s %-35s level %.0f charges %.0f cooldown %.1f purchase time %.1f\n", i, e.GetClassName(), name,
			number(e, "m_iLevel"), number(e, "m_iCurrentCharges"), number(e, "m_fCooldown"), number(e, "m_flPurchaseTime"))
		if first {
			first = false
			printProperties(e, nil)
		}
	}
}

func sortedKeys[V any](m map[string]V) []string {
	keys := make([]string, 0, len(m))
	for k := range m {
		keys = append(keys, k)
	}
	sort.Strings(keys)
	return keys
}

func fieldCounts(fields map[string]int) string {
	parts := make([]string, 0, len(fields))
	for _, name := range sortedKeys(fields) {
		parts = append(parts, fmt.Sprintf("%s:%d", name, fields[name]))
	}
	return strings.Join(parts, " ")
}

func spread(values []float64) string {
	if len(values) == 0 {
		return "no samples"
	}
	low, high, sum := math.Inf(1), math.Inf(-1), 0.0
	for _, v := range values {
		low, high, sum = math.Min(low, v), math.Max(high, v), sum+v
	}
	return fmt.Sprintf("n=%d min %.3f avg %.3f max %.3f s", len(values), low, sum/float64(len(values)), high)
}
