package testkit

import (
	"encoding/json"
	"errors"
	"fmt"
	"net"
	"sync"
	"time"

	"fms/exchange-server/internal/wire"
)

// A Device is a scripted client of DEVICE-EXCHANGE: it enrolls with a password or a pairing code,
// says hello on new streams, and keeps one control stream. Everything it learns that must never reach
// a log - its ids, its token, a tunnel id, a pairing code - is told to the Env, which checks the log
// at the end of the test.

// Device is one scripted installation.
type Device struct {
	Env     *Env
	Addr    string
	Pin     string
	ID      string
	Login   string
	Name    string
	Product string
	Token   string
	// Password is what Enroll presents (default testkit.TestPassword).
	Password string
	// Roles are the roles the device declares (default: producer, consumer and receiver); an empty
	// non-nil slice declares none.
	Roles []string

	mu      sync.Mutex
	control *Control
}

// NewDevice returns a device of an account with a fresh deviceId, not yet enrolled.
func (e *Env) NewDevice(login string) *Device {
	d := &Device{Env: e, Addr: e.Addr, Pin: e.Fingerprint, ID: wire.NewID(), Login: login, Name: "Test device", Product: "fms-windows"}
	e.AddSecret(d.ID)
	return d
}

func (d *Device) enrollFrame() *wire.Enroll {
	roles := d.Roles
	if roles == nil {
		roles = []string{"resource-producer", "resource-consumer", "receiver"}
	}
	return &wire.Enroll{Login: d.Login, DeviceID: d.ID, DeviceName: d.Name, Product: d.Product,
		ProductVersion: "1.0", Platform: "test", Roles: roles,
		Receiver: json.RawMessage(`{"modes":["AUDIO_ONLY"],"transports":["HTTP_ADTS"]}`)}
}

// Enroll enrolls with the account's password on a new TLS stream. On `enrolled` the token is kept
// and the stream becomes the device's Control (authenticated, with no first envelope sent yet).
func (d *Device) Enroll() (wire.Envelope, error) {
	pw := d.Password
	if pw == "" {
		pw = TestPassword
	}
	return d.enroll(pw, "")
}

// EnrollWith enrolls with an explicit password.
func (d *Device) EnrollWith(pw string) (wire.Envelope, error) { return d.enroll(pw, "") }

// EnrollWithCode enrolls with a pairing code.
func (d *Device) EnrollWithCode(code string) (wire.Envelope, error) { return d.enroll("", code) }

func (d *Device) enroll(pw, code string) (wire.Envelope, error) {
	c, err := DialTLS(d.Addr, d.Pin)
	if err != nil {
		return nil, err
	}
	e := d.enrollFrame()
	e.Password, e.PairingCode = pw, code
	if err := c.Send(e); err != nil {
		_ = c.Close()
		return nil, err
	}
	r, err := c.Recv(5 * time.Second)
	if err != nil {
		_ = c.Close()
		return nil, err
	}
	en, ok := r.(*wire.Enrolled)
	if !ok {
		_ = c.Close()
		return r, nil
	}
	d.Token = en.DeviceToken
	d.secret(d.Token)
	c.Authenticated()
	d.mu.Lock()
	d.control = newControl(d, c)
	d.mu.Unlock()
	return en, nil
}

// Hello opens a new TLS stream and says hello with the device's token. On `welcome` the connection
// is returned authenticated; otherwise the refusal and a nil connection.
func (d *Device) Hello() (*Conn, wire.Envelope, error) { return d.HelloWith(d.Token) }

// HelloWith says hello with an explicit token.
func (d *Device) HelloWith(token string) (*Conn, wire.Envelope, error) {
	c, err := DialTLS(d.Addr, d.Pin)
	if err != nil {
		return nil, nil, err
	}
	if err := c.Send(&wire.Hello{DeviceID: d.ID, DeviceToken: token, ProductVersion: "1.0"}); err != nil {
		_ = c.Close()
		return nil, nil, err
	}
	r, err := c.Recv(5 * time.Second)
	if err != nil {
		_ = c.Close()
		return nil, nil, err
	}
	if _, ok := r.(*wire.Welcome); !ok {
		_ = c.Close()
		return nil, r, nil
	}
	c.Authenticated()
	return c, r, nil
}

