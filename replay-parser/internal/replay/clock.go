package replay

import (
	"github.com/andreomorg/dota_analyst/replay-parser/internal/matchdata"
	"github.com/dotabuff/manta"
)

const ticksPerSecond = 30

// gameClock follows the game rules tick by tick and turns the server clock into the game clock (seconds since the
// horn, pauses left out). Both readers use it: Analyzer (/v1/laning) and Session (/v1/match).
type gameClock struct {
	pause       pauseTracker
	gameStart   float64 // m_flGameStartTime in server seconds; 0 until the horn
	pausedTicks float64 // m_nTotalPausedTicks on the last tick seen
	// pauses are every pause seen, in order, on the server clock (G3, 21/10/2026). The counter only grows when a pause
	// ends, so an event stamped with the counter of its moment came out up to a whole pause ahead (and the next one
	// went back): with the spans, a moment inside a pause is the moment it began (the game clock is frozen) and a
	// moment after it leaves the whole pause out, whenever the server updates the counter.
	pauses []pauseSpan
}

// pauseSpan is one pause: when it began on the server clock and the paused-ticks counter before and after it. Open
// until the counter changes after the pause flag clears (or the settle wait gives up: then after == before).
type pauseSpan struct {
	begin         float64
	before, after float64
	cleared       float64 // server seconds when the pause flag cleared; -1 while still paused
	closed        bool
}

// update reads the game rules on this tick and returns the seconds since the horn. It is not running before the horn,
// while the game is paused, or right after a pause until the pause is counted in m_nTotalPausedTicks (see
// pauseTracker).
func (c *gameClock) update(netTick uint32, rules *manta.Entity) (float64, bool) {
	if pausedTicks, ok := number(rules, "m_pGameRules.m_nTotalPausedTicks"); ok {
		c.pausedTicks = pausedTicks
	}
	paused, _ := rules.GetBool("m_pGameRules.m_bGamePaused")
	c.trackPause(float64(netTick)/ticksPerSecond, paused)
	gameStart, ok := number(rules, "m_pGameRules.m_flGameStartTime")
	if !ok || gameStart <= 0 {
		return 0, false
	}
	c.gameStart = gameStart
	if !c.pause.running(paused, c.pausedTicks) {
		return 0, false
	}
	clock := float64(netTick)/ticksPerSecond - gameStart - c.pausedTicks/ticksPerSecond
	return clock, clock >= 0
}

// trackPause opens a span on the first paused tick and closes it once the flag cleared and the counter moved (or after
// maxPauseSettleTicks of waiting, as pauseTracker does).
func (c *gameClock) trackPause(serverSeconds float64, paused bool) {
	if len(c.pauses) == 0 || c.pauses[len(c.pauses)-1].closed {
		if paused {
			c.pauses = append(c.pauses, pauseSpan{begin: serverSeconds, before: c.pausedTicks, cleared: -1})
		}
		return
	}
	span := &c.pauses[len(c.pauses)-1]
	if paused {
		span.cleared = -1
		return
	}
	if span.cleared < 0 {
		span.cleared = serverSeconds
	}
	if c.pausedTicks != span.before || serverSeconds-span.cleared >= float64(maxPauseSettleTicks)/ticksPerSecond {
		span.after = c.pausedTicks
		span.closed = true
	}
}

// pausedTicksAt is the paused-ticks counter that applies to a server-clock moment, from the pause spans: inside a pause,
// the counter before it plus the time already paused (so the game clock stays at the pause's start); after it, the
// counter after it.
func (c *gameClock) pausedTicksAt(serverSeconds float64) float64 {
	total := c.pauses[0].before
	for _, span := range c.pauses {
		if serverSeconds < span.begin {
			return total
		}
		inside := (serverSeconds - span.begin) * ticksPerSecond
		if !span.closed || inside < span.after-span.before {
			return span.before + inside
		}
		total = span.after
	}
	return total
}

// maxPauseSettleTicks is how long, after a pause, the clock waits for m_nTotalPausedTicks to count the pause.
const maxPauseSettleTicks = ticksPerSecond

