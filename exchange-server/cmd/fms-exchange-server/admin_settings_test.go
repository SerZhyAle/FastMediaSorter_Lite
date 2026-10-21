package main

import (
	"encoding/json"
	"os"
	"path/filepath"
	"reflect"
	"strings"
	"testing"

	"fms/exchange-server/internal/config"
)

// The host settings of the page and its WebSocket (WEB-BINDING page-served-by-the-server, websocket-binding
// Origin and Messages; audit gaps G-7 and G-10): the web directory, the listed origins and the message cap are
// set from the command line, shown by `admin status`, and refused when the config would refuse them.

// settingsEnv is a config file in <root>\data with a web directory next to it.
func settingsEnv(t *testing.T) (cfgFile, dataDir, webDir string) {
	t.Helper()
	root := t.TempDir()
	cfgFile = filepath.Join(root, "data", config.FileName)
	webDir = filepath.Join(root, "web")
	if err := os.MkdirAll(webDir, 0o700); err != nil {
		t.Fatal(err)
	}
	// a valid first write creates the file, so a later refusal can be compared with it
	if code, _, stderr := execute(t, []string{"admin", "set", "--lifetime", "20", "--config", cfgFile}, ""); code != 0 {
		t.Fatalf("the first admin set: %d %s", code, stderr)
	}
	return cfgFile, filepath.Dir(cfgFile), webDir
}

func TestAdminSetWebDirOriginsAndTheMessageCapAreStoredAndShown(t *testing.T) {
	p, _, web := settingsEnv(t)

	code, out, stderr := execute(t, []string{"admin", "set", "--config", p, "--web-dir", web,
		"--origin", "https://app.example", "--origin", "https://other.example:8443", "--ws-max-message", "524288"}, "")
	if code != 0 || !strings.Contains(out, "admin settings updated") {
		t.Fatalf("admin set: %d %q %q", code, out, stderr)
	}
	cfg, err := config.Load(p)
	if err != nil {
		t.Fatal(err)
	}
	if cfg.WebDir != web || !reflect.DeepEqual(cfg.Admin.Origins, []string{"https://app.example", "https://other.example:8443"}) ||
		cfg.Limits.WebSocketMessageBytes != 524288 || cfg.Admin.SessionLifetimeMinutes != 20 {
		t.Fatalf("stored: webDir %q origins %q cap %d lifetime %d", cfg.WebDir, cfg.Admin.Origins, cfg.Limits.WebSocketMessageBytes, cfg.Admin.SessionLifetimeMinutes)
	}

	code, out, _ = execute(t, []string{"admin", "status", "--config", p}, "")
	for _, want := range []string{"web dir " + web, "origins https://app.example, https://other.example:8443", "websocket message cap 524288 bytes"} {
		if code != 0 || !strings.Contains(out, want) {
			t.Fatalf("status lacks %q: %d %q", want, code, out)
		}
	}
	var st struct {
		WebDir                string   `json:"webDir"`
		Origins               []string `json:"origins"`
		WebSocketMessageBytes int      `json:"webSocketMessageBytes"`
	}
	code, out, _ = execute(t, []string{"admin", "status", "--json", "--config", p}, "")
	if err := json.Unmarshal([]byte(out), &st); code != 0 || err != nil || st.WebDir != web || len(st.Origins) != 2 || st.WebSocketMessageBytes != 524288 {
		t.Fatalf("status --json: %d %v %+v", code, err, st)
	}

	// an origin given twice is one entry; one is removed by name; a name that is not there is an error
	if code, _, _ = execute(t, []string{"admin", "set", "--config", p, "--origin", "HTTPS://app.example", "--remove-origin", "https://other.example:8443"}, ""); code != 0 {
		t.Fatalf("add and remove: %d", code)
	}
	cfg, _ = config.Load(p)
	if !reflect.DeepEqual(cfg.Admin.Origins, []string{"https://app.example"}) {
		t.Fatalf("origins after add and remove: %q", cfg.Admin.Origins)
	}
	code, _, stderr = execute(t, []string{"admin", "set", "--config", p, "--remove-origin", "https://nothere.example"}, "")
	if code != exitUsage || !strings.Contains(stderr, "is not in the list") {
		t.Fatalf("removing an origin that is not listed: %d %q", code, stderr)
	}
	cfg, _ = config.Load(p)
	if !reflect.DeepEqual(cfg.Admin.Origins, []string{"https://app.example"}) {
		t.Fatalf("a refused removal changed the list: %q", cfg.Admin.Origins)
	}

	// --origins replaces the whole list; - clears it
	if code, _, _ = execute(t, []string{"admin", "set", "--config", p, "--origins", "https://a.example, https://b.example:444"}, ""); code != 0 {
		t.Fatalf("--origins: %d", code)
	}
	cfg, _ = config.Load(p)
	if !reflect.DeepEqual(cfg.Admin.Origins, []string{"https://a.example", "https://b.example:444"}) {
		t.Fatalf("origins after --origins: %q", cfg.Admin.Origins)
	}
	if code, _, _ = execute(t, []string{"admin", "set", "--config", p, "--origins", "-"}, ""); code != 0 {
		t.Fatalf("--origins -: %d", code)
	}
	cfg, _ = config.Load(p)
	if len(cfg.Admin.Origins) != 0 {
		t.Fatalf("origins after --origins -: %q", cfg.Admin.Origins)
	}

	// 0 is the default, and the status says the default's number; - clears the web directory
	if code, _, _ = execute(t, []string{"admin", "set", "--config", p, "--ws-max-message", "0", "--web-dir", "-"}, ""); code != 0 {
		t.Fatalf("--ws-max-message 0 --web-dir -: %d", code)
	}
	cfg, _ = config.Load(p)
	if cfg.WebDir != "" || cfg.Limits.WebSocketMessageBytes != 0 {
		t.Fatalf("after clearing: webDir %q cap %d", cfg.WebDir, cfg.Limits.WebSocketMessageBytes)
	}
	code, out, _ = execute(t, []string{"admin", "status", "--config", p}, "")
	if code != 0 || strings.Contains(out, "web dir") || !strings.Contains(out, "websocket message cap 1048580 bytes") {
		t.Fatalf("status after clearing: %d %q", code, out)
	}
}

