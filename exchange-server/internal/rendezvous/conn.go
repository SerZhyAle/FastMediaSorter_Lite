// Package rendezvous is the protocol core of the exchange server: the one listener with its three
// openings and its stream state machine (tickets 161), behind which the handler sets of accounts
// (162), the directory (163), tunnels and the third-party port (164) - and later the relay (165)
// and cast (166) - do their work.
//
// This file is the connection layer: the accept loop, the first-byte split, TLS, the HTTP opening
// and the tracked-connection set. stream.go is the state machine; engine*.go is the real Backend.
// Everything here is pure Go and OS-free; limits and the log come in through the types of their
// own packages. Nothing a handler learns is written to disk from this package.
package rendezvous

import (
	"bufio"
	"crypto/tls"
	"errors"
	"fmt"
	"net"
	"strings"
	"sync"
	"time"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/admin"
	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/wire"
)

// Config is everything a Server needs.
type Config struct {
	Params  Params
	TLS     *tls.Config // TLS 1.2 minimum, no client certificates, no ALPN (DEVICE-EXCHANGE 7.1)
	Limiter *limits.Limiter
	PreAuth *limits.PreAuth
	Backend Backend
	Log     *logx.Logger
	Now     func() time.Time

	// Ticket 245 Remote Administration and Web Client (244)
	AdminConfig     config.Admin
	PublicEndpoint  string
	LeafFingerprint string
	Ops             *admin.Ops
	Verifier        *limits.Verifier
	AdminPacer      *accounts.Pacer
	WebDir          string

	// WebSocketMaxMessage caps one WebSocket message (the host setting limits.webSocketMessageBytes,
	// WEB-BINDING websocket-binding "Messages"); 0 is the default, wsMaxMessage.
	WebSocketMaxMessage int
}

// Server is one deployment's listener.
type Server struct {
	cfg Config
	p   Params
	now func() time.Time

	closed    chan struct{}
	closeOnce sync.Once

	mu    sync.Mutex
	jobs  sync.WaitGroup
	ln    net.Listener
	conns map[*trackedConn]struct{}
}

