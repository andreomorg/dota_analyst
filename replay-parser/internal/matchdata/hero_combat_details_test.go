package matchdata

import (
	"encoding/json"
	"strings"
	"testing"
)

func TestCombatDetailsKeepsComponentsPrecisionAndUnknowns(t *testing.T) {
	bat, bonus := 1.45, 0.0
	got := marshal(t, CombatDetails{BaseAttackTime: &bat, DamageBonus: &bonus})
	want := `[null,null,null,null,null,0,1.45,null,null,null,null,null]`
	if got != want {
		t.Fatalf("got %s, want %s", got, want)
	}
}

func TestAbsentCombatDetailsDoesNotChangeLegacyPositionContract(t *testing.T) {
	data, err := json.Marshal(PlayerPositions{})
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(string(data), "combatDetails") {
		t.Fatal("unknown extension must be omitted")
	}
}
