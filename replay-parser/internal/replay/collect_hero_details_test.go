package replay

import (
	"math"
	"testing"
)

func TestHeroCombatDetailsDoesNotInventTotalsAndPreservesBAT(t *testing.T) {
	values := map[string]float64{"m_flBaseAttackTime": 1.45, "m_iDamageBonus": 0, "m_flStrength": 33.3,
		"m_flManaThinkRegen": math.Inf(1)}
	d := readHeroCombatDetails(func(k string) (float64, bool) { v, ok := values[k]; return v, ok })
	if d.BaseAttackTime == nil || *d.BaseAttackTime != 1.45 {
		t.Fatal("BAT lost precision")
	}
	if d.DamageBonus == nil || *d.DamageBonus != 0 {
		t.Fatal("observed zero lost")
	}
	if d.DamageBaseMax != nil || d.ManaThinkRegen != nil {
		t.Fatal("missing or nonfinite became a value")
	}
}
