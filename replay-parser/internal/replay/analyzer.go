// Package replay reads Dota 2 replays with manta and hands the laning package what it needs: the game clock, where
// each hero is and each player's numbers. It is the only package that knows manta (anti-corruption layer): entity
// classes and property names stay in here.
package replay

import (
	"context"
	"fmt"
	"io"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/laning"
	"github.com/dotabuff/manta"
	"github.com/dotabuff/manta/dota"
)

// Analyzer reads the laning phase of a replay. The zero value is ready to use and safe for concurrent use: every call
// to Analyze keeps its own state.
type Analyzer struct{}

// Analyze reads the replay from r (already decompressed: a plain .dem stream) up to 10:00 of the game clock, or to the
// end when the match ends first, and stops reading there. A replay manta cannot read, or without 10 players, gives an
// error wrapping laning.ErrUnreadableReplay; a cancelled ctx stops the parse and gives ctx.Err().
func (Analyzer) Analyze(ctx context.Context, r io.Reader) (result laning.Result, err error) {
	if err := ctx.Err(); err != nil {
		return laning.Result{}, err
	}
	// manta turns panics inside Start into errors, but corrupt input can still panic elsewhere (e.g. while reading the
	// header); a bad replay must never take the whole service down.
	defer func() {
		if recovered := recover(); recovered != nil {
			result, err = laning.Result{}, fmt.Errorf("%w: panic: %v", laning.ErrUnreadableReplay, recovered)
		}
	}()

	parser, err := manta.NewStreamParser(r)
	if err != nil {
		return laning.Result{}, unreadable(err)
	}
	p := &parse{ctx: ctx, parser: parser, entities: map[string]int32{}}
	parser.Callbacks.OnCNETMsg_Tick(func(*dota.CNETMsg_Tick) error { return p.onTick() })

	startErr := parser.Start()
	// Checked first: when ctx is cancelled the reader usually fails too, and that is not the replay's fault.
	if err := ctx.Err(); err != nil {
		return laning.Result{}, err
	}
	if startErr != nil {
		return laning.Result{}, unreadable(startErr)
	}
	return p.result()
}

func unreadable(err error) error {
	return fmt.Errorf("%w: %w", laning.ErrUnreadableReplay, err)
}

// parse is the state of one call to Analyze. Nothing is kept in package variables: the service may parse two
// replays at the same time.
type parse struct {
	ctx      context.Context
	parser   *manta.Parser
	entities map[string]int32 // class name -> entity index of the singletons (game rules, player resource, team data)

	positions  laning.Aggregator
	clockRan   bool // the game clock has run at least once (the horn sounded)
	lastSecond int  // last whole second of the game clock already sampled
	done       bool // 10:00 reached: the numbers in last are final

	// last holds the players' numbers at the last sampled second (or at 10:00), so a replay that ends before 10:00
	// still has numbers: the ones at its end.
	last struct {
		second  int
		players []laning.PlayerAtEnd
		err     error
	}
}

// onTick runs on every net tick (30 per second). The clock is checked on every tick so the cut at 10:00 is exact;
// positions and numbers are read once per second of game clock.
func (p *parse) onTick() error {
	if err := p.ctx.Err(); err != nil {
		p.parser.Stop()
		return err
	}
	if p.done {
		return nil
	}
	rules, resource := p.find("CDOTAGamerulesProxy"), p.find("CDOTA_PlayerResource")
	if rules == nil || resource == nil {
		return nil // still loading
	}
	clock, running := p.gameClock(rules)
	if !running {
		return nil
	}

	if clock >= laning.EndsAtSeconds {
		p.readPlayers(resource, laning.EndsAtSeconds)
		p.done = true
		p.parser.Stop() // no need to read the rest of the replay (about 40% of the time saved)
		return nil
	}
	if second := int(clock); !p.clockRan || second != p.lastSecond {
		p.clockRan, p.lastSecond = true, second
		p.samplePositions(resource, second)
		p.readPlayers(resource, second)
	}
	return nil
}

// result turns what was read into the laning phase: at 10:00, or at the last second read when the replay ended first.
func (p *parse) result() (laning.Result, error) {
	if !p.clockRan {
		return laning.Result{}, fmt.Errorf("%w: the game clock never started", laning.ErrUnreadableReplay)
	}
	if p.last.err != nil {
		return laning.Result{}, unreadable(p.last.err)
	}
	return p.positions.Result(p.last.second, p.last.players), nil
}

