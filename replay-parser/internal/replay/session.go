package replay

import (
	"context"
	"math"
	"strings"

	"github.com/dotabuff/manta"
	"github.com/dotabuff/manta/dota"
)

// Session is one read of a whole replay, shared by all the collectors of POST /v1/match. It owns the manta parser and
// registers each manta callback once, fanning it out to the collectors' hooks, and it offers what every collector
// needs and must be computed only once: the clock (with the deferred conversion from server time), the players'
// identity and the name tables.
//
// Hooks are registered in Collector.Register, before the replay is read; they run in the order they were registered.
// Hooks have no error return: a collector that finds bad data remembers it and returns the error from Fill. A panic in
// a hook makes the replay unreadable (manta turns it into an error).
type Session struct {
	// Parser is the escape hatch for what the hooks below do not cover: entity lookups (FindEntityByHandle,
	// FilterEntity) and rare user messages (Parser.Callbacks.OnCDOTAUserMsg_..., stamped with Clock.Now()). Never
	// register on hot messages (CNETMsg_Tick, CSVCMsg_PacketEntities, CDemoPacket): it turns off manta's fast path.
	Parser *manta.Parser
	// Clock stamps moments as ServerTime while the replay is read and converts them to the game clock in Fill.
	Clock *Clock
	// Players links player index 0-9, steam ID, hero, hero entity and combat log name.
	Players *Players

	ctx        context.Context
	singletons map[string]int32 // class name -> entity index of the single-instance entities

	onSecond    []func(second int)
	onEnd       []func(second int)
	onCombatLog []combatLogHook
	onEntity    []entityHook
	onChatEvent []func(*dota.CDOTAUserMsg_ChatEvent, ServerTime)
	onFileInfo  []func(*dota.CDemoFileInfo)

	lastSecond     int     // last whole second of the game clock handed to OnSecond
	secondsStarted bool    // OnSecond has run at least once
	ended          bool    // OnEnd has run
	lastLearnTry   float64 // server second of the last attempt to learn the players
}

type combatLogHook struct {
	types map[dota.DOTA_COMBATLOG_TYPES]bool // nil = every type
	fn    func(CombatEntry)
}

type entityHook struct {
	classPrefix string
	fn          func(*manta.Entity, manta.EntityOp)
}

// CombatEntry is a combat log entry with its names resolved (through the CombatLogNames string table), its time and
// the players involved.
type CombatEntry struct {
	// Raw is manta's message, for the fields not copied here (value, is_*_hero/illusion/building, assist_players,
	// will_reincarnate, ability_level...). Do not keep it after the hook returns: copy what you need.
	Raw *dota.CMsgDOTACombatLogEntry
	// Type is the entry type (DAMAGE, DEATH, PURCHASE...).
	Type dota.DOTA_COMBATLOG_TYPES
	// At is the entry's timestamp (convert with Clock.Seconds in Fill).
	At ServerTime
	// Attacker, Target, Inflictor and Source are the resolved names of attacker_name, target_name, inflictor_name
	// and damage_source_name ("" when the field is empty). Inflictor "" on DAMAGE is a right-click. For PURCHASE the
	// item is in Raw.Value: use Session.Name(entry.Raw.GetValue()).
	Attacker, Target, Inflictor, Source string
	// AttackerPlayer and TargetPlayer are the player index when the attacker/target is a player's hero or one of its
	// illusions (is_*_hero and a known hero name), else -1. Before the players are known (early pre-game) they are
	// always -1: resolve the names again with Players.ByHeroName in Fill when that matters.
	AttackerPlayer, TargetPlayer int
}

func newSession(ctx context.Context, parser *manta.Parser) *Session {
	s := &Session{
		Parser:       parser,
		Clock:        &Clock{parser: parser},
		ctx:          ctx,
		singletons:   map[string]int32{},
		lastLearnTry: math.Inf(-1),
	}
	s.Players = &Players{session: s}
	return s
}

