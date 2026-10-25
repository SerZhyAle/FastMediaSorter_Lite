package testkit

import (
	"bufio"
	"crypto/tls"
	"crypto/x509"
	"encoding/binary"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"time"

	"fms/exchange-server/internal/tlsid"
	"fms/exchange-server/internal/wire"
)

// The relay's side of the scripted client (ticket 165): a broadcaster that starts a broadcast and
// uploads chunks, and a listener that GETs the listen URL inside TLS and reads the body. The probe of
// ticket 205 uses the same pieces against a real deployment.

// BroadcastJSON builds a section 6.3 `broadcast` record for id: the descriptor is a stub with an
// endpoints list (a receiver imports it as it imports one from a link).
func BroadcastJSON(id, mode, title string) json.RawMessage {
	return json.RawMessage(fmt.Sprintf(`{"broadcastId":%q,"mode":%q,"title":%q,"descriptor":{"endpoints":[]}}`, id, mode, title))
}

// StartBroadcast sends `broadcast-start` on the device's control stream and returns the broadcast id
// the server answered with (or the refusal as an error).
func (d *Device) StartBroadcast(mode string, maxListeners *int) (string, error) {
	return d.StartBroadcastWith(mode, maxListeners, nil)
}

// StartBroadcastWith is StartBroadcast with the broadcast's listener verifiers (item BF, item DV): nil sends no
// `verifiers` member, a pointer to an empty list sends `"verifiers": []`.
func (d *Device) StartBroadcastWith(mode string, maxListeners *int, verifiers *[]wire.ListenerVerifier) (string, error) {
	id := wire.NewID()
	d.secret(id)
	ctl := d.Control()
	if ctl == nil {
		return "", errors.New("testkit: the device has no control stream")
	}
	r, err := ctl.Request(&wire.BroadcastStart{Broadcast: BroadcastJSON(id, mode, "probe"), MaxListeners: maxListeners, Verifiers: verifiers},
		5*time.Second, wire.TypeBroadcastStarted, wire.TypeRefused)
	if err != nil {
		return "", err
	}
	if f, ok := r.(*wire.Refused); ok {
		return "", fmt.Errorf("broadcast-start refused: %s", f.Reason)
	}
	return r.(*wire.BroadcastStarted).BroadcastID, nil
}

// UpdateBroadcast sends `broadcast-update` for a broadcast StartBroadcast made (not answered; a test waits for
// the effect it looks for). nil verifiers leave the held list unchanged, a pointer to a list replaces it.
func (d *Device) UpdateBroadcast(id, mode, title string, verifiers *[]wire.ListenerVerifier) error {
	ctl := d.Control()
	if ctl == nil {
		return errors.New("testkit: the device has no control stream")
	}
	return ctl.Send(&wire.BroadcastUpdate{Broadcast: BroadcastJSON(id, mode, title), Verifiers: verifiers})
}

// EndBroadcast sends `broadcast-end` (not answered).
func (d *Device) EndBroadcast(id string) error {
	return d.Control().Send(&wire.BroadcastEnd{BroadcastID: id})
}

// Upload opens a data stream for a broadcast: hello, `upload`, and the answer. On `upload-accepted` the
// stream carries chunks (WriteChunk); any other answer is returned as the envelope with a nil stream.
func (d *Device) Upload(broadcastID, contentType string) (*Conn, wire.Envelope, error) {
	c, r, err := d.Hello()
	if err != nil {
		return nil, nil, err
	}
	if c == nil {
		return nil, r, nil
	}
	if err := c.Send(&wire.Upload{BroadcastID: broadcastID, ContentType: contentType}); err != nil {
		_ = c.Close()
		return nil, nil, err
	}
	ans, err := c.Recv(5 * time.Second)
	if err != nil {
		_ = c.Close()
		return nil, nil, err
	}
	if _, ok := ans.(*wire.UploadAccepted); !ok {
		_ = c.Close()
		return nil, ans, nil
	}
	return c, ans, nil
}

