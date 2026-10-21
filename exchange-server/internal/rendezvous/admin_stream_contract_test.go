package rendezvous_test

import (
	"bufio"
	"crypto/tls"
	"net/http"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/admin"
	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/rendezvous"
	"fms/exchange-server/internal/wire"
)

// These tests hold two rules of the operator wire that the audit of 2026-10-20 found open: which origin may
// open an administration stream (WEB-BINDING operator-administration and normative resolution 2; gap G-5)
// and which operations the remote wire carries (WEB-BINDING "Which operations", REMOTE-ADMIN; gap G-14).

// wsOpen opens /v2/ws with an explicit Host and Origin (origin == "" sends no Origin header at all) and
// returns the client and the opening's status. The client is nil unless the status is 101.
func wsOpen(t *testing.T, addr, host, origin string) (*wsClient, int) {
	t.Helper()
	conn, err := tls.Dial("tcp", addr, &tls.Config{InsecureSkipVerify: true})
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = conn.Close() })
	req := "GET /v2/ws HTTP/1.1\r\nHost: " + host + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
		"Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n" +
		"Sec-WebSocket-Protocol: fms-exchange-v2\r\n"
	if origin != "" {
		req += "Origin: " + origin + "\r\n"
	}
	if _, err := conn.Write([]byte(req + "\r\n")); err != nil {
		t.Fatal(err)
	}
	br := bufio.NewReader(conn)
	resp, err := http.ReadResponse(br, nil)
	if err != nil {
		t.Fatal(err)
	}
	if resp.StatusCode != 101 {
		return nil, resp.StatusCode
	}
	return &wsClient{t: t, conn: conn, br: br}, 101
}

// requireClosedWithoutAnAnswer reads until the end of the stream and fails on any data message: a closed
// stream may carry the transport's own Close frame, but never an envelope.
func (c *wsClient) requireClosedWithoutAnAnswer() {
	c.t.Helper()
	deadline := time.Now().Add(5 * time.Second)
	for time.Now().Before(deadline) {
		op, p, err := c.frame(2 * time.Second)
		if err != nil {
			if isClosed(err) {
				return
			}
			continue
		}
		switch op {
		case 0x1, 0x2:
			c.t.Fatalf("the stream answered a refused origin with a data message (%d bytes): %q", len(p), p)
		case 0x8:
			c.write(wsMasked(0x88, p))
		}
	}
	c.t.Fatal("the stream was not closed")
}

func TestOperatorStreamOverTheWebSocketNeedsTheServersOwnOriginOrNone(t *testing.T) {
	hash, err := password.HashWith("SecretAdmin1", password.Params{Time: 1, Memory: 64, Threads: 1, KeyLen: 32})
	if err != nil {
		t.Fatal(err)
	}
	var capture auditCapture
	env, _ := setupAdminEnv(t, config.Admin{On: true, PasswordRecord: hash, Origins: []string{"https://app.example"}},
		func(c *rendezvous.Config) { c.Ops.AuditLog = logx.NewAudit(&capture) })

	// the server's own origin (the page it serves) and a native client with no Origin are operators
	for name, origin := range map[string]string{"same origin": "https://" + env.addr, "no Origin": ""} {
		t.Run(name, func(t *testing.T) {
			c, status := wsOpen(t, env.addr, env.addr, origin)
			if status != 101 {
				t.Fatalf("the opening answered %d, want 101", status)
			}
			c.write(wsMasked(0x82, wsAuthFrame(t)))
			c.requireAuthOK()
		})
	}

	// an origin that is only in the allowlist is a consumer's: the stream is closed, nothing is answered
	for _, origin := range []string{"https://app.example", "https://app.example:443", "https://APP.example"} {
		t.Run("listed "+origin, func(t *testing.T) {
			c, status := wsOpen(t, env.addr, env.addr, origin)
			if status != 101 {
				t.Fatalf("the opening of a listed origin answered %d, want 101 (a consumer may use it)", status)
			}
			c.write(wsMasked(0x82, wsAuthFrame(t)))
			c.requireClosedWithoutAnAnswer()
		})
	}
	eventually(t, "origin-not-same audit", func() bool { return strings.Contains(capture.String(), "REFUSED:origin-not-same") })
	if strings.Contains(capture.String(), "SecretAdmin1") || strings.Contains(capture.String(), hash) {
		t.Fatal("audit leaked credentials")
	}

	// an origin that is not listed and not the server's own is refused at the opening
	for _, origin := range []string{"https://evil.example", "null", "http://" + env.addr, "https://" + env.addr + "/x", "https://u@" + env.addr, "https://app.example."} {
		t.Run("refused "+origin, func(t *testing.T) {
			if c, status := wsOpen(t, env.addr, env.addr, origin); c != nil || status != 403 {
				t.Fatalf("the opening answered %d, want 403", status)
			}
		})
	}

	// the refusals above did not disturb a real operator
	c, status := wsOpen(t, env.addr, env.addr, "")
	if status != 101 {
		t.Fatalf("the opening answered %d, want 101", status)
	}
	c.write(wsMasked(0x82, wsAuthFrame(t)))
	c.requireAuthOK()
}

