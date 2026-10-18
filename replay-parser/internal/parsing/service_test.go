package parsing

import (
	"context"
	"errors"
	"io"
	"strings"
	"testing"
	"time"
)

type sourceFunc func(context.Context, string) (io.ReadCloser, error)

func (f sourceFunc) Open(ctx context.Context, url string) (io.ReadCloser, error) { return f(ctx, url) }

type stream struct {
	io.Reader
	closed bool
}

func (s *stream) Close() error { s.closed = true; return nil }
func newService() (*Service, *stream) {
	r := &stream{Reader: strings.NewReader("replay")}
	return New(sourceFunc(func(context.Context, string) (io.ReadCloser, error) { return r, nil }), 1, time.Second), r
}
func TestLifecycle(t *testing.T) {
	for _, tc := range []struct {
		name       string
		failure    error
		panicValue bool
	}{
		{"success", nil, false}, {"failure", ErrUnreadableReplay, false}, {"panic", nil, true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			s, r := newService()
			func() {
				defer func() {
					if got := recover(); (got != nil) != tc.panicValue {
						t.Errorf("panic = %v", got)
					}
				}()
				err := s.Run(context.Background(), "unused", func(context.Context, io.Reader) error {
					if tc.panicValue {
						panic("collector")
					}
					return tc.failure
				}, nil)
				if !errors.Is(err, tc.failure) {
					t.Errorf("error = %v", err)
				}
			}()
			if !r.closed || len(s.slots) != 0 {
				t.Fatal("stream or capacity leaked")
			}
		})
	}
}
func TestCapacityIncludesConsumptionAfterClosingStream(t *testing.T) {
	s, r := newService()
	err := s.Run(context.Background(), "unused", func(context.Context, io.Reader) error { return nil }, func() {
		if !r.closed {
			t.Error("result consumed before stream closed")
		}
		err := s.Run(context.Background(), "unused", func(context.Context, io.Reader) error { t.Fatal("busy work executed"); return nil }, nil)
		if !errors.Is(err, ErrBusy) {
			t.Errorf("expected busy, got %v", err)
		}
	})
	if err != nil || len(s.slots) != 0 {
		t.Fatalf("error %v, slots %d", err, len(s.slots))
	}
}
func TestDeadlineAndCancellation(t *testing.T) {
	s, _ := newService()
	s.timeout = time.Millisecond
	err := s.Run(context.Background(), "unused", func(ctx context.Context, _ io.Reader) error { <-ctx.Done(); return ctx.Err() }, nil)
	if !errors.Is(err, ErrTimedOut) || !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("deadline = %v", err)
	}
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	err = s.Run(ctx, "unused", func(context.Context, io.Reader) error { t.Fatal("cancelled work executed"); return nil }, nil)
	if !errors.Is(err, context.Canceled) || len(s.slots) != 0 {
		t.Fatalf("cancellation = %v", err)
	}
}

type brokenReader struct{}

func (brokenReader) Read([]byte) (int, error) { return 0, ErrDownloadFailed }
func TestDownloadFailurePrecedesDecoderError(t *testing.T) {
	s, r := newService()
	r.Reader = brokenReader{}
	err := s.Run(context.Background(), "unused", func(_ context.Context, r io.Reader) error { _, _ = io.ReadAll(r); return ErrUnreadableReplay }, nil)
	if !errors.Is(err, ErrDownloadFailed) || !r.closed {
		t.Fatalf("error = %v, closed = %v", err, r.closed)
	}
}
func TestOpenFailureReleasesCapacity(t *testing.T) {
	s := New(sourceFunc(func(context.Context, string) (io.ReadCloser, error) { return nil, ErrReplayNotFound }), 1, time.Second)
	err := s.Run(context.Background(), "unused", func(context.Context, io.Reader) error { t.Fatal("analyzer called"); return nil }, nil)
	if !errors.Is(err, ErrReplayNotFound) || len(s.slots) != 0 {
		t.Fatalf("error = %v", err)
	}
}

func TestObserverMeasuresConsumedBytesWithoutIdentities(t *testing.T) {
	s, _ := newService()
	var got Measurement
	s.observe = func(m Measurement) { got = m }
	err := s.Run(context.Background(), "private-url", func(_ context.Context, r io.Reader) error { _, err := io.Copy(io.Discard, r); return err }, nil)
	if err != nil || got.ReadBytes != 6 || got.ActiveAtStart != 1 || got.Elapsed < 0 || got.Open < 0 || got.Analyze < 0 {
		t.Fatalf("error %v measurement %+v", err, got)
	}
}
