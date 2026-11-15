package admin

import (
	"encoding/json"
	"fmt"
	"io"
	"net"
	"os"
	"path/filepath"
	"reflect"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/directory"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/notes"
	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/wire"
)

var cheap = password.Params{Time: 1, Memory: 8, Threads: 1, KeyLen: 32}

const pw1 = "Passw0rdTest"

func newOps(t *testing.T) (*Ops, *logx.Capture) {
	t.Helper()
	dir := t.TempDir()
	store, err := accounts.Open(accounts.Options{Dir: dir, Params: cheap})
	if err != nil {
		t.Fatal(err)
	}
	d := directory.New(directory.Options{Dir: dir})
	if err := d.Load(nil); err != nil {
		t.Fatal(err)
	}
	log := logx.NewCapture()
	return &Ops{Accounts: store, Dir: d, Log: log.Logger}, log
}

// serve runs the channel over a loopback listener (the transport is deploy's; the protocol is not
// tied to it) and returns a function that sends one request.
func serve(t *testing.T, o *Ops, log *logx.Logger) func(Request) (Response, []byte) {
	t.Helper()
	l, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	done := make(chan struct{})
	go func() { Serve(l, o, log); close(done) }()
	t.Cleanup(func() { l.Close(); <-done })
	return func(req Request) (Response, []byte) {
		t.Helper()
		c, err := net.Dial("tcp", l.Addr().String())
		if err != nil {
			t.Fatal(err)
		}
		defer c.Close()
		b, _ := json.Marshal(req)
		_, _ = c.Write(b)
		_ = c.SetReadDeadline(time.Now().Add(5 * time.Second))
		raw, _ := io.ReadAll(c)
		var resp Response
		if err := json.Unmarshal(raw, &resp); err != nil {
			t.Fatalf("answer %q: %v", raw, err)
		}
		return resp, raw
	}
}

func enroll(t *testing.T, o *Ops, login, devID string) accounts.Enrolled {
	t.Helper()
	e, err := o.Accounts.Enroll(accounts.EnrollSpec{Account: login, DeviceID: devID, Name: "Phone", Product: "fms-android"})
	if err != nil {
		t.Fatal(err)
	}
	o.Dir.DeviceEnrolled(directory.DeviceInfo{ID: devID, Account: login, Name: "Phone", Product: "fms-android"})
	return e
}

func TestTheChannelDoesTheSevenOperationsAndNeverReturnsASecret(t *testing.T) {
	o, log := newOps(t)
	call := serve(t, o, log.Logger)
	var all []string

	r, raw := call(Request{Op: OpAccountAdd, Login: "Alice", Password: pw1})
	all = append(all, string(raw))
	if !r.OK {
		t.Fatalf("%+v", r)
	}
	var added struct{ Login string }
	_ = json.Unmarshal(r.Data, &added)
	if added.Login != "alice" {
		t.Fatalf("the normalised login is returned: %s", r.Data)
	}
	devA, devB := wire.NewID(), wire.NewID()
	ea := enroll(t, o, "alice", devA)
	enroll(t, o, "alice", devB)

	r, raw = call(Request{Op: OpAccountList})
	all = append(all, string(raw))
	var accts []AccountView
	_ = json.Unmarshal(r.Data, &accts)
	if !r.OK || len(accts) != 1 || accts[0].Login != "alice" || accts[0].Devices != 2 {
		t.Fatalf("%+v %s", r, r.Data)
	}
	r, raw = call(Request{Op: OpDeviceList, Login: "alice"})
	all = append(all, string(raw))
	var devs []DeviceView
	_ = json.Unmarshal(r.Data, &devs)
	if !r.OK || len(devs) != 2 || devs[0].Online {
		t.Fatalf("%+v %s", r, r.Data)
	}
	r, raw = call(Request{Op: OpStatus})
	all = append(all, string(raw))
	var st Status
	_ = json.Unmarshal(r.Data, &st)
	if !r.OK || st.Accounts != 1 || st.Devices != 2 {
		t.Fatalf("%+v", st)
	}
	r, _ = call(Request{Op: OpDeviceRevoke, Login: "alice", DeviceID: devA})
	if !r.OK {
		t.Fatalf("%+v", r)
	}
	r, _ = call(Request{Op: OpDeviceRevoke, Login: "alice", DeviceID: devA})
	if r.OK || r.Code != CodeNotFound {
		t.Fatalf("a second revoke: %+v", r)
	}
	r, _ = call(Request{Op: OpAccountPasswd, Login: "alice", Password: "N3wPassword!x"})
	if !r.OK {
		t.Fatalf("%+v", r)
	}
	r, _ = call(Request{Op: OpAccountRemove, Login: "alice"})
	if !r.OK || o.Accounts.Exists("alice") {
		t.Fatalf("%+v", r)
	}

	// no response, on any call, carries a password, a token or a hash
	for _, a := range all {
		for _, secret := range []string{pw1, "N3wPassword!x", ea.Token, ea.Device.TokenHash, "$argon2id$"} {
			if strings.Contains(a, secret) {
				t.Errorf("a response carries %q: %s", secret, a)
			}
		}
	}
	// and the log never saw a password either
	log.AssertNone(t, pw1, "N3wPassword!x", ea.Token, devA, devB)
}

