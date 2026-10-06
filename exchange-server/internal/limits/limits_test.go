package limits

import (
	"io"
	"net"
	"sync"
	"sync/atomic"
	"testing"
	"time"
)

type clock struct {
	mu sync.Mutex
	t  time.Time
}

func (c *clock) now() time.Time { c.mu.Lock(); defer c.mu.Unlock(); return c.t }
func (c *clock) add(d time.Duration) {
	c.mu.Lock()
	c.t = c.t.Add(d)
	c.mu.Unlock()
}

func newClock() *clock { return &clock{t: time.Date(2026, 10, 6, 12, 0, 0, 0, time.UTC)} }

func TestBurstThenRefill(t *testing.T) {
	ck := newClock()
	cfg := DefaultConfig() // unknown-id: 10/min burst 3
	l := NewLimiter(cfg, ck.now)
	for i := 0; i < 3; i++ {
		if !l.Allow("a", UnknownID) {
			t.Fatalf("burst token %d refused", i)
		}
	}
	if l.Allow("a", UnknownID) || l.Has("a", UnknownID) {
		t.Fatal("the burst is spent, nothing may be left")
	}
	ck.add(5 * time.Second) // 10/min = one token per 6 s: not yet
	if l.Has("a", UnknownID) {
		t.Fatal("refilled too early")
	}
	ck.add(2 * time.Second)
	if !l.Allow("a", UnknownID) {
		t.Fatal("no token after 7 s")
	}
	ck.add(10 * time.Hour)
	for i := 0; i < 3; i++ {
		if !l.Allow("a", UnknownID) {
			t.Fatal("a long idle must refill to the burst")
		}
	}
	if l.Allow("a", UnknownID) {
		t.Fatal("refill must not exceed the burst")
	}
}

func TestRefundKeepsATokenForFailuresOnly(t *testing.T) {
	l := NewLimiter(DefaultConfig(), newClock().now) // failed-register: 5/min burst 2
	// Take-first, refund-on-success: a stream of correct passwords never uses the bucket up...
	for i := 0; i < 50; i++ {
		if !l.Allow("a", FailedRegister) {
			t.Fatalf("a refunded token must be available again (round %d)", i)
		}
		l.Refund("a", FailedRegister)
	}
	// ...two failures do.
	l.Allow("a", FailedRegister)
	l.Allow("a", FailedRegister)
	if l.Allow("a", FailedRegister) {
		t.Fatal("two unrefunded takes must exhaust a burst of 2")
	}
	// A refund never lifts the bucket above its burst.
	for i := 0; i < 10; i++ {
		l.Refund("a", FailedRegister)
	}
	l.Allow("a", FailedRegister)
	l.Allow("a", FailedRegister)
	if l.Allow("a", FailedRegister) {
		t.Fatal("refund exceeded the burst")
	}
}

func TestClassesAndAddressesAreIndependent(t *testing.T) {
	l := NewLimiter(DefaultConfig(), newClock().now)
	for l.Allow("a", UnknownID) {
	}
	if !l.Allow("a", FailedRegister) {
		t.Fatal("an exhausted class must not touch another class")
	}
	if !l.Allow("b", UnknownID) {
		t.Fatal("an exhausted address must not touch another address")
	}
}

func TestLRUDropsOldestAddress(t *testing.T) {
	cfg := DefaultConfig()
	cfg.MaxAddrs = 3
	l := NewLimiter(cfg, newClock().now)
	for l.Allow("old", UnknownID) {
	}
	for _, k := range []string{"x", "y", "z"} { // pushes "old" out
		l.Allow(k, UnknownID)
	}
	if l.Addresses() != 3 {
		t.Fatalf("table holds %d addresses, want 3", l.Addresses())
	}
	// "old" lost its (spent) bucket: an attacker cycling addresses only resets their own state.
	if !l.Allow("old", UnknownID) {
		t.Fatal("an evicted address starts fresh")
	}
}

func TestKeyGroupsIPv6By64(t *testing.T) {
	a := Key(&net.TCPAddr{IP: net.ParseIP("2001:db8:1:2:aaaa::1")})
	b := Key(&net.TCPAddr{IP: net.ParseIP("2001:db8:1:2:bbbb::9")})
	c := Key(&net.TCPAddr{IP: net.ParseIP("2001:db8:1:3::1")})
	if a != b {
		t.Fatalf("same /64 must share a key: %q vs %q", a, b)
	}
	if a == c {
		t.Fatal("different /64 must not share a key")
	}
	v4 := Key(&net.TCPAddr{IP: net.ParseIP("192.0.2.9")})
	mapped := Key(&net.TCPAddr{IP: net.ParseIP("::ffff:192.0.2.9")})
	if v4 != "192.0.2.9" || v4 != mapped {
		t.Fatalf("IPv4 keys: %q %q", v4, mapped)
	}
	if KeyOfString("192.0.2.9") != v4 {
		t.Fatal("KeyOfString disagrees with Key")
	}
}

func TestVerifierBound(t *testing.T) {
	var running, peak atomic.Int32
	v := NewVerifier(func(string) bool {
		n := running.Add(1)
		for {
			p := peak.Load()
			if n <= p || peak.CompareAndSwap(p, n) {
				break
			}
		}
		time.Sleep(40 * time.Millisecond)
		running.Add(-1)
		return false
	}, 2, 2*time.Second)
	var wg sync.WaitGroup
	for i := 0; i < 12; i++ {
		wg.Add(1)
		go func() { defer wg.Done(); _, _ = v.Verify("x") }()
	}
	wg.Wait()
	if peak.Load() > 2 || v.Peak() > 2 {
		t.Fatalf("more than 2 verifications at once: %d (verifier says %d)", peak.Load(), v.Peak())
	}
}

func TestVerifierGivesUp(t *testing.T) {
	release := make(chan struct{})
	v := NewVerifier(func(string) bool { <-release; return true }, 1, 50*time.Millisecond)
	go func() { _, _ = v.Verify("a") }()
	time.Sleep(20 * time.Millisecond)
	if _, err := v.Verify("b"); err != ErrBusy {
		t.Fatalf("a waiter past the wait must get ErrBusy, got %v", err)
	}
	close(release)
}

type fakeCloser struct{ closed atomic.Bool }

func (f *fakeCloser) Close() error { f.closed.Store(true); return nil }

var _ io.Closer = (*fakeCloser)(nil)

func TestPreAuthEvictsOldestUnauthenticated(t *testing.T) {
	p := NewPreAuth(2)
	a, b, c := &fakeCloser{}, &fakeCloser{}, &fakeCloser{}
	leaveA := p.Enter(a)
	p.Enter(b)
	_ = leaveA // a authenticates: gives its slot back
	leaveA()
	leaveA() // idempotent
	if a.closed.Load() {
		t.Fatal("a stream that left must not be closed")
	}
	p.Enter(c)
	if p.Len() != 2 || b.closed.Load() || c.closed.Load() {
		t.Fatalf("len=%d b=%v c=%v", p.Len(), b.closed.Load(), c.closed.Load())
	}
	d := &fakeCloser{}
	p.Enter(d) // full: the OLDEST unauthenticated (b) goes
	if !b.closed.Load() || c.closed.Load() || d.closed.Load() {
		t.Fatalf("eviction picked the wrong stream: b=%v c=%v d=%v", b.closed.Load(), c.closed.Load(), d.closed.Load())
	}
}
