package replay

import (
	"fmt"
	"math"
	"sort"
	"strings"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
	"github.com/dotabuff/manta/dota"
)

// itemsCollector fills the items and abilities sections.
//
// Sources:
//   - items.purchases: combat log PURCHASE (target = the hero, item name index in value). The combat log of a replay
//     only has purchases from about a minute before the horn on, so the first ones are not in it: they come from the
//     item entities (m_flPurchaseTime in server time, buyer by m_iPlayerOwnerID, name from EntityNames) made before
//     the first PURCHASE entry, when the log does not have them (same hero and item within a second). Free items (the
//     starting teleport: m_nPurchasedPrice 0) are not purchases. Recipes appear as items (item_recipe_bracer);
//     item_ward_dispenser is left out: the log adds it whenever observers and sentries stack, nobody buys it.
//   - items.uses / abilities.uses: combat log ITEM / ABILITY entries whose caster is a player's hero (not an
//     illusion; couriers, summons and creeps are left out).
//   - items.final: the hero's m_hItems on the tick the match ends (OnEnd); the item entities are gone at the end of
//     the replay.
//   - abilities.upgrades: every rise of m_iLevel of an ability entity in a player's hero's m_vecAbilities, at the tick
//     it happens (so the point spent before the horn gets its real time). Talents (special_bonus_*) are points spent
//     too and are included. Not upgrades: the level an ability is created with (innate abilities start at level 1),
//     rises of abilities that are hidden (sub-abilities) or were hidden until then (granted by the Aghanim's Shard or
//     Scepter, which shows the ability at level 1), of stolen abilities (Rubick), and a second ability of the same
//     hero rising on the same tick (Monkey King's Primal Spring levels with Tree Dance: the lower m_vecAbilities slot
//     wins).
//
// Everything that happens after the Ancient falls is left out.
type itemsCollector struct {
	session *Session

	purchases   []namedEvent // combat log PURCHASE
	itemUses    []namedEvent // combat log ITEM
	abilityUses []namedEvent // combat log ABILITY
	firstLogged *ServerTime  // the first PURCHASE entry of the combat log

	entityPurchases []namedEvent               // purchases read from the item entities before the log has them
	earlyItems      map[entityKey]bool         // item entities already in entityPurchases
	ownerHeroes     map[int32]string           // m_iPlayerOwnerID of the items -> hero name
	abilities       map[entityKey]abilityState // last state seen of every ability entity
	abilityOwners   map[entityKey]abilitySlot  // where each ability entity sits (hero "" = not a player's hero)
	upgrades        []upgradeEvent

	final map[string]matchdata.FinalItems // hero name -> items when the match ended
}

// namedEvent is something a hero did with a named item or ability. The hero is kept by name and resolved to a player
// in Fill: entries from the start of the pre-game come before the players are known.
type namedEvent struct {
	at   ServerTime
	hero string // combat log name of the hero (npc_dota_hero_riki)
	name string // item_blink, riki_smoke_screen
}

type upgradeEvent struct {
	namedEvent
	level int
	slot  int // place in the hero's m_vecAbilities
}

// abilityState is what onAbility remembers of an ability entity between updates.
type abilityState struct {
	level  int32
	hidden bool
}

// abilitySlot is where an ability sits: the hero ("" when it is not a player's hero's) and the m_vecAbilities slot.
type abilitySlot struct {
	hero string
	slot int
}

// entityKey identifies an entity for its whole life: entity indexes are reused, serials tell the uses apart.
type entityKey struct{ index, serial int32 }

func keyOf(e *manta.Entity) entityKey { return entityKey{e.GetIndex(), e.GetSerial()} }

// Slots of the hero's m_hItems and m_vecAbilities.
const (
	inventorySlots  = 6
	backpackFirst   = 6
	backpackSlots   = 3
	teleportSlot    = 15
	neutralSlot     = 16
	enhancementSlot = 17
	itemSlots       = 18 // m_hItems.0000-0017 (the array is longer; heroes do not use the rest)
	abilitySlots    = 35 // m_vecAbilities.0000-0034

	wardDispenser = "item_ward_dispenser"
)