func TestFailuresHaveStableCodes(t *testing.T) {
	o, log := newOps(t)
	call := serve(t, o, log.Logger)
	for name, c := range map[string]struct {
		req  Request
		code string
	}{
		"weak password":   {Request{Op: OpAccountAdd, Login: "carol", Password: "weak"}, CodeWeak},
		"bad login":       {Request{Op: OpAccountAdd, Login: "c", Password: pw1}, CodeBadLogin},
		"unknown account": {Request{Op: OpAccountPasswd, Login: "nobody", Password: pw1}, CodeNoAccount},
		"device list":     {Request{Op: OpDeviceList, Login: "nobody"}, CodeNoAccount},
		"remove nobody":   {Request{Op: OpAccountRemove, Login: "nobody"}, CodeNoAccount},
		"unknown op":      {Request{Op: "format.disk"}, CodeBadRequest},
		"no op":           {Request{}, CodeBadRequest},
	} {
		r, raw := call(c.req)
		if r.OK || r.Code != c.code || r.Error == "" {
			t.Errorf("%s: %+v", name, r)
		}
		if strings.Contains(string(raw), "weak") && name == "weak password" && strings.Contains(string(raw), c.req.Password+"\"") {
			t.Errorf("%s: the password came back", name)
		}
	}
	if r, _ := call(Request{Op: OpAccountAdd, Login: "dave", Password: pw1}); !r.OK {
		t.Fatal(r)
	}
	if r, _ := call(Request{Op: OpAccountAdd, Login: "DAVE", Password: pw1}); r.OK || r.Code != CodeExists {
		t.Fatalf("a duplicate: %+v", r)
	}
	if err := (&Error{Code: "x", Message: "m"}); err.Error() != "m" {
		t.Fatal("Error text")
	}
	if (Response{OK: true}).Err() != nil || (Response{Code: "c", Error: "e"}).Err() == nil {
		t.Fatal("Response.Err")
	}
}

func TestPasswordChangeSignOutRevokesEveryDeviceOfTheAccountOnly(t *testing.T) {
	o, log := newOps(t)
	call := serve(t, o, log.Logger)
	call(Request{Op: OpAccountAdd, Login: "alice", Password: pw1})
	call(Request{Op: OpAccountAdd, Login: "bobby", Password: pw1})
	a1, a2, b1 := wire.NewID(), wire.NewID(), wire.NewID()
	e1 := enroll(t, o, "alice", a1)
	enroll(t, o, "alice", a2)
	eb := enroll(t, o, "bobby", b1)
	// From release 46 a reset signs every device of the account out (19 item AT) and keeps the records
	call(Request{Op: OpAccountPasswd, Login: "alice", Password: "N3wPassword!x"})
	if _, st := o.Accounts.Hello(a1, e1.Token); st != accounts.HelloSignedOut {
		t.Fatalf("a reset must sign the devices out: %v", st)
	}
	if len(o.Accounts.Devices("alice")) != 2 || len(o.Accounts.Devices("bobby")) != 1 {
		t.Fatal("a reset must keep the device records and touch no other account")
	}
	if _, st := o.Accounts.Hello(b1, eb.Token); st != accounts.HelloOK {
		t.Fatal("another account's device was signed out")
	}
}

func TestTheRequestIsBoundedAndEveryConnectionGetsOneAnswer(t *testing.T) {
	old := ExchangeLimit
	ExchangeLimit = 300 * time.Millisecond
	defer func() { ExchangeLimit = old }()
	o, log := newOps(t)
	l, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer l.Close()
	go Serve(l, o, log.Logger)

	send := func(body []byte) Response {
		c, err := net.Dial("tcp", l.Addr().String())
		if err != nil {
			t.Fatal(err)
		}
		defer c.Close()
		_, _ = c.Write(body)
		_ = c.SetReadDeadline(time.Now().Add(5 * time.Second))
		raw, _ := io.ReadAll(c)
		var r Response
		_ = json.Unmarshal(raw, &r)
		return r
	}
	big, _ := json.Marshal(Request{Op: OpAccountAdd, Login: "alice", Password: strings.Repeat("A1a", 3000)})
	if r := send(big); r.OK || r.Code != CodeBadRequest {
		t.Fatalf("a request above %d bytes: %+v", MaxRequest, r)
	}
	for _, junk := range []string{"not json", "[]", `{"op":`, ""} {
		if r := send([]byte(junk)); r.OK || r.Code != CodeBadRequest {
			t.Errorf("%q: %+v", junk, r)
		}
	}
	if o.Accounts.HasAccounts() {
		t.Fatal("a refused request created an account")
	}
}

