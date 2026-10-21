package replay

import (
	"math"
	"sort"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
)

type typedCombatEvent struct {
	at                     ServerTime
	source, target, amount int
	damageType             uint32
	heal                   bool
}

func (c *combatCollector) fillTypedBuckets(rows matchdata.List[matchdata.PlayerCombatBuckets]) {
	type key struct{ player, start int }
	damage := map[key]matchdata.DamageTypeBucket{}
	healing := map[key]matchdata.HealingBucket{}
	for _, e := range c.typed {
		start := int(math.Floor(c.session.Clock.Game(e.at)/matchdata.BucketSeconds)) * matchdata.BucketSeconds
		if e.heal {
			if e.target >= 0 && e.target < len(rows) {
				k := key{e.target, start}
				v := healing[k]
				v[0] = start
				v[1] += e.amount
				healing[k] = v
			}
			if e.source >= 0 && e.source < len(rows) {
				k := key{e.source, start}
				v := healing[k]
				v[0] = start
				v[2] += e.amount
				healing[k] = v
			}
			continue
		}
		column := damageTypeColumn(e.damageType)
		// HP removal has no physical/magical/pure type; do not silently relabel it as pure.
		if column < 0 {
			continue
		}
		if e.source >= 0 && e.source < len(rows) {
			k := key{e.source, start}
			v := damage[k]
			v[0] = start
			v[1+column] += e.amount
			damage[k] = v
		}
		if e.target >= 0 && e.target < len(rows) {
			k := key{e.target, start}
			v := damage[k]
			v[0] = start
			v[4+column] += e.amount
			damage[k] = v
		}
	}
	for k, v := range damage {
		if v[1] != 0 || v[2] != 0 || v[3] != 0 || v[4] != 0 || v[5] != 0 || v[6] != 0 {
			rows[k.player].ByType = append(rows[k.player].ByType, v)
		}
	}
	for k, v := range healing {
		if v[1] != 0 || v[2] != 0 {
			rows[k.player].Healing = append(rows[k.player].Healing, v)
		}
	}
	for i := range rows {
		sort.Slice(rows[i].ByType, func(a, b int) bool { return rows[i].ByType[a][0] < rows[i].ByType[b][0] })
		sort.Slice(rows[i].Healing, func(a, b int) bool { return rows[i].Healing[a][0] < rows[i].Healing[b][0] })
	}
}

func damageTypeColumn(raw uint32) int {
	switch raw {
	case 1:
		return 0
	case 2:
		return 1
	case 4:
		return 2
	default:
		return -1
	}
}
