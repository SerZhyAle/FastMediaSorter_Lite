// Package tlsid is the exchange server's TLS identity: a self-signed ECDSA P-256 certificate that a
// producer pins on first registration (contract section 6.1, trust on first use).
//
// The certificate IS the identity, so it is never regenerated: a key file that exists but does not
// parse is an error, not a reason to make a new one - a new certificate makes every pinned producer
// refuse, exactly as a changed SSH host key does.
package tlsid

import (
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/sha256"
	"crypto/tls"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/base64"
	"encoding/pem"
	"errors"
	"fmt"
	"math/big"
	"os"
	"path/filepath"
	"time"
)

// ErrPartial means exactly one of the certificate and the key exists.
var ErrPartial = errors.New("the TLS identity is incomplete: only one of the certificate and the key exists")

// Identity is a loaded certificate and its fingerprint.
type Identity struct {
	Cert        tls.Certificate
	Fingerprint string
	Created     bool
}

// Fingerprint is SHA-256 of the leaf certificate's DER, written "SHA256:" + unpadded base64 (Q134-10) -
// the text form the product's SSH host-key fingerprints already use.
func Fingerprint(der []byte) string {
	sum := sha256.Sum256(der)
	return "SHA256:" + base64.RawStdEncoding.EncodeToString(sum[:])
}

// Load reads an existing pair and never writes anything.
func Load(certPath, keyPath string) (*Identity, error) {
	c, err := tls.LoadX509KeyPair(certPath, keyPath)
	if err != nil {
		return nil, fmt.Errorf("the TLS identity does not load (it is left untouched): %w", err)
	}
	if len(c.Certificate) == 0 {
		return nil, errors.New("the TLS certificate file holds no certificate")
	}
	return &Identity{Cert: c, Fingerprint: Fingerprint(c.Certificate[0])}, nil
}

// Present reports which of the two files exist.
func Present(certPath, keyPath string) (cert, key bool) {
	_, e1 := os.Stat(certPath)
	_, e2 := os.Stat(keyPath)
	return e1 == nil, e2 == nil
}

// FingerprintOfFile reads only the certificate file.
func FingerprintOfFile(certPath string) (string, error) {
	data, err := os.ReadFile(certPath)
	if err != nil {
		return "", err
	}
	block, _ := pem.Decode(data)
	if block == nil || block.Type != "CERTIFICATE" {
		return "", errors.New("the TLS certificate file holds no PEM certificate")
	}
	return Fingerprint(block.Bytes), nil
}

// LoadOrCreate loads the pair or, when NEITHER file exists, generates and stores a new identity.
// protect hardens the directory that will hold the key before the key is written (it may be nil).
func LoadOrCreate(certPath, keyPath string, protect func(dir string) error) (*Identity, error) {
	hasCert, hasKey := Present(certPath, keyPath)
	switch {
	case hasCert && hasKey:
		return Load(certPath, keyPath)
	case hasCert != hasKey:
		return nil, ErrPartial
	}
	dir := filepath.Dir(keyPath)
	if err := os.MkdirAll(dir, 0o700); err != nil {
		return nil, err
	}
	if protect != nil {
		if err := protect(dir); err != nil {
			return nil, fmt.Errorf("could not protect the TLS directory: %w", err)
		}
	}
	key, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		return nil, err
	}
	serial, err := rand.Int(rand.Reader, new(big.Int).Lsh(big.NewInt(1), 127))
	if err != nil {
		return nil, err
	}
	now := time.Now()
	tmpl := &x509.Certificate{
		SerialNumber:          serial,
		Subject:               pkix.Name{CommonName: "fms-exchange"},
		NotBefore:             now.Add(-time.Hour),
		NotAfter:              now.AddDate(100, 0, 0),
		KeyUsage:              x509.KeyUsageDigitalSignature,
		ExtKeyUsage:           []x509.ExtKeyUsage{x509.ExtKeyUsageServerAuth},
		BasicConstraintsValid: true,
	}
	der, err := x509.CreateCertificate(rand.Reader, tmpl, tmpl, &key.PublicKey, key)
	if err != nil {
		return nil, err
	}
	keyDER, err := x509.MarshalPKCS8PrivateKey(key)
	if err != nil {
		return nil, err
	}
	if err := writeAtomic(keyPath, pem.EncodeToMemory(&pem.Block{Type: "PRIVATE KEY", Bytes: keyDER})); err != nil {
		return nil, err
	}
	if err := writeAtomic(certPath, pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der})); err != nil {
		// A key without its certificate is a partial identity the next start refuses; remove the
		// key we just made so a retry can succeed.
		_ = os.Remove(keyPath)
		return nil, err
	}
	id, err := Load(certPath, keyPath)
	if err != nil {
		return nil, err
	}
	id.Created = true
	return id, nil
}

func writeAtomic(path string, data []byte) error {
	tmp := path + ".tmp"
	if err := os.WriteFile(tmp, data, 0o600); err != nil {
		return err
	}
	if err := os.Rename(tmp, path); err != nil {
		_ = os.Remove(tmp)
		return err
	}
	return nil
}
