// Package tunnel is the table of tunnels waiting for their producer's `attach` and of the tunnels
// that are bridged (DEVICE-EXCHANGE 7.5 and item L, ticket 164).
//
// A tunnel is born when a consumer's `connect` has found a live producer, gets a 16-byte id from the
// server, waits a short window for the producer's data stream, and ends when its bridge ends. The id
// is single use. The table enforces the per-resource and per-broadcast caps over pending AND
// bridged tunnels, and it can end every tunnel of a producer, a resource or a broadcast at once.
//
// The package interprets no byte of the tunnel and imports nothing of the protocol but the id form.
package tunnel

import (
	"errors"
	"io"
	"net"
	"sync"
	"time"

	"fms/exchange-server/internal/wire"
)

// Key names the capped entity a tunnel belongs to: one resource of one account, or one broadcast.
type Key string

// ResourceKey is the key of a resource's tunnels.
func ResourceKey(account, resourceID string) Key { return Key("r:" + account + "/" + resourceID) }

// BroadcastKey is the key of a broadcast's tunnels.
func BroadcastKey(broadcastID string) Key { return Key("b:" + broadcastID) }

// ErrFull: the entity already has its maximum of tunnels.
var ErrFull = errors.New("tunnel: at the cap")

// Table is every tunnel of the deployment.
type Table struct {
	mu       sync.Mutex
	pending  map[string]*Tunnel            // by tunnel id: waiting for their attach
	byKey    map[Key]map[string]*Tunnel    // every tunnel of a capped entity
	byDevice map[string]map[string]*Tunnel // every tunnel of a producing device
	rand     io.Reader                     // the tunnel-id source; nil means wire.NewID's own
}

// NewTable returns an empty table.
func NewTable() *Table {
	return &Table{pending: map[string]*Tunnel{}, byKey: map[Key]map[string]*Tunnel{}, byDevice: map[string]map[string]*Tunnel{}}
}

// SetRand makes tunnel ids come from r (the vector generator of ticket 205 and tests); nil restores
// wire.NewID's source. Call it before the first Open.
func (t *Table) SetRand(r io.Reader) {
	t.mu.Lock()
	t.rand = r
	t.mu.Unlock()
}

// Tunnel is one consumer waiting for, or bridged to, a producer's data stream.
type Tunnel struct {
	ID     string
	Key    Key
	Device string // the producing device: the only one whose attach is accepted

	table    *Table
	consumer net.Conn
	attached chan net.Conn
	done     chan struct{}
	once     sync.Once

	mu   sync.Mutex
	data net.Conn
}

// Open registers a tunnel for a consumer, unless the entity already holds max of them. The id is 16
// random bytes in the id form. The tunnel stays in the table until Done.
func (t *Table) Open(key Key, producerDevice string, consumer net.Conn, max int) (*Tunnel, error) {
	t.mu.Lock()
	defer t.mu.Unlock()
	if len(t.byKey[key]) >= max {
		return nil, ErrFull
	}
	id := wire.NewID()
	if t.rand != nil {
		id = wire.NewIDFrom(t.rand)
	}
	tn := &Tunnel{ID: id, Key: key, Device: producerDevice, table: t, consumer: consumer,
		attached: make(chan net.Conn, 1), done: make(chan struct{})}
	t.pending[tn.ID] = tn
	if t.byKey[key] == nil {
		t.byKey[key] = map[string]*Tunnel{}
	}
	t.byKey[key][tn.ID] = tn
	if t.byDevice[producerDevice] == nil {
		t.byDevice[producerDevice] = map[string]*Tunnel{}
	}
	t.byDevice[producerDevice][tn.ID] = tn
	return tn, nil
}

// Claim is the producer's `attach`: it takes the pending tunnel of that id, once. An unknown or used
// id, an expired one, or an id that was issued for another device (a foreign attach) is not claimed -
// and a foreign attach leaves the real one waiting.
func (t *Table) Claim(id, device string) (*Tunnel, bool) {
	t.mu.Lock()
	defer t.mu.Unlock()
	tn := t.pending[id]
	if tn == nil || tn.Device != device {
		return nil, false
	}
	delete(t.pending, id)
	return tn, true
}

