package replay

import (
	"fmt"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
)

// PlayerInfo is who a player is, learned once per replay (as soon as all 10 heroes exist, before the horn).
type PlayerInfo struct {
	// Index is the player index 0-9: the slot in CDOTA_PlayerResource, the same number the combat log
	// (assist_players, BUYBACK value) and the chat events use. Entities use twice this number (m_iPlayerID of the hero,
	// m_nPlayerID of the controller): never link by those.
	Index int
	// SteamID is the SteamID64 (0 for bots).
	SteamID uint64
	// Name is the in-game name.
	Name string
	// Team is radiant or dire.
	Team matchdata.Team
	// TeamNumber is the game's team number (2 Radiant, 3 Dire), as in m_iTeamNum of entities.
	TeamNumber int
	// TeamSlot is the place in the team, 0-4.
	TeamSlot int
	// HeroID is Valve's hero id.
	HeroID int
	// HeroName is the hero's internal name (npc_dota_hero_riki), the name the combat log uses.
	HeroName string

	// Property prefixes, built once (fmt.Sprintf on every read would be slow).
	resourcePlayer string // "m_vecPlayerData.000N." in CDOTA_PlayerResource
	resourceTeam   string // "m_vecPlayerTeamData.000N." in CDOTA_PlayerResource
	dataPrefix     string // "m_vecDataTeam.000N." in CDOTA_DataRadiant/Dire
	dataClass      string // CDOTA_DataRadiant or CDOTA_DataDire
}

// Players links the player index 0-9 to steam ID, hero, hero entity and combat log name. It is filled once, by the
// session, on the first tick on which all 10 players have a hero and their team data entry.
type Players struct {
	session    *Session
	list       []PlayerInfo
	byHeroName map[string]int
	bySteamID  map[uint64]int
}

// Known reports whether the players have been found. Hooks that run before that (early pre-game combat log entries,
// entities) get -1 from the lookups; OnSecond and OnEnd only run once they are known.
func (p *Players) Known() bool {
	return p.list != nil
}

// All returns the 10 players ordered by index (a copy: changing it changes nothing).
func (p *Players) All() []PlayerInfo {
	return append([]PlayerInfo(nil), p.list...)
}

// Get returns the player with this index.
func (p *Players) Get(index int) (PlayerInfo, bool) {
	for _, player := range p.list {
		if player.Index == index {
			return player, true
		}
	}
	return PlayerInfo{}, false
}

// ByHeroName returns the index of the player whose hero has this combat log name (npc_dota_hero_riki). Illusions
// have the hero's name, so they resolve to the owner: check the entry's illusion flag when that matters. Before all
// 10 players are known (the draft is still going on while the first players buy items), it looks at the heroes that
// already exist.
func (p *Players) ByHeroName(name string) (int, bool) {
	if p.Known() {
		index, ok := p.byHeroName[name]
		return index, ok
	}
	return p.findHeroNow(name)
}

// findHeroNow looks for a player whose hero exists and has this name, without the full identity.
func (p *Players) findHeroNow(name string) (int, bool) {
	resource := p.session.Entity("CDOTA_PlayerResource")
	if resource == nil || name == "" {
		return 0, false
	}
	for slot := range playerCount {
		handle, ok := resource.GetUint64(fmt.Sprintf("m_vecPlayerTeamData.%04d.m_hSelectedHero", slot))
		if !ok {
			return 0, false
		}
		if hero := p.session.Parser.FindEntityByHandle(handle); hero != nil && p.session.EntityName(hero) == name {
			return slot, true
		}
	}
	return 0, false
}

// BySteamID returns the index of the player with this SteamID64 (useless for bots, whose steam ID is 0).
func (p *Players) BySteamID(steamID uint64) (int, bool) {
	if steamID == 0 {
		return 0, false
	}
	index, ok := p.bySteamID[steamID]
	return index, ok
}

// ByHeroEntity returns the index of the player whose (real) hero is this entity. Illusions are other entities, so
// they are not found.
func (p *Players) ByHeroEntity(e *manta.Entity) (int, bool) {
	if e == nil {
		return 0, false
	}
	for _, player := range p.list {
		if hero := p.Hero(player.Index); hero != nil && hero.GetIndex() == e.GetIndex() {
			return player.Index, true
		}
	}
	return 0, false
}

// Hero returns the player's hero entity now (following m_hSelectedHero, so illusions are never returned), or nil.
func (p *Players) Hero(index int) *manta.Entity {
	player, ok := p.Get(index)
	resource := p.session.Entity("CDOTA_PlayerResource")
	if !ok || resource == nil {
		return nil
	}
	handle, ok := resource.GetUint64(player.resourceTeam + "m_hSelectedHero")
	if !ok {
		return nil
	}
	return p.session.Parser.FindEntityByHandle(handle)
}

