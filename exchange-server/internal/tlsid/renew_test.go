package tlsid

import (
	"bytes"
	"crypto/x509"
	"errors"
	"os"
	"path/filepath"
	"testing"
	"time"
)

func TestRenewKeepsKeyAndFailureKeepsPair(t *testing.T) {
	dir := t.TempDir()
	cert, key := filepath.Join(dir, "cert.pem"), filepath.Join(dir, "key.pem")
	old, err := LoadOrCreate(cert, key, nil)
	if err != nil {
		t.Fatal(err)
	}
	beforeKey, _ := os.ReadFile(key)
	beforeCert, _ := os.ReadFile(cert)
	_, err = renew(cert, key, time.Now().AddDate(1, 0, 0), func(string, []byte) error { return errors.New("disk failure") })
	afterCert, _ := os.ReadFile(cert)
	if err == nil || !bytes.Equal(beforeCert, afterCert) {
		t.Fatal("failed write changed certificate")
	}
	renewed, err := Renew(cert, key, time.Now().AddDate(1, 0, 0))
	if err != nil {
		t.Fatal(err)
	}
	afterKey, _ := os.ReadFile(key)
	a, _ := x509.ParseCertificate(old.Cert.Certificate[0])
	b, _ := x509.ParseCertificate(renewed.Cert.Certificate[0])
	if !bytes.Equal(beforeKey, afterKey) || !bytes.Equal(a.RawSubjectPublicKeyInfo, b.RawSubjectPublicKeyInfo) || a.SerialNumber.Cmp(b.SerialNumber) == 0 || a.NotAfter.Equal(b.NotAfter) || !bytes.Equal(a.RawSubject, b.RawSubject) {
		t.Fatal("renewal invariant failed")
	}
}
