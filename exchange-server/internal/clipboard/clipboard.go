// Package clipboard is the exchange server's clipboard slot of DEVICE-EXCHANGE section 22 (ticket
// 229): one value per member account, in memory and nowhere else, the latest COMPLETE upload the
// value. The bytes of a value and of an upload in flight are counted, never persisted, never
// logged; a restart of the server loses them - normal, not an error (item BU).
//
// The table holds, per account, one stored value and at most one upload in flight (ticket 229
// T-1). A value replaces the slot's value only when its last declared byte has arrived (item BX),
// and a reader that began earlier keeps the bytes it was reading: a value's bytes are immutable
// from the moment they are stored, so a get holds its own reference while a replacement or an
// erase only drops the slot's.
package clipboard

import (
	cryptorand "crypto/rand"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"io"
	"sync"
	"time"

	"fms/exchange-server/internal/wire"
)

// hasher is what the running SHA-256 of an upload offers; sha256.New satisfies it.
type hasher interface {
	Write(p []byte) (n int, err error)
	Sum(b []byte) []byte
}

// ErrPutInFlight is a put refused because the account already has one: the server takes at most
// one upload per account at a time, and the wire's word for "cannot take another incoming value
// now" is `capacity` (item BZ).
var ErrPutInFlight = errors.New("clipboard: the account already has an upload in flight")

// Decl is what a `clipboard-put` declared. The server never looks inside a value: the kind is
// metadata (item BU). For the kind `sealed` (27 item DN) Size counts the sealed bytes, MimeType and
// Name are empty and Sealed is the box that opens them: one opaque JSON object, kept verbatim and
// never read.
type Decl struct {
	Kind     string
	Size     int64
	MimeType string
	Name     string
	Sealed   json.RawMessage
}

// Value is one stored clipboard value with the members the server set (item BU). Its bytes are
// shared with every reader that began before the value was replaced: treat Bytes as read-only,
// because a value is never mutated once complete.
type Value struct {
	ClipID           string // 16 random bytes in the share id form, new for every stored value
	Kind             string
	MimeType         string // image and file only
	Name             string // file required, image optional
	Size             int64
	SHA256           string // lower-case hex over the stored bytes
	StoredAt         string // the server's clock
	SourceDeviceID   string
	SourceDeviceName string
	Sealed           json.RawMessage // the box of a `sealed` value (27 item DN): opaque, delivered as it came

	data []byte
}

// Bytes are the value's bytes, exactly Size of them. Read-only.
func (v *Value) Bytes() []byte { return v.data }

// Table is the clipboard slot of every account of one server.
type Table struct {
	now  func() time.Time
	rand io.Reader

	mu    sync.Mutex
	slots map[string]*slot
}

type slot struct {
	value   *Value // the complete value; nil while the clipboard is empty
	putting *Put   // at most one upload in flight
}

// Put is one upload in flight. Its methods are called from the one handler goroutine that serves
// the put's stream; the table's lock makes the byte counts stable while they run.
type Put struct {
	t          *Table
	account    string
	decl       Decl
	deviceID   string
	deviceName string
	buf        []byte
	h          hasher
	done       bool
}

// NewTable builds the table. A nil now means time.Now; a nil rand means the system generator.
func NewTable(now func() time.Time, rand io.Reader) *Table {
	if now == nil {
		now = time.Now
	}
	if rand == nil {
		rand = cryptorand.Reader
	}
	return &Table{now: now, rand: rand, slots: map[string]*slot{}}
}

// BeginPut opens the account's one upload in flight. The size limit and the account kind are the
// caller's checks (the engine reads the accounts store); the table's own refusal is the second
// overlapping put, ErrPutInFlight.
func (t *Table) BeginPut(account string, d Decl, deviceID, deviceName string) (*Put, error) {
	t.mu.Lock()
	defer t.mu.Unlock()
	s := t.slots[account]
	if s == nil {
		s = &slot{}
		t.slots[account] = s
	}
	if s.putting != nil {
		return nil, ErrPutInFlight
	}
	d.Sealed = append(json.RawMessage(nil), d.Sealed...) // the caller's frame buffer is not ours to keep
	p := &Put{t: t, account: account, decl: d, deviceID: deviceID, deviceName: deviceName, h: sha256.New()}
	s.putting = p
	return p, nil
}

