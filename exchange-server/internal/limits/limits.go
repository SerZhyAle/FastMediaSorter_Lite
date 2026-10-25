// Package limits holds the deployment's load discipline (duty R-6, ticket 139): per-address token
// buckets, the bound on concurrent password-hash verifications and the pool of unauthenticated
// streams. Stdlib only (x/time is not in the offline module cache); every clock is injected.
package limits

import (
	"container/list"
	"errors"
	"io"
	"net"
	"net/netip"
	"sync"
	"time"
)

// Class is one kind of attempt that is rate-limited per client address.
type Class int

const (
	// Connection counts accepted connections, per client address.
	Connection Class = iota
	// UnknownID counts connects that named an id nobody holds, per client address.
	UnknownID
	// FailedAuth counts failed enroll and hello, per client address (ticket 162 rule 10).
	FailedAuth
	// AttachMiss counts attaches that named no pending tunnel, per client address.
	AttachMiss
	// FailedEnroll counts failed enroll, per login: the key is the folded login (ticket 162 rule 10).
	FailedEnroll
	// FailedHello counts failed hello, per account: the key is the account once the device is known.
	FailedHello
	// PairingRequest counts pairing-request, per account (ticket 167 rule 6).
	PairingRequest
	// BroadcastStart counts broadcast-start, per device (ticket 167 rule 6).
	BroadcastStart
	// Cast counts `cast`, per device (ticket 166 rule 6, ticket 167 rule 6).
	Cast
	// ListenCheck counts the credential checks of a protected broadcast's listen URL (item BF: at most one per
	// second), per broadcast and source address - the key is both. A check is a PBKDF2 of 600000 rounds, so the
	// token is taken BEFORE the hash and never refunded: a right password costs it as a wrong one does.
	ListenCheck
	classCount
)

// Rate is a token bucket: PerMinute tokens refill continuously up to Burst.
type Rate struct {
	PerMinute float64
	Burst     int
}

// ListenCheckRate is item BF's "at most one check per second": a bucket of one that refills in a second. It
// is fixed by the contract, so it is not a configurable limit.
func ListenCheckRate() Rate { return Rate{PerMinute: 60, Burst: 1} }

// Config is the rate of every class and the size of the address table.
type Config struct {
	Rates    [classCount]Rate
	MaxAddrs int
}

// DefaultConfig returns the working defaults (non-normative until rung 2, Q-133-8).
//
// Connection departs from ticket 139's 30/min burst 10: one SFTP client opens up to 15 sessions
// through the exchange (the phone's JSch pool, R5 section 2), each a connection from the same address,
// so a burst of 10 would turn a legitimate client away.
func DefaultConfig() Config {
	var c Config
	c.Rates[Connection] = Rate{PerMinute: 120, Burst: 40}
	c.Rates[UnknownID] = Rate{PerMinute: 10, Burst: 3}
	c.Rates[FailedAuth] = Rate{PerMinute: 5, Burst: 2}
	c.Rates[AttachMiss] = Rate{PerMinute: 10, Burst: 5}
	c.Rates[FailedEnroll] = Rate{PerMinute: 10, Burst: 5}
	c.Rates[FailedHello] = Rate{PerMinute: 10, Burst: 5}
	c.Rates[PairingRequest] = Rate{PerMinute: 6, Burst: 6}
	c.Rates[BroadcastStart] = Rate{PerMinute: 6, Burst: 6}
	c.Rates[Cast] = Rate{PerMinute: 10, Burst: 10}
	c.Rates[ListenCheck] = ListenCheckRate()
	c.MaxAddrs = 4096
	return c
}

type bucket struct {
	tokens float64
	last   time.Time
}

type entry struct {
	key     string
	buckets [classCount]bucket
	seen    [classCount]bool
}

// Limiter is a set of token buckets keyed by (address, class), held in an LRU table: the oldest
// address is dropped when the table is full, so an attacker cycling addresses resets only their own.
type Limiter struct {
	mu  sync.Mutex
	cfg Config
	now func() time.Time
	m   map[string]*list.Element
	lru *list.List
}

