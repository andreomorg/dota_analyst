package replay

import (
	"cmp"
	"math"
	"slices"
	"strings"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta/dota"
)

// combatCollector fills the kills, buybacks and combat sections, all from the combat log.
//
//   - kills: DEATH of a real hero (is_target_hero and not is_target_illusion). The combat log has no location, so the
//     victim's position is read from the hero entity in the same hook. assist_players are player indexes and include
//     the killer, who is taken out. will_reincarnate marks Aegis/Reincarnation deaths (not on the scoreboard).
//     FIRST_BLOOD, MULTIKILL and KILLSTREAK come just before the DEATH they belong to, with the same timestamp.
//   - buybacks: BUYBACK, with the player index in value; the cost is the rise of m_iGoldSpentOnBuybacks seen in the
//     following seconds.
//   - combat: DAMAGE and HEAL added up per source, target, inflictor and damage type while the replay is read (a few
//     hundred rows instead of ~15 000 events). CRITICAL_DAMAGE is only the "critical strike" announcement of a hit
//     that also comes as DAMAGE, so it is not counted. A unit that is not a hero belongs to a player when its
//     damage_source_name (target_source_name for targets) is that player's hero: summons, Tombstone zombies, dominated
//     creeps.
//
// Entries after the match ended (the Ancient fell) are left out. Times stay as ServerTime until Fill.
type combatCollector struct {
	waves      *wavesCollector
	laneDeaths []laneCreepDeathEvent
	session    *Session

	kills    []pendingCombatKill
	buybacks []pendingCombatBuyback
	// announcements waiting for the DEATH they belong to (same timestamp).
	firstBlood    bool
	firstBloodAt  float32
	announcements map[string]killAnnouncement // attacker hero name -> multikill/streak

	damage  map[combatKey]*combatTotal
	healing map[combatKey]*combatTotal
	minutes []combatMinuteEvent
	typed   []typedCombatEvent
	// Damage between enemy players' heroes, by attacker and target (bucketed in Fill), and creep last hits.
	hits       []heroHitEvent
	creepKills []creepKillEvent

	spentOnBuybacks [playerCount]int  // m_iGoldSpentOnBuybacks on the last second read
	spentKnown      [playerCount]bool // spentOnBuybacks was read at least once
}

type pendingCombatKill struct {
	kill matchdata.Kill
	at   ServerTime
}

type pendingCombatBuyback struct {
	player int
	at     ServerTime
	cost   *int
}

type killAnnouncement struct {
	at        float32
	multikill int
	streak    int
}

// combatUnit is one side of a combat log entry once classified: the player it belongs to (-1 = nobody) and its kind.
type combatUnit struct {
	player int
	kind   matchdata.UnitKind
}

type combatKey struct {
	source, target combatUnit
	inflictor      string
	damageType     matchdata.DamageType
}

type combatTotal struct {
	amount int
	hits   int
}

// combatMinuteField says which per-minute list a combatMinuteEvent adds to.
type combatMinuteField int

const (
	minuteToHeroes combatMinuteField = iota
	minuteFromHeroes
	minuteTaken
	minuteHealed
)

type heroHitEvent struct {
	at               ServerTime
	attacker, target int
	amount           int
}

type creepKillEvent struct {
	at     ServerTime
	player int
	kind   matchdata.CreepKind
	x, y   *int
}

type combatMinuteEvent struct {
	at     ServerTime
	player int
	field  combatMinuteField
	amount int
}

