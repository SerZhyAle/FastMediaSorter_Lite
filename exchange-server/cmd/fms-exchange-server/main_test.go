package main

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"net"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/admin"
	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/connstr"
	"fms/exchange-server/internal/deploy"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// exec runs the command line in process.
func execute(t *testing.T, args []string, stdin string) (code int, stdout, stderr string) {
	t.Helper()
	var out, errb bytes.Buffer
	code = run(context.Background(), args, strings.NewReader(stdin), &out, &errb)
	return code, out.String(), errb.String()
}

func cfgPath(t *testing.T) string { return filepath.Join(t.TempDir(), "data", config.FileName) }

func TestParseArgs(t *testing.T) {
	ok := []struct {
		in   []string
		want command
	}{
		{[]string{"--version"}, command{name: "version"}},
		{[]string{"serve"}, command{name: "serve"}},
		{[]string{"serve", "--config", "a.json"}, command{name: "serve", config: "a.json"}},
		{[]string{"serve", "--config=a.json"}, command{name: "serve", config: "a.json"}},
		{[]string{"inspect", "--init", "--config", "x"}, command{name: "inspect", init: true, config: "x"}},
		{[]string{"account", "add", "alice", "--generate"}, command{name: "account", sub: "add", login: "alice", generate: true}},
		{[]string{"account", "add", "--generate", "alice"}, command{name: "account", sub: "add", login: "alice", generate: true}},
		{[]string{"account", "passwd", "alice", "--sign-out"}, command{name: "account", sub: "passwd", login: "alice", signOut: true}},
		{[]string{"account", "passwd", "alice", "--generate", "--sign-out"}, command{name: "account", sub: "passwd", login: "alice", generate: true, signOut: true}},
		{[]string{"account", "remove", "alice", "--config", "c.json"}, command{name: "account", sub: "remove", login: "alice", config: "c.json"}},
		{[]string{"account", "list", "--json"}, command{name: "account", sub: "list", json: true}},
		{[]string{"device", "list", "alice"}, command{name: "device", sub: "list", login: "alice"}},
		{[]string{"device", "list", "alice", "--json"}, command{name: "device", sub: "list", login: "alice", json: true}},
		{[]string{"device", "revoke", "alice", "AAAAAAAAAAAAAAAAAAAAAA"}, command{name: "device", sub: "revoke", login: "alice", deviceID: "AAAAAAAAAAAAAAAAAAAAAA"}},
		// a deviceId is base64url and may begin with a hyphen
		{[]string{"device", "revoke", "alice", "-pjgKQFygDW5ohQ4zk4Isw"}, command{name: "device", sub: "revoke", login: "alice", deviceID: "-pjgKQFygDW5ohQ4zk4Isw"}},
		{[]string{"device", "revoke", "--", "alice", "--x"}, command{name: "device", sub: "revoke", login: "alice", deviceID: "--x"}},
		{[]string{"set-endpoint", "exchange.example.org:22022"}, command{name: "set-endpoint", value: "exchange.example.org:22022"}},
		{[]string{"set-endpoint", "-"}, command{name: "set-endpoint", value: "-"}},
		{[]string{"set-listen", ":23456", "--config", "x"}, command{name: "set-listen", value: ":23456", config: "x"}},
	}
	for _, c := range ok {
		got, err := parseArgs(c.in)
		if err != nil || got != c.want {
			t.Errorf("%v: got %+v %v, want %+v", c.in, got, err, c.want)
		}
	}
	bad := [][]string{
		nil, {"bogus"}, {"serve", "--nope"}, {"serve", "--generate"}, {"inspect", "--generate"},
		{"serve", "--config"}, {"serve", "--config="}, {"--version", "x"}, {"serve", "stray"},
		{"set-password", "--generate"}, // retired: accounts replaced the single server password
		{"account"}, {"account", "frobnicate", "x"}, {"account", "add"}, {"account", "add", "a", "b"},
		{"account", "list", "extra"}, {"account", "remove", "a", "--generate"}, {"account", "add", "a", "--sign-out"},
		{"account", "add", "a", "--json"}, {"account", "add", "a", "--generate=1"},
		{"device"}, {"device", "list"}, {"device", "revoke", "a"}, {"device", "revoke", "a", "b", "c"},
		{"device", "revoke", "a", "b", "--json"}, {"device", "list", "a", "--generate"},
		{"set-endpoint"}, {"set-endpoint", "a", "b"}, {"set-listen"},
	}
	for _, in := range bad {
		if _, err := parseArgs(in); err == nil {
			t.Errorf("%v must be a usage error", in)
		}
	}
	if code, _, stderr := execute(t, []string{"serve", "--nope"}, ""); code != exitUsage || !strings.Contains(stderr, "usage:") {
		t.Errorf("a usage error exits 2 and prints usage: %d %q", code, stderr)
	}
}

func TestVersion(t *testing.T) {
	code, out, _ := execute(t, []string{"--version"}, "")
	if code != 0 || strings.TrimSpace(out) != version {
		t.Fatalf("%d %q (version %q)", code, out, version)
	}
}

// --- accounts, offline (no server holds the directory) ---