// Online makes the device's control stream: a hello on a new stream, then `subscribe` as the first
// envelope, which fixes the role. The reader goroutine starts.
func (d *Device) Online() (*Control, error) {
	c, r, err := d.Hello()
	if err != nil {
		return nil, err
	}
	if c == nil {
		return nil, fmt.Errorf("testkit: hello was refused: %+v", r)
	}
	ctl := newControl(d, c)
	if err := ctl.Send(&wire.Subscribe{}); err != nil {
		return nil, err
	}
	d.mu.Lock()
	d.control = ctl
	d.mu.Unlock()
	return ctl, nil
}

// OnlineQuietly is Online without a subscription: a keepalive fixes the role.
func (d *Device) OnlineQuietly() (*Control, error) {
	c, r, err := d.Hello()
	if err != nil {
		return nil, err
	}
	if c == nil {
		return nil, fmt.Errorf("testkit: hello was refused: %+v", r)
	}
	ctl := newControl(d, c)
	if err := ctl.Send(&wire.Keepalive{}); err != nil {
		return nil, err
	}
	if _, err := ctl.Await(3*time.Second, wire.TypeKeepalive); err != nil {
		return nil, err
	}
	d.mu.Lock()
	d.control = ctl
	d.mu.Unlock()
	return ctl, nil
}

// Control is the device's current control stream.
func (d *Device) Control() *Control {
	d.mu.Lock()
	defer d.mu.Unlock()
	return d.control
}

// Close closes the control stream.
func (d *Device) Close() {
	if c := d.Control(); c != nil {
		c.Close()
	}
}

// Connect opens a data stream by hello and `connect`, and returns it after `connected`, or the
// envelope that came instead.
func (d *Device) Connect(m *wire.Connect) (*Conn, wire.Envelope, error) {
	c, r, err := d.Hello()
	if err != nil {
		return nil, nil, err
	}
	if c == nil {
		return nil, r, nil
	}
	return d.connectOn(c, m)
}

func (d *Device) connectOn(c *Conn, m *wire.Connect) (*Conn, wire.Envelope, error) {
	if err := c.Send(m); err != nil {
		_ = c.Close()
		return nil, nil, err
	}
	r, err := c.Recv(8 * time.Second)
	if err != nil {
		_ = c.Close()
		return nil, nil, err
	}
	if _, ok := r.(*wire.Connected); !ok {
		return nil, r, nil
	}
	return c, r, nil
}

// OutsiderConnect connects by a capability id with no hello at all.
func (e *Env) OutsiderConnect(m *wire.Connect) (*Conn, wire.Envelope, error) {
	c, err := DialTLS(e.Addr, e.Fingerprint)
	if err != nil {
		return nil, nil, err
	}
	if err := c.Send(m); err != nil {
		_ = c.Close()
		return nil, nil, err
	}
	r, err := c.Recv(8 * time.Second)
	if err != nil {
		_ = c.Close()
		return nil, nil, err
	}
	if _, ok := r.(*wire.Connected); !ok {
		return nil, r, nil
	}
	return c, r, nil
}

// --- the control stream with a reader ---

// Control is a control stream with a background reader: `open` is answered by the device's Producer
// handler (if any), everything else lands in the inbox for Await.
type Control struct {
	d     *Device
	Conn  *Conn
	inbox chan wire.Envelope
	done  chan struct{}
	once  sync.Once

	mu     sync.Mutex
	onOpen func(*wire.Open)
}

func newControl(d *Device, c *Conn) *Control {
	ctl := &Control{d: d, Conn: c, inbox: make(chan wire.Envelope, 256), done: make(chan struct{})}
	go ctl.read()
	return ctl
}

