package parsing

import (
	"context"
	"errors"
	"fmt"
	"io"
	"time"
)

// ReplaySource opens a bounded, decompressed stream owned by the caller.
type ReplaySource interface {
	Open(context.Context, string) (io.ReadCloser, error)
}

// Service shares capacity between all replay operations. Construct once per process.
type Service struct {
	source  ReplaySource
	slots   chan struct{}
	timeout time.Duration
	observe func(Measurement)
}

// Measurement contains only bounded operational fields, never replay URLs or identities.
type Measurement struct {
	Elapsed       time.Duration
	Open          time.Duration
	Analyze       time.Duration
	ReadBytes     int64
	ActiveAtStart int
}

type Option func(*Service)

// WithObserver installs a concurrency-safe, nonblocking observer owned by the composition root.
func WithObserver(observe func(Measurement)) Option { return func(s *Service) { s.observe = observe } }

func New(source ReplaySource, capacity int, timeout time.Duration, options ...Option) *Service {
	if capacity <= 0 || timeout <= 0 {
		panic("parsing: capacity and timeout must be positive")
	}
	s := &Service{source: source, slots: make(chan struct{}, capacity), timeout: timeout}
	for _, option := range options {
		option(s)
	}
	return s
}

// Run owns capacity, deadline and stream lifetime. It never retries or queues work.
// consume runs after the stream closes, retaining capacity until the result is consumed.
// Reading failures take precedence over a decoder's generic unreadable-replay error.
func (s *Service) Run(parent context.Context, url string, analyze func(context.Context, io.Reader) error, consume func()) error {
	if err := parent.Err(); err != nil {
		return err
	}
	select {
	case s.slots <- struct{}{}:
		defer func() { <-s.slots }()
	default:
		return ErrBusy
	}
	started := time.Now()
	measurement := Measurement{ActiveAtStart: len(s.slots)}
	defer func() {
		measurement.Elapsed = time.Since(started)
		if s.observe != nil {
			s.observe(measurement)
		}
	}()
	ctx, cancel := context.WithTimeout(parent, s.timeout)
	defer cancel()
	err := s.analyze(ctx, url, analyze, &measurement)
	if parent.Err() != nil {
		return parent.Err()
	}
	if errors.Is(ctx.Err(), context.DeadlineExceeded) {
		if err == nil {
			err = ctx.Err()
		}
		return fmt.Errorf("%w: gave up after %s: %w", ErrTimedOut, s.timeout, err)
	}
	if err == nil && consume != nil {
		consume()
	}
	return err
}

func (s *Service) analyze(ctx context.Context, url string, analyze func(context.Context, io.Reader) error, measurement *Measurement) error {
	started := time.Now()
	stream, err := s.source.Open(ctx, url)
	measurement.Open = time.Since(started)
	if err != nil {
		return err
	}
	defer stream.Close()
	tracked := &readErrorTracker{r: stream}
	started = time.Now()
	defer func() { measurement.Analyze = time.Since(started); measurement.ReadBytes = tracked.bytes }()
	err = analyze(ctx, tracked)
	if err != nil && tracked.err != nil && knownReadError(tracked.err) {
		return tracked.err
	}
	return err
}

func knownReadError(err error) bool {
	return errors.Is(err, ErrInvalidURL) || errors.Is(err, ErrReplayNotFound) ||
		errors.Is(err, ErrTooLarge) || errors.Is(err, ErrUnreadableReplay) ||
		errors.Is(err, ErrDownloadFailed) || errors.Is(err, ErrTimedOut)
}

type readErrorTracker struct {
	r     io.Reader
	err   error
	bytes int64
}

func (t *readErrorTracker) Read(p []byte) (int, error) {
	n, err := t.r.Read(p)
	t.bytes += int64(n)
	if err != nil && err != io.EOF && t.err == nil {
		t.err = err
	}
	return n, err
}
