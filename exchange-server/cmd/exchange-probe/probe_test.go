package main

import (
	"bufio"
	"bytes"
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
	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/testkit"
)

// Ticket 178 T-2 acceptance 3: the probe's verdict line is PASS against a fresh deployment and names the
// step it stopped at otherwise. The same code runs here in process against the test kit's server and
// against the real fms-exchange-server binary built from this module.

const allSteps = "enroll, hello, publish, list, pairing, subscribe, tunnel-resource, tunnel-share, relay-upload, relay-listen, cast, revoke"

func loose() *limits.Config {
	c := limits.DefaultConfig()
	for i := range c.Rates {
		c.Rates[i] = limits.Rate{PerMinute: 60000, Burst: 10000}
	}
	return &c
}

func startServer(t *testing.T, relay bool, endpoint string) *testkit.Env {
	t.Helper()
	return testkit.StartServer(t, testkit.Options{Limits: loose(), LoginLimits: loose(), Logins: []string{"probe"},
		WithRelay: relay, Cast: true, PublicEndpoint: endpoint})
}

func probe(t *testing.T, args []string, stdin string) (code int, stdout, stderr string) {
	t.Helper()
	var out, errb bytes.Buffer
	code = run(args, strings.NewReader(stdin), &out, &errb)
	return code, out.String(), errb.String()
}

func oneLine(t *testing.T, out string) string {
	t.Helper()
	if strings.Count(out, "\n") != 1 || !strings.HasPrefix(out, "exchange-probe: ") {
		t.Fatalf("the probe ends in exactly one verdict line, got %q", out)
	}
	return strings.TrimSpace(out)
}

func base(env *testkit.Env, extra ...string) []string {
	return append([]string{"--server", env.Addr, "--fingerprint", env.Fingerprint, "--login", "probe", "--password-stdin"}, extra...)
}

func TestThePassesAgainstAServerThatOffersEverythingAndLeavesTheAccountAsItWasFound(t *testing.T) {
	env := startServer(t, true, "203.0.113.5:22022")
	code, out, errb := probe(t, base(env, "-v"), testkit.TestPassword+"\n")
	if line := oneLine(t, out); code != 0 || line != "exchange-probe: PASS ("+allSteps+")" {
		t.Fatalf("%d %q\n%s", code, line, errb)
	}
	for _, step := range strings.Split(allSteps, ", ") {
		if !strings.Contains(errb, "ok  "+step) {
			t.Errorf("-v should show %s passing:\n%s", step, errb)
		}
	}
	// every device it enrolled is revoked, everything it published is gone
	deadline := time.Now().Add(3 * time.Second)
	for time.Now().Before(deadline) {
		if c := env.Dir.Counts(); c.Devices == 0 && c.Resources == 0 && c.Broadcasts == 0 {
			return
		}
		time.Sleep(20 * time.Millisecond)
	}
	t.Fatalf("the probe left %+v behind", env.Dir.Counts())
}

func TestItNamesTheStepItStoppedAt(t *testing.T) {
	good := startServer(t, true, "203.0.113.5:22022")
	for _, c := range []struct {
		name  string
		args  func() []string
		stdin string
		step  string
		why   string
	}{
		{"a wrong password", func() []string { return base(good) }, "Wrongpass1\n", "enroll", "bad-credentials"},
		{"a wrong pin", func() []string {
			return []string{"--server", good.Addr, "--fingerprint", "SHA256:AAAA", "--login", "probe", "--password-stdin"}
		}, testkit.TestPassword + "\n", "enroll", "pinned"},
	} {
		code, out, _ := probe(t, c.args(), c.stdin)
		line := oneLine(t, out)
		if code != 1 || !strings.HasPrefix(line, `exchange-probe: FAIL at step "`+c.step+`"`) || !strings.Contains(line, c.why) {
			t.Errorf("%s: %d %q", c.name, code, line)
		}
	}
	// a server with no relay: the relay step says so
	norelay := startServer(t, false, "203.0.113.5:22022")
	code, out, _ := probe(t, base(norelay), testkit.TestPassword+"\n")
	if line := oneLine(t, out); code != 1 || !strings.Contains(line, `FAIL at step "relay-upload"`) || !strings.Contains(line, "does not offer the relay") {
		t.Errorf("no relay: %d %q", code, line)
	}
	// a server that does not know its public endpoint hides tunnel and relay
	noendpoint := startServer(t, true, "")
	code, out, _ = probe(t, base(noendpoint), testkit.TestPassword+"\n")
	if line := oneLine(t, out); code != 1 || !strings.Contains(line, `FAIL at step "tunnel-resource"`) || !strings.Contains(line, "public endpoint") {
		t.Errorf("no endpoint: %d %q", code, line)
	}
	// ...and what the probe did before it stopped is undone
	deadline := time.Now().Add(3 * time.Second)
	for time.Now().Before(deadline) && noendpoint.Dir.Counts().Devices != 0 {
		time.Sleep(20 * time.Millisecond)
	}
	if c := noendpoint.Dir.Counts(); c.Devices != 0 || c.Resources != 0 {
		t.Errorf("a failed run must clean up after itself: %+v", c)
	}
}

func TestASkippedPartIsNamedAndNeverCountedAsPassed(t *testing.T) {
	env := startServer(t, false, "")
	code, out, _ := probe(t, base(env, "--skip", "tunnel,relay,cast"), testkit.TestPassword+"\n")
	want := "exchange-probe: PASS (enroll, hello, publish, list, pairing, subscribe, revoke; skipped: tunnel, relay, cast)"
	if line := oneLine(t, out); code != 0 || line != want {
		t.Fatalf("%d %q", code, line)
	}
}