func TestAtMostFourConnectionsAreServedAtOnceAndASilentOneTimesOut(t *testing.T) {
	old := ExchangeLimit
	ExchangeLimit = 400 * time.Millisecond
	defer func() { ExchangeLimit = old }()
	o, log := newOps(t)
	l, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer l.Close()
	go Serve(l, o, log.Logger)

	var held []net.Conn
	for i := 0; i < 4; i++ {
		c, err := net.Dial("tcp", l.Addr().String())
		if err != nil {
			t.Fatal(err)
		}
		held = append(held, c)
		defer c.Close()
	}
	time.Sleep(100 * time.Millisecond)
	// the fifth is closed unanswered while the four are in flight
	c5, _ := net.Dial("tcp", l.Addr().String())
	defer c5.Close()
	_ = c5.SetReadDeadline(time.Now().Add(250 * time.Millisecond))
	if n, err := c5.Read(make([]byte, 8)); n != 0 || err == nil {
		t.Fatalf("a fifth concurrent connection must be closed: %d %v", n, err)
	}
	// the four silent ones are answered with a refusal when their deadline passes, then closed
	_ = held[0].SetReadDeadline(time.Now().Add(3 * time.Second))
	raw, _ := io.ReadAll(held[0])
	var r Response
	if err := json.Unmarshal(raw, &r); err != nil || r.OK || r.Code != CodeBadRequest {
		t.Fatalf("a silent connection: %q %v", raw, err)
	}
	// and the slots are free again
	time.Sleep(300 * time.Millisecond)
	c6, _ := net.Dial("tcp", l.Addr().String())
	defer c6.Close()
	b, _ := json.Marshal(Request{Op: OpStatus})
	_, _ = c6.Write(b)
	_ = c6.SetReadDeadline(time.Now().Add(3 * time.Second))
	raw, _ = io.ReadAll(c6)
	if err := json.Unmarshal(raw, &r); err != nil || !r.OK {
		t.Fatalf("after the deadline the channel serves again: %q %v", raw, err)
	}
}

func TestARequestPrintsItsOperationOnly(t *testing.T) {
	r := Request{Op: OpAccountAdd, Login: "alice", Password: pw1}
	for _, v := range []any{r, &r} {
		for _, verb := range []string{"%v", "%+v", "%#v", "%s"} {
			if out := fmt.Sprintf(verb, v); strings.Contains(out, pw1) {
				t.Errorf("%s printed the password: %s", verb, out)
			}
		}
	}
}

func TestDoTalksToServeAndRefusesAnOversizeRequest(t *testing.T) {
	o, log := newOps(t)
	l, _ := net.Listen("tcp", "127.0.0.1:0")
	defer l.Close()
	go Serve(l, o, log.Logger)
	c, _ := net.Dial("tcp", l.Addr().String())
	resp, err := Do(c, Request{Op: OpAccountAdd, Login: "erin", Password: pw1})
	if err != nil || !resp.OK {
		t.Fatalf("%v %+v", err, resp)
	}
	c2, _ := net.Dial("tcp", l.Addr().String())
	if _, err := Do(c2, Request{Op: OpAccountAdd, Login: "erin", Password: strings.Repeat("a", MaxRequest)}); err == nil {
		t.Fatal("the client must refuse to send an oversize request")
	}
}

func TestThePairingCodeOperationIssuesASingleUseCodeAndNeverLogsIt(t *testing.T) {
	o, log := newOps(t)
	call := serve(t, o, log.Logger)
	if r, _ := call(Request{Op: OpAccountAdd, Login: "alice", Password: pw1}); !r.OK {
		t.Fatal(r)
	}

	// an offline command's Ops (its own store, nothing to redeem against) refuses instead of handing out a code
	r, raw := call(Request{Op: OpDevicePairingCode, Login: "alice"})
	if r.OK || r.Code != CodeBadRequest || r.Data != nil {
		t.Fatalf("not live: %s", raw)
	}

	o.Live = true
	if r, _ = call(Request{Op: OpDevicePairingCode, Login: "nobody"}); r.OK || r.Code != CodeNoAccount {
		t.Fatalf("no such account: %+v", r)
	}
	if r, _ = call(Request{Op: OpDevicePairingCode, Login: "x"}); r.OK || r.Code != CodeNoAccount {
		t.Fatalf("a login that cannot exist is no account: %+v", r)
	}

	r, raw = call(Request{Op: OpDevicePairingCode, Login: "Alice"})
	if !r.OK {
		t.Fatalf("%+v", r)
	}
	var v PairingCodeView
	if err := json.Unmarshal(r.Data, &v); err != nil || len(v.Code) != 8 || v.ExpiresAt == "" {
		t.Fatalf("the answer is a code and its expiry: %s (%v)", r.Data, err)
	}
	exp, err := time.Parse(time.RFC3339, v.ExpiresAt)
	if err != nil || time.Until(exp) <= 0 || time.Until(exp) > 11*time.Minute {
		t.Fatalf("expiry %q: %v", v.ExpiresAt, err)
	}
	// it is the very code the engine's store redeems, once
	if !o.Accounts.RedeemPairingCode("alice", v.Code) {
		t.Fatal("the issued code does not redeem")
	}
	if o.Accounts.RedeemPairingCode("alice", v.Code) {
		t.Fatal("a pairing code is single use")
	}

	// the limit of outstanding codes is a named failure, not an internal one
	var capped bool
	for i := 0; i < 20 && !capped; i++ {
		r, raw = call(Request{Op: OpDevicePairingCode, Login: "alice"})
		if !r.OK {
			if r.Code != CodeCapacity {
				t.Fatalf("a full account: %+v", r)
			}
			capped = true
		}
	}
	if !capped {
		t.Fatal("the account never reached its limit of outstanding codes")
	}

	log.AssertNone(t, v.Code, pw1)
	if !strings.Contains(strings.Join(log.Lines(), "\n"), "pairing-code-issued") {
		t.Error("the issue is logged (by the account's short hash)")
	}
}

