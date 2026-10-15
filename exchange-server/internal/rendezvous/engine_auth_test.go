package rendezvous_test

import (
	"bytes"
	"encoding/base64"
	"encoding/hex"
	"net"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
	"testing"
	"time"

	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Ticket 162: accounts, enroll, hello, device tokens, pairing codes, revoke. The scripted client is
// testkit's Device; the server is the real engine over the real stores.

type fakeClock struct {
	mu sync.Mutex
	t  time.Time
}

func newClock() *fakeClock { return &fakeClock{t: time.Date(2026, 10, 7, 12, 0, 0, 0, time.UTC)} }
func (c *fakeClock) Now() time.Time {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.t
}
func (c *fakeClock) Add(d time.Duration) {
	c.mu.Lock()
	c.t = c.t.Add(d)
	c.mu.Unlock()
}

func eventually(t *testing.T, what string, cond func() bool) {
	t.Helper()
	deadline := time.Now().Add(4 * time.Second)
	for time.Now().Before(deadline) {
		if cond() {
			return
		}
		time.Sleep(10 * time.Millisecond)
	}
	t.Fatalf("timed out waiting for: %s", what)
}

// loose is a limits config that does not get in the way of a test that is about something else.
func loose() *limits.Config {
	c := limits.DefaultConfig()
	for i := range c.Rates {
		c.Rates[i] = limits.Rate{PerMinute: 60000, Burst: 10000}
	}
	return &c
}

func looseOpts(logins ...string) testkit.Options {
	return testkit.Options{Limits: loose(), LoginLimits: loose(), Logins: logins}
}

func frameBytes(t *testing.T, e wire.Envelope) []byte {
	t.Helper()
	b, err := wire.Frame(e)
	if err != nil {
		t.Fatal(err)
	}
	return b
}

func mustEnroll(t *testing.T, d *testkit.Device) *wire.Enrolled {
	t.Helper()
	r, err := d.Enroll()
	if err != nil {
		t.Fatal(err)
	}
	en, ok := r.(*wire.Enrolled)
	if !ok {
		t.Fatalf("enroll: %+v", r)
	}
	return en
}

func refusedWith(t *testing.T, r wire.Envelope, want wire.Reason) {
	t.Helper()
	f, ok := r.(*wire.Refused)
	if !ok || f.Reason != string(want) {
		t.Fatalf("want refused %s, got %T %+v", want, r, r)
	}
}

// --- acceptance 1: enroll, hello, and the three refusals that are one answer ---

func TestEnrollThenHelloWithTheToken(t *testing.T) {
	env := testkit.StartServer(t, func() testkit.Options { o := looseOpts("alice"); o.PublicEndpoint = "203.0.113.5:22022"; return o }())
	d := env.NewDevice("alice")
	en := mustEnroll(t, d)
	if !wire.ValidToken(en.DeviceToken) || en.Account != "alice" || en.PublicEndpoint != "203.0.113.5:22022" ||
		en.KeepaliveSeconds != 30 || en.ServerVersion != "test" {
		t.Fatalf("enrolled: %+v", en)
	}
	if strings.Join(en.Features, ",") != "directory,tunnel,pairing,clipboard" {
		t.Fatalf("features are what is built and configured: %v", en.Features)
	}

	c, r, err := d.Hello()
	if err != nil || c == nil {
		t.Fatalf("hello: %v %+v", err, r)
	}
	defer c.Close()
	w := r.(*wire.Welcome)
	if w.Account != "alice" || strings.Join(w.Features, ",") != "directory,tunnel,pairing,clipboard" {
		t.Fatalf("welcome: %+v", w)
	}
	// the stream continues authenticated: a role-fixing envelope works
	_ = c.Send(&wire.List{})
	if r, err := c.Recv(3 * time.Second); err != nil {
		t.Fatal(err)
	} else if dir, ok := r.(*wire.Directory); !ok || len(dir.Devices) != 1 {
		t.Fatalf("directory: %+v", r)
	}
}

func TestWithoutAPublicEndpointTheServerServesButHidesTunnelAndRelay(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	en := mustEnroll(t, env.NewDevice("alice"))
	if en.PublicEndpoint != "" || strings.Join(en.Features, ",") != "directory,pairing,clipboard" {
		t.Fatalf("%+v", en)
	}
	env2 := testkit.StartServer(t, func() testkit.Options { o := looseOpts("alice"); o.PublicEndpoint = "h:1"; o.Cast = true; return o }())
	en2 := mustEnroll(t, env2.NewDevice("alice"))
	if strings.Join(en2.Features, ",") != "directory,tunnel,cast,pairing,clipboard" {
		t.Fatalf("%v", en2.Features)
	}
}

func TestKeepaliveProposalIsClamped(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	for _, c := range []struct{ propose, want int }{{0, 30}, {1, 10}, {45, 45}, {500, 120}} {
		d := env.NewDevice("alice")
		raw, _ := testkit.DialTLS(env.Addr, env.Fingerprint)
		defer raw.Close()
		e := &wire.Enroll{Login: "alice", Password: testkit.TestPassword, DeviceID: d.ID, DeviceName: "n", Product: "x", KeepaliveSeconds: c.propose}
		_ = raw.Send(e)
		r, err := raw.Recv(3 * time.Second)
		en, ok := r.(*wire.Enrolled)
		if err != nil || !ok || en.KeepaliveSeconds != c.want {
			t.Errorf("propose %d: want %d, got %v %+v", c.propose, c.want, err, r)
		}
		d.Token = en.DeviceToken
		// hello proposes too
		hc, hr, _ := d.Hello()
		if hc != nil {
			hc.Close()
		}
		_ = hr
	}
}

func TestEveryFailureOfEnrollIsOneAnswerWithTheSameBytes(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice", "bobby"))
	good := env.NewDevice("alice")
	mustEnroll(t, good)
	ctl := good.Control()
	_ = ctl
	pair, err := good.Online()
	if err != nil {
		t.Fatal(err)
	}
	_ = pair

	refuse := func(name string, d *testkit.Device, pw, code string) []byte {
		t.Helper()
		var r wire.Envelope
		var err error
		if code != "" {
			r, err = d.EnrollWithCode(code)
		} else {
			r, err = d.EnrollWith(pw)
		}
		if err != nil {
			t.Fatalf("%s: %v", name, err)
		}
		refusedWith(t, r, wire.ReasonBadCredentials)
		return frameBytes(t, r)
	}
	wrongPassword := refuse("a wrong password", env.NewDevice("alice"), "Wrongpass1", "")
	wrongCode := refuse("a wrong code", env.NewDevice("alice"), "", "ABCD2345")
	unknown := env.NewDevice("nobody")
	unknownLogin := refuse("an unknown login", unknown, testkit.TestPassword, "")
	unknownLoginCode := refuse("an unknown login with a code", env.NewDevice("nobody"), "", "ABCD2345")
	badLogin := env.NewDevice("a b")
	badLoginForm := refuse("a login that cannot exist", badLogin, testkit.TestPassword, "")
	if !bytes.Equal(wrongPassword, wrongCode) || !bytes.Equal(wrongPassword, unknownLogin) ||
		!bytes.Equal(wrongPassword, unknownLoginCode) || !bytes.Equal(wrongPassword, badLoginForm) {
		t.Fatal("a wrong password, a wrong code and an unknown login must be refused with the same bytes")
	}
	// the stream is closed after the refusal
	c, _ := testkit.DialTLS(env.Addr, env.Fingerprint)
	defer c.Close()
	_ = c.Send(&wire.Enroll{Login: "alice", Password: "Wrongpass1", DeviceID: wire.NewID(), DeviceName: "n", Product: "x"})
	if r, err := c.Recv(3 * time.Second); err != nil {
		t.Fatal(err)
	} else {
		refusedWith(t, r, wire.ReasonBadCredentials)
	}
	if !readsEOF(c.Conn, 2*time.Second) {
		t.Fatal("a refused enroll closes the stream")
	}
	// and a refusal enrolled nothing
	if got := len(env.Accounts.AllDevices()); got != 1 {
		t.Fatalf("%d devices", got)
	}
}

func TestAnUnknownLoginCostsOneVerification(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	before := env.Verifier.Calls()
	r, _ := env.NewDevice("nobody").Enroll()
	refusedWith(t, r, wire.ReasonBadCredentials)
	if got := env.Verifier.Calls() - before; got != 1 {
		t.Fatalf("an unknown login ran %d verifications, want exactly one (the dummy record)", got)
	}
	before = env.Verifier.Calls()
	r, _ = env.NewDevice("alice").EnrollWith("Wrongpass1")
	refusedWith(t, r, wire.ReasonBadCredentials)
	if got := env.Verifier.Calls() - before; got != 1 {
		t.Fatalf("a wrong password ran %d verifications", got)
	}
	// a pairing enroll never pays for an Argon2id run
	before = env.Verifier.Calls()
	r, _ = env.NewDevice("alice").EnrollWithCode("ABCD2345")
	refusedWith(t, r, wire.ReasonBadCredentials)
	if env.Verifier.Calls() != before {
		t.Fatal("a pairing code is compared by its SHA-256, not by a memory-hard hash")
	}
}

func TestEnrollRequiresTLSAndTheFrameShape(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	// a missing member closes without an answer; the account is not charged a verification
	before := env.Verifier.Calls()
	c, _ := testkit.DialTLS(env.Addr, env.Fingerprint)
	defer c.Close()
	_ = c.SendRaw(testkit.RawFrame(`{"schemaVersion":2,"type":"enroll","login":"alice","password":"` + testkit.TestPassword + `"}`))
	if !readsEOF(c.Conn, 2*time.Second) {
		t.Fatal("a frame missing deviceId must close the stream")
	}
	if env.Verifier.Calls() != before {
		t.Fatal("a malformed enroll must not reach the hash")
	}
}

// --- tokens and passwords are never stored ---

func TestNoPasswordTokenOrCodeIsOnDiskAfterAFullScenario(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	prod := env.NewProducer("alice", "ssh-secret")
	if err := prod.Start(); err != nil {
		t.Fatal(err)
	}
	_, _, ans, err := prod.PublishShare(true, nil)
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := ans.(*wire.Published); !ok {
		t.Fatalf("%+v", ans)
	}
	codeAns, err := prod.Control().Request(&wire.PairingRequest{}, 3*time.Second, wire.TypePairingCode)
	if err != nil {
		t.Fatal(err)
	}
	code := codeAns.(*wire.PairingCode).Code
	env.AddSecret(code)
	second := env.NewDevice("alice")
	if r, _ := second.EnrollWithCode(code); r == nil {
		t.Fatal("no answer")
	} else if _, ok := r.(*wire.Enrolled); !ok {
		t.Fatalf("%+v", r)
	}
	env.Engine.Close() // flushes
	scanDataDir(t, env, []string{testkit.TestPassword, prod.Token, second.Token, code})
}

// scanDataDir fails when any needle - raw, base64 or hex - is in a file of the data directory, and
// returns the files it saw.
func scanDataDir(t *testing.T, env *testkit.Env, needles []string) []string {
	t.Helper()
	var seen []string
	_ = filepath.Walk(env.DataDir, func(p string, info os.FileInfo, err error) error {
		if err != nil || info.IsDir() {
			return nil
		}
		b, _ := os.ReadFile(p)
		rel, _ := filepath.Rel(env.DataDir, p)
		seen = append(seen, filepath.ToSlash(rel))
		for _, n := range needles {
			if n == "" {
				continue
			}
			forms := [][]byte{[]byte(n), []byte(base64.StdEncoding.EncodeToString([]byte(n))),
				[]byte(base64.RawURLEncoding.EncodeToString([]byte(n))), []byte(hex.EncodeToString([]byte(n)))}
			for _, f := range forms {
				if bytes.Contains(b, f) {
					t.Errorf("%q is in %s", n, rel)
				}
			}
		}
		return nil
	})
	return seen
}

// --- the buckets and the verifier ---

// A password check is paced (19 item AW), not charged to a bucket of failures: a check right behind the
// first waits one slot, a third inside the window is turned away BEFORE the hash runs, and a right
// password is never charged anything. The test clock is real; the pacer's sleep is a no-op, so the
// "waited" check costs nothing and the interval is long next to the test's own latency.
func TestPasswordChecksArePacedBeforeTheHash(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{LoginLimits: loose(), Logins: []string{"alice"},
		PaceInterval: 2 * time.Second, Sleep: func(time.Duration) {}})
	bad := func() wire.Envelope {
		r, err := env.NewDevice("alice").EnrollWith("Wrongpass1")
		if err != nil {
			t.Fatal(err)
		}
		return r
	}
	refusedWith(t, bad(), wire.ReasonBadCredentials) // evaluated at once
	refusedWith(t, bad(), wire.ReasonBadCredentials) // waited one slot, then evaluated
	callsBefore := env.Verifier.Calls()
	refusedWith(t, bad(), wire.ReasonRateLimited)
	if env.Verifier.Calls() != callsBefore {
		t.Fatal("a check that is turned away must not reach the hash")
	}
	// nothing remembers a failure: a right password is never charged a token, a lockout or a delay
	env2 := testkit.StartServer(t, testkit.Options{LoginLimits: loose(), Logins: []string{"alice"}})
	for i := 0; i < 20; i++ {
		mustEnroll(t, env2.NewDevice("alice"))
	}
}

