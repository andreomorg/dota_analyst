package matchdata

import (
	"encoding/json"
	"testing"
)

func TestWaveCompositionJSON(t *testing.T) {
	b, err := json.Marshal(WaveComposition{T: 60, Radiant: [7]int{1, 2, 3, 4, 5, 6, 7}, Dire: [7]int{8, 9, 10, 11, 12, 13, 14}})
	if err != nil || string(b) != "[60,1,2,3,4,5,6,7,8,9,10,11,12,13,14]" {
		t.Fatalf("%s %v", b, err)
	}
	b, err = json.Marshal(WaveComposition{T: 62})
	if err != nil || string(b) != "[62,0,0,0,0,0,0,0,0,0,0,0,0,0,0]" {
		t.Fatalf("%s %v", b, err)
	}
}