// OnSecond runs fn once per whole second of the game clock, from 0:00 to the end of the match (the second the
// Ancient fell; nothing runs after OnEnd), with the players already known. Pauses do not produce seconds; a second
// the replay skips is not replayed. Use it to sample entities (positions every 2 s, numbers every minute).
func (s *Session) OnSecond(fn func(second int)) {
	s.onSecond = append(s.onSecond, fn)
}

// OnEnd runs fn once, on the tick the match ends (m_flGameEndTime appears), with second = the last second handed to
// OnSecond. Items and abilities still exist then; at the end of the replay they are gone. A replay that stops before
// the match ends gets OnEnd after its last tick instead (entities may be gone by then).
func (s *Session) OnEnd(fn func(second int)) {
	s.onEnd = append(s.onEnd, fn)
}

// OnCombatLog runs fn for every combat log entry of the given types (none = every type), from the start of the
// replay (pre-game included) to its end (post-game included: filter by time when that matters).
func (s *Session) OnCombatLog(fn func(CombatEntry), types ...dota.DOTA_COMBATLOG_TYPES) {
	hook := combatLogHook{fn: fn}
	if len(types) > 0 {
		hook.types = map[dota.DOTA_COMBATLOG_TYPES]bool{}
		for _, t := range types {
			hook.types[t] = true
		}
	}
	s.onCombatLog = append(s.onCombatLog, hook)
}

// OnEntity runs fn for every operation (created, updated, deleted...) on entities whose class name starts with
// classPrefix (e.g. "CDOTA_NPC_Observer_Ward" catches observers and sentries). Updates happen on every tick for
// moving units: keep the work small. Use op.Flag(manta.EntityOpCreated) / op.Flag(manta.EntityOpDeleted).
func (s *Session) OnEntity(classPrefix string, fn func(e *manta.Entity, op manta.EntityOp)) {
	s.onEntity = append(s.onEntity, entityHook{classPrefix: classPrefix, fn: fn})
}

// OnChatEvent runs fn for every CDOTAUserMsg_ChatEvent (runes, Roshan, Aegis, glyphs, scans, smokes, bans, tower
// kills...), stamped with the current time. playerid_1..6 are player indexes 0-9.
func (s *Session) OnChatEvent(fn func(event *dota.CDOTAUserMsg_ChatEvent, at ServerTime)) {
	s.onChatEvent = append(s.onChatEvent, fn)
}

// OnFileInfo runs fn with CDemoFileInfo (match id, game mode, winner, end time, picks and bans), which comes at the
// very end of the file. Not every replay has it.
func (s *Session) OnFileInfo(fn func(*dota.CDemoFileInfo)) {
	s.onFileInfo = append(s.onFileInfo, fn)
}

// Name resolves an index into the CombatLogNames string table ("" when it is not there).
func (s *Session) Name(index uint32) string {
	name, _ := s.Parser.LookupStringByIndex("CombatLogNames", int32(index))
	return name
}

// optionalName resolves a name field of the combat log that may be absent (absent = "", not entry 0 of the table).
func (s *Session) optionalName(index *uint32) string {
	if index == nil {
		return ""
	}
	return s.Name(*index)
}

// EntityName is an entity's internal name (npc_dota_hero_riki, item_blink, riki_smoke_screen), from
// m_pEntity.m_nameStringTableIndex in the EntityNames string table; "" when it has none.
func (s *Session) EntityName(e *manta.Entity) string {
	index, ok := e.GetInt32("m_pEntity.m_nameStringTableIndex")
	if !ok {
		return ""
	}
	name, _ := s.Parser.LookupStringByIndex("EntityNames", index)
	return name
}

// Entity returns the entity of a class that has a single instance (CDOTAGamerulesProxy, CDOTA_PlayerResource,
// CDOTA_DataRadiant, CDOTA_DataDire), or nil while it does not exist. Its index is remembered so the 16 384 entity
// slots are not scanned on every call.
func (s *Session) Entity(className string) *manta.Entity {
	if index, ok := s.singletons[className]; ok {
		if e := s.Parser.FindEntity(index); e != nil && e.GetClassName() == className {
			return e
		}
	}
	found := s.Parser.FilterEntity(func(e *manta.Entity) bool { return e.GetClassName() == className })
	if len(found) == 0 {
		return nil
	}
	s.singletons[className] = found[0].GetIndex()
	return found[0]
}

