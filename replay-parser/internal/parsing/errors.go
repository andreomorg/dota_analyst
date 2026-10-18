// Package parsing coordinates replay acquisition and analysis without HTTP or decoder dependencies.
package parsing

import (
	"errors"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/laning"
)

var (
	ErrInvalidURL     = errors.New("invalid replay URL")
	ErrReplayNotFound = errors.New("replay not found on Valve's CDN")
	ErrDownloadFailed = errors.New("replay download failed")
	ErrTooLarge       = errors.New("replay exceeds the size limit")
	// Preserve the existing sentinel identity and keep laning standard-library only.
	ErrUnreadableReplay = laning.ErrUnreadableReplay
	ErrBusy             = errors.New("the maximum number of replays is already being parsed; try again later")
	ErrTimedOut         = errors.New("timed out")
)
