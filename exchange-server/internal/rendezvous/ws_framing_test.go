package rendezvous_test

import (
	"bufio"
	"bytes"
	"crypto/tls"
	"encoding/binary"
	"errors"
	"io"
	"net"
	"net/http"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/wire"
)

// These tests hold the framing layer of the WebSocket binding (WEB-BINDING websocket-binding:
// Opening, Messages, Half-close, Close) from the client's side of a real TLS connection, with a raw
// masked-frame client so that every violation can be sent byte for byte. Each rule has a case that
// refuses the bad input with the close code the contract names, next to the case that accepts the
// good one.

var wsTestMask = [4]byte{0x37, 0xfa, 0x21, 0x3d}

// wsMasked is a client frame: b0 carries FIN, RSV and the opcode, the payload is masked.
func wsMasked(b0 byte, payload []byte) []byte {
	return wsMaskedWithLength(b0, wsLengthField(len(payload)), payload)
}

// wsLengthField is the shortest length encoding, with the mask bit set.
func wsLengthField(n int) []byte {
	switch {
	case n < 126:
		return []byte{0x80 | byte(n)}
	case n <= 65535:
		return []byte{0x80 | 126, byte(n >> 8), byte(n)}
	default:
		f := []byte{0x80 | 127, 0, 0, 0, 0, 0, 0, 0, 0}
		binary.BigEndian.PutUint64(f[1:], uint64(n))
		return f
	}
}

// wsMaskedWithLength lets a test put any length field on the wire (field[0] carries the mask bit).
func wsMaskedWithLength(b0 byte, field, payload []byte) []byte {
	out := append([]byte{b0}, field...)
	out = append(out, wsTestMask[:]...)
	for i, b := range payload {
		out = append(out, b^wsTestMask[i%4])
	}
	return out
}

// wsUnmasked is a client frame without the mask, which RFC 6455 5.1 forbids.
func wsUnmasked(b0 byte, payload []byte) []byte {
	field := wsLengthField(len(payload))
	field[0] &^= 0x80
	return append(append([]byte{b0}, field...), payload...)
}

func wsClosePayload(code uint16) []byte {
	return []byte{byte(code >> 8), byte(code)}
}

type wsClient struct {
	t    *testing.T
	conn *tls.Conn
	br   *bufio.Reader
}

// wsDial opens /v2/ws on the server of env and requires the 101.
func wsDial(t *testing.T, addr string) *wsClient {
	t.Helper()
	conn, err := tls.Dial("tcp", addr, &tls.Config{InsecureSkipVerify: true})
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = conn.Close() })
	req := "GET /v2/ws HTTP/1.1\r\nHost: " + addr + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
		"Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n" +
		"Sec-WebSocket-Protocol: fms-exchange-v2\r\nOrigin: https://" + addr + "\r\n\r\n"
	if _, err := conn.Write([]byte(req)); err != nil {
		t.Fatal(err)
	}
	br := bufio.NewReader(conn)
	resp, err := http.ReadResponse(br, nil)
	if err != nil {
		t.Fatal(err)
	}
	if resp.StatusCode != 101 {
		t.Fatalf("the opening answered %d, want 101", resp.StatusCode)
	}
	return &wsClient{t: t, conn: conn, br: br}
}

// write sends bytes and says whether the server took them (a server that already closed may refuse).
func (c *wsClient) write(b ...[]byte) bool {
	c.t.Helper()
	_ = c.conn.SetWriteDeadline(time.Now().Add(3 * time.Second))
	_, err := c.conn.Write(bytes.Join(b, nil))
	return err == nil
}

