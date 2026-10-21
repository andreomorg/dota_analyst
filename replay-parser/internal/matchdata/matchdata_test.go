package matchdata

import (
	"encoding/json"
	"strings"
	"testing"
)

func marshal(t *testing.T, value any) string {
	t.Helper()
	raw, err := json.Marshal(value)
	if err != nil {
		t.Fatal(err)
	}
	return string(raw)
}

// The empty document is the contract's skeleton: required lists are [] (never null),
// including debuffs/buffs used by the C# freshness check. Conditional extensions
// (economy, couriers, orders, typed/healing buckets, etc.) remain omitted without evidence.
func TestEmptyMatchJSON(t *testing.T) {
	want := `{"schemaVersion":1,` +
		`"match":{"matchId":"","gameMode":0,"leagueId":0,"winner":null,"durationSeconds":0,"startedAt":null,` +
		`"endedAt":null,"pausedSeconds":0},` +
		`"draft":{"announcedBans":[],"picksBans":[]},` +
		`"players":[],` +
		`"timelines":{"dayNight":[],"perMinute":[],"positionIntervalSeconds":0,"positions":[],"offLaneCreeps":[],"waves":[]},` +
		`"laning":{"endsAtSeconds":0,"lanes":[]},` +
		`"kills":[],"buybacks":[],` +
		`"items":{"purchases":[],"uses":[],"final":[],"slots":[],"cooldowns":[]},` +
		`"abilities":{"upgrades":[],"uses":[],"cooldowns":[]},` +
		`"combat":{"debuffs":[],"buffs":[],"damage":[],"healing":[],"perMinute":[],"buckets":[],"heroHits":[],"laneCreepDeaths":[],"creepKills":[]},` +
		`"vision":{"wards":[],"smokes":[],"scans":[],"glyphs":[]},` +
		`"objectives":{"buildings":[],"roshan":[],"tormentors":[],"couriers":[],"runes":[],"outposts":[]}}`
	if got := marshal(t, New()); got != want {
		t.Errorf("empty match\n got %s\nwant %s", got, want)
	}
}

