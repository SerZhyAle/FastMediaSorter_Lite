package rendezvous

import (
	"bufio"
	"crypto/sha1"
	"encoding/base64"
	"encoding/binary"
	"errors"
	"fmt"
	"io"
	"net"
	"slices"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/wire"
)

const (
	wsProtocol = "fms-exchange-v2"
	wsMagic    = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"

	// wsMaxMessage is the default cap of one message in both directions. WEB-BINDING websocket-binding
	// "Messages": the server caps a message - a host setting, default 1048580 bytes (the largest relay
	// chunk of item U with its length word) - and closes 1009 above it. The setting is
	// limits.webSocketMessageBytes of internal/config; it reaches here as Config.WebSocketMaxMessage, and
	// this is the value in force when that is 0.
	wsMaxMessage = 1048580

	// wsMaxControl is RFC 6455 5.5: a control frame carries at most 125 bytes of payload.
	wsMaxControl = 125

	// The close codes the binding names (WEB-BINDING websocket-binding "Close").
	wsCloseNormal      uint16 = 1000 // a finished stream
	wsCloseProtocol    uint16 = 1002 // a protocol error
	wsCloseUnsupported uint16 = 1003 // a text message
	wsCloseTooBig      uint16 = 1009 // a message above the cap
)

// wsCloseWait is how long a server that sent Close waits for the peer's Close before it closes the
// transport (WEB-BINDING websocket-binding "Close": "waits briefly for the reply"). A variable only so
// that a test can shorten it.
var wsCloseWait = time.Second

var errWSMessageTooBig = errors.New("websocket: message exceeds the cap")

// handleWebSocket handles the HTTP upgrade to WebSocket (DEVICE-EXCHANGE 1.4, WEB-BINDING.md).
func (s *Server) handleWebSocket(st *Stream, req wire.HTTPRequest, br *bufio.Reader, leave func()) {
	upgrade := strings.ToLower(req.Headers["upgrade"])
	connHdr := strings.ToLower(req.Headers["connection"])
	proto := req.Headers["sec-websocket-protocol"]
	ver := req.Headers["sec-websocket-version"]
	key := req.Headers["sec-websocket-key"]

	// websocket-binding "Opening": a missing token, another version, or a method other than GET is 400.
	if req.Method != "GET" || upgrade != "websocket" || !strings.Contains(connHdr, "upgrade") || proto != wsProtocol || ver != "13" || key == "" {
		_ = st.WriteRaw(wire.HTTPResponse(400, 0))
		st.Close()
		return
	}

	// Origin validation (websocket-binding "Origin", normative resolution 2): the host and the effective
	// port of the Host header, or a listed origin; anything else is 403. Whether the origin is the
	// server's own decides the operator stream below: a listed origin is a remote consumer, not an operator.
	origin := req.Headers["origin"]
	host := req.Headers["host"]
	s.mu.Lock()
	adminCfg := s.cfg.AdminConfig
	s.mu.Unlock()
	relation := adminCfg.ClassifyOrigin(origin, host)
	if relation == config.OriginRejected {
		_ = st.WriteRaw(wire.HTTPResponse(403, 0))
		st.Close()
		return
	}
	operatorOrigin := relation == config.OriginAbsent || relation == config.OriginSameHost

	// Compute Sec-WebSocket-Accept
	h := sha1.New()
	h.Write([]byte(key + wsMagic))
	accept := base64.StdEncoding.EncodeToString(h.Sum(nil))

	// No Sec-WebSocket-Extensions header is ever written: nothing is negotiated, permessage-deflate
	// included (websocket-binding "Opening").
	resp := fmt.Sprintf("HTTP/1.1 101 Switching Protocols\r\n"+
		"Upgrade: websocket\r\n"+
		"Connection: Upgrade\r\n"+
		"Sec-WebSocket-Accept: %s\r\n"+
		"Sec-WebSocket-Protocol: %s\r\n\r\n", accept, wsProtocol)

	if err := st.WriteRaw([]byte(resp)); err != nil {
		st.Close()
		return
	}

	// From here the WebSocket carries the stream the TLS opening carries (websocket-binding "Streams"):
	// the first envelope fixes its kind - hello, enroll, register, an outsider connect or admin-auth -
	// and the one state machine of stream.go does the rest, with the TCP peer as the client address.
	// serveStream closes the stream unless it hands it to a backend that owns it, so nothing is closed here.
	_, wsStream := newWSStream(s, st, br, leave)
	s.serveStream(wsStream, operatorOrigin)
}

