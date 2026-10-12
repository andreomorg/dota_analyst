// Package valve gets replays from Valve's CDN: download limits, the *.valve.net allow list and decompression.
package valve

import (
	"bufio"
	"bytes"
	"compress/bzip2"
	"io"

	"github.com/klauspost/compress/zstd"
)

var (
	zstdMagic  = []byte{0x28, 0xB5, 0x2F, 0xFD}
	bzip2Magic = []byte("BZh")
)

// Decompress picks the decoder by the stream's first bytes, not the file name: Valve serves zstd replays named
// .dem.bz2, and older replays are bzip2. Anything else is returned as is (a plain .dem). Close releases the zstd
// decoder; it does not close r.
func Decompress(r io.Reader) (io.ReadCloser, error) {
	buffered := bufio.NewReader(r)
	magic, err := buffered.Peek(len(zstdMagic))
	if err != nil && len(magic) == 0 {
		return nil, err
	}
	switch {
	case bytes.HasPrefix(magic, zstdMagic):
		decoder, err := zstd.NewReader(buffered, zstd.WithDecoderConcurrency(1))
		if err != nil {
			return nil, err
		}
		return decoder.IOReadCloser(), nil
	case bytes.HasPrefix(magic, bzip2Magic):
		return io.NopCloser(bzip2.NewReader(buffered)), nil
	default:
		return io.NopCloser(buffered), nil
	}
}
