package main

import (
	"encoding/json"
	"os"
	"runtime"
	"strconv"
	"strings"
	"time"
)

// profileMatch samples Go-managed memory, not RSS or a production capacity guarantee.
func profileMatch(destination string, operation func() error) error {
	if destination == "" {
		return operation()
	}
	f, err := os.OpenFile(destination, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0600)
	if err != nil {
		return err
	}
	defer f.Close()
	done := make(chan struct{})
	result := make(chan map[string]any, 1)
	started := time.Now()
	go func() {
		ticker := time.NewTicker(time.Millisecond)
		defer ticker.Stop()
		var peakHeap, peakSys uint64
		sample := func() {
			var m runtime.MemStats
			runtime.ReadMemStats(&m)
			peakHeap = max(peakHeap, m.HeapInuse)
			peakSys = max(peakSys, m.Sys)
		}
		for {
			select {
			case <-ticker.C:
				sample()
			case <-done:
				sample()
				result <- map[string]any{"schemaVersion": 1, "elapsedSeconds": time.Since(started).Seconds(), "sampleIntervalMilliseconds": 1,
					"peakHeapInuseBytes": peakHeap, "peakRuntimeSysBytes": peakSys, "measuresRSS": false}
				return
			}
		}
	}()
	err = operation()
	close(done)
	measurement := <-result
	measurement["success"] = err == nil
	if raw, readErr := os.ReadFile("/sys/fs/cgroup/memory.peak"); readErr == nil {
		if peak, parseErr := strconv.ParseUint(strings.TrimSpace(string(raw)), 10, 64); parseErr == nil {
			measurement["cgroupPeakBytes"] = peak
		}
	}
	if writeErr := json.NewEncoder(f).Encode(measurement); writeErr != nil {
		return writeErr
	}
	return err
}