// newWSStream makes the Stream of an upgraded connection. Its envelopes are read through a
// message-aware view of the WebSocket (wsEnvelopeReader), so the envelope phase gets "one message is
// exactly one frame of 7.1"; after `connected`, `upload-accepted`, `clipboard-ready` and
// `clipboard-value` the backend reads st.Conn(), the wsConn itself, where a message is any run of
// bytes and the stream stays a byte stream.
func newWSStream(s *Server, st *Stream, br *bufio.Reader, leave func()) (*wsConn, *Stream) {
	messageCap := wsMaxMessage
	if s.cfg.WebSocketMaxMessage > 0 {
		messageCap = s.cfg.WebSocketMaxMessage
	}
	ws := newWSConn(st.conn, br, messageCap)
	ss := newStream(s, st.raw, ws, true, st.addr)
	ss.leave = leave
	ss.fr = wire.NewFrameReader(&wsEnvelopeReader{w: ws, limit: ss.limit}, wire.MaxFrame)
	return ws, ss
}

// wsCloseError is a failure of the WebSocket framing: the Close frame with its code has already been
// sent when it is returned, and the stream above it only has to end.
type wsCloseError struct {
	code uint16
	why  string
}

func (e *wsCloseError) Error() string { return fmt.Sprintf("websocket: closed %d: %s", e.code, e.why) }

// wsConn adapts a TLS net.Conn (plus any buffered bytes from bufio.Reader) into a net.Conn that
// reads and writes WebSocket binary messages (WEB-BINDING websocket-binding).
//
// Reading: readMessageLocked is the one reader. It reassembles a fragmented message (RFC 6455 5.4: an
// endpoint MUST handle fragments and control frames between them; the contract says "message" and
// never forbids fragments, so a legal fragmented message is one message, bounded by the cap), answers
// Ping, and ends on Close. Read hands the bytes of messages out as a stream (the raw phase);
// wsEnvelopeReader hands them out one message per frame (the envelope phase).
type wsConn struct {
	conn net.Conn
	br   *bufio.Reader
	max  int // the message cap

	// Write side: mu makes one frame one write.
	mu         sync.Mutex
	closeSent  atomic.Bool // the Close frame went out (first one wins); written under mu
	closeErr   error       // the error of that write
	halfClosed bool        // CloseWrite sent the empty message
	failed     atomic.Bool // the Close was a failure of the framing, not a normal end

	// Read side: rmu admits one reader. Close takes it only when nobody reads.
	rmu     sync.Mutex
	buf     []byte // the rest of the current message, raw phase
	rdErr   error  // the end of the read side, sticky (a timeout is not an end)
	msg     []byte // the data message being assembled
	msgOpen bool   // a fragmented message is in progress
	inFrame bool   // a frame header was consumed and its payload is partly read
	fOp     byte
	fFin    bool
	fMask   [4]byte
	fPos    int // position in the mask
	fRemain int // payload bytes still to read
	ctl     []byte

	peerOnce   sync.Once
	peerClosed chan struct{} // closed when the peer's Close arrived or its transport ended
	closeOnce  sync.Once
}

func newWSConn(conn net.Conn, br *bufio.Reader, maxMessage int) *wsConn {
	if br == nil {
		br = bufio.NewReader(conn)
	}
	if maxMessage <= 0 {
		maxMessage = wsMaxMessage
	}
	return &wsConn{conn: conn, br: br, max: maxMessage, peerClosed: make(chan struct{})}
}

func (w *wsConn) peerGone() { w.peerOnce.Do(func() { close(w.peerClosed) }) }

