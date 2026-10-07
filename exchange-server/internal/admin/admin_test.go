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
	r, _ = call(Request{Op: OpAccountPasswd, Login: "alice", Password: "N3wPassword!x"})
	if !r.OK {
		t.Fatalf("%+v", r)
	}
	r, _ = call(Request{Op: OpDeviceRevoke, Login: "alice", DeviceID: devA})
	if !r.OK {
		t.Fatalf("%+v", r)
	}
	r, _ = call(Request{Op: OpDeviceRevoke, Login: "alice", DeviceID: devA})
	if r.OK || r.Code != CodeNotFound {
		t.Fatalf("a second revoke: %+v", r)
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
	// without signOut: tokens survive
	call(Request{Op: OpAccountPasswd, Login: "alice", Password: "N3wPassword!x"})
	if _, st := o.Accounts.Hello(a1, e1.Token); st != accounts.HelloOK {
		t.Fatal("a password change must leave tokens alone")
	}
	call(Request{Op: OpAccountPasswd, Login: "alice", Password: "N3wPassword!y", SignOut: true})
	if _, st := o.Accounts.Hello(a1, e1.Token); st != accounts.HelloRevoked {
		t.Fatalf("signOut must revoke: %v", st)
	}
	if len(o.Accounts.Devices("alice")) != 0 || len(o.Accounts.Devices("bobby")) != 1 {
		t.Fatal("signOut touched the wrong account")
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
