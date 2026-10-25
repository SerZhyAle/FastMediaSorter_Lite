package rendezvous

import (
	"errors"
	"net"
	"sync"
	"time"

	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/wire"
)

// This file is the stream state machine of DEVICE-EXCHANGE 7.2 and ticket 161 rule 5:
//
//	opening        enroll, hello, connect(shareId | broadcastId) are valid
//	authenticated  after enrolled / welcome: the first envelope fixes the role -
//	               attach, connect, upload, clipboard-put or clipboard-get make a DATA stream,
//	               anything else the CONTROL stream
//	control        the device's messages and the server's pushes, envelopes only, for its whole life
//	data           no envelope after the first one; after connected / upload-accepted /
//	               clipboard-ready / clipboard-value the bytes are not envelopes
//
// An envelope invalid in the current state closes the stream; an unknown type or member is ignored.

// QueueLen is the bounded outgoing queue of a control stream (ticket 163 rule 6): a subscriber that
// does not drain it is closed, never buffered without bound.
const QueueLen = 256

// maxUnknown bounds how many unknown-type frames in a row a stream may send before it is closed.
const maxUnknown = 64

// Errors of the control stream's writer.
var (
	ErrClosed    = errors.New("rendezvous: stream closed")
	ErrQueueFull = errors.New("rendezvous: outgoing queue full")
	errUnknown   = errors.New("rendezvous: too many unknown envelopes in a row")
)

// Session is what a successful enroll or hello makes of a stream.
type Session struct {
	Account   string        // the login the stream belongs to
	DeviceID  string        // the authenticated device
	Keepalive time.Duration // the accepted interval, echoed in welcome
	// DeviceName, Product and ProductVersion are what the server learned at authentication (spec
	// 247): untrusted display text for the [AUDIT] log, never a proof of anything.
	DeviceName     string
	Product        string
	ProductVersion string
	// Private is the backend's own data for the session.
	Private any
}

// Result is a Backend's answer to an enroll or a hello.
type Result struct {
	Session *Session
	Reply   wire.Envelope // the `enrolled` or `welcome` written on success
	Refuse  wire.Reason   // a refusal to answer before closing
	Silent  bool          // close with no answer at all
}

// Backend is what the state machine hands a stream to once it knows what the stream is. The
// engine of engine.go is the real one; a test of this layer supplies a fake.
type Backend interface {
	// Enroll, Register and Hello authenticate. They are called on the stream's own goroutine.
	Enroll(st *Stream, e *wire.Enroll) Result
	Register(st *Stream, e *wire.Register) Result
	Hello(st *Stream, e *wire.Hello) Result
	// Control serves a device's control stream until it ends; first is its first envelope.
	Control(cs *ControlStream, first wire.Envelope)
	// Attach, Connect, Upload, ClipboardPut and ClipboardGet serve a data stream. sess is nil for
	// an outsider: a stream that never said hello, which may only connect by shareId or
	// broadcastId. THEY OWN THE STREAM: the state machine does not close it when they return,
	// because a bridge may outlive the call (an attach hands its stream to the consumer waiting
	// for it); the backend closes it.
	Attach(st *Stream, sess *Session, e *wire.Attach)
	Connect(st *Stream, sess *Session, e *wire.Connect)
	Upload(st *Stream, sess *Session, e *wire.Upload)
	// ClipboardPut and ClipboardGet are the clipboard's data streams (22 items BW and BY, ticket
	// 229); they own the stream like the three above.
	ClipboardPut(st *Stream, sess *Session, e *wire.ClipboardPut)
	ClipboardGet(st *Stream, sess *Session, e *wire.ClipboardGet)
	// Listen serves GET /v2/b/<broadcastId>/stream inside TLS and owns the stream like the three above.
	Listen(st *Stream, broadcastID string)
	// Sweep runs every SweepInterval with the server's clock: keepalive windows, tombstones, codes.
	Sweep(now time.Time)
	// Close runs once, when the server closes.
	Close()
}

// Stream is one accepted stream, from its first byte to its hand-over or its end. It is used from
// the goroutine that serves it; only Close may be called from another.
type Stream struct {
	srv    *Server
	raw    *trackedConn
	conn   net.Conn // TLS or plain, with the bytes the first-byte peek buffered
	secure bool
	addr   string // limits.Key of the client
	t0     time.Time
	leave  func()
	authed bool // welcome or enrolled was sent: the frame limit is MaxFrameAuth both ways

	// authorization is the Authorization header of the HTTP request that opened the stream, "" when there was
	// none or the stream is not HTTP. It is a listener's Basic credential (item BF): read by Authorization,
	// never logged, never kept past the request.
	authorization string

	fr *wire.FrameReader
	fw *wire.FrameWriter

	closeOnce sync.Once
	mu        sync.Mutex
	onClose   []func()
	closed    bool
}

