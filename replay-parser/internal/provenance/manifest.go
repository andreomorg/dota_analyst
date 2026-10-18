// Package provenance publishes local sidecars without changing the v1 match document.
package provenance

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"runtime/debug"
	"time"
)

type Manifest struct {
	SchemaVersion         int               `json:"schemaVersion"`
	MatchID               string            `json:"matchId"`
	DocumentSchemaVersion int               `json:"documentSchemaVersion"`
	DocumentSHA256        string            `json:"documentSha256"`
	SourceSHA256          string            `json:"sourceSha256"`
	BinarySHA256          string            `json:"binarySha256"`
	CreatedAt             time.Time         `json:"createdAt"`
	Build                 map[string]string `json:"build"`
	PatchID               *int              `json:"patchId"`
	Coverage              string            `json:"coverage"`
}

func hash(path string) (string, error) {
	f, err := os.Open(path)
	if err != nil {
		return "", err
	}
	defer f.Close()
	h := sha256.New()
	if _, err = io.Copy(h, f); err != nil {
		return "", err
	}
	return hex.EncodeToString(h.Sum(nil)), nil
}

// WriteNew hashes exact local bytes and atomically publishes a new manifest. It never overwrites evidence.
// The sidecar describes extraction identity, not tactical accuracy or observed coverage of empty sections.
func WriteNew(destination, document, source, matchID string, schema int) error {
	if destination == "" || document == "" || source == "" || matchID == "" || schema <= 0 {
		return fmt.Errorf("manifest requires document, source and match identity")
	}
	documentHash, err := hash(document)
	if err != nil {
		return err
	}
	sourceHash, err := hash(source)
	if err != nil {
		return err
	}
	executable, err := os.Executable()
	if err != nil {
		return err
	}
	binaryHash, err := hash(executable)
	if err != nil {
		return err
	}
	build := map[string]string{}
	if info, ok := debug.ReadBuildInfo(); ok {
		build["goVersion"] = info.GoVersion
		build["moduleVersion"] = info.Main.Version
		for _, dep := range info.Deps {
			if dep.Path == "github.com/dotabuff/manta" {
				build["mantaVersion"] = dep.Version
			}
		}
		for _, setting := range info.Settings {
			switch setting.Key {
			case "vcs.revision", "vcs.modified":
				build[setting.Key] = setting.Value
			}
		}
	}
	value := Manifest{SchemaVersion: 1, MatchID: matchID, DocumentSchemaVersion: schema, DocumentSHA256: documentHash,
		SourceSHA256: sourceHash, BinarySHA256: binaryHash, CreatedAt: time.Now().UTC(), Build: build,
		Coverage: "legacy-v1: empty sections do not independently certify observed coverage"}
	temp, err := os.CreateTemp(filepath.Dir(destination), ".manifest-*")
	if err != nil {
		return err
	}
	defer os.Remove(temp.Name())
	defer temp.Close()
	if err = json.NewEncoder(temp).Encode(value); err != nil {
		return err
	}
	if err = temp.Sync(); err != nil {
		return err
	}
	if err = temp.Close(); err != nil {
		return err
	}
	return os.Link(temp.Name(), destination)
}