func TestAccountAddGenerateMeetsThePolicyAndPrintsOnceAndStoresOnlyARecord(t *testing.T) {
	p := cfgPath(t)
	code, stdout, stderr := execute(t, []string{"account", "add", "Alice", "--generate", "--config", p}, "")
	if code != 0 {
		t.Fatalf("%d %s", code, stderr)
	}
	pw := strings.TrimSpace(stdout)
	if password.Check(pw) != nil || strings.Count(stdout, "\n") != 1 {
		t.Fatalf("generated %q (stdout %q)", pw, stdout)
	}
	if strings.Contains(stderr, pw) {
		t.Fatal("the password must only be on stdout")
	}
	raw, _ := os.ReadFile(filepath.Join(filepath.Dir(p), accounts.AccountsFile))
	if bytes.Contains(raw, []byte(pw)) || !bytes.Contains(raw, []byte("$argon2id$")) || !bytes.Contains(raw, []byte(`"alice"`)) {
		t.Fatalf("accounts.json must hold the Argon2id record of the folded login:\n%s", raw)
	}
	// the stores are the server's only: the old config member is not written
	if cfg, err := config.Load(p); err == nil && cfg.ObsoletePasswordHash {
		t.Fatal("no password in the config")
	}
	// a duplicate, case-insensitively, is refused and prints nothing
	code, stdout, stderr = execute(t, []string{"account", "add", "ALICE", "--generate", "--config", p}, "")
	if code != exitFailure || stdout != "" || !strings.Contains(stderr, "exists") {
		t.Fatalf("%d %q %q", code, stdout, stderr)
	}
}

func TestAccountAddFromStdinRefusesBelowThePolicyAndChangesNothing(t *testing.T) {
	p := cfgPath(t)
	for _, weak := range []string{"Abcdefgh1", "abcdefghij1", "ABCDEFGHIJ1", "Abcdefghij", "Абвгдежзий1A", ""} {
		code, _, stderr := execute(t, []string{"account", "add", "alice", "--config", p}, weak+"\n")
		if code != exitPolicy || stderr == "" {
			t.Errorf("%q: exit %d, stderr %q", weak, code, stderr)
		}
	}
	if _, err := os.Stat(filepath.Join(filepath.Dir(p), accounts.AccountsFile)); err == nil {
		t.Fatal("a refused password must not create the store")
	}
	const pw = "Str0ngPassw0rd"
	code, stdout, stderr := execute(t, []string{"account", "add", "alice", "--config", p}, pw+"\r\n")
	if code != 0 || stdout != "" {
		t.Fatalf("exit %d, stdout %q, stderr %q", code, stdout, stderr)
	}
	// no plaintext anywhere in the data directory
	filepath.Walk(filepath.Dir(p), func(f string, info os.FileInfo, err error) error {
		if err == nil && !info.IsDir() {
			if b, _ := os.ReadFile(f); bytes.Contains(b, []byte(pw)) {
				t.Errorf("the plaintext password is in %s", f)
			}
		}
		return nil
	})
	// a bad login is a usage error, not a policy one
	if code, _, _ := execute(t, []string{"account", "add", "a b", "--config", p}, pw+"\n"); code != exitUsage {
		t.Fatalf("a login that cannot exist: exit %d", code)
	}
}

func TestAccountPasswdListRemoveAndTheJSONForm(t *testing.T) {
	p := cfgPath(t)
	execute(t, []string{"account", "add", "alice", "--config", p}, "Str0ngPassw0rd\n")
	execute(t, []string{"account", "add", "bobby", "--config", p}, "Str0ngPassw0rd\n")
	code, out, _ := execute(t, []string{"account", "list", "--config", p}, "")
	if code != 0 || !strings.Contains(out, "alice\t") || !strings.Contains(out, "bobby\t") || !strings.Contains(out, "0 devices") {
		t.Fatalf("%d %q", code, out)
	}
	code, out, _ = execute(t, []string{"account", "list", "--json", "--config", p}, "")
	var views []admin.AccountView
	if code != 0 || json.Unmarshal([]byte(out), &views) != nil || len(views) != 2 || strings.Contains(out, "argon2") {
		t.Fatalf("%d %q", code, out)
	}
	// passwd: from stdin and --generate
	if code, _, _ := execute(t, []string{"account", "passwd", "alice", "--config", p}, "N3wStrongPass!1\n"); code != 0 {
		t.Fatalf("passwd: %d", code)
	}
	code, newPw, _ := execute(t, []string{"account", "passwd", "alice", "--generate", "--config", p}, "")
	if code != 0 || password.Check(strings.TrimSpace(newPw)) != nil {
		t.Fatalf("passwd --generate: %d %q", code, newPw)
	}
	store, err := accounts.Open(accounts.Options{Dir: filepath.Dir(p), Params: testkit.Cheap()})
	if err != nil {
		t.Fatal(err)
	}
	if !store.VerifyPassword("alice", strings.TrimSpace(newPw)) {
		t.Fatal("the generated password is not the one stored")
	}
	if code, _, stderr := execute(t, []string{"account", "passwd", "nobody", "--generate", "--config", p}, ""); code != exitFailure || !strings.Contains(stderr, "no such account") {
		t.Fatalf("%d %q", code, stderr)
	}
	// remove
	if code, _, _ := execute(t, []string{"account", "remove", "alice", "--config", p}, ""); code != 0 {
		t.Fatalf("remove: %d", code)
	}
	if code, _, stderr := execute(t, []string{"account", "remove", "alice", "--config", p}, ""); code != exitFailure || !strings.Contains(stderr, "no such account") {
		t.Fatalf("%d %q", code, stderr)
	}
	_, out, _ = execute(t, []string{"account", "list", "--config", p}, "")
	if strings.Contains(out, "alice") || !strings.Contains(out, "bobby") {
		t.Fatalf("%q", out)
	}
}