func TestAdminInvitations(t *testing.T) {
	o, log := newOps(t)
	call := serve(t, o, log.Logger)

	// Create a member invitation
	r, raw := call(Request{Op: OpInvitationCreate, Kind: "member"})
	if !r.OK {
		t.Fatalf("create invitation failed: %+v", r)
	}
	var iv CreateInvitationResult
	if err := json.Unmarshal(r.Data, &iv); err != nil || len(iv.Code) != 22 || iv.Invitation.Kind != "member" {
		t.Fatalf("unexpected invitation view: %s (%v)", r.Data, err)
	}

	// Create guest invitation
	r, _ = call(Request{Op: OpInvitationCreate, Kind: "guest"})
	if !r.OK {
		t.Fatalf("create guest invitation failed: %+v", r)
	}
	var iv2 CreateInvitationResult
	_ = json.Unmarshal(r.Data, &iv2)
	if iv2.Invitation.Kind != "guest" {
		t.Fatalf("unexpected guest invitation: %+v", iv2)
	}

	// List invitations
	r, rawList := call(Request{Op: OpInvitationList})
	if !r.OK {
		t.Fatalf("list invitations failed: %+v", r)
	}
	var list []InvitationView
	if err := json.Unmarshal(r.Data, &list); err != nil || len(list) != 2 {
		t.Fatalf("unexpected list: %s (%v)", r.Data, err)
	}

	// Code must NEVER be in list response
	if strings.Contains(string(rawList), iv.Code) || strings.Contains(string(rawList), iv2.Code) {
		t.Fatalf("raw list response leaked plaintext code: %s", rawList)
	}

	// Withdraw first invitation
	r, _ = call(Request{Op: OpInvitationWithdraw, CodeHash: iv.Invitation.CodeHash[:8]})
	if !r.OK {
		t.Fatalf("withdraw invitation failed: %+v", r)
	}

	// List again - withdrawn invitation should be removed from invitations list
	r, _ = call(Request{Op: OpInvitationList})
	_ = json.Unmarshal(r.Data, &list)
	for _, it := range list {
		if it.CodeHash == iv.Invitation.CodeHash {
			t.Fatalf("withdrawn invitation still in list: %+v", it)
		}
	}

	// Logs must never leak code
	log.AssertNone(t, iv.Code, iv2.Code)
	_ = raw
}

func TestAdminSharedInvitation(t *testing.T) {
	o, log := newOps(t)
	call := serve(t, o, log.Logger)

	// Status initially off
	r, _ := call(Request{Op: OpSharedInvitationStatus})
	if !r.OK {
		t.Fatalf("status failed: %+v", r)
	}
	var st SharedInvitationView
	_ = json.Unmarshal(r.Data, &st)
	if st.Enabled {
		t.Fatal("shared invitation should initially be disabled")
	}

	// Set shared invitation
	secretPW := "SharedSecret123!"
	r, _ = call(Request{Op: OpSharedInvitationSet, Password: secretPW, Kind: "guest"})
	if !r.OK {
		t.Fatalf("set shared invitation failed: %+v", r)
	}

	// Check status
	r, rawSt := call(Request{Op: OpSharedInvitationStatus})
	if !r.OK {
		t.Fatalf("status failed: %+v", r)
	}
	_ = json.Unmarshal(r.Data, &st)
	if !st.Enabled || st.Kind != "guest" {
		t.Fatalf("unexpected status view: %+v", st)
	}
	if strings.Contains(string(rawSt), secretPW) {
		t.Fatal("status response leaked shared password")
	}

	// Turn off
	r, _ = call(Request{Op: OpSharedInvitationSet, Password: ""})
	if !r.OK {
		t.Fatalf("turn off failed: %+v", r)
	}
	r, _ = call(Request{Op: OpSharedInvitationStatus})
	_ = json.Unmarshal(r.Data, &st)
	if st.Enabled {
		t.Fatal("shared invitation should now be disabled")
	}

	log.AssertNone(t, secretPW)
}

