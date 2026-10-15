package rendezvous_test

import (
	"bufio"
	"bytes"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"net"
	"net/http"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"sync"
	"testing"
	"time"

	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/rendezvous"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Ticket 167: the server's promises about what it stores, what it logs and what it will carry are
// tests. This file holds the data-directory manifest test (rule 2), the memory-only tests (rule 3),
// and the exhaustion tests of the limits of rule 6 that no other file already holds.

// --- the manifest (rule 2) ---

// dataDirViolations lists what is wrong with a data directory: a file that is not on the allowed list,
// and every needle found in a file - as raw bytes and as base64 (standard and URL form) and hex.
// logNeedles are searched only in files under logs/ besides needles (a deviceId or a resourceId is
// legitimately on disk in devices.json and directory.json, but a log names an id only by its hash).
func dataDirViolations(dir string, allowed map[string]bool, needles, logNeedles []string) []string {
	var out []string
	_ = filepath.Walk(dir, func(p string, info os.FileInfo, err error) error {
		if err != nil || info.IsDir() {
			return nil
		}
		rel, _ := filepath.Rel(dir, p)
		rel = filepath.ToSlash(rel)
		if !allowed[rel] && !(strings.HasPrefix(rel, "logs/") && logx.IsDayFile(strings.TrimPrefix(rel, "logs/"))) {
			out = append(out, "unexpected file: "+rel)
		}
		b, _ := os.ReadFile(p)
		check := needles
		if strings.HasPrefix(rel, "logs/") {
			check = append(append([]string(nil), needles...), logNeedles...)
		}
		for _, n := range check {
			if n == "" {
				continue
			}
			for _, form := range [][]byte{[]byte(n), []byte(base64.StdEncoding.EncodeToString([]byte(n))),
				[]byte(base64.RawURLEncoding.EncodeToString([]byte(n))), []byte(hex.EncodeToString([]byte(n)))} {
				if bytes.Contains(b, form) {
					out = append(out, "a secret is in "+rel)
					break
				}
			}
		}
		return nil
	})
	sort.Strings(out)
	return out
}

// the files a deployment may hold (7.9 and ticket 182 L-1): the stores, the TLS identity and the log
// with its one rotation. Never content, never a broadcast, never a tunnel, never a pairing code.
var manifestAllowed = map[string]bool{
	"accounts.json": true, "devices.json": true, "directory.json": true,
	"invitations.json": true, "grants.json": true,
	"tls/cert.pem": true, "tls/key.pem": true,
	"logs/exchange.log": true, "logs/exchange.log.1": true,
}

// the manifest test must be able to fail: a file that should not exist, and a secret planted in a
// file that may exist, in every form the scan knows - the mutation check of acceptance 1
func TestTheManifestScanCatchesWhatItIsMeantToCatch(t *testing.T) {
	const access = "FMSSFTP2.test-secret-access-string"
	clean := t.TempDir()
	if err := os.MkdirAll(filepath.Join(clean, "logs"), 0o700); err != nil {
		t.Fatal(err)
	}
	for _, f := range []string{"accounts.json", "devices.json", "directory.json", "logs/exchange.log"} {
		if err := os.WriteFile(filepath.Join(clean, filepath.FromSlash(f)), []byte(`{"records":[]}`), 0o600); err != nil {
			t.Fatal(err)
		}
	}
	if v := dataDirViolations(clean, manifestAllowed, []string{access}, nil); len(v) != 0 {
		t.Fatalf("a clean directory has violations: %v", v)
	}
	forms := map[string][]byte{
		"raw": []byte(access), "base64": []byte(base64.StdEncoding.EncodeToString([]byte(access))),
		"base64url": []byte(base64.RawURLEncoding.EncodeToString([]byte(access))), "hex": []byte(hex.EncodeToString([]byte(access))),
	}
	for name, planted := range forms {
		dir := t.TempDir()
		_ = os.MkdirAll(filepath.Join(dir, "logs"), 0o700)
		_ = os.WriteFile(filepath.Join(dir, "directory.json"), append([]byte(`{"x":"`), append(planted, '"', '}')...), 0o600)
		if v := dataDirViolations(dir, manifestAllowed, []string{access}, nil); len(v) == 0 {
			t.Errorf("`access` written to directory.json as %s was not caught", name)
		}
	}
	// a file nobody listed
	dir := t.TempDir()
	_ = os.WriteFile(filepath.Join(dir, "broadcast-buffer.bin"), []byte("x"), 0o600)
	if v := dataDirViolations(dir, manifestAllowed, nil, nil); len(v) != 1 || !strings.Contains(v[0], "unexpected file") {
		t.Errorf("an unlisted file must be a violation: %v", v)
	}
	// a log line that names an id in clear: the log needles apply to logs/ only
	dir = t.TempDir()
	_ = os.MkdirAll(filepath.Join(dir, "logs"), 0o700)
	_ = os.WriteFile(filepath.Join(dir, "logs", "exchange.log"), []byte("INFO\tonline\tdevice=AAAAAAAAAAAAAAAAAAAAAA\n"), 0o600)
	_ = os.WriteFile(filepath.Join(dir, "devices.json"), []byte(`{"deviceId":"AAAAAAAAAAAAAAAAAAAAAA"}`), 0o600)
	v := dataDirViolations(dir, manifestAllowed, nil, []string{"AAAAAAAAAAAAAAAAAAAAAA"})
	if len(v) != 1 || !strings.Contains(v[0], "logs/exchange.log") {
		t.Errorf("an id in a log is a violation, in devices.json it is not: %v", v)
	}
}

// the scenario: every message of 161-166 against a server on a temporary directory - enroll by
// password and by code, publish with `access` and `shareId`, a broadcast with an upload and a
// listener, a tunnel by resourceId, shareId and broadcastId, a cast, a revoke, a restart - and then the
// directory is the allowed list and holds no secret the scenario used.
func TestTheDataDirectoryManifestHoldsForEveryMessage(t *testing.T) {
	lo, hi := testkit.FreeRange(4)
	o := looseOpts("alice", "bobby")
	o.PortRange = [2]int{lo, hi}
	o.PublicEndpoint = "203.0.113.5:22022"
	o.WithRelay = true
	o.Cast = true
	o.LogFile = true
	env := testkit.StartServer(t, o)

	const accessSecret = "FMSSFTP2.test-secret-access-string"
	payload := "payload-needle-" + wire.NewID()
	titleNeedle := "title-needle-" + wire.NewID()
	descNeedle := "descriptor-needle-" + wire.NewID()

	prod := env.NewProducer("alice", sshPW)
	var tunnelMu sync.Mutex
	var tunnelIDs []string
	if err := prod.Start(); err != nil {
		t.Fatal(err)
	}
	prod.Control().OnOpen(func(op *wire.Open) {
		tunnelMu.Lock()
		tunnelIDs = append(tunnelIDs, op.TunnelID)
		tunnelMu.Unlock()
		prod.Answer(op)
	})
	resID, shareID, ans, err := prod.PublishShare(true, intPtr(0)) // access, shareId and a third-party port
	if err != nil || ans.TypeName() != wire.TypePublished {
		t.Fatalf("%v %+v", err, ans)
	}
	// a broadcast, its upload, a relay listener
	bid := env.NewID()
	rec := `{"broadcastId":"` + bid + `","mode":"AUDIO_ONLY","title":"` + titleNeedle + `","descriptor":{"url":"` + descNeedle + `"}}`
	if r, err := prod.Control().Request(&wire.BroadcastStart{Broadcast: json.RawMessage(rec)}, 3*time.Second, wire.TypeBroadcastStarted, wire.TypeRefused); err != nil || r.TypeName() != wire.TypeBroadcastStarted {
		t.Fatalf("%v %+v", err, r)
	}
	up, uans, err := prod.Upload(bid, "audio/aac")
	if err != nil || up == nil {
		t.Fatalf("%v %+v", err, uans)
	}
	defer up.Close()
	_ = up.WriteChunk(wire.ChunkJoinable, []byte(payload))
	waitBytesIn(t, env, bid, int64(len(payload)))
	ls := listen(t, env, bid)
	if got, err := ls.ReadFull(len(payload), 3*time.Second); err != nil || string(got) != payload {
		t.Fatalf("the relay must carry the payload: %q %v", got, err)
	}
	// pairing code, and a second device that enrolls with it
	code := requestCode(t, prod)
	second := env.NewDevice("alice")
	if r, _ := second.EnrollWithCode(code); r == nil || r.TypeName() != wire.TypeEnrolled {
		t.Fatalf("%+v", r)
	}
	second.Control().Close()
	sc, err := second.Online()
	if err != nil {
		t.Fatal(err)
	}
	// tunnels: by resourceId (SSH through it), by shareId as an outsider, by broadcastId with a scheme
	cn, _, err := second.Connect(&wire.Connect{ResourceID: resID})
	if err != nil || cn == nil {
		t.Fatalf("connect: %v", err)
	}
	if out, err := testkit.SSHThrough(cn.Conn, sshPW, "x"); err != nil || out != "echo:x" {
		t.Fatalf("%q %v", out, err)
	}
	if oc, r, err := env.OutsiderConnect(&wire.Connect{ShareID: shareID}); err != nil || oc == nil {
		t.Fatalf("outsider: %v %+v", err, r)
	} else {
		oc.Close()
	}
	if bc, r, err := second.Connect(&wire.Connect{BroadcastID: bid, Scheme: "http"}); err != nil || bc == nil {
		t.Fatalf("broadcast tunnel: %v %+v", err, r)
	} else {
		bc.Close()
	}
	// cast: prod offers its broadcast to the second device, which accepts
	_ = prod.Control().Send(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: second.ID})
	if _, err := sc.Await(3*time.Second, wire.TypeCastOffer); err != nil {
		t.Fatal(err)
	}
	_ = sc.Send(&wire.CastAnswer{BroadcastID: bid, Accepted: true})
	if _, err := prod.Control().Await(3*time.Second, wire.TypeCastResult); err != nil {
		t.Fatal(err)
	}
	// a revoke
	other := env.NewDevice("bobby")
	mustEnroll(t, other)
	env.Ops.RevokeDevice("bobby", other.ID)
	otherToken := other.Token
	prodToken, secondToken := prod.Token, second.Token

	// a restart on the same directory: the producer comes back and publishes again
	env.Stop()
	env.Engine.Close()
	o2 := looseOpts("alice", "bobby")
	o2.PortRange = [2]int{lo, hi}
	o2.PublicEndpoint = "203.0.113.5:22022"
	o2.WithRelay = true
	o2.Cast = true
	o2.LogFile = true
	o2.DataDir = env.DataDir
	env2 := testkit.StartServer(t, o2)
	prod.Rebind(env2)
	if err := prod.Resume(); err != nil {
		t.Fatal(err)
	}
	if _, _, a, err := prod.PublishShare(true, nil); err != nil || a.TypeName() != wire.TypePublished {
		t.Fatalf("%v %+v", err, a)
	}
	env2.Engine.Close()

	// an invitation, shared invitation and a grant (ticket 224 rule 16)
	invCode, _, _ := env.Accounts.CreateInvitation("guest", "alice")
	const sharedPWNeedle = "SharedTestPassword123!"
	_ = env.Accounts.SetSharedInvitation(sharedPWNeedle, "family")
	_ = env.Accounts.AddGrant(resID, "resource", "bobby", "alice")

	diskNeedles := []string{testkit.TestPassword, sharedPWNeedle, invCode, accessSecret, shareID, bid, descNeedle, titleNeedle, payload, code,
		prodToken, secondToken, otherToken, "broadcast-access-needle"}
	tunnelMu.Lock()
	diskNeedles = append(diskNeedles, tunnelIDs...)
	tunnelMu.Unlock()
	logNeedles := []string{resID, prod.ID, second.ID, other.ID, "alice", "bobby"}
	logNeedles = append(logNeedles, env.Secrets()...)
	logNeedles = append(logNeedles, env2.Secrets()...)

	for _, v := range dataDirViolations(env.DataDir, manifestAllowed, diskNeedles, logNeedles) {
		t.Error(v)
	}
	// the two things the manifest exists to protect are really absent, and the scan is not vacuous
	dirJSON, _ := os.ReadFile(filepath.Join(env.DataDir, "directory.json"))
	if !bytes.Contains(dirJSON, []byte(resID)) {
		t.Fatalf("the resource record should be on disk, without its live members:\n%s", dirJSON)
	}
	logBytes, _ := os.ReadFile(filepath.Join(env.DataDir, "logs", "exchange.log"))
	for _, ev := range []string{"enrolled", "published", "relay-ended", "cast-offered", "tunnel-closed"} {
		if !bytes.Contains(logBytes, []byte(ev)) {
			t.Errorf("the log file should have recorded %q:\n%s", ev, logBytes)
		}
	}
}