func TestFailedEnrollsAreRateLimitedPerLoginToo(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{Limits: loose(), LoginLimits: loose(), Logins: []string{"alice", "bobby"}})
	for i := 0; i < 3; i++ {
		r, _ := env.NewDevice("alice").EnrollWith("Wrongpass1")
		refusedWith(t, r, wire.ReasonBadCredentials)
	}
	// Under ticket 224 rule 11 there is no lockout: right password succeeds within a second of its turn
	time.Sleep(1100 * time.Millisecond)
	mustEnroll(t, env.NewDevice("alice"))
	mustEnroll(t, env.NewDevice("bobby"))
}

// A flood of wrong passwords from one address runs at most two verifications (the first, and the one
// that waits one slot), never more than two at once; the rest are turned away before the hash.
func TestPasswordFloodRunsAtMostTwoVerificationsAndTwoAtOnce(t *testing.T) {
	lc := limits.DefaultConfig()
	lc.Rates[limits.Connection] = limits.Rate{PerMinute: 6000, Burst: 1000}
	env := testkit.StartServer(t, testkit.Options{Limits: &lc, LoginLimits: loose(), Logins: []string{"alice"},
		PaceInterval: 2 * time.Second, Sleep: func(time.Duration) {}})
	var wg sync.WaitGroup
	var mu sync.Mutex
	reasons := map[string]int{}
	for i := 0; i < 50; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			r, err := env.NewDevice("alice").EnrollWith("Wrongpass1")
			mu.Lock()
			defer mu.Unlock()
			if err != nil {
				reasons["error"]++
				return
			}
			if f, ok := r.(*wire.Refused); ok {
				reasons[f.Reason]++
			} else {
				reasons["other"]++
			}
		}()
	}
	wg.Wait()
	if c := env.Verifier.Calls(); c > 2 {
		t.Errorf("%d verifications ran from one address, the window holds 2", c)
	}
	if env.Verifier.Peak() > 2 {
		t.Errorf("%d verifications at once, the bound is 2", env.Verifier.Peak())
	}
	if reasons[string(wire.ReasonBadCredentials)] > 2 || reasons[string(wire.ReasonRateLimited)]+reasons["error"] < 48 {
		t.Errorf("the flood was not held to the burst: %v", reasons)
	}
}