// Property names, built once (fmt.Sprintf on every read would be slow).
var (
	itemSlotNames    = propertyNames("m_hItems.%04d", itemSlots)
	abilitySlotNames = propertyNames("m_vecAbilities.%04d", abilitySlots)
	selectedHeroes   = propertyNames("m_vecPlayerTeamData.%04d.m_hSelectedHero", maxPlayerSlots)
)

func propertyNames(format string, count int) []string {
	names := make([]string, count)
	for i := range names {
		names[i] = fmt.Sprintf(format, i)
	}
	return names
}

func (c *itemsCollector) Register(s *Session) {
	c.session = s
	c.ownerHeroes = map[int32]string{}
	c.earlyItems = map[entityKey]bool{}
	c.abilities = map[entityKey]abilityState{}
	c.abilityOwners = map[entityKey]abilitySlot{}

	s.OnCombatLog(func(entry CombatEntry) {
		switch entry.Type {
		case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_PURCHASE:
			if c.firstLogged == nil {
				at := entry.At
				c.firstLogged = &at
			}
			if item := s.Name(entry.Raw.GetValue()); item != "" && item != wardDispenser && entry.Target != "" {
				c.purchases = append(c.purchases, namedEvent{at: entry.At, hero: entry.Target, name: item})
			}
		case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_ITEM, dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_ABILITY:
			if !entry.Raw.GetIsAttackerHero() || entry.Raw.GetIsAttackerIllusion() || entry.Inflictor == "" {
				return
			}
			event := namedEvent{at: entry.At, hero: entry.Attacker, name: entry.Inflictor}
			if entry.Type == dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_ITEM {
				c.itemUses = append(c.itemUses, event)
			} else {
				c.abilityUses = append(c.abilityUses, event)
			}
		}
	}, dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_PURCHASE, dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_ITEM,
		dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_ABILITY)

	s.OnEntity("CDOTA_Item", func(e *manta.Entity, op manta.EntityOp) {
		// Only while the combat log has no purchases yet (and a second after, for the entries of the same tick):
		// later the log has them all, and an item combined later keeps the purchase time of a component.
		if c.firstLogged == nil || s.Clock.Now().Seconds <= c.firstLogged.Seconds+1 {
			c.onEarlyItem(e, op)
		}
	})
	s.OnEntity("CDOTA_Ability", c.onAbility)
	s.OnEnd(func(int) { c.readFinalItems() })
}

// onEarlyItem remembers, once per entity, an item entity that was bought: it has a price, a purchase time and a
// buyer. The buyer (m_iPlayerOwnerID) may come in an update after the item is created, so updates are looked at too.
func (c *itemsCollector) onEarlyItem(e *manta.Entity, op manta.EntityOp) {
	key := keyOf(e)
	if op.Flag(manta.EntityOpDeleted) || c.earlyItems[key] {
		return
	}
	price, _ := number(e, "m_nPurchasedPrice")
	boughtAt, _ := number(e, "m_flPurchaseTime")
	owner, ok := number(e, "m_iPlayerOwnerID")
	if price <= 0 || boughtAt <= 0 || !ok {
		return
	}
	name := c.session.EntityName(e)
	if !strings.HasPrefix(name, "item_") || name == wardDispenser {
		return
	}
	hero := c.heroOfPlayerID(int32(owner))
	if hero == "" {
		return
	}
	c.earlyItems[key] = true
	c.entityPurchases = append(c.entityPurchases, namedEvent{at: c.session.Clock.At(boughtAt), hero: hero, name: name})
}

// heroOfPlayerID finds the hero name of an entity player ID (m_iPlayerOwnerID of items, m_iPlayerID of heroes; not
// the player index). Found names are remembered.
func (c *itemsCollector) heroOfPlayerID(playerID int32) string {
	if hero, ok := c.ownerHeroes[playerID]; ok {
		return hero
	}
	for _, hero := range c.heroesNow() {
		if id, ok := number(hero, "m_iPlayerID"); ok && int32(id) == playerID {
			name := c.session.EntityName(hero)
			c.ownerHeroes[playerID] = name
			return name
		}
	}
	return ""
}

