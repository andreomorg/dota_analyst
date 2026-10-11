// Command inspect is a development tool: it reads a replay (.dem or .dem.bz2) and prints the properties of the
// entities the laning analysis needs, so we can see their exact names in the current Dota version.
//
//	go run ./cmd/inspect -replay ../work/replays/9034320024.dem.bz2 -filter "Hero|Gamerules|PlayerResource|Data(Radiant|Dire)"
package main

import (
	"compress/bzip2"
	"flag"
	"fmt"
	"io"
	"log"
	"os"
	"regexp"
	"sort"
	"strings"

	"github.com/dotabuff/manta"
)

func main() {
	path := flag.String("replay", "", "path to the .dem or .dem.bz2 file")
	atTick := flag.Uint("tick", 30*60*15, "net tick at which the entities are printed (30 ticks per second)")
	classFilter := flag.String("filter", "Hero|Gamerules|PlayerResource|Data(Radiant|Dire)", "regexp on the entity class name")
	keyFilter := flag.String("keys", "", "regexp on the property name (empty = all)")
	flag.Parse()

	file, err := os.Open(*path)
	if err != nil {
		log.Fatal(err)
	}
	defer file.Close()

	var replay io.Reader = file
	if strings.HasSuffix(*path, ".bz2") {
		replay = bzip2.NewReader(file)
	}

	parser, err := manta.NewStreamParser(replay)
	if err != nil {
		log.Fatal(err)
	}

	classes := regexp.MustCompile(*classFilter)
	keys := regexp.MustCompile(*keyFilter)
	printed := false

	parser.OnEntity(func(e *manta.Entity, op manta.EntityOp) error {
		if printed || parser.NetTick < uint32(*atTick) {
			return nil
		}
		printed = true

		for _, entity := range parser.FilterEntity(func(e *manta.Entity) bool { return classes.MatchString(e.GetClassName()) }) {
			fmt.Printf("=== %s (index %d)\n", entity.GetClassName(), entity.GetIndex())
			properties := entity.Map()
			names := make([]string, 0, len(properties))
			for name := range properties {
				if keys.MatchString(name) {
					names = append(names, name)
				}
			}
			sort.Strings(names)
			for _, name := range names {
				fmt.Printf("  %s = %v\n", name, properties[name])
			}
		}

		parser.Stop()
		return nil
	})

	if err := parser.Start(); err != nil {
		log.Fatal(err)
	}
	fmt.Printf("stopped at net tick %d\n", parser.NetTick)
}
