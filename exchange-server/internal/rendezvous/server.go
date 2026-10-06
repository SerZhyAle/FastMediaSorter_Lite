// Package rendezvous is the protocol core of the exchange server (duties R-1 to R-4): the listener
// with its first-byte split, the registration table with its keepalive lifetime, the consumer's
// connect, and the open/attach handshake that gives every tunnel its own data stream.
//
// Everything here is pure Go and OS-free; limits, the password check and the log come in through
// the types of their own packages. Sessions live in memory only and nothing is ever written to disk.
package rendezvous

import (
	"bufio"
	"crypto/subtle"
	"crypto/tls"
	"encoding/json"
	"errors"
	"net"
	"sync"
	"time"

	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/ports"
	"fms/exchange-server/internal/relay"
	"fms/exchange-server/internal/wire"
)

// Params are the protocol's numbers: working defaults until rung 2 freezes them (Q-133-8).
type Params struct {
	KeepaliveDefault  int // seconds, used when a producer proposes none
	KeepaliveMin      int
	KeepaliveMax      int
	WindowMultiplier  int // missed intervals that end a registration
	TunnelCap         int // simultaneous tunnels per share id (Q136-1)
	PendingWindow     time.Duration
	UnavailableDelay  time.Duration // fixed minimum before an unavailable answer
	IdleTimeout       time.Duration // a bridge with no byte either way is closed
	PeekTimeout       time.Duration // for the first byte
	HandshakeTimeout  time.Duration
	FirstFrameTimeout time.Duration
	MaxRegistrations  int
	MaxConns          int
	SweepInterval     time.Duration
}

// DefaultParams returns the working defaults.
func DefaultParams() Params {
	return Params{
		KeepaliveDefault: 30, KeepaliveMin: 10, KeepaliveMax: 120, WindowMultiplier: 3,
		TunnelCap:         16,
		PendingWindow:     10 * time.Second,
		UnavailableDelay:  250 * time.Millisecond,
		IdleTimeout:       10 * time.Minute,
		PeekTimeout:       5 * time.Second,
		HandshakeTimeout:  15 * time.Second,
		FirstFrameTimeout: 10 * time.Second,
		MaxRegistrations:  200,
		MaxConns:          1000,
		SweepInterval:     time.Second,
	}
}

// PasswordVerifier checks a registration's password inside a bound on concurrent verifications.
// limits.ErrBusy means no slot freed up in time.
type PasswordVerifier interface {
	Verify(password string) (bool, error)
}

// Config is everything a Server needs.
type Config struct {
	Params   Params
	TLS      *tls.Config
	Verifier PasswordVerifier
	Limiter  *limits.Limiter
	PreAuth  *limits.PreAuth
	Pool     *ports.Pool // nil: no third-party ports, a port request is port-unavailable
	Log      *logx.Logger
	Now      func() time.Time
}

// Server is one deployment's rendezvous and relay.
type Server struct {
	cfg Config
	p   Params
	now func() time.Time

	closed    chan struct{}
	closeOnce sync.Once

	mu      sync.Mutex
	ln      net.Listener
	regs    map[string]*registration
	pending map[string]*tunnel // by tunnel id: waiting for their attach
	conns   map[*trackedConn]struct{}
}

// New builds a server. TLS, Verifier and Limiter are required for Serve.
func New(cfg Config) *Server {
	s := &Server{
		cfg: cfg, p: cfg.Params, now: cfg.Now,
		closed:  make(chan struct{}),
		regs:    map[string]*registration{},
		pending: map[string]*tunnel{},
		conns:   map[*trackedConn]struct{}{},
	}
	if s.now == nil {
		s.now = time.Now
	}
	return s
}

// Addr is the listener's address once Serve runs.
func (s *Server) Addr() net.Addr {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.ln == nil {
		return nil
	}
	return s.ln.Addr()
}

