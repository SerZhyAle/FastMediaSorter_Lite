package wire

import (
	"bytes"
	"encoding/hex"
	"errors"
	"io"
	"strings"
	"testing"
	"testing/iotest"
)

// Ticket 165 rule 2 and DEVICE-EXCHANGE item U: the chunk framing.

func TestChunkEveryFlagCombination(t *testing.T) {
	for flags := 0; flags < 256; flags++ {
		b, err := AppendChunk(nil, byte(flags), []byte("abc"))
		defined := byte(flags)&^(ChunkJoinable|ChunkHeader) == 0
		if defined != (err == nil) {
			t.Fatalf("AppendChunk flags %#x: err = %v", flags, err)
		}
		if err != nil {
			if !errors.Is(err, ErrChunkFlags) {
				t.Fatalf("flags %#x: %v", flags, err)
			}
			// the reader refuses the same byte when it arrives on the wire
			raw := []byte{0, 0, 0, 4, byte(flags), 'a', 'b', 'c'}
			if _, rerr := ReadChunk(bytes.NewReader(raw)); !errors.Is(rerr, ErrChunkFlags) {
				t.Fatalf("ReadChunk flags %#x: %v", flags, rerr)
			}
			continue
		}
		c, err := ReadChunk(bytes.NewReader(b))
		if err != nil || c.Flags != byte(flags) || string(c.Payload) != "abc" {
			t.Fatalf("flags %#x round trip: %+v %v", flags, c, err)
		}
		wantJoin := flags&1 != 0 && flags&2 == 0
		if c.Joinable() != wantJoin || c.IsHeader() != (flags&2 != 0) {
			t.Fatalf("flags %#x: joinable=%v header=%v", flags, c.Joinable(), c.IsHeader())
		}
	}
}

func TestChunkWireBytes(t *testing.T) {
	b, _ := AppendChunk(nil, ChunkJoinable, []byte{0xAA, 0xBB})
	if got := hex.EncodeToString(b); got != "0000000301aabb" {
		t.Fatalf("the length counts the flag byte plus the payload: %s", got)
	}
	if got := hex.EncodeToString(EndChunk()); got != "00000000" {
		t.Fatalf("the end of a broadcast is a length of zero: %s", got)
	}
	b, _ = AppendChunk(nil, ChunkHeader, nil)
	if got := hex.EncodeToString(b); got != "0000000102" {
		t.Fatalf("an empty payload is a chunk of the flag byte alone: %s", got)
	}
}

func TestChunkLengthZeroEndsTheBroadcast(t *testing.T) {
	if _, err := ReadChunk(bytes.NewReader(EndChunk())); !errors.Is(err, ErrChunkEnd) {
		t.Fatalf("%v", err)
	}
}

// the bound is on the LENGTH FIELD: flag byte plus payload at most 1048576 (item U)
func TestChunkBoundIsOnTheLengthField(t *testing.T) {
	atBound := bytes.Repeat([]byte{7}, MaxChunkLen-1)
	b, err := AppendChunk(nil, ChunkJoinable, atBound)
	if err != nil {
		t.Fatalf("a payload of 1048575 bytes is the largest a chunk carries: %v", err)
	}
	c, err := ReadChunk(bytes.NewReader(b))
	if err != nil || len(c.Payload) != MaxChunkLen-1 {
		t.Fatalf("%v %d", err, len(c.Payload))
	}
	if _, err := AppendChunk(nil, ChunkJoinable, append(atBound, 1)); !errors.Is(err, ErrChunkSize) {
		t.Fatalf("a payload of 1 MiB makes the length 1048577: %v", err)
	}
	// one above the bound on the wire: refused before any payload is read or allocated
	over := []byte{0x00, 0x10, 0x00, 0x01, 0x01}
	r := &countingReader{r: io.MultiReader(bytes.NewReader(over), bytes.NewReader(bytes.Repeat([]byte{1}, 2<<20)))}
	if _, err := ReadChunk(r); !errors.Is(err, ErrChunkSize) {
		t.Fatalf("%v", err)
	}
	if r.n > 4 {
		t.Fatalf("a chunk over the bound must be refused after its length alone, %d bytes were read", r.n)
	}
	// a length that cannot be a chunk at all
	if _, err := ReadChunk(bytes.NewReader([]byte{0xff, 0xff, 0xff, 0xff})); !errors.Is(err, ErrChunkSize) {
		t.Fatalf("%v", err)
	}
}

type countingReader struct {
	r io.Reader
	n int
}

func (c *countingReader) Read(p []byte) (int, error) {
	n, err := c.r.Read(p)
	c.n += n
	return n, err
}

