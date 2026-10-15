package tlsid

import (
	"crypto"
	"crypto/rand"
	"crypto/x509"
	"encoding/pem"
	"errors"
	"math/big"
	"time"
)

// Renew replaces only the self-signed leaf. The private key is never written or renamed.
// writeAtomic commits the leaf only after it has been generated and checked; failure preserves the pair.
func Renew(certPath, keyPath string, now time.Time) (*Identity, error) {
	return renew(certPath, keyPath, now, writeAtomic)
}

func renew(certPath, keyPath string, now time.Time, write func(string, []byte) error) (*Identity, error) {
	id, err := Load(certPath, keyPath)
	if err != nil {
		return nil, err
	}
	old, err := x509.ParseCertificate(id.Cert.Certificate[0])
	if err != nil {
		return nil, err
	}
	if old.CheckSignature(old.SignatureAlgorithm, old.RawTBSCertificate, old.Signature) != nil {
		return nil, errors.New("renew applies only to the self-signed identity")
	}
	key, ok := id.Cert.PrivateKey.(crypto.Signer)
	if !ok {
		return nil, ErrUnreadable
	}
	serial, err := rand.Int(rand.Reader, new(big.Int).Lsh(big.NewInt(1), 127))
	if err != nil {
		return nil, err
	}
	leaf := *old
	leaf.SerialNumber = serial
	leaf.NotBefore, leaf.NotAfter = now.Add(-time.Hour), now.AddDate(100, 0, 0)
	der, err := x509.CreateCertificate(rand.Reader, &leaf, &leaf, key.Public(), key)
	if err != nil {
		return nil, err
	}
	if _, err := x509.ParseCertificate(der); err != nil {
		return nil, err
	}
	if err := write(certPath, pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der})); err != nil {
		return nil, err
	}
	return Load(certPath, keyPath)
}
