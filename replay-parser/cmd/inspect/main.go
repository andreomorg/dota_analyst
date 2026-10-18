// Command inspect is a development tool: it reads a replay (.dem or .dem.bz2) and prints the properties of the
// entities the laning analysis needs, so we can see their exact names in the current Dota version. With -laning it
// prints each hero's numbers at 10:00 of the game clock; with -full it only measures a whole parse; -catalog prints an
// inventory of everything in the replay (combat log, messages, game events, entities, end state); -measure parses it
// with match-like collectors and prints time, memory and JSON sizes; -download saves a Valve replay (decompressed);
// -match writes the POST /v1/match document of the replay (to -out or the standard output).
//
//	go run ./cmd/inspect -replay ../work/replays/9034320024.dem.bz2 -match -out ../work/9034320024.json
//	go run ./cmd/inspect -replay ../work/replays/9034320024.dem.bz2 -filter "Hero|Gamerules|PlayerResource|Data(Radiant|Dire)"
//	go run ./cmd/inspect -replay ../work/replays/9034320024.dem.bz2 -laning
//	go run ./cmd/inspect -replay ../work/replays/9034320024.dem.bz2 -catalog > catalog.txt
//	go run ./cmd/inspect -replay ../work/replays/9034320024.dem.bz2 -measure -sizes-out ../work/sizes
//	go run ./cmd/inspect -download http://replay118.valve.net/570/<match>_<salt>.dem.bz2 -replay ../work/replays/<match>.dem
package main

import (
	"flag"
	"fmt"
	"log"
	"os"
	"regexp"
	"sort"

	"github.com/andreomorg/dota_analyst/replay-parser/internal/valve"
	"github.com/dotabuff/manta"
)

func main() {
	path := flag.String("replay", "", "path to the .dem or .dem.bz2 file")
	atTick := flag.Uint("tick", 30*60*15, "net tick at which the entities are printed (30 ticks per second)")
	classFilter := flag.String("filter", "Hero|Gamerules|PlayerResource|Data(Radiant|Dire)", "regexp on the entity class name")
	keyFilter := flag.String("keys", "", "regexp on the property name (empty = all)")
	laning := flag.Bool("laning", false, "print each hero's laning numbers at 10:00 instead of the properties (wins over -full)")
	full := flag.Bool("full", false, "parse the whole replay and print the time and memory it took")
	catalog := flag.Bool("catalog", false, "parse the whole replay and print an inventory of everything it carries")
	measure := flag.Bool("measure", false, "parse the whole replay with match-like collectors; print time, memory and JSON sizes")
	sizesOut := flag.String("sizes-out", "", "with -measure: also write the candidate JSON sections to this directory")
	download := flag.String("download", "", "download this Valve replay URL, decompressed, to the -replay path and exit")
	match := flag.Bool("match", false, "run the POST /v1/match analyzer and write its JSON (to -out, or the standard output)")
	out := flag.String("out", "", "with -match: file to write the JSON to")
	manifest := flag.String("manifest", "", "with -match and -out: publish a new local provenance sidecar")
	metrics := flag.String("metrics", "", "with -match: record Go memory samples in a new local JSON file")
	flag.Parse()
	if *metrics != "" && !*match {
		log.Fatal("-metrics requires -match")
	}
	if *manifest != "" && (!*match || *out == "" || *download != "") {
		log.Fatal("-manifest requires -match and -out, without -download")
	}

	if *download != "" {
		if err := downloadReplay(*download, *path); err != nil {
			log.Fatal(err)
		}
		return
	}

	file, err := os.Open(*path)
	if err != nil {
		log.Fatal(err)
	}
	defer file.Close()

	replay, err := valve.Decompress(file)
	if err != nil {
		log.Fatal(err)
	}
	defer replay.Close()

	if *match {
		if err := profileMatch(*metrics, func() error { return runMatch(replay, *out, *manifest, *path) }); err != nil {
			log.Fatal(err)
		}
		return
	}

	parser, err := manta.NewStreamParser(replay)
	if err != nil {
		log.Fatal(err)
	}

	switch {
	case *laning:
		runLaning(parser)
		return
	case *full:
		runFull(parser)
		return
	case *catalog:
		runCatalog(parser)
		return
	case *measure:
		runMeasure(parser, *sizesOut)
		return
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
