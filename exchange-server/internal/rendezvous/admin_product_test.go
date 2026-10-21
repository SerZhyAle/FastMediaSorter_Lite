package rendezvous_test

import (
	"encoding/binary"
	"encoding/json"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/admin"
	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/rendezvous"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Ticket 244 (devex-delta G4): nothing on the remote administration wire carries the operator's product
// (`admin-auth` has only the password, `admin-request` no product member, an operator is no enrolled device),
// so the ring and the file audit are anonymous about it on that path: every remote operator is
// admin.ClientProductRemote, and a product a client smuggles in as an extra member is never believed.

// frameWithMember is the frame of e with one more JSON member in its body: a client that claims something.
func frameWithMember(t *testing.T, e wire.Envelope, name, value string) []byte {
	t.Helper()
	f, err := wire.EncodeFrame(e, wire.MaxFrameAuth)
	if err != nil {
		t.Fatal(err)
	}
	var m map[string]any
	if err := json.Unmarshal(f[4:], &m); err != nil {
		t.Fatal(err)
	}
	m[name] = value
	body, err := json.Marshal(m)
	if err != nil {
		t.Fatal(err)
	}
	out := make([]byte, 4+len(body))
	binary.BigEndian.PutUint32(out, uint32(len(body)))
	copy(out[4:], body)
	return out
}

func TestRemoteAdministrationIsAnonymousAboutTheProductInTheRingAndTheFileAudit(t *testing.T) {
	hash, err := password.HashWith("SecretAdmin1", testkit.Cheap())
	if err != nil {
		t.Fatal(err)
	}
	for _, via := range []string{"TLS-stream", "WebSocket, own origin", "WebSocket, no Origin"} {
		t.Run(via, func(t *testing.T) {
			var capture auditCapture
			env, ops := setupAdminEnv(t, config.Admin{On: true, PasswordRecord: hash}, func(c *rendezvous.Config) {
				c.Ops.Audit = admin.NewAuditTracker()
				c.Ops.AuditLog = logx.NewAudit(&capture)
			})
			const claim = "FMS_A"
			add := &wire.AdminRequest{ID: 1, Op: "account.add", Login: "carol", Password: "CarolPass123"}

			var closeStream func()
			switch via {
			case "TLS-stream":
				c := env.tls(t)
				if err := c.SendRaw(frameWithMember(t, &wire.AdminAuth{Password: "SecretAdmin1"}, "product", claim)); err != nil {
					t.Fatal(err)
				}
				reply, err := c.Recv(2 * time.Second)
				if err != nil {
					t.Fatal(err)
				}
				ok, isOK := reply.(*wire.AdminAuthOK)
				if !isOK {
					t.Fatalf("expected admin-auth-ok: %+v", reply)
				}
				c.Authenticated()
				add.Token = ok.Token
				if err := c.SendRaw(frameWithMember(t, add, "product", claim)); err != nil {
					t.Fatal(err)
				}
				if rep, err := c.Recv(2 * time.Second); err != nil {
					t.Fatal(err)
				} else if r, isReply := rep.(*wire.AdminReply); !isReply || !r.OK {
					t.Fatalf("account.add: %+v", rep)
				}
				closeStream = func() { _ = c.Close() }
			default:
				origin := ""
				if via == "WebSocket, own origin" {
					origin = "https://" + env.addr
				}
				c, status := wsOpen(t, env.addr, env.addr, origin)
				if status != 101 {
					t.Fatalf("the opening answered %d", status)
				}
				c.sendBytes(frameWithMember(t, &wire.AdminAuth{Password: "SecretAdmin1"}, "product", claim))
				reply, err := c.recvEnvelope(5 * time.Second)
				if err != nil {
					t.Fatal(err)
				}
				ok, isOK := reply.(*wire.AdminAuthOK)
				if !isOK {
					t.Fatalf("expected admin-auth-ok: %+v", reply)
				}
				add.Token = ok.Token
				c.sendBytes(frameWithMember(t, add, "product", claim))
				if rep, err := c.recvEnvelope(5 * time.Second); err != nil {
					t.Fatal(err)
				} else if r, isReply := rep.(*wire.AdminReply); !isReply || !r.OK {
					t.Fatalf("account.add: %+v", rep)
				}
				closeStream = func() { _ = c.conn.Close() }
			}

			// the ring: one active session and the change, both "remote", never the claimed product
			view := ops.Audit.View()
			if len(view.ActiveSessions) != 1 || view.ActiveSessions[0].ClientProduct != admin.ClientProductRemote || view.ActiveSessions[0].RemoteAddr == "" {
				t.Fatalf("the session list: %+v", view.ActiveSessions)
			}
			var changes int
			for _, r := range view.Records {
				if r.Op == "account.add" {
					changes++
					if r.ClientProduct != admin.ClientProductRemote || r.Target != "carol" {
						t.Fatalf("the ring record: %+v", r)
					}
				}
			}
			if changes != 1 {
				t.Fatalf("expected one account.add in the ring: %+v", view.Records)
			}

			// the file audit: the same word in the product slot of every administration line, none unknown
			closeStream()
			eventually(t, "the disconnect line", func() bool { return strings.Contains(capture.String(), "OP=admin.disconnect") })
			lines := 0
			for _, line := range strings.Split(capture.String(), "\n") {
				if !strings.Contains(line, "OP=admin.") && !strings.Contains(line, "OP=account.add") {
					continue
				}
				lines++
				if !strings.Contains(line, "["+admin.ClientProductRemote+" @ ") {
					t.Fatalf("an administration line without the remote product: %q", line)
				}
			}
			if lines < 3 { // admin.auth, account.add, admin.disconnect
				t.Fatalf("expected the administration lines, got %d in %q", lines, capture.String())
			}
			if strings.Contains(capture.String(), claim) {
				t.Fatalf("a claimed product reached the audit: %q", capture.String())
			}
			if !strings.Contains(capture.String(), strings.Split(via, ",")[0]) {
				t.Fatalf("the transport %q is not named in %q", via, capture.String())
			}
		})
	}
}