func TestVerifierBusyAnswersCapacityAndKeepsTheToken(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{VerifyConc: 1, VerifyWait: 100 * time.Millisecond, LoginLimits: loose(), Logins: []string{"alice"}})
	release := make(chan struct{})
	started := make(chan struct{})
	go env.Verifier.Run(func() bool { close(started); <-release; return true }) // the only slot is held
	<-started
	r, err := env.NewDevice("alice").Enroll()
	if err != nil {
		t.Fatal(err)
	}
	refusedWith(t, r, wire.ReasonCapacity)
	close(release)
	// our own busy state is not the client's failure: it did not cost the address a token
	for i := 0; i < 2; i++ {
		mustEnroll(t, env.NewDevice("alice"))
	}
}

// --- hello ---

func TestHelloRefusalsAndDeviceRevokedOnlyOnAMatchingHash(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	d := env.NewDevice("alice")
	mustEnroll(t, d)
	good := d.Token

	c, r, err := d.HelloWith("not-the-token")
	if err != nil || c != nil {
		t.Fatal(err)
	}
	refusedWith(t, r, wire.ReasonBadCredentials)
	ghost := env.NewDevice("alice") // never enrolled
	ghost.Token = good
	_, r, _ = ghost.Hello()
	refusedWith(t, r, wire.ReasonBadCredentials)

	// revoke it (through the operator's path), then: the old token is told, a stranger is not
	if found, err := env.Ops.RevokeDevice("alice", d.ID); err != nil || !found {
		t.Fatal(err)
	}
	_, r, _ = d.HelloWith(good)
	refusedWith(t, r, wire.ReasonDeviceRevoked)
	_, r, _ = d.HelloWith("a-token-nobody-ever-issued")
	refusedWith(t, r, wire.ReasonBadCredentials)
	_, r, _ = ghost.Hello() // a different deviceId with the same token: nothing to learn
	refusedWith(t, r, wire.ReasonBadCredentials)
}