func TestDeviceListAndRevokeOffline(t *testing.T) {
	p := cfgPath(t)
	dir := filepath.Dir(p)
	execute(t, []string{"account", "add", "alice", "--config", p}, "Str0ngPassw0rd\n")
	store, _ := accounts.Open(accounts.Options{Dir: dir, Params: testkit.Cheap()})
	id := wire.NewID()
	en, err := store.Enroll(accounts.EnrollSpec{Account: "alice", DeviceID: id, Name: "Phone", Product: "fms-android"})
	if err != nil {
		t.Fatal(err)
	}
	if err := store.Flush(); err != nil {
		t.Fatal(err)
	}
	code, out, _ := execute(t, []string{"device", "list", "alice", "--config", p}, "")
	if code != 0 || !strings.Contains(out, id) || !strings.Contains(out, "offline") || strings.Contains(out, en.Token) || strings.Contains(out, en.Device.TokenHash) {
		t.Fatalf("%d %q", code, out)
	}
	code, out, _ = execute(t, []string{"device", "list", "alice", "--json", "--config", p}, "")
	var ds []admin.DeviceView
	if code != 0 || json.Unmarshal([]byte(out), &ds) != nil || len(ds) != 1 || ds[0].DeviceID != id {
		t.Fatalf("%d %q", code, out)
	}
	if code, _, stderr := execute(t, []string{"device", "list", "nobody", "--config", p}, ""); code != exitFailure || !strings.Contains(stderr, "no such account") {
		t.Fatalf("%d %q", code, stderr)
	}
	if code, _, stderr := execute(t, []string{"device", "revoke", "alice", wire.NewID(), "--config", p}, ""); code != exitFailure || !strings.Contains(stderr, "no such device") {
		t.Fatalf("%d %q", code, stderr)
	}
	if code, _, _ := execute(t, []string{"device", "revoke", "alice", id, "--config", p}, ""); code != 0 {
		t.Fatalf("revoke: %d", code)
	}
	again, _ := accounts.Open(accounts.Options{Dir: dir, Params: testkit.Cheap()})
	if _, st := again.Hello(id, en.Token); st != accounts.HelloRevoked {
		t.Fatalf("the revoked token must be told it was revoked: %v", st)
	}
	_, out, _ = execute(t, []string{"device", "list", "alice", "--config", p}, "")
	if strings.Contains(out, id) {
		t.Fatalf("a revoked device is still listed: %q", out)
	}
}

func TestAccountCommandsRefuseWhileAServerHoldsTheDirectoryWithoutAChannel(t *testing.T) {
	p := cfgPath(t)
	release, err := deploy.LockDir(filepath.Dir(p))
	if err != nil {
		t.Fatal(err)
	}
	defer release()
	code, _, stderr := execute(t, []string{"account", "list", "--config", p}, "")
	if code != exitFailure || !strings.Contains(stderr, "admin channel") {
		t.Fatalf("a directory held by something that does not answer: %d %q", code, stderr)
	}
}

// --- inspect ---

func TestInspectCreatesNothing(t *testing.T) {
	dir := filepath.Join(t.TempDir(), "never")
	p := filepath.Join(dir, config.FileName)
	code, stdout, _ := execute(t, []string{"inspect", "--config", p}, "")
	if code != 0 {
		t.Fatalf("exit %d", code)
	}
	var rep inspectReport
	if err := json.Unmarshal([]byte(stdout), &rep); err != nil {
		t.Fatalf("not JSON: %v\n%s", err, stdout)
	}
	if rep.ConfigPresent || rep.Accounts != 0 || rep.CertPresent || rep.Fingerprint != "" || rep.DataDir != dir {
		t.Fatalf("report on a missing directory: %+v", rep)
	}
	if _, err := os.Stat(dir); err == nil {
		t.Fatal("inspect created the data directory")
	}
}

func TestInspectInitThenInspectAndItCountsWithoutOpeningTheStore(t *testing.T) {
	p := cfgPath(t)
	execute(t, []string{"account", "add", "alice", "--config", p}, "Str0ngPassw0rd\n")
	_, out1, _ := execute(t, []string{"inspect", "--init", "--config", p}, "")
	var a inspectReport
	_ = json.Unmarshal([]byte(out1), &a)
	if !a.CertPresent || !strings.HasPrefix(a.Fingerprint, "SHA256:") || a.Accounts != 1 {
		t.Fatalf("after --init: %+v", a)
	}
	_, out2, _ := execute(t, []string{"inspect", "--config", p}, "")
	var b inspectReport
	_ = json.Unmarshal([]byte(out2), &b)
	if b.Fingerprint != a.Fingerprint || b.Listen != ":22022" || b.PortRange != "40000-40100" || b.Accounts != 1 {
		t.Fatalf("plain inspect: %+v vs %+v", b, a)
	}
	for _, leak := range []string{"argon2", "alice", "passwordHash"} {
		if strings.Contains(out2, leak) {
			t.Fatalf("inspect must not print %q", leak)
		}
	}
	// it works while a server holds the directory (it never takes the lock)
	release, err := deploy.LockDir(filepath.Dir(p))
	if err != nil {
		t.Fatal(err)
	}
	defer release()
	if code, _, _ := execute(t, []string{"inspect", "--config", p}, ""); code != 0 {
		t.Fatal("inspect must work beside a running server")
	}
}

