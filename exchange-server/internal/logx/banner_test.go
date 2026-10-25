package logx

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

// A long-running service writes its "start" line only into the file of the day it started, so a file
// taken alone must still say which build wrote it: the first line of every file names the version.
func TestDailyLogEveryFileStartsWithTheVersion(t *testing.T) {
	dir := t.TempDir()
	now := time.Date(2026, 10, 14, 23, 59, 0, 0, time.UTC)
	l, err := OpenDaily(dir, 600, 3, func() time.Time { return now })
	if err != nil {
		t.Fatal(err)
	}
	defer l.Close()
	l.SetBanner("26.10.21.1442")
	for i := 0; i < 10; i++ {
		l.Info("busy", Text("value", strings.Repeat("x", 80)))
	}
	now = now.Add(2 * time.Minute) // past midnight: the next day's file
	l.Info("next-day")

	for _, name := range []string{"exchange-2026-10-14.log", "exchange-2026-10-14.log.1", "exchange-2026-10-15.log"} {
		data, err := os.ReadFile(filepath.Join(dir, name))
		if err != nil {
			t.Fatal(name, err)
		}
		first := strings.SplitN(string(data), "\n", 2)[0]
		if !strings.Contains(first, "\tfile-open\t") || !strings.Contains(first, "version=26.10.21.1442") {
			t.Fatalf("%s starts with %q, want the version banner", name, first)
		}
	}
}

func TestDailyLogWithoutABannerKeepsItsFirstLine(t *testing.T) {
	dir := t.TempDir()
	l, err := OpenDaily(dir, 4096, 3, nil)
	if err != nil {
		t.Fatal(err)
	}
	defer l.Close()
	l.Info("first")
	files, _ := filepath.Glob(filepath.Join(dir, "exchange-*.log"))
	if len(files) != 1 {
		t.Fatalf("day files: %v", files)
	}
	data, _ := os.ReadFile(files[0])
	if !strings.Contains(strings.SplitN(string(data), "\n", 2)[0], "\tfirst") {
		t.Fatalf("unexpected first line %q", data)
	}
}

func TestAuditEveryFileStartsWithTheServerVersion(t *testing.T) {
	dir := t.TempDir()
	now := time.Date(2026, 10, 14, 23, 59, 0, 0, time.UTC)
	a, err := OpenAuditDaily(dir, 700, 3, func() time.Time { return now })
	if err != nil {
		t.Fatal(err)
	}
	defer a.Close()
	a.SetProduct("fms-exchange-server/26.10.21.1442")
	for i := 0; i < 8; i++ {
		a.Record(AuditEvent{Op: "connection.open", Actor: "unknown", Device: "unknown", Product: "unknown", Status: AuditStatusOK, Details: strings.Repeat("x", 60)})
	}
	now = now.Add(2 * time.Minute)
	a.Record(AuditEvent{Op: "connection.open", Status: AuditStatusOK})

	for _, name := range []string{"audit-2026-10-14.log", "audit-2026-10-14.log.1", "audit-2026-10-15.log"} {
		data, err := os.ReadFile(filepath.Join(dir, name))
		if err != nil {
			t.Fatal(name, err)
		}
		first := strings.SplitN(string(data), "\n", 2)[0]
		if !strings.HasPrefix(first, "[AUDIT] ") || !strings.Contains(first, "[fms-exchange-server/26.10.21.1442]") || !strings.Contains(first, "OP=server.file-open") {
			t.Fatalf("%s starts with %q, want the version event", name, first)
		}
	}
}