func newStream(s *Server, raw *trackedConn, conn net.Conn, secure bool, addr string) *Stream {
	return &Stream{
		srv: s, raw: raw, conn: conn, secure: secure, addr: addr, t0: time.Now(),
		fr: wire.NewFrameReader(conn, wire.MaxFrame), fw: wire.NewFrameWriter(conn, wire.MaxFrame),
		leave: func() {},
	}
}

// Server is the listener that accepted the stream.
func (st *Stream) Server() *Server { return st.srv }

// Secure reports whether the stream is inside TLS.
func (st *Stream) Secure() bool { return st.secure }

// Addr is the limiter key of the client address (an IP, an IPv6 /64).
func (st *Stream) Addr() string { return st.addr }

// Transport reports the actual opening, never a client-supplied header.
func (st *Stream) Transport() string {
	if _, ok := st.conn.(*wsConn); ok {
		return "WebSocket"
	}
	return "TLS-stream"
}

// Authorization is the Authorization header of the HTTP request that opened the stream (the relay's listen
// URL, 7.6), or "". It carries a secret: a caller never logs it, copies it out or keeps it.
func (st *Stream) Authorization() string { return st.authorization }

// T0 is when the first envelope of the stream arrived: the zero of the uniform delay.
func (st *Stream) T0() time.Time { return st.t0 }

// Conn is the stream for the bridge. After `connected` or `upload-accepted` the frame reader has
// consumed exactly one frame and nothing more, so reading from Conn continues with the raw bytes.
func (st *Stream) Conn() net.Conn { return st.conn }

// Send writes one envelope synchronously. Use it only before the stream becomes a control stream.
func (st *Stream) Send(e wire.Envelope) error {
	frame, err := wire.EncodeFrame(e, st.limit())
	if err != nil {
		return err
	}
	return st.WriteRaw(frame)
}

// WriteRaw writes bytes under the write deadline.
func (st *Stream) WriteRaw(b []byte) error {
	_ = st.conn.SetWriteDeadline(time.Now().Add(writeTimeout))
	_, err := st.conn.Write(b)
	_ = st.conn.SetWriteDeadline(time.Time{})
	return err
}

func (st *Stream) limit() int {
	if st.authed {
		return wire.MaxFrameAuth
	}
	return wire.MaxFrame
}

// Refuse answers with a refusal and closes the stream.
func (st *Stream) Refuse(r wire.Reason) {
	_ = st.Send(wire.Refuse(r))
	st.Close()
}

// SetReadDeadline bounds the next reads; the zero time clears it.
func (st *Stream) SetReadDeadline(t time.Time) { _ = st.conn.SetReadDeadline(t) }

// Recv reads the next KNOWN envelope: an unknown type is ignored, up to a bound.
func (st *Stream) Recv() (wire.Envelope, error) {
	for i := 0; i < maxUnknown; i++ {
		e, err := st.fr.Read()
		if err != nil {
			return nil, err
		}
		if wire.Known(e) {
			return e, nil
		}
	}
	return nil, errUnknown
}

// authenticated raises the frame limit in both directions (DEVICE-EXCHANGE 15 item T): after
// `welcome` or `enrolled`, on this stream, a frame may carry MaxFrameAuth bytes.
func (st *Stream) markAuthenticated() {
	st.authed = true
	st.fr.SetLimit(wire.MaxFrameAuth)
	st.fw.SetLimit(wire.MaxFrameAuth)
}

// Close closes the stream and its connection. Safe to call from any goroutine, more than once.
func (st *Stream) Close() {
	st.closeOnce.Do(func() {
		_ = st.conn.Close()
		_ = st.raw.Close()
		st.leave()
		st.mu.Lock()
		st.closed = true
		fns := st.onClose
		st.onClose = nil
		st.mu.Unlock()
		for _, f := range fns {
			f()
		}
	})
}

// OnClose runs f once the stream is closed (at once, if it already is). The backend uses it to forget
// a stream it was tracking.
func (st *Stream) OnClose(f func()) {
	st.mu.Lock()
	if st.closed {
		st.mu.Unlock()
		f()
		return
	}
	st.onClose = append(st.onClose, f)
	st.mu.Unlock()
}

// --- the state machine ---

