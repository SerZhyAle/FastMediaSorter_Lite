package config

import (
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"testing"
)

// linkDir makes link a directory link to target: a symlink, or on Windows a junction (which needs no
// privilege). It skips the test, naming why, when the OS refuses both.
func linkDir(t *testing.T, link, target string) {
	t.Helper()
	if err := os.Symlink(target, link); err == nil {
		return
	} else if runtime.GOOS != "windows" {
		t.Skipf("cannot create a symlink here: %v", err)
	}
	if out, err := exec.Command("cmd", "/c", "mklink", "/J", link, target).CombinedOutput(); err != nil {
		t.Skipf("cannot create a symlink or a junction here: %v: %s", err, out)
	}
}

// Audit gap G-6: a webDir that is, contains or lies inside the data directory or the TLS directory, or
// contains the server's private key file, would serve exchange.json, accounts.json and tls/key.pem
// ("never ... a private key", WEB-BINDING page-served-by-the-server and normative resolution 5).
func TestWebDirIsKeptOutOfTheDataAndTLSDirectories(t *testing.T) {
	root := t.TempDir()
	data := filepath.Join(root, "data")
	tlsDir := filepath.Join(data, TLSDir)
	for _, d := range []string{data, tlsDir, filepath.Join(data, "web"), filepath.Join(root, "site")} {
		if err := os.MkdirAll(d, 0o700); err != nil {
			t.Fatal(err)
		}
	}
	cfgPath := filepath.Join(data, FileName)

	cases := []struct {
		name   string
		webDir string
		ok     bool
	}{
		{"nothing set", "", true},
		{"a normal separate directory", filepath.Join(root, "site"), true},
		{"a directory that does not exist yet, elsewhere", filepath.Join(root, "later", "site"), true},
		{"a sibling whose name starts like the data directory", filepath.Join(root, "data-web"), true},
		{"the data directory itself", data, false},
		{"the data directory with a trailing separator", data + string(os.PathSeparator), false},
		{"the data directory spelled through ..", filepath.Join(data, "web", ".."), false},
		{"a parent of the data directory", root, false},
		{"the parent of the parent", filepath.Dir(root), false},
		{"the TLS directory", tlsDir, false},
		{"a directory inside the data directory", filepath.Join(data, "web"), false},
		{"a directory inside the data directory that does not exist", filepath.Join(data, "nope"), false},
		{"the log directory", filepath.Join(data, LogsDir), false},
	}
	for _, c := range cases {
		cfg := Default()
		cfg.WebDir = c.webDir
		viaIn := cfg.ValidateIn(Paths{Dir: data})
		if (viaIn == nil) != c.ok {
			t.Errorf("%s: ValidateIn = %v, want ok=%v", c.name, viaIn, c.ok)
		}
		// the same through Load: the config remembers the directory of its file
		if err := Save(cfgPath, cfg); err != nil {
			t.Fatal(err)
		}
		loaded, err := Load(cfgPath)
		if err != nil {
			t.Fatal(err)
		}
		if err := loaded.Validate(); (err == nil) != c.ok {
			t.Errorf("%s: Validate after Load = %v, want ok=%v", c.name, err, c.ok)
		}
	}
	// a case that is only different in spelling is the same place on Windows
	if runtime.GOOS == "windows" {
		cfg := Default()
		cfg.WebDir = filepath.Join(filepath.Dir(data), "DATA")
		if cfg.ValidateIn(Paths{Dir: data}) == nil {
			t.Error("the data directory in another case was accepted")
		}
	}
}

