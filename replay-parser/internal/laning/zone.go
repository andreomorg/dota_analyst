package laning

import "math"

// Zone is the part of the map a point belongs to.
type Zone string

const (
	ZoneTop    Zone = "top"
	ZoneMiddle Zone = "middle"
	ZoneBottom Zone = "bottom"
	ZoneJungle Zone = "jungle" // everything off the lanes, the river included
	ZoneBase   Zone = "base"   // behind the tier-3 towers, fountain included: not counted when choosing the lane
)

// Map coordinates are world units, roughly -8200..8200 on both axes, with Radiant bottom-left and Dire top-right.
// The limits below come from the towers of match 9034320024 (patch of 10/2026):
//
//	top lane:    Radiant towers at x ≈ -6450 (y -3408..1856), Dire towers at y ≈ 5950 (x -5275..3552)
//	bottom lane: Radiant towers at y ≈ -6250 (x -3952..4860), Dire towers at x ≈ 6350 (y -2240..3032)
//	middle lane: towers on the diagonal, from (-4640,-4144) to (4272,3759)
//	bases:       just behind the tier-3 towers (the lane towers themselves stay in their lane)
//
// Side lanes are L-shaped bands 1250 units wide on the inner side of the towers (the outer side is the map edge);
// the middle lane is a band about 1060 units wide each side of the diagonal (|x-y| < 1500).
const (
	radiantBaseMaxX = -4000 // Radiant tier-3 towers: top (-6592,-3408), middle (-4640,-4144), bottom (-3952,-6112)
	radiantBaseMaxY = -3700
	direBaseMinX    = 3700 // Dire tier-3 towers: top (3552,5776), middle (4272,3759), bottom (6336,3032)
	direBaseMinY    = 3300

	topLaneMaxX    = -5200 // vertical stretch, Radiant side
	topLaneMinY    = 4700  // horizontal stretch, Dire side
	bottomLaneMaxY = -5000 // horizontal stretch, Radiant side
	bottomLaneMinX = 5100  // vertical stretch, Dire side

	middleLaneMaxDiagonalGap = 1500 // |x - y|
)

// ZoneOf says which zone of the map the point (x, y) is in.
func ZoneOf(x, y float64) Zone {
	switch {
	case x < radiantBaseMaxX && y < radiantBaseMaxY, x > direBaseMinX && y > direBaseMinY:
		return ZoneBase
	case x < topLaneMaxX || y > topLaneMinY:
		return ZoneTop
	case y < bottomLaneMaxY || x > bottomLaneMinX:
		return ZoneBottom
	case math.Abs(x-y) < middleLaneMaxDiagonalGap:
		return ZoneMiddle
	default:
		return ZoneJungle
	}
}