// envelopes serves a connection that opened with an envelope: the first frame, the authentication,
// the role of the stream. raw is the tracked TCP connection, conn the stream above TLS (or the
// plain buffered one).
func (s *Server) envelopes(raw *trackedConn, conn net.Conn, secure bool, key string, leave func()) {
	st := newStream(s, raw, conn, secure, key)
	st.leave = leave
	s.serveStream(st, true) // the TLS opening has no Origin: it is a native client's, an operator's if it says admin-auth
}

// serveStream is the state machine of one stream, whatever carries it: the TLS opening above, or the
// WebSocket one (ws.go), whose Stream reads one message per frame and writes one message per frame and
// is otherwise the same (WEB-BINDING websocket-binding "Streams": "each is its own TLS stream today").
// operatorOrigin says an administration stream is allowed here (no Origin, or the server's own); only the
// WebSocket opening can say false.
func (s *Server) serveStream(st *Stream, operatorOrigin bool) {
	conn, secure, leave := st.conn, st.secure, st.leave
	// A data stream is handed to the backend, which then owns it and closes it when its bridge ends;
	// every other way out of this function closes the stream here.
	owned := false
	defer func() {
		if !owned {
			st.Close()
		}
	}()

	_ = conn.SetReadDeadline(time.Now().Add(s.p.FirstFrameTimeout))
	first, err := st.Recv()
	if err != nil {
		st.refuseVersion(err)
		return
	}
	st.t0 = time.Now()
	_ = conn.SetReadDeadline(time.Time{})

	// The pre-auth slot (161 rule 3) is held until the stream AUTHENTICATES - that is, until the
	// backend has answered its enroll or hello - not merely until its first envelope has arrived: a
	// stream that sent a frame and then stalls (or is slow to be judged) is still unauthenticated.
	// An outsider connect never authenticates, so it gives the slot back as it is handed over;
	// every refusal path closes the stream, which gives it back too.
	var res Result
	switch e := first.(type) {
	case *wire.AdminAuth:
		if !secure {
			st.Refuse(wire.ReasonTLSRequired)
			return
		}
		owned = true
		s.serveAdminStream(st, e, leave, operatorOrigin)
		return
	case *wire.Enroll:
		if !secure {
			st.Refuse(wire.ReasonTLSRequired)
			return
		}
		res = s.cfg.Backend.Enroll(st, e)
	case *wire.Register:
		if !secure {
			st.Refuse(wire.ReasonTLSRequired)
			return
		}
		res = s.cfg.Backend.Register(st, e)
	case *wire.Hello:
		if !secure {
			st.Refuse(wire.ReasonTLSRequired)
			return
		}
		res = s.cfg.Backend.Hello(st, e)
	case *wire.Connect:
		if !secure { // 0.17 item V: every outsider connect is TLS only; no lookup is made on a plain stream
			st.Refuse(wire.ReasonTLSRequired)
			return
		}
		if e.ResourceID != "" { // a resourceId is never a capability: it needs a hello
			return
		}
		leave() // an outsider's stream waits for its producer for up to the pending window: not a pre-auth wait
		owned = true
		s.cfg.Backend.Connect(st, nil, e)
		return
	default:
		return // known, but invalid in the opening state
	}

	if res.Session != nil && res.Reply != nil && res.Refuse == "" && !res.Silent {
		leave() // authenticated: from here the stream counts against its device, not the pre-auth pool
	}
	switch {
	case res.Silent:
		return
	case res.Refuse != "":
		st.Refuse(res.Refuse)
		return
	case res.Session == nil || res.Reply == nil:
		return
	}
	if err := st.Send(res.Reply); err != nil {
		return
	}
	st.markAuthenticated()

	// authenticated: the first envelope fixes what the stream is
	_ = conn.SetReadDeadline(time.Now().Add(s.p.FirstFrameTimeout))
	next, err := st.Recv()
	if err != nil {
		return
	}
	st.t0 = time.Now()
	_ = conn.SetReadDeadline(time.Time{})
	switch e := next.(type) {
	case *wire.Attach:
		owned = true
		s.cfg.Backend.Attach(st, res.Session, e)
	case *wire.Connect:
		owned = true
		s.cfg.Backend.Connect(st, res.Session, e)
	case *wire.Upload:
		owned = true
		s.cfg.Backend.Upload(st, res.Session, e)
	case *wire.ClipboardPut:
		owned = true
		s.cfg.Backend.ClipboardPut(st, res.Session, e)
	case *wire.ClipboardGet:
		owned = true
		s.cfg.Backend.ClipboardGet(st, res.Session, e)
	default:
		cs := newControl(st, res.Session)
		defer cs.Close()
		s.cfg.Backend.Control(cs, next)
	}
}