func (c *Control) read() {
	defer close(c.done)
	for {
		e, err := c.Conn.fr.Read()
		if err != nil {
			return
		}
		if o, ok := e.(*wire.Open); ok {
			c.d.secret(o.TunnelID)
			c.mu.Lock()
			f := c.onOpen
			c.mu.Unlock()
			if f != nil {
				go f(o)
			}
			continue
		}
		select {
		case c.inbox <- e:
		default: // an overflowing inbox drops: a test that does not read does not care
		}
	}
}

// Send writes one envelope.
func (c *Control) Send(e wire.Envelope) error { return c.Conn.Send(e) }

// Closed is closed when the stream ended.
func (c *Control) Closed() <-chan struct{} { return c.done }

// Close closes the stream.
func (c *Control) Close() { c.once.Do(func() { _ = c.Conn.Close() }) }

// Await returns the next envelope of one of the types, discarding any other that arrives first.
func (c *Control) Await(timeout time.Duration, types ...string) (wire.Envelope, error) {
	deadline := time.After(timeout)
	for {
		select {
		case e := <-c.inbox:
			if len(types) == 0 {
				return e, nil
			}
			for _, t := range types {
				if e.TypeName() == t {
					return e, nil
				}
			}
		case <-c.done:
			select { // drain what arrived before the close
			case e := <-c.inbox:
				if len(types) == 0 {
					return e, nil
				}
				for _, t := range types {
					if e.TypeName() == t {
						return e, nil
					}
				}
				continue
			default:
			}
			return nil, errors.New("testkit: the control stream ended")
		case <-deadline:
			return nil, fmt.Errorf("testkit: no %v within %v", types, timeout)
		}
	}
}

// Drain discards whatever is in the inbox and returns how many envelopes that was.
func (c *Control) Drain() int {
	n := 0
	for {
		select {
		case <-c.inbox:
			n++
		default:
			return n
		}
	}
}

// Request sends an envelope and waits for an answer of one of the types.
func (c *Control) Request(e wire.Envelope, timeout time.Duration, types ...string) (wire.Envelope, error) {
	if err := c.Send(e); err != nil {
		return nil, err
	}
	return c.Await(timeout, types...)
}

// --- a producer: a device that serves a real SSH server behind its attach streams ---

// Producer is a Device that answers `open` by opening a data stream, saying hello, `attach`ing, and
// handing the stream to Handler.
type Producer struct {
	*Device
	Handler func(net.Conn)
}

// NewProducer returns a producer of an account whose Handler is an SSH server with one password.
func (e *Env) NewProducer(login, sshPassword string) *Producer {
	cfg := SSHServerConfig(sshPassword)
	return &Producer{Device: e.NewDevice(login), Handler: func(c net.Conn) { ServeSSH(c, cfg) }}
}

// Start enrolls the device and takes it online: after Start its control stream answers every `open`.
// It returns the enrolled producer ready to publish.
func (p *Producer) Start() error {
	r, err := p.Enroll()
	if err != nil {
		return err
	}
	if _, ok := r.(*wire.Enrolled); !ok {
		return fmt.Errorf("testkit: enroll was refused: %+v", r)
	}
	ctl := p.Control()
	ctl.mu.Lock()
	ctl.onOpen = p.Answer
	ctl.mu.Unlock()
	// the stream already enrolled is authenticated: the first envelope fixes its role
	return ctl.Send(&wire.Subscribe{})
}

// Resume takes an already enrolled producer online on a new stream (a restart of the server, a drop).
func (p *Producer) Resume() error {
	ctl, err := p.Online()
	if err != nil {
		return err
	}
	ctl.mu.Lock()
	ctl.onOpen = p.Answer
	ctl.mu.Unlock()
	return nil
}

// Answer is the producer's reply to an `open`: a new TLS stream, hello, attach, and the stream to Handler.
func (p *Producer) Answer(o *wire.Open) {
	c, r, err := p.Hello()
	if err != nil || c == nil {
		_ = r
		return
	}
	if err := c.Send(&wire.Attach{TunnelID: o.TunnelID}); err != nil {
		_ = c.Close()
		return
	}
	p.Handler(c.Conn)
}