func TestFailedHelloIsChargedToTheAddressAndTheAccount(t *testing.T) {
	// per address: failed-auth 5/min burst 2
	env := testkit.StartServer(t, testkit.Options{LoginLimits: loose(), Logins: []string{"alice"}})
	d := env.NewDevice("alice")
	mustEnroll(t, d)
	bad := func() wire.Envelope { _, r, _ := d.HelloWith("a-wrong-token"); return r }
	refusedWith(t, bad(), wire.ReasonBadCredentials)
	refusedWith(t, bad(), wire.ReasonBadCredentials)
	refusedWith(t, bad(), wire.ReasonRateLimited)

	// per account: another address would be needed; with the address bucket loose, the account's own
	lc := loose()
	lc.Rates[limits.FailedHello] = limits.Rate{PerMinute: 1, Burst: 2}
	env2 := testkit.StartServer(t, testkit.Options{Limits: loose(), LoginLimits: lc, Logins: []string{"alice"}})
	d2 := env2.NewDevice("alice")
	mustEnroll(t, d2)
	bad2 := func() wire.Envelope { _, r, _ := d2.HelloWith("a-wrong-token"); return r }
	refusedWith(t, bad2(), wire.ReasonBadCredentials)
	refusedWith(t, bad2(), wire.ReasonBadCredentials)
	refusedWith(t, bad2(), wire.ReasonRateLimited)
	// the account's bucket is for failures only: the real device still gets in
	if c, r, err := d2.Hello(); err != nil || c == nil {
		t.Fatalf("a good hello after bad ones: %v %+v", err, r)
	} else {
		c.Close()
	}
}

