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

	"fms/exchange-server/internal/config"
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
		{[]string{"set-password", "--generate"}, command{name: "set-password", generate: true}},
		{[]string{"inspect", "--init", "--config", "x"}, command{name: "inspect", init: true, config: "x"}},
	}
	for _, c := range ok {
		got, err := parseArgs(c.in)
		if err != nil || got != c.want {
			t.Errorf("%v: got %+v %v, want %+v", c.in, got, err, c.want)
		}
	}
	bad := [][]string{
		nil, {"bogus"}, {"serve", "--nope"}, {"serve", "--generate"}, {"inspect", "--generate"},
		{"set-password", "--init"}, {"serve", "--config"}, {"serve", "--config="}, {"--version", "x"},
		{"serve", "stray"}, {"set-password", "--generate=1"},
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

func TestSetPasswordRefusesBelowTheFloorAndChangesNothing(t *testing.T) {
	p := cfgPath(t)
	for _, weak := range []string{"Abcdefgh1", "abcdefghij1", "ABCDEFGHIJ1", "Abcdefghij", "Абвгдежзий1A", ""} {
		code, _, stderr := execute(t, []string{"set-password", "--config", p}, weak+"\n")
		if code != exitPolicy || stderr == "" {
			t.Errorf("%q: exit %d, stderr %q", weak, code, stderr)
		}
	}
	if _, err := os.Stat(p); err == nil {
		t.Fatal("a refused password must not even create the config")
	}
	// with an existing config the bytes stay exactly as they were
	if err := config.Save(p, config.Default()); err != nil {
		t.Fatal(err)
	}
	before, _ := os.ReadFile(p)
	if code, _, _ := execute(t, []string{"set-password", "--config", p}, "Abcdefgh1\n"); code != exitPolicy {
		t.Fatal("must be refused")
	}
	after, _ := os.ReadFile(p)
	if !bytes.Equal(before, after) {
		t.Fatal("the config changed on a refused password")
	}
}

func TestSetPasswordStoresOnlyAnArgon2idRecord(t *testing.T) {
	p := cfgPath(t)
	const pw = "Str0ngPassw0rd"
	code, stdout, stderr := execute(t, []string{"set-password", "--config", p}, pw+"\r\n")
	if code != 0 || stdout != "" {
		t.Fatalf("exit %d, stdout %q, stderr %q", code, stdout, stderr)
	}
	cfg, err := config.Load(p)
	if err != nil || !strings.HasPrefix(cfg.PasswordHash, "$argon2id$") || !password.Verify(cfg.PasswordHash, pw) {
		t.Fatalf("stored record: %q %v", cfg.PasswordHash, err)
	}
	if cfg.CheckPassword() != nil {
		t.Fatal("the stored record is not valid for serve")
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
}

func TestSetPasswordGenerateMeetsThePolicyAndPrintsOnce(t *testing.T) {
	p := cfgPath(t)
	code, stdout, stderr := execute(t, []string{"set-password", "--generate", "--config", p}, "")
	if code != 0 {
		t.Fatalf("%d %s", code, stderr)
	}
	pw := strings.TrimSpace(stdout)
	if password.Check(pw) != nil || strings.Count(stdout, "\n") != 1 {
		t.Fatalf("generated %q (stdout %q)", pw, stdout)
	}
	cfg, _ := config.Load(p)
	if !password.Verify(cfg.PasswordHash, pw) {
		t.Fatal("the stored hash is not the printed password's")
	}
	if strings.Contains(stderr, pw) {
		t.Fatal("the password must only be on stdout")
	}
}

func TestSetPasswordOnABrokenConfigIsExit4(t *testing.T) {
	p := cfgPath(t)
	os.MkdirAll(filepath.Dir(p), 0o700)
	os.WriteFile(p, []byte("{ broken"), 0o600)
	if code, _, _ := execute(t, []string{"set-password", "--config", p}, "Str0ngPassw0rd\n"); code != exitConfig {
		t.Fatalf("exit %d", code)
	}
	if b, _ := os.ReadFile(p); string(b) != "{ broken" {
		t.Fatal("a broken config must be left alone")
	}
}

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
	if rep.ConfigPresent || rep.PasswordSet || rep.CertPresent || rep.Fingerprint != "" || rep.DataDir != dir {
		t.Fatalf("report on a missing directory: %+v", rep)
	}
	if _, err := os.Stat(dir); err == nil {
		t.Fatal("inspect created the data directory")
	}
}

func TestInspectInitThenInspect(t *testing.T) {
	p := cfgPath(t)
	execute(t, []string{"set-password", "--config", p}, "Str0ngPassw0rd\n")
	_, out1, _ := execute(t, []string{"inspect", "--init", "--config", p}, "")
	var a inspectReport
	_ = json.Unmarshal([]byte(out1), &a)
	if !a.CertPresent || !strings.HasPrefix(a.Fingerprint, "SHA256:") || !a.PasswordHashValid {
		t.Fatalf("after --init: %+v", a)
	}
	_, out2, _ := execute(t, []string{"inspect", "--config", p}, "")
	var b inspectReport
	_ = json.Unmarshal([]byte(out2), &b)
	if b.Fingerprint != a.Fingerprint || b.Listen != ":22022" || b.PortRange != "40000-40100" {
		t.Fatalf("plain inspect: %+v vs %+v", b, a)
	}
	if strings.Contains(out2, "argon2") {
		t.Fatal("inspect must not print the hash")
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

func TestServeRefusesWithoutAPassword(t *testing.T) {
	p := cfgPath(t)
	code, _, stderr := execute(t, []string{"serve", "--config", p}, "")
	if code != exitPolicy || !strings.Contains(stderr, "set-password") {
		t.Fatalf("exit %d, stderr %q", code, stderr)
	}
	// it left the commented defaults for the operator, and no TLS identity was made
	if _, err := os.Stat(p); err != nil {
		t.Fatalf("the commented defaults were not written: %v", err)
	}
	if _, err := os.Stat(filepath.Join(filepath.Dir(p), "tls")); err == nil {
		t.Fatal("a password-less serve must not create an identity")
	}
	// a malformed record is refused too
	cfg, _ := config.Load(p)
	cfg.PasswordHash = "$argon2id$nonsense"
	config.Save(p, cfg)
	if code, _, _ := execute(t, []string{"serve", "--config", p}, ""); code != exitPolicy {
		t.Fatalf("a malformed record must be refused: exit %d", code)
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

// prepare sets a password and points the config at a loopback port.
func prepare(t *testing.T) (cfgFile, pw string, port int) {
	t.Helper()
	cfgFile = cfgPath(t)
	code, out, errs := execute(t, []string{"set-password", "--generate", "--config", cfgFile}, "")
	if code != 0 {
		t.Fatalf("set-password: %d %s", code, errs)
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

func TestServeRunsAndTheIdentitySurvivesARestart(t *testing.T) {
	cfgFile, pw, port := prepare(t)
	addr := "127.0.0.1:" + strconv.Itoa(port)

	serveOnce := func() string {
		ctx, cancel := context.WithCancel(context.Background())
		var out bytes.Buffer
		done := make(chan int, 1)
		go func() { done <- run(ctx, []string{"serve", "--config", cfgFile}, strings.NewReader(""), &out, &out) }()
		waitListening(t, addr)
		fp := fingerprintOf(t, cfgFile)
		// a producer pinned to that fingerprint registers with the real password
		p := &testkit.Producer{Addr: addr, Pin: fp, Password: pw, ShareID: wire.NewID(), Keepalive: 30}
		reply, err := p.Register()
		if err != nil || reply.Type != wire.TypeRegistered {
			t.Fatalf("register against the served binary: %v %+v\n%s", err, reply, out.String())
		}
		p.Close()
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
	first := serveOnce()
	second := serveOnce()
	if first != second {
		t.Fatalf("the identity changed across a restart: %s vs %s", first, second)
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

// TestHardKillLeavesNothingButConfigIdentityAndLog is 133 acceptance 2 and ticket 139 T-6: sessions
// live in memory only, so after a kill in the middle of a session the data directory holds exactly
// the config, the identity and the log.
func TestHardKillLeavesNothingButConfigIdentityAndLog(t *testing.T) {
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
	gen := exec.Command(bin, "set-password", "--generate")
	gen.Env = env
	pwOut, err := gen.Output()
	if err != nil {
		t.Fatalf("set-password: %v", err)
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

	// a producer with a per-session port, and a consumer in the middle of a tunnel
	prod := &testkit.Producer{Addr: addr, Pin: fp, Password: pw, ShareID: wire.NewID(), Keepalive: 30,
		Port: wire.IntPtr(0), Handler: func(c net.Conn) { buf := make([]byte, 1); c.Read(buf) }}
	reply, err := prod.Register()
	if err != nil || reply.Type != wire.TypeRegistered {
		t.Fatalf("register: %v %+v", err, reply)
	}
	go prod.Serve()
	cc, _ := testkit.DialPlain(addr)
	m := wire.New(wire.TypeConnect)
	m.ShareID = prod.ShareID
	cc.Send(m)
	if r, err := cc.Recv(5 * time.Second); err != nil || r.Type != wire.TypeConnected {
		t.Fatalf("connect: %v %+v", err, r)
	}

	if err := srv.Process.Kill(); err != nil { // the hard kill, mid-session
		t.Fatal(err)
	}
	srv.Wait()

	allowed := map[string]bool{
		config.FileName: true, config.LogName: true,
		filepath.Join(config.TLSDir, config.CertName): true,
		filepath.Join(config.TLSDir, config.KeyName):  true,
	}
	filepath.Walk(dir, func(p string, info os.FileInfo, err error) error {
		if err != nil || info.IsDir() {
			return nil
		}
		rel, _ := filepath.Rel(dir, p)
		if !allowed[rel] {
			t.Errorf("the data directory holds an unexpected file after a kill: %s", rel)
		}
		return nil
	})
	logb, _ := os.ReadFile(filepath.Join(dir, config.LogName))
	if bytes.Contains(logb, []byte(prod.ShareID)) {
		t.Fatal("the log carries a share id")
	}
	if !bytes.Contains(logb, []byte("registered")) {
		t.Fatalf("the log should have recorded the registration:\n%s", logb)
	}
}

func TestSetListenStoresTheAddressAndKeepsTheRest(t *testing.T) {
	cfgPath := filepath.Join(t.TempDir(), "exchange.json")
	var out, errb bytes.Buffer
	if code := run(context.Background(), []string{"set-password", "--generate", "--config", cfgPath}, strings.NewReader(""), &out, &errb); code != exitOK {
		t.Fatalf("set-password: %d %s", code, errb.String())
	}
	before, _ := config.Load(cfgPath)
	out.Reset()
	errb.Reset()
	if code := run(context.Background(), []string{"set-listen", "--config", cfgPath, ":23456"}, strings.NewReader(""), &out, &errb); code != exitOK {
		t.Fatalf("set-listen: %d %s", code, errb.String())
	}
	after, err := config.Load(cfgPath)
	if err != nil || after.Listen != ":23456" || after.PasswordHash != before.PasswordHash {
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
