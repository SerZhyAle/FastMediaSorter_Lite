package wire

import (
	"bytes"
	"encoding/json"
	"errors"
	"sort"
	"strconv"
	"time"
	"unicode/utf8"
)

// Record errors. A record over the bound is `refused {capacity}` (DEVICE-EXCHANGE 6 and item A); a
// body that is not one object is a malformed envelope and closes the stream.
var (
	ErrRecordSize  = errors.New("wire: record is above 16 KiB of JSON")
	ErrRecordShape = errors.New("wire: record is not one JSON object")
)

// Record is a section 6 record - a device, a resource, a broadcast - as the server holds it: an
// object whose members are kept as raw JSON. The server reads and rewrites only the members it
// owns (`deviceId`, `presence`, `updatedAt`, `lastSeen`, and it strips `access`, `shareId` and
// `publicPort` before disk); every other member, known or not, at any depth, travels verbatim to
// the other devices of the account (ticket 161 rule 6: a newer client's extra members must reach
// another newer client through this server).
type Record struct{ m map[string]json.RawMessage }

// NewRecord returns an empty record.
func NewRecord() *Record { return &Record{m: map[string]json.RawMessage{}} }

// ParseRecord parses one record. It refuses a body above MaxRecord and anything that is not one
// JSON object.
func ParseRecord(raw []byte) (*Record, error) {
	if len(raw) > MaxRecord {
		return nil, ErrRecordSize
	}
	raw = bytes.TrimSpace(raw)
	if len(raw) == 0 || raw[0] != '{' {
		return nil, ErrRecordShape
	}
	m := map[string]json.RawMessage{}
	if err := json.Unmarshal(raw, &m); err != nil || m == nil {
		return nil, ErrRecordShape
	}
	for k, v := range m {
		m[k] = append(json.RawMessage(nil), v...)
	}
	return &Record{m: m}, nil
}

// String never prints a member: a record may hold `access`.
func (r *Record) String() string { return "wire.Record" }

// GoString never prints a member.
func (r *Record) GoString() string { return "wire.Record" }

// Has reports whether the member is present.
func (r *Record) Has(key string) bool { _, ok := r.m[key]; return ok }

// Raw returns a member's raw JSON, or nil.
func (r *Record) Raw(key string) json.RawMessage { return r.m[key] }

// Str returns a member that is a JSON string.
func (r *Record) Str(key string) (string, bool) {
	raw, ok := r.m[key]
	if !ok {
		return "", false
	}
	var s string
	if err := json.Unmarshal(raw, &s); err != nil {
		return "", false
	}
	return s, true
}

// Int returns a member that is a JSON integer.
func (r *Record) Int(key string) (int, bool) {
	raw, ok := r.m[key]
	if !ok {
		return 0, false
	}
	var n int
	if err := json.Unmarshal(raw, &n); err != nil {
		return 0, false
	}
	return n, true
}

// SetStr sets a string member.
func (r *Record) SetStr(key, v string) {
	b, _ := marshalNoEscape(v)
	r.m[key] = b
}

// SetInt sets an integer member.
func (r *Record) SetInt(key string, v int) { r.m[key] = json.RawMessage(strconv.Itoa(v)) }

// SetRaw sets a member to raw JSON the caller vouches for.
func (r *Record) SetRaw(key string, raw json.RawMessage) {
	r.m[key] = append(json.RawMessage(nil), raw...)
}

// SetTime sets a timestamp member in the wire form (UTC, second precision, a Z).
func (r *Record) SetTime(key string, t time.Time) { r.SetStr(key, FormatTime(t)) }

// Delete removes a member.
func (r *Record) Delete(key string) { delete(r.m, key) }

// Clone is a deep copy.
func (r *Record) Clone() *Record {
	c := &Record{m: make(map[string]json.RawMessage, len(r.m))}
	for k, v := range r.m {
		c.m[k] = append(json.RawMessage(nil), v...)
	}
	return c
}