func TestSectionsJSON(t *testing.T) {
	player, killer, team := 0, 5, Radiant
	inflictor, blink := "undying_decay", "item_blink"
	health, maxHealth, mana, maxMana := 640, 700, 250, 291
	removed := Seconds(336.54)
	armor, resistance, speed, damageMin, damageMax, strength, agility, intellect := 2.5, 25.0, 315.0, 70.0, 74.0, 31.0, 48.0, 23.5
	tests := []struct {
		name  string
		value any
		want  string
	}{
		{"wave size tuple", WaveSize{T: 60, RadiantInLane: 12, DireInLane: 7, RadiantAtFront: 5, DireAtFront: 4}, `[60,12,7,5,4]`},
		{"wave size with absent team", LaneWaves{Lane: "top", Sizes: List[WaveSize]{{T: 62, RadiantInLane: 4, RadiantAtFront: 4}}},
			`{"lane":"top","fronts":[],"sizes":[[62,4,0,4,0]]}`},
		{"player with empty final numbers",
			Player{Player: 0, SteamID: "76561198059783443", Name: "x", Team: Dire, TeamSlot: 4, HeroID: 31,
				HeroName: "npc_dota_hero_lich", PickOrder: &player},
			`{"player":0,"steamId":"76561198059783443","name":"x","team":"dire","teamSlot":4,"heroId":31,` +
				`"heroName":"npc_dota_hero_lich","rankTier":null,"pickOrder":0,"randomed":false,"role":null,` +
				`"facet":null,"final":{"level":0,` +
				`"kills":0,"deaths":0,"assists":0,"lastHits":0,"denies":0,"missedLastHits":0,"netWorth":0,` +
				`"goldEarned":0,"xp":0,"heroDamage":0,"towerDamage":0,"healing":0,"stunSeconds":0,` +
				`"teamFightParticipation":0,"campsStacked":0,"runePickups":0,"observersPlaced":0,"sentriesPlaced":0,` +
				`"wardsDestroyed":0,"smokesUsed":0,"towerKills":0,"roshanKills":0,"courierKills":0,` +
				`"goldBySource":{"creeps":0,"heroes":0,"buildings":0,"neutrals":0,"roshan":0,"bountyRunes":0,` +
				`"passive":0,"shared":0,"wards":0,"couriers":0,"summons":0,"denies":0,"abilities":0,"comeback":0,` +
				`"other":0},"goldSpent":{"items":0,"consumables":0,"support":0,"buybacks":0},"goldLostToDeath":0,` +
				`"gold":0,"firstBlood":false,"creepsStacked":0,"bountyRunes":0,"powerRunes":0,"waterRunes":0,` +
				`"wardsPurchased":0,"tormentorKills":0,"outpostsCaptured":0,"watchersTaken":0,` +
				`"lotusesTaken":0,"wisdomShrinesTaken":0,"neutralTokensFound":0,"commandsIssued":0}}`},
		{"per-minute columns, nil lists are []",
			PlayerMinutes{Player: 3, Gold: List[int]{0, 290}},
			`{"player":3,"gold":[0,290],"xp":[],"netWorth":[],"lastHits":[],"denies":[],"level":[],"kills":[],` +
				`"deaths":[],"assists":[],"heroDamage":[]}`},
		{"positions as [t, x, y]",
			PlayerPositions{Player: 1, Samples: List[Sample]{{0, -6700, -6700}, {2, -6520, -6480}}},
			`{"player":1,"samples":[[0,-6700,-6700],[2,-6520,-6480]],"vitals":[],"stats":[]}`},
		{"vitals as [health, maxHealth, mana, maxMana, visibleToEnemies, unitState, gold], missing ones null",
			PlayerPositions{Player: 1, Samples: List[Sample]{{0, -6700, -6700}},
				Vitals: List[Vitals]{{Health: &health, MaxHealth: &maxHealth, Mana: &mana, MaxMana: &maxMana, Gold: &maxMana}}},
			`{"player":1,"samples":[[0,-6700,-6700]],"vitals":[[640,700,250,291,null,null,291]],"stats":[]}`},
		{"day/night tuples", List[DayNight]{{T: 0, IsDay: true}, {T: 300, IsDay: false}}, `[[0,true],[300,false]]`},
		{"combat stats tuples, missing properties null", List[CombatStats]{
			{Armor: &armor, MagicResist: &resistance, MoveSpeed: &speed, DamageMin: &damageMin, DamageMax: &damageMax,
				Strength: &strength, Agility: &agility, Intellect: &intellect}, {}},
			`[[2.5,25,315,70,74,31,48,23.5],[null,null,null,null,null,null,null,null]]`},
		{"kill",
			Kill{T: 115.66, Victim: 3, Killer: &killer, KillerUnit: "npc_dota_hero_undying", Inflictor: &inflictor,
				Assists: List[int]{6}, X: -1830, Y: 1500},
			`{"t":115.7,"victim":3,"killer":5,"killerUnit":"npc_dota_hero_undying","inflictor":"undying_decay",` +
				`"assists":[6],"x":-1830,"y":1500,"reincarnated":false,"firstBlood":false,"multikill":0,"streak":0}`},
		{"kill by a tower, no assists",
			Kill{T: -0.04, Victim: 3, KillerUnit: "npc_dota_goodguys_tower1_mid"},
			`{"t":0,"victim":3,"killer":null,"killerUnit":"npc_dota_goodguys_tower1_mid","inflictor":null,` +
				`"assists":[],"x":0,"y":0,"reincarnated":false,"firstBlood":false,"multikill":0,"streak":0}`},
		{"purchases as [t, name], negative before the horn",
			PlayerItems{Player: 0, Items: List[TimedName]{{-89.94, "item_tango"}, {34.1, "item_circlet"}}},
			`{"player":0,"items":[[-89.9,"item_tango"],[34.1,"item_circlet"]]}`},
		{"final items with empty slots",
			FinalItems{Player: 2, Inventory: List[*string]{&blink, nil}, Neutral: &blink},
			`{"player":2,"inventory":["item_blink",null],"backpack":[],"teleport":null,"neutral":"item_blink",` +
				`"enhancement":null}`},
		{"ability upgrades as [t, name, level]",
			PlayerAbilityLevels{Player: 0, Levels: List[AbilityLevel]{{56, "riki_blink_strike", 1}}},
			`{"player":0,"levels":[[56,"riki_blink_strike",1]]}`},
		{"damage row",
			CombatRow{SourcePlayer: &player, SourceKind: KindHero, TargetKind: KindLaneCreeps, Amount: 30210, Hits: 410},
			`{"sourcePlayer":0,"sourceKind":"hero","targetPlayer":null,"targetKind":"laneCreeps","inflictor":null,` +
				`"damageType":null,"amount":30210,"hits":410}`},
		{"ward",
			Ward{Type: Observer, Player: &player, Team: team, X: -307, Y: -976, PlacedAt: -30.3, RemovedAt: &removed},
			`{"type":"observer","player":0,"team":"radiant","x":-307,"y":-976,"placedAt":-30.3,"removedAt":336.5,` +
				`"destroyed":false,"destroyedBy":null,"destroyedByTeam":null}`},
		{"Roshan", RoshanKill{T: 1092.7, Team: Radiant, AegisPlayer: &killer},
			`{"t":1092.7,"team":"radiant","killer":null,"aegisPlayer":5}`},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			if got := marshal(t, test.value); got != test.want {
				t.Errorf("\n got %s\nwant %s", got, test.want)
			}
		})
	}
}

