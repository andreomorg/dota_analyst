package matchdata

import (
	"encoding/json"
	"testing"
)

func TestCampCountJSON(t *testing.T) {
	for _, row := range []CampCount{{60, 3}, {120, 6}, {130, 0}} {
		data, err := json.Marshal(row)
		if err != nil {
			t.Fatal(err)
		}
		var values []int
		if err = json.Unmarshal(data, &values); err != nil || len(values) != 2 || values[0] != row.T || values[1] != row.Alive {
			t.Fatalf("bad count tuple %s", data)
		}
	}
	data, err := json.Marshal(NeutralCamp{ID: "camp:1:2", X: 1, Y: 2})
	if err != nil {
		t.Fatal(err)
	}
	if string(data) != `{"id":"camp:1:2","x":1,"y":2,"type":null,"spawnerType":null,"alive":[]}` {
		t.Fatal(string(data))
	}
}
