package ports

import (
	"errors"
	"net"
	"strconv"
	"sync"
	"testing"
	"time"
)

type fakeListener struct {
	port int
	ch   chan net.Conn
	once sync.Once
}

func (f *fakeListener) Accept() (net.Conn, error) {
	c, ok := <-f.ch
	if !ok {
		return nil, net.ErrClosed
	}
	return c, nil
}
func (f *fakeListener) Close() error   { f.once.Do(func() { close(f.ch) }); return nil }
func (f *fakeListener) Addr() net.Addr { return &net.TCPAddr{Port: f.port} }

// fakeBind opens fake listeners; ports in `taken` fail to bind like a port another program holds.
type fakeBind struct {
	mu    sync.Mutex
	taken map[int]bool
	open  map[int]*fakeListener
	calls []int
}

func newFakeBind(taken ...int) *fakeBind {
	f := &fakeBind{taken: map[int]bool{}, open: map[int]*fakeListener{}}
	for _, p := range taken {
		f.taken[p] = true
	}
	return f
}

func (f *fakeBind) bind(_ string, port int) (net.Listener, error) {
	f.mu.Lock()
	defer f.mu.Unlock()
	f.calls = append(f.calls, port)
	if f.taken[port] {
		return nil, errors.New("address in use")
	}
	l := &fakeListener{port: port, ch: make(chan net.Conn)}
	f.open[port] = l
	return l, nil
}

func nop(net.Conn) {}

func TestAnyPortTakesTheFirstFree(t *testing.T) {
	fb := newFakeBind()
	p := NewPool(100, 102, "h", fb.bind, nil)
	a, err := p.Lease(0, nop)
	if err != nil || a.Port() != 100 {
		t.Fatalf("%v %v", a, err)
	}
	b, _ := p.Lease(0, nop)
	if b.Port() != 101 {
		t.Fatalf("second lease got %d", b.Port())
	}
}

func TestRequestedPortGrantedWhenFree(t *testing.T) {
	p := NewPool(100, 105, "h", newFakeBind().bind, nil)
	l, err := p.Lease(104, nop)
	if err != nil || l.Port() != 104 {
		t.Fatalf("%v %v", l, err)
	}
}

func TestRequestedPortTakenGetsADifferentOne(t *testing.T) { // ask 2
	fb := newFakeBind(104) // another program holds it
	p := NewPool(100, 105, "h", fb.bind, nil)
	l, err := p.Lease(104, nop)
	if err != nil || l.Port() == 104 || l.Port() < 100 || l.Port() > 105 {
		t.Fatalf("must answer a different port in range: %v %v", l, err)
	}
	// Leasing the same number again while the first lease lives also gives a different port.
	p2 := NewPool(100, 105, "h", newFakeBind().bind, nil)
	first, _ := p2.Lease(103, nop)
	second, err := p2.Lease(103, nop)
	if err != nil || second.Port() == first.Port() {
		t.Fatalf("a leased port must not be handed out twice: %v %v", second, err)
	}
}

func TestOutOfRangeRequestGetsAPortInRange(t *testing.T) {
	p := NewPool(100, 102, "h", newFakeBind().bind, nil)
	l, err := p.Lease(9999, nop)
	if err != nil || l.Port() < 100 || l.Port() > 102 {
		t.Fatalf("%v %v", l, err)
	}
}

func TestExhaustedRangeIsPortUnavailable(t *testing.T) {
	p := NewPool(100, 101, "h", newFakeBind().bind, nil)
	p.Lease(0, nop)
	p.Lease(0, nop)
	if _, err := p.Lease(0, nop); !errors.Is(err, ErrPortUnavailable) {
		t.Fatalf("got %v", err)
	}
	fb := newFakeBind(100, 101)
	p = NewPool(100, 101, "h", fb.bind, nil)
	if _, err := p.Lease(0, nop); !errors.Is(err, ErrPortUnavailable) {
		t.Fatalf("a range nobody can bind: got %v", err)
	}
}

