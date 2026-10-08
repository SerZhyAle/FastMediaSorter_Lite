package tlsid

import (
	"crypto/tls"
	"crypto/x509"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"
)

// What a pair a person offers can be refused for (ticket 168 section 3.4). Each is a different
// sentence for the operator, so each is its own error; none of them ever carries key material.
var (
	// ErrUnreadable: a file is missing or is not PEM.
	ErrUnreadable = errors.New("the certificate or the key file cannot be read as PEM")
	// ErrMismatch: both parse, but the key is not the certificate's key.
	ErrMismatch = errors.New("the key does not belong to the certificate")
	// ErrExpired: the certificate's validity ended.
	ErrExpired = errors.New("the certificate has expired")
	// ErrNotYetValid: the certificate's validity has not begun.
	ErrNotYetValid = errors.New("the certificate is not valid yet")
)

// Check validates a pair for serving: it loads, the key matches the certificate, and the certificate
// is within its validity at now. It reads and never writes.
func Check(certPath, keyPath string, now time.Time) (*Identity, error) {
	certPEM, err := os.ReadFile(certPath)
	if err != nil {
		return nil, ErrUnreadable
	}
	keyPEM, err := os.ReadFile(keyPath)
	if err != nil {
		return nil, ErrUnreadable
	}
	pair, err := tls.X509KeyPair(certPEM, keyPEM)
	if err != nil {
		// crypto/tls says which of the two failed without echoing any content: a mismatch is the one that
		// names both halves.
		if strings.Contains(strings.ToLower(err.Error()), "does not match") {
			return nil, ErrMismatch
		}
		return nil, ErrUnreadable
	}
	if len(pair.Certificate) == 0 {
		return nil, ErrUnreadable
	}
	leaf, err := x509.ParseCertificate(pair.Certificate[0])
	if err != nil {
		return nil, ErrUnreadable
	}
	if now.After(leaf.NotAfter) {
		return nil, ErrExpired
	}
	if now.Before(leaf.NotBefore) {
		return nil, ErrNotYetValid
	}
	return &Identity{Cert: pair, Fingerprint: Fingerprint(pair.Certificate[0])}, nil
}

// Import validates a pair and copies it to dstCert and dstKey (an atomic write each, the key first,
// the directory protected before the key is written), then reads the copy back. A pair that fails the
// check writes nothing. The source files are never touched.
func Import(srcCert, srcKey, dstCert, dstKey string, protect func(dir string) error, now time.Time) (*Identity, error) {
	if _, err := Check(srcCert, srcKey, now); err != nil {
		return nil, err
	}
	certPEM, err := os.ReadFile(srcCert)
	if err != nil {
		return nil, ErrUnreadable
	}
	keyPEM, err := os.ReadFile(srcKey)
	if err != nil {
		return nil, ErrUnreadable
	}
	dir := filepath.Dir(dstKey)
	if err := os.MkdirAll(dir, 0o700); err != nil {
		return nil, err
	}
	if protect != nil {
		if err := protect(dir); err != nil {
			return nil, fmt.Errorf("could not protect the TLS directory: %w", err)
		}
	}
	if err := writeAtomic(dstKey, keyPEM); err != nil {
		return nil, err
	}
	if err := writeAtomic(dstCert, certPEM); err != nil {
		_ = os.Remove(dstKey)
		return nil, err
	}
	id, err := Load(dstCert, dstKey)
	if err != nil {
		return nil, err
	}
	id.Created = true
	return id, nil
}

// Replace puts a NEW self-signed identity at certPath and keyPath. An existing pair is set aside
// first and removed only once the new one is written and loads; when making the new one fails the old
// one is put back, so a failed replacement never leaves a server with no identity. This is the one
// place a certificate is deliberately regenerated: every device that pinned the old fingerprint refuses
// the server until its user pins the new one.
func Replace(certPath, keyPath string, protect func(dir string) error) (*Identity, error) {
	oldCert, oldKey := certPath+".replaced", keyPath+".replaced"
	_ = os.Remove(oldCert)
	_ = os.Remove(oldKey)
	hadCert, hadKey := Present(certPath, keyPath)
	if hadCert {
		if err := os.Rename(certPath, oldCert); err != nil {
			return nil, err
		}
	}
	if hadKey {
		if err := os.Rename(keyPath, oldKey); err != nil {
			if hadCert {
				_ = os.Rename(oldCert, certPath)
			}
			return nil, err
		}
	}
	id, err := LoadOrCreate(certPath, keyPath, protect)
	if err != nil {
		_ = os.Remove(certPath)
		_ = os.Remove(keyPath)
		if hadCert {
			_ = os.Rename(oldCert, certPath)
		}
		if hadKey {
			_ = os.Rename(oldKey, keyPath)
		}
		return nil, err
	}
	_ = os.Remove(oldCert)
	_ = os.Remove(oldKey)
	return id, nil
}
