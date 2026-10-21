package matchdata

import (
	"encoding/json"
	"testing"
)

func TestExtractionTupleContracts(t *testing.T) {
	zero := 0
	reason := uint32(0)
	tests := []struct {
		name  string
		value any
		want  string
	}{
		{"ping unknown type", MapPing{T: 1.25, Player: 2, X: -3, Y: 4}, `[1.3,2,-3,4,null]`},
		{"phrase ID only", ChatWheel{T: -1, Player: 0, Phrase: 85001}, `[-1,0,85001]`},
		{"negative gold", EconomyBucket{T: 2, Player: 0, Reason: &reason, Amount: -21}, `[2,0,0,0,-21]`},
		{"missing reason", EconomyBucket{T: 2, Player: 1, Resource: 1}, `[2,1,1,null,0]`},
		{"rune removed", RuneOnMap{T: 4, RuneType: 5, X: 10, Y: -20}, `[4,5,10,-20,0]`},
		{"unknown ability target", AbilityTarget{}, `[null,null,null,null]`},
		{"player zero", AbilityTarget{Player: &zero, X: &zero, Y: &zero, PointSource: &zero}, `[0,0,0,0]`},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got, err := json.Marshal(tt.value)
			if err != nil || string(got) != tt.want {
				t.Fatalf("got %s, %v; want %s", got, err, tt.want)
			}
		})
	}
}

func TestOptionalExtractionFieldsAbsent(t *testing.T) {
	data, err := json.Marshal(New())
	if err != nil {
		t.Fatal(err)
	}
	var doc map[string]json.RawMessage
	if err := json.Unmarshal(data, &doc); err != nil {
		t.Fatal(err)
	}
	for section, fields := range map[string][]string{"combat": {"economy"}, "vision": {"pings", "chatWheel"}, "objectives": {"runesOnMap"}} {
		var value map[string]json.RawMessage
		if err := json.Unmarshal(doc[section], &value); err != nil {
			t.Fatal(err)
		}
		for _, field := range fields {
			if _, ok := value[field]; ok {
				t.Errorf("empty optional %s.%s present", section, field)
			}
		}
	}
}
