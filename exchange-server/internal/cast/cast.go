// Package cast is the table of pending casts of DEVICE-EXCHANGE 7.8 (ticket 166): a broadcaster asked
// one named receiver of its account to play a broadcast, the offer went out, and the answer has not
// come back yet.
//
// An entry is memory-only - nothing here ever reaches the disk - keyed by (broadcast, receiver,
// caster), and dies with either control stream, with the broadcast and at the timeout. The table
// holds no record, no `access` and no address: only the three ids and a deadline. The clock is
// injected, so the timeout is tested without sleeping.
package cast

import (
	"errors"
	"sort"
	"sync"
	"time"
)

// DefaultTimeout is how long a receiver has to answer (DEVICE-EXCHANGE item R, working default).
const DefaultTimeout = 60 * time.Second

// DefaultPerAccount is the most pending offers one account may have (ticket 166 rule 6).
const DefaultPerAccount = 16

// ErrCapacity: the account already has its maximum of pending offers.
var ErrCapacity = errors.New("cast: too many pending offers for this account")

// Pending is one offer waiting for its answer.
type Pending struct {
	Account     string
	BroadcastID string
	Receiver    string // the receiver's deviceId
	Caster      string // the caster's deviceId
	Deadline    time.Time
}

type key struct{ broadcast, receiver, caster string }

// Table is every pending offer of the deployment.
type Table struct {
	now        func() time.Time
	timeout    time.Duration
	perAccount int

	mu      sync.Mutex
	pending map[key]*Pending
	count   map[string]int // pending offers by account
}

// New returns an empty table. Zero values keep the defaults; a nil clock is time.Now.
func New(now func() time.Time, timeout time.Duration, perAccount int) *Table {
	if now == nil {
		now = time.Now
	}
	if timeout <= 0 {
		timeout = DefaultTimeout
	}
	if perAccount <= 0 {
		perAccount = DefaultPerAccount
	}
	return &Table{now: now, timeout: timeout, perAccount: perAccount, pending: map[key]*Pending{}, count: map[string]int{}}
}

func (t *Table) removeLocked(k key, p *Pending) {
	delete(t.pending, k)
	if t.count[p.Account]--; t.count[p.Account] <= 0 {
		delete(t.count, p.Account)
	}
}

// Add records an offer. The same (broadcast, receiver, caster) offered again replaces the earlier
// one and restarts its timeout without counting twice; a new one past the account's maximum is
// ErrCapacity.
func (t *Table) Add(account, broadcastID, receiver, caster string) error {
	t.mu.Lock()
	defer t.mu.Unlock()
	k := key{broadcastID, receiver, caster}
	if old := t.pending[k]; old != nil {
		old.Deadline = t.now().Add(t.timeout)
		return nil
	}
	if t.count[account] >= t.perAccount {
		return ErrCapacity
	}
	t.pending[k] = &Pending{Account: account, BroadcastID: broadcastID, Receiver: receiver, Caster: caster, Deadline: t.now().Add(t.timeout)}
	t.count[account]++
	return nil
}

func sorted(ps []*Pending) []Pending {
	sort.Slice(ps, func(i, j int) bool {
		a, b := ps[i], ps[j]
		if !a.Deadline.Equal(b.Deadline) {
			return a.Deadline.Before(b.Deadline)
		}
		return a.Caster < b.Caster
	})
	out := make([]Pending, len(ps))
	for i, p := range ps {
		out[i] = *p
	}
	return out
}

// Answer takes the offers an answering receiver is answering - every pending offer of that broadcast
// to that receiver, whoever cast it - out of the table and returns them. An offer whose time has
// passed is NOT returned (a late answer is dropped; Expire reports it as a timeout).
func (t *Table) Answer(broadcastID, receiver string) []Pending {
	t.mu.Lock()
	defer t.mu.Unlock()
	now := t.now()
	var got []*Pending
	for k, p := range t.pending {
		if k.broadcast == broadcastID && k.receiver == receiver && now.Before(p.Deadline) {
			got = append(got, p)
			t.removeLocked(k, p)
		}
	}
	return sorted(got)
}

// Expire removes and returns the offers whose time has passed.
func (t *Table) Expire() []Pending {
	t.mu.Lock()
	defer t.mu.Unlock()
	now := t.now()
	var dead []*Pending
	for k, p := range t.pending {
		if !now.Before(p.Deadline) {
			dead = append(dead, p)
			t.removeLocked(k, p)
		}
	}
	return sorted(dead)
}

// DropDevice removes every offer a control stream that ended was part of. Offers the device had MADE
// are dropped (asCaster); offers MADE TO it (asReceiver) are returned so the caster can be told its
// receiver is gone.
func (t *Table) DropDevice(deviceID string) (asReceiver []Pending) {
	t.mu.Lock()
	defer t.mu.Unlock()
	var rcv []*Pending
	for k, p := range t.pending {
		switch {
		case k.caster == deviceID:
			t.removeLocked(k, p)
		case k.receiver == deviceID:
			rcv = append(rcv, p)
			t.removeLocked(k, p)
		}
	}
	return sorted(rcv)
}

// DropBroadcast removes every offer of a broadcast that ended.
func (t *Table) DropBroadcast(broadcastID string) {
	t.mu.Lock()
	defer t.mu.Unlock()
	for k, p := range t.pending {
		if k.broadcast == broadcastID {
			t.removeLocked(k, p)
		}
	}
}

// Len is how many offers are pending (a count, never a list).
func (t *Table) Len() int {
	t.mu.Lock()
	defer t.mu.Unlock()
	return len(t.pending)
}

// Timeout is how long a receiver has.
func (t *Table) Timeout() time.Duration { return t.timeout }
