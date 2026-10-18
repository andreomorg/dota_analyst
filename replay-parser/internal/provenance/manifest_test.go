package provenance

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestManifestPreservesEvidenceAndRejectsOverwrite(t *testing.T) {
	dir := t.TempDir()
	document := filepath.Join(dir, "match.json")
	source := filepath.Join(dir, "source.dem")
	if err := os.WriteFile(document, []byte("{\"schemaVersion\":1}\n"), 0600); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(source, []byte("local replay"), 0600); err != nil {
		t.Fatal(err)
	}
	destination := filepath.Join(dir, "manifest.json")
	if err := WriteNew(destination, document, source, "123", 1); err != nil {
		t.Fatal(err)
	}
	raw, err := os.ReadFile(destination)
	if err != nil {
		t.Fatal(err)
	}
	var value Manifest
	if err = json.Unmarshal(raw, &value); err != nil {
		t.Fatal(err)
	}
	expected, _ := hash(document)
	if value.DocumentSHA256 != expected || value.PatchID != nil || len(value.BinarySHA256) != 64 || value.CreatedAt.IsZero() {
		t.Fatalf("invalid manifest: %+v", value)
	}
	if strings.Contains(string(raw), dir) {
		t.Fatal("local paths leaked")
	}
	if err = WriteNew(destination, document, source, "456", 1); err == nil {
		t.Fatal("manifest overwritten")
	}
	after, _ := os.ReadFile(destination)
	if string(after) != string(raw) {
		t.Fatal("existing evidence changed")
	}
}
