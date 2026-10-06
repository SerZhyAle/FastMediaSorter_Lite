// Package testkit is the harness shared by the module's tests, the in-process end-to-end proof and
// the manual-proof binary cmd/exchange-fakeproducer: a wire client, a fake producer that serves a
// real SSH server behind its attach streams, and a started server on loopback.
//
// It is not part of the shipped binary (nothing under cmd/fms-exchange-server imports it).
package testkit

import (
	"crypto/ed25519"
	"crypto/rand"
	"crypto/tls"
	"crypto/x509"
	"errors"
	"fmt"
	"net"
	"path/filepath"
	"strconv"
	"sync"
	"time"

	"golang.org/x/crypto/ssh"

	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/ports"
	"fms/exchange-server/internal/rendezvous"
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

// NewConn wraps c.
func NewConn(c net.Conn) *Conn {
	return &Conn{Conn: c, fr: wire.NewFrameReader(c), fw: wire.NewFrameWriter(c)}
}

// Send writes one envelope.
func (c *Conn) Send(m wire.Message) error { return c.fw.Write(m) }

// Recv reads one envelope, failing after timeout.
func (c *Conn) Recv(timeout time.Duration) (wire.Message, error) {
	_ = c.SetReadDeadline(time.Now().Add(timeout))
	defer c.SetReadDeadline(time.Time{})
	return c.fr.Read()
}

// DialTLS connects over TLS the way a producer does: no name check, the certificate pinned by its
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

// --- the fake producer ---

// Producer is a producer's side of the wire: it registers, answers the control stream and opens a
// data stream for every `open`, handing that stream to Handler.
type Producer struct {
	Addr      string
	Pin       string
	Password  string
	ShareID   string
	Keepalive int
	Port      *int
	Handler   func(net.Conn)
	// Secret is told about every secret the producer learns (resume token, tunnel ids), so a test
	// can prove none of them reaches a log.
	Secret func(string)

	mu         sync.Mutex
	control    *Conn
	registered wire.Message
}

// Register dials, registers (resuming with the stored token when there is one) and returns the
// server's answer. On `registered` the control stream is kept.
func (p *Producer) Register() (wire.Message, error) {
	c, err := DialTLS(p.Addr, p.Pin)
	if err != nil {
		return wire.Message{}, err
	}
	m := wire.New(wire.TypeRegister)
	m.Password, m.ShareID, m.KeepaliveSeconds, m.Port = p.Password, p.ShareID, p.Keepalive, p.Port
	p.mu.Lock()
	m.ResumeToken = p.registered.ResumeToken
	p.mu.Unlock()
	if err := c.Send(m); err != nil {
		_ = c.Close()
		return wire.Message{}, err
	}
	reply, err := c.Recv(5 * time.Second)
	if err != nil {
		_ = c.Close()
		return wire.Message{}, err
	}
	if reply.Type != wire.TypeRegistered {
		_ = c.Close()
		return reply, nil
	}
	p.mu.Lock()
	p.control, p.registered = c, reply
	p.mu.Unlock()
	if p.Secret != nil {
		p.Secret(reply.ResumeToken)
	}
	return reply, nil
}

// Registered is the last `registered` answer.
func (p *Producer) Registered() wire.Message {
	p.mu.Lock()
	defer p.mu.Unlock()
	return p.registered
}

// Control is the control stream.
func (p *Producer) Control() *Conn {
	p.mu.Lock()
	defer p.mu.Unlock()
	return p.control
}

// Serve answers the control stream until it closes: an `open` starts an attach. Run it in a goroutine.
func (p *Producer) Serve() {
	c := p.Control()
	for {
		m, err := c.fr.Read()
		if err != nil {
			return
		}
		if m.Type == wire.TypeOpen {
			if p.Secret != nil {
				p.Secret(m.TunnelID)
			}
			go p.Attach(m.TunnelID)
		}
	}
}

// Attach opens a data stream for one tunnel and hands it to Handler.
func (p *Producer) Attach(tunnelID string) {
	c, err := DialTLS(p.Addr, p.Pin)
	if err != nil {
		return
	}
	m := wire.New(wire.TypeAttach)
	m.ShareID, m.TunnelID = p.ShareID, tunnelID
	if err := c.Send(m); err != nil {
		_ = c.Close()
		return
	}
	p.Handler(c.Conn)
}

// SendKeepalive sends one keepalive on the control stream and waits for the echo.
func (p *Producer) SendKeepalive() error {
	c := p.Control()
	if err := c.Send(wire.New(wire.TypeKeepalive)); err != nil {
		return err
	}
	m, err := c.Recv(5 * time.Second)
	if err != nil {
		return err
	}
	if m.Type != wire.TypeKeepalive {
		return fmt.Errorf("testkit: expected keepalive, got %s", m.Type)
	}
	return nil
}

// Close closes the control stream.
func (p *Producer) Close() {
	if c := p.Control(); c != nil {
		_ = c.Close()
	}
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

// --- a started server ---

// Options tune StartServer; a zero field keeps the test default.
type Options struct {
	Params      *rendezvous.Params
	Limits      *limits.Config
	PortRange   [2]int // zero: no pool
	VerifyConc  int
	VerifyWait  time.Duration
	PreAuthSlot int
	Now         func() time.Time
	// Verify replaces the password check (a test that must hold a verification in flight).
	Verify func(string) bool
}

// Env is a running server.
type Env struct {
	Server      *rendezvous.Server
	Addr        string
	Host        string
	Fingerprint string
	Password    string
	Log         *logx.Capture
	Limiter     *limits.Limiter
	Verifier    *limits.Verifier
	Pool        *ports.Pool
	Params      rendezvous.Params

	secretMu sync.Mutex
	secrets  []string
}

// TestPassword is the password every started server accepts.
const TestPassword = "Passw0rdTest"

// cheap Argon2id so a test run does not spend seconds on the hash.
var cheap = password.Params{Time: 1, Memory: 8, Threads: 1, KeyLen: 32}

// TestParams are defaults tuned for tests: short delays and windows.
func TestParams() rendezvous.Params {
	p := rendezvous.DefaultParams()
	p.PendingWindow = 2 * time.Second
	p.UnavailableDelay = 60 * time.Millisecond
	p.IdleTimeout = 10 * time.Second
	p.PeekTimeout = time.Second
	p.FirstFrameTimeout = 2 * time.Second
	p.SweepInterval = 50 * time.Millisecond
	return p
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

// StartServer starts a server on 127.0.0.1 with a fresh identity and the test password.
func StartServer(t TB, opt Options) *Env {
	t.Helper()
	dir := t.TempDir()
	id, err := tlsid.LoadOrCreate(filepath.Join(dir, "cert.pem"), filepath.Join(dir, "key.pem"), nil)
	if err != nil {
		t.Fatalf("identity: %v", err)
	}
	rec, err := password.HashWith(TestPassword, cheap)
	if err != nil {
		t.Fatalf("hash: %v", err)
	}
	env := &Env{Params: TestParams(), Password: TestPassword, Fingerprint: id.Fingerprint, Log: logx.NewCapture()}
	if opt.Params != nil {
		env.Params = *opt.Params
	}
	lc := limits.DefaultConfig()
	if opt.Limits != nil {
		lc = *opt.Limits
	}
	env.Limiter = limits.NewLimiter(lc, opt.Now)
	conc, wait := opt.VerifyConc, opt.VerifyWait
	if conc == 0 {
		conc = 2
	}
	if wait == 0 {
		wait = 2 * time.Second
	}
	verify := opt.Verify
	if verify == nil {
		verify = func(p string) bool { return password.Verify(rec, p) }
	}
	env.Verifier = limits.NewVerifier(verify, conc, wait)
	slots := opt.PreAuthSlot
	if slots == 0 {
		slots = 16
	}
	if opt.PortRange[0] != 0 {
		env.Pool = ports.NewPool(opt.PortRange[0], opt.PortRange[1], "127.0.0.1", nil, opt.Now)
	}
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("listen: %v", err)
	}
	env.Addr = ln.Addr().String()
	env.Host = "127.0.0.1"
	env.Server = rendezvous.New(rendezvous.Config{
		Params:   env.Params,
		TLS:      &tls.Config{Certificates: []tls.Certificate{id.Cert}, MinVersion: tls.VersionTLS12},
		Verifier: env.Verifier, Limiter: env.Limiter, PreAuth: limits.NewPreAuth(slots),
		Pool: env.Pool, Log: env.Log.Logger, Now: opt.Now,
	})
	go func() { _ = env.Server.Serve(ln) }()
	t.Cleanup(env.Server.Close)
	// Every test of every package that starts a server also proves, at its end, that no secret it
	// handled reached the log (ticket 139 acceptance 1). Cleanups run last-in first-out, so this
	// one runs before the server closes.
	t.Cleanup(func() { env.AssertLogsClean(t) })
	return env
}

// NewProducer returns a producer for this server with a fresh share id and an SSH server behind it.
// Every secret it learns is remembered, and AssertLogsClean checks none of them is in the log.
func (e *Env) NewProducer(sshPassword string) *Producer {
	cfg := SSHServerConfig(sshPassword)
	p := &Producer{
		Addr: e.Addr, Pin: e.Fingerprint, Password: e.Password, ShareID: wire.NewID(), Keepalive: 30,
		Handler: func(c net.Conn) { ServeSSH(c, cfg) },
		Secret:  e.AddSecret,
	}
	e.AddSecret(p.ShareID)
	return p
}

// AddSecret records a value that must never appear in a log line.
func (e *Env) AddSecret(s string) {
	if s == "" {
		return
	}
	e.secretMu.Lock()
	e.secrets = append(e.secrets, s)
	e.secretMu.Unlock()
}

// AssertLogsClean fails if any recorded secret, or the server password, is in the captured log.
func (e *Env) AssertLogsClean(t TB) {
	t.Helper()
	e.secretMu.Lock()
	all := append([]string{e.Password}, e.secrets...)
	e.secretMu.Unlock()
	e.Log.AssertNone(t, all...)
}
