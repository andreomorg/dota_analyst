// Package matchdata is the contract of POST /v1/match: everything read from a whole replay, as JSON. It only holds
// types (standard library only, like the laning package): the replay package fills them, httpapi writes them, and the
// C# client reads the same shape. This file is the source of truth for field names and units.
//
// Units, everywhere:
//   - time: Seconds of the game clock (since the horn, pauses left out, negative before the horn); events have one
//     decimal place, samples are whole seconds;
//   - position: map coordinates, whole units, about -8200..8200, Radiant bottom-left;
//   - player: the player index 0-9 (the scoreboard order of CDOTA_PlayerResource; 0-4 are usually Radiant);
//   - heroes: heroId (Valve's numeric id) and the internal name (npc_dota_hero_riki);
//   - items and abilities: internal names (item_blink, riki_smoke_screen); units that are not heroes: the combat log
//     name (npc_dota_roshan) or a UnitKind;
//   - steamId and matchId: text (64-bit numbers; JavaScript would round them).
//
// A null field is something the replay does not tell; an empty list ([], never null) is something that did not
// happen. Adding a field is compatible; renaming, removing or changing the meaning of one is /v2/ (SchemaVersion 2).
package matchdata

// SchemaVersion is the version of this contract, written at the top of every document.
const SchemaVersion = 1

// PositionIntervalSeconds is how often hero positions are sampled (owner's decision after the spike: every 2 s).
const PositionIntervalSeconds = 2

// Match is the whole document. Each section is filled by one collector of the replay package.
type Match struct {
	// SchemaVersion is always SchemaVersion (1).
	SchemaVersion int `json:"schemaVersion"`
	// Match is the general information (id, mode, winner, duration).
	Match Info `json:"match"`
	// Draft is the picks and bans.
	Draft Draft `json:"draft"`
	// Players is the 10 players with their final numbers, ordered by player index.
	Players List[Player] `json:"players"`
	// Timelines is each player's numbers per minute and position every PositionIntervalSeconds.
	Timelines Timelines `json:"timelines"`
	// Kills is every hero death, in time order.
	Kills List[Kill] `json:"kills"`
	// Buybacks is every buyback, in time order.
	Buybacks List[Buyback] `json:"buybacks"`
	// Items is purchases, item uses and the final inventories.
	Items Items `json:"items"`
	// Abilities is ability upgrades and uses.
	Abilities Abilities `json:"abilities"`
	// Combat is damage and healing, aggregated (never event by event).
	Combat Combat `json:"combat"`
	// Vision is wards, smokes, scans and glyphs.
	Vision Vision `json:"vision"`
	// Objectives is buildings, Roshan, Tormentors, couriers, runes and outposts.
	Objectives Objectives `json:"objectives"`
}

// New returns an empty document of the current SchemaVersion. Its lists are nil, which List writes as [].
func New() Match {
	return Match{SchemaVersion: SchemaVersion}
}

// Team is a side of the map.
type Team string

const (
	Radiant Team = "radiant" // bottom-left
	Dire    Team = "dire"    // top-right
)

// ----- match and draft (card 02) -----

// Info is the general information about the match.
type Info struct {
	// MatchID is the match id, as text.
	MatchID string `json:"matchId"`
	// GameMode is Valve's game mode number (22 = All Pick, 23 = Turbo, 2 = Captains Mode...).
	GameMode int `json:"gameMode"`
	// LeagueID is the league of a tournament match; 0 for public matches.
	LeagueID int `json:"leagueId"`
	// Winner is the team that won; null when the replay does not say (it ended before the Ancient fell).
	Winner *Team `json:"winner"`
	// DurationSeconds is the game clock when the match ended (the scoreboard duration, e.g. 1725.5 = 28:45); when the
	// replay ends before the match does, the last second of game clock it has.
	DurationSeconds Seconds `json:"durationSeconds"`
	// StartedAt is when the horn sounded, in Unix seconds (derived from EndedAt and the server clock, so +-1 s); null
	// when EndedAt is unknown.
	StartedAt *int64 `json:"startedAt"`
	// EndedAt is when the match ended, in Unix seconds; null when the replay does not say.
	EndedAt *int64 `json:"endedAt"`
	// PausedSeconds is the time the game spent paused (left out of every game-clock time in this document).
	PausedSeconds Seconds `json:"pausedSeconds"`
}

