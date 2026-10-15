package wire

import (
	"bytes"
	"encoding/binary"
	"encoding/json"
	"fmt"
	"io"
	"reflect"
)

// legacySchemaVersion is the schemaVersion of the superseded ANYWHERE-ACCESS 0.10 wire. It is
// recognised in this one place, for this one purpose: the refusal of DEVICE-EXCHANGE 7.1. Nothing
// reads it, writes it or falls back to it (ticket 198 rule 1), and a source scan holds the module
// to exactly this occurrence.
const legacySchemaVersion = 1

// LegacyProbeFrame is the frame a peer of the superseded wire would send first: a `hello` carrying the
// legacy schemaVersion. It exists for the conformance vectors and the probe of ticket 205, which show
// the server's `version-unsupported` answer; nothing in the server reads or writes that version.
func LegacyProbeFrame() []byte {
	body := fmt.Sprintf("{\"schemaVersion\":%d,\"type\":\"hello\",\"deviceId\":\"AAAAAAAAAAAAAAAAAAAAAA\",\"deviceToken\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}", legacySchemaVersion)
	frame := make([]byte, 4+len(body))
	binary.BigEndian.PutUint32(frame, uint32(len(body)))
	copy(frame[4:], body)
	return frame
}

// registry maps a `type` to a constructor of its typed envelope.
var registry = map[string]func() Envelope{
	TypeEnroll:           func() Envelope { return new(Enroll) },
	TypeEnrolled:         func() Envelope { return new(Enrolled) },
	TypeHello:            func() Envelope { return new(Hello) },
	TypeWelcome:          func() Envelope { return new(Welcome) },
	TypeRefused:          func() Envelope { return new(Refused) },
	TypeKeepalive:        func() Envelope { return new(Keepalive) },
	TypeBye:              func() Envelope { return new(Bye) },
	TypeList:             func() Envelope { return new(List) },
	TypeDirectory:        func() Envelope { return new(Directory) },
	TypeSubscribe:        func() Envelope { return new(Subscribe) },
	TypeChanged:          func() Envelope { return new(Changed) },
	TypePublish:          func() Envelope { return new(Publish) },
	TypePublished:        func() Envelope { return new(Published) },
	TypeUnpublish:        func() Envelope { return new(Unpublish) },
	TypeBroadcastStart:   func() Envelope { return new(BroadcastStart) },
	TypeBroadcastStarted: func() Envelope { return new(BroadcastStarted) },
	TypeBroadcastUpdate:  func() Envelope { return new(BroadcastUpdate) },
	TypeBroadcastEnd:     func() Envelope { return new(BroadcastEnd) },
	TypePairingRequest:   func() Envelope { return new(PairingRequest) },
	TypePairingCode:      func() Envelope { return new(PairingCode) },
	TypeRevoke:           func() Envelope { return new(Revoke) },
	TypeConnect:          func() Envelope { return new(Connect) },
	TypeConnected:        func() Envelope { return new(Connected) },
	TypeOpen:             func() Envelope { return new(Open) },
	TypeAttach:           func() Envelope { return new(Attach) },
	TypeUpload:           func() Envelope { return new(Upload) },
	TypeUploadAccepted:   func() Envelope { return new(UploadAccepted) },
	TypeCast:             func() Envelope { return new(Cast) },
	TypeCastOffer:        func() Envelope { return new(CastOffer) },
	TypeCastAnswer:       func() Envelope { return new(CastAnswer) },
	TypeCastResult:       func() Envelope { return new(CastResult) },
	TypeCastStop:         func() Envelope { return new(CastStop) },
	TypeClipboardPut:     func() Envelope { return new(ClipboardPut) },
	TypeClipboardReady:   func() Envelope { return new(ClipboardReady) },
	TypeClipboardStored:  func() Envelope { return new(ClipboardStored) },
	TypeClipboardInfo:    func() Envelope { return new(ClipboardInfo) },
	TypeClipboardState:   func() Envelope { return new(ClipboardState) },
	TypeClipboardGet:     func() Envelope { return new(ClipboardGet) },
	TypeClipboardValue:   func() Envelope { return new(ClipboardValue) },
	TypeClipboardEmpty:   func() Envelope { return new(ClipboardEmpty) },
	TypeRegister:         func() Envelope { return new(Register) },
	TypePasswordChange:   func() Envelope { return new(PasswordChange) },
	TypePasswordChanged:  func() Envelope { return new(PasswordChanged) },
	TypeAccountDelete:    func() Envelope { return new(AccountDelete) },
	TypeAccountDeleted:   func() Envelope { return new(AccountDeleted) },
}

