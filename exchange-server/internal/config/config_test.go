package config

import (
	"encoding/json"
	"errors"
	"io/fs"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestDefaultsAreValid(t *testing.T) {
	c := Default()
	if err := c.Validate(); err != nil {
		t.Fatalf("defaults do not validate: %v", err)
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
	for _, want := range []string{`"_comment"`, `"listen": ":22022"`, `"portRange": "40000-40100"`, `"publicEndpoint": ""`, `"tunnelsPerResource": 16`, `"failedAuthRate"`, `"manageSids": []`} {
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
	good := Default()
	if err := good.Validate(); err != nil {
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
		"max accounts zero":       func(c *Config) { c.Limits.MaxAccounts = 0 },
		"cap zero":                func(c *Config) { c.Limits.DevicesPerAccount = 0 },
		"tunnel cap zero":         func(c *Config) { c.Limits.TunnelsPerResource = 0 },
		"loopback endpoint":       func(c *Config) { c.PublicEndpoint = "127.0.0.1:22022" },
		"endpoint without a port": func(c *Config) { c.PublicEndpoint = "host" },
		"admin sid that is not":   func(c *Config) { c.Admin.ManageSIDs = []string{"alice"} },
		"pairing rate zero":       func(c *Config) { c.Limits.PairingRequest.Burst = 0 },
		"rate zero":               func(c *Config) { c.Limits.UnknownID.PerMinute = 0 },
		"tls half set":            func(c *Config) { c.TLS.CertFile = "x.pem" },
		"log bound tiny":          func(c *Config) { c.Log.MaxBytes = 10 },
		"cast rate zero":          func(c *Config) { c.Limits.Cast.PerMinute = 0 },
		"relay upload zero":       func(c *Config) { c.Limits.RelayUploadMbit = 0 },
		"relay listeners zero":    func(c *Config) { c.Limits.RelayListenersPerBcast = 0 },
		"relay total zero":        func(c *Config) { c.Limits.RelayListenersTotal = 0 },
		"cast pending zero":       func(c *Config) { c.Limits.CastPendingPerAccount = 0 },
	}
	for name, f := range mutate {
		c := good
		f(&c)
		if c.Validate() == nil {
			t.Errorf("%s: accepted", name)
		}
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
	c.PublicEndpoint = "kept.example:22022"
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
	if err != nil || got.Listen != ":23023" || got.PublicEndpoint != "kept.example:22022" {
		t.Fatalf("load: %+v %v", got, err)
	}
	// With no existing file there is nothing to rewrite: the refusal stands.
	if err := Save(filepath.Join(t.TempDir(), "absent.json"), c); !errors.Is(err, fs.ErrPermission) {
		t.Fatalf("expected the permission error, got %v", err)
	}
}

func TestPublicEndpointIsValidatedLikeAnAddressAndNeverLoopback(t *testing.T) {
	for _, ok := range []string{"", "203.0.113.43:22022", "exchange.example.org:443", "[2001:db8::1]:22022", "host:1"} {
		if err := CheckPublicEndpoint(ok); err != nil {
			t.Errorf("%q refused: %v", ok, err)
		}
	}
	for _, bad := range []string{"host", "host:", ":22022", "host:0", "host:70000", "host:abc", "127.0.0.1:22022", "[::1]:22022",
		"localhost:22022", "LOCALHOST:22022", "x.localhost:22022", "0.0.0.0:22022", "[::]:22022", "a b:22022", "http://h:22022", "u@h:22022"} {
		if err := CheckPublicEndpoint(bad); err == nil {
			t.Errorf("%q accepted", bad)
		}
	}
}

func TestAnObsoletePasswordHashIsIgnoredFlaggedOnceAndGoneAfterTheNextSave(t *testing.T) {
	p := filepath.Join(t.TempDir(), FileName)
	if err := os.WriteFile(p, []byte(`{"listen":":9999","passwordHash":"$argon2id$v=19$m=8,t=1,p=1$c2FsdHNhbHQ$a2V5a2V5a2V5a2V5a2V5a2V5"}`), 0o600); err != nil {
		t.Fatal(err)
	}
	c, err := Load(p)
	if err != nil || !c.ObsoletePasswordHash || c.Listen != ":9999" {
		t.Fatalf("%+v %v", c, err)
	}
	if err := c.Validate(); err != nil {
		t.Fatalf("an obsolete member must not make the file invalid: %v", err)
	}
	if err := Save(p, c); err != nil {
		t.Fatal(err)
	}
	raw, _ := os.ReadFile(p)
	if strings.Contains(string(raw), "passwordHash") || strings.Contains(string(raw), "argon2") {
		t.Fatalf("the obsolete member must be gone after a save:\n%s", raw)
	}
	again, _ := Load(p)
	if again.ObsoletePasswordHash {
		t.Fatal("still flagged")
	}
	if plain, _ := Load(filepath.Join(t.TempDir(), "none.json")); plain.ObsoletePasswordHash {
		t.Fatal("a fresh default is not flagged")
	}
}

func TestTheNewLimitNamesReplaceTheOldOnes(t *testing.T) {
	raw, _ := json.Marshal(Default())
	s := string(raw)
	for _, gone := range []string{"maxRegistrations", "tunnelsPerShare", "failedRegisterRate", "passwordHash"} {
		if strings.Contains(s, gone) {
			t.Errorf("the superseded member %q is still written", gone)
		}
	}
	for _, want := range []string{"maxAccounts", "tunnelsPerResource", "failedAuthRate", "failedEnrollRate", "failedHelloRate", "pairingRequestRate",
		"broadcastStartRate", "devicesPerAccount", "resourcesPerAccount", "publicEndpoint", "manageSids"} {
		if !strings.Contains(s, want) {
			t.Errorf("the member %q is missing from the defaults", want)
		}
	}
}

// TestEveryLogLivesUnderTheLogsDirectory is ticket 182 L-1 / ticket 167 T-5: the server's log, its
// one rotation and the service log are all inside <data dir>\logs, whichever way the directory was
// chosen; nothing else about the data directory changes (the enrolled user reads logs\ and nothing
// above it).
func TestEveryLogLivesUnderTheLogsDirectory(t *testing.T) {
	dir := t.TempDir()
	byEnv := Resolve("", func(k string) string {
		if k == EnvDataDir {
			return dir
		}
		return ""
	})
	byFlag := Resolve(filepath.Join(dir, FileName), func(string) string { return "" })
	for name, p := range map[string]Paths{"env": byEnv, "flag": byFlag} {
		logs := filepath.Join(dir, LogsDir)
		if p.LogsDir != logs || p.Log != filepath.Join(logs, LogName) || p.ServiceLog != filepath.Join(logs, ServiceLogName) {
			t.Errorf("%s: %+v", name, p)
		}
		if filepath.Dir(p.Log+".1") != logs {
			t.Errorf("%s: the rotation must stay inside logs\\", name)
		}
		if p.Config != filepath.Join(dir, FileName) || filepath.Dir(p.Cert) != filepath.Join(dir, TLSDir) {
			t.Errorf("%s: the config and the identity keep their places: %+v", name, p)
		}
	}
	if LogName != "exchange.log" || ServiceLogName != "service.log" || LogsDir != "logs" {
		t.Fatal("the names the Share Manager and the helper read are frozen")
	}
}