// heroesNow returns the players' hero entities that exist now (straight from CDOTA_PlayerResource, so it also works
// before the 10 players are known).
func (c *itemsCollector) heroesNow() []*manta.Entity {
	resource := c.session.Entity("CDOTA_PlayerResource")
	if resource == nil {
		return nil
	}
	var heroes []*manta.Entity
	for _, property := range selectedHeroes {
		handle, ok := resource.GetUint64(property)
		if !ok {
			break // past the last slot
		}
		if hero := c.session.Parser.FindEntityByHandle(handle); hero != nil {
			heroes = append(heroes, hero)
		}
	}
	return heroes
}

// onAbility follows the level of every ability entity and records the upgrades of the players' heroes' abilities.
// manta runs the entity hooks after the whole packet is read, so every entity is already in its state of this tick.
func (c *itemsCollector) onAbility(e *manta.Entity, op manta.EntityOp) {
	key := keyOf(e)
	if op.Flag(manta.EntityOpDeleted) {
		delete(c.abilities, key)
		delete(c.abilityOwners, key)
		return
	}
	level, ok := e.GetInt32("m_iLevel")
	if !ok {
		return
	}
	hidden, _ := e.GetBool("m_bHidden")
	last, seen := c.abilities[key]
	c.abilities[key] = abilityState{level: level, hidden: hidden}
	if !seen || level <= last.level {
		return // the level it is created with is not an upgrade
	}
	// Hidden now: a sub-ability. Hidden until now: granted (the Aghanim's Shard shows the ability at level 1).
	if hidden || last.hidden {
		return
	}
	if stolen, _ := e.GetBool("m_bStolen"); stolen {
		return
	}
	owner, ok := c.abilityOwner(e, key)
	if !ok {
		return
	}
	upgrade := upgradeEvent{
		namedEvent: namedEvent{at: c.session.Clock.Now(), hero: owner.hero, name: c.session.EntityName(e)},
		level:      int(level),
		slot:       owner.slot,
	}
	// Two abilities of a hero rising on the same tick: one point leveled both (Monkey King's Tree Dance takes Primal
	// Spring along). The hero's own ability comes first in m_vecAbilities.
	if n := len(c.upgrades); n > 0 && c.upgrades[n-1].hero == owner.hero && c.upgrades[n-1].at == upgrade.at {
		if upgrade.slot < c.upgrades[n-1].slot {
			c.upgrades[n-1] = upgrade
		}
		return
	}
	c.upgrades = append(c.upgrades, upgrade)
}

// abilityOwner is the hero whose m_vecAbilities has this ability, and the slot; false for creeps, couriers and
// summons. The answer is remembered for the life of the entity.
func (c *itemsCollector) abilityOwner(e *manta.Entity, key entityKey) (abilitySlot, bool) {
	if owner, ok := c.abilityOwners[key]; ok {
		return owner, owner.hero != ""
	}
	owner := abilitySlot{}
	for _, hero := range c.heroesNow() {
		for slot, property := range abilitySlotNames {
			handle, ok := hero.GetUint32(property)
			if !ok {
				continue
			}
			if ability := c.session.Parser.FindEntityByHandle(uint64(handle)); ability != nil && keyOf(ability) == key {
				owner = abilitySlot{hero: c.session.EntityName(hero), slot: slot}
				break
			}
		}
		if owner.hero != "" {
			break
		}
	}
	c.abilityOwners[key] = owner
	return owner, owner.hero != ""
}

