package replay

import "github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"

// itemsCollector fills the items and abilities sections.
//
// TODO(sprint 10, card 05): purchases (incl. pre-horn), item uses, final items, ability upgrades and uses.
//   - items.purchases: OnCombatLog(PURCHASE) (target = hero, item name = Session.Name(Raw.GetValue())); the purchases
//     before ~-0:59 are missing from the combat log: complete them from the item entities (m_flPurchaseTime is server
//     time: Clock.At(purchaseTime); name by Session.EntityName).
//   - items.uses: OnCombatLog(ITEM); abilities.uses: OnCombatLog(ABILITY).
//   - items.final: in OnEnd, the hero's m_hItems.0000-0017 (uint32 handles, not GetUint64): slots 0-5 inventory,
//     6-8 backpack, 15 teleport, 16 neutral, 17 enhancement. They are gone at the end of the replay.
//   - abilities.upgrades: in OnSecond, compare m_vecAbilities.0000-0034 levels (uint32 handles) with the last ones.
type itemsCollector struct{}

func (c *itemsCollector) Register(*Session) {}

func (c *itemsCollector) Fill(*matchdata.Match) error { return nil }
