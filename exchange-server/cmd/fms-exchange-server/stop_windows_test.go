package main

import (
	"bufio"
	"bytes"
	"context"
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
	"fms/exchange-server/internal/testkit"
)

func hasStopLine(logFile string) bool {
	b, _ := os.ReadFile(logFile)
	return strings.Contains(string(b), "\tINFO\tstop")
}

// TestStopRoundTripInProcess: a console serve waits on the stop event of its config; `stop` sets
// it; serve closes cleanly (exit 0) and writes the same `stop` log line Ctrl+C does.
func TestStopRoundTripInProcess(t *testing.T) {
	cfgFile, _, port := prepare(t)
	addr := "127.0.0.1:" + strconv.Itoa(port)
	var out bytes.Buffer
	done := make(chan int, 1)
	go func() {
		done <- run(context.Background(), []string{"serve", "--config", cfgFile}, strings.NewReader(""), &out, &out)
	}()
	waitListening(t, addr)

	code, stdout, stderr := execute(t, []string{"stop", "--config", cfgFile}, "")
	if code != 0 || !strings.Contains(stdout, "stop requested") || stderr != "" {
		t.Fatalf("stop: exit %d, stdout %q, stderr %q", code, stdout, stderr)
	}
	select {
	case c := <-done:
		if c != 0 {
			t.Fatalf("serve exited %d after a stop request:\n%s", c, out.String())
		}
	case <-time.After(10 * time.Second):
		t.Fatal("serve did not stop on the stop request")
	}
	if !hasStopLine(filepath.Join(filepath.Dir(cfgFile), config.LogName)) {
		t.Fatal("the log has no stop line")
	}
	// the event is gone with the server: a second stop finds nothing
	if code, _, _ := execute(t, []string{"stop", "--config", cfgFile}, ""); code != exitFailure {
		t.Fatalf("a stop after the server ended must exit 1, got %d", code)
	}
	// the port is free again
	waitFree(t, addr)
}

func waitFree(t *testing.T, addr string) {
	t.Helper()
	for i := 0; i < 100; i++ {
		if ln, err := net.Listen("tcp", addr); err == nil {
			ln.Close()
			return
		}
		time.Sleep(50 * time.Millisecond)
	}
	t.Fatalf("%s is still held after the stop", addr)
}

// TestStopRoundTripWithTheBuiltBinary is the real shape: two processes, as the Share Manager uses it.
func TestStopRoundTripWithTheBuiltBinary(t *testing.T) {
	if testing.Short() {
		t.Skip("builds the binary")
	}
	goExe := filepath.Join(runtime.GOROOT(), "bin", "go.exe")
	if _, err := os.Stat(goExe); err != nil {
		t.Skipf("no go tool at %s", goExe)
	}
	bin := filepath.Join(t.TempDir(), "fms-exchange-server.exe")
	if out, err := exec.Command(goExe, "build", "-o", bin, ".").CombinedOutput(); err != nil {
		t.Fatalf("build: %v\n%s", err, out)
	}

	dir := t.TempDir()
	env := append(os.Environ(), config.EnvDataDir+"="+dir)
	gen := exec.Command(bin, "set-password", "--generate")
	gen.Env = env
	if _, err := gen.Output(); err != nil {
		t.Fatalf("set-password: %v", err)
	}
	cfgFile := filepath.Join(dir, config.FileName)
	cfg, _ := config.Load(cfgFile)
	port := freePort(t)
	cfg.Listen = "127.0.0.1:" + strconv.Itoa(port)
	lo, hi := testkit.FreeRange(5)
	cfg.PortRange = strconv.Itoa(lo) + "-" + strconv.Itoa(hi)
	if err := config.Save(cfgFile, cfg); err != nil {
		t.Fatal(err)
	}

	// nothing running yet: exit 1, one line on stderr
	st := exec.Command(bin, "stop")
	st.Env = env
	var serr bytes.Buffer
	st.Stderr = &serr
	if err := st.Run(); err == nil || st.ProcessState.ExitCode() != 1 || strings.TrimSpace(serr.String()) == "" {
		t.Fatalf("stop with nothing running: %v, stderr %q", err, serr.String())
	}

	srv := exec.Command(bin, "serve")
	srv.Env = env
	stdout, _ := srv.StdoutPipe()
	if err := srv.Start(); err != nil {
		t.Fatal(err)
	}
	defer srv.Process.Kill()
	sc := bufio.NewScanner(stdout)
	listening := false
	for sc.Scan() {
		if strings.HasPrefix(sc.Text(), "tls fingerprint ") {
			listening = true
			break
		}
	}
	if !listening {
		t.Fatal("serve never printed its fingerprint")
	}

	// the stop names the config the same way serve does: through the data dir override
	stop := exec.Command(bin, "stop")
	stop.Env = env
	if out, err := stop.CombinedOutput(); err != nil {
		t.Fatalf("stop: %v\n%s", err, out)
	}
	waited := make(chan error, 1)
	go func() { waited <- srv.Wait() }()
	select {
	case err := <-waited:
		if err != nil || srv.ProcessState.ExitCode() != 0 {
			t.Fatalf("serve exited with %v (%d)", err, srv.ProcessState.ExitCode())
		}
	case <-time.After(15 * time.Second):
		t.Fatal("the server process did not exit after stop")
	}
	if !hasStopLine(filepath.Join(dir, config.LogName)) {
		b, _ := os.ReadFile(filepath.Join(dir, config.LogName))
		t.Fatalf("the log has no stop line:\n%s", b)
	}
}
