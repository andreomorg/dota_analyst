package valve

import (
	"bytes"
	"context"
	"errors"
	"io"
	"net/http"
	"net/http/httptest"
	"net/url"
	"strconv"
	"testing"

	"github.com/klauspost/compress/zstd"
)

func TestParseURL(t *testing.T) {
	tests := []struct {
		url   string
		valid bool
	}{
		{"http://replay202.valve.net/570/9034320024_96247495.dem.bz2", true},
		{"https://replay202.valve.net/570/9034320024_96247495.dem.bz2", true},
		{"HTTP://REPLAY202.VALVE.NET/570/9034320024_96247495.dem.bz2", true},
		{"http://replay1.cluster.valve.net/570/1_2.dem.bz2", true},

		{"", false},
		{"replay202.valve.net/570/1_2.dem.bz2", false},
		{"ftp://replay202.valve.net/570/1_2.dem.bz2", false},
		{"file:///570/1_2.dem.bz2", false},
		{"http:replay202.valve.net/570/1_2.dem.bz2", false},
		{"http://example.com/570/1_2.dem.bz2", false},
		{"http://valve.net/570/1_2.dem.bz2", false},
		{"http://.valve.net/570/1_2.dem.bz2", false},
		{"http://a..valve.net/570/1_2.dem.bz2", false},
		{"http://evilvalve.net/570/1_2.dem.bz2", false},
		{"http://replay202.evilvalve.net/570/1_2.dem.bz2", false},
		{"http://valve.net.evil.com/570/1_2.dem.bz2", false},
		{"http://replay202.valve.net.evil.com/570/1_2.dem.bz2", false},
		{"http://replay202.valve.net./570/1_2.dem.bz2", false},
		{"http://replay202.valve.net%2eevil.com/570/1_2.dem.bz2", false},
		{"http://user@replay202.valve.net/570/1_2.dem.bz2", false},
		{"http://user:pass@replay202.valve.net/570/1_2.dem.bz2", false},
		{"http://evil.com@replay202.valve.net/570/1_2.dem.bz2", false},
		{"http://replay202.valve.net:8080/570/1_2.dem.bz2", false},
		{"http://replay202.valve.net:80/570/1_2.dem.bz2", false},
		{"http://replay202.valve.net:/570/1_2.dem.bz2", false},
		{"http://127.0.0.1/570/1_2.dem.bz2", false},
		{"http://[::1]/570/1_2.dem.bz2", false},
		{"http://169.254.169.254/570/1_2.dem.bz2", false},
		{"http://replay202.valve.net/", false},
		{"http://replay202.valve.net/571/1_2.dem.bz2", false},
		{"http://replay202.valve.net/570/1_2.dem", false},
		{"http://replay202.valve.net/570/1_2.dem.bz2.exe", false},
		{"http://replay202.valve.net/570/a/b.dem.bz2", false},
		{"http://replay202.valve.net/570/../1_2.dem.bz2", false},
		{"http://replay202.valve.net/570/a%2Fb.dem.bz2", false},
		{"http://replay202.valve.net/570/.dem.bz2", false},
		{"http://replay202.valve.net//570/1_2.dem.bz2", false},
		{"http://replay202.valve.net/570/1_2.dem.bz2?x=1", false},
		{"http://replay202.valve.net/570/1_2.dem.bz2?", false},
		{"http://replay202.valve.net/570/1_2.dem.bz2#frag", false},
	}
	d := NewDownloader(Limits{})
	for _, test := range tests {
		t.Run(test.url, func(t *testing.T) {
			_, err := d.parseURL(test.url)
			if test.valid && err != nil {
				t.Fatalf("expected valid, got %v", err)
			}
			if !test.valid && !errors.Is(err, ErrInvalidURL) {
				t.Fatalf("expected ErrInvalidURL, got %v", err)
			}
		})
	}
}

// Open must refuse an invalid URL before any network access.
func TestOpen_InvalidURLNeverConnects(t *testing.T) {
	hits := 0
	server := httptest.NewServer(http.HandlerFunc(func(http.ResponseWriter, *http.Request) { hits++ }))
	defer server.Close()

	_, err := NewDownloader(Limits{}).Open(context.Background(), server.URL+"/570/1_2.dem.bz2")
	if !errors.Is(err, ErrInvalidURL) {
		t.Fatalf("expected ErrInvalidURL, got %v", err)
	}
	if hits != 0 {
		t.Fatalf("server was called %d times", hits)
	}
}

