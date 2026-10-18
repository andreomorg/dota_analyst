package architecture

import (
	"go/parser"
	"go/token"
	"io/fs"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
)

// Check imports directly: internal/ visibility alone does not enforce dependency direction.
func TestPackageBoundaries(t *testing.T) {
	for _, name := range []string{"laning", "matchdata", "parsing", "httpapi"} {
		err := filepath.WalkDir(filepath.Join("..", name), func(path string, d fs.DirEntry, err error) error {
			if err != nil {
				return err
			}
			if d.IsDir() || !strings.HasSuffix(path, ".go") || strings.HasSuffix(path, "_test.go") {
				return nil
			}
			file, err := parser.ParseFile(token.NewFileSet(), path, nil, parser.ImportsOnly)
			if err != nil {
				return err
			}
			for _, item := range file.Imports {
				importPath, err := strconv.Unquote(item.Path.Value)
				if err != nil {
					return err
				}
				if (name == "laning" || name == "matchdata") && strings.Contains(strings.Split(importPath, "/")[0], ".") {
					t.Errorf("%s must use only standard library: %s", path, importPath)
				}
				if name == "parsing" && (strings.Contains(importPath, "/internal/httpapi") || strings.Contains(importPath, "/internal/valve") || strings.Contains(importPath, "/internal/replay") || strings.Contains(importPath, "github.com/dotabuff/manta")) {
					t.Errorf("%s depends on adapter %s", path, importPath)
				}
				if name == "httpapi" && (strings.Contains(importPath, "/internal/valve") || strings.Contains(importPath, "/internal/replay") || strings.Contains(importPath, "github.com/dotabuff/manta")) {
					t.Errorf("%s depends on adapter %s", path, importPath)
				}
			}
			return nil
		})
		if err != nil {
			t.Fatal(err)
		}
	}
}
