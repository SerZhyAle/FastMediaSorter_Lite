package accounts

import (
	"strings"
	"sync"
	"time"
)

// Pacer is the pace of password checks (DEVICE-EXCHANGE 19 item AW): at most one check a second per login
// and per client address, and nothing else. There is no counter of wrong passwords, no lockout and no
// growing delay: a person who has typed a hundred wrong passwords still has the right one evaluated
// within a second of its turn, because nothing here remembers a failure - only when a check last ran.
//
// A check that arrives inside the second does not wait for ever: it waits at most one slot, and one more
// behind it is turned away at once (rate-limited, not evaluated). That is what keeps a stranger who
// hammers a login from queueing the person who owns it for longer than a second.
type Pacer struct {
	mu       sync.Mutex
	now      func() time.Time
	sleep    func(time.Duration)
	interval time.Duration
	byKey    map[string]time.Time // when the next check of this key may start
	max      int
}

// PaceInterval is the time between two checks of one login or one address.
const PaceInterval = time.Second

// MaxPaceWait is the longest a check waits for its turn. One slot: a second.
const MaxPaceWait = PaceInterval

// paceTableMax bounds the table. A key that is evicted loses only its own memory of "a check ran a
// moment ago", which lets one extra check through - harmless, and the price of a table that a stranger
// cycling logins cannot grow.
const paceTableMax = 8192

// newPacer makes the pacer. interval is the time between two checks of one key: 0 is PaceInterval (the
// contract's one second), a negative one switches pacing off - a test that is not about the pace and
// enrolls many devices in a row asks for that, and nothing in a server does.
func newPacer(now func() time.Time, sleep func(time.Duration), interval time.Duration) *Pacer {
	if now == nil {
		now = time.Now
	}
	if sleep == nil {
		sleep = time.Sleep
	}
	switch {
	case interval == 0:
		interval = PaceInterval
	case interval < 0:
		interval = 0
	}
	return &Pacer{now: now, sleep: sleep, interval: interval, byKey: map[string]time.Time{}, max: paceTableMax}
}

func paceKey(kind, s string) string { return kind + "\x00" + strings.ToLower(strings.TrimSpace(s)) }

// Reserve claims the next slot of every given key. keys are made with LoginKey and AddrKey. It
// returns how long the caller must wait before it evaluates, and false when the wait would be longer
// than one slot (the check is turned away and nothing is claimed).
func (p *Pacer) Reserve(keys ...string) (wait time.Duration, ok bool) {
	p.mu.Lock()
	defer p.mu.Unlock()
	now := p.now()
	start := now
	for _, k := range keys {
		if k == "" {
			continue
		}
		if next, held := p.byKey[k]; held && next.After(start) {
			start = next
		}
	}
	wait = start.Sub(now)
	if wait > p.interval {
		return wait, false
	}
	for _, k := range keys {
		if k == "" {
			continue
		}
		p.byKey[k] = start.Add(p.interval)
	}
	p.trimLocked(now)
	return wait, true
}

// trimLocked drops keys whose slot has passed once the table is over its bound; when everything is
// still live it drops the oldest half's worth, one entry at a time, by the earliest slot.
func (p *Pacer) trimLocked(now time.Time) {
	if len(p.byKey) <= p.max {
		return
	}
	for k, next := range p.byKey {
		if !next.After(now) {
			delete(p.byKey, k)
		}
	}
	for len(p.byKey) > p.max {
		var oldest string
		var at time.Time
		for k, next := range p.byKey {
			if oldest == "" || next.Before(at) {
				oldest, at = k, next
			}
		}
		delete(p.byKey, oldest)
	}
}

// LoginKey is the pace key of a login (case folded).
func LoginKey(login string) string { return paceKey("login", login) }

// AddrKey is the pace key of a client address (the limiter's key: the IP, IPv6 by its /64).
func AddrKey(addr string) string { return paceKey("addr", addr) }

// Wait takes a turn for one password check of `login` from `addr` and blocks until it arrives. It
// returns false when the check must be turned away (rate-limited, not evaluated). An empty addr is
// paced by the login alone.
func (p *Pacer) Wait(login, addr string) bool {
	keys := []string{LoginKey(login)}
	if addr != "" {
		keys = append(keys, AddrKey(addr))
	}
	wait, ok := p.Reserve(keys...)
	if !ok {
		return false
	}
	if wait > 0 {
		p.sleep(wait)
	}
	return true
}