// Resource returns CDOTA_PlayerResource and the player's two property prefixes in it: "m_vecPlayerData.000N." (steam
// ID, name, team, rank) and "m_vecPlayerTeamData.000N." (hero, K/D/A, level). entity is nil while it does not exist.
func (p *Players) Resource(index int) (entity *manta.Entity, playerPrefix, teamPrefix string) {
	player, ok := p.Get(index)
	if !ok {
		return nil, "", ""
	}
	return p.session.Entity("CDOTA_PlayerResource"), player.resourcePlayer, player.resourceTeam
}

// TeamData returns the player's CDOTA_DataRadiant/CDOTA_DataDire entity and the prefix of their entry in it
// ("m_vecDataTeam.000N.": gold, XP, net worth, last hits, damage...). entity is nil while it does not exist.
func (p *Players) TeamData(index int) (entity *manta.Entity, prefix string) {
	player, ok := p.Get(index)
	if !ok {
		return nil, ""
	}
	return p.session.Entity(player.dataClass), player.dataPrefix
}

// learn tries to find the 10 players on this tick. It fails (and is tried again later) while heroes or team data are
// still missing.
func (p *Players) learn() error {
	s := p.session
	resource := s.Entity("CDOTA_PlayerResource")
	if resource == nil {
		return fmt.Errorf("no CDOTA_PlayerResource yet")
	}
	dataClasses := map[int]string{teamRadiant: "CDOTA_DataRadiant", teamDire: "CDOTA_DataDire"}
	list := make([]PlayerInfo, 0, playerCount)
	entries := teamDataEntries{}
	for slot := range maxPlayerSlots {
		player := PlayerInfo{
			Index:          slot,
			resourcePlayer: fmt.Sprintf("m_vecPlayerData.%04d.", slot),
			resourceTeam:   fmt.Sprintf("m_vecPlayerTeamData.%04d.", slot),
		}
		f := fields{entity: resource}
		player.TeamNumber = f.int(player.resourcePlayer + "m_iPlayerTeam")
		player.HeroID = f.int(player.resourceTeam + "m_nSelectedHeroID")
		if f.err != nil {
			break // past the last slot
		}
		if player.TeamNumber != teamRadiant && player.TeamNumber != teamDire {
			continue // spectator, coach or empty slot
		}
		if player.HeroID <= 0 {
			return fmt.Errorf("slot %d has no hero yet", slot)
		}
		player.Team = teamOf(player.TeamNumber)
		player.TeamSlot = f.int(player.resourceTeam + "m_iTeamSlot")
		if f.err != nil {
			return fmt.Errorf("slot %d: %w", slot, f.err)
		}
		steamID, ok := resource.GetUint64(player.resourcePlayer + "m_iPlayerSteamID")
		if !ok {
			return fmt.Errorf("slot %d: no m_iPlayerSteamID", slot)
		}
		player.SteamID = steamID
		player.Name, _ = resource.GetString(player.resourcePlayer + "m_iszPlayerName")

		handle, ok := resource.GetUint64(player.resourceTeam + "m_hSelectedHero")
		hero := s.Parser.FindEntityByHandle(handle)
		if !ok || hero == nil {
			return fmt.Errorf("slot %d: hero entity not found yet", slot)
		}
		if player.HeroName = s.EntityName(hero); player.HeroName == "" {
			return fmt.Errorf("slot %d: hero entity without a name", slot)
		}

		player.dataClass = dataClasses[player.TeamNumber]
		data := s.Entity(player.dataClass)
		if data == nil {
			return fmt.Errorf("slot %d: no %s yet", slot, player.dataClass)
		}
		index, ok := teamDataIndex(data, steamID, player.TeamSlot)
		if !ok {
			return fmt.Errorf("slot %d: player not found in %s", slot, player.dataClass)
		}
		if err := entries.claim(player.TeamNumber, index, slot); err != nil {
			return err
		}
		player.dataPrefix = fmt.Sprintf("m_vecDataTeam.%04d.", index)
		list = append(list, player)
	}
	if len(list) != playerCount {
		return fmt.Errorf("found %d players with a hero, want %d", len(list), playerCount)
	}

	p.byHeroName, p.bySteamID = map[string]int{}, map[uint64]int{}
	for _, player := range list {
		if other, taken := p.byHeroName[player.HeroName]; taken {
			return fmt.Errorf("players %d and %d both have hero %s", other, player.Index, player.HeroName)
		}
		p.byHeroName[player.HeroName] = player.Index
		if player.SteamID != 0 {
			p.bySteamID[player.SteamID] = player.Index
		}
	}
	p.list = list
	return nil
}

// teamOf turns the game's team number into the contract's team ("" for anything else: neutrals, spectators).
func teamOf(teamNumber int) matchdata.Team {
	switch teamNumber {
	case teamRadiant:
		return matchdata.Radiant
	case teamDire:
		return matchdata.Dire
	default:
		return ""
	}
}
