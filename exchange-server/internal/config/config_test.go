package config

import (
	"errors"
	"io/fs"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"fms/exchange-server/internal/password"
)

func TestDefaultsAreValid(t *testing.T) {
	c := Default()
	if err := c.Validate(); err != nil {
		t.Fatalf("defaults do not validate: %v", err)
	}
	if err := c.CheckPassword(); !errors.Is(err, ErrNoPassword) {
		t.Fatalf("a fresh config has no password: %v", err)
	}
	if c.ValidateForServe() == nil {
		t.Fatal("serve must refuse a config with no password")
	}
}

func TestLoadOrInitWritesCommentedDefaults(t *testing.T) {
	p := filepath.Join(t.TempDir(), "sub", FileName)
	c, err := LoadOrInit(p)
	if err != nil {
		t.Fatal(err)
	}
	raw, _ := os.ReadFile(p)
	s := string(raw)
	for _, want := range []string{`"_comment"`, `"listen": ":22022"`, `"portRange": "40000-40100"`, `"passwordHash": ""`} {
		if !strings.Contains(s, want) {
			t.Errorf("first-run file lacks %s", want)
		}
	}
	back, err := Load(p)
	if err != nil || back.Listen != c.Listen {
		t.Fatalf("round trip: %v %+v", err, back)
	}
}

func TestMissingMembersKeepDefaultsAndUnknownAreIgnored(t *testing.T) {
	p := filepath.Join(t.TempDir(), FileName)
	if err := os.WriteFile(p, []byte(`{"listen":":9999","somethingNew":{"a":1}}`), 0o600); err != nil {
		t.Fatal(err)
	}
	c, err := Load(p)
	if err != nil {
		t.Fatal(err)
	}
	if c.Listen != ":9999" || c.PortRange != "40000-40100" || c.Keepalive.DefaultSeconds != 30 {
		t.Fatalf("defaults not kept: %+v", c)
	}
	if err := c.Validate(); err != nil {
		t.Fatal(err)
	}
}

func TestEveryRefusal(t *testing.T) {
	rec, _ := password.HashWith("Passw0rdTest", password.Params{Time: 1, Memory: 8, Threads: 1, KeyLen: 32})
	good := Default()
	good.PasswordHash = rec
	if err := good.ValidateForServe(); err != nil {
		t.Fatalf("a good config is refused: %v", err)
	}
	mutate := map[string]func(*Config){
		"listen not host:port":    func(c *Config) { c.Listen = "nonsense" },
		"listen port too high":    func(c *Config) { c.Listen = ":70000" },
		"range reversed":          func(c *Config) { c.PortRange = "40100-40000" },
		"range one number":        func(c *Config) { c.PortRange = "40000" },
		"range zero":              func(c *Config) { c.PortRange = "0-10" },
		"keepalive min > default": func(c *Config) { c.Keepalive.MinSeconds = 60 },
		"keepalive window zero":   func(c *Config) { c.Keepalive.WindowMultiplier = 0 },
		"cap zero":                func(c *Config) { c.Limits.MaxRegistrations = 0 },
		"rate zero":               func(c *Config) { c.Limits.UnknownID.PerMinute = 0 },
		"tls half set":            func(c *Config) { c.TLS.CertFile = "x.pem" },
		"log bound tiny":          func(c *Config) { c.Log.MaxBytes = 10 },
	}
	for name, f := range mutate {
		c := good
		f(&c)
		if c.Validate() == nil {
			t.Errorf("%s: accepted", name)
		}
	}
	c := good
	c.PasswordHash = "$argon2id$garbage"
	if c.ValidateForServe() == nil || errors.Is(c.CheckPassword(), ErrNoPassword) {
		t.Error("a malformed hash must be refused as malformed, not as missing")
	}
}