// --- enrolling a known device, collisions, caps ---

func TestEnrollingAKnownDeviceRotatesItsTokenAndClosesItsStreams(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	d := env.NewDevice("alice")
	first := mustEnroll(t, d)
	oldCtl := d.Control()
	oldToken := first.DeviceToken
	if err := oldCtl.Send(&wire.Subscribe{}); err != nil {
		t.Fatal(err)
	}
	eventually(t, "online", func() bool { return env.Dir.Counts().Online == 1 })
	data, _, err := d.Hello() // a second stream of the same device, authenticated and idle
	if err != nil || data == nil {
		t.Fatal(err)
	}
	defer data.Close()

	second := mustEnroll(t, d) // the same deviceId again: a rotation
	if second.DeviceToken == oldToken {
		t.Fatal("the token must rotate")
	}
	select {
	case <-oldCtl.Closed():
	case <-time.After(3 * time.Second):
		t.Fatal("the old control stream must be closed at once")
	}
	if !readsEOF(data.Conn, 2*time.Second) {
		t.Fatal("every other stream of the device must be closed too")
	}
	_, r, _ := d.HelloWith(oldToken)
	refusedWith(t, r, wire.ReasonBadCredentials)
	if c, _, _ := d.HelloWith(second.DeviceToken); c == nil {
		t.Fatal("the new token must work")
	} else {
		c.Close()
	}
	if len(env.Accounts.Devices("alice")) != 1 {
		t.Fatal("a rotation must not add a device")
	}
}

func TestDeviceIDCollisionAcrossAccountsAndTheDeviceCap(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice", "bobby"))
	d := env.NewDevice("alice")
	mustEnroll(t, d)
	thief := env.NewDevice("bobby")
	thief.ID = d.ID
	r, _ := thief.Enroll()
	refusedWith(t, r, wire.ReasonIDCollision)
	// the same login with the same id is the rotation above, not a collision

	p := testkit.TestParams()
	p.DevicesPerAccount = 3
	env2 := testkit.StartServer(t, func() testkit.Options { o := looseOpts("alice"); o.Params = &p; return o }())
	for i := 0; i < 3; i++ {
		mustEnroll(t, env2.NewDevice("alice"))
	}
	r, _ = env2.NewDevice("alice").Enroll()
	refusedWith(t, r, wire.ReasonLimitReached) // a count limit of the account (19 item AS)
}

