// Package wire holds everything a producer and a consumer see on the exchange wire: the envelope
// set, its codec and the TCP framing (ANYWHERE-ACCESS section 6 plus the wire completion of
// ticket 134, still a proposal until rung 2 freezes it).
//
// It is the one place a changed owner decision costs edits: no other package builds or parses a
// byte of the wire. It is pure Go with no OS dependency.
package wire

import (
	"crypto/rand"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"io"
)

const (
	// SchemaVersion is the integer every envelope carries (Q134-6): 1 for the whole 0.x wire.
	SchemaVersion = 1
	// MaxFrame is the largest JSON body one frame may carry (Q134-2). An envelope never needs more.
	MaxFrame = 16 * 1024
)

// Envelope types (contract section 6 plus Q134-1: control stream and per-tunnel data stream).
const (
	TypeRegister   = "register"
	TypeRegistered = "registered"
	TypeRefused    = "refused"
	TypeKeepalive  = "keepalive"
	TypeUnregister = "unregister"
	TypeOpen       = "open"
	TypeAttach     = "attach"
	TypeConnect    = "connect"
	TypeConnected  = "connected"
)

// Refusal reasons (Q134-5). Clients show an unknown reason as a generic refusal.
const (
	ReasonBadPassword     = "bad-password"
	ReasonIDCollision     = "id-collision"
	ReasonCapacity        = "capacity"
	ReasonPortUnavailable = "port-unavailable"
	ReasonTLSRequired     = "tls-required"
	ReasonUnavailable     = "unavailable"
	ReasonRateLimited     = "rate-limited"
)

// Errors a decoder reports. Every one of them ends the stream (Q134-12); an unknown TYPE is not one.
var (
	ErrMalformed  = errors.New("wire: malformed envelope")
	ErrSchema     = errors.New("wire: unsupported schemaVersion")
	ErrNoType     = errors.New("wire: envelope has no type")
	ErrFrameSize  = errors.New("wire: frame length out of range")
	ErrBadMessage = errors.New("wire: message cannot be encoded")
)

// Message is every envelope of the wire in one shape; which members are meaningful depends on Type.
// The member order below is the order Encode writes, so the golden vectors are byte-stable.
type Message struct {
	SchemaVersion    int             `json:"schemaVersion"`
	Type             string          `json:"type"`
	Password         string          `json:"password,omitempty"`
	ShareID          string          `json:"shareId,omitempty"`
	KeepaliveSeconds int             `json:"keepaliveSeconds,omitempty"`
	Port             *int            `json:"port,omitempty"`
	ResumeToken      string          `json:"resumeToken,omitempty"`
	Claim            json.RawMessage `json:"claim,omitempty"`
	Reason           string          `json:"reason,omitempty"`
	TunnelID         string          `json:"tunnelId,omitempty"`
}

// String never prints the password, so a Message handed to a formatter by mistake cannot leak it.
func (m Message) String() string {
	return fmt.Sprintf("wire.Message{type=%s}", m.Type)
}

// Known reports whether the type is one this wire defines. A caller ignores an unknown type
// (contract section 6.1: never an error).
func (m Message) Known() bool {
	switch m.Type {
	case TypeRegister, TypeRegistered, TypeRefused, TypeKeepalive, TypeUnregister,
		TypeOpen, TypeAttach, TypeConnect, TypeConnected:
		return true
	}
	return false
}

// New returns an envelope of the given type with the schema version set.
func New(typ string) Message { return Message{SchemaVersion: SchemaVersion, Type: typ} }

// Refused builds a refusal.
func Refused(reason string) Message {
	m := New(TypeRefused)
	m.Reason = reason
	return m
}

// IntPtr is a helper for the optional port member.
func IntPtr(n int) *int { return &n }

// Encode renders the envelope as its JSON body.
func Encode(m Message) ([]byte, error) {
	if m.Type == "" {
		return nil, ErrBadMessage
	}
	if m.SchemaVersion == 0 {
		m.SchemaVersion = SchemaVersion
	}
	b, err := json.Marshal(m)
	if err != nil {
		return nil, fmt.Errorf("%w: %v", ErrBadMessage, err)
	}
	if len(b) > MaxFrame {
		return nil, ErrFrameSize
	}
	return b, nil
}

