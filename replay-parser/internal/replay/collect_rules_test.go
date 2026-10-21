package replay

import "testing"

func TestCooldownRose(t *testing.T) {
	for _, tc := range []struct {
		name              string
		before, remaining float64
		want              bool
	}{
		{"first sight ready", 0, 0, false},
		{"new cooldown", 0, 24, true},
		{"countdown", 24, 23, false},
		{"float noise", 23, 23.3, false},
		{"restarted cooldown", 3, 80, true},
		{"exact margin", 23, 23.5, false},
		{"above margin", 23, 23.5001, true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			if got := cooldownRose(tc.before, tc.remaining); got != tc.want {
				t.Errorf("cooldownRose(%v, %v) = %v; want %v", tc.before, tc.remaining, got, tc.want)
			}
		})
	}
}

func TestVisibleBit(t *testing.T) {
	for _, tc := range []struct {
		name             string
		index, wordIndex int
		word             uint64
		available        bool
		want             int
	}{
		{"first bit", 0, 0, 1, true, 1},
		{"last bit", 63, 0, uint64(1) << 63, true, 1},
		{"next word", 64, 1, 1, true, 1},
		{"replay entity", 461, 7, 8192, true, 1},
		{"high bit in later word", 511, 7, uint64(1)<<63 | 1, true, 1},
		{"low bit above float precision", 448, 7, uint64(1)<<63 | 1, true, 1},
		{"neighbor bit only", 461, 7, 4096, true, 0},
		{"empty word", 64, 1, 0, true, 0},
		{"missing word", 461, 7, 0, false, 0},
	} {
		t.Run(tc.name, func(t *testing.T) {
			got := visibleBit(func(word int) (uint64, bool) {
				if word != tc.wordIndex {
					t.Errorf("requested word %d; want %d", word, tc.wordIndex)
				}
				return tc.word, tc.available
			}, tc.index)
			if !tc.available {
				if got != nil {
					t.Errorf("missing word returned %d; want nil", *got)
				}
				return
			}
			if got == nil {
				t.Fatal("got nil for an available word")
			}
			if *got != tc.want {
				t.Errorf("visibleBit = %d; want %d", *got, tc.want)
			}
		})
	}
}

func TestLowMaskValue(t *testing.T) {
	for _, tc := range []struct {
		name  string
		value any
		want  uint32
	}{
		{"uint64 above float precision", uint64(1)<<53 | 0x12345679, 0x12345679},
		{"uint64 highest bit", uint64(1)<<63 | 0xFFFFFFFF, 0xFFFFFFFF},
		{"int64 above float precision", int64(1)<<53 | 0x12345679, 0x12345679},
		{"negative int64", int64(-2), 0xFFFFFFFE},
		{"uint32", uint32(0xFEDCBA98), 0xFEDCBA98},
		{"int32", int32(123), 123},
		{"negative int32", int32(-2), 0xFFFFFFFE},
		{"zero", uint64(0), 0},
	} {
		t.Run(tc.name, func(t *testing.T) {
			got := lowMaskValue(tc.value)
			if got == nil {
				t.Fatal("got nil for a supported integer")
			}
			if *got != int(tc.want) {
				t.Errorf("lowMaskValue(%v) = %d; want %d", tc.value, *got, tc.want)
			}
		})
	}
	for _, tc := range []struct {
		name  string
		value any
	}{
		{"nil", nil},
		{"float64", float64(1)},
		{"float32", float32(1)},
		{"int", int(1)},
		{"uint", uint(1)},
		{"string", "1"},
		{"bool", true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			if got := lowMaskValue(tc.value); got != nil {
				t.Errorf("unsupported %T returned %d; want nil", tc.value, *got)
			}
		})
	}
}