// --- memory only (rule 3) ---

// access, shareId, broadcasts, tunnels, relay buffers, pairing codes and pending casts each live in
// memory only: a restart loses every one.
func TestARestartLosesEverythingThatLivesInMemoryOnly(t *testing.T) {
	lo, hi := testkit.FreeRange(3)
	o := looseOpts("alice")
	o.PortRange = [2]int{lo, hi}
	o.PublicEndpoint = "203.0.113.5:22022"
	o.WithRelay = true
	o.Cast = true
	env := testkit.StartServer(t, o)

	prod := env.NewProducer("alice", sshPW)
	if err := prod.Start(); err != nil {
		t.Fatal(err)
	}
	resID, shareID, ans, _ := prod.PublishShare(true, nil)
	if ans.TypeName() != wire.TypePublished {
		t.Fatalf("%+v", ans)
	}
	bid := env.NewID()
	if r, err := prod.Control().Request(&wire.BroadcastStart{Broadcast: castBroadcast(bid)}, 3*time.Second, wire.TypeBroadcastStarted); err != nil {
		t.Fatalf("%v %+v", err, r)
	}
	up, _, err := prod.Upload(bid, "audio/aac")
	if err != nil || up == nil {
		t.Fatal(err)
	}
	_ = up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(1, 64))
	waitBytesIn(t, env, bid, 64)
	code := requestCode(t, prod)
	rcv := env.NewDevice("alice")
	mustEnroll(t, rcv)
	rc, _ := rcv.OnlineQuietly()
	_ = prod.Control().Send(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: rcv.ID})
	if _, err := rc.Await(3*time.Second, wire.TypeCastOffer); err != nil {
		t.Fatal(err)
	}
	// a tunnel in flight
	tun, _, err := rcv.Connect(&wire.Connect{ResourceID: resID})
	if err != nil || tun == nil {
		t.Fatal(err)
	}
	if env.Engine.Tunnels() == 0 || env.Engine.PendingCasts() != 1 || env.Dir.Counts().Broadcasts != 1 {
		t.Fatal("the scenario did not set up what the restart must lose")
	}
	env.Stop()
	if _, err := tun.Write([]byte("x")); err == nil {
		_ = tun.SetReadDeadline(time.Now().Add(2 * time.Second))
		if _, err := tun.Read(make([]byte, 1)); err == nil {
			t.Fatal("a tunnel does not survive a restart")
		}
	}

	o2 := looseOpts("alice")
	o2.PortRange = [2]int{lo, hi}
	o2.PublicEndpoint = "203.0.113.5:22022"
	o2.WithRelay = true
	o2.Cast = true
	o2.DataDir = env.DataDir
	env2 := testkit.StartServer(t, o2)
	rcv.Rebind(env2)
	cc, err := rcv.Online()
	if err != nil {
		t.Fatal(err)
	}
	dir := listDirectory(t, cc)
	if len(dir.Broadcasts) != 0 {
		t.Fatal("a broadcast is not kept")
	}
	if len(dir.Resources) != 1 {
		t.Fatalf("%d resources", len(dir.Resources))
	}
	if r := recs(dir.Resources)[0]; r.Has("access") || r.Has("shareId") || r.Has("publicPort") {
		t.Fatalf("access, shareId and the port are in memory only: %s", r.Marshal())
	}
	// the shareId and the broadcastId are unknown, the relay holds nothing, nothing is pending
	if oc, r, _ := env2.OutsiderConnect(&wire.Connect{ShareID: shareID}); oc != nil {
		t.Fatalf("a shareId survived a restart: %+v", r)
	}
	if _, _, _, _, ok := env2.Relay.Held(bid); ok || env2.Engine.PendingCasts() != 0 || env2.Engine.Tunnels() != 0 {
		t.Fatal("the relay, the cast table and the tunnel table are empty after a restart")
	}
	if code, _ := statusOf(t, env2, getRequest(bid), "GET"); code != 404 {
		t.Fatalf("a restarted server does not know the broadcast: %d", code)
	}
	// the pairing code died with the process
	late := env2.NewDevice("alice")
	if r, _ := late.EnrollWithCode(code); r == nil || r.TypeName() != wire.TypeRefused {
		t.Fatalf("a pairing code must not survive a restart: %+v", r)
	}
}