// NewLimiter builds a limiter. A nil now means time.Now.
func NewLimiter(cfg Config, now func() time.Time) *Limiter {
	if now == nil {
		now = time.Now
	}
	if cfg.MaxAddrs <= 0 {
		cfg.MaxAddrs = 4096
	}
	return &Limiter{cfg: cfg, now: now, m: map[string]*list.Element{}, lru: list.New()}
}

// Key turns a remote address into the table key: the IP, with IPv6 grouped by its /64 prefix.
func Key(a net.Addr) string {
	switch v := a.(type) {
	case *net.TCPAddr:
		return keyOfIP(v.IP)
	case nil:
		return ""
	default:
		host, _, err := net.SplitHostPort(a.String())
		if err != nil {
			host = a.String()
		}
		return KeyOfString(host)
	}
}

// KeyOfString is Key for an address already rendered as text.
func KeyOfString(host string) string {
	ip := net.ParseIP(host)
	if ip == nil {
		return host
	}
	return keyOfIP(ip)
}

func keyOfIP(ip net.IP) string {
	addr, ok := netip.AddrFromSlice(ip)
	if !ok {
		return ""
	}
	addr = addr.Unmap()
	if addr.Is6() {
		p, err := addr.Prefix(64)
		if err == nil {
			return p.String()
		}
	}
	return addr.String()
}

func (l *Limiter) entryFor(key string) *entry {
	if el, ok := l.m[key]; ok {
		l.lru.MoveToFront(el)
		return el.Value.(*entry)
	}
	e := &entry{key: key}
	l.m[key] = l.lru.PushFront(e)
	for l.lru.Len() > l.cfg.MaxAddrs {
		back := l.lru.Back()
		l.lru.Remove(back)
		delete(l.m, back.Value.(*entry).key)
	}
	return e
}

func (l *Limiter) refill(e *entry, c Class) *bucket {
	r := l.cfg.Rates[c]
	b := &e.buckets[c]
	now := l.now()
	if !e.seen[c] {
		e.seen[c] = true
		b.tokens = float64(r.Burst)
		b.last = now
		return b
	}
	if el := now.Sub(b.last); el > 0 {
		b.tokens += el.Minutes() * r.PerMinute
		if b.tokens > float64(r.Burst) {
			b.tokens = float64(r.Burst)
		}
		b.last = now
	}
	return b
}

// Allow takes one token and reports whether there was one.
func (l *Limiter) Allow(key string, c Class) bool {
	if l == nil {
		return true
	}
	l.mu.Lock()
	defer l.mu.Unlock()
	b := l.refill(l.entryFor(key), c)
	if b.tokens >= 1 {
		b.tokens--
		return true
	}
	return false
}

// Refund gives back a token taken by Allow. It is how a class that must be taken BEFORE an
// expensive step (the password hash) is kept for failures only: take first, refund on success. A
// parallel wave cannot all pass a "has a token" check before the first failure is counted.
func (l *Limiter) Refund(key string, c Class) {
	if l == nil {
		return
	}
	l.mu.Lock()
	defer l.mu.Unlock()
	b := l.refill(l.entryFor(key), c)
	b.tokens++
	if max := float64(l.cfg.Rates[c].Burst); b.tokens > max {
		b.tokens = max
	}
}

// Has reports whether a token is available without taking it.
func (l *Limiter) Has(key string, c Class) bool {
	if l == nil {
		return true
	}
	l.mu.Lock()
	defer l.mu.Unlock()
	return l.refill(l.entryFor(key), c).tokens >= 1
}

// Addresses returns how many addresses the table holds (for tests and counts-only log lines).
func (l *Limiter) Addresses() int {
	l.mu.Lock()
	defer l.mu.Unlock()
	return l.lru.Len()
}