func (c *combatCollector) Register(s *Session) {
	c.session = s
	c.announcements = map[string]killAnnouncement{}
	c.damage = map[combatKey]*combatTotal{}
	c.healing = map[combatKey]*combatTotal{}

	s.OnCombatLog(func(entry CombatEntry) {
		if c.afterEnd(entry.At) {
			return
		}
		switch entry.Type {
		case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_DEATH:
			c.onDeath(entry)
		case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_FIRST_BLOOD:
			c.firstBlood, c.firstBloodAt = true, entry.Raw.GetTimestamp()
		case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_MULTIKILL:
			a := c.pendingAnnouncement(entry)
			a.multikill = int(entry.Raw.GetValue())
			c.announcements[entry.Attacker] = a
		case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_KILLSTREAK:
			a := c.pendingAnnouncement(entry)
			a.streak = int(entry.Raw.GetValue())
			c.announcements[entry.Attacker] = a
		case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_BUYBACK:
			if player := int(entry.Raw.GetValue()); player >= 0 && player < playerCount {
				c.buybacks = append(c.buybacks, pendingCombatBuyback{player: player, at: entry.At})
			}
		case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_DAMAGE:
			c.onDamage(entry)
		case dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_HEAL:
			c.onHeal(entry)
		}
	},
		dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_DEATH,
		dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_FIRST_BLOOD,
		dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_MULTIKILL,
		dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_KILLSTREAK,
		dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_BUYBACK,
		dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_DAMAGE,
		dota.DOTA_COMBATLOG_TYPES_DOTA_COMBATLOG_HEAL,
	)
	s.OnSecond(func(int) { c.readBuybackCosts() })
	s.OnEnd(func(int) { c.readBuybackCosts() })
}

// afterEnd reports whether a moment is after the match ended (post-game damage and deaths are not part of it).
func (c *combatCollector) afterEnd(at ServerTime) bool {
	end, ended := c.session.Clock.GameEnd()
	return ended && at.Seconds > end.Seconds
}

// pendingAnnouncement returns the pending multikill/streak of the entry's attacker, started again when it is from an
// earlier moment.
func (c *combatCollector) pendingAnnouncement(entry CombatEntry) killAnnouncement {
	a, ok := c.announcements[entry.Attacker]
	if !ok || a.at != entry.Raw.GetTimestamp() {
		a = killAnnouncement{at: entry.Raw.GetTimestamp()}
	}
	return a
}

func (c *combatCollector) onDeath(entry CombatEntry) {
	raw := entry.Raw
	if !raw.GetIsTargetHero() {
		c.onCreepDeath(entry)
		return
	}
	if raw.GetIsTargetIllusion() || entry.TargetPlayer < 0 {
		return // creeps, buildings, illusions (and a hero the replay cannot tell)
	}
	victim := entry.TargetPlayer
	kill := matchdata.Kill{
		Victim:       victim,
		KillerUnit:   entry.Attacker,
		Reincarnated: raw.GetWillReincarnate(),
	}
	killer := c.attacker(entry).player
	if killer >= 0 {
		kill.Killer = &killer
	}
	if entry.Inflictor != "" {
		inflictor := entry.Inflictor
		kill.Inflictor = &inflictor
	}
	for _, assist := range raw.GetAssistPlayers() {
		player := int(assist)
		if player < 0 || player >= playerCount || player == killer || player == victim || slices.Contains(kill.Assists, player) {
			continue
		}
		kill.Assists = append(kill.Assists, player)
	}
	if hero := c.session.Players.Hero(victim); hero != nil {
		if x, y, ok := position(hero); ok {
			kill.X, kill.Y = int(math.Round(x)), int(math.Round(y))
		}
	}
	if c.firstBlood && c.firstBloodAt == raw.GetTimestamp() {
		kill.FirstBlood, c.firstBlood = true, false
	}
	if a, ok := c.announcements[entry.Attacker]; ok && a.at == raw.GetTimestamp() {
		kill.Multikill, kill.Streak = a.multikill, a.streak
		delete(c.announcements, entry.Attacker)
	}
	c.kills = append(c.kills, pendingCombatKill{kill: kill, at: entry.At})
}