// --- limits (rule 6): each exhausted, then an unrelated address is still answered ---

func secondAddress(t *testing.T, env *testkit.Env) net.IP {
	t.Helper()
	ip := net.ParseIP("127.0.0.2")
	c, err := net.DialTCP("tcp", &net.TCPAddr{IP: ip}, mustTCPAddr(t, env.Addr))
	if err != nil {
		t.Skipf("cannot originate from 127.0.0.2 on this host: %v", err)
	}
	_ = c.Close()
	return ip
}

// an attach that names no pending tunnel costs its address a token; when they are gone the address's
// attaches are closed - and another address's attach still works
func TestTheAttachMissLimitIsExhaustedAndAnotherAddressStillAttaches(t *testing.T) {
	lc := loose()
	lc.Rates[limits.AttachMiss] = limits.Rate{PerMinute: 1, Burst: 3}
	env := testkit.StartServer(t, testkit.Options{Limits: lc, LoginLimits: loose(), Logins: []string{"alice"}})
	ip := secondAddress(t, env)
	p, resID, _, _ := shareProducer(t, env, "alice", nil)
	defer p.Close()

	attachFrom := func(tunnelID string) bool { // reports whether the stream was closed unanswered
		c, err := testkit.DialTLSFrom(env.Addr, env.Fingerprint, ip)
		if err != nil {
			t.Fatal(err)
		}
		defer c.Close()
		_ = c.Send(&wire.Hello{DeviceID: p.ID, DeviceToken: p.Token})
		if _, err := c.Recv(3 * time.Second); err != nil {
			t.Fatal(err)
		}
		c.Authenticated()
		_ = c.Send(&wire.Attach{TunnelID: tunnelID})
		return readsEOF(c.Conn, 2*time.Second)
	}
	for i := 0; i < 3; i++ {
		if !attachFrom(wire.NewID()) {
			t.Fatalf("a miss %d must be closed unanswered", i)
		}
	}
	if env.Limiter.Has(ip.String(), limits.AttachMiss) {
		t.Fatal("the bucket of the address should be empty")
	}
	// another address attaches for a real tunnel: served
	cons := consumerDevice(t, env, "alice")
	c := connectOK(t, env, cons, resID)
	if out, err := testkit.SSHThrough(c.Conn, sshPW, "after-misses"); err != nil || out != "echo:after-misses" {
		t.Fatalf("an unrelated address was affected by another's misses: %q %v", out, err)
	}
}