// Types lists every type this wire defines, sorted as the table of ticket 161 section 4.
func Types() []string {
	return []string{TypeEnroll, TypeEnrolled, TypeHello, TypeWelcome, TypeRefused, TypeKeepalive, TypeBye,
		TypeList, TypeDirectory, TypeSubscribe, TypeChanged, TypePublish, TypePublished, TypeUnpublish,
		TypeBroadcastStart, TypeBroadcastStarted, TypeBroadcastUpdate, TypeBroadcastEnd,
		TypePairingRequest, TypePairingCode, TypeRevoke, TypeConnect, TypeConnected, TypeOpen, TypeAttach,
		TypeUpload, TypeUploadAccepted, TypeCast, TypeCastOffer, TypeCastAnswer, TypeCastResult, TypeCastStop,
		TypeClipboardPut, TypeClipboardReady, TypeClipboardStored, TypeClipboardInfo, TypeClipboardState,
		TypeClipboardGet, TypeClipboardValue, TypeClipboardEmpty,
		TypeRegister, TypePasswordChange, TypePasswordChanged, TypeAccountDelete, TypeAccountDeleted}
}

// New returns a zero typed envelope of the given type, or nil for a type this wire does not define.
func New(typ string) Envelope {
	if mk := registry[typ]; mk != nil {
		return mk()
	}
	return nil
}

// Known reports whether e is an envelope this wire defines. A reader ignores an unknown one.
func Known(e Envelope) bool {
	_, unknown := e.(*Unknown)
	return !unknown && e != nil
}

// Decode parses one JSON body into its typed envelope. Unknown members are ignored; an unknown
// TYPE is returned as *Unknown for the caller to ignore. The errors are the ones of wire.go: a
// body that is not one object, an absent or other schemaVersion, a missing type, a required member
// missing or of the wrong shape. The legacy schemaVersion is ErrVersionRefused, which the caller
// answers (7.1); every other error closes the stream without an answer.
func Decode(body []byte) (Envelope, error) {
	var h Head
	if err := json.Unmarshal(body, &h); err != nil {
		return nil, ErrMalformed
	}
	switch h.SchemaVersion {
	case SchemaVersion:
	case legacySchemaVersion:
		return nil, ErrVersionRefused
	default:
		return nil, ErrSchema
	}
	if h.Type == "" {
		return nil, ErrNoType
	}
	mk := registry[h.Type]
	if mk == nil {
		return &Unknown{Head: h, Raw: append(json.RawMessage(nil), body...)}, nil
	}
	e := mk()
	if err := json.Unmarshal(body, e); err != nil {
		return nil, ErrMalformed
	}
	*e.head() = h
	if err := e.validate(); err != nil {
		return nil, fmt.Errorf("%w: %v", ErrMalformed, err)
	}
	return e, nil
}

// Encode renders the envelope as its JSON body: members in struct order, no HTML escaping, no
// trailing newline. It fixes the envelope's Head, so the schema version and the type are always
// the codec's own.
func Encode(e Envelope) ([]byte, error) {
	if e == nil || reflect.ValueOf(e).IsNil() {
		return nil, ErrBadMessage
	}
	if _, unknown := e.(*Unknown); unknown {
		return nil, ErrBadMessage
	}
	*e.head() = Head{SchemaVersion: SchemaVersion, Type: e.TypeName()}
	switch v := e.(type) {
	case *Directory:
		v.Devices, v.Resources, v.Broadcasts = nonNil(v.Devices), nonNil(v.Resources), nonNil(v.Broadcasts)
	case *Changed:
		v.Upserts.Devices, v.Upserts.Resources, v.Upserts.Broadcasts =
			nonNil(v.Upserts.Devices), nonNil(v.Upserts.Resources), nonNil(v.Upserts.Broadcasts)
		v.Removals.DeviceIDs, v.Removals.ResourceIDs, v.Removals.BroadcastIDs =
			nonNilS(v.Removals.DeviceIDs), nonNilS(v.Removals.ResourceIDs), nonNilS(v.Removals.BroadcastIDs)
	case *Welcome:
		v.Features = nonNilS(v.Features)
	case *Enrolled:
		v.Features = nonNilS(v.Features)
	}
	var buf bytes.Buffer
	enc := json.NewEncoder(&buf)
	enc.SetEscapeHTML(false)
	if err := enc.Encode(e); err != nil {
		return nil, fmt.Errorf("%w: %v", ErrBadMessage, err)
	}
	return bytes.TrimRight(buf.Bytes(), "\n"), nil
}