// Write appends bytes of the upload. The caller reads at most the declared size from the stream,
// so Write never holds more than Decl.Size bytes.
func (p *Put) Write(b []byte) {
	p.t.mu.Lock()
	defer p.t.mu.Unlock()
	if p.done {
		return
	}
	p.buf = append(p.buf, b...)
	_, _ = p.h.Write(b)
}

// Complete stores the upload: the slot's value is replaced at the moment its last byte arrived
// (item BX), a new clipId is drawn, and the old value's bytes are dropped. A put is completed or
// dropped exactly once; a second call returns nil. It also returns nil when the account was erased
// while the upload ran - the value is lost with the account, never stored over the erasure.
func (p *Put) Complete() *Value {
	p.t.mu.Lock()
	defer p.t.mu.Unlock()
	if p.done {
		return nil
	}
	p.done = true
	s := p.t.slots[p.account]
	if s == nil || s.putting != p {
		p.buf = nil
		return nil
	}
	s.putting = nil
	sum := p.h.Sum(nil)
	v := &Value{
		ClipID:           wire.NewIDFrom(p.t.rand),
		Kind:             p.decl.Kind,
		MimeType:         p.decl.MimeType,
		Name:             p.decl.Name,
		Size:             int64(len(p.buf)),
		SHA256:           hex.EncodeToString(sum[:]),
		StoredAt:         wire.FormatTime(p.t.now()),
		SourceDeviceID:   p.deviceID,
		SourceDeviceName: p.deviceName,
		Sealed:           p.decl.Sealed,
		data:             p.buf,
	}
	s.value = v
	p.buf = nil
	return v
}

// Drop abandons the upload: what it carried is dropped and the previous value stays (item BX). A
// refused, broken or oversize send ends here.
func (p *Put) Drop() {
	p.t.mu.Lock()
	defer p.t.mu.Unlock()
	if p.done {
		return
	}
	p.done = true
	if s := p.t.slots[p.account]; s != nil && s.putting == p {
		s.putting = nil
		if s.value == nil {
			delete(p.t.slots, p.account) // an account with nothing stored holds nothing
		}
	}
	p.buf = nil
}

// Get returns the account's value, or nil for an empty clipboard. A get that begins while an
// upload runs returns the previous complete value (item BY); the bytes it returns stay whole even
// when the value is replaced mid-read.
func (t *Table) Get(account string) *Value {
	t.mu.Lock()
	defer t.mu.Unlock()
	if s := t.slots[account]; s != nil {
		return s.value
	}
	return nil
}

// GetByID returns the account's value when it is still the one named; a clipId that is no longer
// the value is the caller's `unavailable` (item BY).
func (t *Table) GetByID(account, clipID string) *Value {
	v := t.Get(account)
	if v != nil && v.ClipID == clipID {
		return v
	}
	return nil
}

// Erase forgets the account's value and cuts its upload in flight (item BU: erased with the
// account and when its kind becomes a guest). The bytes of a get already running are the reader's
// own reference and die with its stream.
func (t *Table) Erase(account string) {
	t.mu.Lock()
	defer t.mu.Unlock()
	if s := t.slots[account]; s != nil {
		if s.putting != nil {
			s.putting.done = true
			s.putting.buf = nil
		}
		delete(t.slots, account)
	}
}

// Counts are the deployment's clipboard totals: how many accounts hold a value, and how many
// bytes the slots hold now - stored values and uploads in flight together. Counts only, never a
// list (ticket 167's discipline for the operator's surfaces).
func (t *Table) Counts() (values int, bytes int64) {
	t.mu.Lock()
	defer t.mu.Unlock()
	for _, s := range t.slots {
		if s.value != nil {
			values++
			bytes += s.value.Size
		}
		if s.putting != nil {
			bytes += int64(len(s.putting.buf))
		}
	}
	return values, bytes
}
