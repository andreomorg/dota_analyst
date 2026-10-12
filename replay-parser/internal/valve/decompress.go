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

// MaxZstdWindowBytes caps the window a zstd stream may declare. The decoder keeps a buffer of about twice the window
// in memory, and the frame header chooses it (up to 512 MB by the library's default, or the whole content for a
// single-segment frame): without a cap, a small hostile file could make the service allocate gigabytes before the
// MaxDecompressedBytes limit sees a single byte. Measured on 11/10/2026: the real replay of match 9034320024 (32 MB
// compressed, 56 MB decompressed) declares a 4 MB window (zstd.Header.WindowSize = 4 194 304), so 64 MB leaves 16x
// of room.
const MaxZstdWindowBytes = 64 << 20

// Decompress picks the decoder by the stream's first bytes, not the file name: Valve serves zstd replays named
// .dem.bz2, and older replays are bzip2. Anything else is returned as is (a plain .dem). Close releases the zstd
// decoder; it does not close r. A zstd stream declaring a window larger than MaxZstdWindowBytes fails on Read.
func Decompress(r io.Reader) (io.ReadCloser, error) {
	buffered := bufio.NewReader(r)
	magic, err := buffered.Peek(len(zstdMagic))
	if err != nil && len(magic) == 0 {
		return nil, err
	}
	switch {
	case bytes.HasPrefix(magic, zstdMagic):
		// When streaming, MaxMemory is also a window cap (and the one that applies to single-segment frames, whose
		// window is the whole content); both are set so neither default (512 MB / 64 GB) is left open.
		decoder, err := zstd.NewReader(buffered, zstd.WithDecoderConcurrency(1),
			zstd.WithDecoderMaxWindow(MaxZstdWindowBytes), zstd.WithDecoderMaxMemory(MaxZstdWindowBytes))
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
