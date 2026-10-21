package valve

import (
	"context"
	"errors"
	"github.com/andreomorg/dota_analyst/replay-parser/internal/parsing"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"sync/atomic"
	"testing"
	"time"
)

type cacheTransport func(*http.Request) (*http.Response, error)

func (f cacheTransport) RoundTrip(r *http.Request) (*http.Response, error) { return f(r) }

const cacheURL = "https://replay202.valve.net/570/123.dem.bz2"

func testCache(t *testing.T, fn cacheTransport, cap int64, slots int) *Cache {
	t.Helper()
	d := NewDownloader(Limits{MaxDownloadBytes: 16, MaxDecompressedBytes: 32})
	d.client = &http.Client{Transport: fn}
	c, err := NewCache(d, t.TempDir(), cap, slots)
	if err != nil {
		t.Fatal(err)
	}
	return c
}
func cacheResponse(body io.ReadCloser) *http.Response {
	return &http.Response{StatusCode: 200, Body: body, ContentLength: -1}
}

func TestCacheHitAndDeleteAfterParse(t *testing.T) {
	for _, fail := range []bool{false, true} {
		t.Run(map[bool]string{false: "success", true: "failure"}[fail], func(t *testing.T) {
			var calls atomic.Int32
			c := testCache(t, func(*http.Request) (*http.Response, error) {
				calls.Add(1)
				return cacheResponse(io.NopCloser(strings.NewReader("demo"))), nil
			}, 64, 1)
			if n, err := c.Prefetch(context.Background(), cacheURL); err != nil || n != 4 {
				t.Fatalf("%d %v", n, err)
			}
			s := parsing.New(c, 1, time.Second)
			err := s.Run(context.Background(), cacheURL, func(_ context.Context, r io.Reader) error {
				b, e := io.ReadAll(r)
				if string(b) != "demo" {
					t.Errorf("body %q", b)
				}
				if fail {
					return parsing.ErrUnreadableReplay
				}
				return e
			}, nil)
			if fail != (err != nil) {
				t.Fatalf("parse: %v", err)
			}
			if calls.Load() != 1 {
				t.Fatal("cache hit downloaded")
			}
			entries, _ := os.ReadDir(c.dir)
			if len(entries) != 0 {
				t.Fatal("cache survived parse")
			}
		})
	}
}

type brokenCacheReader struct{}

func (brokenCacheReader) Read([]byte) (int, error) { return 0, io.ErrUnexpectedEOF }
func (brokenCacheReader) Close() error             { return nil }
func TestCacheAtomicFailure(t *testing.T) {
	c := testCache(t, func(*http.Request) (*http.Response, error) { return cacheResponse(brokenCacheReader{}), nil }, 64, 1)
	if _, err := c.Prefetch(context.Background(), cacheURL); !errors.Is(err, parsing.ErrDownloadFailed) {
		t.Fatal(err)
	}
	entries, _ := os.ReadDir(c.dir)
	if len(entries) != 0 {
		t.Fatal(entries)
	}
}
func TestCacheDedupeAndCapacity(t *testing.T) {
	started, release := make(chan struct{}), make(chan struct{})
	var calls atomic.Int32
	c := testCache(t, func(*http.Request) (*http.Response, error) {
		calls.Add(1)
		close(started)
		<-release
		return cacheResponse(io.NopCloser(strings.NewReader("demo"))), nil
	}, 64, 1)
	done := make(chan error, 2)
	go func() { _, e := c.Prefetch(context.Background(), cacheURL); done <- e }()
	<-started
	go func() { _, e := c.Prefetch(context.Background(), cacheURL); done <- e }()
	if _, e := c.Prefetch(context.Background(), strings.Replace(cacheURL, "123", "456", 1)); !errors.Is(e, parsing.ErrBusy) {
		t.Fatal(e)
	}
	close(release)
	for range 2 {
		if e := <-done; e != nil {
			t.Fatal(e)
		}
	}
	if calls.Load() != 1 {
		t.Fatal(calls.Load())
	}
	if _, e := c.Prefetch(context.Background(), cacheURL); e != nil {
		t.Fatal(e)
	}
}
func TestCacheDiskCapAndExpiry(t *testing.T) {
	var calls atomic.Int32
	c := testCache(t, func(*http.Request) (*http.Response, error) {
		calls.Add(1)
		return cacheResponse(io.NopCloser(strings.NewReader("demo"))), nil
	}, 16, 2)
	if _, e := c.Prefetch(context.Background(), cacheURL); e != nil {
		t.Fatal(e)
	}
	other := strings.Replace(cacheURL, "123", "456", 1)
	if _, e := c.Prefetch(context.Background(), other); !errors.Is(e, parsing.ErrBusy) {
		t.Fatal(e)
	}
	key, _ := c.key(cacheURL)
	path := filepath.Join(c.dir, key)
	old := time.Now().Add(-31 * time.Minute)
	if e := os.Chtimes(path, old, old); e != nil {
		t.Fatal(e)
	}
	if e := c.Cleanup(); e != nil {
		t.Fatal(e)
	}
	if _, e := c.Prefetch(context.Background(), other); e != nil {
		t.Fatal(e)
	}
	if calls.Load() != 2 {
		t.Fatal(calls.Load())
	}
}
func TestCacheDisabled(t *testing.T) {
	var calls atomic.Int32
	d := NewDownloader(Limits{})
	d.client = &http.Client{Transport: cacheTransport(func(*http.Request) (*http.Response, error) {
		calls.Add(1)
		return cacheResponse(io.NopCloser(strings.NewReader("demo"))), nil
	})}
	c, e := NewCache(d, "", 0, 0)
	if e != nil {
		t.Fatal(e)
	}
	if _, e = c.Prefetch(context.Background(), cacheURL); !errors.Is(e, parsing.ErrBusy) {
		t.Fatal(e)
	}
	for range 2 {
		s, e := c.Open(context.Background(), cacheURL)
		if e != nil {
			t.Fatal(e)
		}
		io.ReadAll(s)
		s.Close()
	}
	if calls.Load() != 2 {
		t.Fatal(calls.Load())
	}
}

