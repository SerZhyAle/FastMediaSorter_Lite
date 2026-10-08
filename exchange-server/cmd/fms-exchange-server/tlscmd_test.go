package main

import (
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/json"
	"encoding/pem"
	"math/big"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/tlsid"
)

// Tickets 168 section 3.4 and 3.5: the Share Manager changes the certificate and the third-party port
// range THROUGH the binary, never by editing exchange.json itself.

func TestParseArgsOfTheAdministrationVerbs(t *testing.T) {
	ok := []struct {
		in   []string
		want command
	}{
		{[]string{"set-port-range", "40000-40100"}, command{name: "set-port-range", value: "40000-40100"}},
		{[]string{"tls", "replace"}, command{name: "tls", sub: "replace"}},
		{[]string{"tls", "import", "--cert", "a.pem", "--key", "b.pem"}, command{name: "tls", sub: "import", cert: "a.pem", key: "b.pem"}},
		{[]string{"tls", "import", "--key=b.pem", "--cert=a.pem", "--config", "c.json"}, command{name: "tls", sub: "import", cert: "a.pem", key: "b.pem", config: "c.json"}},
	}
	for _, c := range ok {
		got, err := parseArgs(c.in)
		if err != nil || got != c.want {
			t.Errorf("%v: got %+v %v, want %+v", c.in, got, err, c.want)
		}
	}
	for _, in := range [][]string{
		{"set-port-range"}, {"set-port-range", "a", "b"},
		{"tls"}, {"tls", "frobnicate"}, {"tls", "import"}, {"tls", "import", "--cert", "a"}, {"tls", "import", "--key", "b"},
		{"tls", "replace", "--cert", "a"}, {"tls", "replace", "extra"}, {"tls", "import", "--cert"},
		{"serve", "--cert", "a"}, {"account", "list", "--key", "k"},
	} {
		if _, err := parseArgs(in); err == nil {
			t.Errorf("%v must be a usage error", in)
		}
	}
}

func TestSetPortRangeValidatesKeepsTheRestAndNeverContainsTheListenPort(t *testing.T) {
	p := filepath.Join(t.TempDir(), "exchange.json")
	if code, _, e := execute(t, []string{"set-listen", "--config", p, ":22022"}, ""); code != exitOK {
		t.Fatal(code, e)
	}
	if code, _, e := execute(t, []string{"set-endpoint", "--config", p, "exchange.example.org:22022"}, ""); code != exitOK {
		t.Fatal(code, e)
	}
	if code, out, e := execute(t, []string{"set-port-range", "--config", p, "41000-41100"}, ""); code != exitOK || !strings.Contains(out, "41000-41100") {
		t.Fatalf("%d %q %s", code, out, e)
	}
	cfg, err := config.Load(p)
	if err != nil || cfg.PortRange != "41000-41100" || cfg.Listen != ":22022" || cfg.PublicEndpoint != "exchange.example.org:22022" {
		t.Fatalf("%+v %v", cfg, err)
	}
	for _, bad := range []string{"40100-40000", "0-10", "1-70000", "abc", "40000", "22000-23000"} {
		if code, _, _ := execute(t, []string{"set-port-range", "--config", p, bad}, ""); code != exitUsage {
			t.Errorf("%q: exit %d, want usage", bad, code)
		}
	}
	if again, _ := config.Load(p); again.PortRange != "41000-41100" {
		t.Fatalf("a refused range must not change the file: %q", again.PortRange)
	}
}

func writeTestPair(t *testing.T, dir, name string, from, to time.Time) (certPath, keyPath string) {
	t.Helper()
	key, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		t.Fatal(err)
	}
	tmpl := &x509.Certificate{SerialNumber: big.NewInt(time.Now().UnixNano()), Subject: pkix.Name{CommonName: name},
		NotBefore: from, NotAfter: to, KeyUsage: x509.KeyUsageDigitalSignature,
		ExtKeyUsage: []x509.ExtKeyUsage{x509.ExtKeyUsageServerAuth}, BasicConstraintsValid: true}
	der, err := x509.CreateCertificate(rand.Reader, tmpl, tmpl, &key.PublicKey, key)
	if err != nil {
		t.Fatal(err)
	}
	keyDER, _ := x509.MarshalPKCS8PrivateKey(key)
	certPath, keyPath = filepath.Join(dir, name+".crt"), filepath.Join(dir, name+".key")
	_ = os.WriteFile(certPath, pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der}), 0o600)
	_ = os.WriteFile(keyPath, pem.EncodeToMemory(&pem.Block{Type: "PRIVATE KEY", Bytes: keyDER}), 0o600)
	return certPath, keyPath
}