// fail sends Close with code and returns the error that ends the read side. It never waits: the
// wait for the peer's reply belongs to Close.
func (w *wsConn) fail(code uint16, why string) error {
	w.failed.Store(true)
	_ = w.writeClose(code)
	return &wsCloseError{code: code, why: why}
}

// readMessageLocked returns the next data message (rmu held). envLimit > 0 is the envelope phase: the
// largest frame body in force, so a message above envLimit+4 is refused before it is buffered. An
// empty message is the peer's half-close.
func (w *wsConn) readMessageLocked(envLimit int) ([]byte, error) {
	for {
		if !w.inFrame {
			if err := w.readHeader(envLimit); err != nil {
				return nil, w.readFailed(err)
			}
		}
		if err := w.readPayload(); err != nil {
			return nil, w.readFailed(err)
		}
		w.inFrame = false
		switch w.fOp {
		case 0x8: // Close: answer it and end
			_ = w.writeClose(wsCloseReplyCode(w.ctl))
			w.peerGone()
			w.rdErr = io.EOF
			return nil, io.EOF
		case 0x9: // Ping
			_ = w.writePong(w.ctl)
		case 0xA: // Pong
		default: // 0x0 continuation, 0x1 text (refused at the header), 0x2 binary
			w.msgOpen = !w.fFin
			if !w.fFin {
				continue
			}
			msg := w.msg
			w.msg = nil
			if w.closeSent.Load() {
				continue // after our Close nothing is delivered any more
			}
			return msg, nil
		}
	}
}

// readHeader peeks one frame header, judges it and consumes it. Nothing is consumed before the whole
// header is buffered, so a read deadline that fires inside a header loses nothing.
func (w *wsConn) readHeader(envLimit int) error {
	hdr, err := w.br.Peek(2)
	if err != nil {
		return err
	}
	b0, b1 := hdr[0], hdr[1]
	if b0&0x70 != 0 {
		return w.fail(wsCloseProtocol, "reserved bits set, no extension was negotiated")
	}
	if b1&0x80 == 0 {
		return w.fail(wsCloseProtocol, "unmasked client frame") // RFC 6455 5.1
	}
	op := b0 & 0x0f
	fin := b0&0x80 != 0
	lb := int(b1 & 0x7f)
	control := op&0x08 != 0
	switch op {
	case 0x0, 0x1, 0x2, 0x8, 0x9, 0xA:
	default:
		return w.fail(wsCloseProtocol, "reserved opcode")
	}
	if control && (!fin || lb > wsMaxControl) {
		return w.fail(wsCloseProtocol, "control frame fragmented or longer than 125 bytes") // RFC 6455 5.5
	}
	ext := 0
	switch lb {
	case 126:
		ext = 2
	case 127:
		ext = 8
	}
	hdr, err = w.br.Peek(2 + ext + 4)
	if err != nil {
		return err
	}
	// The length is judged as the unsigned number it is: a 64-bit word is never converted to a signed
	// integer, let alone used to size a buffer, before it has been compared with the cap.
	n := uint64(lb)
	switch lb {
	case 126:
		n = uint64(binary.BigEndian.Uint16(hdr[2:4]))
	case 127:
		n = binary.BigEndian.Uint64(hdr[2:10])
	}
	if lb == 126 && n < 126 || lb == 127 && n < 65536 {
		return w.fail(wsCloseProtocol, "length not in its shortest form") // RFC 6455 5.2
	}
	if !control {
		switch {
		case op == 0x0 && !w.msgOpen:
			return w.fail(wsCloseProtocol, "continuation without a message")
		case op != 0x0 && w.msgOpen:
			return w.fail(wsCloseProtocol, "a new message inside a fragmented one")
		case op == 0x1:
			return w.fail(wsCloseUnsupported, "text message") // "Binary only; a text message closes the stream with 1003"
		}
		have := uint64(len(w.msg))
		if n > uint64(w.max) || have+n > uint64(w.max) {
			return w.fail(wsCloseTooBig, "message above the cap")
		}
		if envLimit > 0 && have+n > uint64(envLimit)+4 {
			return w.fail(wsCloseProtocol, "frame above the limit in force") // item T, README 7.1
		}
	}
	copy(w.fMask[:], hdr[2+ext:2+ext+4])
	_, _ = w.br.Discard(2 + ext + 4)
	w.fOp, w.fFin, w.fPos, w.fRemain, w.inFrame = op, fin, 0, int(n), true
	if control {
		w.ctl = w.ctl[:0]
	}
	return nil
}