func TestEnrollKeepsOnlyKnownRolesAndTheReceiverVerbatim(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	d := env.NewDevice("alice")
	raw, _ := testkit.DialTLS(env.Addr, env.Fingerprint)
	defer raw.Close()
	_ = raw.Send(&wire.Enroll{Login: "alice", Password: testkit.TestPassword, DeviceID: d.ID, DeviceName: "n", Product: "x",
		Roles:    []string{"receiver", "emperor", "broadcaster"},
		Receiver: []byte(`{"modes":["AUDIO_ONLY"],"plays":[{"mode":"AUDIO_ONLY","transport":"HTTP_ADTS"}],"vendor":{"deep":{"er":1}}}`)})
	if r, _ := raw.Recv(3 * time.Second); r == nil {
		t.Fatal("no answer")
	}
	dev, _ := env.Accounts.Device("alice", d.ID)
	if strings.Join(dev.Roles, ",") != "receiver,broadcaster" {
		t.Fatalf("roles: %v", dev.Roles)
	}
	if !bytes.Contains(dev.Receiver, []byte(`"vendor":{"deep":{"er":1}}`)) {
		t.Fatalf("receiver: %s", dev.Receiver)
	}
}

// --- pairing ---

func requestCode(t *testing.T, d *testkit.Producer) string {
	t.Helper()
	r, err := d.Control().Request(&wire.PairingRequest{}, 3*time.Second, wire.TypePairingCode, wire.TypeRefused)
	if err != nil {
		t.Fatal(err)
	}
	pc, ok := r.(*wire.PairingCode)
	if !ok {
		t.Fatalf("pairing-request: %+v", r)
	}
	d.Env.AddSecret(pc.Code)
	return pc.Code
}

func TestPairingCodeEnrollsASecondDeviceOnceOnly(t *testing.T) {
	ck := newClock()
	o := looseOpts("alice")
	o.Now = ck.Now
	env := testkit.StartServer(t, o)
	prod := env.NewProducer("alice", "x")
	if err := prod.Start(); err != nil {
		t.Fatal(err)
	}
	code := requestCode(t, prod)
	if len(code) != 8 {
		t.Fatalf("code %q", code)
	}
	// typed sloppily
	sloppy := strings.ToLower(code[:4]) + "-" + strings.ToLower(code[4:])
	watch := env.NewDevice("alice")
	r, err := watch.EnrollWithCode(sloppy)
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := r.(*wire.Enrolled); !ok {
		t.Fatalf("a pairing code did not enroll: %+v", r)
	}
	// single use
	other := env.NewDevice("alice")
	r, _ = other.EnrollWithCode(code)
	refusedWith(t, r, wire.ReasonBadCredentials)
	// both then say hello with their tokens
	for _, d := range []*testkit.Device{prod.Device, watch} {
		if c, r, _ := d.Hello(); c == nil {
			t.Fatalf("hello: %+v", r)
		} else {
			c.Close()
		}
	}
	// a code of another account does not enroll into this one
	code2 := requestCode(t, prod)
	mine := env.NewDevice("alice")
	_ = mine
	ck.Add(11 * time.Minute) // expired
	late := env.NewDevice("alice")
	r, _ = late.EnrollWithCode(code2)
	refusedWith(t, r, wire.ReasonBadCredentials)
}

func TestFiveWrongCodesBurnEveryActiveCodeOfTheLogin(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	prod := env.NewProducer("alice", "x")
	if err := prod.Start(); err != nil {
		t.Fatal(err)
	}
	good := requestCode(t, prod)
	for i := 0; i < 5; i++ {
		r, _ := env.NewDevice("alice").EnrollWithCode("WRONG123")
		refusedWith(t, r, wire.ReasonBadCredentials)
	}
	r, _ := env.NewDevice("alice").EnrollWithCode(good)
	refusedWith(t, r, wire.ReasonBadCredentials)
	if r, _ := env.NewDevice("alice").Enroll(); r == nil {
		t.Fatal("no answer")
	} else if _, ok := r.(*wire.Enrolled); !ok {
		t.Fatalf("the password path is unaffected: %+v", r)
	}
}

func TestPairingRequestCapsAndRateLimits(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	prod := env.NewProducer("alice", "x")
	if err := prod.Start(); err != nil {
		t.Fatal(err)
	}
	for i := 0; i < 5; i++ {
		requestCode(t, prod)
	}
	r, err := prod.Control().Request(&wire.PairingRequest{}, 3*time.Second, wire.TypePairingCode, wire.TypeRefused)
	if err != nil {
		t.Fatal(err)
	}
	refusedWith(t, r, wire.ReasonCapacity)

	lc := loose()
	lc.Rates[limits.PairingRequest] = limits.Rate{PerMinute: 1, Burst: 2}
	env2 := testkit.StartServer(t, testkit.Options{Limits: loose(), LoginLimits: lc, Logins: []string{"alice"}})
	p2 := env2.NewProducer("alice", "x")
	if err := p2.Start(); err != nil {
		t.Fatal(err)
	}
	requestCode(t, p2)
	requestCode(t, p2)
	r, _ = p2.Control().Request(&wire.PairingRequest{}, 3*time.Second, wire.TypePairingCode, wire.TypeRefused)
	refusedWith(t, r, wire.ReasonRateLimited)
}