// Draft is the picks and bans.
type Draft struct {
	// Bans is the heroIds banned, in the order the bans were announced. All Pick does not say who banned.
	Bans List[int] `json:"bans"`
	// PicksBans is the full pick/ban sequence when the replay has it (Captains Mode); [] in All Pick and Turbo, where
	// each player's Player.PickOrder tells the pick order instead.
	PicksBans List[DraftEvent] `json:"picksBans"`
}

// DraftEvent is one step of a Captains Mode draft.
type DraftEvent struct {
	// Order is the position in the draft, from 0.
	Order int `json:"order"`
	// IsPick is true for a pick, false for a ban.
	IsPick bool `json:"isPick"`
	// Team is the team that picked or banned.
	Team Team `json:"team"`
	// HeroID is the hero picked or banned.
	HeroID int `json:"heroId"`
}

// ----- players and timelines (card 03) -----

// Player is one of the 10 players.
type Player struct {
	// Player is the player index 0-9 (the key every other section uses).
	Player int `json:"player"`
	// SteamID is the SteamID64 as text; "0" for bots.
	SteamID string `json:"steamId"`
	// Name is the in-game name the player had in this match.
	Name string `json:"name"`
	// Team is the player's team.
	Team Team `json:"team"`
	// TeamSlot is the place in the team, 0-4.
	TeamSlot int `json:"teamSlot"`
	// HeroID is Valve's hero id.
	HeroID int `json:"heroId"`
	// HeroName is the internal hero name (npc_dota_hero_riki), the one the combat log uses.
	HeroName string `json:"heroName"`
	// RankTier is the medal (tens = medal, units = stars, e.g. 74 = Divine 4); null when hidden or unknown.
	RankTier *int `json:"rankTier"`
	// PickOrder is the order in which the player picked; null when unknown.
	PickOrder *int `json:"pickOrder"`
	// Randomed is true when the hero was randomed.
	Randomed bool `json:"randomed"`
	// Final is the player's numbers at the end of the match.
	Final PlayerFinal `json:"final"`
}

// PlayerFinal is a player's numbers at the end of the match (the scoreboard and the post-game statistics).
type PlayerFinal struct {
	// Level is the hero level.
	Level int `json:"level"`
	// Kills is the hero kills.
	Kills int `json:"kills"`
	// Deaths is the deaths as the scoreboard counts them (a death with Aegis does not count).
	Deaths int `json:"deaths"`
	// Assists is the assists.
	Assists int `json:"assists"`
	// LastHits is the creeps last hit.
	LastHits int `json:"lastHits"`
	// Denies is the creeps denied.
	Denies int `json:"denies"`
	// MissedLastHits is the last hits missed (creeps in range that someone else, or nobody, killed).
	MissedLastHits int `json:"missedLastHits"`
	// NetWorth is gold plus the value of the items.
	NetWorth int `json:"netWorth"`
	// GoldEarned is all the gold earned.
	GoldEarned int `json:"goldEarned"`
	// XP is all the experience earned.
	XP int `json:"xp"`
	// HeroDamage is the damage dealt to heroes.
	HeroDamage int `json:"heroDamage"`
	// TowerDamage is the damage dealt to towers.
	TowerDamage int `json:"towerDamage"`
	// Healing is the healing done to heroes.
	Healing int `json:"healing"`
	// StunSeconds is the total stun duration applied to enemy heroes, in seconds.
	StunSeconds float64 `json:"stunSeconds"`
	// TeamFightParticipation is the share (0-1) of the team's kills the player took part in.
	TeamFightParticipation float64 `json:"teamFightParticipation"`
	// CampsStacked is the neutral camps stacked.
	CampsStacked int `json:"campsStacked"`
	// RunePickups is the runes picked up.
	RunePickups int `json:"runePickups"`
	// ObserversPlaced is the observer wards placed.
	ObserversPlaced int `json:"observersPlaced"`
	// SentriesPlaced is the sentry wards placed.
	SentriesPlaced int `json:"sentriesPlaced"`
	// WardsDestroyed is the enemy wards destroyed.
	WardsDestroyed int `json:"wardsDestroyed"`
	// SmokesUsed is the Smokes of Deceit used.
	SmokesUsed int `json:"smokesUsed"`
	// TowerKills is the towers last hit.
	TowerKills int `json:"towerKills"`
	// RoshanKills is the Roshans last hit.
	RoshanKills int `json:"roshanKills"`
	// CourierKills is the enemy couriers killed.
	CourierKills int `json:"courierKills"`
	// GoldBySource is where the earned gold came from.
	GoldBySource GoldBySource `json:"goldBySource"`
	// GoldSpent is what the gold was spent on.
	GoldSpent GoldSpent `json:"goldSpent"`
	// GoldLostToDeath is the gold lost when dying.
	GoldLostToDeath int `json:"goldLostToDeath"`
}

