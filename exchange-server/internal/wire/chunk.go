package wire

import (
	"encoding/binary"
	"errors"
	"io"
	"regexp"
	"strconv"
	"strings"
)

// The relay's chunk framing (DEVICE-EXCHANGE 7.6 and item U, ticket 165 rule 2). After
// `upload-accepted` the broadcaster's stream carries chunks, not envelopes: a 4-byte unsigned
// big-endian length that counts ONE FLAG BYTE PLUS THE PAYLOAD, the flag byte, the payload. This file
// is the only place that builds or parses one, and the only place that writes the HTTP chunked
// framing of a listener's body.

const (
	// ChunkJoinable (flag 0x01): a listener may start here.
	ChunkJoinable byte = 0x01
	// ChunkHeader (flag 0x02): a header chunk, kept and replayed to every new listener before its
	// first joinable chunk; a new one replaces the old one. One header chunk holds PAT and PMT together.
	ChunkHeader byte = 0x02

	// MaxChunkLen is the bound of item U, on the VALUE OF THE LENGTH FIELD: the flag byte plus the
	// payload is at most 1048576 (1 MiB), so a payload is at most 1048575 bytes.
	MaxChunkLen = 1 << 20
)

// Errors of the chunk reader. Every one but ErrChunkEnd ends the upload stream as a protocol
// violation; ErrChunkEnd is the broadcaster's clean end.
var (
	// ErrChunkEnd: a length of zero ends the broadcast cleanly.
	ErrChunkEnd = errors.New("wire: zero-length chunk ends the broadcast")
	// ErrChunkSize: a length above MaxChunkLen.
	ErrChunkSize = errors.New("wire: chunk length above 1 MiB")
	// ErrChunkFlags: a flag byte with a bit set other than 0x01 and 0x02.
	ErrChunkFlags = errors.New("wire: chunk flag byte has an undefined bit")
)

// Chunk is one unit of an upload.
type Chunk struct {
	Flags   byte
	Payload []byte
}

// Joinable reports whether a listener may start at this chunk. A header chunk is never a start: it
// is replayed to a listener before the first joinable chunk it starts at.
func (c Chunk) Joinable() bool { return c.Flags&ChunkJoinable != 0 && c.Flags&ChunkHeader == 0 }

// IsHeader reports whether this is a header chunk.
func (c Chunk) IsHeader() bool { return c.Flags&ChunkHeader != 0 }

// ReadChunk reads one chunk. It reads exactly the bytes of that chunk (a payload split across reads
// is reassembled) and returns ErrChunkEnd for a length of zero, ErrChunkSize for a length above the
// bound (nothing of the payload is read or allocated) and ErrChunkFlags for an undefined flag bit.
func ReadChunk(r io.Reader) (Chunk, error) {
	var hdr [4]byte
	if _, err := io.ReadFull(r, hdr[:]); err != nil {
		return Chunk{}, err
	}
	n := binary.BigEndian.Uint32(hdr[:])
	switch {
	case n == 0:
		return Chunk{}, ErrChunkEnd
	case n > MaxChunkLen:
		return Chunk{}, ErrChunkSize
	}
	buf := make([]byte, n)
	if _, err := io.ReadFull(r, buf); err != nil {
		if err == io.EOF {
			err = io.ErrUnexpectedEOF
		}
		return Chunk{}, err
	}
	if buf[0]&^(ChunkJoinable|ChunkHeader) != 0 {
		return Chunk{}, ErrChunkFlags
	}
	return Chunk{Flags: buf[0], Payload: buf[1:]}, nil
}

// AppendChunk appends the wire form of a chunk to dst. A payload that would make the length field
// exceed MaxChunkLen is refused: a broadcaster never writes a larger value.
func AppendChunk(dst []byte, flags byte, payload []byte) ([]byte, error) {
	if len(payload)+1 > MaxChunkLen {
		return dst, ErrChunkSize
	}
	if flags&^(ChunkJoinable|ChunkHeader) != 0 {
		return dst, ErrChunkFlags
	}
	var hdr [4]byte
	binary.BigEndian.PutUint32(hdr[:], uint32(len(payload)+1))
	dst = append(dst, hdr[:]...)
	dst = append(dst, flags)
	return append(dst, payload...), nil
}

// EndChunk is the wire form of the clean end of a broadcast: a length of zero.
func EndChunk() []byte { return []byte{0, 0, 0, 0} }

// --- the listener's HTTP body (7.6) ---

var contentTypePattern = regexp.MustCompile(`^[a-z]+/[a-z0-9.+-]{1,60}$`)

// ValidContentType reports whether s may be copied into a response header: the pattern of ticket
// 165 rule 1, `^[a-z]+/[a-z0-9.+-]{1,60}$`, and nothing else.
func ValidContentType(s string) bool { return contentTypePattern.MatchString(s) }

// ListenResponseHead is the status line and headers of a listener's `200`: the upload's content
// type, chunked transfer, no cache, no ranges, no icy-metaint. The caller has checked contentType.
func ListenResponseHead(contentType string) []byte {
	var b strings.Builder
	b.WriteString("HTTP/1.1 200 OK\r\n")
	b.WriteString("Content-Type: " + contentType + "\r\n")
	b.WriteString("Transfer-Encoding: chunked\r\n")
	b.WriteString("Cache-Control: no-cache\r\n")
	b.WriteString("Accept-Ranges: none\r\n")
	b.WriteString("Connection: close\r\n\r\n")
	return []byte(b.String())
}

// AppendHTTPChunkHead appends the size line of one HTTP chunk of n payload bytes; the caller writes
// the payload and then HTTPChunkTail.
func AppendHTTPChunkHead(dst []byte, n int) []byte {
	dst = strconv.AppendInt(dst, int64(n), 16)
	return append(dst, '\r', '\n')
}

// HTTPChunkTail ends one HTTP chunk.
var HTTPChunkTail = []byte("\r\n")

// HTTPFinalChunk is the last chunk of a body that ended cleanly.
var HTTPFinalChunk = []byte("0\r\n\r\n")
