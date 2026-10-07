// Package testkit is the harness shared by the module's tests, the in-process end-to-end proof and
// the manual-proof binary cmd/exchange-fakeproducer: a wire client that speaks DEVICE-EXCHANGE
// section 7, a device that enrolls and says hello, a producer that serves a real SSH server behind
// its attach streams, and a started server on loopback.
//
// It is not part of the shipped binary (nothing under cmd/fms-exchange-server imports it).
package testkit

import (
	"crypto/ed25519"
	"crypto/rand"
	"crypto/tls"
	"crypto/x509"
	"errors"
	"net"
	"strconv"
	"time"

	"golang.org/x/crypto/ssh"

	"fms/exchange-server/internal/tlsid"
	"fms/exchange-server/internal/wire"
)

// TB is the part of testing.TB the harness needs.
type TB interface {
	Helper()
	Fatalf(format string, args ...any)
	Cleanup(func())
	TempDir() string
}

// --- wire client ---

// Conn is a stream with a frame codec on it. After `connected` use the embedded net.Conn directly:
// the reader never reads beyond a frame, so the raw bytes that follow are untouched.
type Conn struct {
	net.Conn
	fr *wire.FrameReader
	fw *wire.FrameWriter
}

// NewConn wraps c under the pre-authentication frame limit; call Authenticated after welcome.
func NewConn(c net.Conn) *Conn {
	return &Conn{Conn: c, fr: wire.NewFrameReader(c, wire.MaxFrame), fw: wire.NewFrameWriter(c, wire.MaxFrame)}
}

// Authenticated raises the frame limit both ways, as the server did when it sent welcome or enrolled.
func (c *Conn) Authenticated() {
	c.fr.SetLimit(wire.MaxFrameAuth)
	c.fw.SetLimit(wire.MaxFrameAuth)
}

// Send writes one envelope.
func (c *Conn) Send(e wire.Envelope) error { return c.fw.Write(e) }

// SendRaw writes bytes as they are (a frame built by hand, or bytes behind a frame).
func (c *Conn) SendRaw(b []byte) error { _, err := c.Conn.Write(b); return err }

// Recv reads one envelope (an unknown type comes back as *wire.Unknown), failing after timeout.
func (c *Conn) Recv(timeout time.Duration) (wire.Envelope, error) {
	_ = c.SetReadDeadline(time.Now().Add(timeout))
	defer c.SetReadDeadline(time.Time{})
	return c.fr.Read()
}

// DialTLS connects over TLS the way a device does: no name check, the certificate pinned by its
// fingerprint when pin is not empty.
func DialTLS(addr, pin string) (*Conn, error) {
	cfg := &tls.Config{
		InsecureSkipVerify: true, // trust on first use: the pin below is the check
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
	return NewConn(c), nil
}

// DialPlain connects without TLS.
func DialPlain(addr string) (*Conn, error) {
	c, err := net.DialTimeout("tcp", addr, 5*time.Second)
	if err != nil {
		return nil, err
	}
	return NewConn(c), nil
}

// RawFrame frames a JSON body by hand, for envelopes the codec would not build.
func RawFrame(body string) []byte {
	n := len(body)
	return append([]byte{byte(n >> 24), byte(n >> 16), byte(n >> 8), byte(n)}, body...)
}

// --- a real SSH server and client ---

// SSHServerConfig returns a server config with a fresh ed25519 host key and one password.
func SSHServerConfig(pw string) *ssh.ServerConfig {
	_, priv, err := ed25519.GenerateKey(rand.Reader)
	if err != nil {
		panic(err)
	}
	signer, err := ssh.NewSignerFromKey(priv)
	if err != nil {
		panic(err)
	}
	cfg := &ssh.ServerConfig{PasswordCallback: func(_ ssh.ConnMetadata, got []byte) (*ssh.Permissions, error) {
		if string(got) == pw {
			return nil, nil
		}
		return nil, errors.New("denied")
	}}
	cfg.AddHostKey(signer)
	return cfg
}

// ServeSSH runs an SSH server on conn: every `exec` request is answered with "echo:" + command.
func ServeSSH(conn net.Conn, cfg *ssh.ServerConfig) {
	sc, chans, reqs, err := ssh.NewServerConn(conn, cfg)
	if err != nil {
		_ = conn.Close()
		return
	}
	defer sc.Close()
	go ssh.DiscardRequests(reqs)
	for nc := range chans {
		if nc.ChannelType() != "session" {
			_ = nc.Reject(ssh.UnknownChannelType, "session only")
			continue
		}
		ch, creqs, err := nc.Accept()
		if err != nil {
			continue
		}
		go func() {
			defer ch.Close()
			for r := range creqs {
				if r.Type != "exec" {
					_ = r.Reply(false, nil)
					continue
				}
				var p struct{ Cmd string }
				_ = ssh.Unmarshal(r.Payload, &p)
				_ = r.Reply(true, nil)
				_, _ = ch.Write([]byte("echo:" + p.Cmd))
				_, _ = ch.SendRequest("exit-status", false, ssh.Marshal(struct{ Status uint32 }{0}))
				return
			}
		}()
	}
}

// SSHClient runs the SSH handshake over an already connected stream.
func SSHClient(conn net.Conn, user, pw string) (*ssh.Client, error) {
	cc, chans, reqs, err := ssh.NewClientConn(conn, "exchange", &ssh.ClientConfig{
		User: user, Auth: []ssh.AuthMethod{ssh.Password(pw)},
		HostKeyCallback: ssh.InsecureIgnoreHostKey(), Timeout: 10 * time.Second,
	})
	if err != nil {
		return nil, err
	}
	return ssh.NewClient(cc, chans, reqs), nil
}

// SSHExecOn runs one command on an open client and returns its output.
func SSHExecOn(client *ssh.Client, cmd string) (string, error) {
	s, err := client.NewSession()
	if err != nil {
		return "", err
	}
	defer s.Close()
	out, err := s.Output(cmd)
	return string(out), err
}

// SSHExec runs one command over an already connected stream and returns its output.
func SSHExec(conn net.Conn, user, pw, cmd string) (string, error) {
	client, err := SSHClient(conn, user, pw)
	if err != nil {
		return "", err
	}
	defer client.Close()
	s, err := client.NewSession()
	if err != nil {
		return "", err
	}
	defer s.Close()
	out, err := s.Output(cmd)
	return string(out), err
}

// FreeRange finds n consecutive loopback ports that can be bound right now.
func FreeRange(n int) (lo, hi int) {
	for base := 41000; base < 60000; base += 37 {
		ok := true
		var lns []net.Listener
		for p := base; p < base+n; p++ {
			ln, err := net.Listen("tcp", net.JoinHostPort("127.0.0.1", strconv.Itoa(p)))
			if err != nil {
				ok = false
				break
			}
			lns = append(lns, ln)
		}
		for _, ln := range lns {
			_ = ln.Close()
		}
		if ok {
			return base, base + n - 1
		}
	}
	panic("testkit: no free port range")
}