// GoldBySource splits the earned gold by where it came from.
type GoldBySource struct {
	// Creeps is gold from lane creeps last hit.
	Creeps int `json:"creeps"`
	// Heroes is gold from hero kills.
	Heroes int `json:"heroes"`
	// Buildings is gold from buildings destroyed.
	Buildings int `json:"buildings"`
	// Neutrals is gold from neutral creeps.
	Neutrals int `json:"neutrals"`
	// Roshan is gold from Roshan.
	Roshan int `json:"roshan"`
	// BountyRunes is gold from bounty runes.
	BountyRunes int `json:"bountyRunes"`
	// Passive is the gold every player earns over time.
	Passive int `json:"passive"`
	// Shared is gold shared by teammates' kills (assists, buildings).
	Shared int `json:"shared"`
	// Wards is gold from destroying wards.
	Wards int `json:"wards"`
	// Couriers is gold from killing couriers.
	Couriers int `json:"couriers"`
	// Summons is gold from killing summoned units.
	Summons int `json:"summons"`
}

// GoldSpent splits the gold spent.
type GoldSpent struct {
	// Items is gold spent on items (except consumables and support items).
	Items int `json:"items"`
	// Consumables is gold spent on consumables (tangos, salves, TPs...).
	Consumables int `json:"consumables"`
	// Support is wards, Smoke, Dust and the like.
	Support int `json:"support"`
	// Buybacks is gold spent on buybacks.
	Buybacks int `json:"buybacks"`
}

// Timelines is each player's numbers over time.
type Timelines struct {
	// PerMinute is one entry per player with the numbers at each minute of the game clock.
	PerMinute List[PlayerMinutes] `json:"perMinute"`
	// PositionIntervalSeconds is the time between two position samples (PositionIntervalSeconds).
	PositionIntervalSeconds int `json:"positionIntervalSeconds"`
	// Positions is one entry per player with the hero's positions.
	Positions List[PlayerPositions] `json:"positions"`
}

// PlayerMinutes is one player's numbers per minute, one list per number (columns: about 3x smaller than one object
// per minute). Index i of every list is minute i of the game clock (0 = 0:00), read when the minute turns; the last
// value is the end of the match. All lists have the same length.
type PlayerMinutes struct {
	// Player is the player index 0-9.
	Player int `json:"player"`
	// Gold is the total gold earned so far (not the gold in hand).
	Gold List[int] `json:"gold"`
	// XP is the total experience earned so far.
	XP List[int] `json:"xp"`
	// NetWorth is the net worth.
	NetWorth List[int] `json:"netWorth"`
	// LastHits is the last hits so far.
	LastHits List[int] `json:"lastHits"`
	// Denies is the denies so far.
	Denies List[int] `json:"denies"`
	// Level is the hero level.
	Level List[int] `json:"level"`
	// Kills is the kills so far.
	Kills List[int] `json:"kills"`
	// Deaths is the deaths so far.
	Deaths List[int] `json:"deaths"`
	// Assists is the assists so far.
	Assists List[int] `json:"assists"`
	// HeroDamage is the damage dealt to heroes so far.
	HeroDamage List[int] `json:"heroDamage"`
}

