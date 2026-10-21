package replay

import (
	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"math"
)

// worldCollector records day/night changes from the replicated world clock, including temporary overrides.
// Inspected CDOTAGamerulesProxy in 9034320024 at ticks 9000/18000/27000/36000: m_iNetTimeOfDay
// was 20483/53248/20479/53244, a 16-bit fraction of a day; m_flDaytimeStart/m_flNighttimeStart
// were .25/.75. All three temporary flags existed (false); their active precedence is not replay-verified.
type worldCollector struct {
	changes matchdata.List[matchdata.DayNight]
}

func (c *worldCollector) Register(s *Session) {
	s.OnSecond(func(second int) {
		rules := s.Entity("CDOTAGamerulesProxy")
		if rules == nil {
			return
		}
		const prefix = "m_pGameRules."
		time, known := number(rules, prefix+"m_iNetTimeOfDay")
		dayStart, dayKnown := number(rules, prefix+"m_flDaytimeStart")
		nightStart, nightKnown := number(rules, prefix+"m_flNighttimeStart")
		nightstalker, _ := rules.GetBool(prefix + "m_bIsNightstalkerNight")
		temporaryNight, _ := rules.GetBool(prefix + "m_bIsTemporaryNight")
		temporaryDay, _ := rules.GetBool(prefix + "m_bIsTemporaryDay")
		isDay := false
		switch {
		case nightstalker || temporaryNight:
			// Temporary nights count as night even if the natural clock still says day.
		case temporaryDay:
			isDay = true
		case known && dayKnown && nightKnown:
			isDay = naturalDay(time, dayStart, nightStart)
		default:
			return // Do not synthesize a clock when the replay lacks the properties.
		}
		if len(c.changes) == 0 || c.changes[len(c.changes)-1].IsDay != isDay {
			c.changes = append(c.changes, matchdata.DayNight{T: second, IsDay: isDay})
		}
	})
}

func (c *worldCollector) Fill(m *matchdata.Match) error {
	m.Timelines.DayNight = c.changes
	return nil
}

// naturalDay compares in the replicated 16-bit domain. At the horn, .25 is encoded
// as 16383 (inspect ticks 7874/7876 in 9034320024). Dividing by 65536 incorrectly
// labels that boundary as night; quantize both thresholds into the same domain.
func naturalDay(time, dayStart, nightStart float64) bool {
	day := math.Floor(dayStart * 65535)
	night := math.Floor(nightStart * 65535)
	if day <= night {
		return time >= day && time < night
	}
	return time >= day || time < night
}