func TestAdminAccountLimits(t *testing.T) {
	o, log := newOps(t)
	call := serve(t, o, log.Logger)

	call(Request{Op: OpAccountAdd, Login: "carol", Password: pw1, Kind: "member"})

	devs := 5
	res := 25
	bcasts := 3
	clip := int64(1048576)

	r, _ := call(Request{
		Op:    OpAccountLimits,
		Login: "carol",
		Limits: &accounts.AccountLimits{
			DevicesPerAccount:    &devs,
			ResourcesPerAccount:  &res,
			BroadcastsPerAccount: &bcasts,
			ClipboardMaxBytes:    &clip,
		},
	})
	if !r.OK {
		t.Fatalf("set limits failed: %+v", r)
	}

	r, _ = call(Request{Op: OpAccountList})
	if !r.OK {
		t.Fatalf("list accounts failed: %+v", r)
	}
	var accts []AccountView
	_ = json.Unmarshal(r.Data, &accts)
	var carol *AccountView
	for i := range accts {
		if accts[i].Login == "carol" {
			carol = &accts[i]
		}
	}
	if carol == nil || carol.Limits == nil || carol.Limits.DevicesPerAccount == nil || *carol.Limits.DevicesPerAccount != 5 || *carol.Limits.ResourcesPerAccount != 25 {
		t.Fatalf("carol limits not reflected: %+v", carol)
	}
}

type sink struct{ frames int }

func (s *sink) Send(wire.Envelope) error { s.frames++; return nil }
func (s *sink) SendFrame([]byte) error   { s.frames++; return nil }
func (s *sink) Close()                   {}

func TestAdminGrants(t *testing.T) {
	o, log := newOps(t)
	call := serve(t, o, log.Logger)

	call(Request{Op: OpAccountAdd, Login: "host1", Password: pw1, Kind: "member"})
	call(Request{Op: OpAccountAdd, Login: "guest1", Password: pw1, Kind: "guest"})
	dev := wire.NewID()
	enroll(t, o, "host1", dev)
	o.Dir.Online("host1", dev, &sink{})
	resID := wire.NewID()
	rec := `{"resourceId":"` + resID + `","deviceId":"` + dev + `","kind":"sftp-share","name":"Photos",` +
		`"access":{"descriptor":"FMSSFTP2.secret-descriptor","root":"/"}}`
	if _, err := o.Dir.Publish("host1", dev, json.RawMessage(rec)); err != nil {
		t.Fatal(err)
	}

	// a resource the account does not hold, and an account that does not exist, are named failures
	r, _ := call(Request{Op: OpGrantAdd, Login: "host1", GuestAccount: "guest1", TargetID: wire.NewID(), Kind: "resource"})
	if r.OK {
		t.Fatal("a grant of what the account does not hold must be refused")
	}
	r, _ = call(Request{Op: OpGrantAdd, Login: "host1", GuestAccount: "nobody", TargetID: resID, Kind: "resource"})
	if r.OK || r.Code != CodeNoAccount {
		t.Fatalf("a grant to an unknown account: %+v", r)
	}

	// Grant resource
	r, _ = call(Request{Op: OpGrantAdd, Login: "host1", GuestAccount: "guest1", TargetID: resID, Kind: "resource"})
	if !r.OK {
		t.Fatalf("add grant failed: %+v", r)
	}

	// List grants (what the guest was given)
	r, _ = call(Request{Op: OpGrantList, GuestAccount: "guest1"})
	if !r.OK {
		t.Fatalf("list grants failed: %+v", r)
	}
	var grants []GrantView
	if err := json.Unmarshal(r.Data, &grants); err != nil || len(grants) != 1 {
		t.Fatalf("unexpected grants: %s (%v)", r.Data, err)
	}
	if grants[0].TargetID != resID || grants[0].Kind != "resource" || grants[0].Guest != "guest1" {
		t.Fatalf("grant details wrong: %+v", grants[0])
	}

	// Remove grant
	r, _ = call(Request{Op: OpGrantRemove, Login: "host1", GuestAccount: "guest1", TargetID: resID})
	if !r.OK {
		t.Fatalf("remove grant failed: %+v", r)
	}

	// List grants again
	r, _ = call(Request{Op: OpGrantList, GuestAccount: "guest1"})
	grants = nil
	_ = json.Unmarshal(r.Data, &grants)
	if len(grants) != 0 {
		t.Fatalf("grants not empty after remove: %+v", grants)
	}
}

// The notes of an account and its life (DEVICE-EXCHANGE notes sync, ticket L0321, items AU and AO): the erasure runs
// in Ops.RemoveAccount and in Ops.SetKind on the way to a guest, so the holder's own deletion, the operator's
// removal and the offline command line all end in it. A box is the marker below; the log must never carry it.

const (
	noteX   = "00000000-0000-4000-8000-000000000001"
	noteY   = "00000000-0000-4000-8000-000000000002"
	noteZ   = "00000000-0000-4000-8000-000000000003"
	noteBox = `{"v":1,"k":"BOXSECRET"}` // already in the compact form the store keeps
)

