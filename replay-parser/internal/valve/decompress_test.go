package valve

import (
	"bytes"
	"compress/bzip2"
	"encoding/binary"
	"errors"
	"io"
	"os"
	"testing"

	"github.com/klauspost/compress/zstd"
)

func TestDecompress(t *testing.T) {
	content := []byte("PBDEMS2\x00 replay bytes")

	zstdEncoded := func() []byte {
		var buffer bytes.Buffer
		encoder, err := zstd.NewWriter(&buffer)
		if err != nil {
			t.Fatal(err)
		}
		if _, err := encoder.Write(content); err != nil {
			t.Fatal(err)
		}
		if err := encoder.Close(); err != nil {
			t.Fatal(err)
		}
		return buffer.Bytes()
	}()

	tests := []struct {
		name  string
		input []byte
	}{
		{"zstd (what Valve serves as .dem.bz2)", zstdEncoded},
		{"plain .dem", content},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			reader, err := Decompress(bytes.NewReader(test.input))
			if err != nil {
				t.Fatal(err)
			}
			defer reader.Close()
			got, err := io.ReadAll(reader)
			if err != nil {
				t.Fatal(err)
			}
			if !bytes.Equal(got, content) {
				t.Errorf("got %q, want %q", got, content)
			}
		})
	}
}

// The standard library has no bzip2 writer, so only the detection is checked: a bzip2 header must not be read as
// plain bytes.
func TestDecompress_Bzip2HeaderUsesBzip2(t *testing.T) {
	reader, err := Decompress(bytes.NewReader([]byte("BZh9 not really bzip2")))
	if err != nil {
		t.Fatal(err)
	}
	defer reader.Close()
	if _, err := io.ReadAll(reader); err == nil {
		t.Fatal("expected a bzip2 error for an invalid bzip2 stream, got plain bytes")
	} else if _, ok := err.(bzip2.StructuralError); !ok {
		t.Fatalf("expected bzip2.StructuralError, got %T: %v", err, err)
	}
}

// zstdWithWindow compresses content with the encoder declaring the given window in the frame header.
func zstdWithWindow(t *testing.T, window int, content []byte) []byte {
	t.Helper()
	var buffer bytes.Buffer
	encoder, err := zstd.NewWriter(&buffer, zstd.WithWindowSize(window), zstd.WithEncoderConcurrency(1))
	if err != nil {
		t.Fatal(err)
	}
	if _, err := encoder.Write(content); err != nil {
		t.Fatal(err)
	}
	// Flush writes the frame header before the encoder knows the content is small (on Close alone it would shrink
	// the window to fit), like a streaming encoder of a real replay.
	if err := encoder.Flush(); err != nil {
		t.Fatal(err)
	}
	if err := encoder.Close(); err != nil {
		t.Fatal(err)
	}
	var header zstd.Header
	if err := header.Decode(buffer.Bytes()); err != nil {
		t.Fatal(err)
	}
	if header.WindowSize != uint64(window) {
		t.Fatalf("frame header declares a %d byte window, want %d", header.WindowSize, window)
	}
	return buffer.Bytes()
}

// A hostile stream must not make the decoder allocate whatever window its header asks for.
func TestDecompress_RejectsZstdWindowAboveTheLimit(t *testing.T) {
	// Single-segment frame (FHD 0xE0: 8-byte content size, no window descriptor) declaring 1 GB of content: the window
	// of such a frame is the whole content.
	singleSegment := append([]byte{0x28, 0xB5, 0x2F, 0xFD, 0xE0}, binary.LittleEndian.AppendUint64(nil, 1<<30)...)

	tests := []struct {
		name  string
		input []byte
	}{
		{"window of twice the limit", zstdWithWindow(t, 2*MaxZstdWindowBytes, []byte("PBDEMS2\x00"))},
		{"single-segment frame of 1 GB", singleSegment},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			reader, err := Decompress(bytes.NewReader(test.input))
			if err != nil {
				return // rejected up front: also fine
			}
			defer reader.Close()
			_, err = io.ReadAll(reader)
			if !errors.Is(err, zstd.ErrWindowSizeExceeded) && !errors.Is(err, zstd.ErrDecoderSizeExceeded) {
				t.Fatalf("err = %v, want the window to be refused", err)
			}
		})
	}
}

// The limit is on the window, not on the output: a stream bigger than the limit (real replays of long matches are
// over 64 MB decompressed) still decodes.
func TestDecompress_OutputLargerThanTheWindowLimit(t *testing.T) {
	content := bytes.Repeat([]byte("PBDEMS2\x00"), (MaxZstdWindowBytes+(1<<20))/8)
	reader, err := Decompress(bytes.NewReader(zstdWithWindow(t, 4<<20, content)))
	if err != nil {
		t.Fatal(err)
	}
	defer reader.Close()
	n, err := io.Copy(io.Discard, reader)
	if err != nil {
		t.Fatal(err)
	}
	if n != int64(len(content)) {
		t.Errorf("decoded %d bytes, want %d", n, len(content))
	}
}

// The real replay (when it is on this machine) declares a 4 MB window and must decode in full under the limit.
func TestDecompress_RealReplay(t *testing.T) {
	path := os.Getenv("REPLAY_TEST_FILE")
	if path == "" {
		path = "../../../work/replays/9034320024.dem.bz2"
	}
	file, err := os.Open(path)
	if errors.Is(err, os.ErrNotExist) {
		t.Skipf("replay %s not found (set REPLAY_TEST_FILE to run this test)", path)
	}
	if err != nil {
		t.Fatal(err)
	}
	defer file.Close()

	reader, err := Decompress(file)
	if err != nil {
		t.Fatal(err)
	}
	defer reader.Close()
	n, err := io.Copy(io.Discard, reader)
	if err != nil {
		t.Fatal(err)
	}
	t.Logf("decompressed %d bytes", n)
}

func TestDecompress_EmptyInput(t *testing.T) {
	if _, err := Decompress(bytes.NewReader(nil)); err == nil {
		t.Fatal("expected an error for an empty stream")
	}
}