// onCreepDeath records a lane creep or neutral whose last hit was a player's (hero, illusion or summon), at the
// position of that player's hero (the combat log has none).
func (c *combatCollector) onCreepDeath(entry CombatEntry) {
	target, killer := c.target(entry), c.attacker(entry)
	if combatUnitKind(entry.Target, false) == matchdata.KindLaneCreeps {
		c.onLaneCreepDeath(entry, killer)
	}
	if killer.player < 0 || (target.kind != matchdata.KindLaneCreeps && target.kind != matchdata.KindNeutrals) {
		return
	}
	kind := matchdata.CreepNeutral
	if target.kind == matchdata.KindLaneCreeps {
		kind = matchdata.CreepLane
		// Lane creep names say their side (npc_dota_creep_goodguys_melee): the killer's own side is a deny.
		if player, ok := c.session.Players.Get(killer.player); ok &&
			strings.Contains(entry.Target, "goodguys") == (player.Team == matchdata.Radiant) {
			kind = matchdata.CreepDeny
		}
	}
	event := creepKillEvent{at: entry.At, player: killer.player, kind: kind}
	if hero := c.session.Players.Hero(killer.player); hero != nil {
		if x, y, ok := position(hero); ok {
			px, py := int(math.Round(x)), int(math.Round(y))
			event.x, event.y = &px, &py
		}
	}
	c.creepKills = append(c.creepKills, event)
}

func (c *combatCollector) onDamage(entry CombatEntry) {
	source, target := c.attacker(entry), c.target(entry)
	if source.player < 0 && target.player < 0 {
		return // nothing of a player's: creeps vs creeps, towers vs creeps
	}
	amount := int(entry.Raw.GetValue())
	key := combatKey{source: source, target: target, inflictor: entry.Inflictor, damageType: combatDamageType(entry.Raw.GetDamageType())}
	addCombatTotal(c.damage, key, amount)

	if target.kind == matchdata.KindHero {
		c.minute(entry.At, target.player, minuteTaken, amount)
		if source.player >= 0 && c.enemies(source.player, target.player) {
			c.minute(entry.At, source.player, minuteToHeroes, amount)
			c.minute(entry.At, target.player, minuteFromHeroes, amount)
			c.typed = append(c.typed, typedCombatEvent{at: entry.At, source: source.player, target: target.player, amount: amount, damageType: entry.Raw.GetDamageType()})
			c.hits = append(c.hits, heroHitEvent{at: entry.At, attacker: source.player, target: target.player, amount: amount})
		}
	}
}

func (c *combatCollector) onHeal(entry CombatEntry) {
	source, target := c.attacker(entry), c.target(entry)
	if source.player < 0 && target.player < 0 {
		return
	}
	amount := int(entry.Raw.GetValue())
	addCombatTotal(c.healing, combatKey{source: source, target: target, inflictor: entry.Inflictor}, amount)
	if target.kind == matchdata.KindHero {
		c.typed = append(c.typed, typedCombatEvent{at: entry.At, source: source.player, target: target.player, amount: amount, heal: true})
	}
	// Like the scoreboard's healing (m_fHealing): other heroes only, the player's own hero left out.
	if source.player >= 0 && target.kind == matchdata.KindHero && target.player != source.player {
		c.minute(entry.At, source.player, minuteHealed, amount)
	}
}

func addCombatTotal(totals map[combatKey]*combatTotal, key combatKey, amount int) {
	total, ok := totals[key]
	if !ok {
		total = &combatTotal{}
		totals[key] = total
	}
	total.amount += amount
	total.hits++
}

func (c *combatCollector) minute(at ServerTime, player int, field combatMinuteField, amount int) {
	c.minutes = append(c.minutes, combatMinuteEvent{at: at, player: player, field: field, amount: amount})
}

// enemies reports whether two players are on different teams.
func (c *combatCollector) enemies(a, b int) bool {
	pa, okA := c.session.Players.Get(a)
	pb, okB := c.session.Players.Get(b)
	return okA && okB && pa.Team != pb.Team
}