const replayFile = "/570/1_2.dem.bz2"

// testDownloader allows only the given test servers' hosts; every other check (scheme, path, ...) stays on.
func testDownloader(limits Limits, servers ...*httptest.Server) *Downloader {
	d := NewDownloader(limits)
	d.allowHost = func(u *url.URL) bool {
		for _, server := range servers {
			if "http://"+u.Host == server.URL {
				return true
			}
		}
		return false
	}
	return d
}

func zstdBytes(t *testing.T, content []byte) []byte {
	t.Helper()
	var buffer bytes.Buffer
	encoder, err := zstd.NewWriter(&buffer)
	if err != nil {
		t.Fatal(err)
	}
	if _, err := encoder.Write(content); err != nil {
		t.Fatal(err)
	}
	if err := encoder.Close(); err != nil {
		t.Fatal(err)
	}
	return buffer.Bytes()
}

func TestOpen(t *testing.T) {
	content := bytes.Repeat([]byte("PBDEMS2 "), 10) // 80 bytes
	compressed := zstdBytes(t, content)
	limits := Limits{MaxDownloadBytes: 1000, MaxDecompressedBytes: 100}

	tests := []struct {
		name    string
		limits  Limits
		handler http.HandlerFunc
		openErr error // expected from Open
		readErr error // expected from reading the stream (when Open succeeds)
		notErr  error // must not be in the chain
	}{
		{
			name:    "zstd replay",
			handler: func(w http.ResponseWriter, _ *http.Request) { w.Write(compressed) },
		},
		{
			name: "plain replay of exactly the decompressed limit",
			limits: Limits{
				MaxDownloadBytes: 1000, MaxDecompressedBytes: int64(len(content)),
			},
			handler: func(w http.ResponseWriter, _ *http.Request) { w.Write(content) },
		},
		{
			name:    "404",
			handler: func(w http.ResponseWriter, _ *http.Request) { http.NotFound(w, nil) },
			openErr: ErrReplayNotFound,
		},
		{
			name:    "410",
			handler: func(w http.ResponseWriter, _ *http.Request) { w.WriteHeader(http.StatusGone) },
			openErr: ErrReplayNotFound,
		},
		{
			name: "502 with the GCS NoSuchKey body (what Valve's CDN answers for a missing replay)",
			handler: func(w http.ResponseWriter, _ *http.Request) {
				w.Header().Set("Content-Type", "text/html")
				w.WriteHeader(http.StatusBadGateway)
				io.WriteString(w, "<?xml version='1.0' encoding='UTF-8'?><Error><Code>NoSuchKey</Code>"+
					"<Message>The specified key does not exist.</Message></Error>")
			},
			openErr: ErrReplayNotFound,
		},
		{
			name:    "plain 502",
			handler: func(w http.ResponseWriter, _ *http.Request) { w.WriteHeader(http.StatusBadGateway) },
			openErr: ErrDownloadFailed,
			notErr:  ErrReplayNotFound,
		},
		{
			name:    "403 is a download failure, not a missing replay",
			handler: func(w http.ResponseWriter, _ *http.Request) { w.WriteHeader(http.StatusForbidden) },
			openErr: ErrDownloadFailed,
			notErr:  ErrReplayNotFound,
		},
		{
			name:    "empty body",
			handler: func(http.ResponseWriter, *http.Request) {},
			openErr: ErrDownloadFailed,
		},
		{
			name: "Content-Length over the download limit is refused before reading",
			handler: func(w http.ResponseWriter, _ *http.Request) {
				w.Header().Set("Content-Length", "1001")
				w.Write(bytes.Repeat([]byte{0}, 1001))
			},
			openErr: ErrTooLarge,
		},
		{
			name: "body over the download limit without Content-Length",
			handler: func(w http.ResponseWriter, _ *http.Request) {
				w.Write(content) // starts like a replay
				w.(http.Flusher).Flush()
				w.Write(bytes.Repeat([]byte{0}, 2000))
			},
			limits:  Limits{MaxDownloadBytes: 1000, MaxDecompressedBytes: 1 << 20},
			readErr: ErrTooLarge,
		},
		{
			name:    "decompressed size over the limit",
			handler: func(w http.ResponseWriter, _ *http.Request) { w.Write(zstdBytes(t, bytes.Repeat(content, 2))) },
			readErr: ErrTooLarge,
		},
		{
			name: "connection dropped in the middle of a zstd stream",
			handler: func(w http.ResponseWriter, _ *http.Request) {
				w.Header().Set("Content-Length", strconv.Itoa(len(compressed)+100))
				w.Write(compressed[:len(compressed)/2])
			},
			readErr: ErrDownloadFailed,
		},
		{
			name: "redirect inside the allowed host",
			handler: func(w http.ResponseWriter, r *http.Request) {
				if r.URL.Path == replayFile {
					http.Redirect(w, r, "/570/3_4.dem.bz2", http.StatusFound)
					return
				}
				w.Write(compressed)
			},
		},
		{
			name: "redirect to a path outside /570/",
			handler: func(w http.ResponseWriter, r *http.Request) {
				http.Redirect(w, r, "/admin", http.StatusFound)
			},
			openErr: ErrDownloadFailed,
			notErr:  ErrInvalidURL,
		},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			server := httptest.NewServer(test.handler)
			defer server.Close()
			testLimits := limits
			if test.limits != (Limits{}) {
				testLimits = test.limits
			}

			stream, err := testDownloader(testLimits, server).Open(context.Background(), server.URL+replayFile)
			if test.notErr != nil && errors.Is(err, test.notErr) {
				t.Fatalf("error must not be %v: %v", test.notErr, err)
			}
			if test.openErr != nil {
				if !errors.Is(err, test.openErr) {
					t.Fatalf("Open: expected %v, got %v", test.openErr, err)
				}
				return
			}
			if err != nil {
				t.Fatalf("Open: %v", err)
			}
			defer stream.Close()

			got, err := io.ReadAll(stream)
			if test.readErr != nil {
				if !errors.Is(err, test.readErr) {
					t.Fatalf("read: expected %v, got %v", test.readErr, err)
				}
				return
			}
			if err != nil {
				t.Fatalf("read: %v", err)
			}
			if !bytes.Equal(got, content) {
				t.Fatalf("got %q, want %q", got, content)
			}
		})
	}
}