// PlayerPositions is where one player's hero was over time.
type PlayerPositions struct {
	// Player is the player index 0-9.
	Player int `json:"player"`
	// Samples is [t, x, y] every PositionIntervalSeconds while the hero is alive (a dead hero has no sample).
	Samples List[Sample] `json:"samples"`
}

// ----- kills, buybacks and combat (card 04) -----

// Kill is the death of a real hero (illusions are not kills).
type Kill struct {
	// T is when the hero died.
	T Seconds `json:"t"`
	// Victim is the player whose hero died.
	Victim int `json:"victim"`
	// Killer is the player who got the kill (an illusion or summon counts for its owner); null when it was not a
	// player (creeps, towers, Roshan, neutrals) or a suicide the replay does not attribute.
	Killer *int `json:"killer"`
	// KillerUnit is the combat log name of what dealt the killing blow (npc_dota_hero_undying,
	// npc_dota_badguys_tower1_mid, npc_dota_creep_goodguys_melee).
	KillerUnit string `json:"killerUnit"`
	// Inflictor is the ability or item of the killing blow; null for a right-click.
	Inflictor *string `json:"inflictor"`
	// Assists is the players credited with an assist.
	Assists List[int] `json:"assists"`
	// X and Y are where the victim died.
	X int `json:"x"`
	Y int `json:"y"`
	// Reincarnated is true when the hero came back (Aegis, Wraith King): the scoreboard does not count it as a death.
	Reincarnated bool `json:"reincarnated"`
}

// Buyback is a player buying back.
type Buyback struct {
	// T is when the player bought back.
	T Seconds `json:"t"`
	// Player is the player index 0-9.
	Player int `json:"player"`
}

// UnitKind says who dealt or took damage/healing when it is not a player's hero.
type UnitKind string

const (
	KindHero       UnitKind = "hero"     // a player's hero (the player field says which)
	KindIllusion   UnitKind = "illusion" // an illusion (the player field is its owner)
	KindSummon     UnitKind = "summons"  // a summoned unit (the player field is its owner when known)
	KindLaneCreeps UnitKind = "laneCreeps"
	KindNeutrals   UnitKind = "neutrals"
	KindBuildings  UnitKind = "buildings"
	KindRoshan     UnitKind = "roshan"
	KindTormentor  UnitKind = "tormentor"
	KindOtherUnits UnitKind = "otherUnits"
)

// Combat is damage and healing, aggregated over the whole match. Rows without any player's hero (creeps vs creeps,
// towers vs creeps) are left out.
type Combat struct {
	// Damage is the damage dealt, one row per source, target and inflictor.
	Damage List[CombatRow] `json:"damage"`
	// Healing is the healing done, one row per source, target and inflictor.
	Healing List[CombatRow] `json:"healing"`
	// PerMinute is one entry per player with the damage dealt to and taken from heroes in each minute.
	PerMinute List[PlayerCombatMinutes] `json:"perMinute"`
}

// CombatRow is the total of damage (or healing) from one source to one target with one inflictor. Heroes are kept
// individually (the player fields); every other unit is grouped by its kind.
type CombatRow struct {
	// SourcePlayer is the player whose hero (or illusion, or summon) dealt it; null when no player did.
	SourcePlayer *int `json:"sourcePlayer"`
	// SourceKind is what dealt it.
	SourceKind UnitKind `json:"sourceKind"`
	// TargetPlayer is the player whose hero (or illusion, or summon) took it; null when it was not a player's unit.
	TargetPlayer *int `json:"targetPlayer"`
	// TargetKind is what took it.
	TargetKind UnitKind `json:"targetKind"`
	// Inflictor is the ability or item; null for right-clicks.
	Inflictor *string `json:"inflictor"`
	// Amount is the total damage (after reductions) or healing.
	Amount int `json:"amount"`
	// Hits is how many combat log entries were added up.
	Hits int `json:"hits"`
}