// attacker classifies who dealt the entry's damage, healing or killing blow.
func (c *combatCollector) attacker(entry CombatEntry) combatUnit {
	raw := entry.Raw
	if raw.GetIsAttackerHero() && entry.AttackerPlayer >= 0 {
		if raw.GetIsAttackerIllusion() {
			return combatUnit{entry.AttackerPlayer, matchdata.KindIllusion}
		}
		return combatUnit{entry.AttackerPlayer, matchdata.KindHero}
	}
	return c.unit(entry.Attacker, entry.Source, false)
}

// target classifies who took the entry's damage or healing.
func (c *combatCollector) target(entry CombatEntry) combatUnit {
	raw := entry.Raw
	if raw.GetIsTargetHero() && entry.TargetPlayer >= 0 {
		if raw.GetIsTargetIllusion() {
			return combatUnit{entry.TargetPlayer, matchdata.KindIllusion}
		}
		return combatUnit{entry.TargetPlayer, matchdata.KindHero}
	}
	return c.unit(entry.Target, c.session.Name(raw.GetTargetSourceName()), raw.GetIsTargetBuilding())
}

// unit classifies a unit that is not a player's hero (or illusion) from its combat log name and the name of its
// source (its owner's hero for summons; its own name otherwise).
func (c *combatCollector) unit(name, source string, building bool) combatUnit {
	if source != name && strings.HasPrefix(source, "npc_dota_hero_") {
		if owner, ok := c.session.Players.ByHeroName(source); ok {
			return combatUnit{owner, matchdata.KindSummon}
		}
	}
	return combatUnit{-1, combatUnitKind(name, building)}
}

// unitKind groups a unit that belongs to no player by its combat log name.
func combatUnitKind(name string, building bool) matchdata.UnitKind {
	switch {
	case name == "npc_dota_roshan":
		return matchdata.KindRoshan
	case strings.HasPrefix(name, "npc_dota_miniboss"):
		return matchdata.KindTormentor
	case strings.HasPrefix(name, "npc_dota_neutral_"), strings.Contains(name, "dark_troll_warlord_skeleton"):
		return matchdata.KindNeutrals
	case strings.HasPrefix(name, "npc_dota_creep_goodguys_"), strings.HasPrefix(name, "npc_dota_creep_badguys_"),
		name == "npc_dota_goodguys_siege", name == "npc_dota_badguys_siege",
		strings.HasPrefix(name, "npc_dota_goodguys_siege_"), strings.HasPrefix(name, "npc_dota_badguys_siege_"):
		return matchdata.KindLaneCreeps
	case building, strings.HasPrefix(name, "npc_dota_goodguys_"), strings.HasPrefix(name, "npc_dota_badguys_"),
		name == "dota_fountain", name == "npc_dota_watch_tower":
		return matchdata.KindBuildings
	default:
		return matchdata.KindOtherUnits // wards, couriers, units the replay does not tie to a player
	}
}

// damageType turns the combat log's damage_type (DAMAGE_TYPE_* flags) into the contract's; "" for none (HP removal).
func combatDamageType(value uint32) matchdata.DamageType {
	switch value {
	case 1:
		return matchdata.Physical
	case 2:
		return matchdata.Magical
	case 4:
		return matchdata.Pure
	default:
		return ""
	}
}

// readBuybackCosts reads each player's gold spent on buybacks and gives the rise to the oldest buyback of that
// player still without a cost.
func (c *combatCollector) readBuybackCosts() {
	for player := range playerCount {
		data, prefix := c.session.Players.TeamData(player)
		if data == nil {
			continue
		}
		spent, ok := number(data, prefix+"m_iGoldSpentOnBuybacks")
		if !ok {
			continue
		}
		value := int(spent)
		if c.spentKnown[player] && value > c.spentOnBuybacks[player] {
			for i := range c.buybacks {
				if b := &c.buybacks[i]; b.player == player && b.cost == nil {
					cost := value - c.spentOnBuybacks[player]
					b.cost = &cost
					break
				}
			}
		}
		c.spentOnBuybacks[player], c.spentKnown[player] = value, true
	}
}