// Registrations is the number of live registrations (a count, never a list).
func (s *Server) Registrations() int {
	s.mu.Lock()
	defer s.mu.Unlock()
	return len(s.regs)
}

// Serve accepts connections until Close. It returns nil after Close.
func (s *Server) Serve(ln net.Listener) error {
	s.mu.Lock()
	s.ln = ln
	s.mu.Unlock()
	go s.sweepLoop()
	for {
		c, err := ln.Accept()
		if err != nil {
			select {
			case <-s.closed:
				return nil
			default:
			}
			var ne net.Error
			if errors.As(err, &ne) && ne.Timeout() {
				time.Sleep(50 * time.Millisecond)
				continue
			}
			return err
		}
		tc := s.track(c)
		if tc == nil {
			continue
		}
		go s.handle(tc)
	}
}

// Close stops accepting, ends every registration and closes every stream.
func (s *Server) Close() {
	s.closeOnce.Do(func() {
		close(s.closed)
		s.mu.Lock()
		ln := s.ln
		var regs []*registration
		for _, r := range s.regs {
			regs = append(regs, r)
		}
		var conns []*trackedConn
		for c := range s.conns {
			conns = append(conns, c)
		}
		s.mu.Unlock()
		if ln != nil {
			_ = ln.Close()
		}
		for _, r := range regs {
			s.end(r, "shutdown")
		}
		for _, c := range conns {
			_ = c.Close()
		}
	})
}

func (s *Server) sweepLoop() {
	t := time.NewTicker(s.p.SweepInterval)
	defer t.Stop()
	for {
		select {
		case <-s.closed:
			return
		case <-t.C:
			s.Sweep(s.now())
		}
	}
}

// Sweep ends every registration whose keepalive window has passed at now.
func (s *Server) Sweep(now time.Time) {
	s.mu.Lock()
	var dead []*registration
	for _, r := range s.regs {
		if now.After(r.deadline) {
			dead = append(dead, r)
		}
	}
	s.mu.Unlock()
	for _, r := range dead {
		s.end(r, "expired")
	}
}

// --- connections ---

type trackedConn struct {
	net.Conn
	s    *Server
	once sync.Once
}

func (c *trackedConn) Close() error {
	c.once.Do(func() {
		c.s.mu.Lock()
		delete(c.s.conns, c)
		c.s.mu.Unlock()
	})
	return c.Conn.Close()
}

func (c *trackedConn) CloseWrite() error {
	if cw, ok := c.Conn.(interface{ CloseWrite() error }); ok {
		return cw.CloseWrite()
	}
	return nil
}

// track admits a connection or closes it when the deployment is at its connection or rate cap.
func (s *Server) track(c net.Conn) *trackedConn {
	key := limits.Key(c.RemoteAddr())
	s.mu.Lock()
	full := len(s.conns) >= s.p.MaxConns
	s.mu.Unlock()
	if full || !s.cfg.Limiter.Allow(key, limits.Connection) {
		s.cfg.Log.Warn("connection-refused", logx.Addr(key), logx.Text("why", ternary(full, "connection-cap", "rate-limited")))
		_ = c.Close()
		return nil
	}
	tc := &trackedConn{Conn: c, s: s}
	s.mu.Lock()
	s.conns[tc] = struct{}{}
	s.mu.Unlock()
	return tc
}

func ternary(c bool, a, b string) string {
	if c {
		return a
	}
	return b
}

// bufConn reads through a bufio.Reader that already holds the first byte, and keeps whatever it
// buffered beyond a frame, so nothing is lost when a stream turns raw after `connected`.
type bufConn struct {
	net.Conn
	r *bufio.Reader
}

func (c *bufConn) Read(p []byte) (int, error) { return c.r.Read(p) }

func (c *bufConn) CloseWrite() error {
	if cw, ok := c.Conn.(interface{ CloseWrite() error }); ok {
		return cw.CloseWrite()
	}
	return nil
}

