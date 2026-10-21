package main

import (
	"bytes"
	"context"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/config"
)

func TestConfigurationCommandWritesStateWithoutArguments(t *testing.T) {
	dir := t.TempDir()
	t.Setenv(config.EnvDataDir, dir)
	var out, errs bytes.Buffer
	code := run(context.Background(), []string{"set-endpoint", "private-host.example:4443"}, strings.NewReader(""), &out, &errs)
	if code != exitOK {
		t.Fatalf("exit %d: %s", code, errs.String())
	}
	path := filepath.Join(dir, "logs", "exchange-"+time.Now().UTC().Format("2006-01-02")+".log")
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	line := string(data)
	if !strings.Contains(line, "[STATE-CHANGE]") || !strings.Contains(line, "operation=set-endpoint") || strings.Contains(line, "private-host") {
		t.Fatalf("missing change or leaked arguments: %s", line)
	}
}

func TestReadOnlyCommandDoesNotCreateStateLog(t *testing.T) {
	dir := t.TempDir()
	t.Setenv(config.EnvDataDir, dir)
	var out, errs bytes.Buffer
	if code := run(context.Background(), []string{"version"}, strings.NewReader(""), &out, &errs); code != exitOK {
		t.Fatalf("exit %d: %s", code, errs.String())
	}
	if _, err := os.Stat(filepath.Join(dir, "logs")); !os.IsNotExist(err) {
		t.Fatalf("read-only command created a log directory: %v", err)
	}
}