func nonNil(s []json.RawMessage) []json.RawMessage {
	if s == nil {
		return []json.RawMessage{}
	}
	return s
}

func nonNilS(s []string) []string {
	if s == nil {
		return []string{}
	}
	return s
}

// EncodeFrame renders the envelope as a whole frame - the 4-byte unsigned big-endian length, then
// the body - and refuses a body above limit. It is the form the golden vectors record.
func EncodeFrame(e Envelope, limit int) ([]byte, error) {
	body, err := Encode(e)
	if err != nil {
		return nil, err
	}
	if len(body) > limit {
		return nil, ErrFrameSize
	}
	frame := make([]byte, 4+len(body))
	binary.BigEndian.PutUint32(frame, uint32(len(body)))
	copy(frame[4:], body)
	return frame, nil
}

// Frame is EncodeFrame under the authenticated limit: the bytes of an envelope on any stream.
func Frame(e Envelope) ([]byte, error) { return EncodeFrame(e, MaxFrameAuth) }

// FrameWriter writes envelopes as frames under the limit in force for its stream.
type FrameWriter struct {
	w     io.Writer
	limit int
}

// NewFrameWriter wraps w. limit is MaxFrame until the stream is authenticated.
func NewFrameWriter(w io.Writer, limit int) *FrameWriter { return &FrameWriter{w: w, limit: limit} }

// SetLimit changes the limit (the stream authenticated).
func (fw *FrameWriter) SetLimit(n int) { fw.limit = n }

// Write encodes and sends one envelope in a single Write call.
func (fw *FrameWriter) Write(e Envelope) error {
	frame, err := EncodeFrame(e, fw.limit)
	if err != nil {
		return err
	}
	return fw.WriteFrame(frame)
}

// WriteFrame sends a frame that was already encoded (a fan-out encodes once and writes many times).
func (fw *FrameWriter) WriteFrame(frame []byte) error {
	_, err := fw.w.Write(frame)
	return err
}

// FrameReader reads frames under the limit in force for its stream. It reads exactly the bytes of
// one frame and nothing more, so the bytes that follow stay in the underlying reader: after
// `connected` or `upload-accepted` the stream is not envelopes any more.
type FrameReader struct {
	r     io.Reader
	limit int
}

// NewFrameReader wraps r. limit is MaxFrame until the stream is authenticated.
func NewFrameReader(r io.Reader, limit int) *FrameReader { return &FrameReader{r: r, limit: limit} }

// SetLimit changes the limit (the stream authenticated).
func (fr *FrameReader) SetLimit(n int) { fr.limit = n }

// ReadBody returns the next frame's JSON body. A length of zero or above the limit is ErrFrameSize
// and the stream must be closed by the caller.
func (fr *FrameReader) ReadBody() ([]byte, error) {
	var hdr [4]byte
	if _, err := io.ReadFull(fr.r, hdr[:]); err != nil {
		return nil, err
	}
	n := binary.BigEndian.Uint32(hdr[:])
	if n == 0 || int64(n) > int64(fr.limit) {
		return nil, ErrFrameSize
	}
	body := make([]byte, n)
	if _, err := io.ReadFull(fr.r, body); err != nil {
		if err == io.EOF {
			err = io.ErrUnexpectedEOF
		}
		return nil, err
	}
	return body, nil
}

// Read returns the next envelope (an unknown type comes back as *Unknown).
func (fr *FrameReader) Read() (Envelope, error) {
	body, err := fr.ReadBody()
	if err != nil {
		return nil, err
	}
	return Decode(body)
}