// readPayload reads what is left of the current frame's payload, unmasked, into the message (or the
// control buffer). It keeps its place, so it can be called again after a read deadline.
func (w *wsConn) readPayload() error {
	control := w.fOp&0x08 != 0
	for w.fRemain > 0 {
		if !control && w.closeSent.Load() { // after our Close a data payload is only skipped
			n, err := w.br.Discard(min(w.fRemain, 4096))
			w.fRemain -= n
			if err != nil {
				return err
			}
			continue
		}
		dst := &w.msg
		if control {
			dst = &w.ctl
		}
		k := min(w.fRemain, 16384)
		*dst = slices.Grow(*dst, k)
		l := len(*dst)
		n, err := w.br.Read((*dst)[l : l+k])
		if n > 0 {
			chunk := (*dst)[l : l+n]
			for i := range chunk {
				chunk[i] ^= w.fMask[(w.fPos+i)&3]
			}
			w.fPos += n
			*dst = (*dst)[:l+n]
			w.fRemain -= n
		}
		if err != nil {
			return err
		}
	}
	return nil
}

// readFailed records the end of the read side. A read deadline is not an end: the state is kept and
// the caller may read again.
func (w *wsConn) readFailed(err error) error {
	var ce *wsCloseError
	if errors.As(err, &ce) {
		w.rdErr = err
		return err
	}
	var ne net.Error
	if errors.As(err, &ne) && ne.Timeout() {
		return err
	}
	w.rdErr = err
	w.peerGone()
	return err
}

// Read is the raw phase: the bytes of the messages as one stream. The empty message is the peer's end
// of its direction (a half-close) and reads as io.EOF, for good.
func (w *wsConn) Read(p []byte) (int, error) {
	if len(p) == 0 {
		return 0, nil
	}
	w.rmu.Lock()
	defer w.rmu.Unlock()
	for {
		if len(w.buf) > 0 {
			n := copy(p, w.buf)
			w.buf = w.buf[n:]
			return n, nil
		}
		if w.rdErr != nil {
			return 0, w.rdErr
		}
		msg, err := w.readMessageLocked(0)
		if err != nil {
			return 0, err
		}
		if len(msg) == 0 {
			w.rdErr = io.EOF
			return 0, io.EOF
		}
		w.buf = msg
	}
}

// wsEnvelopeReader is the envelope phase as wire.FrameReader sees it: each message is exactly one
// frame of 7.1 (WEB-BINDING websocket-binding "Messages": "the length word and the JSON body, byte
// for byte"), and "the length word equals the message length minus 4, else 1002". The frame reader
// reads the length word and then the body through it and consumes the message to its last byte, so
// the next Read starts a message. The empty message ends the stream ("in an envelope phase it ends
// the stream"). limit is the frame limit in force (item T).
type wsEnvelopeReader struct {
	w     *wsConn
	limit func() int
	rest  []byte
}

func (r *wsEnvelopeReader) Read(p []byte) (int, error) {
	if len(p) == 0 {
		return 0, nil
	}
	if len(r.rest) == 0 {
		if err := r.next(); err != nil {
			return 0, err
		}
	}
	n := copy(p, r.rest)
	r.rest = r.rest[n:]
	return n, nil
}