const ticksPerSecond = 30

// gameClock returns the seconds since the horn (0:00), pauses left out. It is not running before the horn or while
// the game is paused: the paused ticks are probably only added to m_nTotalPausedTicks when the pause ends.
func (p *parse) gameClock(rules *manta.Entity) (float64, bool) {
	gameStart, ok := number(rules, "m_pGameRules.m_flGameStartTime")
	if !ok || gameStart <= 0 {
		return 0, false
	}
	if paused, _ := rules.GetBool("m_pGameRules.m_bGamePaused"); paused {
		return 0, false
	}
	pausedTicks, _ := number(rules, "m_pGameRules.m_nTotalPausedTicks")
	clock := float64(p.parser.NetTick)/ticksPerSecond - gameStart - pausedTicks/ticksPerSecond
	return clock, clock >= 0
}

// samplePositions records where each living hero is at this second of the game clock.
func (p *parse) samplePositions(resource *manta.Entity, second int) {
	for slot := range maxPlayerSlots {
		hero := p.heroOf(resource, slot)
		if hero == nil {
			continue
		}
		if lifeState, _ := number(hero, "m_lifeState"); lifeState != 0 {
			continue // dead (2) or dying: a dead hero's position says nothing about the lane
		}
		if x, y, ok := position(hero); ok {
			p.positions.AddSample(second, slot, x, y)
		}
	}
}

// heroOf follows the player's hero handle. Looking heroes up by class name would also pick up illusions (several
// CDOTA_Unit_Hero_Riki entities, for example).
func (p *parse) heroOf(resource *manta.Entity, slot int) *manta.Entity {
	handle, ok := resource.GetUint64(fmt.Sprintf("m_vecPlayerTeamData.%04d.m_hSelectedHero", slot))
	if !ok {
		return nil
	}
	return p.parser.FindEntityByHandle(handle)
}

// position converts the cell + offset coordinates to world units (map roughly -8200..8200, Radiant bottom-left).
func position(hero *manta.Entity) (x, y float64, ok bool) {
	const cellWidth, worldOffset = 128, 16384
	f := fields{entity: hero}
	x = f.float("CBodyComponent.m_cellX")*cellWidth + f.float("CBodyComponent.m_vecX") - worldOffset
	y = f.float("CBodyComponent.m_cellY")*cellWidth + f.float("CBodyComponent.m_vecY") - worldOffset
	return x, y, f.err == nil
}

const (
	maxPlayerSlots = 24 // the 10 players are in slots 0-9 so far; the rest is room for spectators and coaches
	playerCount    = 10
	teamRadiant    = 2
	teamDire       = 3
)

// readPlayers keeps each player's numbers at this second of the game clock in p.last.
func (p *parse) readPlayers(resource *manta.Entity, second int) {
	p.last.second = second
	p.last.players, p.last.err = p.players(resource)
}