func (c *combatCollector) Fill(m *matchdata.Match) error {
	clock := c.session.Clock

	kills := make(matchdata.List[matchdata.Kill], 0, len(c.kills))
	for _, k := range c.kills {
		kill := k.kill
		kill.T = clock.Seconds(k.at)
		kills = append(kills, kill)
	}
	m.Kills = kills

	buybacks := make(matchdata.List[matchdata.Buyback], 0, len(c.buybacks))
	for _, b := range c.buybacks {
		buybacks = append(buybacks, matchdata.Buyback{T: clock.Seconds(b.at), Player: b.player, Cost: b.cost})
	}
	m.Buybacks = buybacks

	m.Combat = matchdata.Combat{
		Damage:          combatRows(c.damage),
		Healing:         combatRows(c.healing),
		PerMinute:       c.perMinute(),
		Buckets:         c.buckets(),
		HeroHits:        c.heroHits(),
		CreepKills:      c.creepKillList(),
		LaneCreepDeaths: c.laneCreepDeathList(),
	}
	return nil
}

// heroHits adds the hero-vs-hero damage up per bucket, attacker and target, in time order (then attacker, target).
func (c *combatCollector) heroHits() matchdata.List[matchdata.HeroHit] {
	clock := c.session.Clock
	type key struct{ start, attacker, target int }
	totals := map[key]int{}
	for _, hit := range c.hits {
		if hit.attacker < 0 || hit.attacker >= playerCount || hit.target < 0 || hit.target >= playerCount {
			continue
		}
		start := int(math.Floor(clock.Game(hit.at)/matchdata.BucketSeconds)) * matchdata.BucketSeconds
		totals[key{start, hit.attacker, hit.target}] += hit.amount
	}
	hits := make(matchdata.List[matchdata.HeroHit], 0, len(totals))
	for k, amount := range totals {
		if amount != 0 {
			hits = append(hits, matchdata.HeroHit{T: k.start, Attacker: k.attacker, Target: k.target, Amount: amount})
		}
	}
	slices.SortFunc(hits, func(a, b matchdata.HeroHit) int {
		return cmp.Or(cmp.Compare(a.T, b.T), cmp.Compare(a.Attacker, b.Attacker), cmp.Compare(a.Target, b.Target))
	})
	return hits
}

// creepKillList is the creep last hits with game-clock times, already in time order (combat log order).
func (c *combatCollector) creepKillList() matchdata.List[matchdata.CreepKill] {
	kills := make(matchdata.List[matchdata.CreepKill], 0, len(c.creepKills))
	for _, kill := range c.creepKills {
		kills = append(kills, matchdata.CreepKill{
			T: c.session.Clock.Seconds(kill.at), Player: kill.player, Kind: kill.kind, X: kill.x, Y: kill.y})
	}
	return kills
}

// buckets is the hero damage of perMinute every matchdata.BucketSeconds, only the buckets with damage.
func (c *combatCollector) buckets() matchdata.List[matchdata.PlayerCombatBuckets] {
	clock := c.session.Clock
	type key struct{ player, start int }
	to, from := map[key]int{}, map[key]int{}
	for _, e := range c.minutes {
		if e.player < 0 || e.player >= playerCount {
			continue
		}
		start := int(math.Floor(clock.Game(e.at)/matchdata.BucketSeconds)) * matchdata.BucketSeconds
		switch e.field {
		case minuteToHeroes:
			to[key{e.player, start}] += e.amount
		case minuteFromHeroes:
			from[key{e.player, start}] += e.amount
		}
	}
	sorted := func(amounts map[key]int, player int) matchdata.List[matchdata.Bucket] {
		var list matchdata.List[matchdata.Bucket]
		for k, amount := range amounts {
			if k.player == player && amount != 0 {
				list = append(list, matchdata.Bucket{T: k.start, Amount: amount})
			}
		}
		slices.SortFunc(list, func(a, b matchdata.Bucket) int { return cmp.Compare(a.T, b.T) })
		return list
	}
	result := make(matchdata.List[matchdata.PlayerCombatBuckets], playerCount)
	for player := range result {
		result[player] = matchdata.PlayerCombatBuckets{Player: player, ToHeroes: sorted(to, player), FromHeroes: sorted(from, player)}
	}
	c.fillTypedBuckets(result)
	return result
}