// PlayerCombatMinutes is one player's hero-vs-hero damage per minute. Index i is minute i of the game clock (damage
// from i:00 to i:59); all lists have the same length.
type PlayerCombatMinutes struct {
	// Player is the player index 0-9.
	Player int `json:"player"`
	// DamageToHeroes is the damage the player dealt to enemy heroes in each minute.
	DamageToHeroes List[int] `json:"damageToHeroes"`
	// DamageFromHeroes is the damage the player's hero took from enemy heroes in each minute.
	DamageFromHeroes List[int] `json:"damageFromHeroes"`
}

// ----- items and abilities (card 05) -----

// Items is what the players bought, used and ended with.
type Items struct {
	// Purchases is one entry per player with every purchase, including the ones before the horn; recipes appear as
	// items (item_recipe_wraith_band).
	Purchases List[PlayerItems] `json:"purchases"`
	// Uses is one entry per player with every item used (activated).
	Uses List[PlayerItems] `json:"uses"`
	// Final is one entry per player with the items at the end of the match.
	Final List[FinalItems] `json:"final"`
}

// PlayerItems is one player's items with times, as [t, "item_name"] tuples in time order.
type PlayerItems struct {
	// Player is the player index 0-9.
	Player int `json:"player"`
	// Items is [t, "item_name"].
	Items List[TimedName] `json:"items"`
}

// FinalItems is one player's items at the end of the match. Empty slots are null.
type FinalItems struct {
	// Player is the player index 0-9.
	Player int `json:"player"`
	// Inventory is the 6 main slots, in slot order.
	Inventory List[*string] `json:"inventory"`
	// Backpack is the 3 backpack slots.
	Backpack List[*string] `json:"backpack"`
	// Teleport is the teleport slot.
	Teleport *string `json:"teleport"`
	// Neutral is the neutral item slot.
	Neutral *string `json:"neutral"`
	// Enhancement is the neutral enhancement slot.
	Enhancement *string `json:"enhancement"`
}

// Abilities is ability upgrades and uses.
type Abilities struct {
	// Upgrades is one entry per player with every point spent, in time order.
	Upgrades List[PlayerAbilityLevels] `json:"upgrades"`
	// Uses is one entry per player with every ability cast, in time order.
	Uses List[PlayerAbilities] `json:"uses"`
}

// PlayerAbilityLevels is one player's ability upgrades as [t, "ability", level] tuples.
type PlayerAbilityLevels struct {
	// Player is the player index 0-9.
	Player int `json:"player"`
	// Levels is [t, "ability_name", level].
	Levels List[AbilityLevel] `json:"levels"`
}

// PlayerAbilities is one player's ability casts as [t, "ability"] tuples.
type PlayerAbilities struct {
	// Player is the player index 0-9.
	Player int `json:"player"`
	// Abilities is [t, "ability_name"].
	Abilities List[TimedName] `json:"abilities"`
}

// ----- vision and objectives (card 06) -----

// Vision is wards and the other vision tools.
type Vision struct {
	// Wards is every observer and sentry ward placed, in the order they were placed.
	Wards List[Ward] `json:"wards"`
	// Smokes is every Smoke of Deceit used.
	Smokes List[Smoke] `json:"smokes"`
	// Scans is every scan used.
	Scans List[TeamEvent] `json:"scans"`
	// Glyphs is every Glyph of Fortification used.
	Glyphs List[TeamEvent] `json:"glyphs"`
}

// WardType is the kind of ward.
type WardType string

const (
	Observer WardType = "observer"
	Sentry   WardType = "sentry"
)

// Ward is a ward from the moment it was placed until it was gone.
type Ward struct {
	// Type is observer or sentry.
	Type WardType `json:"type"`
	// Player is who placed it; null when the replay does not say.
	Player *int `json:"player"`
	// Team is the team that owns it.
	Team Team `json:"team"`
	// X and Y are where it was placed.
	X int `json:"x"`
	Y int `json:"y"`
	// PlacedAt is when it was placed.
	PlacedAt Seconds `json:"placedAt"`
	// RemovedAt is when it was gone (expired or destroyed); null when it still existed when the match ended.
	RemovedAt *Seconds `json:"removedAt"`
	// Destroyed is true when a unit killed it; false when it expired (or still existed when the match ended).
	Destroyed bool `json:"destroyed"`
	// DestroyedBy is the player who destroyed it (a summon or illusion counts for its owner); null when it expired or
	// a non-player unit (creep, tower) killed it.
	DestroyedBy *int `json:"destroyedBy"`
	// DestroyedByTeam is the team of the unit that killed it (the ward's own team for a deny); null when it was not
	// destroyed or a neutral killed it.
	DestroyedByTeam *Team `json:"destroyedByTeam"`
}