func TestAdminSetRefusesWhatTheConfigRefusesAndWritesNothing(t *testing.T) {
	p, data, web := settingsEnv(t)
	before, err := os.ReadFile(p)
	if err != nil {
		t.Fatal(err)
	}
	if err := os.MkdirAll(filepath.Join(data, "sub"), 0o700); err != nil {
		t.Fatal(err)
	}

	refused := map[string][]string{
		"the data directory":            {"--web-dir", data},
		"the parent of the data dir":    {"--web-dir", filepath.Dir(data)},
		"the TLS directory":             {"--web-dir", filepath.Join(data, config.TLSDir)},
		"a directory inside the data":   {"--web-dir", filepath.Join(data, "sub")},
		"a path with dot dot into data": {"--web-dir", filepath.Join(web, "..", "data")},
		"http origin":                   {"--origin", "http://app.example"},
		"wildcard origin":               {"--origin", "*"},
		"origin with a path":            {"--origin", "https://app.example/x"},
		"origin with userinfo":          {"--origin", "https://u@app.example"},
		"null origin":                   {"--origin", "null"},
		"origin in a list":              {"--origins", "https://ok.example,http://bad.example"},
		"cap below the least":           {"--ws-max-message", "100"},
		"cap above the most":            {"--ws-max-message", "17000000"},
		"cap beyond int":                {"--ws-max-message", "99999999999"},
	}
	for name, opts := range refused {
		args := append([]string{"admin", "set", "--config", p}, opts...)
		code, out, stderr := execute(t, args, "")
		if code != exitUsage || strings.Contains(out, "updated") || stderr == "" {
			t.Errorf("%s: exit %d stdout %q stderr %q, want a refusal", name, code, out, stderr)
		}
		after, err := os.ReadFile(p)
		if err != nil || string(after) != string(before) {
			t.Errorf("%s: a refused admin set changed the config file", name)
		}
	}

	// the same settings, valid, are accepted: the refusals above are about the values
	if code, _, stderr := execute(t, []string{"admin", "set", "--config", p, "--web-dir", web, "--origin", "https://app.example", "--ws-max-message", "262148"}, ""); code != 0 {
		t.Fatalf("the valid settings: %d %s", code, stderr)
	}
	if code, _, stderr := execute(t, []string{"admin", "set", "--config", p, "--ws-max-message", "16777216"}, ""); code != 0 {
		t.Fatalf("the largest cap: %d %s", code, stderr)
	}
}

func TestAdminSettingsOptionsBelongToAdminSetOnly(t *testing.T) {
	p, _, web := settingsEnv(t)
	for _, args := range [][]string{
		{"admin", "on", "--web-dir", web},
		{"admin", "off", "--origin", "https://app.example"},
		{"admin", "status", "--ws-max-message", "300000"},
		{"admin", "passwd", "--origins", "https://app.example"},
		{"admin", "set", "--origins", "https://a.example", "--origin", "https://b.example"},
		{"admin", "set", "--origins", "https://a.example", "--remove-origin", "https://b.example"},
		{"admin", "set", "--web-dir"},
		{"admin", "set", "--origin"},
		{"admin", "set", "--ws-max-message"},
		{"admin", "set", "--ws-max-message", "-5"},
		{"admin", "set", "--ws-max-message", "many"},
		{"serve", "--web-dir", web},
		{"inspect", "--origin", "https://app.example"},
	} {
		code, out, _ := execute(t, append(args, "--config", p), "")
		if code != exitUsage || out != "" {
			t.Errorf("%v: exit %d stdout %q, want a usage error", args, code, out)
		}
	}
}

func TestServeRefusesAWebDirThatIsOrHoldsTheDataDirectory(t *testing.T) {
	p, data, web := settingsEnv(t)
	for name, dir := range map[string]string{
		"the data directory": data,
		"its parent":         filepath.Dir(data),
		"the TLS directory":  filepath.Join(data, config.TLSDir),
		"inside the data":    filepath.Join(data, "sub", "page"),
	} {
		cfg, err := config.Load(p)
		if err != nil {
			t.Fatal(err)
		}
		cfg.WebDir = dir
		if err := config.Save(p, cfg); err != nil { // Save does not validate: this is a config edited by hand
			t.Fatal(err)
		}
		code, out, stderr := execute(t, []string{"serve", "--config", p}, "")
		if code != exitConfig || !strings.Contains(stderr, "the config is not valid") || !strings.Contains(stderr, "web") || strings.Contains(out, "listening") {
			t.Errorf("serve with webDir = %s: exit %d stdout %q stderr %q, want a refusal to start", name, code, out, stderr)
		}
	}
	// a separate directory is not refused by the check (serve then goes on to need an account: its own refusal)
	cfg, _ := config.Load(p)
	cfg.WebDir = web
	if err := config.Save(p, cfg); err != nil {
		t.Fatal(err)
	}
	code, _, stderr := execute(t, []string{"serve", "--config", p}, "")
	if code != exitPolicy || !strings.Contains(stderr, "no account") {
		t.Fatalf("a separate web directory must pass the check and reach the no-account refusal: exit %d %q", code, stderr)
	}
}