// pauseTracker decides whether the game clock can be computed on a tick, given m_bGamePaused and
// m_nTotalPausedTicks. No paused replay has been checked yet, so it does not rely on when the server updates the
// counter (during the pause, or when it ends): it only refuses a snapshot that would put the clock in the wrong place.
// The dangerous one is m_bGamePaused already cleared while m_nTotalPausedTicks does not count the pause yet (a tick
// later): the clock would jump forward by the whole pause (a 5-minute pause at 8:00 would read as 13:00 and cut the
// laning phase at 8:00's numbers). So after a pause the clock stays stopped until the counter has changed since the
// pause began. If it does not change within maxPauseSettleTicks (an unexpected replay format), the clock runs again
// anyway, as it did before this guard.
type pauseTracker struct {
	paused       bool    // inside a pause, or waiting for the counter after one
	ticksAtPause float64 // m_nTotalPausedTicks when the pause began
	waited       int     // ticks waited for the counter after the pause flag cleared
}

// running reports whether the clock can be read on this tick.
func (t *pauseTracker) running(paused bool, totalPausedTicks float64) bool {
	if paused {
		if !t.paused {
			*t = pauseTracker{paused: true, ticksAtPause: totalPausedTicks}
		}
		return false
	}
	if t.paused {
		if totalPausedTicks == t.ticksAtPause && t.waited < maxPauseSettleTicks {
			t.waited++
			return false
		}
		t.paused = false
	}
	return true
}

// ServerTime is a moment on the server clock, the scale of the combat log timestamps (NetTick / 30), together with
// the pause ticks counted so far. It is what collectors keep while the replay is read: the game clock cannot be
// computed before the horn (m_flGameStartTime is 0 until then), so the conversion waits until Fill.
type ServerTime struct {
	// Seconds on the server clock.
	Seconds float64
	// PausedTicks is m_nTotalPausedTicks at that moment.
	PausedTicks float64
}

// Clock is the session's clock. While the replay is read, Now and At stamp moments as ServerTime; in Fill (the horn
// is known by then) Game and Seconds turn them into the game clock.
type Clock struct {
	parser  *manta.Parser
	game    gameClock
	gameEnd *ServerTime // when m_flGameEndTime appeared (the Ancient fell)
}

// Now is the current tick as a ServerTime.
func (c *Clock) Now() ServerTime {
	return ServerTime{Seconds: float64(c.parser.NetTick) / ticksPerSecond, PausedTicks: c.game.pausedTicks}
}

// At stamps a server-clock value read from the replay (a combat log timestamp, an item's m_flPurchaseTime) with the
// pause ticks counted so far.
func (c *Clock) At(serverSeconds float64) ServerTime {
	return ServerTime{Seconds: serverSeconds, PausedTicks: c.game.pausedTicks}
}

// Started reports whether the horn time is known. Game and Seconds are only meaningful once it is (always true in
// Fill: MatchAnalyzer refuses a replay whose clock never started).
func (c *Clock) Started() bool {
	return c.game.gameStart > 0
}

// Game converts a ServerTime to the game clock: seconds since the horn, pauses left out, negative before the horn. With
// the pauses seen (a real replay) the pause spans decide (a moment inside a pause is the pause's start); without any
// (no pause, or a hand-made clock) the counter stamped on the moment does, as before.
func (c *Clock) Game(t ServerTime) float64 {
	paused := t.PausedTicks
	if len(c.game.pauses) > 0 {
		paused = c.game.pausedTicksAt(t.Seconds)
	}
	return t.Seconds - c.game.gameStart - paused/ticksPerSecond
}

// Seconds is Game as the contract's type.
func (c *Clock) Seconds(t ServerTime) matchdata.Seconds {
	return matchdata.Seconds(c.Game(t))
}

// GameEnd is when the match ended (m_flGameEndTime, as a ServerTime); false when the replay stops before that.
func (c *Clock) GameEnd() (ServerTime, bool) {
	if c.gameEnd == nil {
		return ServerTime{}, false
	}
	return *c.gameEnd, true
}

// PausedSeconds is the time paused so far (at the end of the replay: the whole match).
func (c *Clock) PausedSeconds() float64 {
	return c.game.pausedTicks / ticksPerSecond
}