func TestTrustFirstUsePinsWhatTheServerPresentsAndSaysSo(t *testing.T) {
	env := startServer(t, true, "203.0.113.5:22022")
	code, out, errb := probe(t, []string{"--server", env.Addr, "--trust-first-use", "--login", "probe", "--password-stdin"}, testkit.TestPassword+"\n")
	if line := oneLine(t, out); code != 0 || !strings.HasPrefix(line, "exchange-probe: PASS") {
		t.Fatalf("%d %q", code, line)
	}
	if !strings.Contains(errb, "pinned "+env.Fingerprint) {
		t.Fatalf("the pinned fingerprint is printed so the operator can compare it: %q", errb)
	}
}

func TestUsageErrorsAreCouldNotRunAndNeverAPass(t *testing.T) {
	for name, c := range map[string]struct {
		args  []string
		stdin string
		want  string
	}{
		"no server":              {[]string{"--fingerprint", "SHA256:x", "--login", "a", "--password-stdin"}, "pw\n", "--server"},
		"no login":               {[]string{"--server", "h:1", "--fingerprint", "SHA256:x", "--password-stdin"}, "pw\n", "--login"},
		"no password flag":       {[]string{"--server", "h:1", "--fingerprint", "SHA256:x", "--login", "a"}, "pw\n", "--password-stdin"},
		"no password on stdin":   {[]string{"--server", "h:1", "--fingerprint", "SHA256:x", "--login", "a", "--password-stdin"}, "", "standard input"},
		"no pin":                 {[]string{"--server", "h:1", "--login", "a", "--password-stdin"}, "pw\n", "no pin"},
		"a skip that is no part": {[]string{"--server", "h:1", "--fingerprint", "SHA256:x", "--login", "a", "--password-stdin", "--skip", "enroll"}, "pw\n", "--skip"},
	} {
		code, out, _ := probe(t, c.args, c.stdin)
		if line := oneLine(t, out); code != 2 || !strings.Contains(line, "COULD NOT RUN") || !strings.Contains(line, c.want) {
			t.Errorf("%s: %d %q", name, code, line)
		}
	}
}

func TestAServerThatNeverAnswersIsATimeoutNamingTheStep(t *testing.T) {
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer ln.Close()
	go func() {
		for {
			c, err := ln.Accept()
			if err != nil {
				return
			}
			_ = c // accepted and never spoken to: the handshake never completes
		}
	}()
	code, out, _ := probe(t, []string{"--server", ln.Addr().String(), "--fingerprint", "SHA256:AAAA", "--login", "probe", "--password-stdin",
		"--timeout", "700ms"}, "pw\n")
	if line := oneLine(t, out); code != 1 || !strings.Contains(line, "FAIL at step") {
		t.Fatalf("%d %q", code, line)
	}
}

// the real thing: the binary the Server edition ships, built from this module, serving on loopback
func TestThePassesAgainstTheBuiltServerBinary(t *testing.T) {
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
	if out, err := exec.Command(goExe, "build", "-o", bin, "fms/exchange-server/cmd/fms-exchange-server").CombinedOutput(); err != nil {
		t.Fatalf("build: %v\n%s", err, out)
	}

	dir := t.TempDir()
	env := append(os.Environ(), config.EnvDataDir+"="+dir)
	gen := exec.Command(bin, "account", "add", "probe", "--generate")
	gen.Env = env
	pwOut, err := gen.Output()
	if err != nil {
		t.Fatalf("account add: %v", err)
	}
	pw := strings.TrimSpace(string(pwOut))

	cfgFile := filepath.Join(dir, config.FileName)
	cfg, _ := config.Load(cfgFile)
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	port := ln.Addr().(*net.TCPAddr).Port
	_ = ln.Close()
	addr := "127.0.0.1:" + strconv.Itoa(port)
	cfg.Listen = addr
	lo, hi := testkit.FreeRange(5)
	cfg.PortRange = strconv.Itoa(lo) + "-" + strconv.Itoa(hi)
	cfg.PublicEndpoint = "203.0.113.5:22022" // what the server tells clients; the probe dials the address it was given
	if err := config.Save(cfgFile, cfg); err != nil {
		t.Fatal(err)
	}

	srv := exec.Command(bin, "serve")
	srv.Env = env
	stdout, _ := srv.StdoutPipe()
	if err := srv.Start(); err != nil {
		t.Fatal(err)
	}
	defer func() { _ = srv.Process.Kill(); _ = srv.Wait() }()
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
	go func() { _, _ = bufio.NewReader(stdout).WriteTo(os.Stderr) }() // keep the pipe drained

	var out, errb bytes.Buffer
	code := run([]string{"--server", addr, "--fingerprint", fp, "--login", "probe", "--password-stdin", "-v"}, strings.NewReader(pw+"\n"), &out, &errb)
	if line := oneLine(t, out.String()); code != 0 || line != "exchange-probe: PASS ("+allSteps+")" {
		t.Fatalf("%d %q\n%s", code, line, errb.String())
	}

	// the account is as it was found: the server's own administration says no device is left
	dev := exec.Command(bin, "device", "list", "probe", "--json")
	dev.Env = env
	listing, err := dev.Output()
	if err != nil {
		t.Fatalf("device list: %v", err)
	}
	if bytes.Contains(listing, []byte(`"deviceId"`)) || bytes.Contains(listing, []byte(`"id"`)) {
		t.Fatalf("the probe left a device behind:\n%s", listing)
	}
}