func TestTheUnknownIDLimitOfTheListenURLIsExhaustedAndAnotherAddressStillListens(t *testing.T) {
	lc := loose()
	lc.Rates[limits.UnknownID] = limits.Rate{PerMinute: 1, Burst: 2}
	o := testkit.Options{Limits: lc, LoginLimits: loose(), Logins: []string{"alice"}, WithRelay: true, PublicEndpoint: "h:1"}
	env := testkit.StartServer(t, o)
	ip := secondAddress(t, env)
	_, id, up := startBroadcast(t, env, "alice", nil, "audio/aac")
	_ = up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(1, 16))
	waitBytesIn(t, env, id, 16)

	get := func(from net.IP, id string) int {
		c, err := testkit.DialTLSFrom(env.Addr, env.Fingerprint, from)
		if err != nil {
			t.Fatal(err)
		}
		defer c.Close()
		_, _ = c.Write([]byte(getRequest(id)))
		_ = c.SetReadDeadline(time.Now().Add(3 * time.Second))
		resp, err := http.ReadResponse(bufio.NewReader(c), &http.Request{Method: "GET"})
		if err != nil {
			t.Fatal(err)
		}
		return resp.StatusCode
	}
	for i := 0; i < 2; i++ {
		if c := get(ip, env.NewID()); c != 404 {
			t.Fatalf("%d", c)
		}
	}
	// the address is out of unknown-id attempts: even a REAL id is answered 404 from it, with no lookup
	if c := get(ip, id); c != 404 {
		t.Fatalf("an address that guessed too often is answered 404 whatever it asks: %d", c)
	}
	// another address (127.0.0.1) still listens
	l := listen(t, env, id)
	if l.Resp.StatusCode != 200 {
		t.Fatalf("%d", l.Resp.StatusCode)
	}
}