// perMinute spreads the minute events over the minutes of the game clock (before the horn = minute 0).
func (c *combatCollector) perMinute() matchdata.List[matchdata.PlayerCombatMinutes] {
	clock := c.session.Clock
	length := 1
	if end, ended := clock.GameEnd(); ended {
		length = int(math.Floor(clock.Game(end)/60)) + 1
	}
	for _, e := range c.minutes {
		length = max(length, combatMinute(clock.Game(e.at))+1)
	}
	result := make(matchdata.List[matchdata.PlayerCombatMinutes], playerCount)
	for player := range result {
		result[player] = matchdata.PlayerCombatMinutes{
			Player:           player,
			DamageToHeroes:   make(matchdata.List[int], length),
			DamageFromHeroes: make(matchdata.List[int], length),
			DamageTaken:      make(matchdata.List[int], length),
			Healing:          make(matchdata.List[int], length),
		}
	}
	for _, e := range c.minutes {
		if e.player < 0 || e.player >= playerCount {
			continue
		}
		row, minute := &result[e.player], combatMinute(clock.Game(e.at))
		switch e.field {
		case minuteToHeroes:
			row.DamageToHeroes[minute] += e.amount
		case minuteFromHeroes:
			row.DamageFromHeroes[minute] += e.amount
		case minuteTaken:
			row.DamageTaken[minute] += e.amount
		case minuteHealed:
			row.Healing[minute] += e.amount
		}
	}
	return result
}

// minuteOf is the minute of the game clock a moment falls in (before the horn = 0).
func combatMinute(game float64) int {
	return max(0, int(math.Floor(game/60)))
}

// rows turns the totals into the contract's rows, ordered by source, target, inflictor and damage type (stable output
// for the same replay).
func combatRows(totals map[combatKey]*combatTotal) matchdata.List[matchdata.CombatRow] {
	keys := make([]combatKey, 0, len(totals))
	for key := range totals {
		keys = append(keys, key)
	}
	slices.SortFunc(keys, func(a, b combatKey) int {
		return cmp.Or(
			compareCombatUnits(a.source, b.source),
			compareCombatUnits(a.target, b.target),
			cmp.Compare(a.inflictor, b.inflictor),
			cmp.Compare(a.damageType, b.damageType),
		)
	})
	result := make(matchdata.List[matchdata.CombatRow], 0, len(keys))
	for _, key := range keys {
		total := totals[key]
		row := matchdata.CombatRow{
			SourcePlayer: combatPlayer(key.source.player),
			SourceKind:   key.source.kind,
			TargetPlayer: combatPlayer(key.target.player),
			TargetKind:   key.target.kind,
			Amount:       total.amount,
			Hits:         total.hits,
		}
		if key.inflictor != "" {
			inflictor := key.inflictor
			row.Inflictor = &inflictor
		}
		if key.damageType != "" {
			damageType := key.damageType
			row.DamageType = &damageType
		}
		result = append(result, row)
	}
	return result
}

// compareUnits orders units by player (nobody's units last) and then by kind.
func compareCombatUnits(a, b combatUnit) int {
	playerOrder := func(u combatUnit) int {
		if u.player < 0 {
			return playerCount
		}
		return u.player
	}
	return cmp.Or(cmp.Compare(playerOrder(a), playerOrder(b)), cmp.Compare(a.kind, b.kind))
}

