package replay

import (
	"context"
	"encoding/json"
	"math"
	"os"
	"strings"
	"testing"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
)

// analyzeItems reads a real replay with only the items collector.
func analyzeItems(t *testing.T, replay realReplay) (matchdata.Match, *itemsCollector) {
	t.Helper()
	r := replay.open(t)
	collector := &itemsCollector{}
	match, err := analyzeMatch(context.Background(), r, []Collector{collector})
	if err != nil {
		t.Fatal(err)
	}
	return match, collector
}

// checkSlotsAndCooldowns checks the inventory changes and the cooldowns of one player: tracked slots only, in time order,
// cooldowns that end after they start; every hero used abilities, so it has ability cooldowns.
func checkSlotsAndCooldowns(t *testing.T, i int, items matchdata.Items, abilities matchdata.Abilities) {
	t.Helper()
	if len(items.Slots) != playerCount || len(items.Cooldowns) != playerCount || len(abilities.Cooldowns) != playerCount {
		t.Fatalf("want %d slot/cooldown entries: %d, %d, %d", playerCount, len(items.Slots), len(items.Cooldowns),
			len(abilities.Cooldowns))
	}
	tracked := map[int]bool{}
	for _, slot := range trackedSlots {
		tracked[slot] = true
	}
	changes := items.Slots[i].Changes
	for j, change := range changes {
		if !tracked[change.Slot] || j > 0 && change.T < changes[j-1].T || change.Name != nil && !strings.HasPrefix(*change.Name, "item_") {
			t.Errorf("player %d: bad slot change %+v", i, change)
			break
		}
	}
	if len(changes) == 0 {
		t.Errorf("player %d: no inventory changes", i)
	}
	for _, list := range []matchdata.List[matchdata.Cooldown]{items.Cooldowns[i].Cooldowns, abilities.Cooldowns[i].Cooldowns} {
		for j, cooldown := range list {
			if cooldown.ReadyAt <= cooldown.T || j > 0 && cooldown.T < list[j-1].T || cooldown.Name == "" {
				t.Errorf("player %d: bad cooldown %+v", i, cooldown)
				break
			}
		}
	}
	if len(abilities.Cooldowns[i].Cooldowns) == 0 {
		t.Errorf("player %d: no ability cooldowns", i)
	}
}

// checkItemsShape checks what must hold for any replay: one entry per player in order, lists in time order, final
// slots of the right size, ability levels that grow one at a time.
func checkItemsShape(t *testing.T, match matchdata.Match) {
	t.Helper()
	items, abilities := match.Items, match.Abilities
	if len(items.Purchases) != playerCount || len(items.Uses) != playerCount || len(items.Final) != playerCount ||
		len(abilities.Upgrades) != playerCount || len(abilities.Uses) != playerCount {
		t.Fatalf("want %d entries per list: %d purchases, %d uses, %d final, %d upgrades, %d ability uses", playerCount,
			len(items.Purchases), len(items.Uses), len(items.Final), len(abilities.Upgrades), len(abilities.Uses))
	}
	inOrder := func(what string, player int, list []matchdata.TimedName) {
		for i := 1; i < len(list); i++ {
			if list[i].T < list[i-1].T {
				t.Errorf("player %d %s out of order: %v after %v", player, what, list[i], list[i-1])
				return
			}
		}
	}
	for i := range playerCount {
		if items.Purchases[i].Player != i || items.Uses[i].Player != i || items.Final[i].Player != i ||
			abilities.Upgrades[i].Player != i || abilities.Uses[i].Player != i {
			t.Errorf("entry %d is not player %d", i, i)
		}
		inOrder("purchases", i, items.Purchases[i].Items)
		inOrder("item uses", i, items.Uses[i].Items)
		inOrder("ability uses", i, abilities.Uses[i].Abilities)
		if len(items.Purchases[i].Items) == 0 || len(abilities.Uses[i].Abilities) == 0 {
			t.Errorf("player %d: %d purchases, %d ability uses", i, len(items.Purchases[i].Items),
				len(abilities.Uses[i].Abilities))
		}
		for _, purchase := range items.Purchases[i].Items {
			if !strings.HasPrefix(purchase.Name, "item_") || purchase.Name == wardDispenser {
				t.Errorf("player %d bought %q", i, purchase.Name)
			}
		}
		checkSlotsAndCooldowns(t, i, items, abilities)
		final := items.Final[i]
		inventory := 0
		for _, slot := range final.Inventory {
			if slot != nil {
				inventory++
			}
		}
		if len(final.Inventory) != inventorySlots || len(final.Backpack) != backpackSlots || inventory < 2 {
			t.Errorf("player %d final items %+v", i, final)
		}

		upgrades := abilities.Upgrades[i].Levels
		if len(upgrades) < 10 || upgrades[0].T > 60 {
			t.Errorf("player %d: %d upgrades, the first at %.1f", i, len(upgrades), upgrades[0].T)
		}
		levels := map[string]int{}
		for j, upgrade := range upgrades {
			if j > 0 && upgrade.T < upgrades[j-1].T {
				t.Errorf("player %d upgrades out of order at %v", i, upgrade)
			}
			if levels[upgrade.Name]++; levels[upgrade.Name] != upgrade.Level {
				t.Errorf("player %d: %s goes to level %d after %d upgrades", i, upgrade.Name, upgrade.Level,
					levels[upgrade.Name])
			}
		}
	}
}

