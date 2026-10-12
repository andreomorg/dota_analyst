package replay

import "github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"

// visionCollector fills the vision and objectives sections.
//
// TODO(sprint 10, card 06): wards, smokes, scans, glyphs; towers, barracks, Roshan, Tormentor, couriers, runes,
// outposts.
//   - vision.wards: OnEntity("CDOTA_NPC_Observer_Ward") (the _TrueSight suffix is a sentry): created = placed,
//     deleted = removed; owner by m_hOwnerEntity (the hero for sentries -> Players.ByHeroEntity; the
//     CDOTAPlayerController for observers -> m_steamID -> Players.BySteamID); destroyedBy from OnCombatLog(DEATH) of
//     npc_dota_observer_wards / npc_dota_sentry_wards.
//   - smokes/scans/glyphs, Roshan + Aegis, runes: OnChatEvent (SMOKE_ACTIVATED, SCAN_USED, GLYPH_USED, ROSHAN_KILL,
//     AEGIS, RUNE_PICKUP/RUNE_BOTTLE).
//   - buildings, Roshan, Tormentor, couriers: OnCombatLog(DEATH) (Raw.GetIsTargetBuilding(), npc_dota_roshan,
//     npc_dota_miniboss, npc_dota_courier).
//   - outposts: Parser.Callbacks.OnCDOTAUserMsg_OutpostCaptured (stamp with Clock.Now()).
type visionCollector struct{}

func (c *visionCollector) Register(*Session) {}

func (c *visionCollector) Fill(*matchdata.Match) error { return nil }
