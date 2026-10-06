package tlsid

import (
	"bytes"
	"crypto/x509"
	"errors"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func paths(t *testing.T) (cert, key string) {
	d := filepath.Join(t.TempDir(), "tls")
	return filepath.Join(d, "cert.pem"), filepath.Join(d, "key.pem")
}

func TestCreateThenLoadKeepsTheIdentity(t *testing.T) {
	cert, key := paths(t)
	protected := 0
	id, err := LoadOrCreate(cert, key, func(string) error { protected++; return nil })
	if err != nil {
		t.Fatal(err)
	}
	if !id.Created || protected != 1 {
		t.Fatalf("created=%v protected=%d", id.Created, protected)
	}
	if !strings.HasPrefix(id.Fingerprint, "SHA256:") || strings.HasSuffix(id.Fingerprint, "=") {
		t.Fatalf("fingerprint form: %q", id.Fingerprint)
	}
	// A second start - and a "rebuilt binary" is the same thing to this package - keeps it.
	again, err := LoadOrCreate(cert, key, func(string) error { protected++; return nil })
	if err != nil {
		t.Fatal(err)
	}
	if again.Created || again.Fingerprint != id.Fingerprint || protected != 1 {
		t.Fatalf("identity changed or re-protected: created=%v fp=%s protected=%d", again.Created, again.Fingerprint, protected)
	}
	if fp, err := FingerprintOfFile(cert); err != nil || fp != id.Fingerprint {
		t.Fatalf("FingerprintOfFile = %q, %v", fp, err)
	}
}

func TestCertificateShape(t *testing.T) {
	cert, key := paths(t)
	id, err := LoadOrCreate(cert, key, nil)
	if err != nil {
		t.Fatal(err)
	}
	leaf, err := x509.ParseCertificate(id.Cert.Certificate[0])
	if err != nil {
		t.Fatal(err)
	}
	if leaf.Subject.CommonName != "fms-exchange" || len(leaf.DNSNames) != 0 || len(leaf.IPAddresses) != 0 {
		t.Fatalf("subject/SAN: %v %v %v", leaf.Subject, leaf.DNSNames, leaf.IPAddresses)
	}
	if years := leaf.NotAfter.Sub(leaf.NotBefore) / (365 * 24 * time.Hour); years < 99 {
		t.Fatalf("validity is %d years, want ~100", years)
	}
	if leaf.PublicKeyAlgorithm != x509.ECDSA || leaf.IsCA {
		t.Fatalf("key algorithm %v, CA %v", leaf.PublicKeyAlgorithm, leaf.IsCA)
	}
}

func TestDamagedKeyIsAnErrorNeverARegeneration(t *testing.T) {
	cert, key := paths(t)
	if _, err := LoadOrCreate(cert, key, nil); err != nil {
		t.Fatal(err)
	}
	full, _ := os.ReadFile(key)
	trunc := full[:len(full)/2]
	if err := os.WriteFile(key, trunc, 0o600); err != nil {
		t.Fatal(err)
	}
	certBefore, _ := os.ReadFile(cert)
	if _, err := LoadOrCreate(cert, key, nil); err == nil {
		t.Fatal("a truncated key must stop the server")
	}
	after, _ := os.ReadFile(key)
	certAfter, _ := os.ReadFile(cert)
	if !bytes.Equal(after, trunc) || !bytes.Equal(certBefore, certAfter) {
		t.Fatal("a damaged identity must be left exactly as it was")
	}
}

func TestPartialIdentityIsRefused(t *testing.T) {
	cert, key := paths(t)
	if _, err := LoadOrCreate(cert, key, nil); err != nil {
		t.Fatal(err)
	}
	if err := os.Remove(key); err != nil {
		t.Fatal(err)
	}
	if _, err := LoadOrCreate(cert, key, nil); !errors.Is(err, ErrPartial) {
		t.Fatalf("got %v, want ErrPartial", err)
	}
	if _, err := os.Stat(key); err == nil {
		t.Fatal("a missing key must not be re-created beside an existing certificate")
	}
}

func TestLoadNeverCreates(t *testing.T) {
	cert, key := paths(t)
	if _, err := Load(cert, key); err == nil {
		t.Fatal("Load of nothing must fail")
	}
	if _, err := os.Stat(filepath.Dir(cert)); err == nil {
		t.Fatal("Load created a directory")
	}
}