func TestOpen_RedirectToAnotherHostIsRefused(t *testing.T) {
	otherHits := 0
	other := httptest.NewServer(http.HandlerFunc(func(http.ResponseWriter, *http.Request) { otherHits++ }))
	defer other.Close()
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		http.Redirect(w, r, other.URL+replayFile, http.StatusFound)
	}))
	defer server.Close()

	_, err := testDownloader(Limits{}, server).Open(context.Background(), server.URL+replayFile)
	if !errors.Is(err, ErrDownloadFailed) || errors.Is(err, ErrInvalidURL) {
		t.Fatalf("expected ErrDownloadFailed (not ErrInvalidURL), got %v", err)
	}
	if otherHits != 0 {
		t.Fatal("the redirect target was called")
	}
}

func TestOpen_CanceledContextStopsTheDownload(t *testing.T) {
	content := bytes.Repeat([]byte("PBDEMS2 "), 10)
	release := make(chan struct{})
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.Write(content)
		w.(http.Flusher).Flush()
		<-release // never sends the rest
	}))
	defer server.Close()
	defer close(release)

	ctx, cancel := context.WithCancel(context.Background())
	stream, err := testDownloader(Limits{}, server).Open(ctx, server.URL+replayFile)
	if err != nil {
		t.Fatal(err)
	}
	defer stream.Close()
	if _, err := io.ReadFull(stream, make([]byte, len(content))); err != nil {
		t.Fatal(err)
	}
	cancel()
	_, err = io.ReadAll(stream)
	if !errors.Is(err, context.Canceled) || !errors.Is(err, ErrDownloadFailed) {
		t.Fatalf("expected a canceled download, got %v", err)
	}
}

type closeSpy struct{ closed bool }

func (c *closeSpy) Close() error { c.closed = true; return nil }

func TestReplayStream_CloseClosesDecompressorAndConnection(t *testing.T) {
	decompressor, connection := &closeSpy{}, &closeSpy{}
	stream := &replayStream{decompressor: decompressor, httpBody: connection}
	if err := stream.Close(); err != nil {
		t.Fatal(err)
	}
	if !decompressor.closed || !connection.closed {
		t.Fatalf("decompressor closed: %v, connection closed: %v", decompressor.closed, connection.closed)
	}
}