// frame reads one server frame (the server never masks).
func (c *wsClient) frame(within time.Duration) (op byte, payload []byte, err error) {
	_ = c.conn.SetReadDeadline(time.Now().Add(within))
	h := make([]byte, 2)
	if _, err = io.ReadFull(c.br, h); err != nil {
		return 0, nil, err
	}
	n := uint64(h[1] & 0x7f)
	switch n {
	case 126:
		var l uint16
		if err = binary.Read(c.br, binary.BigEndian, &l); err != nil {
			return 0, nil, err
		}
		n = uint64(l)
	case 127:
		if err = binary.Read(c.br, binary.BigEndian, &n); err != nil {
			return 0, nil, err
		}
	}
	payload = make([]byte, n)
	if _, err = io.ReadFull(c.br, payload); err != nil {
		return 0, nil, err
	}
	return h[0] & 0x0f, payload, nil
}

// isClosed is true for the end of the connection, false for a read deadline.
func isClosed(err error) bool {
	var ne net.Error
	return err != nil && !(errors.As(err, &ne) && ne.Timeout())
}

// expectClose reads until the server's Close frame, requires its code, answers it as a client does
// and requires the connection to end.
func (c *wsClient) expectClose(want uint16) {
	c.t.Helper()
	c.readClose(want)
	c.write(wsMasked(0x88, wsClosePayload(want)))
	c.expectEnd(3 * time.Second)
}

// readClose reads until a Close frame and requires its code; it does not answer.
func (c *wsClient) readClose(want uint16) {
	c.t.Helper()
	for {
		op, p, err := c.frame(3 * time.Second)
		if err != nil {
			c.t.Fatalf("waiting for Close(%d): %v", want, err)
		}
		switch op {
		case 0x8:
			if len(p) < 2 {
				c.t.Fatalf("a Close without a code, want %d", want)
			}
			if got := binary.BigEndian.Uint16(p); got != want {
				c.t.Fatalf("closed with %d, want %d", got, want)
			}
			return
		case 0xA:
		default:
			c.t.Fatalf("waiting for Close(%d): frame op %d, %d bytes", want, op, len(p))
		}
	}
}

func (c *wsClient) expectEnd(within time.Duration) {
	c.t.Helper()
	_, _, err := c.frame(within)
	if !isClosed(err) {
		c.t.Fatalf("the server did not end the connection: %v", err)
	}
}

func wsAdminEnv(t *testing.T) *layerEnv {
	t.Helper()
	hash, err := password.HashWith("SecretAdmin1", password.Params{Time: 1, Memory: 64, Threads: 1, KeyLen: 32})
	if err != nil {
		t.Fatal(err)
	}
	env, _ := setupAdminEnv(t, config.Admin{On: true, PasswordRecord: hash})
	return env
}

func wsAuthFrame(t *testing.T) []byte {
	t.Helper()
	f, err := wire.EncodeFrame(&wire.AdminAuth{Password: "SecretAdmin1"}, wire.MaxFrame)
	if err != nil {
		t.Fatal(err)
	}
	return f
}

// requireAuthOK reads the next binary message and requires the admin-auth-ok in it.
func (c *wsClient) requireAuthOK() {
	c.t.Helper()
	for {
		op, p, err := c.frame(5 * time.Second)
		if err != nil {
			c.t.Fatalf("waiting for admin-auth-ok: %v", err)
		}
		if op == 0xA {
			continue
		}
		if op != 0x2 || len(p) < 4 {
			c.t.Fatalf("waiting for admin-auth-ok: frame op %d, %d bytes", op, len(p))
		}
		if n := binary.BigEndian.Uint32(p); int(n) != len(p)-4 {
			c.t.Fatalf("the reply is not one frame: word %d, message %d", n, len(p))
		}
		e, err := wire.Decode(p[4:])
		if err != nil {
			c.t.Fatal(err)
		}
		if ok, isOK := e.(*wire.AdminAuthOK); !isOK || ok.Token == "" {
			c.t.Fatalf("expected admin-auth-ok, got %+v", e)
		}
		return
	}
}

// requireAlive proves the server still serves: a fresh WebSocket authenticates.
func requireAlive(t *testing.T, env *layerEnv) {
	t.Helper()
	c := wsDial(t, env.addr)
	c.write(wsMasked(0x82, wsAuthFrame(t)))
	c.requireAuthOK()
}

