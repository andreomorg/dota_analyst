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

	// m_fCooldown last seen of every item/ability entity (and when), the cooldowns that started (it rose past now) and
	// the ones that dropped while running (DATA-D15: resets, reductions).
	itemCooldownAt         map[entityKey]cooldownReading
	abilityCooldownAt      map[entityKey]cooldownReading
	itemCooldowns          []cooldownEvent
	abilityCooldowns       []cooldownEvent
	itemCooldownChanges    []cooldownChangeEvent
	abilityCooldownChanges []cooldownChangeEvent
	// Stolen abilities (DATA-D17): the ones held now, every period that ended, and their cooldowns that started.
	stolenNow       map[entityKey]*stolenPeriod
	stolenPeriods   []*stolenPeriod
	stolenCooldowns []cooldownChangeEvent
	// The inventory, teleport and neutral slots last read of every hero, and their changes.
	slotsNow    map[string][itemSlots]string // hero name -> item name per slot ("" = empty)
	slotChanges []slotEvent

	final map[string]matchdata.FinalItems // hero name -> items when the match ended
}

// cooldownEvent is a cooldown of a hero's item or ability that started at `at` and is ready at readyAt.
type cooldownEvent struct {
	namedEvent
	readyAt ServerTime
}

// cooldownReading is m_fCooldown (the time remaining) read at a server second.
type cooldownReading struct {
	remaining, at float64
}

// stolenPeriod is a stolen ability held by a hero from `from` to `to` (nil: still held), with its observed level and hidden
// flag.
type stolenPeriod struct {
	hero, name string
	entity     entityKey
	level      int
	hidden     bool
	from       ServerTime
	to         *ServerTime
}

// cooldownChangeEvent is a running cooldown that dropped at `at` (reset or reduction): ready at readyAt instead.
type cooldownChangeEvent struct {
	namedEvent
	entity  entityKey
	readyAt ServerTime
}

// slotEvent is a slot of a hero's inventory that changed at a second of the game clock (name "" = empty).
type slotEvent struct {
	second int
	hero   string
	slot   int
	name   string
}

// Stash slots 9-14 are replicated too, so pending deliveries remain distinguishable from usable items.
// Enhancement changes use the same two-second cadence as artifacts. Backpack slots 6-8 are tracked too (DATA-D16): an
// item moved to or from the backpack shows up, while consumers keep counting only the usable slots as usable.
var trackedSlots = []int{0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, teleportSlot, neutralSlot, enhancementSlot}

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
	c.stolenNow = map[entityKey]*stolenPeriod{}
	c.itemCooldownAt = map[entityKey]cooldownReading{}
	c.abilityCooldownAt = map[entityKey]cooldownReading{}
	c.slotsNow = map[string][itemSlots]string{}

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
		c.onItemCooldown(e, op)
	})
	s.OnEntity("CDOTA_Ability", c.onAbility)
	s.OnSecond(func(second int) {
		if second%matchdata.PositionIntervalSeconds == 0 {
			c.readSlots(second)
			c.readStolen()
		}
	})
	s.OnEnd(func(int) { c.readFinalItems() })
}

// readCooldown remembers m_fCooldown and reports the server time the item or ability is ready, whether a new cooldown
// started and whether a running one dropped. m_fCooldown is the time remaining, in seconds (checked on a replay: a
// teleport's 24.0 is 23.0 a second later and 14.0 ten seconds later), so it only goes down while a cooldown runs: a
// rise means a new one; falling faster than the time passed means a reset or a reduction. An entity seen for the first
// time counts as having had no cooldown (never a drop).
func (c *itemsCollector) readCooldown(e *manta.Entity, last map[entityKey]cooldownReading) (readyAt float64, started, dropped bool) {
	key := keyOf(e)
	remaining, ok := number(e, "m_fCooldown")
	if !ok {
		return 0, false, false
	}
	now := c.session.Clock.Now().Seconds
	before, seen := last[key]
	last[key] = cooldownReading{remaining: remaining, at: now}
	return now + remaining, cooldownRose(before.remaining, remaining), seen && cooldownDropped(before, remaining, now)
}

// cooldownRose says whether the time remaining went up (a new cooldown), given the last value seen. Half a second of
// margin: float noise in the updates is not a new cooldown.
func cooldownRose(before, remaining float64) bool {
	return remaining > before+0.5
}

// cooldownDropped says whether a running cooldown fell more than half a second below what the server time passed
// since the last reading explains (a reset to 0 included). Server time also runs during pauses, when cooldowns stop:
// that only makes the expected value lower, never a false drop.
func cooldownDropped(before cooldownReading, remaining, now float64) bool {
	expected := before.remaining - (now - before.at)
	return expected > 0.5 && remaining < expected-0.5
}

