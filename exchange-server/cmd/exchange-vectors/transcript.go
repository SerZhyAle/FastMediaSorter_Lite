package main

import (
	"bytes"
	"encoding/binary"
	"encoding/json"
	"fmt"
	"io"
	"net"
	"strings"
	"sync"
	"time"

	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// A transcript is what two programs said to each other on one scripted scenario: every frame, in the
// order the script saw it, with the JSON body AND the exact bytes (the 4-byte length and the body).
// It is recorded by a tap under the frame codec - on the stream's plain text, above TLS - so what is
// written here is what crossed the wire, not what a codec says it would have written.

// transcript is one scenario.
type transcript struct {
	Name        string       `json:"name"`
	Description string       `json:"description"`
	Streams     []streamInfo `json:"streams"`
	Steps       []step       `json:"steps"`

	mu sync.Mutex
}

type streamInfo struct {
	ID        string `json:"id"`
	Transport string `json:"transport"` // "tls" or "plain"
	Purpose   string `json:"purpose"`
}

// step is one frame, one chunk or one HTTP exchange.
type step struct {
	Stream    string     `json:"stream"`
	Direction string     `json:"direction"` // "client->server" or "server->client"
	JSON      string     `json:"json,omitempty"`
	Hex       string     `json:"hex,omitempty"`
	Note      string     `json:"note,omitempty"`
	Closed    bool       `json:"closed,omitempty"` // the server closed the stream here, with nothing more to say
	Chunk     *chunkStep `json:"chunk,omitempty"`
	HTTP      *httpStep  `json:"http,omitempty"`
}

// chunkStep is one chunk of an upload (the bytes after `upload-accepted`; never envelopes).
type chunkStep struct {
	Flags      int    `json:"flags"`
	PayloadHex string `json:"payloadHex"`
	Hex        string `json:"hex"`
}

// httpStep is one HTTP exchange inside TLS on the same port (the relay's listen URL).
type httpStep struct {
	Request      string   `json:"request"`
	Status       int      `json:"status"`
	ResponseHead string   `json:"responseHead"`
	BodyPayloads []string `json:"bodyPayloadsHex,omitempty"`
	ResponseHex  string   `json:"responseHex"`
}

const (
	c2s = "client->server"
	s2c = "server->client"
)

func (t *transcript) add(s step) {
	t.mu.Lock()
	t.Steps = append(t.Steps, s)
	t.mu.Unlock()
}

func (t *transcript) addFrame(stream, dir string, frame []byte) {
	t.add(step{Stream: stream, Direction: dir, JSON: string(frame[4:]), Hex: hexOf(frame)})
}

// note attaches a sentence to the most recent step.
func (t *transcript) note(s string) {
	t.mu.Lock()
	if n := len(t.Steps); n > 0 {
		t.Steps[n-1].Note = s
	}
	t.mu.Unlock()
}

// frameAssembler cuts a byte stream into frames: a 4-byte big-endian length, then that many bytes.
type frameAssembler struct{ buf []byte }

func (a *frameAssembler) feed(p []byte, emit func(frame []byte)) {
	a.buf = append(a.buf, p...)
	for len(a.buf) >= 4 {
		n := int(binary.BigEndian.Uint32(a.buf))
		if n == 0 || n > wire.MaxFrameAuth {
			a.buf = nil // not a frame stream any more
			return
		}
		if len(a.buf) < 4+n {
			return
		}
		emit(append([]byte(nil), a.buf[:4+n]...))
		a.buf = a.buf[4+n:]
	}
}

// tapConn records the frames that cross a stream, until recording is switched off (after `connected`
// or `upload-accepted` the bytes are not envelopes).
type tapConn struct {
	net.Conn
	tr     *transcript
	stream string

	mu      sync.Mutex
	parsing bool
	in, out frameAssembler
}

func (t *tapConn) Read(p []byte) (int, error) {
	n, err := t.Conn.Read(p)
	if n > 0 {
		t.mu.Lock()
		if t.parsing {
			t.in.feed(p[:n], func(f []byte) { t.tr.addFrame(t.stream, s2c, f) })
		}
		t.mu.Unlock()
	}
	return n, err
}

func (t *tapConn) Write(p []byte) (int, error) {
	n, err := t.Conn.Write(p)
	if n > 0 {
		t.mu.Lock()
		if t.parsing {
			t.out.feed(p[:n], func(f []byte) { t.tr.addFrame(t.stream, c2s, f) })
		}
		t.mu.Unlock()
	}
	return n, err
}

func (t *tapConn) stop() {
	t.mu.Lock()
	t.parsing = false
	t.mu.Unlock()
}

func (t *tapConn) start() {
	t.mu.Lock()
	t.parsing = true
	t.in.buf, t.out.buf = nil, nil // bytes the record missed are not frames
	t.mu.Unlock()
}

// CloseWrite forwards the write-side close of the underlying connection (a clipboard sender says
// "that was all of it" this way); net.Conn does not carry it, so sess.halfClose would not find it
// through the embedding alone.
func (t *tapConn) CloseWrite() error {
	if cw, ok := t.Conn.(interface{ CloseWrite() error }); ok {
		return cw.CloseWrite()
	}
	return nil
}

// sess is one scripted stream.
type sess struct {
	r   *rig
	tr  *transcript
	id  string
	c   *testkit.Conn
	tap *tapConn
}

func (r *rig) newTranscript(name, description string) *transcript {
	return &transcript{Name: name, Description: description}
}

// open dials the server and starts recording. transport is "tls" (pinned to the server's identity) or "plain".
func (r *rig) open(tr *transcript, id, transport, purpose string) *sess {
	var raw net.Conn
	switch transport {
	case "tls":
		c, err := testkit.DialTLS(r.env.Addr, r.env.Fingerprint)
		if err != nil {
			failf("dial tls: %v", err)
		}
		raw = c.Conn
	case "plain":
		c, err := testkit.DialPlain(r.env.Addr)
		if err != nil {
			failf("dial plain: %v", err)
		}
		raw = c.Conn
	default:
		failf("transport %q", transport)
	}
	tap := &tapConn{Conn: raw, tr: tr, stream: id, parsing: true}
	tr.mu.Lock()
	tr.Streams = append(tr.Streams, streamInfo{ID: id, Transport: transport, Purpose: purpose})
	tr.mu.Unlock()
	s := &sess{r: r, tr: tr, id: id, c: testkit.NewConn(tap), tap: tap}
	r.mu.Lock()
	r.sessions = append(r.sessions, s)
	r.mu.Unlock()
	return s
}

// auth raises the frame limit both ways, as the server did when it sent `welcome` or `enrolled`.
func (s *sess) auth() { s.c.Authenticated() }

func (s *sess) send(e wire.Envelope) {
	if err := s.c.Send(e); err != nil {
		failf("%s: send %s: %v", s.id, e.TypeName(), err)
	}
}

// sendFrame writes a frame built by hand (the codec would not build it).
func (s *sess) sendFrame(frame []byte) {
	if err := s.c.SendRaw(frame); err != nil {
		failf("%s: send: %v", s.id, err)
	}
}

func (s *sess) recv() wire.Envelope {
	e, err := s.c.Recv(5 * time.Second)
	if err != nil {
		failf("%s: receive: %v", s.id, err)
	}
	return e
}

// expect receives the next envelope and requires its type.
func (s *sess) expect(typ string) wire.Envelope {
	e := s.recv()
	if e.TypeName() != typ {
		if f, ok := e.(*wire.Refused); ok {
			failf("%s: expected %s, the server refused: %s", s.id, typ, f.Reason)
		}
		failf("%s: expected %s, the server sent %s", s.id, typ, e.TypeName())
	}
	return e
}

// expectRefused receives a refusal with the reason.
func (s *sess) expectRefused(reason wire.Reason) {
	e := s.expect(wire.TypeRefused)
	if got := e.(*wire.Refused).Reason; got != string(reason) {
		failf("%s: expected refused %s, got %s", s.id, reason, got)
	}
}

// expectClosed requires the server to close the stream with nothing more to say.
func (s *sess) expectClosed() {
	_ = s.c.SetReadDeadline(time.Now().Add(5 * time.Second))
	if _, err := s.c.Read(make([]byte, 1)); err == nil {
		failf("%s: the stream should have been closed", s.id)
	} else if ne, ok := err.(net.Error); ok && ne.Timeout() {
		failf("%s: the stream was not closed", s.id)
	}
	s.tr.add(step{Stream: s.id, Direction: s2c, Closed: true})
}

// stopRecording ends the frame record: what follows on this stream is not envelopes.
func (s *sess) stopRecording() { s.tap.stop() }

// startRecording resumes the frame record (a clipboard stream is frames again once the raw byte
// run has crossed: the `clipboard-stored` answer is recorded like any frame).
func (s *sess) startRecording() { s.tap.start() }

func (s *sess) close() { _ = s.c.Close() }

// sendRaw writes bytes as they are and records them as one step: after `clipboard-ready` a
// clipboard value's bytes are raw - no frames, no chunk headers (22 item BW).
func (s *sess) sendRaw(note string, b []byte) {
	s.tr.add(step{Stream: s.id, Direction: c2s, Note: note, Hex: hexOf(b)})
	if err := s.c.SendRaw(b); err != nil {
		failf("%s: send raw: %v", s.id, err)
	}
}

// readRaw reads exactly n bytes the server wrote as they are and records them (the raw byte run of
// a `clipboard-value`).
func (s *sess) readRaw(note string, n int) []byte {
	_ = s.c.SetReadDeadline(time.Now().Add(5 * time.Second))
	b := make([]byte, n)
	if _, err := io.ReadFull(s.c.Conn, b); err != nil {
		failf("%s: read raw: %v", s.id, err)
	}
	_ = s.c.SetReadDeadline(time.Time{})
	s.tr.add(step{Stream: s.id, Direction: s2c, Note: note, Hex: hexOf(b)})
	return b
}

// halfClose closes the write side: the way a sender says "that was all of it" after a clipboard
// value's raw bytes. The server sees EOF after what it has.
func (s *sess) halfClose() {
	if cw, ok := s.c.Conn.(interface{ CloseWrite() error }); ok {
		if err := cw.CloseWrite(); err != nil {
			failf("%s: close write: %v", s.id, err)
		}
	}
}

// --- common scripted steps ---

// enroll opens a TLS stream and enrolls with the account's password; the stream is returned
// authenticated, its role not yet fixed. It returns the stream, the device id and the token.
func (r *rig) enroll(tr *transcript, id, login, name, product string, roles []string, receiver string) (*sess, string, string) {
	s := r.open(tr, id, "tls", "enroll with the account's password")
	devID := r.newID()
	e := &wire.Enroll{Login: login, Password: testkit.TestPassword, DeviceID: devID, DeviceName: name, Product: product,
		ProductVersion: "1.0.0", Platform: "vector", Roles: roles, KeepaliveSeconds: 30}
	if receiver != "" {
		e.Receiver = json.RawMessage(receiver)
	}
	s.send(e)
	en := s.expect(wire.TypeEnrolled).(*wire.Enrolled)
	s.auth()
	return s, devID, en.DeviceToken
}

// hello opens a TLS stream and says hello; the stream is returned authenticated.
func (r *rig) hello(tr *transcript, id, purpose, devID, token string) *sess {
	s := r.open(tr, id, "tls", purpose)
	s.send(&wire.Hello{DeviceID: devID, DeviceToken: token, ProductVersion: "1.0.0", KeepaliveSeconds: 30})
	s.expect(wire.TypeWelcome)
	s.auth()
	return s
}

// online waits until the server has the device's control stream.
func (r *rig) online(account, devID string) {
	r.until("the device to be online", func() bool { return r.env.Dir.DeviceOnline(account, devID) })
}

func (r *rig) offline(account, devID string) {
	r.until("the device to be offline", func() bool { return !r.env.Dir.DeviceOnline(account, devID) })
}

// rawHTTP sends a request inside TLS and reads the whole response (the server closes after it, or
// the body ends), recording it as one step.
func (r *rig) rawHTTP(tr *transcript, id, purpose, request string) *httpStep {
	c, err := testkit.DialTLS(r.env.Addr, r.env.Fingerprint)
	if err != nil {
		failf("dial tls: %v", err)
	}
	tr.mu.Lock()
	tr.Streams = append(tr.Streams, streamInfo{ID: id, Transport: "tls", Purpose: purpose})
	tr.mu.Unlock()
	defer c.Close()
	if _, err := io.WriteString(c, request); err != nil {
		failf("%s: %v", id, err)
	}
	var raw bytes.Buffer
	_ = c.SetReadDeadline(time.Now().Add(5 * time.Second))
	if _, err := io.Copy(&raw, c.Conn); err != nil { // the server closes after the response, or the body ends
		failf("%s: reading the response: %v", id, err)
	}
	hs := parseHTTPResponse(request, raw.Bytes())
	tr.add(step{Stream: id, Direction: c2s + " / " + s2c, HTTP: hs})
	return hs
}

// parseHTTPResponse splits a raw response into its head, its status and the payloads of its chunked body.
func parseHTTPResponse(request string, raw []byte) *httpStep {
	hs := &httpStep{Request: request, ResponseHex: hexOf(raw)}
	end := bytes.Index(raw, []byte("\r\n\r\n"))
	if end < 0 {
		failf("a response with no head: %q", raw)
	}
	hs.ResponseHead = string(raw[:end+4])
	fmt.Sscanf(hs.ResponseHead, "HTTP/1.1 %d", &hs.Status)
	if hs.Status == 200 && strings.Contains(hs.ResponseHead, "Transfer-Encoding: chunked") {
		body := raw[end+4:]
		for len(body) > 0 {
			nl := bytes.Index(body, []byte("\r\n"))
			if nl < 0 {
				break
			}
			var n int
			fmt.Sscanf(string(body[:nl]), "%x", &n)
			if n == 0 {
				break
			}
			body = body[nl+2:]
			if len(body) < n+2 {
				break
			}
			hs.BodyPayloads = append(hs.BodyPayloads, hexOf(body[:n]))
			body = body[n+2:]
		}
	}
	return hs
}