// readFinalItems reads every hero's slots on the tick the match ends.
func (c *itemsCollector) readFinalItems() {
	c.final = map[string]matchdata.FinalItems{}
	for _, hero := range c.heroesNow() {
		var slots [itemSlots]*string
		for i, property := range itemSlotNames {
			handle, ok := hero.GetUint32(property)
			if !ok {
				continue
			}
			if item := c.session.Parser.FindEntityByHandle(uint64(handle)); item != nil {
				if name := c.session.EntityName(item); name != "" {
					slots[i] = &name
				}
			}
		}
		c.final[c.session.EntityName(hero)] = matchdata.FinalItems{
			Inventory:   matchdata.List[*string](slots[:inventorySlots]),
			Backpack:    matchdata.List[*string](slots[backpackFirst : backpackFirst+backpackSlots]),
			Teleport:    slots[teleportSlot],
			Neutral:     slots[neutralSlot],
			Enhancement: slots[enhancementSlot],
		}
	}
}

func (c *itemsCollector) Fill(m *matchdata.Match) error {
	clock := c.session.Clock
	players := c.session.Players
	end := math.Inf(1)
	if gameEnd, ok := clock.GameEnd(); ok {
		end = clock.Game(gameEnd)
	}
	// playerAt resolves an event to its player and game time; false for unknown heroes and events after the end.
	playerAt := func(event namedEvent) (int, float64, bool) {
		player, ok := players.ByHeroName(event.hero)
		t := clock.Game(event.at)
		return player, t, ok && player >= 0 && player < playerCount && t <= end
	}

	// byPlayer turns events into one time-ordered list per player.
	byPlayer := func(events []namedEvent) [playerCount]matchdata.List[matchdata.TimedName] {
		var lists [playerCount]matchdata.List[matchdata.TimedName]
		for _, event := range events {
			if player, t, ok := playerAt(event); ok {
				lists[player] = append(lists[player], matchdata.TimedName{T: matchdata.Seconds(t), Name: event.name})
			}
		}
		for _, list := range lists {
			sort.SliceStable(list, func(i, j int) bool { return list[i].T < list[j].T })
		}
		return lists
	}
	purchases := byPlayer(append(c.earlyPurchases(), c.purchases...))
	itemUses := byPlayer(c.itemUses)
	abilityUses := byPlayer(c.abilityUses)

	var upgrades [playerCount]matchdata.List[matchdata.AbilityLevel]
	for _, upgrade := range c.upgrades { // already in time order
		if player, t, ok := playerAt(upgrade.namedEvent); ok {
			upgrades[player] = append(upgrades[player],
				matchdata.AbilityLevel{T: matchdata.Seconds(t), Name: upgrade.name, Level: upgrade.level})
		}
	}

	items, abilities := matchdata.Items{}, matchdata.Abilities{}
	for _, player := range players.All() {
		i := player.Index
		if i < 0 || i >= playerCount {
			continue
		}
		items.Purchases = append(items.Purchases, matchdata.PlayerItems{Player: i, Items: purchases[i]})
		items.Uses = append(items.Uses, matchdata.PlayerItems{Player: i, Items: itemUses[i]})
		final, ok := c.final[player.HeroName]
		if !ok { // the replay stopped before the end and the hero was gone
			final = matchdata.FinalItems{
				Inventory: make(matchdata.List[*string], inventorySlots),
				Backpack:  make(matchdata.List[*string], backpackSlots),
			}
		}
		final.Player = i
		items.Final = append(items.Final, final)
		abilities.Upgrades = append(abilities.Upgrades, matchdata.PlayerAbilityLevels{Player: i, Levels: upgrades[i]})
		abilities.Uses = append(abilities.Uses, matchdata.PlayerAbilities{Player: i, Abilities: abilityUses[i]})
	}
	m.Items = items
	m.Abilities = abilities
	return nil
}

// earlyPurchases are the purchases read from the item entities that the combat log does not have (same hero and item
// within a second).
func (c *itemsCollector) earlyPurchases() []namedEvent {
	clock := c.session.Clock
	var early []namedEvent
	for _, candidate := range c.entityPurchases {
		t := clock.Game(candidate.at)
		logged := false
		for _, purchase := range c.purchases {
			if purchase.hero == candidate.hero && purchase.name == candidate.name &&
				math.Abs(clock.Game(purchase.at)-t) <= 1 {
				logged = true
				break
			}
		}
		if !logged {
			early = append(early, candidate)
		}
	}
	return early
}
