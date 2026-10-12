package valve

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/url"
	"regexp"
	"strings"
	"time"
)

// Errors returned by Downloader. Callers map them to HTTP statuses with errors.Is.
var (
	// ErrInvalidURL means the URL is not a Valve replay URL (http(s)://*.valve.net/570/<file>.dem.bz2).
	ErrInvalidURL = errors.New("invalid replay URL")
	// ErrReplayNotFound means Valve no longer has the replay (replays expire after a few weeks).
	ErrReplayNotFound = errors.New("replay not found on Valve's CDN")
	// ErrDownloadFailed means the download failed for a reason that may go away on a retry (network, CDN error).
	ErrDownloadFailed = errors.New("replay download failed")
	// ErrTooLarge means the replay is bigger than the configured limits; retrying will not help.
	ErrTooLarge = errors.New("replay exceeds the size limit")
)

// Default limits. A real replay is ~30 MB compressed and a few hundred MB decompressed.
const (
	DefaultMaxDownloadBytes     int64 = 200 << 20 // 200 MB
	DefaultMaxDecompressedBytes int64 = 1 << 30   // 1 GB
)

const (
	maxRedirects = 5
	// How much of an error response is read to look for the "missing object" marker.
	maxErrorBodyBytes = 4 << 10
)

// Valve's CDN is Google Cloud Storage behind Google's edge cache: a missing replay comes back as 502 with this GCS
// error in the body, not as 404 (checked on 11/10/2026).
var noSuchKeyMarker = []byte("<Code>NoSuchKey</Code>")

var (
	replayPath = regexp.MustCompile(`^/570/[A-Za-z0-9_-]+\.dem\.bz2$`)
	hostLabel  = regexp.MustCompile(`^[a-z0-9]([a-z0-9-]*[a-z0-9])?$`)
)

// Limits caps how much a single replay may use. Zero values mean the defaults.
type Limits struct {
	MaxDownloadBytes     int64
	MaxDecompressedBytes int64
}

// Downloader downloads replays from Valve's CDN and returns them decompressed. It only talks to *.valve.net, even
// when redirected (SSRF protection: the URL comes from outside).
type Downloader struct {
	client          *http.Client
	maxDownload     int64
	maxDecompressed int64
	// allowHost checks the host part of the URL; tests swap it so they can reach an httptest server on 127.0.0.1.
	allowHost func(u *url.URL) bool
}

// NewDownloader returns a Downloader with the given limits.
func NewDownloader(limits Limits) *Downloader {
	d := &Downloader{
		maxDownload:     orDefault(limits.MaxDownloadBytes, DefaultMaxDownloadBytes),
		maxDecompressed: orDefault(limits.MaxDecompressedBytes, DefaultMaxDecompressedBytes),
		allowHost:       isValveHost,
	}
	transport := http.DefaultTransport.(*http.Transport).Clone()
	// Without this the transport would transparently gunzip, and the byte count would no longer be the download size.
	transport.DisableCompression = true
	transport.ResponseHeaderTimeout = 30 * time.Second
	// No Client.Timeout: the caller's context bounds the whole download, including reading the body while parsing.
	d.client = &http.Client{Transport: transport, CheckRedirect: d.checkRedirect}
	return d
}

func orDefault(value, fallback int64) int64 {
	if value <= 0 {
		return fallback
	}
	return value
}

// Open starts downloading replayURL and returns the decompressed replay as a stream. Reading the stream reads the
// download, so ctx must stay alive until the stream is closed. Close releases the decompressor and the connection.
func (d *Downloader) Open(ctx context.Context, replayURL string) (io.ReadCloser, error) {
	u, err := d.parseURL(replayURL)
	if err != nil {
		return nil, err
	}

	request, err := http.NewRequestWithContext(ctx, http.MethodGet, u.String(), nil)
	if err != nil {
		return nil, fmt.Errorf("%w: %v", ErrInvalidURL, err)
	}
	response, err := d.client.Do(request)
	if err != nil {
		return nil, fmt.Errorf("%w: %w", ErrDownloadFailed, err)
	}
	if response.StatusCode != http.StatusOK {
		defer response.Body.Close()
		return nil, statusError(response)
	}
	if response.ContentLength > d.maxDownload {
		response.Body.Close()
		return nil, fmt.Errorf("%w: download of %d bytes, limit %d", ErrTooLarge, response.ContentLength, d.maxDownload)
	}

	body := &limitedReader{r: response.Body, remaining: d.maxDownload, wrap: downloadError}
	decompressed, err := Decompress(body)
	if err != nil {
		response.Body.Close()
		if errors.Is(err, io.EOF) {
			return nil, fmt.Errorf("%w: empty response", ErrDownloadFailed)
		}
		return nil, wrapAsDownloadError(err)
	}
	return &replayStream{
		limited:      &limitedReader{r: decompressed, remaining: d.maxDecompressed, wrap: identity},
		body:         body,
		decompressor: decompressed,
		httpBody:     response.Body,
	}, nil
}

