package main

import (
	"crypto/tls"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"sync"
	"time"

	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/tlsid"
	"fms/exchange-server/internal/wire"
)

// stepError is the failure of one named step.
type stepError struct {
	step string
	err  error
}

func (e *stepError) Error() string { return e.step + ": " + e.err.Error() }

// prober is one run: the two or three devices it enrolls, and what it must undo.
type prober struct {
	server, pin, login, password string
	skip                         map[string]bool
	verbose                      bool
	log                          io.Writer

	mu      sync.Mutex
	cur     string
	passed  []string
	cleanup []func()
}

func (p *prober) current() string {
	p.mu.Lock()
	defer p.mu.Unlock()
	if p.cur == "" {
		return "connect"
	}
	return p.cur
}

// step runs one named step; its failure is the verdict.
func (p *prober) step(name string, f func() error) error {
	p.mu.Lock()
	p.cur = name
	p.mu.Unlock()
	start := time.Now()
	if err := f(); err != nil {
		return &stepError{step: name, err: err}
	}
	p.mu.Lock()
	p.passed = append(p.passed, name)
	p.mu.Unlock()
	if p.verbose {
		fmt.Fprintf(p.log, "ok  %-16s %v\n", name, time.Since(start).Round(time.Millisecond))
	}
	return nil
}

// learnPin connects once and returns the fingerprint of the certificate the server presents.
func learnPin(addr string) (string, error) {
	c, err := tls.DialWithDialer(&net.Dialer{Timeout: 5 * time.Second}, "tcp", addr,
		&tls.Config{InsecureSkipVerify: true, MinVersion: tls.VersionTLS12}) // reading the certificate to show it, nothing else
	if err != nil {
		return "", fmt.Errorf("cannot connect: %w", err)
	}
	defer c.Close()
	certs := c.ConnectionState().PeerCertificates
	if len(certs) == 0 {
		return "", errors.New("the server presented no certificate")
	}
	return tlsid.Fingerprint(certs[0].Raw), nil
}

func (p *prober) device(name string, roles []string) *testkit.Device {
	d := testkit.NewRemoteDevice(p.server, p.pin, p.login)
	d.Name, d.Password, d.Roles = name, p.password, roles
	return d
}

func has(features []string, f string) bool {
	for _, x := range features {
		if x == f {
			return true
		}
	}
	return false
}

func memberOf(raw json.RawMessage, key string) string {
	var m map[string]json.RawMessage
	if json.Unmarshal(raw, &m) != nil {
		return ""
	}
	var s string
	_ = json.Unmarshal(m[key], &s)
	return s
}

func hasMember(raw json.RawMessage, key string) bool {
	var m map[string]json.RawMessage
	_ = json.Unmarshal(raw, &m)
	_, ok := m[key]
	return ok
}

// awaitChanged waits for a `changed` push that satisfies pred.
func awaitChanged(c *testkit.Control, d time.Duration, pred func(*wire.Changed) bool) error {
	deadline := time.Now().Add(d)
	for {
		left := time.Until(deadline)
		if left <= 0 {
			return errors.New("no matching changed within the time")
		}
		e, err := c.Await(left, wire.TypeChanged)
		if err != nil {
			return err
		}
		if pred(e.(*wire.Changed)) {
			return nil
		}
	}
}

