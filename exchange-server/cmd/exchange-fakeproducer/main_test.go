package main

import (
	"fmt"
	"io"
	"net"
	"os"
	"path/filepath"
	"strconv"
	"testing"
	"time"

	"github.com/pkg/sftp"
	"golang.org/x/crypto/ssh"

	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// TestSFTPListsAndReadsThroughThePerSessionPort is the automated stand-in for the WinSCP/FileZilla
// proof of ticket 137 T-4: a real SFTP client, speaking SSH from its first byte to the per-session
// port, lists a folder and reads a file through the bridge to a producer that attached outward.
// The tool-by-hand step (WinSCP or FileZilla against `serve`) stays in testdata/manual-winscp.md.
func TestSFTPListsAndReadsThroughThePerSessionPort(t *testing.T) {
	lo, hi := testkit.FreeRange(3)
	env := testkit.StartServer(t, testkit.Options{PortRange: [2]int{lo, hi}})

	root := t.TempDir()
	if err := os.WriteFile(filepath.Join(root, "hello.txt"), []byte("through the exchange"), 0o600); err != nil {
		t.Fatal(err)
	}
	cfg := testkit.SSHServerConfig("")
	cfg.PasswordCallback = func(c ssh.ConnMetadata, got []byte) (*ssh.Permissions, error) {
		if c.User() == "tester" && string(got) == "tester-pass" {
			return nil, nil
		}
		return nil, fmt.Errorf("denied")
	}
	p := env.NewProducer("")
	p.Port = wire.IntPtr(0)
	p.Handler = func(c net.Conn) { serveSFTP(c, cfg, root) }
	reply, err := p.Register()
	if err != nil || reply.Type != wire.TypeRegistered || reply.Port == nil {
		t.Fatalf("register: %v %+v", err, reply)
	}
	go p.Serve()
	defer p.Close()

	raw, err := net.DialTimeout("tcp", net.JoinHostPort("127.0.0.1", strconv.Itoa(*reply.Port)), 3*time.Second)
	if err != nil {
		t.Fatal(err)
	}
	client, err := testkit.SSHClient(raw, "tester", "tester-pass")
	if err != nil {
		t.Fatal(err)
	}
	defer client.Close()
	sc, err := sftp.NewClient(client)
	if err != nil {
		t.Fatalf("no SFTP subsystem through the bridge: %v", err)
	}
	defer sc.Close()

	infos, err := sc.ReadDir(".")
	if err != nil || len(infos) != 1 || infos[0].Name() != "hello.txt" {
		t.Fatalf("listing: %v %v", infos, err)
	}
	f, err := sc.Open("hello.txt")
	if err != nil {
		t.Fatal(err)
	}
	defer f.Close()
	b, _ := io.ReadAll(f)
	if string(b) != "through the exchange" {
		t.Fatalf("read %q", b)
	}

	// An upload by an ABSOLUTE path lands inside the served folder, and a path that climbs out of
	// it is refused - the proof tool must not be able to write where the scratch folder is not.
	w, err := sc.Create("/uploaded.txt")
	if err != nil {
		t.Fatalf("upload: %v", err)
	}
	if _, err := w.Write([]byte("up")); err != nil {
		t.Fatal(err)
	}
	if err := w.Close(); err != nil {
		t.Fatal(err)
	}
	if got, err := os.ReadFile(filepath.Join(root, "uploaded.txt")); err != nil || string(got) != "up" {
		t.Fatalf("absolute upload did not land in the served folder: %q %v", got, err)
	}
	if _, err := os.Stat(filepath.Join(filepath.Dir(root), "escaped.txt")); err == nil {
		t.Fatal("something exists outside the served folder before the test wrote it")
	}
	if w2, err := sc.Create("../escaped.txt"); err == nil {
		_, _ = w2.Write([]byte("x"))
		_ = w2.Close()
	}
	if _, err := os.Stat(filepath.Join(filepath.Dir(root), "escaped.txt")); err == nil {
		t.Fatal("a ../ path wrote outside the served folder")
	}
}