func TestInspectReportsAJSONErrorOnAFailure(t *testing.T) {
	p := cfgPath(t)
	os.MkdirAll(filepath.Dir(p), 0o700)
	os.WriteFile(p, []byte("nope"), 0o600)
	code, stdout, _ := execute(t, []string{"inspect", "--config", p}, "")
	var rep inspectReport
	if err := json.Unmarshal([]byte(stdout), &rep); err != nil || rep.Error == "" || code == 0 {
		t.Fatalf("a failing inspect still prints JSON with an error: %d %v %s", code, err, stdout)
	}
}

// --- serve ---

func TestServeRefusesWithNoAccountAndSaysHow(t *testing.T) {
	p := cfgPath(t)
	code, _, stderr := execute(t, []string{"serve", "--config", p}, "")
	if code != exitPolicy || !strings.Contains(stderr, "account add") || !strings.Contains(stderr, "no account") {
		t.Fatalf("exit %d, stderr %q", code, stderr)
	}
	// it left the commented defaults for the operator, and no TLS identity was made
	if _, err := os.Stat(p); err != nil {
		t.Fatalf("the commented defaults were not written: %v", err)
	}
	if _, err := os.Stat(filepath.Join(filepath.Dir(p), "tls")); err == nil {
		t.Fatal("an account-less serve must not create an identity")
	}
	// the account it asks for is enough
	execute(t, []string{"account", "add", "alice", "--config", p}, "Str0ngPassw0rd\n")
	// a store written by a newer server is refused, not served
	os.WriteFile(filepath.Join(filepath.Dir(p), accounts.AccountsFile), []byte(`{"storeVersion":9,"accounts":[]}`), 0o600)
	if code, _, _ := execute(t, []string{"serve", "--config", p}, ""); code != exitConfig {
		t.Fatalf("a newer store: exit %d", code)
	}
}

func freePort(t *testing.T) int {
	t.Helper()
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer ln.Close()
	return ln.Addr().(*net.TCPAddr).Port
}

// prepare makes an account, and points the config at a loopback port.
func prepare(t *testing.T) (cfgFile, pw string, port int) {
	t.Helper()
	cfgFile = cfgPath(t)
	code, out, errs := execute(t, []string{"account", "add", "alice", "--generate", "--config", cfgFile}, "")
	if code != 0 {
		t.Fatalf("account add: %d %s", code, errs)
	}
	pw = strings.TrimSpace(out)
	cfg, _ := config.Load(cfgFile)
	port = freePort(t)
	cfg.Listen = "127.0.0.1:" + strconv.Itoa(port)
	lo, hi := testkit.FreeRange(5)
	cfg.PortRange = strconv.Itoa(lo) + "-" + strconv.Itoa(hi)
	if err := config.Save(cfgFile, cfg); err != nil {
		t.Fatal(err)
	}
	return
}

func waitListening(t *testing.T, addr string) {
	t.Helper()
	deadline := time.Now().Add(10 * time.Second)
	for time.Now().Before(deadline) {
		if c, err := net.DialTimeout("tcp", addr, 200*time.Millisecond); err == nil {
			c.Close()
			return
		}
		time.Sleep(50 * time.Millisecond)
	}
	t.Fatalf("nothing listens on %s", addr)
}

func fingerprintOf(t *testing.T, cfgFile string) string {
	t.Helper()
	_, out, _ := execute(t, []string{"inspect", "--config", cfgFile}, "")
	var rep inspectReport
	_ = json.Unmarshal([]byte(out), &rep)
	return rep.Fingerprint
}

// enrollOver enrolls a device of the account with the password over TLS pinned to fp.
func enrollOver(t *testing.T, addr, fp, login, pw, deviceID string) *wire.Enrolled {
	t.Helper()
	c, err := testkit.DialTLS(addr, fp)
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	if err := c.Send(&wire.Enroll{Login: login, Password: pw, DeviceID: deviceID, DeviceName: "Test", Product: "fms-windows"}); err != nil {
		t.Fatal(err)
	}
	r, err := c.Recv(10 * time.Second)
	en, ok := r.(*wire.Enrolled)
	if err != nil || !ok {
		t.Fatalf("enroll: %v %+v", err, r)
	}
	return en
}

