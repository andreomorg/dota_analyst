package replay

import (
	"fmt"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/laning"
	"github.com/dotabuff/manta"
)

func unreadable(err error) error {
	return fmt.Errorf("%w: %w", laning.ErrUnreadableReplay, err)
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

// teamDataEntries remembers which player slot took each team data entry, keyed by team and entry index.
type teamDataEntries map[[2]int]int

// claim gives the entry to the player in slot, or fails when another player already has it: two players with the
// same numbers means the link between them and the team data is wrong.
func (e teamDataEntries) claim(team, index, slot int) error {
	key := [2]int{team, index}
	if other, taken := e[key]; taken {
		return fmt.Errorf("slots %d and %d: both resolve to entry %d of the team data of team %d", other, slot, index, team)
	}
	e[key] = slot
	return nil
}

// uint64Reader is the part of *manta.Entity that teamDataIndex needs (tests hand it a fake).
type uint64Reader interface {
	GetUint64(name string) (uint64, bool)
}

// teamDataIndex finds the player's entry (m_vecDataTeam.000N) in CDOTA_DataRadiant/Dire. The steam ID is the reliable
// link (the entries' m_nPlayerID do not follow the player slots). Only bots and players without a Steam account
// (steam ID 0, so the steam ID cannot tell them apart) fall back to the player's place in the team (m_iTeamSlot). A
// real steam ID missing from the team data is not found: guessing by the team slot could hand this player another
// player's numbers.
func teamDataIndex(data uint64Reader, steamID uint64, teamSlot int) (int, bool) {
	const playersPerTeam = 5
	if steamID != 0 {
		for i := range playersPerTeam {
			if entry, ok := data.GetUint64(fmt.Sprintf("m_vecDataTeam.%04d.m_iPlayerSteamID", i)); ok && entry == steamID {
				return i, true
			}
		}
		return 0, false
	}
	if teamSlot < 0 || teamSlot >= playersPerTeam {
		return 0, false
	}
	return teamSlot, true
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