// onItemCooldown records the cooldowns of the players' items (owner by m_iPlayerOwnerID).
func (c *itemsCollector) onItemCooldown(e *manta.Entity, op manta.EntityOp) {
	if op.Flag(manta.EntityOpDeleted) {
		delete(c.itemCooldownAt, keyOf(e))
		return
	}
	readyAt, started, dropped := c.readCooldown(e, c.itemCooldownAt)
	if !started && !dropped {
		return
	}
	owner, ok := number(e, "m_iPlayerOwnerID")
	if !ok {
		return
	}
	hero := c.heroOfPlayerID(int32(owner))
	if hero == "" {
		return
	}
	event := namedEvent{at: c.session.Clock.Now(), hero: hero, name: c.session.EntityName(e)}
	if started {
		c.itemCooldowns = append(c.itemCooldowns, cooldownEvent{namedEvent: event, readyAt: c.session.Clock.At(readyAt)})
	} else {
		c.itemCooldownChanges = append(c.itemCooldownChanges,
			cooldownChangeEvent{namedEvent: event, entity: keyOf(e), readyAt: c.session.Clock.At(readyAt)})
	}
}

// onAbilityCooldown records the cooldowns of the players' heroes' abilities (not stolen ones).
func (c *itemsCollector) onAbilityCooldown(e *manta.Entity, key entityKey) {
	readyAt, started, dropped := c.readCooldown(e, c.abilityCooldownAt)
	if !started && !dropped {
		return
	}
	owner, ok := c.abilityOwner(e, key)
	if !ok {
		return
	}
	event := namedEvent{at: c.session.Clock.Now(), hero: owner.hero, name: c.session.EntityName(e)}
	// A stolen ability's cooldowns are its carrier's, kept apart with the entity (never the original hero's).
	if stolen, _ := e.GetBool("m_bStolen"); stolen {
		if started {
			c.stolenCooldowns = append(c.stolenCooldowns,
				cooldownChangeEvent{namedEvent: event, entity: key, readyAt: c.session.Clock.At(readyAt)})
		}
		return
	}
	if started {
		c.abilityCooldowns = append(c.abilityCooldowns, cooldownEvent{namedEvent: event, readyAt: c.session.Clock.At(readyAt)})
	} else {
		c.abilityCooldownChanges = append(c.abilityCooldownChanges,
			cooldownChangeEvent{namedEvent: event, entity: key, readyAt: c.session.Clock.At(readyAt)})
	}
}

// readStolen follows the stolen abilities (m_bStolen) in every player's hero's m_vecAbilities: a period starts when one
// appears and ends when it is gone (spike: cmd/inspect -stolen-spike).
func (c *itemsCollector) readStolen() {
	now := c.session.Clock.Now()
	seen := map[entityKey]bool{}
	for _, hero := range c.heroesNow() {
		heroName := c.session.EntityName(hero)
		for _, property := range abilitySlotNames {
			handle, ok := hero.GetUint32(property)
			if !ok {
				continue
			}
			ability := c.session.Parser.FindEntityByHandle(uint64(handle))
			if ability == nil {
				continue
			}
			if stolen, _ := ability.GetBool("m_bStolen"); !stolen {
				continue
			}
			key := keyOf(ability)
			seen[key] = true
			level, _ := ability.GetInt32("m_iLevel")
			hidden, _ := ability.GetBool("m_bHidden")
			c.observeStolen(key, heroName, c.session.EntityName(ability), int(level), hidden, now)
		}
	}
	for key, period := range c.stolenNow {
		if !seen[key] {
			at := now
			period.to = &at
			delete(c.stolenNow, key)
		}
	}
}

// observeStolen starts a new observation period when the carrier or replicated state changes.
func (c *itemsCollector) observeStolen(key entityKey, hero, name string, level int, hidden bool, now ServerTime) {
	period := c.stolenNow[key]
	if period != nil && (period.hero != hero || period.name != name || period.level != level || period.hidden != hidden) {
		at := now
		period.to = &at
		period = nil
	}
	if period == nil {
		period = &stolenPeriod{hero: hero, name: name, entity: key, from: now, level: level, hidden: hidden}
		c.stolenNow[key] = period
		c.stolenPeriods = append(c.stolenPeriods, period)
	}
}

