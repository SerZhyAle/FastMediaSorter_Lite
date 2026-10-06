// Package ports is the per-session public TCP port pool (duty R-5, ticket 137): the only surface
// third-party SFTP clients (WinSCP, FileZilla) ever touch.
//
// A lease binds its listener BEFORE the caller answers `registered`, so the port a producer is told
// about is a port that really listens. Never SO_REUSEADDR: on Windows it lets an unrelated process
// steal a live port. (Go sets no SO_REUSEADDR for Windows listeners; do not add one.)
package ports

import (
	"errors"
	"net"
	"strconv"
	"sync"
	"time"
)

// ErrPortUnavailable means no port of the range could be leased: the wire's `port-unavailable`.
var ErrPortUnavailable = errors.New("ports: no port available in the configured range")

// BindFunc opens a listener on host:port.
type BindFunc func(host string, port int) (net.Listener, error)

// TCPBind is the real BindFunc.
func TCPBind(host string, port int) (net.Listener, error) {
	return net.Listen("tcp", net.JoinHostPort(host, strconv.Itoa(port)))
}

// Pool leases ports from [lo, hi].
type Pool struct {
	lo, hi   int
	host     string
	bind     BindFunc
	now      func() time.Time
	cooldown time.Duration

	mu     sync.Mutex
	leased map[int]*Lease
	busy   map[int]time.Time
}

// NewPool builds a pool. host is the interface the ports bind to (the rendezvous listener's host);
// a nil bind means TCPBind and a nil now means time.Now.
func NewPool(lo, hi int, host string, bind BindFunc, now func() time.Time) *Pool {
	if bind == nil {
		bind = TCPBind
	}
	if now == nil {
		now = time.Now
	}
	return &Pool{lo: lo, hi: hi, host: host, bind: bind, now: now, cooldown: 5 * time.Minute,
		leased: map[int]*Lease{}, busy: map[int]time.Time{}}
}

// SetCooldown changes how long a port that failed to bind is skipped (default 5 minutes).
func (p *Pool) SetCooldown(d time.Duration) { p.cooldown = d }

// Range returns the pool's bounds.
func (p *Pool) Range() (lo, hi int) { return p.lo, p.hi }

// Lease picks a port and starts listening. requested == 0 takes the first free port; requested == N
// takes N when it is in range, free and bindable, and otherwise a different one (ask 2: the producer
// shows what it got). handler is called, on the listener's goroutine, once per accepted connection
// and must not block. When nothing can be bound the error is ErrPortUnavailable.
func (p *Pool) Lease(requested int, handler func(net.Conn)) (*Lease, error) {
	p.mu.Lock()
	defer p.mu.Unlock()
	now := p.now()
	try := func(port int) *Lease {
		if _, taken := p.leased[port]; taken {
			return nil
		}
		if until, ok := p.busy[port]; ok {
			if now.Before(until) {
				return nil
			}
			delete(p.busy, port)
		}
		ln, err := p.bind(p.host, port)
		if err != nil {
			p.busy[port] = now.Add(p.cooldown)
			return nil
		}
		l := &Lease{port: port, ln: ln, pool: p}
		p.leased[port] = l
		go l.accept(handler)
		return l
	}
	if requested >= p.lo && requested <= p.hi {
		if l := try(requested); l != nil {
			return l, nil
		}
	}
	for port := p.lo; port <= p.hi; port++ {
		if port == requested {
			continue
		}
		if l := try(port); l != nil {
			return l, nil
		}
	}
	return nil, ErrPortUnavailable
}

// Leased is the number of ports currently leased.
func (p *Pool) Leased() int {
	p.mu.Lock()
	defer p.mu.Unlock()
	return len(p.leased)
}

// Lease is one listening port, owned by one registration.
type Lease struct {
	port int
	ln   net.Listener
	pool *Pool
	once sync.Once
}

// Port is the port that is really listening.
func (l *Lease) Port() int { return l.port }

func (l *Lease) accept(handler func(net.Conn)) {
	for {
		c, err := l.ln.Accept()
		if err != nil {
			if ne, ok := err.(net.Error); ok && ne.Timeout() {
				continue
			}
			return
		}
		handler(c)
	}
}

// Close stops listening and frees the port. Safe to call more than once.
func (l *Lease) Close() {
	l.once.Do(func() {
		_ = l.ln.Close()
		l.pool.mu.Lock()
		delete(l.pool.leased, l.port)
		l.pool.mu.Unlock()
	})
}