// ErrBusy is returned by Verifier.Verify when no verification slot freed up in time.
var ErrBusy = errors.New("limits: password verifier busy")

// Verifier bounds how many password-hash verifications run at once. A verification is memory-hard
// (64 MiB), so without a bound the password check would be the cheapest way to exhaust the host.
type Verifier struct {
	verify func(string) bool
	sem    chan struct{}
	wait   time.Duration

	mu       sync.Mutex
	inFlight int
	peak     int
	calls    int
}

// NewVerifier wraps verify with at most concurrency simultaneous calls; a caller that cannot get a
// slot within wait gets ErrBusy.
func NewVerifier(verify func(string) bool, concurrency int, wait time.Duration) *Verifier {
	if concurrency < 1 {
		concurrency = 1
	}
	return &Verifier{verify: verify, sem: make(chan struct{}, concurrency), wait: wait}
}

// Verify runs the wrapped check inside the bound.
func (v *Verifier) Verify(password string) (bool, error) {
	return v.Run(func() bool { return v.verify(password) })
}

// Run runs fn - one memory-hard verification against a record the caller picked - inside the same
// bound. An unknown login runs the same fn against a dummy record, so the work and the time are
// the same and the bound counts both (ticket 162 rule 2).
func (v *Verifier) Run(fn func() bool) (bool, error) {
	t := time.NewTimer(v.wait)
	defer t.Stop()
	select {
	case v.sem <- struct{}{}:
	case <-t.C:
		return false, ErrBusy
	}
	defer func() { <-v.sem }()

	v.mu.Lock()
	v.inFlight++
	v.calls++
	if v.inFlight > v.peak {
		v.peak = v.inFlight
	}
	v.mu.Unlock()
	defer func() {
		v.mu.Lock()
		v.inFlight--
		v.mu.Unlock()
	}()
	return fn(), nil
}

// Calls is how many verifications actually ran (a refused or timed-out caller is not one).
func (v *Verifier) Calls() int {
	v.mu.Lock()
	defer v.mu.Unlock()
	return v.calls
}

// Peak is the highest number of verifications that ever ran at once.
func (v *Verifier) Peak() int {
	v.mu.Lock()
	defer v.mu.Unlock()
	return v.peak
}

// Hold takes a verification slot until the returned release function is called.
func (v *Verifier) Hold() func() {
	v.sem <- struct{}{}
	return func() { <-v.sem }
}

// PreAuth is the shared pool of streams that have not yet registered, attached or connected.
// When it is full, the OLDEST unauthenticated stream is evicted - never an authenticated one.
type PreAuth struct {
	mu    sync.Mutex
	max   int
	order *list.List
}

type slot struct{ c io.Closer }

// NewPreAuth builds a pool of max slots.
func NewPreAuth(max int) *PreAuth {
	if max < 1 {
		max = 1
	}
	return &PreAuth{max: max, order: list.New()}
}

// Enter takes a slot for c and returns the function that gives it back (call it once the stream
// has authenticated or closed; it is safe to call more than once).
func (p *PreAuth) Enter(c io.Closer) (leave func()) {
	if p == nil {
		return func() {}
	}
	p.mu.Lock()
	el := p.order.PushBack(&slot{c: c})
	var evict []io.Closer
	for p.order.Len() > p.max {
		front := p.order.Front()
		p.order.Remove(front)
		evict = append(evict, front.Value.(*slot).c)
	}
	p.mu.Unlock()
	for _, e := range evict {
		_ = e.Close()
	}
	var once sync.Once
	return func() {
		once.Do(func() {
			p.mu.Lock()
			// Remove only if still queued (an evicted element is already gone).
			for e := p.order.Front(); e != nil; e = e.Next() {
				if e == el {
					p.order.Remove(e)
					break
				}
			}
			p.mu.Unlock()
		})
	}
}

// Len is the number of unauthenticated streams currently held.
func (p *PreAuth) Len() int {
	p.mu.Lock()
	defer p.mu.Unlock()
	return p.order.Len()
}