const writeTimeout = 5 * time.Second

func writeMsg(stream net.Conn, m wire.Message) error {
	_ = stream.SetWriteDeadline(time.Now().Add(writeTimeout))
	err := wire.NewFrameWriter(stream).Write(m)
	_ = stream.SetWriteDeadline(time.Time{})
	return err
}

// handle is the first-byte split: 0x16 is a TLS record, 0x00 the first byte of a plain frame
// (a frame is at most 16 KiB, so its 4-byte big-endian length starts with a zero byte); anything
// else is closed without a word.
func (s *Server) handle(raw *trackedConn) {
	leave := s.cfg.PreAuth.Enter(raw)
	defer leave()
	key := limits.Key(raw.RemoteAddr())

	_ = raw.SetReadDeadline(time.Now().Add(s.p.PeekTimeout))
	br := bufio.NewReaderSize(raw, 512)
	b, err := br.Peek(1)
	if err != nil {
		_ = raw.Close()
		return
	}
	bc := &bufConn{Conn: raw, r: br}

	var stream net.Conn
	secure := false
	switch b[0] {
	case 0x16:
		tc := tls.Server(bc, s.cfg.TLS)
		_ = tc.SetDeadline(time.Now().Add(s.p.HandshakeTimeout))
		if err := tc.Handshake(); err != nil {
			_ = raw.Close()
			return
		}
		stream, secure = tc, true
	case 0x00:
		stream = bc
	default:
		_ = raw.Close()
		return
	}

	_ = stream.SetReadDeadline(time.Now().Add(s.p.FirstFrameTimeout))
	fr := wire.NewFrameReader(stream)
	var first wire.Message
	for i := 0; ; i++ {
		m, err := fr.Read()
		if err != nil || i >= 8 {
			_ = stream.Close()
			return
		}
		if m.Known() { // an unknown type is ignored, never an error (contract section 6.1)
			first = m
			break
		}
	}
	t0 := time.Now()
	leave()
	_ = stream.SetReadDeadline(time.Time{})

	switch first.Type {
	case wire.TypeRegister:
		if !secure {
			s.refuse(stream, wire.ReasonTLSRequired)
			return
		}
		s.control(stream, first, key)
	case wire.TypeAttach:
		if !secure {
			s.refuse(stream, wire.ReasonTLSRequired)
			return
		}
		s.attach(stream, first, key)
	case wire.TypeConnect:
		s.consumer(stream, first, key, t0)
	default:
		// A known envelope that is invalid in the stream's first state closes the stream.
		_ = stream.Close()
	}
}

func (s *Server) refuse(stream net.Conn, reason string) {
	_ = writeMsg(stream, wire.Refused(reason))
	_ = stream.Close()
}

// unavailable answers a consumer that asked for an unknown, ended or full share id. It is ONE code
// path with ONE fixed minimum delay, so neither the bytes nor the timing tell the cases apart.
func (s *Server) unavailable(stream net.Conn, t0 time.Time) {
	if d := s.p.UnavailableDelay - time.Since(t0); d > 0 {
		time.Sleep(d)
	}
	s.refuse(stream, wire.ReasonUnavailable)
}

// --- the producer ---

type ctrl struct {
	conn net.Conn
	mu   sync.Mutex
}

func (c *ctrl) write(m wire.Message) error {
	c.mu.Lock()
	defer c.mu.Unlock()
	return writeMsg(c.conn, m)
}

func (c *ctrl) close() { _ = c.conn.Close() }

type registration struct {
	id       string
	ctrl     *ctrl
	interval time.Duration
	deadline time.Time
	resume   string
	lease    *ports.Lease
	claim    json.RawMessage // stored opaquely for the registration's life, never interpreted
	tunnels  map[string]*tunnel
	ended    bool
}