// resources, live broadcasts per account and per device, devices per account: capped and `limit-reached`
// (19 item AS), another account untouched
func TestAccountCapsAreExhaustedAndAnotherAccountIsUntouched(t *testing.T) {
	p := testkit.TestParams()
	p.ResourcesPerAccount, p.BroadcastsPerAccount, p.BroadcastsPerDevice, p.DevicesPerAccount = 3, 3, 2, 3
	o := looseOpts("alice", "bobby")
	o.Params = &p
	env := testkit.StartServer(t, o)
	a := startProducer(t, env, "alice")
	defer a.Close()
	for i := 0; i < 3; i++ {
		if _, _, ans, err := a.PublishShare(false, nil); err != nil || ans.TypeName() != wire.TypePublished {
			t.Fatalf("%d: %v %+v", i, err, ans)
		}
	}
	_, _, ans, _ := a.PublishShare(false, nil)
	refusedWith(t, ans, wire.ReasonLimitReached)
	start := func(d *testkit.Producer) wire.Envelope {
		r, err := d.Control().Request(&wire.BroadcastStart{Broadcast: castBroadcast(env.NewID())}, 3*time.Second, wire.TypeBroadcastStarted, wire.TypeRefused)
		if err != nil {
			t.Fatal(err)
		}
		return r
	}
	for i := 0; i < 2; i++ {
		if r := start(a); r.TypeName() != wire.TypeBroadcastStarted {
			t.Fatalf("%+v", r)
		}
	}
	refusedWith(t, start(a), wire.ReasonLimitReached) // 2 per device
	a2 := startProducer(t, env, "alice")
	defer a2.Close()
	if r := start(a2); r.TypeName() != wire.TypeBroadcastStarted {
		t.Fatalf("%+v", r) // a third live broadcast of the account, on another device
	}
	refusedWith(t, start(a2), wire.ReasonLimitReached) // 3 per account
	// devices: the account has alice's two producers; a third enrolls, the fourth is refused
	third := env.NewDevice("alice")
	mustEnroll(t, third)
	fourth := env.NewDevice("alice")
	if r, _ := fourth.Enroll(); r == nil {
		t.Fatal("no answer")
	} else {
		refusedWith(t, r, wire.ReasonLimitReached)
	}
	// another account has its own counts for all of them
	b := startProducer(t, env, "bobby")
	defer b.Close()
	if _, _, ans, err := b.PublishShare(false, nil); err != nil || ans.TypeName() != wire.TypePublished {
		t.Fatalf("%v %+v", err, ans)
	}
	if r := start(b); r.TypeName() != wire.TypeBroadcastStarted {
		t.Fatalf("%+v", r)
	}
}

