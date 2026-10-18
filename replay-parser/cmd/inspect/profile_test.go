package main

import (
	"errors"
	"os"
	"path/filepath"
	"testing"
)

func TestProfilePreservesOperationFailureAndNeverOverwrites(t *testing.T) {
	path := filepath.Join(t.TempDir(), "metrics.json")
	want := errors.New("synthetic failure")
	if got := profileMatch(path, func() error { return want }); !errors.Is(got, want) {
		t.Fatalf("error = %v", got)
	}
	before, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	if err = profileMatch(path, func() error { t.Fatal("existing metrics overwritten"); return nil }); err == nil {
		t.Fatal("overwrite accepted")
	}
	after, _ := os.ReadFile(path)
	if string(before) != string(after) {
		t.Fatal("metrics changed")
	}
}