func TestBindFailureCooldown(t *testing.T) {
	now := time.Date(2026, 10, 6, 12, 0, 0, 0, time.UTC)
	fb := newFakeBind(100)
	p := NewPool(100, 101, "h", fb.bind, func() time.Time { return now })
	l, _ := p.Lease(0, nop)
	if l.Port() != 101 {
		t.Fatalf("got %d", l.Port())
	}
	l.Close()
	fb.calls = nil
	// 100 is in its cooldown: it is not even tried again.
	l2, _ := p.Lease(0, nop)
	if l2.Port() != 101 {
		t.Fatalf("got %d", l2.Port())
	}
	for _, c := range fb.calls {
		if c == 100 {
			t.Fatal("a port in cooldown was bound again")
		}
	}
	l2.Close()
	// After the cooldown it is tried again - and now it works.
	fb.mu.Lock()
	delete(fb.taken, 100)
	fb.mu.Unlock()
	now = now.Add(6 * time.Minute)
	l3, err := p.Lease(0, nop)
	if err != nil || l3.Port() != 100 {
		t.Fatalf("after the cooldown: %v %v", l3, err)
	}
}

func TestCloseFreesThePortAndStopsListening(t *testing.T) {
	fb := newFakeBind()
	p := NewPool(100, 100, "h", fb.bind, nil)
	l, _ := p.Lease(0, nop)
	if p.Leased() != 1 {
		t.Fatal("not leased")
	}
	l.Close()
	l.Close() // idempotent
	if p.Leased() != 0 {
		t.Fatal("not freed")
	}
	if _, err := fb.open[100].Accept(); err == nil {
		t.Fatal("the listener is still open")
	}
	if again, err := p.Lease(0, nop); err != nil || again.Port() != 100 {
		t.Fatalf("a freed port must be leasable again: %v %v", again, err)
	}
}

func TestHandlerReceivesAcceptedConnections(t *testing.T) {
	fb := newFakeBind()
	p := NewPool(100, 100, "h", fb.bind, nil)
	got := make(chan net.Conn, 1)
	l, _ := p.Lease(0, func(c net.Conn) { got <- c })
	defer l.Close()
	a, b := net.Pipe()
	defer a.Close()
	defer b.Close()
	fb.open[100].ch <- a
	select {
	case c := <-got:
		if c != a {
			t.Fatal("wrong connection")
		}
	case <-time.After(time.Second):
		t.Fatal("handler not called")
	}
}

func TestConcurrentLeasesNeverShareAPort(t *testing.T) {
	p := NewPool(100, 140, "h", newFakeBind().bind, nil)
	var wg sync.WaitGroup
	var mu sync.Mutex
	seen := map[int]int{}
	for i := 0; i < 40; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			l, err := p.Lease(0, nop)
			if err != nil {
				t.Error(err)
				return
			}
			mu.Lock()
			seen[l.Port()]++
			mu.Unlock()
		}()
	}
	wg.Wait()
	for port, n := range seen {
		if n != 1 {
			t.Errorf("port %d leased %d times", port, n)
		}
	}
	if len(seen) != 40 {
		t.Fatalf("%d distinct ports", len(seen))
	}
}

func TestRealBindOnLoopback(t *testing.T) {
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Skip("no loopback")
	}
	_, ps, _ := net.SplitHostPort(ln.Addr().String())
	free, _ := strconv.Atoi(ps)
	ln.Close()
	p := NewPool(free, free, "127.0.0.1", nil, nil)
	got := make(chan struct{}, 1)
	l, err := p.Lease(free, func(c net.Conn) { c.Close(); got <- struct{}{} })
	if err != nil {
		t.Skipf("port %d was taken meanwhile: %v", free, err)
	}
	c, err := net.Dial("tcp", net.JoinHostPort("127.0.0.1", strconv.Itoa(l.Port())))
	if err != nil {
		t.Fatal(err)
	}
	c.Close()
	select {
	case <-got:
	case <-time.After(2 * time.Second):
		t.Fatal("no connection reached the handler")
	}
	l.Close()
	if c, err := net.DialTimeout("tcp", net.JoinHostPort("127.0.0.1", strconv.Itoa(free)), time.Second); err == nil {
		c.Close()
		t.Fatal("the port still accepts after release")
	}
}