func (r *wsEnvelopeReader) next() error {
	w := r.w
	w.rmu.Lock()
	defer w.rmu.Unlock()
	if w.rdErr != nil {
		return w.rdErr
	}
	lim := r.limit()
	msg, err := w.readMessageLocked(lim)
	if err != nil {
		return err
	}
	if len(msg) == 0 {
		w.rdErr = io.EOF
		return io.EOF
	}
	if len(msg) < 4 {
		w.rdErr = w.fail(wsCloseProtocol, "a message shorter than a length word")
		return w.rdErr
	}
	word := uint64(binary.BigEndian.Uint32(msg))
	switch {
	case word != uint64(len(msg)-4):
		w.rdErr = w.fail(wsCloseProtocol, "the length word is not the message length minus 4")
	case word == 0 || word > uint64(lim):
		w.rdErr = w.fail(wsCloseProtocol, "a length of zero or above the limit in force")
	default:
		// README 7.1: "a body that is not one object closes the stream" - on a WebSocket that is a
		// protocol error, 1002. The frame reader decodes the body again; the schemaVersion of the
		// superseded wire is not a framing error and is left to it, which answers it with a refusal.
		if _, err := wire.Decode(msg[4:]); err != nil && !errors.Is(err, wire.ErrVersionRefused) {
			w.rdErr = w.fail(wsCloseProtocol, "the body is not an envelope")
			return w.rdErr
		}
		r.rest = msg
		return nil
	}
	return w.rdErr
}

// wsFrameHeader is the header of an unmasked server frame.
func wsFrameHeader(op byte, n int) []byte {
	switch {
	case n < 126:
		return []byte{0x80 | op, byte(n)}
	case n <= 65535:
		h := []byte{0x80 | op, 126, 0, 0}
		binary.BigEndian.PutUint16(h[2:], uint16(n))
		return h
	default:
		h := make([]byte, 10)
		h[0], h[1] = 0x80|op, 127
		binary.BigEndian.PutUint64(h[2:], uint64(n))
		return h
	}
}

// Write sends p as one binary message. An empty p sends nothing: the empty message means the end of
// this direction and only CloseWrite says it.
func (w *wsConn) Write(p []byte) (int, error) {
	if len(p) == 0 {
		return 0, nil
	}
	if len(p) > w.max {
		return 0, errWSMessageTooBig
	}
	header := wsFrameHeader(0x2, len(p))

	w.mu.Lock()
	defer w.mu.Unlock()
	if w.closeSent.Load() {
		return 0, net.ErrClosed
	}
	if w.halfClosed {
		return 0, io.ErrClosedPipe
	}
	_ = w.conn.SetWriteDeadline(time.Now().Add(writeTimeout))
	if _, err := w.conn.Write(header); err != nil {
		_ = w.conn.SetWriteDeadline(time.Time{})
		return 0, err
	}
	n, err := w.conn.Write(p)
	_ = w.conn.SetWriteDeadline(time.Time{})
	return n, err
}

// CloseWrite is this side's half-close: the empty binary message (websocket-binding "Half-close"), the
// signal a TCP FIN gives. relay.Splice calls it when the other stream ended its direction, so the
// half-close passes through a bridge in both directions.
func (w *wsConn) CloseWrite() error {
	w.mu.Lock()
	defer w.mu.Unlock()
	if w.closeSent.Load() || w.halfClosed {
		return nil
	}
	w.halfClosed = true
	_ = w.conn.SetWriteDeadline(time.Now().Add(writeTimeout))
	_, err := w.conn.Write(wsFrameHeader(0x2, 0))
	_ = w.conn.SetWriteDeadline(time.Time{})
	return err
}

func (w *wsConn) writePong(payload []byte) error {
	w.mu.Lock()
	defer w.mu.Unlock()
	if w.closeSent.Load() {
		return nil
	}
	frame := append(wsFrameHeader(0xA, len(payload)), payload...)
	_ = w.conn.SetWriteDeadline(time.Now().Add(writeTimeout))
	_, err := w.conn.Write(frame)
	_ = w.conn.SetWriteDeadline(time.Time{})
	return err
}

// writeClose sends the Close frame once; the first code wins.
func (w *wsConn) writeClose(code uint16) error {
	w.mu.Lock()
	defer w.mu.Unlock()
	if w.closeSent.Load() {
		return w.closeErr
	}
	w.closeSent.Store(true)
	frame := []byte{0x88, 2, byte(code >> 8), byte(code)}
	_ = w.conn.SetWriteDeadline(time.Now().Add(writeTimeout))
	_, w.closeErr = w.conn.Write(frame)
	_ = w.conn.SetWriteDeadline(time.Time{})
	return w.closeErr
}