func combatPlayer(player int) *int {
	if player < 0 {
		return nil
	}
	return &player
}

type laneCreepDeathEvent struct {
	at   ServerTime
	name string
	row  matchdata.LaneCreepDeath
}

// onLaneCreepDeath retains all lane deaths, including blows from units without a player owner.
func (c *combatCollector) onLaneCreepDeath(e CombatEntry, killer combatUnit) {
	team := teamOf(int(e.Raw.GetTargetTeam()))
	if strings.Contains(e.Target, "goodguys") {
		team = matchdata.Radiant
	} else if strings.Contains(e.Target, "badguys") {
		team = matchdata.Dire
	}
	row := matchdata.LaneCreepDeath{CreepTeam: team, KillerKind: laneCreepKillerKind(killer, e.Attacker), KillerPlayer: combatPlayer(killer.player)}
	// A log location can identify a lane, but cannot substitute for a sampled entity position.
	if e.Raw.LocationX != nil && e.Raw.LocationY != nil {
		lane := waveLane(float64(e.Raw.GetLocationX()), float64(e.Raw.GetLocationY()))
		if lane != "" {
			row.Lane = &lane
		}
	}
	c.laneDeaths = append(c.laneDeaths, laneCreepDeathEvent{at: e.At, name: e.Target, row: row})
}

// laneCreepKillerKind gives ownership priority, so controlled creeps and denies remain hero blows.
func laneCreepKillerKind(killer combatUnit, name string) string {
	if killer.player >= 0 {
		return "hero"
	}
	switch killer.kind {
	case matchdata.KindLaneCreeps:
		return "creep"
	case matchdata.KindBuildings:
		if (strings.HasPrefix(name, "npc_dota_goodguys_") || strings.HasPrefix(name, "npc_dota_badguys_")) && (strings.Contains(name, "_tower") || strings.Contains(name, "_fort") || strings.Contains(name, "_rax_")) {
			return "tower"
		}
		return "other"
	case matchdata.KindNeutrals:
		return "neutral"
	default:
		return "other"
	}
}

// A small window covers float32 log timestamps and several ticks of entity replication delay.
// Wider or many-to-one matches cannot establish identity and retain null coordinates.
const creepDeathLinkWindow = 0.15

// laneCreepDeathList links only unambiguous same-name/team death transitions within a net-log delay.
// CombatLogNames are unit types, not entity IDs: never choose an arbitrary creep of that type.
func (c *combatCollector) laneCreepDeathList() matchdata.List[matchdata.LaneCreepDeath] {
	result := make(matchdata.List[matchdata.LaneCreepDeath], 0, len(c.laneDeaths))
	matches := make([][]int, len(c.laneDeaths))
	uses := map[int]int{}
	if c.waves != nil {
		for i, event := range c.laneDeaths {
			for j, death := range c.waves.deaths {
				if event.name == death.name && event.row.CreepTeam == death.team && math.Abs(event.at.Seconds-death.at.Seconds) <= creepDeathLinkWindow {
					matches[i] = append(matches[i], j)
					uses[j]++
				}
			}
		}
	}
	for i, event := range c.laneDeaths {
		row := event.row
		row.T = c.session.Clock.Seconds(event.at)
		if len(matches[i]) == 1 && uses[matches[i][0]] == 1 {
			p := c.waves.deaths[matches[i][0]].position
			if p.known {
				row.X, row.Y = &p.x, &p.y
				row.Lane = nil
				if lane := waveLane(float64(p.x), float64(p.y)); lane != "" {
					row.Lane = &lane
				}
			}
		}
		result = append(result, row)
	}
	slices.SortStableFunc(result, func(a, b matchdata.LaneCreepDeath) int { return cmp.Compare(a.T, b.T) })
	return result
}
