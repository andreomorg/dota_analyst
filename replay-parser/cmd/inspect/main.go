// Command inspect is a development tool: it reads a replay (.dem or .dem.bz2) and prints the properties of the
// entities the laning analysis needs, so we can see their exact names in the current Dota version. With -laning it
// prints each hero's numbers at 10:00 of the game clock; with -full it only measures a whole parse.
//
//	go run ./cmd/inspect -replay ../work/replays/9034320024.dem.bz2 -filter "Hero|Gamerules|PlayerResource|Data(Radiant|Dire)"
//	go run ./cmd/inspect -replay ../work/replays/9034320024.dem.bz2 -laning
package main

import (
	"bufio"
	"bytes"
	"compress/bzip2"
	"flag"
	"fmt"
	"io"
	"log"
	"os"
	"regexp"
	"sort"

	"github.com/dotabuff/manta"
	"github.com/klauspost/compress/zstd"
)

func main() {
	path := flag.String("replay", "", "path to the .dem or .dem.bz2 file")
	atTick := flag.Uint("tick", 30*60*15, "net tick at which the entities are printed (30 ticks per second)")
	classFilter := flag.String("filter", "Hero|Gamerules|PlayerResource|Data(Radiant|Dire)", "regexp on the entity class name")
	keyFilter := flag.String("keys", "", "regexp on the property name (empty = all)")
	laning := flag.Bool("laning", false, "print each hero's laning numbers at 10:00 instead of the properties (wins over -full)")
	full := flag.Bool("full", false, "parse the whole replay and print the time and memory it took")
	flag.Parse()

	file, err := os.Open(*path)
	if err != nil {
		log.Fatal(err)
	}
	defer file.Close()

	replay, err := decompress(bufio.NewReader(file))
	if err != nil {
		log.Fatal(err)
	}
	defer replay.Close()

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

// decompress picks the decoder by the file's magic bytes, not its name: Valve serves zstd replays named .dem.bz2.
// Close releases the zstd decoder's goroutines; stopping the parser early does not.
func decompress(r *bufio.Reader) (io.ReadCloser, error) {
	magic, err := r.Peek(4)
	if err != nil {
		return nil, err
	}
	switch {
	case bytes.Equal(magic, []byte{0x28, 0xB5, 0x2F, 0xFD}):
		decoder, err := zstd.NewReader(r, zstd.WithDecoderConcurrency(1))
		if err != nil {
			return nil, err
		}
		return decoder.IOReadCloser(), nil
	case bytes.HasPrefix(magic, []byte("BZh")):
		return io.NopCloser(bzip2.NewReader(r)), nil
	default:
		return io.NopCloser(r), nil // plain .dem
	}
}
