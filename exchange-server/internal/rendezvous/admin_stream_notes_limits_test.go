package rendezvous_test

import (
	"encoding/json"
	"fmt"
	"testing"
	"time"

	"fms/exchange-server/internal/admin"
	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Items FY and FZ of DEVICE-EXCHANGE 1.17 (ticket L0322): the remote administration wire carries the
// notes count and the notes byte limit. `account.limits` and `policy.set` accept both members, zero is
// a value and not "unset", a negative value is answered bad-limit by the operation, and a fractional
// value - a frame the codec would refuse to decode - is answered bad-limit too, with the stream still
// open for the next request (item FY).

// notesLimitsSession opens an authenticated administration stream and returns it with its token.
func notesLimitsSession(t *testing.T) (*admin.Ops, *testkit.Conn, string) {
	t.Helper()
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
	reply, err := c.Recv(3 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	ok, valid := reply.(*wire.AdminAuthOK)
	if !valid {
		t.Fatalf("expected admin-auth-ok, got %+v", reply)
	}
	c.Authenticated()
	return ops, c, ok.Token
}

// askNotesLimit sends one admin-request envelope and returns its reply.
func askNotesLimit(t *testing.T, c *testkit.Conn, id int64, token string, req *wire.AdminRequest) *wire.AdminReply {
	t.Helper()
	req.ID = id
	req.Token = token
	if err := c.Send(req); err != nil {
		t.Fatal(err)
	}
	reply, err := c.Recv(3 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	rep, valid := reply.(*wire.AdminReply)
	if !valid || rep.ID != id {
		t.Fatalf("request %d: expected its reply, got %+v", id, reply)
	}
	return rep
}

// askNotesLimitRaw sends one hand-built admin-request body - for the fractional value of item FY, which no
// typed envelope carries - and returns its reply.
func askNotesLimitRaw(t *testing.T, c *testkit.Conn, id int64, token, op, login, limits string) *wire.AdminReply {
	t.Helper()
	body := fmt.Sprintf(`{"schemaVersion":%d,"type":"admin-request","id":%d,"token":%q,"op":%q,"login":%q,"limits":%s}`,
		wire.SchemaVersion, id, token, op, login, limits)
	if err := c.SendRaw(testkit.RawFrame(body)); err != nil {
		t.Fatal(err)
	}
	reply, err := c.Recv(3 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	rep, valid := reply.(*wire.AdminReply)
	if !valid || rep.ID != id {
		t.Fatalf("raw request %d: expected its reply, got %+v", id, reply)
	}
	return rep
}

// accountListAlice reads account.list and returns alice's view.
func accountListAlice(t *testing.T, c *testkit.Conn, token string) admin.AccountView {
	t.Helper()
	rep := askNotesLimit(t, c, 9000, token, &wire.AdminRequest{Op: admin.OpAccountList})
	if !rep.OK {
		t.Fatalf("account.list: %+v", rep)
	}
	var list []admin.AccountView
	if err := json.Unmarshal(rep.Data, &list); err != nil {
		t.Fatal(err)
	}
	for _, a := range list {
		if a.Login == "alice" {
			return a
		}
	}
	t.Fatal("account.list does not name alice")
	return admin.AccountView{}
}

func TestRemoteAdministrationCarriesTheNotesLimitsOfItemsFYAndFZ(t *testing.T) {
	ops, c, token := notesLimitsSession(t)

	// account.limits sets both notes members on an existing account, and account.list shows the
	// own limits and the effective limits (items FY and FZ: notesPerAccount, notesMaxBytes, notes, notesBytes)
	five, thousand := 5, int64(1000)
	if rep := askNotesLimit(t, c, 1, token, &wire.AdminRequest{Op: admin.OpAccountLimits, Login: "alice",
		Limits: &wire.AdminLimits{NotesPerAccount: &five, NotesMaxBytes: &thousand}}); !rep.OK {
		t.Fatalf("account.limits: %+v", rep)
	}
	a := accountListAlice(t, c, token)
	if a.Limits == nil || a.Limits.NotesPerAccount == nil || *a.Limits.NotesPerAccount != 5 ||
		a.Limits.NotesMaxBytes == nil || *a.Limits.NotesMaxBytes != 1000 {
		t.Fatalf("alice's own limits: %+v", a.Limits)
	}
	if a.Effective.Notes != 5 || a.Effective.NotesBytes != 1000 {
		t.Fatalf("alice's effective limits: %+v", a.Effective)
	}

	// zero is a value: the account may hold no record and no byte, not "back to the default" (item FY)
	zero, zeroBytes := 0, int64(0)
	if rep := askNotesLimit(t, c, 2, token, &wire.AdminRequest{Op: admin.OpAccountLimits, Login: "alice",
		Limits: &wire.AdminLimits{NotesPerAccount: &zero, NotesMaxBytes: &zeroBytes}}); !rep.OK {
		t.Fatalf("account.limits with zero: %+v", rep)
	}
	if l := ops.Accounts.LimitsFor("alice"); l.Notes != 0 || l.NotesBytes != 0 {
		t.Fatalf("the limits in force after zero: %+v", l)
	}

	// a negative value is answered bad-limit (item FY), and the account keeps its value
	negative := -3
	if rep := askNotesLimit(t, c, 3, token, &wire.AdminRequest{Op: admin.OpAccountLimits, Login: "alice",
		Limits: &wire.AdminLimits{NotesPerAccount: &negative}}); rep.OK || rep.Code != admin.CodeBadLimit {
		t.Fatalf("a negative notes count: %+v, want bad-limit", rep)
	}
	if l := ops.Accounts.LimitsFor("alice"); l.Notes != 0 {
		t.Fatalf("a refused request changed the limits: %+v", l)
	}

	// a fractional value is the same answer over a stream the codec would have closed: the wire keeps
	// the number, the operation answers bad-limit, and the stream stays open for the next request
	if rep := askNotesLimitRaw(t, c, 4, token, admin.OpAccountLimits, "alice", `{"notesPerAccount":1.5}`); rep.OK || rep.Code != admin.CodeBadLimit {
		t.Fatalf("a fractional notes count: %+v, want bad-limit", rep)
	}
	if rep := askNotesLimitRaw(t, c, 5, token, admin.OpPolicySet, "", `{"notesMaxBytes":2.5}`); rep.OK || rep.Code != admin.CodeBadLimit {
		t.Fatalf("a fractional notes byte limit on policy.set: %+v, want bad-limit", rep)
	}
	if rep := askNotesLimit(t, c, 6, token, &wire.AdminRequest{Op: admin.OpStatus}); !rep.OK {
		t.Fatalf("the stream did not survive the fractional requests: %+v", rep)
	}
	if l := ops.Accounts.LimitsFor("alice"); l.Notes != 0 || l.NotesBytes != 0 {
		t.Fatalf("a fraction changed the limits: %+v", l)
	}

	// policy.set reads the limits object as the defaults of a new account (item FY), policy.status shows
	// them in defaults, and an account without its own value resolves to them
	nine, twoKiB := 9, int64(2048)
	if rep := askNotesLimit(t, c, 7, token, &wire.AdminRequest{Op: admin.OpPolicySet,
		Limits: &wire.AdminLimits{NotesPerAccount: &nine, NotesMaxBytes: &twoKiB}}); !rep.OK {
		t.Fatalf("policy.set: %+v", rep)
	}
	rep := askNotesLimit(t, c, 8, token, &wire.AdminRequest{Op: admin.OpPolicyStatus})
	if !rep.OK {
		t.Fatalf("policy.status: %+v", rep)
	}
	var policy admin.PolicyView
	if err := json.Unmarshal(rep.Data, &policy); err != nil {
		t.Fatal(err)
	}
	if policy.Defaults.Notes != 9 || policy.Defaults.NotesBytes != 2048 {
		t.Fatalf("policy.status defaults: %+v", policy.Defaults)
	}
	if _, err := ops.AddAccount("carol", "CarolPass123"); err != nil {
		t.Fatal(err)
	}
	if l := ops.Accounts.LimitsFor("carol"); l.Notes != 9 || l.NotesBytes != 2048 {
		t.Fatalf("a new account resolves to the new default: %+v", l)
	}

	// an absent limits object clears the account's own values (REMOTE-ADMIN account.limits), also for
	// the notes members, and the effective limits fall back to the defaults
	if rep := askNotesLimit(t, c, 10, token, &wire.AdminRequest{Op: admin.OpAccountLimits, Login: "alice"}); !rep.OK {
		t.Fatalf("account.limits without limits: %+v", rep)
	}
	a = accountListAlice(t, c, token)
	if a.Limits != nil {
		t.Fatalf("alice's own limits survived the clear: %+v", a.Limits)
	}
	if a.Effective.Notes != 9 || a.Effective.NotesBytes != 2048 {
		t.Fatalf("after the clear the effective limits are the defaults: %+v", a.Effective)
	}
}