// notesOps is newOps with a notes store on a data directory of its own and two member accounts, alice and bobby.
func notesOps(t *testing.T) (*Ops, *logx.Capture, *notes.Store, string) {
	t.Helper()
	o, log := newOps(t)
	dir := t.TempDir()
	ns, err := notes.Open(dir, nil)
	if err != nil {
		t.Fatal(err)
	}
	o.Notes = ns
	for _, l := range []string{"alice", "bobby"} {
		if _, err := o.AddAccount(l, pw1); err != nil {
			t.Fatal(err)
		}
	}
	return o, log, ns, dir
}

// putNote stores one live note of an account straight in the store: the engine would refuse a guest, the store
// knows no kinds and keeps what it is given.
func putNote(t *testing.T, ns *notes.Store, account, noteID string) {
	t.Helper()
	if _, err := ns.Put(account, "device-1", noteID, 0, false, json.RawMessage(noteBox), 0); err != nil {
		t.Fatal(err)
	}
}

// putTombstone stores the tombstone of a note the store never held (a delete with base revision 0 creates one).
func putTombstone(t *testing.T, ns *notes.Store, account, noteID string) {
	t.Helper()
	if _, err := ns.Put(account, "device-1", noteID, 0, true, nil, 0); err != nil {
		t.Fatal(err)
	}
}

// notesOf is what the store holds for an account: its records, tombstones counted, and its revision counter.
func notesOf(ns *notes.Store, account string) (count int, revision int64) {
	revision, _, count = ns.State(account)
	return count, revision
}

// linesWith is the captured log lines whose event column is event.
func linesWith(log *logx.Capture, event string) []string {
	var out []string
	for _, l := range log.Lines() {
		if strings.Contains(l, "\t"+event+"\t") {
			out = append(out, l)
		}
	}
	return out
}

func TestRemoveAccountErasesItsNotesAndLeavesTheOthers(t *testing.T) {
	o, log, ns, dir := notesOps(t)
	putNote(t, ns, "alice", noteX)
	putNote(t, ns, "alice", noteY)
	putTombstone(t, ns, "alice", noteZ)
	putNote(t, ns, "bobby", noteX)

	if err := o.RemoveAccount("alice"); err != nil {
		t.Fatalf("RemoveAccount: %v", err)
	}
	if o.Accounts.Exists("alice") {
		t.Fatal("the account is still there")
	}
	if n, rev := notesOf(ns, "alice"); n != 0 || rev != 0 {
		t.Fatalf("alice keeps %d records and counter %d after the removal", n, rev)
	}
	if n, rev := notesOf(ns, "bobby"); n != 1 || rev != 1 {
		t.Fatalf("another account lost its notes: %d records, counter %d", n, rev)
	}
	if got := ns.All("bobby"); len(got) != 1 || got[0].NoteID != noteX || string(got[0].Sealed) != noteBox {
		t.Fatalf("bobby's record changed: %+v", got)
	}

	// the erasure is in the file too: a store opened again sees the same
	again, err := notes.Open(dir, nil)
	if err != nil {
		t.Fatal(err)
	}
	if n, _ := notesOf(again, "alice"); n != 0 {
		t.Fatalf("alice has %d records on disk after the removal", n)
	}
	if n, _ := notesOf(again, "bobby"); n != 1 {
		t.Fatalf("bobby has %d records on disk, want 1", n)
	}

	// the log carries the count of records (tombstones included) and never a note id, a box or a login
	erased := linesWith(log, "notes-erased")
	if len(erased) != 1 || !strings.Contains(erased[0], "records=3") {
		t.Fatalf("want one notes-erased line with records=3, got %q", erased)
	}
	log.AssertNone(t, "BOXSECRET", noteX, noteY, noteZ, "alice")
}