func (s *Server) clampInterval(seconds int) time.Duration {
	if seconds <= 0 {
		seconds = s.p.KeepaliveDefault
	}
	if seconds < s.p.KeepaliveMin {
		seconds = s.p.KeepaliveMin
	}
	if seconds > s.p.KeepaliveMax {
		seconds = s.p.KeepaliveMax
	}
	return time.Duration(seconds) * time.Second
}

func (s *Server) control(stream net.Conn, first wire.Message, key string) {
	ct := &ctrl{conn: stream}
	refuse := func(reason string) {
		s.cfg.Log.Info("register-refused", logx.Addr(key), logx.Text("reason", reason))
		_ = ct.write(wire.Refused(reason))
		ct.close()
	}
	if !wire.ValidID(first.ShareID) {
		ct.close()
		return
	}
	// A failed registration costs a memory-hard hash, so the token is taken BEFORE the hash runs
	// (an address with none left never reaches Argon2id) and given back when the password was right.
	if !s.cfg.Limiter.Allow(key, limits.FailedRegister) {
		refuse(wire.ReasonRateLimited)
		return
	}
	ok, err := s.cfg.Verifier.Verify(first.Password)
	if err != nil {
		s.cfg.Limiter.Refund(key, limits.FailedRegister) // our busy state is not the client's failure
		refuse(wire.ReasonCapacity)
		return
	}
	if !ok {
		refuse(wire.ReasonBadPassword)
		return
	}
	s.cfg.Limiter.Refund(key, limits.FailedRegister)
	interval := s.clampInterval(first.KeepaliveSeconds)

	// Decide under one lock: collision or resume, capacity, the port lease, the insert.
	s.mu.Lock()
	reg := s.regs[first.ShareID]
	var old *ctrl
	resumed := false
	if reg != nil {
		if first.ResumeToken == "" || subtle.ConstantTimeCompare([]byte(first.ResumeToken), []byte(reg.resume)) != 1 {
			s.mu.Unlock()
			refuse(wire.ReasonIDCollision)
			return
		}
		old, resumed = reg.ctrl, true
	} else {
		if len(s.regs) >= s.p.MaxRegistrations {
			s.mu.Unlock()
			refuse(wire.ReasonCapacity)
			return
		}
		reg = &registration{id: first.ShareID, tunnels: map[string]*tunnel{}}
	}
	if reg.lease == nil && first.Port != nil {
		if s.cfg.Pool == nil {
			s.mu.Unlock()
			refuse(wire.ReasonPortUnavailable)
			return
		}
		lease, err := s.cfg.Pool.Lease(*first.Port, func(c net.Conn) { s.portConn(reg, c) })
		if err != nil {
			s.mu.Unlock()
			refuse(wire.ReasonPortUnavailable)
			return
		}
		reg.lease = lease
	}
	reg.ctrl, reg.interval = ct, interval
	reg.deadline = s.now().Add(interval * time.Duration(s.p.WindowMultiplier))
	reg.resume = wire.NewID()
	reg.claim = first.Claim
	s.regs[reg.id] = reg
	reply := wire.New(wire.TypeRegistered)
	reply.KeepaliveSeconds = int(interval / time.Second)
	reply.ResumeToken = reg.resume
	leased := 0
	if reg.lease != nil {
		leased = reg.lease.Port()
		reply.Port = wire.IntPtr(leased)
	}
	s.mu.Unlock()

	if old != nil {
		old.close()
	}
	if err := ct.write(reply); err != nil {
		if resumed {
			s.controlGone(reg, ct)
		} else {
			s.end(reg, "write-failed")
		}
		return
	}
	s.cfg.Log.Info(ternary(resumed, "resumed", "registered"), logx.Share(reg.id), logx.Addr(key),
		logx.Int("keepalive", reply.KeepaliveSeconds), logx.Int("port", leased))
	s.readControl(reg, ct, stream)
}