func inspectOf(t *testing.T, p string) map[string]any {
	t.Helper()
	code, out, e := execute(t, []string{"inspect", "--config", p}, "")
	if code != exitOK {
		t.Fatalf("inspect %d %s", code, e)
	}
	var m map[string]any
	if err := json.Unmarshal([]byte(out), &m); err != nil {
		t.Fatal(err)
	}
	return m
}

func TestTLSImportRefusesAMismatchedExpiredOrUnreadablePairWithOneStableLine(t *testing.T) {
	p := cfgPath(t)
	src := t.TempDir()
	now := time.Now()
	cert, key := writeTestPair(t, src, "good", now.Add(-time.Hour), now.Add(time.Hour))
	_, wrongKey := writeTestPair(t, src, "wrong", now.Add(-time.Hour), now.Add(time.Hour))
	oldCert, oldKey := writeTestPair(t, src, "old", now.Add(-48*time.Hour), now.Add(-24*time.Hour))
	cases := []struct {
		cert, key, class string
	}{
		{cert, wrongKey, "mismatch"}, {oldCert, oldKey, "expired"}, {filepath.Join(src, "none.crt"), key, "unreadable"},
	}
	for _, c := range cases {
		code, out, e := execute(t, []string{"tls", "import", "--config", p, "--cert", c.cert, "--key", c.key}, "")
		if code != exitUsage || strings.TrimSpace(e) != "tls-refused: "+c.class || out != "" {
			t.Errorf("%s: exit %d stdout %q stderr %q", c.class, code, out, e)
		}
	}
	if _, err := os.Stat(filepath.Dir(p)); err == nil {
		if entries, _ := os.ReadDir(filepath.Dir(p)); len(entries) > 0 {
			t.Errorf("a refused import must leave the data directory empty: %v", entries)
		}
	}
	if m := inspectOf(t, p); m["certImported"] != false {
		t.Errorf("nothing is imported: %v", m["certImported"])
	}
}

func TestTLSImportServesTheCopyAndReplaceGoesBackToAFreshSelfSignedIdentity(t *testing.T) {
	p := cfgPath(t)
	src := t.TempDir()
	now := time.Now()
	cert, key := writeTestPair(t, src, "good", now.Add(-time.Hour), now.Add(24*time.Hour))
	want, _ := tlsid.FingerprintOfFile(cert)

	// a first inspect --init makes the generated identity the person is about to replace
	if code, _, e := execute(t, []string{"inspect", "--init", "--config", p}, ""); code != exitOK {
		t.Fatal(code, e)
	}
	generated := inspectOf(t, p)["fingerprint"].(string)

	code, out, e := execute(t, []string{"tls", "import", "--config", p, "--cert", cert, "--key", key}, "")
	if code != exitOK || strings.TrimSpace(out) != "fingerprint "+want {
		t.Fatalf("import: %d %q %s", code, out, e)
	}
	m := inspectOf(t, p)
	if m["certImported"] != true || m["fingerprint"] != want {
		t.Fatalf("inspect after import: %v", m)
	}
	cfg, _ := config.Load(p)
	if cfg.TLS.CertFile == "" || filepath.Dir(cfg.TLS.CertFile) != filepath.Join(filepath.Dir(p), "tls") {
		t.Fatalf("exchange.json names the copy in the data directory's tls folder: %+v", cfg.TLS)
	}
	if strings.Contains(out+e, "PRIVATE KEY") {
		t.Fatal("a key is never printed")
	}

	code, out, e = execute(t, []string{"tls", "replace", "--config", p}, "")
	if code != exitOK || !strings.HasPrefix(out, "fingerprint SHA256:") {
		t.Fatalf("replace: %d %q %s", code, out, e)
	}
	m = inspectOf(t, p)
	fresh := strings.TrimSpace(strings.TrimPrefix(out, "fingerprint "))
	if m["certImported"] != false || m["fingerprint"] != fresh || fresh == generated || fresh == want {
		t.Fatalf("replace: a NEW self-signed identity, the import dropped: %v (generated %s, imported %s)", m, generated, want)
	}
	cfg, _ = config.Load(p)
	if cfg.TLS.CertFile != "" || cfg.TLS.KeyFile != "" {
		t.Fatalf("the import is no longer named: %+v", cfg.TLS)
	}
	for _, gone := range []string{"imported-cert.pem", "imported-key.pem"} {
		if _, err := os.Stat(filepath.Join(filepath.Dir(p), "tls", gone)); err == nil {
			t.Errorf("%s: the private-key copy of an import must not stay behind", gone)
		}
	}
}