func TestSetKindErasesTheNotesOnlyOnTheWayToAGuest(t *testing.T) {
	o, log, ns, dir := notesOps(t)
	putNote(t, ns, "alice", noteX)
	putNote(t, ns, "alice", noteY)
	putNote(t, ns, "bobby", noteX)
	if _, _, err := o.AddAccountKind("gina", pw1, accounts.KindGuest); err != nil {
		t.Fatal(err)
	}
	putNote(t, ns, "gina", noteX) // a guest holds none through the engine; the store keeps what it is given

	// no change of kind: nothing is erased
	if changed, err := o.SetKind("alice", accounts.KindMember); changed || err != nil {
		t.Fatalf("a member made a member: changed %v, %v", changed, err)
	}
	if n, _ := notesOf(ns, "alice"); n != 2 {
		t.Fatalf("a kind that did not change erased the notes: %d records left, want 2", n)
	}

	// a guest that becomes a member: only the way to a guest erases
	if changed, err := o.SetKind("gina", accounts.KindMember); !changed || err != nil {
		t.Fatalf("a guest made a member: changed %v, %v", changed, err)
	}
	if n, _ := notesOf(ns, "gina"); n != 1 {
		t.Fatalf("becoming a member erased the notes: %d records left, want 1", n)
	}
	if got := linesWith(log, "notes-erased"); len(got) != 0 {
		t.Fatalf("a notes-erased line before any erasure: %q", got)
	}

	// a member that becomes a guest: its notes go with one write, the other accounts keep theirs
	if changed, err := o.SetKind("alice", accounts.KindGuest); !changed || err != nil {
		t.Fatalf("a member made a guest: changed %v, %v", changed, err)
	}
	if o.Accounts.KindOf("alice") != accounts.KindGuest {
		t.Fatal("alice is not a guest")
	}
	// the account goes on: no records, but the counter it had (2) is kept and the horizon is raised to it, so a
	// revision never comes back (item FG) and a device that synchronised up to 2 is answered `full`
	if n, rev := notesOf(ns, "alice"); n != 0 || rev != 2 {
		t.Fatalf("a new guest keeps %d records and counter %d, want no records and the counter 2 it had", n, rev)
	}
	if _, hor, _ := ns.State("alice"); hor != 2 {
		t.Fatalf("a new guest has horizon %d, want 2 (the counter)", hor)
	}
	if n, _ := notesOf(ns, "bobby"); n != 1 {
		t.Fatalf("another account lost its notes: %d records, want 1", n)
	}
	if n, _ := notesOf(ns, "gina"); n != 1 {
		t.Fatalf("the former guest lost its notes: %d records, want 1", n)
	}
	again, err := notes.Open(dir, nil)
	if err != nil {
		t.Fatal(err)
	}
	if a, rev := notesOf(again, "alice"); a != 0 || rev != 2 {
		t.Fatalf("alice has %d records and counter %d on disk after becoming a guest, want none and 2", a, rev)
	}
	if _, hor, _ := again.State("alice"); hor != 2 {
		t.Fatalf("alice has horizon %d on disk after becoming a guest, want 2", hor)
	}
	if b, _ := notesOf(again, "bobby"); b != 1 {
		t.Fatalf("bobby has %d records on disk, want 1", b)
	}
	erased := linesWith(log, "notes-erased")
	if len(erased) != 1 || !strings.Contains(erased[0], "records=2") {
		t.Fatalf("want one notes-erased line with records=2, got %q", erased)
	}
	log.AssertNone(t, "BOXSECRET", noteX, noteY, "alice")

	// a guest made a guest again is no change: what the store holds for it is not touched
	putNote(t, ns, "alice", noteZ)
	if changed, err := o.SetKind("alice", accounts.KindGuest); changed || err != nil {
		t.Fatalf("a guest made a guest: changed %v, %v", changed, err)
	}
	if n, rev := notesOf(ns, "alice"); n != 1 || rev != 3 {
		t.Fatalf("a kind that did not change erased the notes (or the new note reused a revision): %d records, counter %d, want 1 and 3", n, rev)
	}
	if got := linesWith(log, "notes-erased"); len(got) != 1 {
		t.Fatalf("a second notes-erased line for no change: %q", got)
	}
}

func TestNotesErasureIsANoOpWithoutNotesOrWithoutAStore(t *testing.T) {
	// no notes anywhere: the removal and the way to a guest write nothing and say nothing, and the store keeps no file
	o, log, ns, dir := notesOps(t)
	if _, err := o.AddAccount("carol", pw1); err != nil {
		t.Fatal(err)
	}
	if err := o.RemoveAccount("alice"); err != nil {
		t.Fatalf("RemoveAccount of an account with no notes: %v", err)
	}
	if changed, err := o.SetKind("carol", accounts.KindGuest); !changed || err != nil {
		t.Fatalf("SetKind of an account with no notes: changed %v, %v", changed, err)
	}
	if _, err := os.Stat(filepath.Join(dir, notes.File)); !os.IsNotExist(err) {
		t.Fatalf("an erasure with nothing to erase created %s: %v", notes.File, err)
	}
	if got := linesWith(log, "notes-erased"); len(got) != 0 {
		t.Fatalf("a notes-erased line with nothing erased: %q", got)
	}

	// another account holds notes: erasing an account that holds none leaves the file as it was
	putNote(t, ns, "bobby", noteX)
	before, err := os.ReadFile(filepath.Join(dir, notes.File))
	if err != nil {
		t.Fatal(err)
	}
	if err := o.RemoveAccount("carol"); err != nil {
		t.Fatalf("RemoveAccount of an account with no notes: %v", err)
	}
	after, err := os.ReadFile(filepath.Join(dir, notes.File))
	if err != nil || string(after) != string(before) {
		t.Fatalf("an erasure with nothing to erase rewrote the store file: %v", err)
	}
	if got := linesWith(log, "notes-erased"); len(got) != 0 {
		t.Fatalf("a notes-erased line with nothing erased: %q", got)
	}

	// no store at all (a server that keeps none): both paths work and the log says nothing about notes
	bare, bareLog := newOps(t)
	for _, l := range []string{"alice", "bobby"} {
		if _, err := bare.AddAccount(l, pw1); err != nil {
			t.Fatal(err)
		}
	}
	if changed, err := bare.SetKind("alice", accounts.KindGuest); !changed || err != nil {
		t.Fatalf("SetKind with no notes store: changed %v, %v", changed, err)
	}
	if err := bare.RemoveAccount("bobby"); err != nil {
		t.Fatalf("RemoveAccount with no notes store: %v", err)
	}
	for _, l := range bareLog.Lines() {
		if strings.Contains(l, "notes") {
			t.Fatalf("a log line about notes with no notes store: %q", l)
		}
	}
}

