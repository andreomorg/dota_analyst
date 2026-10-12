package laning

import "testing"

func TestZoneOf(t *testing.T) {
	tests := []struct {
		name string
		x, y float64
		want Zone
	}{
		// Bases and fountains (towers and buildings of match 9034320024).
		{"Radiant fountain", -7456, -6938, ZoneBase},
		{"Radiant ancient", -5920, -5352, ZoneBase},
		{"Radiant middle tier 3", -4640, -4144, ZoneBase},
		{"Radiant top barracks", -6336, -3758, ZoneBase},
		{"Radiant bottom barracks", -4280, -6360, ZoneBase},
		{"Dire fountain", 7408, 6848, ZoneBase},
		{"Dire ancient", 5528, 5000, ZoneBase},
		{"Dire middle tier 3", 4272, 3759, ZoneBase},
		{"Dire top barracks", 3898, 5496, ZoneBase},
		{"Dire bottom barracks", 6064, 3376, ZoneBase},

		// Top lane: Radiant's vertical stretch, the corner and Dire's horizontal stretch.
		{"Radiant top tier 3", -6592, -3408, ZoneTop},
		{"Radiant top tier 2", -6501, -872, ZoneTop},
		{"Radiant top tier 1", -6336, 1856, ZoneTop},
		{"top corner", -6200, 5900, ZoneTop},
		{"Dire top tier 1", -5275, 6036, ZoneTop},
		{"Dire top tier 2", -128, 6016, ZoneTop},
		{"Dire top tier 3", 3552, 5776, ZoneTop},
		{"inner edge of the top lane", -5300, 0, ZoneTop},
		{"river crossing the top lane", -6300, 4000, ZoneTop},

		// Bottom lane: Radiant's horizontal stretch, the corner and Dire's vertical stretch.
		{"Radiant bottom tier 3", -3952, -6112, ZoneBottom},
		{"Radiant bottom tier 2", -360, -6256, ZoneBottom},
		{"Radiant bottom tier 1", 4860, -6379, ZoneBottom},
		{"bottom corner", 6300, -6200, ZoneBottom},
		{"Dire bottom tier 1", 6269, -2240, ZoneBottom},
		{"Dire bottom tier 2", 6400, 384, ZoneBottom},
		{"Dire bottom tier 3", 6336, 3032, ZoneBottom},
		{"river crossing the bottom lane", 6300, -4000, ZoneBottom},

		// Middle lane: the diagonal between the bases.
		{"Radiant middle tier 2", -3190, -2926, ZoneMiddle},
		{"Radiant middle tier 1", -1544, -1408, ZoneMiddle},
		{"river crossing the middle lane", -400, -300, ZoneMiddle},
		{"Dire middle tier 1", 524, 652, ZoneMiddle},
		{"Dire middle tier 2", 2496, 2112, ZoneMiddle},
		{"side of the middle lane", 1000, -400, ZoneMiddle},

		// Jungle (and river away from the lanes).
		{"Radiant jungle near the bottom lane", 1500, -3800, ZoneJungle},
		{"Radiant jungle near the top lane", -4100, -400, ZoneJungle},
		{"Dire jungle near the top lane", -1000, 3800, ZoneJungle},
		{"Dire jungle near the bottom lane", 4200, -500, ZoneJungle},
		{"river between top and middle", -3194, 2395, ZoneJungle},
		{"river between middle and bottom (Roshan)", 2831, -2740, ZoneJungle},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			if got := ZoneOf(test.x, test.y); got != test.want {
				t.Errorf("ZoneOf(%v, %v) = %q, want %q", test.x, test.y, got, test.want)
			}
		})
	}
}