// refuseVersion answers the legacy schemaVersion with the refusal of 7.1 and closes; any other
// failure of the first frame closes without an answer.
func (st *Stream) refuseVersion(err error) {
	if errors.Is(err, wire.ErrVersionRefused) {
		st.srv.cfg.Log.Info("version-refused", logx.Addr(st.addr))
		st.Refuse(wire.ReasonVersionUnsupported)
	}
}

// --- the control stream ---

// ControlStream is the one stream of a device that carries envelopes for its whole life. Writes go
// through a bounded queue and one writer goroutine, so the directory pushing a `changed` to many
// subscribers never waits on a socket; reads belong to the backend's loop (Next).
type ControlStream struct {
	st   *Stream
	sess *Session
	out  chan []byte
	done chan struct{}
	once sync.Once
	// wmu makes one frame one write: the writer goroutine and SendSync never interleave.
	wmu sync.Mutex
}

func newControl(st *Stream, sess *Session) *ControlStream {
	cs := &ControlStream{st: st, sess: sess, out: make(chan []byte, QueueLen), done: make(chan struct{})}
	go cs.writer()
	return cs
}

func (cs *ControlStream) writer() {
	for {
		select {
		case <-cs.done:
			return
		case frame := <-cs.out:
			cs.wmu.Lock()
			err := cs.st.WriteRaw(frame)
			cs.wmu.Unlock()
			if err != nil {
				cs.Close()
				return
			}
		}
	}
}

// Session is the authenticated device.
func (cs *ControlStream) Session() *Session { return cs.sess }

// Addr is the client's limiter key.
func (cs *ControlStream) Addr() string { return cs.st.addr }

// Done is closed when the stream ends.
func (cs *ControlStream) Done() <-chan struct{} { return cs.done }

// Next reads the next known envelope. It has no deadline of its own: the backend's keepalive window
// ends a silent stream by closing it.
func (cs *ControlStream) Next() (wire.Envelope, error) { return cs.st.Recv() }

// Send encodes and queues one envelope. A stream that cannot take it (closed, or its queue full) is
// closed and the error says why.
func (cs *ControlStream) Send(e wire.Envelope) error {
	frame, err := wire.EncodeFrame(e, wire.MaxFrameAuth)
	if err != nil {
		return err
	}
	return cs.SendFrame(frame)
}

// SendSync writes one envelope to the connection before it returns, for the answer after which the
// caller ends the stream (`account-deleted`): a queued frame could be lost to the close that follows.
// It waits at most the write deadline of WriteRaw.
func (cs *ControlStream) SendSync(e wire.Envelope) error {
	frame, err := wire.EncodeFrame(e, wire.MaxFrameAuth)
	if err != nil {
		return err
	}
	cs.wmu.Lock()
	defer cs.wmu.Unlock()
	select {
	case <-cs.done:
		return ErrClosed
	default:
	}
	return cs.st.WriteRaw(frame)
}

// SendFrame queues a frame that was already encoded: a fan-out encodes once and queues many times.
// It never blocks: a full queue closes the stream (the client relists when it reconnects).
func (cs *ControlStream) SendFrame(frame []byte) error {
	select {
	case <-cs.done:
		return ErrClosed
	default:
	}
	select {
	case cs.out <- frame:
		return nil
	default:
		cs.abort()
		return ErrQueueFull
	}
}

// abort ends the stream without waiting for anything: the raw connection is closed at once (a TLS
// close would try to write an alert to a peer that is not reading, which is exactly why this stream is
// being ended), and the rest of the cleanup follows on its own goroutine. It is what SendFrame uses,
// because the directory calls SendFrame under its lock.
func (cs *ControlStream) abort() {
	cs.once.Do(func() {
		close(cs.done)
		_ = cs.st.raw.Close()
		go cs.st.Close()
	})
}

// Close ends the stream without a message. Safe from any goroutine, more than once.
func (cs *ControlStream) Close() {
	cs.once.Do(func() {
		close(cs.done)
		cs.st.Close()
	})
}

// Abort ends the stream without waiting for anything: the raw connection is closed at once and the
// rest of the cleanup follows on its own goroutine. A TLS close would first try to write an alert to
// a peer that may not be reading, so anything that ends someone else's stream (a revoke, a rotated
// token) uses Abort and never Close.
func (st *Stream) Abort() {
	_ = st.raw.Close()
	go st.Close()
}
