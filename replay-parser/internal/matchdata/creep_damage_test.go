package matchdata

import (
	"encoding/json"
	"testing"
)

func TestCreepHeroDamageJSON(t *testing.T) {
	b, err := json.Marshal(CreepHeroDamage{T: 26, Lane: "middle", CreepTeam: Radiant, TargetPlayer: 7, Hits: 1})
	if err != nil || string(b) != `[26,"middle","radiant",7,1]` {
		t.Fatalf("%s %v", b, err)
	}
}
