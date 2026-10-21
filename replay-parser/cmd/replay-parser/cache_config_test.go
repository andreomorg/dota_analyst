package main

import "testing"

func TestCacheConfiguration(t *testing.T) {
	for _, name := range []string{"PORT", "MAX_CONCURRENT_PARSES", "MAX_DOWNLOAD_MB", "MAX_DECOMPRESSED_MB", "REQUEST_TIMEOUT", "REPLAY_CACHE_DIR", "MAX_CONCURRENT_PREFETCHES", "REPLAY_CACHE_MAX_BYTES"} {
		t.Setenv(name, "")
	}
	c, e := loadConfig()
	if e != nil {
		t.Fatal(e)
	}
	if c.cacheDir != "" || c.prefetches != 4 || c.cacheMax != 4294967296 {
		t.Fatalf("defaults: %+v", c)
	}
	t.Setenv("REPLAY_CACHE_DIR", t.TempDir())
	t.Setenv("MAX_CONCURRENT_PREFETCHES", "2")
	t.Setenv("REPLAY_CACHE_MAX_BYTES", "1024")
	c, e = loadConfig()
	if e != nil || c.cacheDir == "" || c.prefetches != 2 || c.cacheMax != 1024 {
		t.Fatalf("config %+v %v", c, e)
	}
	for _, name := range []string{"MAX_CONCURRENT_PREFETCHES", "REPLAY_CACHE_MAX_BYTES"} {
		t.Run(name, func(t *testing.T) {
			t.Setenv(name, "-1")
			if _, e := loadConfig(); e == nil {
				t.Fatal("invalid accepted")
			}
		})
	}
}
