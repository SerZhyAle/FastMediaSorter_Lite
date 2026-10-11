package tlsid

import (
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/pem"
	"errors"
	"math/big"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

// writePair writes a self-signed pair valid in [from, to] under dir and returns the two paths.
func writePair(t *testing.T, dir, name string, from, to time.Time) (certPath, keyPath string) {
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
	keyDER, err := x509.MarshalPKCS8PrivateKey(key)
	if err != nil {
		t.Fatal(err)
	}
	certPath, keyPath = filepath.Join(dir, name+".crt"), filepath.Join(dir, name+".key")
	if err := os.WriteFile(certPath, pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der}), 0o600); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(keyPath, pem.EncodeToMemory(&pem.Block{Type: "PRIVATE KEY", Bytes: keyDER}), 0o600); err != nil {
		t.Fatal(err)
	}
	return certPath, keyPath
}

func TestCheckAcceptsAValidPairAndRefusesEachFailureByName(t *testing.T) {
	dir := t.TempDir()
	now := time.Now()
	cert, key := writePair(t, dir, "good", now.Add(-time.Hour), now.Add(24*time.Hour))
	id, err := Check(cert, key, now)
	if err != nil || !strings.HasPrefix(id.Fingerprint, "SHA256:") {
		t.Fatalf("a valid pair: %v %v", id, err)
	}

	otherCert, otherKey := writePair(t, dir, "other", now.Add(-time.Hour), now.Add(24*time.Hour))
	if _, err := Check(cert, otherKey, now); !errors.Is(err, ErrMismatch) {
		t.Errorf("a key of another certificate: %v", err)
	}
	_ = otherCert

	oldCert, oldKey := writePair(t, dir, "old", now.Add(-48*time.Hour), now.Add(-24*time.Hour))
	if _, err := Check(oldCert, oldKey, now); !errors.Is(err, ErrExpired) {
		t.Errorf("an expired pair: %v", err)
	}
	futureCert, futureKey := writePair(t, dir, "future", now.Add(24*time.Hour), now.Add(48*time.Hour))
	if _, err := Check(futureCert, futureKey, now); !errors.Is(err, ErrNotYetValid) {
		t.Errorf("a pair that is not valid yet: %v", err)
	}

	if _, err := Check(filepath.Join(dir, "missing.crt"), key, now); !errors.Is(err, ErrUnreadable) {
		t.Errorf("a missing certificate: %v", err)
	}
	junk := filepath.Join(dir, "junk.pem")
	_ = os.WriteFile(junk, []byte("not pem at all"), 0o600)
	if _, err := Check(junk, key, now); !errors.Is(err, ErrUnreadable) {
		t.Errorf("junk as a certificate: %v", err)
	}
	if _, err := Check(cert, junk, now); !errors.Is(err, ErrUnreadable) {
		t.Errorf("junk as a key: %v", err)
	}
}

func TestNoRefusalCarriesKeyMaterial(t *testing.T) {
	dir := t.TempDir()
	now := time.Now()
	cert, key := writePair(t, dir, "good", now.Add(-time.Hour), now.Add(time.Hour))
	_, otherKey := writePair(t, dir, "other", now.Add(-time.Hour), now.Add(time.Hour))
	keyText, _ := os.ReadFile(otherKey)
	body := strings.TrimSpace(strings.Join(strings.Split(string(keyText), "\n")[1:2], ""))
	for _, err := range []error{
		func() error { _, e := Check(cert, otherKey, now); return e }(),
		func() error { _, e := Check(cert, filepath.Join(dir, "missing.key"), now); return e }(),
		func() error { _, e := Check(key, cert, now); return e }(),
	} {
		if err == nil || (body != "" && strings.Contains(err.Error(), body)) {
			t.Fatalf("a refusal that echoes key material, or no refusal: %v", err)
		}
	}
}

func TestImportCopiesAValidPairAndWritesNothingForAnInvalidOne(t *testing.T) {
	dir := t.TempDir()
	now := time.Now()
	cert, key := writePair(t, dir, "good", now.Add(-time.Hour), now.Add(time.Hour))
	_, wrongKey := writePair(t, dir, "wrong", now.Add(-time.Hour), now.Add(time.Hour))
	tlsDir := filepath.Join(dir, "tls")
	dstCert, dstKey := filepath.Join(tlsDir, "imported-cert.pem"), filepath.Join(tlsDir, "imported-key.pem")

	protected := ""
	protect := func(d string) error { protected = d; return nil }
	if _, err := Import(cert, wrongKey, dstCert, dstKey, protect, now); !errors.Is(err, ErrMismatch) {
		t.Fatalf("a mismatched pair must be refused: %v", err)
	}
	if _, err := os.Stat(tlsDir); err == nil {
		t.Fatal("a refused pair must write nothing, not even the directory")
	}

	id, err := Import(cert, key, dstCert, dstKey, protect, now)
	if err != nil {
		t.Fatal(err)
	}
	if protected != tlsDir {
		t.Fatalf("the directory is protected before the key is written: %q", protected)
	}
	want, _ := FingerprintOfFile(cert)
	if id.Fingerprint != want {
		t.Fatalf("the fingerprint of the copy is the certificate's: %s vs %s", id.Fingerprint, want)
	}
	for _, p := range []string{cert, key} {
		if _, err := os.Stat(p); err != nil {
			t.Fatalf("the source %s must stay: %v", p, err)
		}
	}
	if _, err := Load(dstCert, dstKey); err != nil {
		t.Fatalf("the copy must load: %v", err)
	}
}