// start registers the session's manta callbacks. Only what some hook needs is registered: every callback costs time
// on every message of its type.
func (s *Session) start() {
	s.Parser.Callbacks.OnCNETMsg_Tick(func(*dota.CNETMsg_Tick) error { return s.onTick() })
	if len(s.onCombatLog) > 0 {
		s.Parser.Callbacks.OnCMsgDOTACombatLogEntry(func(e *dota.CMsgDOTACombatLogEntry) error {
			s.dispatchCombatLog(e)
			return nil
		})
	}
	if len(s.onEntity) > 0 {
		s.Parser.OnEntity(func(e *manta.Entity, op manta.EntityOp) error {
			class := e.GetClassName()
			for _, hook := range s.onEntity {
				if strings.HasPrefix(class, hook.classPrefix) {
					hook.fn(e, op)
				}
			}
			return nil
		})
	}
	if len(s.onChatEvent) > 0 {
		s.Parser.Callbacks.OnCDOTAUserMsg_ChatEvent(func(e *dota.CDOTAUserMsg_ChatEvent) error {
			at := s.Clock.Now()
			for _, fn := range s.onChatEvent {
				fn(e, at)
			}
			return nil
		})
	}
	if len(s.onFileInfo) > 0 {
		s.Parser.Callbacks.OnCDemoFileInfo(func(info *dota.CDemoFileInfo) error {
			for _, fn := range s.onFileInfo {
				fn(info)
			}
			return nil
		})
	}
}

// onTick runs on every net tick (30 per second): it follows the clock, learns the players and drives OnSecond/OnEnd.
func (s *Session) onTick() error {
	if err := s.ctx.Err(); err != nil {
		s.Parser.Stop()
		return err
	}
	rules := s.Entity("CDOTAGamerulesProxy")
	if rules == nil {
		return nil // still loading
	}
	clock, running := s.Clock.game.update(s.Parser.NetTick, rules)
	if !s.Players.Known() {
		// At most once per server second: it reads ~100 properties and fails until every hero exists.
		if now := s.Clock.Now().Seconds; now-s.lastLearnTry >= 1 {
			s.lastLearnTry = now
			_ = s.Players.learn()
		}
	}
	if s.ended || !s.Players.Known() {
		return nil
	}
	if running {
		if second := int(math.Floor(clock)); !s.secondsStarted || second != s.lastSecond {
			s.secondsStarted, s.lastSecond = true, second
			for _, fn := range s.onSecond {
				fn(second)
			}
		}
	}
	if gameEnd, ok := number(rules, "m_pGameRules.m_flGameEndTime"); ok && gameEnd > 0 && s.Clock.Started() {
		end := s.Clock.At(gameEnd)
		s.Clock.gameEnd = &end
		s.end()
	}
	return nil
}

// end runs the OnEnd hooks once.
func (s *Session) end() {
	if s.ended {
		return
	}
	s.ended = true
	for _, fn := range s.onEnd {
		fn(s.lastSecond)
	}
}

func (s *Session) dispatchCombatLog(e *dota.CMsgDOTACombatLogEntry) {
	entry := CombatEntry{
		Raw:            e,
		Type:           e.GetType(),
		At:             s.Clock.At(float64(e.GetTimestamp())),
		Attacker:       s.optionalName(e.AttackerName),
		Target:         s.optionalName(e.TargetName),
		Inflictor:      s.optionalName(e.InflictorName),
		Source:         s.optionalName(e.DamageSourceName),
		AttackerPlayer: -1,
		TargetPlayer:   -1,
	}
	if e.GetIsAttackerHero() {
		if index, ok := s.Players.ByHeroName(entry.Attacker); ok {
			entry.AttackerPlayer = index
		}
	}
	if e.GetIsTargetHero() {
		if index, ok := s.Players.ByHeroName(entry.Target); ok {
			entry.TargetPlayer = index
		}
	}
	for _, hook := range s.onCombatLog {
		if hook.types == nil || hook.types[entry.Type] {
			hook.fn(entry)
		}
	}
}