// WriteChunk writes one chunk of an upload.
func (c *Conn) WriteChunk(flags byte, payload []byte) error {
	b, err := wire.AppendChunk(nil, flags, payload)
	if err != nil {
		return err
	}
	return c.SendRaw(b)
}

// EndUpload writes the zero-length chunk that ends a broadcast cleanly.
func (c *Conn) EndUpload() error { return c.SendRaw(wire.EndChunk()) }

// Listening is a listener's connection after the response head: Resp.Body is the decoded body.
type Listening struct {
	Conn net.Conn
	Resp *http.Response
}

// DialListen connects over TLS (pinned like a device does) and sends `GET /v2/b/<id>/stream`; method
// and extra headers can be changed by Raw. It returns after the response head, whatever the status.
func DialListen(addr, pin, broadcastID string) (*Listening, error) {
	return DialRaw(addr, pin, "GET "+wire.ListenPath(broadcastID)+" HTTP/1.1\r\nHost: exchange\r\nAccept: */*\r\n\r\n", "GET")
}

// DialRaw connects over TLS and sends the request bytes as they are, then reads the response head for
// a request of the given method.
func DialRaw(addr, pin, request, method string) (*Listening, error) {
	cfg := &tls.Config{
		InsecureSkipVerify: true, // the pin below is the check
		MinVersion:         tls.VersionTLS12,
		VerifyPeerCertificate: func(raw [][]byte, _ [][]*x509.Certificate) error {
			if pin == "" {
				return nil
			}
			if len(raw) == 0 || tlsid.Fingerprint(raw[0]) != pin {
				return errors.New("testkit: the server's certificate is not the pinned one")
			}
			return nil
		},
	}
	d := net.Dialer{Timeout: 5 * time.Second}
	c, err := tls.DialWithDialer(&d, "tcp", addr, cfg)
	if err != nil {
		return nil, err
	}
	if _, err := io.WriteString(c, request); err != nil {
		_ = c.Close()
		return nil, err
	}
	_ = c.SetReadDeadline(time.Now().Add(8 * time.Second))
	resp, err := http.ReadResponse(bufio.NewReader(c), &http.Request{Method: method})
	if err != nil {
		_ = c.Close()
		return nil, err
	}
	_ = c.SetReadDeadline(time.Time{})
	return &Listening{Conn: c, Resp: resp}, nil
}

// Close closes the connection.
func (l *Listening) Close() { _ = l.Conn.Close() }

// ReadFull reads exactly n body bytes within timeout.
func (l *Listening) ReadFull(n int, timeout time.Duration) ([]byte, error) {
	_ = l.Conn.SetReadDeadline(time.Now().Add(timeout))
	defer l.Conn.SetReadDeadline(time.Time{})
	buf := make([]byte, n)
	if _, err := io.ReadFull(l.Resp.Body, buf); err != nil {
		return nil, err
	}
	return buf, nil
}

// ReadToEnd reads the body until its clean end (the final chunk) within timeout and returns what was
// read; a body that was cut instead of ended is the error.
func (l *Listening) ReadToEnd(timeout time.Duration) ([]byte, error) {
	_ = l.Conn.SetReadDeadline(time.Now().Add(timeout))
	defer l.Conn.SetReadDeadline(time.Time{})
	return io.ReadAll(l.Resp.Body)
}

// SeqPayload is a fixed-size payload that carries a sequence number, so a reader that sees only the
// concatenated body can tell which chunk it is looking at.
func SeqPayload(seq uint32, size int) []byte {
	if size < 4 {
		size = 4
	}
	p := make([]byte, size)
	binary.BigEndian.PutUint32(p, seq)
	for i := 4; i < size; i++ {
		p[i] = byte(seq) ^ byte(i)
	}
	return p
}

// ReadSeq reads one SeqPayload of size and returns its sequence number.
func (l *Listening) ReadSeq(size int, timeout time.Duration) (uint32, error) {
	b, err := l.ReadFull(size, timeout)
	if err != nil {
		return 0, err
	}
	return binary.BigEndian.Uint32(b), nil
}