// Marshal renders the record: members sorted by name, values exactly as held, no whitespace. The
// order is fixed so the record bytes are as stable as the envelope bytes.
func (r *Record) Marshal() json.RawMessage {
	keys := make([]string, 0, len(r.m))
	for k := range r.m {
		keys = append(keys, k)
	}
	sort.Strings(keys)
	var b bytes.Buffer
	b.WriteByte('{')
	for i, k := range keys {
		if i > 0 {
			b.WriteByte(',')
		}
		kb, _ := marshalNoEscape(k)
		b.Write(kb)
		b.WriteByte(':')
		b.Write(compactRaw(r.m[k]))
	}
	b.WriteByte('}')
	return b.Bytes()
}

func compactRaw(raw json.RawMessage) []byte {
	var b bytes.Buffer
	if err := json.Compact(&b, raw); err != nil {
		return raw
	}
	return b.Bytes()
}

func marshalNoEscape(v any) (json.RawMessage, error) {
	var buf bytes.Buffer
	enc := json.NewEncoder(&buf)
	enc.SetEscapeHTML(false)
	if err := enc.Encode(v); err != nil {
		return nil, err
	}
	return bytes.TrimRight(buf.Bytes(), "\n"), nil
}

// FormatTime is the wire form of a timestamp: UTC ISO 8601 with a Z, second precision.
func FormatTime(t time.Time) string { return t.UTC().Format("2006-01-02T15:04:05Z") }

// ParseTime reads FormatTime's form (and a fractional-second one a client may write).
func ParseTime(s string) (time.Time, error) { return time.Parse(time.RFC3339Nano, s) }

// CheckReceiver validates a device's `receiver` block (DEVICE-EXCHANGE 6.1, item E): an object whose
// `modes`, `transports` and `plays` lists hold at most 8 entries of at most 16 characters (a `plays`
// entry is a {mode, transport} pair of such strings). Every other member is kept as it is.
func CheckReceiver(raw []byte) error {
	if len(raw) > MaxRecord {
		return ErrRecordSize
	}
	var m map[string]json.RawMessage
	if err := json.Unmarshal(raw, &m); err != nil || m == nil {
		return errors.New("receiver is not an object")
	}
	for _, key := range []string{"modes", "transports"} {
		v, ok := m[key]
		if !ok {
			continue
		}
		var list []string
		if err := json.Unmarshal(v, &list); err != nil {
			return errors.New("receiver." + key + " is not a list of strings")
		}
		if err := checkList(key, list); err != nil {
			return err
		}
	}
	if v, ok := m["plays"]; ok {
		var pairs []map[string]json.RawMessage
		if err := json.Unmarshal(v, &pairs); err != nil {
			return errors.New("receiver.plays is not a list of pairs")
		}
		if len(pairs) > 8 {
			return errors.New("receiver.plays has more than 8 entries")
		}
		for _, p := range pairs {
			var mode, transport string
			if err := json.Unmarshal(p["mode"], &mode); err != nil {
				return errors.New("a plays pair has no mode")
			}
			if err := json.Unmarshal(p["transport"], &transport); err != nil {
				return errors.New("a plays pair has no transport")
			}
			if err := checkList("plays", []string{mode, transport}); err != nil {
				return err
			}
		}
	}
	return nil
}

func checkList(name string, list []string) error {
	if len(list) > 8 {
		return errors.New("receiver." + name + " has more than 8 entries")
	}
	for _, s := range list {
		if len(s) > 16 || !utf8.ValidString(s) {
			return errors.New("receiver." + name + " has an entry above 16 characters")
		}
	}
	return nil
}

// DirectoryFits reports whether a `directory` envelope holding exactly these records still encodes
// within MaxFrameAuth (item A and item T: a server never accepts what it could not deliver). The
// revision is taken at its widest, so the answer holds for the next few transactions too.
func DirectoryFits(devices, resources, broadcasts []json.RawMessage) bool {
	sum := 0
	for _, l := range [][]json.RawMessage{devices, resources, broadcasts} {
		for _, r := range l {
			sum += len(r) + 1
		}
	}
	if sum > MaxFrameAuth {
		return false
	}
	d := &Directory{Revision: 1<<62 - 1, Devices: devices, Resources: resources, Broadcasts: broadcasts}
	body, err := Encode(d)
	return err == nil && len(body) <= MaxFrameAuth
}