func TestLoadRefusesBrokenJSONAndLeavesTheFile(t *testing.T) {
	p := filepath.Join(t.TempDir(), FileName)
	if err := os.WriteFile(p, []byte(`{ not json`), 0o600); err != nil {
		t.Fatal(err)
	}
	if _, err := Load(p); err == nil {
		t.Fatal("broken JSON must be an error")
	}
	if _, err := LoadOrInit(p); err == nil {
		t.Fatal("LoadOrInit must not paper over a broken file")
	}
	raw, _ := os.ReadFile(p)
	if string(raw) != `{ not json` {
		t.Fatal("a broken config was overwritten")
	}
}

func TestSaveIsAtomicAndLeavesNoTemp(t *testing.T) {
	dir := t.TempDir()
	p := filepath.Join(dir, FileName)
	c := Default()
	c.Listen = ":1234"
	if err := Save(p, c); err != nil {
		t.Fatal(err)
	}
	c.Listen = ":5678"
	if err := Save(p, c); err != nil {
		t.Fatal(err)
	}
	entries, _ := os.ReadDir(dir)
	if len(entries) != 1 {
		t.Fatalf("leftovers: %v", entries)
	}
	back, _ := Load(p)
	if back.Listen != ":5678" {
		t.Fatalf("overwrite lost: %s", back.Listen)
	}
}

func TestResolveOrder(t *testing.T) {
	none := func(string) string { return "" }
	explicit := Resolve(filepath.Join("some", "dir", "my.json"), none)
	if filepath.Base(explicit.Config) != "my.json" || filepath.Base(explicit.Dir) != "dir" {
		t.Fatalf("explicit --config: %+v", explicit)
	}
	if filepath.Dir(explicit.Cert) != filepath.Join(explicit.Dir, TLSDir) {
		t.Fatalf("the TLS directory must sit beside the config: %+v", explicit)
	}
	env := Resolve("", func(k string) string {
		if k == EnvDataDir {
			return filepath.Join("override", "here")
		}
		return ""
	})
	if env.Dir != filepath.Join("override", "here") || env.Config != filepath.Join("override", "here", FileName) {
		t.Fatalf("env override: %+v", env)
	}
	// An explicit --config beats the override.
	both := Resolve(filepath.Join("a", "b.json"), func(string) string { return "ignored" })
	if filepath.Base(both.Config) != "b.json" {
		t.Fatalf("--config must win: %+v", both)
	}
	def := Resolve("", none)
	if def.Dir == "" || filepath.Base(def.Config) != FileName {
		t.Fatalf("platform default: %+v", def)
	}
}

func TestParsePortRange(t *testing.T) {
	lo, hi, err := ParsePortRange(" 40000 - 40100 ")
	if err != nil || lo != 40000 || hi != 40100 {
		t.Fatalf("%d %d %v", lo, hi, err)
	}
	for _, bad := range []string{"", "x-y", "1-70000", "5-4", "-5-10"} {
		if _, _, err := ParsePortRange(bad); err == nil {
			t.Errorf("%q accepted", bad)
		}
	}
}

func TestSaveRewritesInPlaceWhenTheTemporaryFileIsRefused(t *testing.T) {
	p := filepath.Join(t.TempDir(), FileName)
	c := Default()
	c.PasswordHash = "kept"
	if err := Save(p, c); err != nil {
		t.Fatal(err)
	}
	old := writeTemp
	writeTemp = func(string, []byte, os.FileMode) error { return fs.ErrPermission }
	defer func() { writeTemp = old }()

	c.Listen = ":23023"
	if err := Save(p, c); err != nil {
		t.Fatalf("an in-place rewrite was expected: %v", err)
	}
	got, err := Load(p)
	if err != nil || got.Listen != ":23023" || got.PasswordHash != "kept" {
		t.Fatalf("load: %+v %v", got, err)
	}
	// With no existing file there is nothing to rewrite: the refusal stands.
	if err := Save(filepath.Join(t.TempDir(), "absent.json"), c); !errors.Is(err, fs.ErrPermission) {
		t.Fatalf("expected the permission error, got %v", err)
	}
}