// The 64-bit length of a frame is an unsigned number: a word with the top bit set used to become a
// negative int64, slip past a signed cap check and panic in make() - one frame after the upgrade ended
// the process (audit G-1). Every length above the cap is a close with 1009, whatever its bits.
func TestWebSocketLengthAboveTheCapClosesWith1009AndTheServerLives(t *testing.T) {
	env := wsAdminEnv(t)
	cases := []struct {
		name string
		word uint64
	}{
		{"top bit set", 1 << 63},
		{"all bits set", ^uint64(0)},
		{"largest positive int64", 1<<63 - 1},
		{"needs more than 32 bits", 1 << 32},
		{"one above the cap", 1048581},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			c := wsDial(t, env.addr)
			field := make([]byte, 9)
			field[0] = 0x80 | 127
			binary.BigEndian.PutUint64(field[1:], tc.word)
			c.write(wsMaskedWithLength(0x82, field, nil))
			c.expectClose(1009)
		})
	}
	requireAlive(t, env)
}

// "While the stream carries envelopes, one message is exactly one frame of 7.1 ... the length word
// equals the message length minus 4, else 1002." and "The limits of item T hold".
func TestWebSocketEnvelopeMessageIsExactlyOneFrame(t *testing.T) {
	env := wsAdminEnv(t)
	good := wsAuthFrame(t)
	word := func(n int, body ...byte) []byte {
		return append(binary.BigEndian.AppendUint32(nil, uint32(n)), body...)
	}
	cases := []struct {
		name string
		msgs [][]byte
	}{
		{"the length word alone, then the body", [][]byte{good[:4], good[4:]}},
		{"a length word larger than the message", [][]byte{word(len(good)-3, good[4:]...)}},
		{"five trailing bytes", [][]byte{append(append([]byte(nil), good...), 1, 2, 3, 4, 5)}},
		{"two frames in one message", [][]byte{append(append([]byte(nil), good...), good...)}},
		{"a message shorter than the length word", [][]byte{{0, 0, 1}}},
		{"a zero length word", [][]byte{word(0)}},
		{"a frame above the limit before authentication", [][]byte{word(16385, bytes.Repeat([]byte("a"), 16385)...)}},
		{"a body that is not an object", [][]byte{word(1, 'x')}},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			c := wsDial(t, env.addr)
			for _, m := range tc.msgs {
				if !c.write(wsMasked(0x82, m)) {
					break
				}
			}
			c.expectClose(1002)
		})
	}
	// the control: the same frame as one message is taken
	c := wsDial(t, env.addr)
	c.write(wsMasked(0x82, good))
	c.requireAuthOK()
}

