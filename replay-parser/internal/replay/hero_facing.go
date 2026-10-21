package replay

import (
	"math"

	"github.com/dotabuff/manta"
)

func heroFacing(hero *manta.Entity) *int {
	rotation, ok := hero.Get("CBodyComponent.m_angRotation").([]float32)
	if !ok || len(rotation) < 2 {
		return nil
	}
	return normalizedYaw(float64(rotation[1]))
}

// Rounding may produce 360; wrap afterwards so north never has two representations.
func normalizedYaw(yaw float64) *int {
	if math.IsNaN(yaw) || math.IsInf(yaw, 0) {
		return nil
	}
	v := int(math.Round(math.Mod(yaw, 360))) % 360
	if v < 0 {
		v += 360
	}
	return &v
}