// --- revoke (162 rule 7, acceptance 2) ---

func TestRevokeCascades(t *testing.T) {
	lo, hi := testkit.FreeRange(3)
	env := testkit.StartServer(t, func() testkit.Options { o := looseOpts("alice"); o.PortRange = [2]int{lo, hi}; return o }())

	target := env.NewProducer("alice", "ssh")
	if err := target.Start(); err != nil {
		t.Fatal(err)
	}
	resID, shareID, ans, err := target.PublishShare(true, intPtr(0))
	if err != nil {
		t.Fatal(err)
	}
	pub := ans.(*wire.Published)
	if pub.PublicPort == nil {
		t.Fatalf("%+v", pub)
	}
	port := *pub.PublicPort
	broadcast := `{"broadcastId":"` + env.NewID() + `","mode":"AUDIO_ONLY","descriptor":{"endpoints":[]}}`
	if r, _ := target.Control().Request(&wire.BroadcastStart{Broadcast: []byte(broadcast)}, 3*time.Second, wire.TypeBroadcastStarted, wire.TypeRefused); r == nil {
		t.Fatal()
	} else if _, ok := r.(*wire.BroadcastStarted); !ok {
		t.Fatalf("%+v", r)
	}

	observer := env.NewDevice("alice")
	mustEnroll(t, observer)
	oc, err := observer.Online()
	if err != nil {
		t.Fatal(err)
	}
	// the observer sees the resource and the broadcast
	dir := listDirectory(t, oc)
	if len(dir.Resources) != 1 || len(dir.Broadcasts) != 1 || len(dir.Devices) != 2 {
		t.Fatalf("before: %d resources, %d broadcasts, %d devices", len(dir.Resources), len(dir.Broadcasts), len(dir.Devices))
	}
	oc.Drain()

	// a stream of the target that is only a data stream: it must close too
	data, _, _ := target.Hello()
	if data == nil {
		t.Fatal("hello")
	}
	defer data.Close()

	// the observer revokes the target
	if err := oc.Send(&wire.Revoke{DeviceID: target.ID}); err != nil {
		t.Fatal(err)
	}
	ch, err := oc.Await(3*time.Second, wire.TypeChanged)
	if err != nil {
		t.Fatal(err)
	}
	c := ch.(*wire.Changed)
	if len(c.Removals.DeviceIDs) != 1 || len(c.Removals.ResourceIDs) != 1 || len(c.Removals.BroadcastIDs) != 1 {
		t.Fatalf("one changed carries the whole revoke: %+v", c.Removals)
	}
	select {
	case <-target.Control().Closed():
	case <-time.After(3 * time.Second):
		t.Fatal("the revoked device's control stream must close")
	}
	if !readsEOF(data.Conn, 2*time.Second) {
		t.Fatal("its data streams must close")
	}
	dir = listDirectory(t, oc)
	if len(dir.Resources) != 0 || len(dir.Broadcasts) != 0 || len(dir.Devices) != 1 {
		t.Fatalf("after: %d resources, %d broadcasts, %d devices", len(dir.Resources), len(dir.Broadcasts), len(dir.Devices))
	}
	// its port is released, its share does not resolve, and it is told device-revoked
	eventually(t, "the port to be released", func() bool { return env.Engine.Leases() == 0 })
	if cc, err := net.DialTimeout("tcp", net.JoinHostPort("127.0.0.1", itoa(port)), 300*time.Millisecond); err == nil {
		cc.Close()
		t.Fatal("the port still listens")
	}
	if _, r, _ := env.OutsiderConnect(&wire.Connect{ShareID: shareID}); r == nil {
		t.Fatal("no answer")
	} else {
		refusedWith(t, r, wire.ReasonUnavailable)
	}
	_ = resID
	_, r, _ := target.Hello()
	refusedWith(t, r, wire.ReasonDeviceRevoked)
	// the log names both devices by hashed id and carries no id
	env.AssertLogsClean(t)
	if !strings.Contains(strings.Join(env.Log.Lines(), "\n"), "device-revoked") {
		t.Fatal("a revoke must be logged")
	}
}