func (p *parse) players(resource *manta.Entity) ([]laning.PlayerAtEnd, error) {
	teamData := map[int]*manta.Entity{teamRadiant: p.find("CDOTA_DataRadiant"), teamDire: p.find("CDOTA_DataDire")}
	players := make([]laning.PlayerAtEnd, 0, playerCount)
	for slot := range maxPlayerSlots {
		player := fmt.Sprintf("m_vecPlayerData.%04d.", slot)
		playerTeam := fmt.Sprintf("m_vecPlayerTeamData.%04d.", slot)
		resourceFields := fields{entity: resource}
		team := resourceFields.int(player + "m_iPlayerTeam")
		heroID := resourceFields.int(playerTeam + "m_nSelectedHeroID")
		if resourceFields.err != nil {
			break // past the last slot
		}
		if (team != teamRadiant && team != teamDire) || heroID <= 0 {
			continue // spectator, coach or empty slot
		}

		steamID, ok := resource.GetUint64(player + "m_iPlayerSteamID")
		if !ok {
			return nil, fmt.Errorf("slot %d: property %s not found", slot, player+"m_iPlayerSteamID")
		}
		deaths := resourceFields.int(playerTeam + "m_iDeaths")
		teamSlot := resourceFields.int(playerTeam + "m_iTeamSlot")
		if resourceFields.err != nil {
			return nil, fmt.Errorf("slot %d: %w", slot, resourceFields.err)
		}

		data := teamData[team]
		if data == nil {
			return nil, fmt.Errorf("slot %d: no team data entity for team %d", slot, team)
		}
		index, ok := teamDataIndex(data, steamID, teamSlot)
		if !ok {
			return nil, fmt.Errorf("slot %d: player not found in the team data (steam ID %d, team slot %d)", slot, steamID, teamSlot)
		}
		prefix := fmt.Sprintf("m_vecDataTeam.%04d.", index)
		stats := fields{entity: data}
		numbers := laning.PlayerLaning{
			HeroID:    heroID,
			SteamID:   steamID,
			IsRadiant: team == teamRadiant,
			LastHits:  stats.int(prefix + "m_iLastHitCount"),
			Denies:    stats.int(prefix + "m_iDenyCount"), // not m_iClaimedDenyCount: this is the scoreboard's
			NetWorth:  stats.int(prefix + "m_iNetWorth"),
			XP:        stats.int(prefix + "m_iTotalEarnedXP"),
			Deaths:    deaths,
		}
		if stats.err != nil {
			return nil, fmt.Errorf("slot %d: %w", slot, stats.err)
		}
		players = append(players, laning.PlayerAtEnd{Slot: slot, PlayerLaning: numbers})
	}
	if len(players) != playerCount {
		return nil, fmt.Errorf("found %d players with a hero, want %d", len(players), playerCount)
	}
	return players, nil
}

// uint64Reader is the part of *manta.Entity that teamDataIndex needs (tests hand it a fake).
type uint64Reader interface {
	GetUint64(name string) (uint64, bool)
}

// teamDataIndex finds the player's entry (m_vecDataTeam.000N) in CDOTA_DataRadiant/Dire. The steam ID is the reliable
// link (the entries' m_nPlayerID do not follow the player slots); bots and players without a Steam account all have
// steam ID 0, so for them the player's place in the team (m_iTeamSlot) is used.
func teamDataIndex(data uint64Reader, steamID uint64, teamSlot int) (int, bool) {
	const playersPerTeam = 5
	if steamID != 0 {
		for i := range playersPerTeam {
			if entry, ok := data.GetUint64(fmt.Sprintf("m_vecDataTeam.%04d.m_iPlayerSteamID", i)); ok && entry == steamID {
				return i, true
			}
		}
	}
	if teamSlot < 0 || teamSlot >= playersPerTeam {
		return 0, false
	}
	return teamSlot, true
}

// find returns the entity of a class that has a single instance. Its index is remembered so the 16 384 entity slots
// are not scanned on every tick.
func (p *parse) find(className string) *manta.Entity {
	if index, ok := p.entities[className]; ok {
		if e := p.parser.FindEntity(index); e != nil && e.GetClassName() == className {
			return e
		}
	}
	found := p.parser.FilterEntity(func(e *manta.Entity) bool { return e.GetClassName() == className })
	if len(found) == 0 {
		return nil
	}
	p.entities[className] = found[0].GetIndex()
	return found[0]
}

// fields reads numeric properties of one entity and keeps the first error, so a group of reads is checked once at
// the end (the same idea as bufio.Writer's sticky error).
type fields struct {
	entity *manta.Entity
	err    error
}

func (f *fields) float(name string) float64 {
	value, ok := number(f.entity, name)
	if !ok && f.err == nil {
		f.err = fmt.Errorf("property %s not found on %s", name, f.entity.GetClassName())
	}
	return value
}

func (f *fields) int(name string) int {
	return int(f.float(name))
}

// number reads any numeric property as float64: the wire types vary between properties and patches (int32, uint32,
// uint64, float32). ok is false when the property does not exist or is not a number.
func number(e *manta.Entity, name string) (float64, bool) {
	switch v := e.Get(name).(type) {
	case int32:
		return float64(v), true
	case uint32:
		return float64(v), true
	case int64:
		return float64(v), true
	case uint64:
		return float64(v), true
	case float32:
		return float64(v), true
	case bool:
		if v {
			return 1, true
		}
		return 0, true
	default:
		return 0, false
	}
}
