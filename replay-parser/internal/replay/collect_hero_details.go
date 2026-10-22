package replay

import (
	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"math"
)

// Reads replicated components only. Four decimal places preserve BAT 1.45 and small regen values without
// claiming the effective total displayed in the HUD. Missing/nonfinite properties remain null.
func readHeroCombatDetails(read func(string) (float64, bool)) matchdata.CombatDetails {
	value := func(key string) *float64 {
		v, ok := read(key)
		if !ok || math.IsNaN(v) || math.IsInf(v, 0) {
			return nil
		}
		v = math.Round(v*10000) / 10000
		return &v
	}
	return matchdata.CombatDetails{
		StrengthBase: value("m_flStrength"), AgilityBase: value("m_flAgility"), IntellectBase: value("m_flIntellect"),
		DamageBaseMin: value("m_iBaseDamageMin"), DamageBaseMax: value("m_iBaseDamageMax"), DamageBonus: value("m_iDamageBonus"),
		BaseAttackTime: value("m_flBaseAttackTime"), AttackRange: value("m_iAttackRange"), AttackCapability: value("m_iAttackCapabilities"),
		HealthThinkRegen: value("m_flHealthThinkRegen"), ManaThinkRegen: value("m_flManaThinkRegen"),
	}
}