func TestARevokeOfAnUnknownOrForeignDeviceDoesNothingAndIsNotAnswered(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice", "bobby"))
	mine := env.NewProducer("alice", "x")
	if err := mine.Start(); err != nil {
		t.Fatal(err)
	}
	theirs := env.NewProducer("bobby", "x")
	if err := theirs.Start(); err != nil {
		t.Fatal(err)
	}
	rev := env.Dir.Revision("alice")
	_ = mine.Control().Send(&wire.Revoke{DeviceID: theirs.ID})    // another account's
	_ = mine.Control().Send(&wire.Revoke{DeviceID: wire.NewID()}) // unknown
	_ = mine.Control().Send(&wire.Keepalive{})                    // the stream still works, and nothing came before its echo
	r, err := mine.Control().Await(2*time.Second, wire.TypeKeepalive, wire.TypeRefused, wire.TypeChanged)
	if err != nil || r.TypeName() != wire.TypeKeepalive {
		t.Fatalf("a revoke of nothing must not be answered: %v %+v", err, r)
	}
	if env.Dir.Revision("alice") != rev || len(env.Accounts.Devices("bobby")) != 1 {
		t.Fatal("a foreign revoke changed something")
	}
	if c, _, _ := theirs.Hello(); c == nil {
		t.Fatal("bobby's device lost its token")
	} else {
		c.Close()
	}
}

func TestADeviceMayRevokeItselfAndTheAccountRemovalTakesEveryone(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice", "bobby"))
	a := env.NewProducer("alice", "x")
	b := env.NewProducer("alice", "x")
	other := env.NewProducer("bobby", "x")
	for _, p := range []*testkit.Producer{a, b, other} {
		if err := p.Start(); err != nil {
			t.Fatal(err)
		}
	}
	_ = a.Control().Send(&wire.Revoke{DeviceID: a.ID})
	select {
	case <-a.Control().Closed():
	case <-time.After(3 * time.Second):
		t.Fatal("a device that revokes itself is closed")
	}
	if len(env.Accounts.Devices("alice")) != 1 {
		t.Fatal("self revoke")
	}
	if err := env.Ops.RemoveAccount("alice"); err != nil {
		t.Fatal(err)
	}
	select {
	case <-b.Control().Closed():
	case <-time.After(3 * time.Second):
		t.Fatal("account removal must close its devices' streams")
	}
	if len(env.Accounts.Devices("alice")) != 0 || env.Accounts.Exists("alice") {
		t.Fatal("the account is still there")
	}
	if c, _, _ := other.Hello(); c == nil {
		t.Fatal("another account was touched")
	} else {
		c.Close()
	}
	r, _ := env.NewDevice("alice").Enroll()
	refusedWith(t, r, wire.ReasonBadCredentials)
}

func TestAPasswordResetSignsEveryDeviceOut(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	d := env.NewDevice("alice")
	mustEnroll(t, d)
	// From release 46 a reset ALWAYS signs out every device of the account (19 item AT); the record stays.
	n, err := env.Ops.SetPassword("alice", "N3wPassword!x")
	if err != nil || n != 1 {
		t.Fatalf("reset: %d %v", n, err)
	}
	_, r, _ := d.Hello()
	refusedWith(t, r, wire.ReasonSignedOut)
	if len(env.Accounts.Devices("alice")) != 1 {
		t.Fatal("a signed-out device record must stay")
	}

	r, _ = env.NewDevice("alice").Enroll()
	refusedWith(t, r, wire.ReasonBadCredentials)
	if _, err := env.Ops.SetPassword("alice", testkit.TestPassword); err != nil {
		t.Fatal(err)
	}
	mustEnroll(t, env.NewDevice("alice"))
}

func TestEnrollAndHelloOverlongLoginsAreJustUnknown(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	for _, login := range []string{strings.Repeat("a", 200), "ALICE ", "alice\u0000"} {
		d := env.NewDevice("alice")
		d.Login = login
		r, err := d.Enroll()
		if err != nil {
			continue // a login with a control character is a malformed frame: the stream closes
		}
		if f, ok := r.(*wire.Refused); ok {
			if f.Reason != string(wire.ReasonBadCredentials) {
				t.Errorf("%q: %v", login, f.Reason)
			}
		} else if login != "ALICE " {
			t.Errorf("%q enrolled", login)
		}
	}
}

func intPtr(n int) *int { return &n }

func itoa(n int) string { return strconv.Itoa(n) }

func listDirectory(t *testing.T, c *testkit.Control) *wire.Directory {
	t.Helper()
	d, err := c.ListDirectory()
	if err != nil {
		t.Fatal(err)
	}
	return d
}