// remoteOps is the whole of what WEB-BINDING "Which operations" lists; nothing else is carried.
var remoteOps = []string{
	"account.add", "account.passwd", "account.remove", "account.list", "account.kind", "account.limits",
	"device.list", "device.revoke", "device.pairing-code",
	"invitation.create", "invitation.list", "invitation.withdraw",
	"shared_invitation.status", "shared_invitation.set",
	"policy.status", "policy.set", "grant.list", "status",
}

// notRemote are the operations that stay on the local administration channel or are not carried until they
// are audited: the visibility operations of the Share Manager, host.settings (never on the wire) and the grants.
var notRemote = []string{
	admin.OpBroadcastList, admin.OpBroadcastEnd, admin.OpResourceList, admin.OpResourceUnpublish,
	admin.OpClipboardInfo, admin.OpClipboardErase, admin.OpAdminAuditList,
	"host.settings", "grant.add", "grant.remove", "broadcast.start", "no.such.op", "STATUS", "status ",
}

func TestRemoteAdministrationCarriesExactlyTheContractOperations(t *testing.T) {
	hash, err := password.HashWith("SecretAdmin1", password.Params{Time: 1, Memory: 64, Threads: 1, KeyLen: 32})
	if err != nil {
		t.Fatal(err)
	}
	env, ops := setupAdminEnv(t, config.Admin{On: true, PasswordRecord: hash})
	if _, err := ops.AddAccount("alice", "AlicePass123"); err != nil {
		t.Fatal(err)
	}
	c := env.tls(t)
	if err := c.Send(&wire.AdminAuth{Password: "SecretAdmin1"}); err != nil {
		t.Fatal(err)
	}
	r, err := c.Recv(3 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	authOK, ok := r.(*wire.AdminAuthOK)
	if !ok {
		t.Fatalf("expected admin-auth-ok, got %+v", r)
	}
	c.Authenticated()

	ask := func(id int64, op string) *wire.AdminReply {
		t.Helper()
		if err := c.Send(&wire.AdminRequest{ID: id, Token: authOK.Token, Op: op, Login: "alice", TargetID: "x", DeviceID: "x"}); err != nil {
			t.Fatal(err)
		}
		r, err := c.Recv(3 * time.Second)
		if err != nil {
			t.Fatalf("%q: %v", op, err)
		}
		rep, ok := r.(*wire.AdminReply)
		if !ok || rep.ID != id {
			t.Fatalf("%q: expected the reply of request %d, got %+v", op, id, r)
		}
		return rep
	}

	// every one that is not carried is answered bad-request, the stream stays open, nothing is executed
	var id int64 = 100
	for _, op := range notRemote {
		id++
		rep := ask(id, op)
		if rep.OK || rep.Code != admin.CodeBadRequest || rep.Error != "uncarried or unknown operation" || len(rep.Data) != 0 {
			t.Errorf("%q over the remote wire: %+v, want bad-request and no data", op, rep)
		}
	}
	// ... and every one that is carried gets past the carriage check (its own parameter errors are another thing)
	for _, op := range remoteOps {
		id++
		if rep := ask(id, op); rep.Error == "uncarried or unknown operation" {
			t.Errorf("%q is a contract operation but the remote wire refused to carry it", op)
		}
	}
	// the operations that need nothing but the session answer well
	for _, op := range []string{"status", "account.list", "policy.status", "grant.list", "invitation.list", "shared_invitation.status"} {
		id++
		if rep := ask(id, op); !rep.OK {
			t.Errorf("%q: %+v, want ok", op, rep)
		}
	}

	// the same operations are still there on the local channel (internal/admin, the Share Manager's pipe)
	for _, op := range []string{admin.OpBroadcastList, admin.OpResourceList} {
		if resp := ops.Handle(admin.Request{Op: op, Login: "alice"}); !resp.OK {
			t.Errorf("%q on the local channel: %+v, want ok", op, resp)
		}
	}
}
