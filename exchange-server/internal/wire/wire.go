// Package wire holds everything a device and the exchange server see on the wire of DEVICE-EXCHANGE
// section 7: the envelope set, its codec, the 4-byte length framing, the opaque records and the
// reason list. It is the ONE place that builds or parses a wire byte (ticket 161): no other package
// writes a JSON member name of an envelope, a frame header or an HTTP status line, so a changed
// owner decision costs edits here and nowhere else, and the golden frames of ticket 178 can be
// generated from this code and nothing else.
//
// It is pure Go with no OS dependency and no dependency on any other package of the module.
package wire

import (
	"crypto/rand"
	"encoding/base64"
	"errors"
	"io"
	"sync/atomic"
)

const (
	// SchemaVersion is the integer every envelope carries (DEVICE-EXCHANGE 7.1).
	SchemaVersion = 2

	// MaxFrame is the most a frame may carry until its stream is authenticated, in both directions
	// (DEVICE-EXCHANGE 7.1 and 15 item T): every frame up to and including `welcome` or `enrolled`,
	// and every frame of a stream that never authenticates. An unauthenticated peer cannot make the
	// other side buffer more. 16384 bytes of JSON.
	MaxFrame = 16 * 1024

	// MaxFrameAuth is the most a frame may carry after `welcome` or `enrolled`, in both directions
	// (DEVICE-EXCHANGE 15 item T, owner decision of 2026-10-07: FMS_W's answer to ask 1, no paging).
	// 262144 bytes of JSON. Question 1 of ticket 181 was open when this module was started; the
	// contract text of 0.17 settled it, and this is the one line to change if the owner moves it.
	MaxFrameAuth = 256 * 1024

	// MaxRecord is the most one record (a resource, a broadcast, a receiver block) may hold
	// (DEVICE-EXCHANGE 6): unchanged by the frame limit, the record ceiling is not lowered below the
	// frame and the frame is raised above it.
	MaxRecord = 16 * 1024
)

// Errors a decoder or a frame reader reports. Every one of them ends the stream; an unknown TYPE
// and an unknown MEMBER are not errors (DEVICE-EXCHANGE 7.1).
var (
	// ErrMalformed: not one JSON object, a member of the wrong shape, a required member missing.
	ErrMalformed = errors.New("wire: malformed envelope")
	// ErrSchema: a schemaVersion that is neither 2 nor the one this server recognises only to
	// refuse it. The stream closes without an answer.
	ErrSchema = errors.New("wire: unsupported schemaVersion")
	// ErrVersionRefused: the legacy schemaVersion of the superseded wire. The peer is answered
	// `refused {version-unsupported}` carrying schemaVersion 2 and the stream closes (7.1, item P).
	// This is the only place the number 1 of that wire exists in the module.
	ErrVersionRefused = errors.New("wire: schemaVersion 1 is not supported")
	// ErrNoType: an object with no type member.
	ErrNoType = errors.New("wire: envelope has no type")
	// ErrFrameSize: a zero length or a length above the limit in force for the stream.
	ErrFrameSize = errors.New("wire: frame length out of range")
	// ErrBadMessage: an envelope that cannot be encoded.
	ErrBadMessage = errors.New("wire: message cannot be encoded")
)

var idEncoding = base64.RawURLEncoding.Strict()

// randSource boxes the package's id source so it can be swapped atomically.
type randSource struct{ r io.Reader }

var idRand atomic.Pointer[randSource]

func init() { idRand.Store(&randSource{rand.Reader}) }

// SetRandom replaces the source NewID and NewToken draw from (ticket 161 T-5: the vector generator of
// ticket 178 and tests that need stable ids). nil restores the system's cryptographic generator. It
// is NOT for production use - nothing outside tests and cmd/exchange-vectors calls it - and a
// replacement source must itself be safe for concurrent use, or only be drawn from by one goroutine
// at a time, as a scripted generator run is.
func SetRandom(r io.Reader) {
	if r == nil {
		r = rand.Reader
	}
	idRand.Store(&randSource{r})
}

func currentRandom() io.Reader { return idRand.Load().r }

// NewID returns 16 bytes from the system's cryptographic generator as base64url without padding:
// the id form of a deviceId, resourceId, shareId, broadcastId and tunnelId (DEVICE-EXCHANGE 2).
func NewID() string { return NewIDFrom(currentRandom()) }

// NewIDFrom is NewID over an injected source (ticket 178: a generated vector draws its ids from a
// seeded source so the bytes are stable).
func NewIDFrom(r io.Reader) string {
	var b [16]byte
	if _, err := io.ReadFull(r, b[:]); err != nil {
		panic("wire: random source failed: " + err.Error())
	}
	return idEncoding.EncodeToString(b[:])
}

// NewToken returns 32 random bytes as base64url without padding: a device token (5.2).
func NewToken() string { return NewTokenFrom(currentRandom()) }

// NewTokenFrom is NewToken over an injected source.
func NewTokenFrom(r io.Reader) string {
	var b [32]byte
	if _, err := io.ReadFull(r, b[:]); err != nil {
		panic("wire: random source failed: " + err.Error())
	}
	return base64.RawURLEncoding.EncodeToString(b[:])
}

// ValidID reports whether s is exactly 22 characters of base64url that decode to 16 bytes.
func ValidID(s string) bool {
	if len(s) != 22 {
		return false
	}
	b, err := idEncoding.DecodeString(s)
	return err == nil && len(b) == 16
}

// ValidToken reports whether s has the form of a device token: 43 characters of base64url that
// decode to 32 bytes. The server compares a presented token only after this shape check.
func ValidToken(s string) bool {
	if len(s) != 43 {
		return false
	}
	b, err := base64.RawURLEncoding.Strict().DecodeString(s)
	return err == nil && len(b) == 32
}