func TestItemsCollectorAllPick(t *testing.T) {
	match, _ := analyzeItems(t, allPickReplay)
	checkItemsShape(t, match)
	// The first purchases (around -1:30) are before the combat log has any: they come from the item entities.
	early := 0
	for _, player := range match.Items.Purchases {
		if len(player.Items) > 0 && player.Items[0].T < -60 {
			early++
		}
	}
	if early < 8 {
		t.Errorf("only %d players have purchases before -1:00", early)
	}
	logSizes(t, match)
}

// openDotaMatch is the part of OpenDota's parse of match 9041133074 the test compares with
// (testdata/opendota-9041133074-items.json, trimmed from GET https://api.opendota.com/api/matches/9041133074).
type openDotaMatch struct {
	Duration float64 `json:"duration"`
	Players  []struct {
		HeroID      int `json:"hero_id"`
		PurchaseLog []struct {
			Time float64 `json:"time"`
			Key  string  `json:"key"`
		} `json:"purchase_log"`
		ItemUses        map[string]int `json:"item_uses"`
		AbilityUpgrades []int          `json:"ability_upgrades_arr"`
		Item0           int            `json:"item_0"`
		Item1           int            `json:"item_1"`
		Item2           int            `json:"item_2"`
		Item3           int            `json:"item_3"`
		Item4           int            `json:"item_4"`
		Item5           int            `json:"item_5"`
		Backpack0       int            `json:"backpack_0"`
		Backpack1       int            `json:"backpack_1"`
		Backpack2       int            `json:"backpack_2"`
		Neutral         int            `json:"item_neutral"`
		Enhancement     int            `json:"item_neutral2"`
	} `json:"players"`
}

// itemIDs are Valve's ids of the items in the final inventories of match 9041133074 (OpenDota gives ids there).
var itemIDs = map[int]string{
	1: "blink", 21: "ogre_axe", 29: "boots", 34: "magic_stick", 40: "dust", 48: "travel_boots", 63: "power_treads",
	65: "hand_of_midas", 96: "sheepstick", 102: "force_staff", 108: "ultimate_scepter", 110: "refresher",
	114: "heart", 116: "black_king_bar", 119: "shivas_guard", 127: "blade_mail", 141: "greater_crit",
	154: "sange_and_yasha", 174: "diffusal_blade", 176: "ethereal_blade", 180: "arcane_boots", 188: "smoke_of_deceit",
	204: "dagon_5", 214: "tranquil_boots", 218: "ward_dispenser", 226: "lotus_orb", 231: "guardian_greaves",
	235: "octarine_core", 236: "dragon_lance", 242: "crimson_guard", 247: "moon_shard", 249: "silver_edge",
	250: "bloodthorn", 254: "glimmer_cape", 256: "aeon_disk", 265: "infused_raindrop", 273: "kaya_and_sange",
	610: "wind_waker", 939: "harpoon", 1107: "phylactery", 1466: "gungir", 1854: "consecrated_wraps",
	4204: "famango", 4205: "great_famango", 4206: "greater_famango",
}

// purchaseTolerance is how far a purchase time may be from OpenDota's: OpenDota writes whole seconds (rounded
// down), the replay has tenths, and the purchases before the combat log come from the item entities.
const purchaseTolerance = 2.0

