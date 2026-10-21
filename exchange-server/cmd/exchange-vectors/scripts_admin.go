package main

import (
	"bufio"
	"bytes"
	"encoding/binary"
	"encoding/json"
	"fmt"
	"io"
	"net"
	"strings"
	"time"

	"fms/exchange-server/internal/admin"
	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// scenarioAdminNative: remote administration on the native TLS length-prefixed envelope stream (0x00).
// Amends 7.1 and REMOTE-ADMIN.md. Covers off-by-default, address allowlist exclusion, wrong password,
// rate-limited (per-address pacer), capacity (verifier bound), lapsed token, and all 18 carried operations.
func scenarioAdminNative() *transcript { return scenarioAdmin(false) }

func scenarioAdminWebSocket() *transcript { return scenarioAdmin(true) }

func scenarioAdmin(websocket bool) *transcript {
	adminPassword := "AdminSecretPass1"
	adminHash, err := password.HashWith(adminPassword, testkit.Cheap())
	if err != nil {
		failf("admin hash: %v", err)
	}

	adminCfg := config.Admin{
		On:                     false,
		PasswordRecord:         adminHash,
		AllowedAddresses:       []string{"127.0.0.1"},
		SessionLifetimeMinutes: 15,
		PasswordFloor:          10,
	}

	name := "admin-native"
	if websocket {
		name = "admin-websocket"
	}
	r := newRig(name, rigOpts{
		admin:      &adminCfg,
		pace:       true,
		verifyConc: 1,
		verifyWait: 10 * time.Millisecond,
		logins:     []string{account},
	})
	defer mustClose(r)

	tr := r.newTranscript(name,
		"Remote administration over TLS, using native envelopes or WebSocket binary messages as named by the transcript (DEVICE-EXCHANGE 1.4, REMOTE-ADMIN.md). "+
			"Administration is off by default: admin-auth is answered admin-disabled. When turned on, clients outside allowedAddresses "+
			"are closed without answer. A wrong password is bad-credentials; rapid attempts from the same client address are rate-limited "+
			"by the per-address pacer; when the password verifier is saturated the answer is capacity. An authenticated session token "+
			"expires after the configured lifetime (default 15 minutes) or stream close. Carries 18 operations and never host OS settings.")

	open := func(tr *transcript, id, transport, purpose string) *sess {
		if websocket {
			return openAdminWebSocket(r, tr, id, purpose)
		}
		return r.open(tr, id, transport, purpose)
	}
	if websocket {
		tr.note("All following envelopes use binary WebSocket messages after the HTTP upgrade.")
	}

	// 1. Off by default
	s1 := open(tr, "admin-disabled", "tls", "admin-auth when web administration is off")
	s1.send(&wire.AdminAuth{Password: adminPassword})
	s1.expectRefused(wire.ReasonAdminDisabled)
	s1.expectClosed()
	tr.note("web administration is off by default: admin-auth is refused with admin-disabled")

	// 2. Allowed addresses check: exclude loopback
	r.env.Server.SetAdminConfig(config.Admin{
		On:                     true,
		PasswordRecord:         adminHash,
		AllowedAddresses:       []string{"192.0.2.1"},
		SessionLifetimeMinutes: 15,
		PasswordFloor:          10,
	})
	s2 := open(tr, "admin-excluded", "tls", "admin-auth from an address not in allowedAddresses")
	s2.send(&wire.AdminAuth{Password: adminPassword})
	s2.expectClosed()
	tr.note("the client address is not in allowedAddresses: the server closes the stream with no answer")

	// 3. Turn on for 127.0.0.1
	r.env.Server.SetAdminConfig(config.Admin{
		On:                     true,
		PasswordRecord:         adminHash,
		AllowedAddresses:       []string{"127.0.0.1"},
		SessionLifetimeMinutes: 15,
		PasswordFloor:          10,
	})

	// 4. Wrong password
	s3 := open(tr, "admin-wrong", "tls", "admin-auth with the wrong password: evaluated at once")
	s3.send(&wire.AdminAuth{Password: "WrongPassword99!"})
	s3.expectRefused(wire.ReasonBadCredentials)
	s3.expectClosed()
	tr.note("wrong password: refused with bad-credentials")

	// 5. Second check right behind it waits for its slot, then evaluated
	s4 := open(tr, "admin-waiting-slot", "tls", "a second check right behind it waits for its slot, then evaluated")
	s4.send(&wire.AdminAuth{Password: "AnotherWrongPass!"})
	s4.expectRefused(wire.ReasonBadCredentials)
	s4.expectClosed()
	tr.note("second check inside window: waits for its slot, then evaluated")

	// 6. Third check inside window: turned away without evaluating
	s5 := open(tr, "admin-rate-limited", "tls", "a third check inside the window is turned away without evaluating")
	s5.send(&wire.AdminAuth{Password: "ThirdWrongPass!"})
	s5.expectRefused(wire.ReasonRateLimited)
	s5.expectClosed()
	tr.note("pace is per client address, not per login: a third check inside the window is refused with rate-limited before evaluating")

	// Wait out pace interval
	r.clock.Add(3 * time.Second)

	// 7. Capacity: password verifier queue saturated
	release := r.env.Verifier.Hold()
	sCap := open(tr, "admin-capacity", "tls", "admin-auth while the verifier queue is full")
	sCap.send(&wire.AdminAuth{Password: adminPassword})
	sCap.expectRefused(wire.ReasonCapacity)
	sCap.expectClosed()
	release()
	tr.note("password verifier bound saturated: refused with capacity")

	r.clock.Add(3 * time.Second)

	// 8. Lapsed session token
	sLapsed := open(tr, "admin-lapsed", "tls", "session inactivity expires after session lifetime")
	sLapsed.send(&wire.AdminAuth{Password: adminPassword})
	okLapsed := sLapsed.expect(wire.TypeAdminAuthOK).(*wire.AdminAuthOK)
	sLapsed.auth()
	r.clock.Add(16 * time.Minute)
	sLapsed.send(&wire.AdminRequest{ID: 1, Token: okLapsed.Token, Op: admin.OpStatus})
	sLapsed.expectRefused(wire.ReasonBadCredentials)
	sLapsed.expectClosed()
	tr.note("session lifetime of inactivity elapsed: token lapsed, request refused with bad-credentials and stream closed")

	r.clock.Add(3 * time.Second)

	// 8. Successful administration session with all 18 carried operations
	s := open(tr, "admin-ops", "tls", "authenticated administration stream exercising all carried operations")
	s.send(&wire.AdminAuth{Password: adminPassword})
	okResp := s.expect(wire.TypeAdminAuthOK).(*wire.AdminAuthOK)
	token := okResp.Token
	s.auth()
	tr.note("admin-auth succeeded: token issued, frame bound raised to 262144 bytes")

	var reqID int64
	op := func(action string, req *wire.AdminRequest, note string) *wire.AdminReply {
		reqID++
		req.ID = reqID
		req.Token = token
		req.Op = action
		s.send(req)
		rep := s.expect(wire.TypeAdminReply).(*wire.AdminReply)
		if !rep.OK {
			failf("%s failed: code=%s error=%s", action, rep.Code, rep.Error)
		}
		if note != "" {
			tr.note(note)
		}
		return rep
	}

	// 1) status
	op(admin.OpStatus, &wire.AdminRequest{}, "status: server counts and host policy flags")

	// 2) policy.status
	op(admin.OpPolicyStatus, &wire.AdminRequest{}, "policy.status: account cap and default limits")

	// 3) policy.set
	newMax := 8
	op(admin.OpPolicySet, &wire.AdminRequest{MaxAccounts: &newMax}, "policy.set: update account cap")

	// 4) account.list
	op(admin.OpAccountList, &wire.AdminRequest{}, "account.list: all accounts with own and effective limits")

	// 5) account.add
	op(admin.OpAccountAdd, &wire.AdminRequest{
		Login:    "bob",
		Password: "BobPassword123!",
		Kind:     "member",
	}, "account.add: create new member account")

	// 6) account.limits
	limDev := 4
	op(admin.OpAccountLimits, &wire.AdminRequest{
		Login: "bob",
		Limits: &wire.AdminLimits{
			DevicesPerAccount: &limDev,
		},
	}, "account.limits: set per-account limit override")

	// 7) account.passwd
	op(admin.OpAccountPasswd, &wire.AdminRequest{
		Login:    "bob",
		Password: "NewBobPassword123!",
	}, "account.passwd: change account password and sign out devices")

	// 8) account.kind
	op(admin.OpAccountKind, &wire.AdminRequest{
		Login: "bob",
		Kind:  "guest",
	}, "account.kind: switch account between member and guest")

	// 9) invitation.create
	repInv := op(admin.OpInvitationCreate, &wire.AdminRequest{
		Kind: "member",
	}, "invitation.create: creates one-time invitation and returns code and connectionString")

	var invData struct {
		Code             string               `json:"code"`
		Invitation       admin.InvitationView `json:"invitation"`
		ConnectionString string               `json:"connectionString"`
	}
	if err := json.Unmarshal(repInv.Data, &invData); err != nil || invData.Code == "" || invData.ConnectionString == "" {
		failf("invalid invitation.create reply data: %s", repInv.Data)
	}

	// 10) invitation.list
	op(admin.OpInvitationList, &wire.AdminRequest{}, "invitation.list: list pending invitations with hash and kind")

	// 11) invitation.withdraw
	op(admin.OpInvitationWithdraw, &wire.AdminRequest{
		CodeHash: invData.Invitation.CodeHash,
	}, "invitation.withdraw: withdraw unused invitation by codeHash prefix")

	// 12) shared_invitation.status
	op(admin.OpSharedInvitationStatus, &wire.AdminRequest{}, "shared_invitation.status: check shared invitation state")

	// 13) shared_invitation.set
	op(admin.OpSharedInvitationSet, &wire.AdminRequest{
		Password: "SharedPassw0rdTest!",
		Kind:     "guest",
	}, "shared_invitation.set: set reusable shared invitation password and kind")

	// 14) device.list
	op(admin.OpDeviceList, &wire.AdminRequest{
		Login: account,
	}, "device.list: list enrolled devices of account without secrets")

	// 15) device.pairing-code
	op(admin.OpDevicePairingCode, &wire.AdminRequest{
		Login: account,
	}, "device.pairing-code: single-use 10-minute pairing code for local enrollment")

	// 16) device.revoke
	reqID++
	s.send(&wire.AdminRequest{
		ID:       reqID,
		Token:    token,
		Op:       admin.OpDeviceRevoke,
		Login:    account,
		DeviceID: "non-existent-device-id",
	})
	repRev := s.expect(wire.TypeAdminReply).(*wire.AdminReply)
	if repRev.OK || repRev.Code != admin.CodeNotFound {
		failf("expected device.revoke not-found, got %+v", repRev)
	}
	tr.note("device.revoke: not-found when the account has no such device")

	// 17) grant.list
	op(admin.OpGrantList, &wire.AdminRequest{
		Login: account,
	}, "grant.list: list active resource and broadcast grants")

	// 18) account.remove
	op(admin.OpAccountRemove, &wire.AdminRequest{
		Login: "bob",
	}, "account.remove: delete account and clean up resources and directory")

	// End stream
	s.send(&wire.Bye{})
	s.expectClosed()
	tr.note("bye closes the administration stream cleanly")

	return tr
}

// wsClientConn implements client-side RFC 6455 binary frame masking and parsing over net.Conn.
type wsClientConn struct {
	net.Conn
	reader *bufio.Reader
	buf    []byte
}

func newWSClientConn(c net.Conn) *wsClientConn {
	return &wsClientConn{Conn: c, reader: bufio.NewReader(c)}
}

func (w *wsClientConn) Write(p []byte) (int, error) {
	n := len(p)
	var header []byte
	header = append(header, 0x82) // FIN + binary opcode
	switch {
	case n <= 125:
		header = append(header, byte(0x80|n))
	case n <= 65535:
		header = append(header, 0x80|126, byte(n>>8), byte(n))
	default:
		var lenBuf [8]byte
		binary.BigEndian.PutUint64(lenBuf[:], uint64(n))
		header = append(header, 0x80|127)
		header = append(header, lenBuf[:]...)
	}
	// Deterministic client masking key for vector reproducibility
	mask := []byte{0x12, 0x34, 0x56, 0x78}
	header = append(header, mask...)
	masked := make([]byte, n)
	for i := 0; i < n; i++ {
		masked[i] = p[i] ^ mask[i%4]
	}
	if _, err := w.Conn.Write(append(header, masked...)); err != nil {
		return 0, err
	}
	return n, nil
}

func (w *wsClientConn) Read(p []byte) (int, error) {
	for len(w.buf) == 0 {
		b0, err := w.reader.ReadByte()
		if err != nil {
			return 0, err
		}
		opcode := b0 & 0x0f
		if opcode == 0x08 { // close frame
			return 0, io.EOF
		}
		b1, err := w.reader.ReadByte()
		if err != nil {
			return 0, err
		}
		hasMask := (b1 & 0x80) != 0
		len1 := int(b1 & 0x7f)
		var payloadLen int
		switch len1 {
		case 126:
			var buf [2]byte
			if _, err := io.ReadFull(w.reader, buf[:]); err != nil {
				return 0, err
			}
			payloadLen = int(binary.BigEndian.Uint16(buf[:]))
		case 127:
			var buf [8]byte
			if _, err := io.ReadFull(w.reader, buf[:]); err != nil {
				return 0, err
			}
			payloadLen = int(binary.BigEndian.Uint64(buf[:]))
		default:
			payloadLen = len1
		}
		var mask [4]byte
		if hasMask {
			if _, err := io.ReadFull(w.reader, mask[:]); err != nil {
				return 0, err
			}
		}
		data := make([]byte, payloadLen)
		if _, err := io.ReadFull(w.reader, data); err != nil {
			return 0, err
		}
		if hasMask {
			for i := 0; i < payloadLen; i++ {
				data[i] ^= mask[i%4]
			}
		}
		w.buf = data
	}
	n := copy(p, w.buf)
	w.buf = w.buf[n:]
	return n, nil
}

// openAdminWebSocket opens a real WebSocket stream with the same transcript tap as native TLS.
func openAdminWebSocket(r *rig, tr *transcript, streamID, purpose string) *sess {
	// Dial TLS
	c, err := testkit.DialTLS(r.env.Addr, r.env.Fingerprint)
	if err != nil {
		failf("dial tls: %v", err)
	}
	tr.mu.Lock()
	tr.Streams = append(tr.Streams, streamInfo{ID: streamID, Transport: "tls", Purpose: purpose})
	tr.mu.Unlock()

	// 1. Send WebSocket upgrade request
	reqStr := fmt.Sprintf("GET /v2/ws HTTP/1.1\r\n"+
		"Host: %s\r\n"+
		"Upgrade: websocket\r\n"+
		"Connection: Upgrade\r\n"+
		"Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n"+
		"Sec-WebSocket-Version: 13\r\n"+
		"Sec-WebSocket-Protocol: fms-exchange-v2\r\n"+
		"Origin: https://%s\r\n\r\n", publicEndpoint, publicEndpoint)

	if _, err := io.WriteString(c, reqStr); err != nil {
		failf("write ws upgrade: %v", err)
	}

	// Read HTTP 101 response head
	var rawResp bytes.Buffer
	buf := make([]byte, 1024)
	for !strings.Contains(rawResp.String(), "\r\n\r\n") {
		n, err := c.Read(buf)
		if err != nil {
			failf("read ws upgrade response: %v", err)
		}
		rawResp.Write(buf[:n])
	}
	respBytes := rawResp.Bytes()
	headEnd := bytes.Index(respBytes, []byte("\r\n\r\n")) + 4
	headBytes := respBytes[:headEnd]
	hs := parseHTTPResponse(reqStr, headBytes)
	if hs.Status != 101 {
		failf("expected 101 Switching Protocols, got %d", hs.Status)
	}
	tr.add(step{Stream: streamID, Direction: c2s + " / " + s2c, HTTP: hs, Note: "HTTP upgrade to WebSocket (/v2/ws) with subprotocol fms-exchange-v2 and Origin validation"})

	// Wrap connection in wsClientConn, preloading any leftover bytes after the HTTP header
	ws := newWSClientConn(c.Conn)
	if len(respBytes) > headEnd {
		ws.reader = bufio.NewReader(io.MultiReader(bytes.NewReader(respBytes[headEnd:]), c.Conn))
	}

	tap := &tapConn{Conn: ws, tr: tr, stream: streamID, parsing: true}
	s := &sess{r: r, tr: tr, id: streamID, c: testkit.NewConn(tap), tap: tap}
	r.mu.Lock()
	r.sessions = append(r.sessions, s)
	r.mu.Unlock()

	return s
}