// New builds a server. TLS, Limiter and Backend are required for Serve.
func New(cfg Config) *Server {
	s := &Server{
		cfg: cfg, p: cfg.Params, now: cfg.Now,
		closed: make(chan struct{}),
		conns:  map[*trackedConn]struct{}{},
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

// Params returns the numbers the server runs with.
func (s *Server) Params() Params { return s.p }

// Done is closed when the server is closing.
func (s *Server) Done() <-chan struct{} { return s.closed }

// Connections is the number of open connections (a count, never a list).
func (s *Server) Connections() int {
	s.mu.Lock()
	defer s.mu.Unlock()
	return len(s.conns)
}

// Serve accepts connections until Close. It returns nil after Close.
func (s *Server) Serve(ln net.Listener) error {
	s.mu.Lock()
	select {
	case <-s.closed:
		s.mu.Unlock()
		_ = ln.Close()
		return nil
	default:
	}
	s.ln = ln
	s.jobs.Add(1)
	s.mu.Unlock()
	go func() {
		defer s.jobs.Done()
		s.sweepLoop()
	}()
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

// Close stops accepting, closes every stream and tells the backend.
func (s *Server) Close() {
	s.closeOnce.Do(func() {
		close(s.closed)
		s.mu.Lock()
		ln := s.ln
		var conns []*trackedConn
		for c := range s.conns {
			conns = append(conns, c)
		}
		s.mu.Unlock()
		if ln != nil {
			_ = ln.Close()
		}
		if s.cfg.Backend != nil {
			s.cfg.Backend.Close()
		}
		for _, c := range conns {
			_ = c.Close()
		}
		s.jobs.Wait()
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
			s.cfg.Backend.Sweep(s.now())
		}
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
		c.s.recordAudit(logx.AuditEvent{Op: "connection.close", Remote: c.RemoteAddr().String(), Transport: "TCP", Status: logx.AuditStatusOK})
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
	select {
	case <-s.closed:
		s.mu.Unlock()
		_ = c.Close()
		return nil
	default:
	}
	s.conns[tc] = struct{}{}
	s.mu.Unlock()
	s.recordAudit(logx.AuditEvent{Op: "connection.open", Remote: c.RemoteAddr().String(), Transport: "TCP", Status: logx.AuditStatusOK})
	return tc
}

// connectionPanicked is the recover of handle: it logs the event (the client's address as everywhere,
// the panic as bounded, cleaned text - never a stack, never a payload) and closes the connection.
func (s *Server) connectionPanicked(raw *trackedConn, r any) {
	if s.cfg.Log != nil {
		s.cfg.Log.Error("connection-panic", logx.Addr(limits.Key(raw.RemoteAddr())), logx.Text("panic", fmt.Sprintf("%T: %v", r, r)))
	}
	_ = raw.Close()
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

func isASCIILetter(b byte) bool { return b >= 'A' && b <= 'Z' || b >= 'a' && b <= 'z' }

// handle is the first-byte split (DEVICE-EXCHANGE 7.1). Outside TLS: 0x16 is a TLS handshake, 0x00
// the first byte of a plain frame (a frame is at most 16 KiB before authentication, so its 4-byte
// big-endian length starts with a zero byte), anything else is closed without a word. Inside TLS
// the first byte decides again: 0x00 an envelope stream, an ASCII letter an HTTP request, anything
// else closes. Every connection holds a pre-auth slot until it authenticates (the backend has answered
// its enroll or hello), or hands itself over as an outsider connect or an HTTP listen, or ends.
func (s *Server) handle(raw *trackedConn) {
	// One bad connection must never end the process: a panic on this connection's goroutine (a frame
	// the parsers did not foresee, a backend bug) is logged and the connection closed, nothing more.
	// It does not reach goroutines a backend starts of its own (a bridge, a control stream's writer).
	defer func() {
		if r := recover(); r != nil {
			s.connectionPanicked(raw, r)
		}
	}()
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

	switch b[0] {
	case 0x00:
		s.envelopes(raw, bc, false, key, leave)
	case 0x16:
		tc := tls.Server(bc, s.cfg.TLS)
		_ = tc.SetDeadline(time.Now().Add(s.p.HandshakeTimeout))
		if err := tc.Handshake(); err != nil {
			_ = raw.Close()
			return
		}
		_ = tc.SetDeadline(time.Time{}) // the handshake deadline must not outlive the handshake
		_ = tc.SetReadDeadline(time.Now().Add(s.p.PeekTimeout))
		br2 := bufio.NewReaderSize(tc, 512)
		b2, err := br2.Peek(1)
		if err != nil {
			_ = raw.Close()
			return
		}
		inner := &bufConn{Conn: tc, r: br2}
		switch {
		case b2[0] == 0x00:
			s.envelopes(raw, inner, true, key, leave)
		case isASCIILetter(b2[0]):
			s.httpOpening(raw, inner, br2, key, leave)
		default:
			_ = raw.Close()
		}
	default:
		_ = raw.Close()
	}
}

// httpOpening serves an HTTP/1.1 request that arrived inside TLS (the relay's listen URL, 7.6). Any
// path but /v2/b/<id>/stream is 404 and any method but GET is 405; a good request is the
// backend's, which answers 404 itself for an unknown or ended broadcast.
func (s *Server) httpOpening(raw *trackedConn, conn net.Conn, br *bufio.Reader, key string, leave func()) {
	st := newStream(s, raw, conn, true, key)
	st.leave = leave
	owned := false
	defer func() {
		if !owned {
			st.Close()
		}
	}()
	_ = conn.SetReadDeadline(time.Now().Add(s.p.FirstFrameTimeout))
	req, err := wire.ReadHTTPRequest(br)
	if err != nil {
		return
	}
	_ = conn.SetReadDeadline(time.Time{})

	if req.Version != "HTTP/1.1" {
		_ = st.WriteRaw(wire.HTTPResponse(505, 0))
		return
	}

	if req.Path == "/v2/ws" {
		// The WebSocket opening judges its own method: WEB-BINDING websocket-binding "Opening" makes a
		// method other than GET a 400 (the 405 of the listen path below is the relay's, 7.6).
		owned = true
		s.handleWebSocket(st, req, br, leave)
		return
	}

	if id, ok := wire.ParseListenPath(req.Path); ok && wire.ValidID(id) {
		if req.Method != "GET" {
			_ = st.WriteRaw(wire.HTTPResponse(405, 0))
			return
		}
		leave()
		owned = true
		s.cfg.Backend.Listen(st, id)
		return
	}

	leave()

	if s.cfg.WebDir != "" && !strings.HasPrefix(req.Path, "/v2/") {
		s.serveStaticFile(st, req)
		return
	}

	_ = st.WriteRaw(wire.HTTPResponse(404, 0))
}