func TestReplaceMakesANewIdentityAndKeepsTheOldWhenItFails(t *testing.T) {
	dir := t.TempDir()
	cert, key := filepath.Join(dir, "tls", "cert.pem"), filepath.Join(dir, "tls", "key.pem")
	first, err := LoadOrCreate(cert, key, nil)
	if err != nil {
		t.Fatal(err)
	}
	second, err := Replace(cert, key, nil)
	if err != nil {
		t.Fatal(err)
	}
	if second.Fingerprint == first.Fingerprint {
		t.Fatal("a replacement is a different identity")
	}
	if got, _ := FingerprintOfFile(cert); got != second.Fingerprint {
		t.Fatalf("the file holds the new identity: %s", got)
	}
	for _, left := range []string{cert + ".replaced", key + ".replaced"} {
		if _, err := os.Stat(left); err == nil {
			t.Fatalf("%s must not stay: the old private key is gone for good", left)
		}
	}

	// a failure while making the new one puts the old one back
	boom := errors.New("protect failed")
	if _, err := Replace(cert, key, func(string) error { return boom }); err == nil {
		t.Fatal("the protect failure must surface")
	}
	if got, err := FingerprintOfFile(cert); err != nil || got != second.Fingerprint {
		t.Fatalf("the old identity must be back: %s %v", got, err)
	}
	if _, err := Load(cert, key); err != nil {
		t.Fatalf("and it must load: %v", err)
	}
}

func TestReplaceWithNoIdentityJustMakesOne(t *testing.T) {
	dir := t.TempDir()
	id, err := Replace(filepath.Join(dir, "c.pem"), filepath.Join(dir, "k.pem"), nil)
	if err != nil || !strings.HasPrefix(id.Fingerprint, "SHA256:") {
		t.Fatalf("%v %v", id, err)
	}
}

// Ticket 187 T-2: whether a leaf is publicly trusted decides whether a connection string carries its pin.
func TestPubliclyTrustedIsTrueOnlyForALeafARootValidates(t *testing.T) {
	dir := t.TempDir()
	now := time.Now()

	// a self-signed leaf is trusted by nobody's store
	selfCert, _ := writePair(t, dir, "self", now.Add(-time.Hour), now.Add(24*time.Hour))
	if ok, err := PubliclyTrusted(selfCert, "exchange.example.org", now, nil); err != nil || ok {
		t.Fatalf("a self-signed leaf: %v %v", ok, err)
	}

	// a leaf signed by a CA the pool trusts is, for the name it was issued to and not for another
	caKey, _ := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	caTmpl := &x509.Certificate{SerialNumber: big.NewInt(1), Subject: pkix.Name{CommonName: "test ca"}, NotBefore: now.Add(-time.Hour),
		NotAfter: now.Add(48 * time.Hour), IsCA: true, BasicConstraintsValid: true, KeyUsage: x509.KeyUsageCertSign}
	caDER, err := x509.CreateCertificate(rand.Reader, caTmpl, caTmpl, &caKey.PublicKey, caKey)
	if err != nil {
		t.Fatal(err)
	}
	caCert, _ := x509.ParseCertificate(caDER)
	leafKey, _ := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	leafTmpl := &x509.Certificate{SerialNumber: big.NewInt(2), Subject: pkix.Name{CommonName: "exchange.example.org"},
		DNSNames: []string{"exchange.example.org"}, NotBefore: now.Add(-time.Hour), NotAfter: now.Add(24 * time.Hour),
		KeyUsage: x509.KeyUsageDigitalSignature, ExtKeyUsage: []x509.ExtKeyUsage{x509.ExtKeyUsageServerAuth}}
	leafDER, err := x509.CreateCertificate(rand.Reader, leafTmpl, caCert, &leafKey.PublicKey, caKey)
	if err != nil {
		t.Fatal(err)
	}
	path := filepath.Join(dir, "signed.crt")
	if err := os.WriteFile(path, pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: leafDER}), 0o600); err != nil {
		t.Fatal(err)
	}
	pool := x509.NewCertPool()
	pool.AddCert(caCert)
	if ok, err := PubliclyTrusted(path, "exchange.example.org", now, pool); err != nil || !ok {
		t.Fatalf("a leaf of a trusted CA: %v %v", ok, err)
	}
	if ok, _ := PubliclyTrusted(path, "other.example.org", now, pool); ok {
		t.Fatal("a leaf issued to another name is not trusted for this one")
	}
	if ok, _ := PubliclyTrusted(path, "exchange.example.org", now.Add(72*time.Hour), pool); ok {
		t.Fatal("an expired leaf is not trusted")
	}
	if _, err := PubliclyTrusted(filepath.Join(dir, "absent.crt"), "x", now, nil); !errors.Is(err, ErrUnreadable) {
		t.Fatalf("a missing file: %v", err)
	}
}