// readControl serves a registration's control stream: envelopes only, for its whole life.
func (s *Server) readControl(reg *registration, ct *ctrl, stream net.Conn) {
	fr := wire.NewFrameReader(stream)
	for {
		m, err := fr.Read()
		if err != nil {
			s.controlGone(reg, ct)
			return
		}
		switch m.Type {
		case wire.TypeKeepalive:
			s.mu.Lock()
			if reg.ctrl == ct && !reg.ended {
				reg.deadline = s.now().Add(reg.interval * time.Duration(s.p.WindowMultiplier))
			}
			s.mu.Unlock()
			if err := ct.write(wire.New(wire.TypeKeepalive)); err != nil {
				s.controlGone(reg, ct)
				return
			}
		case wire.TypeUnregister:
			s.end(reg, "unregister") // no reply (Q134-11)
			return
		default:
			if m.Known() { // out of state: the stream closes (Q134-12); the registration waits to expire
				s.controlGone(reg, ct)
				return
			}
			// an unknown type is ignored
		}
	}
}

// controlGone drops a control stream. The registration itself lives until its keepalive window
// passes, so a producer that reconnects inside it can resume (Q134-8).
func (s *Server) controlGone(reg *registration, ct *ctrl) {
	s.mu.Lock()
	if reg.ctrl == ct {
		reg.ctrl = nil
	}
	s.mu.Unlock()
	ct.close()
}

// end removes a registration: its stream, its port lease and every tunnel it still has.
func (s *Server) end(reg *registration, why string) {
	s.mu.Lock()
	if reg.ended {
		s.mu.Unlock()
		return
	}
	reg.ended = true
	if s.regs[reg.id] == reg {
		delete(s.regs, reg.id)
	}
	var ts []*tunnel
	for id, t := range reg.tunnels {
		ts = append(ts, t)
		delete(s.pending, id)
	}
	lease, ct := reg.lease, reg.ctrl
	reg.ctrl, reg.lease = nil, nil
	s.mu.Unlock()

	if ct != nil {
		ct.close()
	}
	if lease != nil {
		lease.Close()
	}
	for _, t := range ts {
		t.abort()
	}
	s.cfg.Log.Info("ended", logx.Share(reg.id), logx.Text("why", why))
}

// --- tunnels ---

type tunnel struct {
	id       string
	reg      *registration
	consumer net.Conn
	attached chan net.Conn
	done     chan struct{}
	once     sync.Once

	mu   sync.Mutex
	data net.Conn
}

func (t *tunnel) abort() {
	t.once.Do(func() {
		close(t.done)
		_ = t.consumer.Close()
		t.mu.Lock()
		d := t.data
		t.mu.Unlock()
		if d != nil {
			_ = d.Close()
		}
	})
}

// deliver hands a producer's data stream to the bridge waiting for it. If the tunnel was aborted
// (its registration ended) in the meantime nobody will ever read the stream, so it is closed here
// instead of leaking until the peer gives up.
func (t *tunnel) deliver(d net.Conn) {
	t.attached <- d
	select {
	case <-t.done:
		select {
		case x := <-t.attached:
			_ = x.Close()
		default:
		}
	default:
	}
}

func (t *tunnel) setData(d net.Conn) {
	t.mu.Lock()
	t.data = d
	t.mu.Unlock()
	select {
	case <-t.done:
		_ = d.Close()
	default:
	}
}