// wsCloseReplyCode is what a server answers to the peer's Close: the code it sent when that is a code
// a peer may put on the wire, 1000 when it sent none, 1002 when the payload is not a Close payload.
func wsCloseReplyCode(payload []byte) uint16 {
	switch {
	case len(payload) == 0:
		return wsCloseNormal
	case len(payload) == 1:
		return wsCloseProtocol
	}
	code := binary.BigEndian.Uint16(payload)
	if code >= 1000 && code <= 1014 && code != 1004 && code != 1005 && code != 1006 || code >= 3000 && code <= 4999 {
		return code
	}
	return wsCloseProtocol
}

// Close ends the stream: 1000 unless a failure sent its own code before, then it waits briefly for the
// peer's Close and closes the transport (websocket-binding "Close": "A server that sends Close waits
// briefly for the reply before it closes the transport"). Safe from any goroutine, more than once.
func (w *wsConn) Close() error {
	w.closeOnce.Do(func() {
		if w.writeClose(wsCloseNormal) == nil {
			w.awaitPeerClose()
		}
		_ = w.conn.Close()
	})
	return nil
}

// awaitPeerClose waits at most wsCloseWait for the peer's Close frame or the end of its transport.
// When another goroutine is reading, that reader sees the reply and says so; when nobody reads, this
// goroutine reads itself, so the wait is never a plain sleep.
func (w *wsConn) awaitPeerClose() {
	select {
	case <-w.peerClosed:
		return
	default:
	}
	deadline := time.Now().Add(wsCloseWait)
	if w.rmu.TryLock() {
		defer w.rmu.Unlock()
		_ = w.conn.SetReadDeadline(deadline)
		if w.failed.Load() {
			w.drainAfterFailure()
			return
		}
		for {
			if _, err := w.readMessageLocked(0); err != nil {
				return
			}
		}
	}
	t := time.NewTimer(time.Until(deadline))
	defer t.Stop()
	select {
	case <-w.peerClosed:
	case <-t.C:
	}
}

// drainAfterFailure is the wait of Close after a failure of the framing (rmu held, a read deadline
// set). The peer is not trusted to frame correctly, so frames are skipped leniently - any opcode,
// RSV bits or a missing mask ignored - until its Close, its end, or a length no frame may have; from
// there on the bytes are only read away (bounded). Closing with unread bytes would reset the
// connection and could take our Close frame with it.
func (w *wsConn) drainAfterFailure() {
	for {
		h, err := w.br.Peek(2)
		if err != nil {
			return
		}
		need := 2 + 4*int(h[1]>>7)
		switch h[1] & 0x7f {
		case 126:
			need += 2
		case 127:
			need += 8
		}
		if h, err = w.br.Peek(need); err != nil {
			return
		}
		op, n := h[0]&0x0f, uint64(h[1]&0x7f)
		switch n {
		case 126:
			n = uint64(binary.BigEndian.Uint16(h[2:4]))
		case 127:
			n = binary.BigEndian.Uint64(h[2:10])
		}
		if op == 0x8 {
			return // the peer's reply to our Close
		}
		if n > uint64(w.max) {
			_, _ = io.Copy(io.Discard, io.LimitReader(w.br, 256<<10))
			return
		}
		if _, err := w.br.Discard(need + int(n)); err != nil {
			return
		}
	}
}

func (w *wsConn) LocalAddr() net.Addr                { return w.conn.LocalAddr() }
func (w *wsConn) RemoteAddr() net.Addr               { return w.conn.RemoteAddr() }
func (w *wsConn) SetDeadline(t time.Time) error      { return w.conn.SetDeadline(t) }
func (w *wsConn) SetReadDeadline(t time.Time) error  { return w.conn.SetReadDeadline(t) }
func (w *wsConn) SetWriteDeadline(t time.Time) error { return w.conn.SetWriteDeadline(t) }