// RFC 6455 5.4 lets a message come in fragments, with control frames between them. The contract says
// "message" and names no exception, so a fragmented message is one message: it is reassembled, and
// the envelope rules judge the whole.
func TestWebSocketFragmentedMessageIsReassembled(t *testing.T) {
	env := wsAdminEnv(t)
	good := wsAuthFrame(t)

	c := wsDial(t, env.addr)
	var out []byte
	pings := 0
	for off, size, i := 0, 1, 0; off < len(good); i++ {
		end := min(off+size, len(good))
		b0 := byte(0x00) // a continuation, not final
		if off == 0 {
			b0 = 0x02 // the binary message starts
		}
		if end == len(good) {
			b0 |= 0x80 // FIN
		}
		out = append(out, wsMasked(b0, good[off:end])...)
		if i%3 == 2 && end < len(good) {
			out = append(out, wsMasked(0x89, []byte("ping"))...)
			pings++
		}
		off, size = end, size%4+1
	}
	if pings == 0 {
		t.Fatal("the test sent no ping between fragments")
	}
	c.write(out)
	got := 0
	for {
		op, p, err := c.frame(5 * time.Second)
		if err != nil {
			t.Fatalf("after %d pongs: %v", got, err)
		}
		if op == 0xA {
			if string(p) != "ping" {
				t.Fatalf("pong payload %q", p)
			}
			got++
			continue
		}
		if op != 0x2 {
			t.Fatalf("frame op %d", op)
		}
		e, err := wire.Decode(p[4:])
		if err != nil {
			t.Fatal(err)
		}
		if _, ok := e.(*wire.AdminAuthOK); !ok {
			t.Fatalf("expected admin-auth-ok, got %+v", e)
		}
		break
	}
	if got != pings {
		t.Fatalf("%d pongs for %d pings", got, pings)
	}

	for _, tc := range []struct {
		name string
		code uint16
		out  []byte
	}{
		{"a continuation without a message", 1002, wsMasked(0x80, []byte("x"))},
		{"a new message inside a fragmented one", 1002, append(wsMasked(0x02, []byte("ab")), wsMasked(0x82, []byte("cd"))...)},
		{"fragments that together exceed the limit", 1002, append(wsMasked(0x02, bytes.Repeat([]byte("a"), 10000)), wsMasked(0x80, bytes.Repeat([]byte("a"), 10000))...)},
		{"a fragmented control frame", 1002, wsMasked(0x09, nil)},
		{"a control frame of 126 bytes", 1002, wsMasked(0x89, bytes.Repeat([]byte("a"), 126))},
	} {
		t.Run(tc.name, func(t *testing.T) {
			c := wsDial(t, env.addr)
			c.write(tc.out)
			c.expectClose(tc.code)
		})
	}
}

// The close codes of "Close" and the frames RFC 6455 makes a failure: 1002 for an unmasked client
// frame, set RSV bits (no extension is negotiated) and reserved opcodes, 1003 for a text message.
func TestWebSocketFramingViolationsCloseWithTheNamedCode(t *testing.T) {
	env := wsAdminEnv(t)
	body := []byte("abcde")
	for _, tc := range []struct {
		name string
		code uint16
		out  []byte
	}{
		{"unmasked binary frame", 1002, wsUnmasked(0x82, body)},
		{"unmasked ping", 1002, wsUnmasked(0x89, nil)},
		{"RSV1 (a compressed message nobody agreed to)", 1002, wsMasked(0xC2, body)},
		{"RSV2", 1002, wsMasked(0xA2, body)},
		{"RSV3", 1002, wsMasked(0x92, body)},
		{"reserved data opcode 3", 1002, wsMasked(0x83, body)},
		{"reserved control opcode 11", 1002, wsMasked(0x8B, nil)},
		{"a 16-bit length for a 5-byte payload", 1002, wsMaskedWithLength(0x82, []byte{0x80 | 126, 0, 5}, body)},
		{"a 64-bit length for a 5-byte payload", 1002, wsMaskedWithLength(0x82, []byte{0x80 | 127, 0, 0, 0, 0, 0, 0, 0, 5}, body)},
		{"a text message", 1003, wsMasked(0x81, body)},
		{"the first fragment of a text message", 1003, wsMasked(0x01, body)},
	} {
		t.Run(tc.name, func(t *testing.T) {
			c := wsDial(t, env.addr)
			c.write(tc.out)
			c.expectClose(tc.code)
		})
	}
	requireAlive(t, env)
}

// A Close from the client is answered with a Close, and then the connection ends: a code the peer may
// send is echoed, none means 1000, a payload that is not a Close payload is a protocol error.
func TestWebSocketClientCloseIsAnsweredAndEnds(t *testing.T) {
	env := wsAdminEnv(t)
	for _, tc := range []struct {
		name    string
		payload []byte
		reply   uint16
	}{
		{"no code", nil, 1000},
		{"1000", wsClosePayload(1000), 1000},
		{"1001 going away", wsClosePayload(1001), 1001},
		{"1005, which is never sent on the wire", wsClosePayload(1005), 1002},
		{"one byte", []byte{3}, 1002},
	} {
		t.Run(tc.name, func(t *testing.T) {
			c := wsDial(t, env.addr)
			c.write(wsMasked(0x88, tc.payload))
			c.readClose(tc.reply)
			c.expectEnd(3 * time.Second)
		})
	}
}