// run is the whole probe. It returns the first failing step, after undoing what it did.
func (p *prober) run() (err error) {
	defer func() {
		p.mu.Lock()
		fs := p.cleanup
		p.mu.Unlock()
		for i := len(fs) - 1; i >= 0; i-- {
			fs[i]()
		}
	}()
	later := func(f func()) {
		p.mu.Lock()
		p.cleanup = append(p.cleanup, f)
		p.mu.Unlock()
	}

	sshPW := "probe-" + wire.NewID()
	sshCfg := testkit.SSHServerConfig(sshPW)
	a := &testkit.Producer{
		Device:  p.device("exchange-probe A", []string{"resource-producer", "resource-consumer", "broadcaster", "receiver"}),
		Handler: func(c net.Conn) { testkit.ServeSSH(c, sshCfg) },
	}
	var features []string
	var resID, shareID string
	var enrolled []*testkit.Device

	// --- enroll: the account's password, on a TLS stream pinned to the server's identity ---
	if err := p.step("enroll", func() error {
		r, err := a.Enroll()
		if err != nil {
			return fmt.Errorf("cannot enroll: %w", err)
		}
		en, ok := r.(*wire.Enrolled)
		if !ok {
			return fmt.Errorf("the server did not enroll the device: %s", describe(r))
		}
		features = en.Features
		later(func() { a.Close() })
		// the stream that enrolled is authenticated; its first envelope fixes it as the control stream
		ctl := a.Control()
		ctl.OnOpen(a.Answer)
		if err := ctl.Send(&wire.Subscribe{}); err != nil {
			return err
		}
		if !has(features, "directory") || !has(features, "pairing") {
			return fmt.Errorf("welcome.features is %v: a server without directory and pairing is not this protocol", features)
		}
		return nil
	}); err != nil {
		return err
	}
	// whatever happens next, the devices this run enrolled are revoked at the end - A last, so that the
	// others are revoked through its still-open control stream (this runs before the cleanups above it)
	later(func() {
		ctl := a.Control()
		if ctl == nil {
			return
		}
		for _, d := range enrolled {
			_ = ctl.Send(&wire.Revoke{DeviceID: d.ID})
		}
		_, _ = ctl.Request(&wire.Keepalive{}, 3*time.Second, wire.TypeKeepalive) // the control stream is in order: a barrier
		_ = ctl.Send(&wire.Revoke{DeviceID: a.ID})
		_, _ = ctl.Request(&wire.Keepalive{}, time.Second, wire.TypeKeepalive)
	})

	// --- hello: a second stream of the enrolled device ---
	if err := p.step("hello", func() error {
		c, r, err := a.Hello()
		if err != nil {
			return err
		}
		if c == nil {
			return fmt.Errorf("hello was refused: %s", describe(r))
		}
		w := r.(*wire.Welcome)
		_ = c.Close()
		if w.Account == "" || w.KeepaliveSeconds < 10 {
			return fmt.Errorf("welcome is not what 7.2 says: %+v", w.WelcomeBody)
		}
		return nil
	}); err != nil {
		return err
	}

	// --- publish ---
	if err := p.step("publish", func() error {
		id, share, ans, err := a.PublishShare(true, nil)
		if err != nil {
			return err
		}
		if _, ok := ans.(*wire.Published); !ok {
			return fmt.Errorf("publish was not accepted: %s", describe(ans))
		}
		resID, shareID = id, share
		later(func() { _ = a.Control().Send(&wire.Unpublish{ResourceID: id}) })
		return nil
	}); err != nil {
		return err
	}

	// --- list: the resource is there, online, with its access; so is this device ---
	if err := p.step("list", func() error {
		dir, err := a.Control().ListDirectory()
		if err != nil {
			return err
		}
		var found json.RawMessage
		for _, raw := range dir.Resources {
			if memberOf(raw, "resourceId") == resID {
				found = raw
			}
		}
		if found == nil {
			return errors.New("the published resource is not in the directory")
		}
		if memberOf(found, "presence") != "online" || !hasMember(found, "access") {
			return fmt.Errorf("an enrolled device must read the resource online with its access: %s", found)
		}
		for _, raw := range dir.Devices {
			if memberOf(raw, "deviceId") == a.ID {
				return nil
			}
		}
		return errors.New("this device is not in the directory")
	}); err != nil {
		return err
	}

	// --- pairing: a code from A enrolls B once; the same code a second time is refused ---
	b := p.device("exchange-probe B", []string{"resource-consumer", "receiver"})
	var bc *testkit.Control
	if err := p.step("pairing", func() error {
		r, err := a.Control().Request(&wire.PairingRequest{}, 5*time.Second, wire.TypePairingCode, wire.TypeRefused)
		if err != nil {
			return err
		}
		pc, ok := r.(*wire.PairingCode)
		if !ok {
			return fmt.Errorf("pairing-request: %s", describe(r))
		}
		er, err := b.EnrollWithCode(pc.Code)
		if err != nil {
			return err
		}
		if _, ok := er.(*wire.Enrolled); !ok {
			return fmt.Errorf("enrolling with the code: %s", describe(er))
		}
		enrolled = append(enrolled, b)
		later(func() { b.Close() })
		again := p.device("exchange-probe B again", []string{"receiver"})
		r2, err := again.EnrollWithCode(pc.Code)
		if err != nil {
			return err
		}
		if f, ok := r2.(*wire.Refused); !ok || f.Reason != string(wire.ReasonBadCredentials) {
			if _, isEnrolled := r2.(*wire.Enrolled); isEnrolled {
				enrolled = append(enrolled, again)
			}
			return fmt.Errorf("a pairing code is single use, the second enroll got: %s", describe(r2))
		}
		return nil
	}); err != nil {
		return err
	}

	// --- subscribe: B sees what A publishes next, as a changed ---
	if err := p.step("subscribe", func() error {
		b.Close() // the enrolling stream is replaced by a hello + subscribe stream
		var err error
		if bc, err = b.Online(); err != nil {
			return err
		}
		// a list after the subscribe is the barrier: the control stream is processed in order, so once it is
		// answered the subscription is in force and the next change cannot be missed
		if _, err := bc.ListDirectory(); err != nil {
			return err
		}
		id2, _, ans, err := a.PublishShare(false, nil)
		if err != nil {
			return err
		}
		if _, ok := ans.(*wire.Published); !ok {
			return fmt.Errorf("second publish: %s", describe(ans))
		}
		later(func() { _ = a.Control().Send(&wire.Unpublish{ResourceID: id2}) })
		return awaitChanged(bc, 5*time.Second, func(c *wire.Changed) bool {
			for _, raw := range c.Upserts.Resources {
				if memberOf(raw, "resourceId") == id2 {
					return true
				}
			}
			return false
		})
	}); err != nil {
		return err
	}

	// --- tunnels ---
	if !p.skip["tunnel"] {
		if err := p.step("tunnel-resource", func() error {
			if !has(features, "tunnel") {
				return errors.New("the server does not offer tunnels (welcome.features has no tunnel: is its public endpoint set?)")
			}
			c, r, err := b.Connect(&wire.Connect{ResourceID: resID})
			if err != nil {
				return err
			}
			if c == nil {
				return fmt.Errorf("connect by resourceId: %s", describe(r))
			}
			defer c.Close()
			return sshEcho(c.Conn, sshPW)
		}); err != nil {
			return err
		}
		if err := p.step("tunnel-share", func() error {
			c, err := testkit.DialTLS(p.server, p.pin)
			if err != nil {
				return err
			}
			defer c.Close()
			if err := c.Send(&wire.Connect{ShareID: shareID}); err != nil {
				return err
			}
			r, err := c.Recv(8 * time.Second)
			if err != nil {
				return err
			}
			if _, ok := r.(*wire.Connected); !ok {
				return fmt.Errorf("connect by shareId: %s", describe(r))
			}
			return sshEcho(c.Conn, sshPW)
		}); err != nil {
			return err
		}
	}

	// --- relay: upload and listen ---
	var bid string
	var up *testkit.Conn
	if !p.skip["relay"] {
		const size = 64
		if err := p.step("relay-upload", func() error {
			if !has(features, "relay") {
				return errors.New("the server does not offer the relay (welcome.features has no relay: is its public endpoint set?)")
			}
			var err error
			if bid, err = a.StartBroadcast("AUDIO_ONLY", nil); err != nil {
				return err
			}
			later(func() { _ = a.EndBroadcast(bid) })
			var ans wire.Envelope
			if up, ans, err = a.Upload(bid, "audio/aac"); err != nil {
				return err
			}
			if up == nil {
				return fmt.Errorf("upload was not accepted: %s", describe(ans))
			}
			later(func() { _ = up.Close() })
			if err := up.WriteChunk(wire.ChunkHeader, []byte("PROBE-HEADER")); err != nil {
				return err
			}
			for i := uint32(1); i <= 3; i++ {
				if err := up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(i, size)); err != nil {
					return err
				}
			}
			return nil
		}); err != nil {
			return err
		}
		if err := p.step("relay-listen", func() error {
			// the upload may still be on its way to the ring: a listener told to retry retries
			var l *testkit.Listening
			var err error
			for try := 0; try < 20; try++ {
				if l, err = testkit.DialListen(p.server, p.pin, bid); err != nil {
					return err
				}
				if l.Resp.StatusCode != 503 {
					break
				}
				l.Close()
				time.Sleep(250 * time.Millisecond)
			}
			defer l.Close()
			r := l.Resp
			if r.StatusCode != 200 {
				return fmt.Errorf("listen answered %d, not 200", r.StatusCode)
			}
			if r.Header.Get("Content-Type") != "audio/aac" || r.Header.Get("Cache-Control") != "no-cache" ||
				r.Header.Get("Accept-Ranges") != "none" || r.Header.Get("Icy-Metaint") != "" ||
				len(r.TransferEncoding) != 1 || r.TransferEncoding[0] != "chunked" {
				return fmt.Errorf("the listen response is not the one of 7.6: %v %v", r.Header, r.TransferEncoding)
			}
			hdr, err := l.ReadFull(len("PROBE-HEADER"), 5*time.Second)
			if err != nil || string(hdr) != "PROBE-HEADER" {
				return fmt.Errorf("the body must start with the header chunk: %q %v", hdr, err)
			}
			first, err := l.ReadSeq(size, 5*time.Second)
			if err != nil || first < 1 || first > 3 {
				return fmt.Errorf("then it starts at a joinable chunk: %d %v", first, err)
			}
			// live delivery: chunks uploaded now arrive, in order, without a gap
			for i := uint32(4); i <= 6; i++ {
				if err := up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(i, size)); err != nil {
					return err
				}
			}
			want := first + 1
			for want <= 6 {
				got, err := l.ReadSeq(size, 5*time.Second)
				if err != nil {
					return fmt.Errorf("waiting for chunk %d: %w", want, err)
				}
				if got != want {
					return fmt.Errorf("chunk %d arrived where %d was expected", got, want)
				}
				want++
			}
			return nil
		}); err != nil {
			return err
		}
	}

	// --- cast: A offers its broadcast to B ---
	if !p.skip["cast"] {
		if err := p.step("cast", func() error {
			if !has(features, "cast") {
				return errors.New("the server does not offer cast (welcome.features has no cast)")
			}
			if bid == "" { // the relay part was skipped: a broadcast is still needed
				var err error
				if bid, err = a.StartBroadcast("AUDIO_ONLY", nil); err != nil {
					return err
				}
				later(func() { _ = a.EndBroadcast(bid) })
			}
			if err := a.Control().Send(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: b.ID}); err != nil {
				return err
			}
			e, err := bc.Await(5*time.Second, wire.TypeCastOffer)
			if err != nil {
				return fmt.Errorf("the receiver never got the offer: %w", err)
			}
			offer := e.(*wire.CastOffer)
			if offer.FromDeviceID != a.ID || memberOf(offer.Broadcast, "broadcastId") != bid || hasMember(offer.Broadcast, "access") {
				return fmt.Errorf("the offer must name the caster and the broadcast and never carry access: %s", offer.Broadcast)
			}
			if err := bc.Send(&wire.CastAnswer{BroadcastID: bid, Accepted: true}); err != nil {
				return err
			}
			r, err := a.Control().Await(5*time.Second, wire.TypeCastResult)
			if err != nil {
				return fmt.Errorf("the caster never got the result: %w", err)
			}
			if res := r.(*wire.CastResult); !res.Accepted || res.ReceiverDeviceID != b.ID || res.BroadcastID != bid {
				return fmt.Errorf("the result is not the receiver's acceptance: %+v", res)
			}
			if err := a.Control().Send(&wire.CastStop{BroadcastID: bid, ReceiverDeviceID: b.ID}); err != nil {
				return err
			}
			if _, err := bc.Await(5*time.Second, wire.TypeCastStop); err != nil {
				return fmt.Errorf("the stop was not forwarded: %w", err)
			}
			return nil
		}); err != nil {
			return err
		}
	}

	// --- revoke: a third device is enrolled, revoked by A, and its token is a revoked one ---
	if err := p.step("revoke", func() error {
		c := p.device("exchange-probe C", []string{"resource-consumer"})
		r, err := c.Enroll()
		if err != nil {
			return err
		}
		if _, ok := r.(*wire.Enrolled); !ok {
			return fmt.Errorf("enrolling the third device: %s", describe(r))
		}
		enrolled = append(enrolled, c)
		c.Close()
		cc, err := c.Online()
		if err != nil {
			return err
		}
		if err := a.Control().Send(&wire.Revoke{DeviceID: c.ID}); err != nil {
			return err
		}
		select {
		case <-cc.Closed():
		case <-time.After(5 * time.Second):
			return errors.New("the revoked device's control stream was not closed")
		}
		conn, r2, err := c.Hello()
		if conn != nil {
			conn.Close()
		}
		if err != nil {
			return err
		}
		if f, ok := r2.(*wire.Refused); !ok || f.Reason != string(wire.ReasonDeviceRevoked) {
			return fmt.Errorf("hello with a revoked token must be device-revoked, got %s", describe(r2))
		}
		return nil
	}); err != nil {
		return err
	}

	return nil
}

// sshEcho runs one SSH command through an open tunnel and requires the producer's echo.
func sshEcho(c net.Conn, pw string) error {
	out, err := testkit.SSHThrough(c, pw, "probe")
	if err != nil {
		return fmt.Errorf("the SSH session through the tunnel failed: %w", err)
	}
	if out != "echo:probe" {
		return fmt.Errorf("the producer's answer through the tunnel was %q", out)
	}
	return nil
}

// describe says what a server answered, for a failure line: a refusal by its reason, anything else by type.
func describe(e wire.Envelope) string {
	switch v := e.(type) {
	case nil:
		return "nothing"
	case *wire.Refused:
		return "refused " + v.Reason
	}
	return e.TypeName()
}
