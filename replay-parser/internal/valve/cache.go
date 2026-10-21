package valve

import (
	"context"
	"crypto/sha256"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/parsing"
)

const DefaultCacheMaxBytes int64 = 4 << 30

// Cache stages compressed downloads; one instance owns its directory.
// Capacity reserves the maximum download size before opening a network connection.
type Cache struct {
	downloader *Downloader
	dir        string
	max        int64
	slots      chan struct{}
	mu         sync.Mutex
	pending    map[string]*flight
	reserved   int64
}
type flight struct {
	done  chan struct{}
	bytes int64
	err   error
}

// NewCache constructs a staging cache; an empty directory disables all disk access.
func NewCache(d *Downloader, dir string, max int64, concurrency int) (*Cache, error) {
	if max <= 0 {
		max = DefaultCacheMaxBytes
	}
	if concurrency <= 0 {
		concurrency = 4
	}
	c := &Cache{downloader: d, dir: dir, max: max, slots: make(chan struct{}, concurrency), pending: make(map[string]*flight)}
	if dir != "" {
		if err := os.MkdirAll(dir, 0700); err != nil {
			return nil, err
		}
	}
	return c, nil
}

func (c *Cache) key(raw string) (string, error) {
	u, err := c.downloader.parseURL(raw)
	if err != nil {
		return "", err
	}
	return fmt.Sprintf("%x.replay", sha256.Sum256([]byte(u.String()))), nil
}

// Prefetch downloads once per URL, outside parsing capacity. Disabled caches reject work.
func (c *Cache) Prefetch(ctx context.Context, raw string) (int64, error) {
	if c.dir == "" {
		return 0, parsing.ErrBusy
	}
	if err := ctx.Err(); err != nil {
		return 0, err
	}
	key, err := c.key(raw)
	if err != nil {
		return 0, err
	}
	c.mu.Lock()
	if f := c.pending[key]; f != nil {
		c.mu.Unlock()
		select {
		case <-ctx.Done():
			return 0, ctx.Err()
		case <-f.done:
			return f.bytes, f.err
		}
	}
	used, err := c.sweep()
	if err != nil {
		c.mu.Unlock()
		return 0, fmt.Errorf("%w: cache scan: %v", parsing.ErrDownloadFailed, err)
	}
	if info, err := os.Stat(filepath.Join(c.dir, key)); err == nil {
		c.mu.Unlock()
		return info.Size(), nil
	}
	if used+c.reserved > c.max-c.downloader.maxDownload {
		c.mu.Unlock()
		return 0, parsing.ErrBusy
	}
	select {
	case c.slots <- struct{}{}:
	default:
		c.mu.Unlock()
		return 0, parsing.ErrBusy
	}
	f := &flight{done: make(chan struct{})}
	c.pending[key] = f
	c.reserved += c.downloader.maxDownload
	c.mu.Unlock()
	n, err := c.download(ctx, raw, key)
	c.mu.Lock()
	f.bytes, f.err = n, err
	delete(c.pending, key)
	c.reserved -= c.downloader.maxDownload
	<-c.slots
	close(f.done)
	c.mu.Unlock()
	return n, err
}

func (c *Cache) download(ctx context.Context, raw, key string) (int64, error) {
	stream, err := c.downloader.OpenCompressed(ctx, raw)
	if err != nil {
		return 0, err
	}
	defer stream.Close()
	temp, err := os.CreateTemp(c.dir, ".prefetch-*")
	if err != nil {
		return 0, fmt.Errorf("%w: %v", parsing.ErrDownloadFailed, err)
	}
	defer os.Remove(temp.Name())
	n, copyErr := io.Copy(temp, stream)
	err = temp.Close()
	if copyErr != nil {
		return 0, wrapAsDownloadError(copyErr)
	}
	if err != nil {
		return 0, fmt.Errorf("%w: %v", parsing.ErrDownloadFailed, err)
	}
	if err = ctx.Err(); err != nil {
		return 0, err
	}
	if n == 0 {
		return 0, fmt.Errorf("%w: empty response", parsing.ErrDownloadFailed)
	}
	c.mu.Lock()
	err = os.Rename(temp.Name(), filepath.Join(c.dir, key))
	c.mu.Unlock()
	if err != nil {
		return 0, fmt.Errorf("%w: %v", parsing.ErrDownloadFailed, err)
	}
	return n, nil
}

// sweep runs under mu; temporary files belonging to this process are reserved separately.
func (c *Cache) sweep() (int64, error) {
	entries, err := os.ReadDir(c.dir)
	if err != nil {
		return 0, err
	}
	var used int64
	for _, entry := range entries {
		name := entry.Name()
		if !strings.HasSuffix(name, ".replay") && !strings.HasPrefix(name, ".prefetch-") {
			continue
		}
		info, err := entry.Info()
		if err != nil {
			return 0, err
		}
		if time.Since(info.ModTime()) > 30*time.Minute {
			if err = os.Remove(filepath.Join(c.dir, name)); err != nil {
				return 0, err
			}
			continue
		}
		used += info.Size()
	}
	return used, nil
}

// Open uses a completed cache entry, otherwise keeps the original streaming behavior.
// Closing a cached stream removes the staged file on both successful and failed parses.
func (c *Cache) Open(ctx context.Context, raw string) (io.ReadCloser, error) {
	if c.dir == "" {
		return c.downloader.Open(ctx, raw)
	}
	key, err := c.key(raw)
	if err != nil {
		return nil, err
	}
	path := filepath.Join(c.dir, key)
	c.mu.Lock()
	file, err := os.Open(path)
	if err == nil {
		info, statErr := file.Stat()
		if statErr != nil || time.Since(info.ModTime()) > 30*time.Minute {
			file.Close()
			os.Remove(path)
			err = os.ErrNotExist
		}
	}
	c.mu.Unlock()
	if os.IsNotExist(err) {
		return c.downloader.Open(ctx, raw)
	}
	if err != nil {
		return nil, fmt.Errorf("%w: %v", parsing.ErrDownloadFailed, err)
	}
	info, statErr := file.Stat()
	if statErr != nil {
		file.Close()
		return nil, fmt.Errorf("%w: %v", parsing.ErrDownloadFailed, statErr)
	}
	stream, err := c.downloader.openCompressed(file)
	if err != nil {
		os.Remove(path)
		return nil, err
	}
	return &cachedStream{ReadCloser: stream, path: path, cache: c, info: info}, nil
}

type cachedStream struct {
	cache *Cache
	info  os.FileInfo
	io.ReadCloser
	path string
}

func (s *cachedStream) Close() error {
	err := s.ReadCloser.Close()
	s.cache.mu.Lock()
	defer s.cache.mu.Unlock()
	if current, statErr := os.Stat(s.path); statErr == nil && os.SameFile(current, s.info) {
		os.Remove(s.path)
	}
	return err
}

// Cleanup removes abandoned entries. The process calls it periodically even while idle.
func (c *Cache) Cleanup() error {
	if c.dir == "" {
		return nil
	}
	c.mu.Lock()
	defer c.mu.Unlock()
	_, err := c.sweep()
	return err
}