// Attach opens a data stream for a tunnel id by hand (a test that attaches wrongly).
func (p *Producer) Attach(tunnelID string) (*Conn, error) {
	c, r, err := p.Hello()
	if err != nil {
		return nil, err
	}
	if c == nil {
		return nil, fmt.Errorf("testkit: hello was refused: %+v", r)
	}
	return c, c.Send(&wire.Attach{TunnelID: tunnelID})
}

// ResourceJSON builds a section 6.2 `sftp-share` record. share adds a shareId, port a `publicPort`
// request (nil: none); the returned ids are what the record carries.
func (p *Producer) ResourceJSON(name string, share bool, port *int) (resourceID, shareID string, raw json.RawMessage) {
	resourceID = wire.NewID()
	p.secret(resourceID)
	rec := wire.NewRecord()
	rec.SetStr("resourceId", resourceID)
	rec.SetStr("deviceId", p.ID)
	rec.SetStr("kind", "sftp-share")
	rec.SetStr("name", name)
	rec.SetRaw("access", json.RawMessage(`{"descriptor":"FMSSFTP2.test-secret-access-string","root":"/"}`))
	if share {
		shareID = wire.NewID()
		p.secret(shareID)
		rec.SetStr("shareId", shareID)
	}
	if port != nil {
		rec.SetInt("publicPort", *port)
	}
	return resourceID, shareID, rec.Marshal()
}

// Publish publishes a record and returns the answer (`published` or `refused`).
func (p *Producer) Publish(raw json.RawMessage) (wire.Envelope, error) {
	return p.Control().Request(&wire.Publish{Resource: raw}, 5*time.Second, wire.TypePublished, wire.TypeRefused)
}

// PublishShare publishes a fresh share and returns its ids and the `published` answer.
func (p *Producer) PublishShare(share bool, port *int) (resourceID, shareID string, ans wire.Envelope, err error) {
	resourceID, shareID, raw := p.ResourceJSON("Photos", share, port)
	ans, err = p.Publish(raw)
	return
}

// --- a real SSH server and client ---

// SSHThrough runs one SSH command through an already connected tunnel.
func SSHThrough(c net.Conn, password, cmd string) (string, error) {
	return SSHExec(c, "u", password, cmd)
}

// NewID returns a fresh id and records it as a secret the log must never carry.
func (e *Env) NewID() string {
	id := wire.NewID()
	e.AddSecret(id)
	return id
}

// ListDirectory sends `list` on a control stream and returns the `directory` that answers.
func (c *Control) ListDirectory() (*wire.Directory, error) {
	r, err := c.Request(&wire.List{}, 5*time.Second, wire.TypeDirectory)
	if err != nil {
		return nil, err
	}
	return r.(*wire.Directory), nil
}

// Rebind points the device at another server of the same deployment - a restart on the same data
// directory comes back on another port - and drops its control stream.
func (d *Device) Rebind(e *Env) {
	d.Close()
	d.Env, d.Addr, d.Pin = e, e.Addr, e.Fingerprint
	e.AddSecret(d.ID)
	e.AddSecret(d.Token)
}

// OnOpen replaces what is done with an `open` the server sends on this stream.
func (c *Control) OnOpen(f func(*wire.Open)) {
	c.mu.Lock()
	c.onOpen = f
	c.mu.Unlock()
}

// secret tells the Env (when there is one) about a value that must never reach a log.
func (d *Device) secret(s string) {
	if d.Env != nil {
		d.Env.AddSecret(s)
	}
}

// NewRemoteDevice returns a device that talks to a server somewhere else (the manual-proof tool, the
// probe of ticket 205): no Env, no log to check.
func NewRemoteDevice(addr, pin, login string) *Device {
	return &Device{Addr: addr, Pin: pin, ID: wire.NewID(), Login: login, Name: "Test device", Product: "fms-windows"}
}
