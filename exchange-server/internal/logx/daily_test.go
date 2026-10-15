package logx

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func TestDayFilesRetentionAndCap(t *testing.T) {
	dir := t.TempDir()
	now := time.Date(2026, 10, 14, 23, 59, 0, 0, time.UTC)
	for _, name := range []string{"exchange-2026-10-11.log", "exchange-2026-10-11.log.1", "exchange-2026-10-12.log", "notes.txt", "exchange-invalid.log", "service.log"} {
		if err := os.WriteFile(filepath.Join(dir, name), []byte("preserve"), 0600); err != nil {
			t.Fatal(err)
		}
	}
	l, err := OpenDaily(dir, 256, 3, func() time.Time { return now })
	if err != nil {
		t.Fatal(err)
	}
	defer l.Close()
	for _, name := range []string{"exchange-2026-10-11.log", "exchange-2026-10-11.log.1"} {
		if _, err := os.Stat(filepath.Join(dir, name)); !os.IsNotExist(err) {
			t.Fatal("old file survived", name)
		}
	}
	l.Info("before-midnight")
	now = now.Add(2 * time.Minute)
	for i := 0; i < 20; i++ {
		l.Info("after-midnight", Text("value", strings.Repeat("x", 100)))
	}
	l.Info("oversized", Text("value", strings.Repeat("x", 1024)))
	for _, day := range []string{"2026-10-14", "2026-10-15"} {
		data, err := os.ReadFile(filepath.Join(dir, "exchange-"+day+".log"))
		if err != nil || len(data) > 256 || len(data) == 0 {
			t.Fatalf("day %s size %d err %v", day, len(data), err)
		}
	}
	now = now.Add(24 * time.Hour)
	if err := l.Sweep(); err != nil {
		t.Fatal(err)
	}
	for _, name := range []string{"notes.txt", "exchange-invalid.log", "service.log"} {
		data, err := os.ReadFile(filepath.Join(dir, name))
		if err != nil || string(data) != "preserve" {
			t.Fatal(name, err)
		}
	}
}

func TestThirtyOneDaysRemainAfterThirtyTwoDays(t *testing.T) {
	now := time.Date(2026, 1, 1, 0, 0, 0, 0, time.UTC)
	dir := t.TempDir()
	l, err := OpenDaily(dir, 4096, 31, func() time.Time { return now })
	if err != nil {
		t.Fatal(err)
	}
	defer l.Close()
	for i := 0; i < 32; i++ {
		l.Info("day")
		if err := l.Sweep(); err != nil {
			t.Fatal(err)
		}
		if i < 31 {
			now = now.AddDate(0, 0, 1)
		}
	}
	files, _ := os.ReadDir(dir)
	if len(files) != 31 {
		t.Fatalf("%d day files remain", len(files))
	}
}

func TestSweepRunsOncePerDay(t *testing.T) {
	now := time.Date(2026, 10, 14, 0, 0, 0, 0, time.UTC)
	dir := t.TempDir()
	l, err := OpenDaily(dir, 4096, 31, func() time.Time { return now })
	if err != nil {
		t.Fatal(err)
	}
	defer l.Close()
	old := filepath.Join(dir, "exchange-2026-01-01.log")
	_ = os.WriteFile(old, []byte("old"), 0600)
	now = now.Add(23 * time.Hour)
	if err := l.Sweep(); err != nil {
		t.Fatal(err)
	}
	if _, err := os.Stat(old); err != nil {
		t.Fatal("swept too soon", err)
	}
	now = now.Add(time.Hour)
	if err := l.Sweep(); err != nil {
		t.Fatal(err)
	}
	if _, err := os.Stat(old); !os.IsNotExist(err) {
		t.Fatal("daily sweep did not run")
	}
}