// statusError turns a non-200 response into ErrReplayNotFound or ErrDownloadFailed. 403 is not treated as "not
// found": a wrong "gone" would be stored for good by the API, while a download failure is retried later.
func statusError(response *http.Response) error {
	if response.StatusCode == http.StatusNotFound || response.StatusCode == http.StatusGone {
		return fmt.Errorf("%w: status %d", ErrReplayNotFound, response.StatusCode)
	}
	head, _ := io.ReadAll(io.LimitReader(response.Body, maxErrorBodyBytes))
	if bytes.Contains(head, noSuchKeyMarker) {
		return fmt.Errorf("%w: status %d (NoSuchKey)", ErrReplayNotFound, response.StatusCode)
	}
	return fmt.Errorf("%w: status %d", ErrDownloadFailed, response.StatusCode)
}

// checkRedirect follows a redirect only when its target passes the same checks as the original URL.
func (d *Downloader) checkRedirect(request *http.Request, via []*http.Request) error {
	if len(via) >= maxRedirects {
		return fmt.Errorf("stopped after %d redirects", maxRedirects)
	}
	if err := d.checkURL(request.URL); err != nil {
		// %v, not %w: the caller's URL was valid, so this must not surface as ErrInvalidURL (400).
		return fmt.Errorf("redirect to a URL outside Valve's CDN refused: %v", err)
	}
	return nil
}

func (d *Downloader) parseURL(raw string) (*url.URL, error) {
	u, err := url.Parse(raw)
	if err != nil {
		return nil, fmt.Errorf("%w: %v", ErrInvalidURL, err)
	}
	if err := d.checkURL(u); err != nil {
		return nil, err
	}
	return u, nil
}

// checkURL accepts only http(s)://<name>.valve.net/570/<file>.dem.bz2, with no user info, port, query or fragment.
func (d *Downloader) checkURL(u *url.URL) error {
	switch {
	case u.Scheme != "http" && u.Scheme != "https":
		return fmt.Errorf("%w: scheme must be http or https", ErrInvalidURL)
	case u.Opaque != "":
		return fmt.Errorf("%w: malformed URL", ErrInvalidURL)
	case u.User != nil:
		return fmt.Errorf("%w: user info is not allowed", ErrInvalidURL)
	case !d.allowHost(u):
		return fmt.Errorf("%w: host must be *.valve.net", ErrInvalidURL)
	case !replayPath.MatchString(u.EscapedPath()):
		return fmt.Errorf("%w: path must be /570/<file>.dem.bz2", ErrInvalidURL)
	case u.RawQuery != "" || u.ForceQuery || u.Fragment != "":
		return fmt.Errorf("%w: query and fragment are not allowed", ErrInvalidURL)
	}
	return nil
}

// isValveHost requires a DNS name ending in ".valve.net" (a suffix of whole labels, so "evilvalve.net" and
// "valve.net.evil.com" fail), with no IP literal and no explicit port.
func isValveHost(u *url.URL) bool {
	if u.Port() != "" || strings.HasSuffix(u.Host, ":") {
		return false
	}
	host := strings.ToLower(u.Hostname())
	if net.ParseIP(host) != nil {
		return false
	}
	name, found := strings.CutSuffix(host, ".valve.net")
	if !found || name == "" {
		return false
	}
	for label := range strings.SplitSeq(name, ".") {
		if !hostLabel.MatchString(label) {
			return false
		}
	}
	return true
}

// replayStream is what Open returns: the decompressed replay, capped, closing both the decoder and the connection.
type replayStream struct {
	limited      *limitedReader
	body         *limitedReader
	decompressor io.Closer
	httpBody     io.Closer
}

func (s *replayStream) Read(p []byte) (int, error) {
	n, err := s.limited.Read(p)
	// Decoders may hide or reword the error of the reader below them; when the download itself failed, report that
	// (a network failure is not a corrupt replay).
	if err != nil && err != io.EOF && s.body.err != nil {
		err = s.body.err
	}
	return n, err
}

func (s *replayStream) Close() error {
	return errors.Join(s.decompressor.Close(), s.httpBody.Close())
}

// limitedReader fails with ErrTooLarge once more than remaining bytes are read, and remembers the first error.
// wrap tags errors from the reader below (e.g. as ErrDownloadFailed).
type limitedReader struct {
	r         io.Reader
	remaining int64
	wrap      func(error) error
	err       error
}

func (l *limitedReader) Read(p []byte) (int, error) {
	if l.err != nil {
		return 0, l.err
	}
	// Ask for one byte past the limit so a stream of exactly the limit still ends with a clean EOF.
	if int64(len(p)) > l.remaining+1 {
		p = p[:l.remaining+1]
	}
	n, err := l.r.Read(p)
	if int64(n) > l.remaining {
		n = int(l.remaining)
		l.remaining = 0
		l.err = ErrTooLarge
		return n, l.err
	}
	l.remaining -= int64(n)
	if err != nil && err != io.EOF {
		l.err = l.wrap(err)
		return n, l.err
	}
	return n, err
}

func identity(err error) error { return err }

// downloadError tags an error from the HTTP body as a download failure, keeping ErrTooLarge and context errors
// recognizable with errors.Is.
func downloadError(err error) error {
	return fmt.Errorf("%w: %w", ErrDownloadFailed, err)
}

func wrapAsDownloadError(err error) error {
	if errors.Is(err, ErrDownloadFailed) || errors.Is(err, ErrTooLarge) {
		return err
	}
	return downloadError(err)
}
