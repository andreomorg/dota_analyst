package matchdata

import (
	"encoding/json"
	"testing"
)

func TestWorldExtractionTuples(t *testing.T) {
	yes := true
	one := 1
	for _, tt := range []struct {
		name  string
		value any
		want  string
	}{
		{"courier", CourierSample{T: 2, Alive: 1, X: 100, Y: 200, Flying: &yes}, `[2,1,100,200,true]`},
		{"unknown flight", CourierSample{T: 2, Alive: 0}, `[2,0,0,0,null]`},
		{"typed", DamageTypeBucket{2, 1, 2, 3, 4, 5, 6}, `[2,1,2,3,4,5,6]`},
		{"healing", HealingBucket{2, 30, 40}, `[2,30,40]`},
		{"capture", OutpostCaptureEvent{T: 20.1, ID: 729, Team: Radiant}, `[20.1,729,"radiant"]`},
		{"alive", TormentorSample{T: 1202, Alive: &one}, `[1202,1]`},
		{"unknown", TormentorSample{T: 0}, `[0,null]`},
	} {
		t.Run(tt.name, func(t *testing.T) {
			got, err := json.Marshal(tt.value)
			if err != nil || string(got) != tt.want {
				t.Fatalf("got %s err=%v want %s", got, err, tt.want)
			}
		})
	}
}