// Smoke is a Smoke of Deceit used.
type Smoke struct {
	// T is when it was used.
	T Seconds `json:"t"`
	// Player is the player index 0-9 who used it.
	Player int `json:"player"`
	// X and Y are where the player's hero was at that moment; null when the replay does not say.
	X *int `json:"x"`
	Y *int `json:"y"`
}

// TeamEvent is something a team did at a moment (scan, glyph).
type TeamEvent struct {
	// T is when it happened.
	T Seconds `json:"t"`
	// Team is the team that did it.
	Team Team `json:"team"`
}

// Objectives is the map objectives taken.
type Objectives struct {
	// Buildings is every tower, barracks, filler and Ancient destroyed or denied, in time order.
	Buildings List[BuildingKill] `json:"buildings"`
	// Roshan is every Roshan kill.
	Roshan List[RoshanKill] `json:"roshan"`
	// Tormentors is every Tormentor kill.
	Tormentors List[TormentorKill] `json:"tormentors"`
	// Couriers is every courier kill.
	Couriers List[CourierKill] `json:"couriers"`
	// Runes is every rune picked up.
	Runes List[RunePickup] `json:"runes"`
	// Outposts is every outpost capture.
	Outposts List[OutpostCapture] `json:"outposts"`
}

// BuildingKill is a building destroyed or denied.
type BuildingKill struct {
	// T is when it fell.
	T Seconds `json:"t"`
	// Building is the combat log name (npc_dota_badguys_tower1_mid, npc_dota_goodguys_range_rax_bot).
	Building string `json:"building"`
	// Team is the team that owned the building.
	Team Team `json:"team"`
	// Killer is the player who got the last hit; null when it was not a player (creeps).
	Killer *int `json:"killer"`
	// KillerUnit is the combat log name of what dealt the last hit.
	KillerUnit string `json:"killerUnit"`
	// Denied is true when the owning team destroyed it.
	Denied bool `json:"denied"`
}

// RoshanKill is Roshan dying.
type RoshanKill struct {
	// T is when Roshan died.
	T Seconds `json:"t"`
	// Team is the team that killed Roshan.
	Team Team `json:"team"`
	// Killer is the player who got the last hit; null when it was not a player.
	Killer *int `json:"killer"`
	// AegisPlayer is who picked up the Aegis; null when nobody did (or the replay does not say).
	AegisPlayer *int `json:"aegisPlayer"`
}

// TormentorKill is a Tormentor dying.
type TormentorKill struct {
	// T is when the Tormentor died.
	T Seconds `json:"t"`
	// Team is the team that killed it.
	Team Team `json:"team"`
	// Killer is the player who got the last hit; null when it was not a player.
	Killer *int `json:"killer"`
}

// CourierKill is a courier dying.
type CourierKill struct {
	// T is when the courier died.
	T Seconds `json:"t"`
	// Victim is the player who owned the courier.
	Victim int `json:"victim"`
	// Killer is the player who killed it; null when it was not a player.
	Killer *int `json:"killer"`
}

// RunePickup is a rune taken (or bottled).
type RunePickup struct {
	// T is when it was picked up.
	T Seconds `json:"t"`
	// Player is the player index 0-9.
	Player int `json:"player"`
	// RuneType is the game's rune number (the names are documented with the vision/objectives collector).
	RuneType int `json:"runeType"`
}

// OutpostCapture is an outpost changing hands.
type OutpostCapture struct {
	// T is when it was captured.
	T Seconds `json:"t"`
	// Team is the team that captured it.
	Team Team `json:"team"`
	// Player is who captured it; null when the replay does not say.
	Player *int `json:"player"`
}