// the accepted-connection limit, from one address, leaves another address untouched
func TestTheConnectionLimitIsExhaustedAndAnotherAddressIsServed(t *testing.T) {
	lc := loose()
	lc.Rates[limits.Connection] = limits.Rate{PerMinute: 1, Burst: 6}
	env := testkit.StartServer(t, testkit.Options{Limits: lc, LoginLimits: loose(), Logins: []string{"alice"}})
	ip := secondAddress(t, env)
	var conns []net.Conn
	closed := 0
	for i := 0; i < 20; i++ {
		c, err := net.DialTCP("tcp", &net.TCPAddr{IP: ip}, mustTCPAddr(t, env.Addr))
		if err != nil {
			t.Fatal(err)
		}
		conns = append(conns, c)
	}
	time.Sleep(200 * time.Millisecond)
	for _, c := range conns {
		if readsEOF(c, 50*time.Millisecond) {
			closed++
		}
		defer c.Close()
	}
	if closed < 10 {
		t.Fatalf("only %d of 20 connections over the limit were closed", closed)
	}
	d := env.NewDevice("alice")
	if r, err := d.Enroll(); err != nil || r.TypeName() != wire.TypeEnrolled {
		t.Fatalf("another address must be served: %v %+v", err, r)
	}
	_ = rendezvous.DefaultParams
}

