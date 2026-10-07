package main

import (
	"bytes"
	"context"
	"errors"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/deploy"
)

// fakeProbe stands in for the SCM so the refuse-beside-the-service rule is tested without a service.
type fakeProbe struct {
	running    bool
	runningErr error
	held       bool
	calls      int
}

func (f *fakeProbe) Running() (bool, error) { f.calls++; return f.running, f.runningErr }
func (f *fakeProbe) HoldsPort(int) (bool, error) {
	f.calls++
	return f.held, nil
}

func withProbe(t *testing.T, p deploy.ServiceProbe) {
	t.Helper()
	old := deploy.Probe
	deploy.Probe = p
	t.Cleanup(func() { deploy.Probe = old })
}

func TestParseArgsStopAndService(t *testing.T) {
	ok := []struct {
		in   []string
		want command
	}{
		{[]string{"stop"}, command{name: "stop"}},
		{[]string{"stop", "--config", "a.json"}, command{name: "stop", config: "a.json"}},
		{[]string{"stop", "--config=a.json"}, command{name: "stop", config: "a.json"}},
		{[]string{"serve", "--service"}, command{name: "serve", service: true}},
		{[]string{"serve", "--service", "--config", "a.json"}, command{name: "serve", service: true, config: "a.json"}},
		{[]string{"serve", "--config", "a.json", "--service"}, command{name: "serve", service: true, config: "a.json"}},
	}
	for _, c := range ok {
		got, err := parseArgs(c.in)
		if err != nil || got != c.want {
			t.Errorf("%v: got %+v %v, want %+v", c.in, got, err, c.want)
		}
	}
	bad := [][]string{
		{"stop", "--service"}, {"stop", "--generate"}, {"stop", "--init"}, {"stop", "--nope"},
		{"stop", "--config"}, {"stop", "stray"}, {"stop", "--service=1"},
		{"serve", "--service=1"}, {"inspect", "--service"}, {"account", "--service"},
	}
	for _, in := range bad {
		if _, err := parseArgs(in); err == nil {
			t.Errorf("%v must be a usage error", in)
		}
	}
	if code, _, stderr := execute(t, []string{"stop", "--service"}, ""); code != exitUsage || !strings.Contains(stderr, "usage:") {
		t.Errorf("a bad stop line exits 2 and prints usage: %d %q", code, stderr)
	}
}

func TestStopWithNothingRunningExits1(t *testing.T) {
	code, stdout, stderr := execute(t, []string{"stop", "--config", cfgPath(t)}, "")
	if code != exitFailure || stdout != "" || strings.TrimSpace(stderr) == "" {
		t.Fatalf("exit %d, stdout %q, stderr %q", code, stdout, stderr)
	}
	if strings.Count(strings.TrimSpace(stderr), "\n") != 0 {
		t.Fatalf("one line to stderr, got %q", stderr)
	}
}

func TestStopDoesNotCreateTheConfigOrTheDataDirectory(t *testing.T) {
	p := cfgPath(t)
	execute(t, []string{"stop", "--config", p}, "")
	if _, err := os.Stat(filepath.Dir(p)); err == nil {
		t.Fatal("stop must be a pure request: it created the data directory")
	}
}

func TestServeServiceFromAConsoleIsRefused(t *testing.T) {
	p := cfgPath(t)
	code, stdout, stderr := execute(t, []string{"serve", "--service", "--config", p}, "")
	if code != exitUsage || stdout != "" || !strings.Contains(stderr, "--service") {
		t.Fatalf("exit %d, stdout %q, stderr %q", code, stdout, stderr)
	}
	if _, err := os.Stat(p); err == nil {
		t.Fatal("a refused --service must not touch the config")
	}
}

func TestServeRefusesBesideARunningService(t *testing.T) {
	cfgFile, _, _ := prepare(t)
	withProbe(t, &fakeProbe{running: true})
	code, stdout, stderr := execute(t, []string{"serve", "--config", cfgFile}, "")
	if code != exitService || stdout != "" {
		t.Fatalf("exit %d, stdout %q", code, stdout)
	}
	if !strings.Contains(stderr, deploy.ServiceName) || !strings.Contains(stderr, "service wins") {
		t.Fatalf("the refusal must say why: %q", stderr)
	}
	// it started nothing: no identity, no log
	dir := filepath.Dir(cfgFile)
	for _, rel := range []string{"tls", config.LogsDir} {
		if _, err := os.Stat(filepath.Join(dir, rel)); err == nil {
			t.Errorf("a refused serve created %s", rel)
		}
	}
}

func TestServeRefusesWhileTheServiceProcessHoldsThePort(t *testing.T) {
	cfgFile, _, port := prepare(t)
	withProbe(t, &fakeProbe{held: true})
	code, _, stderr := execute(t, []string{"serve", "--config", cfgFile}, "")
	if code != exitService || !strings.Contains(stderr, strconv.Itoa(port)) || !strings.Contains(stderr, "service wins") {
		t.Fatalf("exit %d, stderr %q", code, stderr)
	}
}

func TestServeStartsWhenTheProbeCannotAnswerButSaysSo(t *testing.T) {
	cfgFile, _, port := prepare(t)
	withProbe(t, &fakeProbe{runningErr: errors.New("access denied")})
	ctx, cancel := context.WithCancel(context.Background())
	var out bytes.Buffer
	done := make(chan int, 1)
	go func() { done <- run(ctx, []string{"serve", "--config", cfgFile}, strings.NewReader(""), &out, &out) }()
	waitListening(t, "127.0.0.1:"+strconv.Itoa(port))
	cancel()
	select {
	case c := <-done:
		if c != 0 {
			t.Fatalf("exit %d:\n%s", c, out.String())
		}
	case <-time.After(10 * time.Second):
		t.Fatal("serve did not stop")
	}
	if !strings.Contains(out.String(), "access denied") {
		t.Fatalf("a probe failure is mentioned, not hidden:\n%s", out.String())
	}
}

// TestTheServiceHostDoesNotApplyTheConsoleRule: the service itself runs serveCore with console=false
// and must neither consult the probe nor wait on the console stop event.
func TestTheServiceHostDoesNotApplyTheConsoleRule(t *testing.T) {
	cfgFile, _, port := prepare(t)
	probe := &fakeProbe{running: true, held: true}
	withProbe(t, probe)
	ctx, cancel := context.WithCancel(context.Background())
	var out bytes.Buffer
	done := make(chan int, 1)
	go func() { done <- serveCore(ctx, command{name: "serve", config: cfgFile}, false, &out, &out) }()
	waitListening(t, "127.0.0.1:"+strconv.Itoa(port))
	cancel()
	select {
	case c := <-done:
		if c != 0 {
			t.Fatalf("exit %d:\n%s", c, out.String())
		}
	case <-time.After(10 * time.Second):
		t.Fatal("serveCore did not stop")
	}
	if probe.calls != 0 {
		t.Fatalf("the service host asked the probe %d times", probe.calls)
	}
}