func TestSecondsJSON(t *testing.T) {
	tests := map[Seconds]string{0: "0", 600: "600", 1725.53: "1725.5", -89.96: "-90", -0.04: "0", 0.05: "0.1", 12.25: "12.3"}
	for value, want := range tests {
		if got := marshal(t, value); got != want {
			t.Errorf("Seconds(%v) = %s, want %s", float64(value), got, want)
		}
	}
}

// A nil List inside another list is still [] (the guarantee does not depend on who built the value).
func TestNestedNilListsAreEmpty(t *testing.T) {
	got := marshal(t, List[PlayerItems]{{Player: 1}})
	if strings.Contains(got, "null") || got != `[{"player":1,"items":[]}]` {
		t.Errorf("got %s", got)
	}
}

func TestOrdersJSON(t *testing.T) {
	x, y := -1234, 5678
	if got := marshal(t, List[Orders]{{MoveX: &x, MoveY: &y, Moves: 4, Attacks: 2, Casts: 1}, {}}); got != `[[-1234,5678,4,2,1],[null,null,0,0,0]]` {
		t.Fatalf("unexpected orders tuples: %s", got)
	}
	old := marshal(t, PlayerPositions{Player: 0})
	if strings.Contains(old, `"orders"`) {
		t.Fatalf("unknown orders must be omitted: %s", old)
	}
	withOrders := marshal(t, PlayerPositions{Player: 0, Samples: List[Sample]{{T: 0}}, Orders: List[Orders]{{Moves: 1}}})
	if !strings.Contains(withOrders, `"orders":[[null,null,1,0,0]]`) {
		t.Fatalf("missing orders: %s", withOrders)
	}
}

func TestDebuffJSON(t *testing.T) {
	end := Seconds(12.26)
	source := 0
	for _, tc := range []struct {
		value Debuff
		want  string
	}{
		{Debuff{Start: 10.14, End: &end, Source: &source, Target: 5, Modifier: "modifier_stunned", Kind: "stun"}, `[10.1,12.3,0,5,"modifier_stunned","stun"]`},
		{Debuff{Start: -0.04, Target: 2, Modifier: "modifier_unknown", Kind: "other"}, `[0,null,null,2,"modifier_unknown","other"]`},
	} {
		if got := marshal(t, tc.value); got != tc.want {
			t.Errorf("got %s, want %s", got, tc.want)
		}
	}
	if !strings.Contains(marshal(t, Combat{}), `"debuffs":[]`) {
		t.Fatal("empty debuffs must distinguish none from an older document")
	}
}

