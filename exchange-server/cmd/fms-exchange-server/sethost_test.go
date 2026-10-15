package main

import (
	"context"
	"io"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"fms/exchange-server/internal/config"
)

func TestHostSettingsDefaultsValidationAndPreservation(t *testing.T) {
	path := filepath.Join(t.TempDir(), config.FileName)
	cfg := config.Default()
	if cfg.KeepAwake || cfg.Log.RetentionDays != 31 {
		t.Fatal("unsafe host defaults")
	}
	cfg.PublicEndpoint = "server.example.org:22022"
	if err := config.Save(path, cfg); err != nil {
		t.Fatal(err)
	}
	for _, args := range [][]string{{"set-keep-awake", "on"}, {"set-log-retention", "7"}} {
		args = append(args, "--config", path)
		if code := run(context.Background(), args, strings.NewReader(""), io.Discard, io.Discard); code != 0 {
			t.Fatal(args, code)
		}
	}
	got, err := config.Load(path)
	if err != nil || !got.KeepAwake || got.Log.RetentionDays != 7 || got.PublicEndpoint != cfg.PublicEndpoint || got.Log.MaxBytes != cfg.Log.MaxBytes {
		t.Fatal(got, err)
	}
	before, _ := os.ReadFile(path)
	for _, args := range [][]string{{"set-log-retention", "0"}, {"set-log-retention", "1.5"}, {"set-log-retention", "36501"}, {"set-keep-awake", "yes"}} {
		args = append(args, "--config", path)
		if code := run(context.Background(), args, strings.NewReader(""), io.Discard, io.Discard); code != exitUsage {
			t.Fatal(args, code)
		}
		after, _ := os.ReadFile(path)
		if string(before) != string(after) {
			t.Fatal("refusal changed config")
		}
	}
}

func TestOldConfigurationGetsRetentionDefault(t *testing.T) {
	path := filepath.Join(t.TempDir(), config.FileName)
	if err := os.WriteFile(path, []byte(`{"listen":":22022","log":{"maxBytes":8192}}`), 0600); err != nil {
		t.Fatal(err)
	}
	cfg, err := config.Load(path)
	if err != nil || cfg.Log.RetentionDays != 31 || cfg.KeepAwake || cfg.Log.MaxBytes != 8192 {
		t.Fatal(cfg, err)
	}
}
