package admin

import (
	"encoding/json"
	"fmt"
	"io"
	"net"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/directory"
	"fms/exchange-server/internal/logx"
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