func TestAFailingNotesStoreIsReportedAndKeepsTheNotes(t *testing.T) {
	o, log, ns, dir := notesOps(t)
	putNote(t, ns, "alice", noteX)
	putNote(t, ns, "bobby", noteX)
	// the store writes through notes.json.tmp: a directory of that name makes every write fail
	if err := os.Mkdir(filepath.Join(dir, notes.File+".tmp"), 0o700); err != nil {
		t.Fatal(err)
	}

	// SetKind: the kind has changed already, so the answer says so and reports the failure
	changed, err := o.SetKind("alice", accounts.KindGuest)
	if !changed || err == nil {
		t.Fatalf("SetKind with a store that cannot be written: changed %v, error %v", changed, err)
	}
	if o.Accounts.KindOf("alice") != accounts.KindGuest {
		t.Fatal("the kind did not change")
	}
	if n, _ := notesOf(ns, "alice"); n != 1 {
		t.Fatalf("a failed write left %d records in memory, want the 1 it held", n)
	}

	// RemoveAccount: the account is gone, the failure is returned, the notes stay for the operator to see
	if err := o.RemoveAccount("bobby"); err == nil {
		t.Fatal("RemoveAccount with a store that cannot be written reported no error")
	}
	if o.Accounts.Exists("bobby") {
		t.Fatal("the account is still there")
	}
	if n, _ := notesOf(ns, "bobby"); n != 1 {
		t.Fatalf("a failed write left %d records in memory, want the 1 it held", n)
	}

	if got := linesWith(log, "notes-erase-failed"); len(got) != 2 {
		t.Fatalf("want two notes-erase-failed lines, got %q", got)
	}
	if got := linesWith(log, "notes-erased"); len(got) != 0 {
		t.Fatalf("a notes-erased line for an erasure that failed: %q", got)
	}
	log.AssertNone(t, "BOXSECRET", noteX, "alice", "bobby")
}

func TestSetPolicyKeepsTheNotesDefaultAndTheWireHasNoMemberForIt(t *testing.T) {
	o, _ := newOps(t)
	if got := o.Accounts.Policy().Defaults.Notes; got != accounts.DefaultNotesPerAccount {
		t.Fatalf("the policy of a fresh store carries notes %d, want %d", got, accounts.DefaultNotesPerAccount)
	}
	p := o.Accounts.Policy()
	p.Defaults.Notes = 7 // what the operator's config says
	o.Accounts.SetPolicy(p)

	// the policy that SaveLimits writes to the config and the one kept in force both still carry it
	var saved accounts.Policy
	o.SaveLimits = func(next accounts.Policy) error { saved = next; return nil }
	three, accountsCap := 3, 4
	v, err := o.SetPolicy(&accountsCap, &accounts.AccountLimits{DevicesPerAccount: &three})
	if err != nil {
		t.Fatal(err)
	}
	if saved.Defaults.Notes != 7 || saved.Defaults.Devices != 3 || saved.MaxAccounts != 4 {
		t.Fatalf("the policy handed to SaveLimits: %+v", saved)
	}
	if got := o.Accounts.Policy(); got.Defaults.Notes != 7 || got.Defaults.Devices != 3 || got.MaxAccounts != 4 {
		t.Fatalf("the policy in force after SetPolicy: %+v", got)
	}
	if got := o.Accounts.LimitsFor("nobody").Notes; got != 7 {
		t.Fatalf("an account resolves to notes %d, want the default 7", got)
	}

	// the answer shows no notes count: the administration wire is not extended (Assumed 9; ticket L0322 carries it
	// to the catalog, and this guard moves with that decision)
	b, _ := json.Marshal(v)
	if strings.Contains(strings.ToLower(string(b)), "note") {
		t.Fatalf("the policy answer names notes: %s", b)
	}
	for _, typ := range []reflect.Type{reflect.TypeOf(LimitsView{}), reflect.TypeOf(PolicyView{}), reflect.TypeOf(AccountView{}), reflect.TypeOf(Request{})} {
		for i := 0; i < typ.NumField(); i++ {
			f := typ.Field(i)
			if strings.Contains(strings.ToLower(f.Name+" "+f.Tag.Get("json")), "note") {
				t.Fatalf("%s.%s is a notes member on the administration wire", typ.Name(), f.Name)
			}
		}
	}
}