func TestWebDirThroughALinkIsTheDirectoryItLeadsTo(t *testing.T) {
	root := t.TempDir()
	data := filepath.Join(root, "data")
	if err := os.MkdirAll(filepath.Join(data, TLSDir), 0o700); err != nil {
		t.Fatal(err)
	}
	if err := os.MkdirAll(filepath.Join(root, "site"), 0o700); err != nil {
		t.Fatal(err)
	}
	toData := filepath.Join(root, "link-to-data")
	linkDir(t, toData, data)
	toSite := filepath.Join(root, "link-to-site")
	linkDir(t, toSite, filepath.Join(root, "site"))

	cfg := Default()
	cfg.WebDir = toData
	if err := cfg.ValidateIn(Paths{Dir: data}); err == nil {
		t.Error("a link to the data directory was accepted as webDir")
	}
	cfg.WebDir = filepath.Join(toData, TLSDir)
	if err := cfg.ValidateIn(Paths{Dir: data}); err == nil {
		t.Error("a path through a link into the TLS directory was accepted as webDir")
	}
	// the data directory reached through a link is refused too, when webDir is real and the data directory is the link
	cfg.WebDir = data
	if err := cfg.ValidateIn(Paths{Dir: toData}); err == nil {
		t.Error("the real data directory was accepted while the config lives behind a link")
	}
	cfg.WebDir = toSite
	if err := cfg.ValidateIn(Paths{Dir: data}); err != nil {
		t.Errorf("a link to a harmless directory was refused: %v", err)
	}
}

// A CA-signed pair is anywhere: webDir must not contain the private key file it names.
func TestWebDirDoesNotContainTheConfiguredPrivateKey(t *testing.T) {
	root := t.TempDir()
	data := filepath.Join(root, "data")
	keys := filepath.Join(root, "certs")
	web := filepath.Join(root, "certs", "public")
	for _, d := range []string{data, keys, web} {
		if err := os.MkdirAll(d, 0o700); err != nil {
			t.Fatal(err)
		}
	}
	keyFile := filepath.Join(keys, "server.key")
	cfg := Default()
	cfg.TLS = TLS{CertFile: filepath.Join(keys, "server.crt"), KeyFile: keyFile}

	cfg.WebDir = keys
	if err := cfg.ValidateIn(Paths{Dir: data}); err == nil {
		t.Error("webDir that holds the key file was accepted")
	}
	cfg.WebDir = root
	if err := cfg.ValidateIn(Paths{Dir: data}); err == nil {
		t.Error("webDir above the key file was accepted")
	}
	cfg.WebDir = web // below the key's directory: it cannot reach the key
	if err := cfg.ValidateIn(Paths{Dir: data}); err != nil {
		t.Errorf("a webDir below the key's directory was refused: %v", err)
	}
	// without paths only the key file can be checked
	cfg.WebDir = keys
	if err := cfg.Validate(); err == nil {
		t.Error("Validate without a data directory ignored the key file")
	}
}

func TestWebSocketMessageCapSettingAndItsBounds(t *testing.T) {
	c := Default()
	if got := c.Limits.WebSocketMessage(); got != 1048580 || c.Limits.WebSocketMessageBytes != 1048580 {
		t.Fatalf("default cap = %d (setting %d), want 1048580", got, c.Limits.WebSocketMessageBytes)
	}
	c.Limits.WebSocketMessageBytes = 0
	if got := c.Limits.WebSocketMessage(); got != DefaultWebSocketMessage || c.Validate() != nil {
		t.Errorf("0 must mean the default: %d, %v", got, c.Validate())
	}
	for _, n := range []int{MinWebSocketMessage, MinWebSocketMessage + 1, 1 << 20, MaxWebSocketMessage} {
		c.Limits.WebSocketMessageBytes = n
		if err := c.Validate(); err != nil || c.Limits.WebSocketMessage() != n {
			t.Errorf("cap %d refused: %v", n, err)
		}
	}
	for _, n := range []int{-1, 1, MinWebSocketMessage - 1, MaxWebSocketMessage + 1, 1<<31 - 1} {
		c.Limits.WebSocketMessageBytes = n
		if c.Validate() == nil {
			t.Errorf("cap %d accepted", n)
		}
	}
}
