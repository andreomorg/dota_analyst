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

// The empty document is the contract's skeleton: every section and field name, every list [] (never null), and null
// only where the replay may not tell.
func TestEmptyMatchJSON(t *testing.T) {
	want := `{"schemaVersion":1,` +
		`"match":{"matchId":"","gameMode":0,"leagueId":0,"winner":null,"durationSeconds":0,"startedAt":null,` +
		`"endedAt":null,"pausedSeconds":0},` +
		`"draft":{"bans":[],"picksBans":[]},` +
		`"players":[],` +
		`"timelines":{"perMinute":[],"positionIntervalSeconds":0,"positions":[]},` +
		`"kills":[],"buybacks":[],` +
		`"items":{"purchases":[],"uses":[],"final":[]},` +
		`"abilities":{"upgrades":[],"uses":[]},` +
		`"combat":{"damage":[],"healing":[],"perMinute":[]},` +
		`"vision":{"wards":[],"smokes":[],"scans":[],"glyphs":[]},` +
		`"objectives":{"buildings":[],"roshan":[],"tormentors":[],"couriers":[],"runes":[],"outposts":[]}}`
	if got := marshal(t, New()); got != want {
		t.Errorf("empty match\n got %s\nwant %s", got, want)
	}
}

func TestSectionsJSON(t *testing.T) {
	player, killer, team := 0, 5, Radiant
	inflictor, blink := "undying_decay", "item_blink"
	removed := Seconds(336.54)
	tests := []struct {
		name  string
		value any
		want  string
	}{
		{"player with empty final numbers",
			Player{Player: 0, SteamID: "76561198059783443", Name: "x", Team: Dire, TeamSlot: 4, HeroID: 31,
				HeroName: "npc_dota_hero_lich", PickOrder: &player},
			`{"player":0,"steamId":"76561198059783443","name":"x","team":"dire","teamSlot":4,"heroId":31,` +
				`"heroName":"npc_dota_hero_lich","rankTier":null,"pickOrder":0,"randomed":false,"final":{"level":0,` +
				`"kills":0,"deaths":0,"assists":0,"lastHits":0,"denies":0,"missedLastHits":0,"netWorth":0,` +
				`"goldEarned":0,"xp":0,"heroDamage":0,"towerDamage":0,"healing":0,"stunSeconds":0,` +
				`"teamFightParticipation":0,"campsStacked":0,"runePickups":0,"observersPlaced":0,"sentriesPlaced":0,` +
				`"wardsDestroyed":0,"smokesUsed":0,"towerKills":0,"roshanKills":0,"courierKills":0,` +
				`"goldBySource":{"creeps":0,"heroes":0,"buildings":0,"neutrals":0,"roshan":0,"bountyRunes":0,` +
				`"passive":0,"shared":0,"wards":0,"couriers":0,"summons":0},` +
				`"goldSpent":{"items":0,"consumables":0,"support":0,"buybacks":0},"goldLostToDeath":0}}`},
		{"per-minute columns, nil lists are []",
			PlayerMinutes{Player: 3, Gold: List[int]{0, 290}},
			`{"player":3,"gold":[0,290],"xp":[],"netWorth":[],"lastHits":[],"denies":[],"level":[],"kills":[],` +
				`"deaths":[],"assists":[],"heroDamage":[]}`},
		{"positions as [t, x, y]",
			PlayerPositions{Player: 1, Samples: List[Sample]{{0, -6700, -6700}, {2, -6520, -6480}}},
			`{"player":1,"samples":[[0,-6700,-6700],[2,-6520,-6480]]}`},
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
				`"destroyedBy":null}`},
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