// every class of the rate limits of rule 6 that has an address, a login, an account or a device as its
// key, in one table: exhaust it, see the refusal, and prove that a party the bucket does not name is
// still answered
func TestEveryKeyedLimitIsExhaustedAndAnUnrelatedPartyIsStillServed(t *testing.T) {
	small := func(c limits.Class, burst int) (*limits.Config, *limits.Config) {
		a, b := loose(), loose()
		r := limits.Rate{PerMinute: 1, Burst: burst}
		a.Rates[c], b.Rates[c] = r, r
		return a, b
	}

	t.Run("password check, per address and per login", func(t *testing.T) {
		env := testkit.StartServer(t, testkit.Options{Limits: loose(), LoginLimits: loose(), Logins: []string{"alice", "bobby"},
			PaceInterval: 2 * time.Second, Sleep: func(time.Duration) {}})
		ip := secondAddress(t, env)
		bad := func() wire.Envelope {
			c, err := testkit.DialTLSFrom(env.Addr, env.Fingerprint, ip)
			if err != nil {
				t.Fatal(err)
			}
			defer c.Close()
			_ = c.Send(&wire.Enroll{Login: "alice", Password: "Wrongpass1", DeviceID: env.NewID(), DeviceName: "x", Product: "fms-windows"})
			e, err := c.Recv(3 * time.Second)
			if err != nil {
				t.Fatal(err)
			}
			return e
		}
		refusedWith(t, bad(), wire.ReasonBadCredentials)
		refusedWith(t, bad(), wire.ReasonBadCredentials)
		refusedWith(t, bad(), wire.ReasonRateLimited)
		mustEnroll(t, env.NewDevice("bobby")) // another login from another address is not paced by the first
	})

	t.Run("failed hello, per address", func(t *testing.T) {
		lc, _ := small(limits.FailedAuth, 2)
		env := testkit.StartServer(t, testkit.Options{Limits: lc, LoginLimits: loose(), Logins: []string{"alice"}})
		d := env.NewDevice("alice")
		mustEnroll(t, d)
		bad := func() wire.Envelope { _, r, _ := d.HelloWith("a-wrong-token"); return r }
		refusedWith(t, bad(), wire.ReasonBadCredentials)
		refusedWith(t, bad(), wire.ReasonBadCredentials)
		refusedWith(t, bad(), wire.ReasonRateLimited)
	})

	t.Run("failed enroll, per login", func(t *testing.T) {
		env := testkit.StartServer(t, testkit.Options{Limits: loose(), LoginLimits: loose(), Logins: []string{"alice", "bobby"}})
		for i := 0; i < 3; i++ {
			r, _ := env.NewDevice("alice").EnrollWith("Wrongpass1")
			refusedWith(t, r, wire.ReasonBadCredentials)
		}
		time.Sleep(1100 * time.Millisecond)
		mustEnroll(t, env.NewDevice("alice"))
		mustEnroll(t, env.NewDevice("bobby"))
	})

	t.Run("failed hello, per account", func(t *testing.T) {
		_, llc := small(limits.FailedHello, 2)
		env := testkit.StartServer(t, testkit.Options{Limits: loose(), LoginLimits: llc, Logins: []string{"alice", "bobby"}})
		a, b := env.NewDevice("alice"), env.NewDevice("bobby")
		mustEnroll(t, a)
		mustEnroll(t, b)
		a.Close()
		bad := strings.Repeat("A", 43)
		for i := 0; i < 2; i++ {
			_, r, _ := a.HelloWith(bad)
			refusedWith(t, r, wire.ReasonBadCredentials)
		}
		_, r, _ := a.HelloWith(bad)
		refusedWith(t, r, wire.ReasonRateLimited)
		if c, r, _ := b.Hello(); c == nil { // another account
			t.Fatalf("%+v", r)
		} else {
			c.Close()
		}
	})

	t.Run("pairing-request, per account", func(t *testing.T) {
		_, llc := small(limits.PairingRequest, 2)
		env := testkit.StartServer(t, testkit.Options{Limits: loose(), LoginLimits: llc, Logins: []string{"alice", "bobby"}})
		pa, pb := startProducer(t, env, "alice"), startProducer(t, env, "bobby")
		defer pa.Close()
		defer pb.Close()
		for i := 0; i < 2; i++ {
			requestCode(t, pa)
		}
		r, _ := pa.Control().Request(&wire.PairingRequest{}, 3*time.Second, wire.TypePairingCode, wire.TypeRefused)
		refusedWith(t, r, wire.ReasonRateLimited)
		requestCode(t, pb)
	})

	t.Run("broadcast-start, per device", func(t *testing.T) {
		_, llc := small(limits.BroadcastStart, 2)
		env := testkit.StartServer(t, testkit.Options{Limits: loose(), LoginLimits: llc, Logins: []string{"alice"}})
		d1, d2 := startProducer(t, env, "alice"), startProducer(t, env, "alice")
		defer d1.Close()
		defer d2.Close()
		start := func(d *testkit.Producer) wire.Envelope {
			r, _ := d.Control().Request(&wire.BroadcastStart{Broadcast: castBroadcast(env.NewID())}, 3*time.Second, wire.TypeBroadcastStarted, wire.TypeRefused)
			return r
		}
		for i := 0; i < 2; i++ {
			if r := start(d1); r.TypeName() != wire.TypeBroadcastStarted {
				t.Fatalf("%+v", r)
			}
		}
		refusedWith(t, start(d1), wire.ReasonRateLimited)
		if r := start(d2); r.TypeName() != wire.TypeBroadcastStarted { // another device
			t.Fatalf("%+v", r)
		}
	})

	t.Run("an unknown id on connect, per address", func(t *testing.T) {
		lc, _ := small(limits.UnknownID, 2)
		env := testkit.StartServer(t, testkit.Options{Limits: lc, LoginLimits: loose(), Logins: []string{"alice"}})
		ip := secondAddress(t, env)
		_, _, share, _ := shareProducer(t, env, "alice", nil)
		guess := func(from net.IP, id string) wire.Envelope {
			c, err := testkit.DialTLSFrom(env.Addr, env.Fingerprint, from)
			if err != nil {
				t.Fatal(err)
			}
			defer c.Close()
			_ = c.Send(&wire.Connect{ShareID: id})
			e, err := c.Recv(5 * time.Second)
			if err != nil {
				t.Fatal(err)
			}
			return e
		}
		for i := 0; i < 3; i++ { // two attempts of burst, then every answer is the same `unavailable` - even a real id
			refusedWith(t, guess(ip, env.NewID()), wire.ReasonUnavailable)
		}
		refusedWith(t, guess(ip, share), wire.ReasonUnavailable) // the bucket answers without a lookup
		if e := guess(net.ParseIP("127.0.0.1"), share); e.TypeName() != wire.TypeConnected {
			t.Fatalf("an unrelated address must still be served: %+v", e)
		}
	})

	t.Run("tunnels per resource", func(t *testing.T) {
		env := testkit.StartServer(t, looseOpts("alice"))
		p := env.NewProducer("alice", sshPW)
		p.Handler = holdHandler
		if err := p.Start(); err != nil {
			t.Fatal(err)
		}
		defer p.Close()
		res1, _, _, _ := p.PublishShare(false, nil)
		res2, _, _, _ := p.PublishShare(false, nil)
		cons := consumerDevice(t, env, "alice")
		var held []*testkit.Conn
		for i := 0; i < 16; i++ {
			c, r, err := cons.Connect(&wire.Connect{ResourceID: res1})
			if err != nil || c == nil {
				t.Fatalf("tunnel %d: %v %+v", i, err, r)
			}
			held = append(held, c)
		}
		defer func() {
			for _, c := range held {
				_ = c.Close()
			}
		}()
		if c, r, _ := cons.Connect(&wire.Connect{ResourceID: res1}); c != nil {
			t.Fatal("the 17th tunnel of one resource must be refused")
		} else {
			refusedWith(t, r, wire.ReasonUnavailable)
		}
		if c, r, err := cons.Connect(&wire.Connect{ResourceID: res2}); err != nil || c == nil { // another resource
			t.Fatalf("%v %+v", err, r)
		} else {
			c.Close()
		}
	})
}