func TestCacheCancellationLeavesNoFinal(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	c := testCache(t, func(*http.Request) (*http.Response, error) {
		return cacheResponse(&cancelCacheReader{cancel: cancel}), nil
	}, 64, 1)
	if _, err := c.Prefetch(ctx, cacheURL); !errors.Is(err, context.Canceled) {
		t.Fatal(err)
	}
	entries, _ := os.ReadDir(c.dir)
	if len(entries) != 0 {
		t.Fatal(entries)
	}
}

type cancelCacheReader struct {
	cancel context.CancelFunc
	sent   bool
}

func (r *cancelCacheReader) Read(p []byte) (int, error) {
	if !r.sent {
		r.sent = true
		return copy(p, "demo"), nil
	}
	r.cancel()
	return 0, context.Canceled
}
func (*cancelCacheReader) Close() error { return nil }

func TestCacheValidationAndSizeLimit(t *testing.T) {
	c := testCache(t, func(*http.Request) (*http.Response, error) {
		return cacheResponse(io.NopCloser(strings.NewReader(strings.Repeat("x", 17)))), nil
	}, 64, 1)
	if _, e := c.Prefetch(context.Background(), "https://evil.example/570/123.dem.bz2"); !errors.Is(e, parsing.ErrInvalidURL) {
		t.Fatal(e)
	}
	if _, e := c.Prefetch(context.Background(), cacheURL); !errors.Is(e, parsing.ErrTooLarge) {
		t.Fatal(e)
	}
	entries, _ := os.ReadDir(c.dir)
	if len(entries) != 0 {
		t.Fatal(entries)
	}
}

func TestCacheKeepsOriginalCompressedBytes(t *testing.T) {
	// A decoder is unnecessary for prefetch: the staging file must be byte-for-byte identical.
	payload := "BZh9" + strings.Repeat("x", 8)
	c := testCache(t, func(*http.Request) (*http.Response, error) {
		return cacheResponse(io.NopCloser(strings.NewReader(payload))), nil
	}, 64, 1)
	if n, e := c.Prefetch(context.Background(), cacheURL); e != nil || n != int64(len(payload)) {
		t.Fatalf("%d %v", n, e)
	}
	key, _ := c.key(cacheURL)
	b, e := os.ReadFile(filepath.Join(c.dir, key))
	if e != nil || string(b) != payload {
		t.Fatalf("%q %v", b, e)
	}
}