// "A server that sends Close waits briefly for the reply before it closes the transport." The
// administration stream ends on `bye` with a Close(1000) from the server.
func TestWebSocketServerCloseWaitsForThePeersReply(t *testing.T) {
	env := wsAdminEnv(t)
	c := wsDial(t, env.addr)
	c.write(wsMasked(0x82, wsAuthFrame(t)))
	c.requireAuthOK()
	bye, err := wire.EncodeFrame(&wire.Bye{}, wire.MaxFrameAuth)
	if err != nil {
		t.Fatal(err)
	}
	c.write(wsMasked(0x82, bye))
	c.readClose(1000)

	// the server holds the transport open while the client has not answered
	if _, _, err := c.frame(150 * time.Millisecond); isClosed(err) {
		t.Fatalf("the server closed the transport before the reply: %v", err)
	}
	c.write(wsMasked(0x88, wsClosePayload(1000)))
	start := time.Now()
	c.expectEnd(3 * time.Second)
	if d := time.Since(start); d > 500*time.Millisecond {
		t.Fatalf("the server took %v to close after the reply", d)
	}
}

// "A missing token, another version, or a method other than GET is 400." No extension is negotiated,
// whatever the client offers.
func TestWebSocketOpeningAnswers400AndNeverNegotiatesAnExtension(t *testing.T) {
	env := wsAdminEnv(t)
	do := func(t *testing.T, req string) *http.Response {
		t.Helper()
		conn, err := tls.Dial("tcp", env.addr, &tls.Config{InsecureSkipVerify: true})
		if err != nil {
			t.Fatal(err)
		}
		t.Cleanup(func() { _ = conn.Close() })
		_, _ = conn.Write([]byte(req))
		_ = conn.SetReadDeadline(time.Now().Add(3 * time.Second))
		resp, err := http.ReadResponse(bufio.NewReader(conn), nil)
		if err != nil {
			t.Fatal(err)
		}
		return resp
	}
	head := func(method, version, protocol string, extra string) string {
		h := method + " /v2/ws HTTP/1.1\r\nHost: " + env.addr + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
			"Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: " + version + "\r\n" + extra
		if protocol != "" {
			h += "Sec-WebSocket-Protocol: " + protocol + "\r\n"
		}
		return h + "\r\n"
	}
	for name, tc := range map[string]struct {
		req  string
		want int
	}{
		"POST":                    {head("POST", "13", "fms-exchange-v2", ""), 400},
		"HEAD":                    {head("HEAD", "13", "fms-exchange-v2", ""), 400},
		"PUT":                     {head("PUT", "13", "fms-exchange-v2", ""), 400},
		"a missing token":         {head("GET", "13", "", ""), 400},
		"another version":         {head("GET", "13", "fms-exchange-v1", ""), 400},
		"another WebSocket draft": {head("GET", "8", "fms-exchange-v2", ""), 400},
		"GET with the token":      {head("GET", "13", "fms-exchange-v2", ""), 101},
		"GET offering deflate": {head("GET", "13", "fms-exchange-v2",
			"Sec-WebSocket-Extensions: permessage-deflate; client_max_window_bits\r\n"), 101},
	} {
		t.Run(name, func(t *testing.T) {
			resp := do(t, tc.req)
			if resp.StatusCode != tc.want {
				t.Fatalf("answered %d, want %d", resp.StatusCode, tc.want)
			}
			if tc.want == 101 {
				if got := resp.Header.Get("Sec-WebSocket-Protocol"); got != "fms-exchange-v2" {
					t.Errorf("the token is echoed as %q", got)
				}
				if got := resp.Header.Get("Sec-WebSocket-Extensions"); got != "" {
					t.Errorf("an extension was negotiated: %q", got)
				}
			}
			if tc.want == 400 && strings.EqualFold(resp.Header.Get("Connection"), "upgrade") {
				t.Errorf("a refused opening must not look like an upgrade")
			}
		})
	}
}