func TestChunkSplitAcrossReads(t *testing.T) {
	payload := bytes.Repeat([]byte("0123456789"), 1000)
	b, _ := AppendChunk(nil, ChunkJoinable, payload)
	b, _ = AppendChunk(b, 0, []byte("second"))
	// one byte at a time, and halved reads
	for _, r := range []io.Reader{iotest.OneByteReader(bytes.NewReader(b)), iotest.HalfReader(bytes.NewReader(b)), iotest.DataErrReader(bytes.NewReader(b))} {
		c1, err := ReadChunk(r)
		if err != nil || !bytes.Equal(c1.Payload, payload) || c1.Flags != ChunkJoinable {
			t.Fatalf("first chunk: %v", err)
		}
		c2, err := ReadChunk(r)
		if err != nil || string(c2.Payload) != "second" || c2.Flags != 0 {
			t.Fatalf("second chunk: %v", err)
		}
	}
}

func TestChunkTruncation(t *testing.T) {
	b, _ := AppendChunk(nil, ChunkJoinable, []byte("hello"))
	for cut := 0; cut < len(b); cut++ {
		_, err := ReadChunk(bytes.NewReader(b[:cut]))
		if err == nil {
			t.Fatalf("a chunk cut at %d of %d was accepted", cut, len(b))
		}
		if cut > 0 && cut < len(b) && err != io.ErrUnexpectedEOF && err != io.EOF {
			t.Fatalf("cut %d: %v", cut, err)
		}
	}
}

func TestContentTypePattern(t *testing.T) {
	for _, ok := range []string{"audio/aac", "video/mp2t", "application/vnd.x+y", "audio/x-m4a"} {
		if !ValidContentType(ok) {
			t.Errorf("%q must be valid", ok)
		}
	}
	for _, bad := range []string{"", "audio", "Audio/aac", "audio/", "audio/aac\r\nX: y", "audio/aac; charset=x", "a/b c",
		"audio/" + strings.Repeat("a", 61), " audio/aac", "audio/aac\n", "au dio/aac"} {
		if ValidContentType(bad) {
			t.Errorf("%q must be refused: it would be copied into a response header", bad)
		}
	}
	// the envelope refuses it too: the stream closes
	body := `{"schemaVersion":2,"type":"upload","broadcastId":"` + idA + `","contentType":"audio/aac\r\nX-Evil: 1"}`
	if _, err := Decode([]byte(body)); !errors.Is(err, ErrMalformed) {
		t.Fatalf("an upload with a header-injecting content type must be malformed: %v", err)
	}
	ok := `{"schemaVersion":2,"type":"upload","broadcastId":"` + idA + `","contentType":"audio/aac"}`
	if _, err := Decode([]byte(ok)); err != nil {
		t.Fatal(err)
	}
}

func TestHTTPListenFraming(t *testing.T) {
	head := string(ListenResponseHead("video/mp2t"))
	for _, want := range []string{"HTTP/1.1 200 OK\r\n", "Content-Type: video/mp2t\r\n", "Transfer-Encoding: chunked\r\n",
		"Cache-Control: no-cache\r\n", "Accept-Ranges: none\r\n"} {
		if !strings.Contains(head, want) {
			t.Errorf("the 200 lacks %q:\n%s", want, head)
		}
	}
	if strings.Contains(strings.ToLower(head), "icy-metaint") || !strings.HasSuffix(head, "\r\n\r\n") {
		t.Errorf("no icy-metaint, and the head ends with a blank line:\n%s", head)
	}
	if got := string(AppendHTTPChunkHead(nil, 255)); got != "ff\r\n" {
		t.Errorf("chunk size line: %q", got)
	}
	if string(HTTPFinalChunk) != "0\r\n\r\n" || string(HTTPChunkTail) != "\r\n" {
		t.Error("final chunk and tail")
	}
	if got := string(HTTPResponse(505, 0)); !strings.HasPrefix(got, "HTTP/1.1 505 HTTP Version Not Supported\r\n") {
		t.Errorf("505: %q", got)
	}
}

func FuzzReadChunk(f *testing.F) {
	for _, seed := range [][]byte{{0, 0, 0, 0}, {0, 0, 0, 1, 1}, {0, 0, 0, 3, 2, 'a', 'b'}, {0, 0x10, 0, 1, 1}, {0, 0, 0, 2, 4, 1}} {
		f.Add(seed)
	}
	f.Fuzz(func(t *testing.T, in []byte) {
		r := bytes.NewReader(in)
		for {
			c, err := ReadChunk(r)
			if err != nil {
				return
			}
			if c.Flags&^(ChunkJoinable|ChunkHeader) != 0 || len(c.Payload)+1 > MaxChunkLen {
				t.Fatalf("the reader returned a chunk it must have refused: %+v", c)
			}
		}
	})
}
