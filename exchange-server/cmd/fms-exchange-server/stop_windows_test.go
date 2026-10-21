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
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
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
	if !hasStopLine(filepath.Join(filepath.Dir(cfgFile), config.LogsDir, "exchange-"+time.Now().UTC().Format("2006-01-02")+".log")) {
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
	gen := exec.Command(bin, "account", "add", "alice", "--generate")
	gen.Env = env
	if _, err := gen.Output(); err != nil {
		t.Fatalf("account add: %v", err)
	}
	adminOn := exec.Command(bin, "admin", "on", "--generate")
	adminOn.Env = env
	adminOutput, err := adminOn.Output()
	if err != nil {
		t.Fatalf("admin on: %v", err)
	}
	adminPassword := strings.Split(strings.TrimSpace(string(adminOutput)), "\n")[0]
	cfgFile := filepath.Join(dir, config.FileName)
	cfg, _ := config.Load(cfgFile)
	port := freePort(t)
	cfg.Listen = "127.0.0.1:" + strconv.Itoa(port)
	cfg.PublicEndpoint = "exchange.example.com:" + strconv.Itoa(port)
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
	fingerprint := ""
	for sc.Scan() {
		if strings.HasPrefix(sc.Text(), "tls fingerprint ") {
			listening = true
			fingerprint = strings.TrimPrefix(sc.Text(), "tls fingerprint ")
			break
		}
	}
	if !listening {
		t.Fatal("serve never printed its fingerprint")
	}

	// Ticket 245: exercise the built process, then scan its persisted state and real logs for secrets.
	client, err := testkit.DialTLS(cfg.Listen, fingerprint)
	if err != nil {
		t.Fatal(err)
	}
	defer client.Close()
	if err := client.Send(&wire.AdminAuth{Password: adminPassword}); err != nil {
		t.Fatal(err)
	}
	auth, err := client.Recv(3 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	authOK, ok := auth.(*wire.AdminAuthOK)
	if !ok {
		t.Fatalf("expected operator authentication, got %T", auth)
	}
	client.Authenticated()
	if err := client.Send(&wire.AdminRequest{ID: 1, Token: authOK.Token, Op: "invitation.create", Kind: "member"}); err != nil {
		t.Fatal(err)
	}
	created, err := client.Recv(3 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	reply, ok := created.(*wire.AdminReply)
	if !ok || !reply.OK {
		t.Fatal("invitation creation failed")
	}
	var invitation struct{ Code, ConnectionString string }
	if err := json.Unmarshal(reply.Data, &invitation); err != nil || invitation.Code == "" || invitation.ConnectionString == "" {
		t.Fatal("invitation response incomplete")
	}
	if err := client.Send(&wire.AdminRequest{ID: 2, Token: authOK.Token, Op: "invitation.list"}); err != nil {
		t.Fatal(err)
	}
	listed, err := client.Recv(3 * time.Second)
	if err != nil {
		t.Fatal(err)
	}
	listedJSON, _ := json.Marshal(listed)
	if strings.Contains(string(listedJSON), invitation.Code) || strings.Contains(string(listedJSON), invitation.ConnectionString) {
		t.Fatal("invitation secret returned after creation")
	}
	client.Close()

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
	if !hasStopLine(filepath.Join(dir, config.LogsDir, "exchange-"+time.Now().UTC().Format("2006-01-02")+".log")) {
		b, _ := os.ReadFile(filepath.Join(dir, config.LogsDir, "exchange-"+time.Now().UTC().Format("2006-01-02")+".log"))
		t.Fatalf("the log has no stop line:\n%s", b)
	}
	if err := filepath.WalkDir(dir, func(path string, entry os.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if entry.IsDir() {
			return nil
		}
		data, err := os.ReadFile(path)
		if err != nil {
			return err
		}
		for _, secret := range []string{adminPassword, authOK.Token, invitation.Code, invitation.ConnectionString} {
			if bytes.Contains(data, []byte(secret)) {
				t.Errorf("secret persisted in %s", entry.Name())
			}
		}
		return nil
	}); err != nil {
		t.Fatal(err)
	}
}