// helloOver says hello and reports what the server answered with.
func helloOver(t *testing.T, addr, fp, deviceID, token string) wire.Envelope {
	t.Helper()
	c, err := testkit.DialTLS(addr, fp)
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	if err := c.Send(&wire.Hello{DeviceID: deviceID, DeviceToken: token}); err != nil {
		t.Fatal(err)
	}
	r, err := c.Recv(10 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	return r
}

func TestServeRunsAndAccountsAndTokensSurviveARestart(t *testing.T) {
	cfgFile, pw, port := prepare(t)
	addr := "127.0.0.1:" + strconv.Itoa(port)
	devID := wire.NewID()
	var token string

	serveOnce := func(first bool) string {
		ctx, cancel := context.WithCancel(context.Background())
		var out bytes.Buffer
		done := make(chan int, 1)
		go func() { done <- run(ctx, []string{"serve", "--config", cfgFile}, strings.NewReader(""), &out, &out) }()
		waitListening(t, addr)
		fp := fingerprintOf(t, cfgFile)
		if first {
			token = enrollOver(t, addr, fp, "alice", pw, devID).DeviceToken
		}
		if w, ok := helloOver(t, addr, fp, devID, token).(*wire.Welcome); !ok || w.Account != "alice" {
			t.Fatalf("hello against the served binary: %+v\n%s", w, out.String())
		}
		cancel()
		select {
		case c := <-done:
			if c != 0 {
				t.Fatalf("serve exited %d:\n%s", c, out.String())
			}
		case <-time.After(10 * time.Second):
			t.Fatal("serve did not stop on cancel")
		}
		if !strings.Contains(out.String(), "tls fingerprint "+fp) {
			t.Fatalf("serve must print the fingerprint it serves:\n%s", out.String())
		}
		return fp
	}
	first := serveOnce(true)
	second := serveOnce(false) // the same account, the same device token, the same identity
	if first != second {
		t.Fatalf("the identity changed across a restart: %s vs %s", first, second)
	}
}

func TestTwoServersOnOneDirectoryAreRefused(t *testing.T) {
	cfgFile, _, port := prepare(t)
	addr := "127.0.0.1:" + strconv.Itoa(port)
	ctx, cancel := context.WithCancel(context.Background())
	var out bytes.Buffer
	done := make(chan int, 1)
	go func() { done <- run(ctx, []string{"serve", "--config", cfgFile}, strings.NewReader(""), &out, &out) }()
	waitListening(t, addr)
	defer func() {
		cancel()
		<-done
	}()
	// a second server on the same directory - on another port, so only the lock can stop it
	cfg2 := filepath.Join(filepath.Dir(cfgFile), "second.json")
	cfg, _ := config.Load(cfgFile)
	cfg.Listen = "127.0.0.1:" + strconv.Itoa(freePort(t))
	config.Save(cfg2, cfg)
	code, _, stderr := execute(t, []string{"serve", "--config", cfg2}, "")
	if code != exitFailure || !strings.Contains(stderr, "already holds") {
		t.Fatalf("exit %d, stderr %q", code, stderr)
	}
}

func TestAccountCommandsReachARunningServerThroughTheAdminChannel(t *testing.T) {
	cfgFile, pw, port := prepare(t)
	addr := "127.0.0.1:" + strconv.Itoa(port)
	ctx, cancel := context.WithCancel(context.Background())
	var out bytes.Buffer
	done := make(chan int, 1)
	go func() { done <- run(ctx, []string{"serve", "--config", cfgFile}, strings.NewReader(""), &out, &out) }()
	waitListening(t, addr)
	defer func() {
		cancel()
		<-done
	}()
	fp := fingerprintOf(t, cfgFile)
	devID := wire.NewID()
	en := enrollOver(t, addr, fp, "alice", pw, devID)

	// the running server's own stores are what the commands change: a new account can enroll at once
	code, newPw, stderr := execute(t, []string{"account", "add", "bobby", "--generate", "--config", cfgFile}, "")
	if code != 0 {
		t.Fatalf("account add against a running server: %d %s\n%s", code, stderr, out.String())
	}
	if en2 := enrollOver(t, addr, fp, "bobby", strings.TrimSpace(newPw), wire.NewID()); en2.Account != "bobby" {
		t.Fatalf("%+v", en2)
	}
	_, list, _ := execute(t, []string{"account", "list", "--config", cfgFile}, "")
	if !strings.Contains(list, "alice\t") || !strings.Contains(list, "bobby\t") || !strings.Contains(list, "1 devices") {
		t.Fatalf("%q", list)
	}
	_, dl, _ := execute(t, []string{"device", "list", "alice", "--config", cfgFile}, "")
	if !strings.Contains(dl, devID) {
		t.Fatalf("%q", dl)
	}
	// revoke while serving: the token dies at once
	if code, _, stderr := execute(t, []string{"device", "revoke", "alice", devID, "--config", cfgFile}, ""); code != 0 {
		t.Fatalf("revoke: %d %s", code, stderr)
	}
	if r, ok := helloOver(t, addr, fp, devID, en.DeviceToken).(*wire.Refused); !ok || r.Reason != string(wire.ReasonDeviceRevoked) {
		t.Fatalf("a revoked token: %+v", r)
	}
	// remove an account while serving
	if code, _, _ := execute(t, []string{"account", "remove", "bobby", "--config", cfgFile}, ""); code != 0 {
		t.Fatal("remove")
	}
	c, _ := testkit.DialTLS(addr, fp)
	defer c.Close()
	_ = c.Send(&wire.Enroll{Login: "bobby", Password: strings.TrimSpace(newPw), DeviceID: wire.NewID(), DeviceName: "x", Product: "x"})
	if r, _ := c.Recv(5 * time.Second); r == nil || r.TypeName() != wire.TypeRefused {
		t.Fatalf("a removed account enrolled: %+v", r)
	}
	// and the password of an account never travelled back
	if strings.Contains(out.String(), pw) || strings.Contains(stderr, pw) {
		t.Fatal("a password reached the server's output")
	}
}

func TestServeStopsOnADamagedKeyInsteadOfReplacingTheIdentity(t *testing.T) {
	cfgFile, _, _ := prepare(t)
	execute(t, []string{"inspect", "--init", "--config", cfgFile}, "")
	dir := filepath.Dir(cfgFile)
	key := filepath.Join(dir, "tls", "key.pem")
	full, _ := os.ReadFile(key)
	damaged := full[:len(full)/2]
	os.WriteFile(key, damaged, 0o600)
	certBefore, _ := os.ReadFile(filepath.Join(dir, "tls", "cert.pem"))

	code, _, stderr := execute(t, []string{"serve", "--config", cfgFile}, "")
	if code != exitFailure || !strings.Contains(stderr, "TLS identity") {
		t.Fatalf("exit %d, stderr %q", code, stderr)
	}
	after, _ := os.ReadFile(key)
	certAfter, _ := os.ReadFile(filepath.Join(dir, "tls", "cert.pem"))
	if !bytes.Equal(after, damaged) || !bytes.Equal(certBefore, certAfter) {
		t.Fatal("a damaged identity was touched")
	}
}

func TestAnObsoletePasswordInTheConfigIsIgnoredLoggedOnceAndRemovedBySetListen(t *testing.T) {
	cfgFile, pw, port := prepare(t)
	raw, _ := os.ReadFile(cfgFile)
	var m map[string]any
	_ = json.Unmarshal(raw, &m)
	m["passwordHash"] = "$argon2id$v=19$m=8,t=1,p=1$c2FsdHNhbHQ$a2V5a2V5a2V5a2V5a2V5a2V5"
	b, _ := json.Marshal(m)
	os.WriteFile(cfgFile, b, 0o600)

	addr := "127.0.0.1:" + strconv.Itoa(port)
	ctx, cancel := context.WithCancel(context.Background())
	var out bytes.Buffer
	done := make(chan int, 1)
	go func() { done <- run(ctx, []string{"serve", "--config", cfgFile}, strings.NewReader(""), &out, &out) }()
	waitListening(t, addr)
	enrollOver(t, addr, fingerprintOf(t, cfgFile), "alice", pw, wire.NewID()) // the account is what counts
	cancel()
	<-done
	logb, _ := os.ReadFile(filepath.Join(filepath.Dir(cfgFile), config.LogsDir, "exchange-"+time.Now().UTC().Format("2006-01-02")+".log"))
	if n := bytes.Count(logb, []byte("\tobsolete\t")); n != 1 {
		t.Fatalf("the obsolete member must be logged once, not %d times:\n%s", n, logb)
	}
	if code, _, _ := execute(t, []string{"set-listen", "--config", cfgFile, ":23456"}, ""); code != 0 {
		t.Fatal("set-listen")
	}
	raw, _ = os.ReadFile(cfgFile)
	if bytes.Contains(raw, []byte("passwordHash")) {
		t.Fatalf("the next save must remove the obsolete member:\n%s", raw)
	}
}

// TestHardKillLeavesNothingButTheStoresTheIdentityTheLockAndTheLog is 133 acceptance 2 and tickets 139
// T-6 and 162 acceptance 4: sessions live in memory only, so after a kill in the middle of a session the
// data directory holds exactly the config, the stores, the identity, the lock file and the log.
func TestHardKillLeavesNothingButTheStoresTheIdentityTheLockAndTheLog(t *testing.T) {
	if testing.Short() {
		t.Skip("builds the binary")
	}
	goExe := filepath.Join(runtime.GOROOT(), "bin", "go")
	if runtime.GOOS == "windows" {
		goExe += ".exe"
	}
	if _, err := os.Stat(goExe); err != nil {
		t.Skipf("no go tool at %s", goExe)
	}
	bin := filepath.Join(t.TempDir(), "fms-exchange-server")
	if runtime.GOOS == "windows" {
		bin += ".exe"
	}
	build := exec.Command(goExe, "build", "-o", bin, ".")
	if out, err := build.CombinedOutput(); err != nil {
		t.Fatalf("build: %v\n%s", err, out)
	}

	dir := t.TempDir()
	env := append(os.Environ(), config.EnvDataDir+"="+dir)
	gen := exec.Command(bin, "account", "add", "alice", "--generate")
	gen.Env = env
	pwOut, err := gen.Output()
	if err != nil {
		t.Fatalf("account add: %v", err)
	}
	pw := strings.TrimSpace(string(pwOut))
	cfgFile := filepath.Join(dir, config.FileName)
	cfg, _ := config.Load(cfgFile)
	port := freePort(t)
	cfg.Listen = "127.0.0.1:" + strconv.Itoa(port)
	lo, hi := testkit.FreeRange(5)
	cfg.PortRange = strconv.Itoa(lo) + "-" + strconv.Itoa(hi)
	config.Save(cfgFile, cfg)

	srv := exec.Command(bin, "serve")
	srv.Env = env
	stdout, _ := srv.StdoutPipe()
	if err := srv.Start(); err != nil {
		t.Fatal(err)
	}
	defer srv.Process.Kill()
	var fp string
	sc := bufio.NewScanner(stdout)
	for sc.Scan() {
		if rest, ok := strings.CutPrefix(sc.Text(), "tls fingerprint "); ok {
			fp = rest
			break
		}
	}
	if fp == "" {
		t.Fatal("serve never printed its fingerprint")
	}
	addr := "127.0.0.1:" + strconv.Itoa(port)

	// a device that is enrolled, online, has published a resource with its access, and holds a tunnel
	devID := wire.NewID()
	c, _ := testkit.DialTLS(addr, fp)
	_ = c.Send(&wire.Enroll{Login: "alice", Password: pw, DeviceID: devID, DeviceName: "T", Product: "fms-windows"})
	r, err := c.Recv(10 * time.Second)
	en, ok := r.(*wire.Enrolled)
	if err != nil || !ok {
		t.Fatalf("%v %+v", err, r)
	}
	c.Authenticated()
	_ = c.Send(&wire.Publish{Resource: json.RawMessage(`{"resourceId":"` + wire.NewID() + `","kind":"sftp-share","name":"n","access":{"descriptor":"FMSSFTP2.kill-test-secret"},"shareId":"` + wire.NewID() + `"}`)})
	if r, err := c.Recv(10 * time.Second); err != nil || r.TypeName() != wire.TypePublished {
		t.Fatalf("publish: %v %+v", err, r)
	}
	time.Sleep(200 * time.Millisecond)

	if err := srv.Process.Kill(); err != nil { // the hard kill, mid-session
		t.Fatal(err)
	}
	srv.Wait()

	allowed := map[string]bool{
		config.FileName: true, filepath.Join(config.LogsDir, "exchange-"+time.Now().UTC().Format("2006-01-02")+".log"): true,
		filepath.Join(config.TLSDir, config.CertName): true, filepath.Join(config.TLSDir, config.KeyName): true,
		accounts.AccountsFile: true, accounts.DevicesFile: true, "directory.json": true, deploy.LockFile: true,
	}
	if runtime.GOOS != "windows" {
		allowed[deploy.AdminSocketName] = true // the admin channel is a socket in the directory off Windows
	}
	filepath.Walk(dir, func(p string, info os.FileInfo, err error) error {
		if err != nil || info.IsDir() {
			return nil
		}
		rel, _ := filepath.Rel(dir, p)
		if !allowed[rel] && !(strings.HasPrefix(filepath.ToSlash(rel), "logs/") && logx.IsDayFile(filepath.Base(rel))) {
			t.Errorf("the data directory holds an unexpected file after a kill: %s", rel)
		}
		if b, _ := os.ReadFile(p); bytes.Contains(b, []byte("kill-test-secret")) || bytes.Contains(b, []byte(pw)) || bytes.Contains(b, []byte(en.DeviceToken)) {
			t.Errorf("a secret reached %s", rel)
		}
		return nil
	})
	logb, _ := os.ReadFile(filepath.Join(dir, config.LogsDir, "exchange-"+time.Now().UTC().Format("2006-01-02")+".log"))
	if bytes.Contains(logb, []byte(devID)) {
		t.Fatal("the log carries a device id")
	}
	if !bytes.Contains(logb, []byte("enrolled")) || !bytes.Contains(logb, []byte("published")) {
		t.Fatalf("the log should have recorded the enrollment and the publication:\n%s", logb)
	}
	// the lock died with the process: the next server starts at once
	release, err := deploy.LockDir(dir)
	if err != nil {
		t.Fatalf("the lock must not outlive a killed process: %v", err)
	}
	release()
}

func TestSetListenStoresTheAddressAndKeepsTheRest(t *testing.T) {
	cfgPath := filepath.Join(t.TempDir(), "exchange.json")
	var out, errb bytes.Buffer
	if code := run(context.Background(), []string{"set-endpoint", "--config", cfgPath, "exchange.example.org:22022"}, strings.NewReader(""), &out, &errb); code != exitOK {
		t.Fatalf("set-endpoint: %d %s", code, errb.String())
	}
	before, _ := config.Load(cfgPath)
	out.Reset()
	errb.Reset()
	if code := run(context.Background(), []string{"set-listen", "--config", cfgPath, ":23456"}, strings.NewReader(""), &out, &errb); code != exitOK {
		t.Fatalf("set-listen: %d %s", code, errb.String())
	}
	after, err := config.Load(cfgPath)
	if err != nil || after.Listen != ":23456" || after.PublicEndpoint != before.PublicEndpoint || after.PublicEndpoint != "exchange.example.org:22022" {
		t.Fatalf("after: %+v %v", after, err)
	}
	if code := run(context.Background(), []string{"set-listen", "--config", cfgPath, "not-an-address"}, strings.NewReader(""), &out, &errb); code != exitUsage {
		t.Fatalf("a bad address must be refused with the usage code, got %d", code)
	}
	if again, _ := config.Load(cfgPath); again.Listen != ":23456" {
		t.Fatalf("a refused address must not change the file: %q", again.Listen)
	}
	if code := run(context.Background(), []string{"set-listen", "--config", cfgPath}, strings.NewReader(""), &out, &errb); code != exitUsage {
		t.Fatalf("no address: %d", code)
	}
}

func TestSetEndpointValidatesAndClears(t *testing.T) {
	p := filepath.Join(t.TempDir(), "exchange.json")
	for _, bad := range []string{"localhost:22022", "127.0.0.1:22022", "host", "0.0.0.0:22022"} {
		if code, _, _ := execute(t, []string{"set-endpoint", "--config", p, bad}, ""); code != exitUsage {
			t.Errorf("%q: exit %d, want usage", bad, code)
		}
	}
	if _, err := os.Stat(p); err == nil {
		t.Fatal("a refused endpoint must not create the file")
	}
	if code, _, _ := execute(t, []string{"set-endpoint", "--config", p, "203.0.113.43:22022"}, ""); code != 0 {
		t.Fatal("a good endpoint")
	}
	if c, _ := config.Load(p); c.PublicEndpoint != "203.0.113.43:22022" {
		t.Fatalf("%q", c.PublicEndpoint)
	}
	if code, _, _ := execute(t, []string{"set-endpoint", "--config", p, "-"}, ""); code != 0 {
		t.Fatal("clear")
	}
	if c, _ := config.Load(p); c.PublicEndpoint != "" {
		t.Fatalf("%q", c.PublicEndpoint)
	}
}

// Ticket 187 T-2: the operator's connection string.
func TestConnectionStringRefusesWithNoPublicEndpointAndCreatesNothing(t *testing.T) {
	p := filepath.Join(t.TempDir(), "data", "exchange.json")
	code, out, errText := execute(t, []string{"connection-string", "--config", p}, "")
	if code != exitUsage || out != "" || !strings.Contains(errText, "set it first") {
		t.Fatalf("exit %d stdout %q stderr %q", code, out, errText)
	}
	if _, err := os.Stat(filepath.Dir(p)); err == nil {
		t.Fatal("a refusal must not create the data directory")
	}
}

func TestConnectionStringIsBuiltFromTheEndpointAndTheRealCertificate(t *testing.T) {
	p := cfgPath(t)
	if code, _, e := execute(t, []string{"inspect", "--init", "--config", p}, ""); code != 0 {
		t.Fatalf("init: %d %s", code, e)
	}
	// the identity exists but the address for clients is not set yet
	if code, _, _ := execute(t, []string{"connection-string", "--config", p}, ""); code != exitUsage {
		t.Fatalf("no endpoint: exit %d", code)
	}
	if code, _, e := execute(t, []string{"set-endpoint", "--config", p, "203.0.113.43:22022"}, ""); code != 0 {
		t.Fatalf("%d %s", code, e)
	}
	fp := fingerprintOf(t, p)
	if !strings.HasPrefix(fp, "SHA256:") {
		t.Fatalf("fingerprint %q", fp)
	}

	// the default: no name, no login hint; the generated leaf is self-signed, so its pin travels
	code, out, _ := execute(t, []string{"connection-string", "--config", p}, "")
	want, err := connstr.New("203.0.113.43:22022", fp, "", "")
	if err != nil {
		t.Fatal(err)
	}
	wantText, _ := want.String()
	if code != 0 || out != wantText+"\n" {
		t.Fatalf("exit %d, got %q, want %q", code, out, wantText)
	}
	back, err := connstr.Parse(strings.TrimSpace(out))
	if err != nil || back.Fingerprint != fp || back.Name != "" || back.Login != "" {
		t.Fatalf("%v %+v", err, back)
	}

	// the optional members
	code, out, _ = execute(t, []string{"connection-string", "--config", p, "--name", "Home NAS", "--login", "Alice"}, "")
	back, err = connstr.Parse(strings.TrimSpace(out))
	if code != 0 || err != nil || back.Name != "Home NAS" || back.Login != "alice" {
		t.Fatalf("exit %d %q %v %+v", code, out, err, back)
	}
	// a login or a name the string cannot carry is refused, not trimmed
	if code, _, _ := execute(t, []string{"connection-string", "--config", p, "--login", "a"}, ""); code != exitUsage {
		t.Fatalf("a short login: exit %d", code)
	}
	// the options belong to this command alone
	if code, _, _ := execute(t, []string{"inspect", "--config", p, "--name", "x"}, ""); code != exitUsage {
		t.Fatalf("--name on inspect: exit %d", code)
	}
}

func TestConnectionStringWithNoCertificateIsAConfigurationFailure(t *testing.T) {
	p := filepath.Join(t.TempDir(), "exchange.json")
	if code, _, _ := execute(t, []string{"set-endpoint", "--config", p, "203.0.113.43:22022"}, ""); code != 0 {
		t.Fatal("set-endpoint")
	}
	code, out, errText := execute(t, []string{"connection-string", "--config", p}, "")
	if code != exitConfig || out != "" || errText == "" {
		t.Fatalf("exit %d stdout %q stderr %q", code, out, errText)
	}
}