// readSlots compares every hero's tracked slots with the last reading and records what changed.
func (c *itemsCollector) readSlots(second int) {
	for _, hero := range c.heroesNow() {
		heroName := c.session.EntityName(hero)
		before, seen := c.slotsNow[heroName]
		var now [itemSlots]string
		for _, slot := range trackedSlots {
			if handle, ok := hero.GetUint32(itemSlotNames[slot]); ok {
				if item := c.session.Parser.FindEntityByHandle(uint64(handle)); item != nil {
					now[slot] = c.session.EntityName(item)
				}
			}
			// The first reading records only the slots with an item; later ones every change.
			if now[slot] != before[slot] && (seen || now[slot] != "") {
				c.slotChanges = append(c.slotChanges, slotEvent{second: second, hero: heroName, slot: slot, name: now[slot]})
			}
		}
		c.slotsNow[heroName] = now
	}
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
		delete(c.abilityCooldownAt, key)
		return
	}
	c.onAbilityCooldown(e, key)
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

	cooldownsByPlayer := func(events []cooldownEvent) [playerCount]matchdata.List[matchdata.Cooldown] {
		var lists [playerCount]matchdata.List[matchdata.Cooldown]
		for _, event := range events {
			if player, t, ok := playerAt(event.namedEvent); ok {
				lists[player] = append(lists[player], matchdata.Cooldown{
					T: matchdata.Seconds(t), Name: event.name, ReadyAt: matchdata.Seconds(clock.Game(event.readyAt))})
			}
		}
		for _, list := range lists {
			sort.SliceStable(list, func(i, j int) bool { return list[i].T < list[j].T })
		}
		return lists
	}
	itemCooldowns := cooldownsByPlayer(c.itemCooldowns)
	abilityCooldowns := cooldownsByPlayer(c.abilityCooldowns)

	changesByPlayer := func(events []cooldownChangeEvent) [playerCount]matchdata.List[matchdata.CooldownChange] {
		var lists [playerCount]matchdata.List[matchdata.CooldownChange]
		for _, event := range events {
			if player, t, ok := playerAt(event.namedEvent); ok {
				lists[player] = append(lists[player], matchdata.CooldownChange{
					T: matchdata.Seconds(t), Name: event.name, Entity: int(event.entity.index), Serial: int(event.entity.serial),
					ReadyAt: matchdata.Seconds(clock.Game(event.readyAt))})
			}
		}
		for _, list := range lists {
			sort.SliceStable(list, func(i, j int) bool { return list[i].T < list[j].T })
		}
		return lists
	}
	itemChanges := changesByPlayer(c.itemCooldownChanges)
	abilityChanges := changesByPlayer(c.abilityCooldownChanges)
	// The stolen abilities' cooldowns share the [t, name, entity, serial, readyAt] tuple (here: cooldowns that started).
	stolenCooldowns := changesByPlayer(c.stolenCooldowns)
	var stolen [playerCount]matchdata.List[matchdata.StolenAbility]
	for _, period := range c.stolenPeriods {
		player, from, ok := playerAt(namedEvent{at: period.from, hero: period.hero, name: period.name})
		if !ok {
			continue
		}
		row := matchdata.StolenAbility{From: matchdata.Seconds(from), Name: period.name, Entity: int(period.entity.index),
			Serial: int(period.entity.serial), Level: period.level, Hidden: period.hidden}
		if period.to != nil && clock.Game(*period.to) <= end {
			to := matchdata.Seconds(clock.Game(*period.to))
			row.To = &to
		}
		stolen[player] = append(stolen[player], row)
	}

	var slots [playerCount]matchdata.List[matchdata.SlotChange]
	for _, change := range c.slotChanges { // already in time order
		player, ok := players.ByHeroName(change.hero)
		if !ok || player < 0 || player >= playerCount || float64(change.second) > end {
			continue
		}
		var name *string
		if change.name != "" {
			itemName := change.name
			name = &itemName
		}
		slots[player] = append(slots[player], matchdata.SlotChange{T: change.second, Slot: change.slot, Name: name})
	}

	items, abilities := matchdata.Items{}, matchdata.Abilities{}
	for _, player := range players.All() {
		i := player.Index
		if i < 0 || i >= playerCount {
			continue
		}
		items.Purchases = append(items.Purchases, matchdata.PlayerItems{Player: i, Items: purchases[i]})
		items.Uses = append(items.Uses, matchdata.PlayerItems{Player: i, Items: itemUses[i]})
		items.Slots = append(items.Slots, matchdata.PlayerSlotChanges{Player: i, Changes: slots[i]})
		items.Cooldowns = append(items.Cooldowns, matchdata.PlayerCooldowns{Player: i, Cooldowns: itemCooldowns[i]})
		abilities.Cooldowns = append(abilities.Cooldowns, matchdata.PlayerCooldowns{Player: i, Cooldowns: abilityCooldowns[i]})
		items.CooldownChanges = append(items.CooldownChanges, matchdata.PlayerCooldownChanges{Player: i, Changes: itemChanges[i]})
		abilities.CooldownChanges = append(abilities.CooldownChanges, matchdata.PlayerCooldownChanges{Player: i, Changes: abilityChanges[i]})
		abilities.Stolen = append(abilities.Stolen, matchdata.PlayerStolenAbilities{Player: i, Abilities: stolen[i], Cooldowns: stolenCooldowns[i]})
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