func TestBuffsJSON(t *testing.T) {
	if !strings.Contains(marshal(t, Combat{}), `"buffs":[]`) {
		t.Fatal("empty buffs must distinguish none from an older document")
	}
	end := Seconds(13)
	source := 2
	c := Combat{Buffs: List[Debuff]{{Start: 10, End: &end, Source: &source, Target: 2, Modifier: "modifier_teleporting", Kind: "teleport"}}}
	if got := marshal(t, c); !strings.Contains(got, `"buffs":[[10,13,2,2,"modifier_teleporting","teleport"]]`) {
		t.Fatalf("missing buff tuple: %s", got)
	}
}

func TestBuildingHealthJSON(t *testing.T) {
	lane, tier := "middle", 2
	b := BuildingHealth{Building: "npc_dota_goodguys_tower2_mid", Team: Radiant, Kind: "tower", Lane: &lane, Tier: &tier, X: -3456, Y: -1216,
		Health: List[BuildingHealthSample]{{T: 0, Health: 2500, MaxHealth: 2500}, {T: 600, Health: 750, MaxHealth: 2500}, {T: 602, Health: 0, MaxHealth: 2500}}}
	want := `{"building":"npc_dota_goodguys_tower2_mid","team":"radiant","kind":"tower","lane":"middle","tier":2,"x":-3456,"y":-1216,"health":[[0,2500,2500],[600,750,2500],[602,0,2500]]}`
	if got := marshal(t, b); got != want {
		t.Errorf("got %s, want %s", got, want)
	}
	ancient := BuildingHealth{Building: "npc_dota_badguys_fort", Team: Dire, Kind: "ancient"}
	if got := marshal(t, ancient); !strings.Contains(got, `"lane":null,"tier":null,"x":0,"y":0,"health":[]`) {
		t.Errorf("nullable metadata: %s", got)
	}
	m := New()
	if strings.Contains(marshal(t, m), `"buildingHealth"`) {
		t.Error("optional field present in empty document")
	}
	m.Objectives.BuildingHealth = List[BuildingHealth]{b}
	if !strings.Contains(marshal(t, m), `"buildingHealth":[`+want+`]`) {
		t.Error("building health missing from objectives")
	}
}

func TestWaveDetailAndLaneDeathTuplesJSON(t *testing.T) {
	x, y, player := 100, -200, 0
	lane := "top"
	for _, test := range []struct {
		name  string
		value any
		want  string
	}{
		{"strength absent team", WaveStrength{T: 60, RadiantHealth: 2000, RadiantHealthAtFront: 1500, RadiantSiege: 1, RadiantCenter: &x}, "[60,2000,0,1500,0,1,0,100,null]"},
		{"off lane absent team", OffLaneCreeps{T: 60, DireCount: 3, DireMeanX: &x, DireMeanY: &y}, "[60,0,3,null,null,100,-200]"},
		{"hero deny", LaneCreepDeath{T: 60.26, Lane: &lane, CreepTeam: Radiant, KillerKind: "hero", KillerPlayer: &player, X: &x, Y: &y}, "[60.3,\"top\",\"radiant\",\"hero\",0,100,-200]"},
		{"unknown position", LaneCreepDeath{T: 62, CreepTeam: Dire, KillerKind: "tower"}, "[62,null,\"dire\",\"tower\",null,null,null]"},
	} {
		t.Run(test.name, func(t *testing.T) {
			if got := marshal(t, test.value); got != test.want {
				t.Errorf("got %s want %s", got, test.want)
			}
		})
	}
}
