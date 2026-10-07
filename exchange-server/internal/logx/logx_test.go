package logx

import (
	"os"
	"path/filepath"
	"strings"
	"testing"

	"fms/exchange-server/internal/wire"
)

func TestShareNeverContainsTheID(t *testing.T) {
	for i := 0; i < 200; i++ {
		id := wire.NewID()
		line := Share(id).val
		if len(line) != 8 {
			t.Fatalf("share fingerprint must be 8 hex characters, got %q", line)
		}
		for j := 0; j+4 <= len(id); j++ {
			if strings.Contains(line, id[j:j+4]) {
				t.Fatalf("%q carries a 4-character piece of the id %q", line, id)
			}
		}
	}
}

func TestTextSanitizesAndCaps(t *testing.T) {
	f := Text("note", "a\tb\nc\x00d\x7fe")
	if strings.ContainsAny(f.val, "\t\n\x00\x7f") {
		t.Fatalf("control characters survived: %q", f.val)
	}
	long := Text("note", strings.Repeat("é", 2000)) // 2 bytes each
	if len(long.val) > MaxText {
		t.Fatalf("value not capped: %d bytes", len(long.val))
	}
	if !strings.HasSuffix(long.val, "é") {
		t.Fatalf("a cap must not cut a character in half")
	}
	if got := Text("bad key!", "x").key; got != "bad_key_" {
		t.Fatalf("key not cleaned: %q", got)
	}
}

func TestLineShape(t *testing.T) {
	c := NewCapture()
	c.Info("registered", Share("abc"), Addr("192.0.2.7:51234"), Port(40001), Int("keepalive", 30))
	lines := c.Lines()
	if len(lines) != 1 {
		t.Fatalf("lines: %v", lines)
	}
	parts := strings.Split(lines[0], "\t")
	if len(parts) != 7 || parts[1] != "INFO" || parts[2] != "registered" || parts[4] != "addr=192.0.2.7" {
		t.Fatalf("unexpected line: %q", lines[0])
	}
}

func TestNilLoggerIsSafe(t *testing.T) {
	var l *Logger
	l.Info("x", Port(1))
	if err := l.Close(); err != nil {
		t.Fatal(err)
	}
}

func TestRotation(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "exchange.log")
	l, err := Open(path, 600)
	if err != nil {
		t.Fatal(err)
	}
	defer l.Close()
	for i := 0; i < 40; i++ {
		l.Info("tick", Int("n", i))
	}
	if _, err := os.Stat(path + ".1"); err != nil {
		t.Fatalf("no rotated file: %v", err)
	}
	st, _ := os.Stat(path)
	if st.Size() > 600+200 {
		t.Fatalf("the live file grew past the bound: %d", st.Size())
	}
	// only one old generation is kept
	entries, _ := os.ReadDir(dir)
	if len(entries) != 2 {
		t.Fatalf("expected the log and one .1, found %d files", len(entries))
	}
}

func TestEveryIDTypeRendersTheFirst8HexOfItsSHA256(t *testing.T) {
	id := wire.NewID()
	want := Share(id).val
	for name, f := range map[string]Field{
		"share": Share(id), "device": Device(id), "resource": Resource(id), "broadcast": Broadcast(id),
		"tunnel": Tunnel(id), "login": Login(id),
	} {
		if f.key != name {
			t.Errorf("%s: key %q", name, f.key)
		}
		if name != "login" && f.val != want {
			t.Errorf("%s: %q, want the shared form %q", name, f.val, want)
		}
		if len(f.val) != 8 || strings.Contains(f.val, id[:4]) {
			t.Errorf("%s: %q", name, f.val)
		}
	}
	if Login("Alice").val != Login("alice").val || Login("alice").val == Login("bob").val {
		t.Fatal("a login is folded to lower case before it is hashed")
	}
	// a password typed into the login field is not in the line
	c := NewCapture()
	c.Info("enroll", Login("Sup3rSecretPw"))
	if strings.Contains(strings.Join(c.Lines(), "\n"), "Sup3rSecretPw") {
		t.Fatal("the login field carried a password into the log")
	}
}