// bridge asks the producer for a data stream (`open`), waits for its `attach`, then splices the
// consumer onto it. announce sends `connected {}` first (the consumer's connect path); a raw
// third-party client on a per-session port gets no envelope at all. It returns false when no
// bridge came to be (full, no control stream, no attach in time, ended).
func (s *Server) bridge(reg *registration, consumer net.Conn, announce bool) bool {
	t := &tunnel{id: wire.NewID(), reg: reg, consumer: consumer,
		attached: make(chan net.Conn, 1), done: make(chan struct{})}
	s.mu.Lock()
	if reg.ended || reg.ctrl == nil || len(reg.tunnels) >= s.p.TunnelCap {
		s.mu.Unlock()
		return false
	}
	reg.tunnels[t.id] = t
	s.pending[t.id] = t
	ct := reg.ctrl
	s.mu.Unlock()
	defer func() {
		s.mu.Lock()
		delete(reg.tunnels, t.id)
		delete(s.pending, t.id)
		s.mu.Unlock()
	}()

	open := wire.New(wire.TypeOpen)
	open.TunnelID = t.id
	if err := ct.write(open); err != nil {
		return false
	}

	var data net.Conn
	timer := time.NewTimer(s.p.PendingWindow)
	defer timer.Stop()
	select {
	case data = <-t.attached:
	case <-timer.C:
		s.mu.Lock()
		_, stillPending := s.pending[t.id]
		delete(s.pending, t.id)
		s.mu.Unlock()
		if stillPending {
			return false
		}
		data = <-t.attached // the attach won the race: it deletes first and sends after
	case <-t.done:
		return false
	case <-s.closed:
		return false
	}
	t.setData(data)
	if announce {
		if err := writeMsg(consumer, wire.New(wire.TypeConnected)); err != nil {
			t.abort()
			return true
		}
	}
	st := relay.Splice(consumer, data, s.p.IdleTimeout)
	s.cfg.Log.Info("tunnel-closed", logx.Share(reg.id), logx.Int("to-producer", clampInt(st.AToB)), logx.Int("to-consumer", clampInt(st.BToA)))
	return true
}

func clampInt(n int64) int {
	if n > int64(^uint(0)>>1) {
		return int(^uint(0) >> 1)
	}
	return int(n)
}

// portConn is a raw client on a registration's per-session port: bridged like a connect, with no
// envelope on its side, because WinSCP and FileZilla speak SSH from the first byte.
func (s *Server) portConn(reg *registration, c net.Conn) {
	key := limits.Key(c.RemoteAddr())
	if !s.cfg.Limiter.Allow(key, limits.Connection) {
		_ = c.Close()
		return
	}
	go func() {
		s.bridge(reg, c, false)
		_ = c.Close()
	}()
}

// attach hands a producer's data stream to the consumer waiting for it. An attach that names no
// pending tunnel is closed without an answer.
func (s *Server) attach(stream net.Conn, m wire.Message, key string) {
	if !s.cfg.Limiter.Allow(key, limits.AttachMiss) {
		_ = stream.Close()
		return
	}
	s.mu.Lock()
	t := s.pending[m.TunnelID]
	if t == nil || t.reg.id != m.ShareID {
		s.mu.Unlock()
		_ = stream.Close()
		return
	}
	s.cfg.Limiter.Refund(key, limits.AttachMiss) // a hit costs nothing
	delete(s.pending, m.TunnelID)                // a tunnel id is single use
	s.mu.Unlock()
	_ = stream.SetDeadline(time.Time{})
	t.deliver(stream)
}

// --- the consumer ---

func (s *Server) consumer(stream net.Conn, m wire.Message, key string, t0 time.Time) {
	if !wire.ValidID(m.ShareID) {
		s.unavailable(stream, t0)
		return
	}
	// An address that has used up its unknown-id attempts is answered "unavailable" without a
	// lookup at all, so guessing ids is bounded by the bucket, not by the network.
	if !s.cfg.Limiter.Allow(key, limits.UnknownID) {
		s.unavailable(stream, t0)
		return
	}
	s.mu.Lock()
	reg := s.regs[m.ShareID]
	known := reg != nil && !reg.ended
	s.mu.Unlock()
	if !known {
		s.cfg.Log.Info("connect-unavailable", logx.Addr(key))
		s.unavailable(stream, t0)
		return
	}
	s.cfg.Limiter.Refund(key, limits.UnknownID) // a known id costs nothing
	if !s.bridge(reg, stream, true) {
		s.unavailable(stream, t0)
	}
}