// Decode parses one JSON body. Unknown members are ignored. An absent or different schemaVersion
// is refused; an unknown type is returned as is (Known() is false) and is for the caller to ignore.
func Decode(b []byte) (Message, error) {
	var m Message
	if err := json.Unmarshal(b, &m); err != nil {
		return Message{}, ErrMalformed
	}
	if m.SchemaVersion != SchemaVersion {
		return Message{}, ErrSchema
	}
	if m.Type == "" {
		return Message{}, ErrNoType
	}
	return m, nil
}

var idEncoding = base64.RawURLEncoding.Strict()

// NewID returns 16 bytes from the system's cryptographic generator as base64url without padding:
// the form of a share id, a tunnel id and a resume token (contract section 6.2).
func NewID() string {
	var b [16]byte
	if _, err := rand.Read(b[:]); err != nil {
		panic("wire: system random source failed: " + err.Error())
	}
	return idEncoding.EncodeToString(b[:])
}

// ValidID reports whether s is exactly 22 characters of base64url that decode to 16 bytes.
func ValidID(s string) bool {
	if len(s) != 22 {
		return false
	}
	b, err := idEncoding.DecodeString(s)
	return err == nil && len(b) == 16
}

// FrameWriter writes envelopes as frames: a 4-byte unsigned big-endian length, then the JSON body.
type FrameWriter struct{ w io.Writer }

// NewFrameWriter wraps w. A frame is written with one Write call.
func NewFrameWriter(w io.Writer) *FrameWriter { return &FrameWriter{w: w} }

// Write encodes and sends one envelope.
func (fw *FrameWriter) Write(m Message) error {
	body, err := Encode(m)
	if err != nil {
		return err
	}
	return fw.writeBody(body)
}

func (fw *FrameWriter) writeBody(body []byte) error {
	buf := make([]byte, 4+len(body))
	buf[0] = byte(len(body) >> 24)
	buf[1] = byte(len(body) >> 16)
	buf[2] = byte(len(body) >> 8)
	buf[3] = byte(len(body))
	copy(buf[4:], body)
	_, err := fw.w.Write(buf)
	return err
}

// Frame returns the exact bytes WriteMessage puts on the stream: the golden-vector form.
func Frame(m Message) ([]byte, error) {
	body, err := Encode(m)
	if err != nil {
		return nil, err
	}
	var sink frameSink
	if err := NewFrameWriter(&sink).writeBody(body); err != nil {
		return nil, err
	}
	return sink.b, nil
}

type frameSink struct{ b []byte }

func (s *frameSink) Write(p []byte) (int, error) { s.b = append(s.b, p...); return len(p), nil }

// FrameReader reads frames. It reads exactly the bytes of one frame and nothing more, so the bytes
// that follow a frame stay in the underlying reader: after `connected` the stream is raw SSH.
type FrameReader struct{ r io.Reader }

// NewFrameReader wraps r.
func NewFrameReader(r io.Reader) *FrameReader { return &FrameReader{r: r} }

// Read returns the next envelope. A length of zero or above MaxFrame is ErrFrameSize and the stream
// must be closed by the caller.
func (fr *FrameReader) Read() (Message, error) {
	var hdr [4]byte
	if _, err := io.ReadFull(fr.r, hdr[:]); err != nil {
		return Message{}, err
	}
	n := int(hdr[0])<<24 | int(hdr[1])<<16 | int(hdr[2])<<8 | int(hdr[3])
	if n <= 0 || n > MaxFrame {
		return Message{}, ErrFrameSize
	}
	body := make([]byte, n)
	if _, err := io.ReadFull(fr.r, body); err != nil {
		if err == io.EOF {
			err = io.ErrUnexpectedEOF
		}
		return Message{}, err
	}
	return Decode(body)
}