// Count is how many tunnels, pending or bridged, an entity holds.
func (t *Table) Count(key Key) int {
	t.mu.Lock()
	defer t.mu.Unlock()
	return len(t.byKey[key])
}

// Total is how many tunnels exist.
func (t *Table) Total() int {
	t.mu.Lock()
	defer t.mu.Unlock()
	n := 0
	for _, m := range t.byKey {
		n += len(m)
	}
	return n
}

// Pending is how many tunnels wait for their attach.
func (t *Table) Pending() int {
	t.mu.Lock()
	defer t.mu.Unlock()
	return len(t.pending)
}

func (t *Table) collect(match func(*Tunnel) bool) []*Tunnel {
	t.mu.Lock()
	defer t.mu.Unlock()
	var out []*Tunnel
	for _, m := range t.byKey {
		for _, tn := range m {
			if match(tn) {
				out = append(out, tn)
			}
		}
	}
	return out
}

// AbortKey ends every tunnel of a resource or a broadcast (it was unpublished, went offline, ended).
func (t *Table) AbortKey(key Key) {
	for _, tn := range t.collect(func(x *Tunnel) bool { return x.Key == key }) {
		tn.Abort()
	}
}

// AbortDevice ends every tunnel its producing device is serving (the device went offline or was revoked).
func (t *Table) AbortDevice(device string) {
	for _, tn := range t.collect(func(x *Tunnel) bool { return x.Device == device }) {
		tn.Abort()
	}
}

// AbortAll ends every tunnel (the server is closing).
func (t *Table) AbortAll() {
	for _, tn := range t.collect(func(*Tunnel) bool { return true }) {
		tn.Abort()
	}
}

// Done takes the tunnel out of the table. Call it once the bridge has ended or never came to be.
func (tn *Tunnel) Done() {
	t := tn.table
	t.mu.Lock()
	delete(t.pending, tn.ID)
	if m := t.byKey[tn.Key]; m != nil {
		delete(m, tn.ID)
		if len(m) == 0 {
			delete(t.byKey, tn.Key)
		}
	}
	if m := t.byDevice[tn.Device]; m != nil {
		delete(m, tn.ID)
		if len(m) == 0 {
			delete(t.byDevice, tn.Device)
		}
	}
	t.mu.Unlock()
}

// Abort ends the tunnel: its consumer's stream and, if it has arrived, its producer's data stream.
func (tn *Tunnel) Abort() {
	tn.once.Do(func() {
		close(tn.done)
		_ = tn.consumer.Close()
		tn.mu.Lock()
		d := tn.data
		tn.mu.Unlock()
		if d != nil {
			_ = d.Close()
		}
	})
}

// Aborted is closed when the tunnel was aborted.
func (tn *Tunnel) Aborted() <-chan struct{} { return tn.done }

// Deliver hands the producer's data stream to the bridge waiting for it. If the tunnel was aborted
// in the meantime nobody will ever read the stream, so it is closed here instead of leaking until the
// peer gives up.
func (tn *Tunnel) Deliver(data net.Conn) {
	tn.attached <- data
	select {
	case <-tn.done:
		select {
		case x := <-tn.attached:
			_ = x.Close()
		default:
		}
	default:
	}
}

// Wait is the consumer side's wait for the producer's attach. It returns the data stream, or false
// when the window passed, the tunnel was aborted or the server is closing. After a window that
// passed the tunnel can no longer be claimed.
func (tn *Tunnel) Wait(window time.Duration, closing <-chan struct{}) (net.Conn, bool) {
	timer := time.NewTimer(window)
	defer timer.Stop()
	var data net.Conn
	select {
	case data = <-tn.attached:
	case <-timer.C:
		t := tn.table
		t.mu.Lock()
		_, stillPending := t.pending[tn.ID]
		delete(t.pending, tn.ID)
		t.mu.Unlock()
		if stillPending {
			return nil, false
		}
		data = <-tn.attached // the attach won the race: it claimed first and delivers right after
	case <-tn.done:
		return nil, false
	case <-closing:
		return nil, false
	}
	tn.mu.Lock()
	tn.data = data
	tn.mu.Unlock()
	select {
	case <-tn.done:
		_ = data.Close()
		return nil, false
	default:
	}
	return data, true
}