func TestItemsCollectorMatchesOpenDota(t *testing.T) {
	raw, err := os.ReadFile("testdata/opendota-9041133074-items.json")
	if err != nil {
		t.Fatal(err)
	}
	var od openDotaMatch
	if err := json.Unmarshal(raw, &od); err != nil {
		t.Fatal(err)
	}
	match, collector := analyzeItems(t, turboReplay)
	checkItemsShape(t, match)

	// The ids of neutral items and ability upgrades are not in the replay: the same id must always be the same name.
	neutrals, abilityNames := bijection{}, bijection{}
	for _, player := range collector.session.Players.All() {
		i := player.Index
		var o = -1
		for j, candidate := range od.Players {
			if candidate.HeroID == player.HeroID {
				o = j
			}
		}
		if o < 0 {
			t.Fatalf("hero %d not in the OpenDota data", player.HeroID)
		}
		want := od.Players[o]

		// Purchases: every OpenDota purchase of the match is ours too, within the tolerance; ours has the recipes
		// on top (OpenDota leaves them out).
		ours := match.Items.Purchases[i].Items
		used := make([]bool, len(ours))
		for _, purchase := range want.PurchaseLog {
			if purchase.Time > od.Duration {
				continue // after the Ancient fell
			}
			found := -1
			for j, our := range ours {
				if !used[j] && our.Name == "item_"+purchase.Key &&
					math.Abs(float64(our.T)-purchase.Time) <= purchaseTolerance &&
					(found < 0 || math.Abs(float64(our.T)-purchase.Time) < math.Abs(float64(ours[found].T)-purchase.Time)) {
					found = j
				}
			}
			if found < 0 {
				t.Errorf("player %d (%s): OpenDota purchase %s at %.0f not found", i, player.HeroName, purchase.Key,
					purchase.Time)
				continue
			}
			used[found] = true
		}
		extra := 0
		for j, our := range ours {
			if !used[j] && !strings.HasPrefix(our.Name, "item_recipe_") {
				extra++
				t.Logf("player %d (%s): purchase not in OpenDota: %v", i, player.HeroName, our)
			}
		}
		// The one known extra: a sentry bought at -1:00, combined with the free observer into a ward dispenser.
		if extra > 1 {
			t.Errorf("player %d (%s): %d purchases that OpenDota does not have", i, player.HeroName, extra)
		}

		// Item uses: the same count per item.
		uses := map[string]int{}
		for _, use := range match.Items.Uses[i].Items {
			uses[strings.TrimPrefix(use.Name, "item_")]++
		}
		for name, count := range want.ItemUses {
			if uses[name] != count {
				t.Errorf("player %d (%s): %d uses of %s, OpenDota %d", i, player.HeroName, uses[name], name, count)
			}
		}
		if len(uses) != len(want.ItemUses) {
			t.Errorf("player %d (%s): item uses %v, OpenDota %v", i, player.HeroName, uses, want.ItemUses)
		}

		// Final items, slot by slot.
		final := match.Items.Final[i]
		slots := append(append([]*string{}, final.Inventory...), final.Backpack...)
		wantIDs := []int{want.Item0, want.Item1, want.Item2, want.Item3, want.Item4, want.Item5, want.Backpack0,
			want.Backpack1, want.Backpack2}
		for slot, id := range wantIDs {
			got := ""
			if slots[slot] != nil {
				got = strings.TrimPrefix(*slots[slot], "item_")
			}
			if wantName := itemIDs[id]; got != wantName {
				t.Errorf("player %d (%s) slot %d: %q, OpenDota %d (%q)", i, player.HeroName, slot, got, id, wantName)
			}
		}
		if final.Neutral == nil || final.Enhancement == nil || final.Teleport == nil {
			t.Errorf("player %d (%s): neutral %v, enhancement %v, teleport %v", i, player.HeroName, final.Neutral,
				final.Enhancement, final.Teleport)
		} else {
			neutrals.check(t, want.Neutral, *final.Neutral)
			neutrals.check(t, want.Enhancement, *final.Enhancement)
		}

		// Ability upgrades: the same number, in the same order.
		upgrades := match.Abilities.Upgrades[i].Levels
		if len(upgrades) != len(want.AbilityUpgrades) {
			t.Errorf("player %d (%s): %d upgrades, OpenDota %d", i, player.HeroName, len(upgrades),
				len(want.AbilityUpgrades))
			continue
		}
		for j, upgrade := range upgrades {
			abilityNames.check(t, want.AbilityUpgrades[j], upgrade.Name)
		}
	}
	logSizes(t, match)
}

// bijection checks that OpenDota's ids and our names correspond one to one.
type bijection struct {
	names map[int]string
	ids   map[string]int
}

func (b *bijection) check(t *testing.T, id int, name string) {
	t.Helper()
	if b.names == nil {
		b.names, b.ids = map[int]string{}, map[string]int{}
	}
	if known, ok := b.names[id]; ok && known != name {
		t.Errorf("id %d is %s and %s", id, known, name)
	}
	if known, ok := b.ids[name]; ok && known != id {
		t.Errorf("%s is id %d and %d", name, known, id)
	}
	b.names[id], b.ids[name] = name, id
}

func logSizes(t *testing.T, match matchdata.Match) {
	t.Helper()
	items, err := json.Marshal(match.Items)
	if err != nil {
		t.Fatal(err)
	}
	abilities, err := json.Marshal(match.Abilities)
	if err != nil {
		t.Fatal(err)
	}
	t.Logf("JSON: items %d bytes, abilities %d bytes", len(items), len(abilities))
}
