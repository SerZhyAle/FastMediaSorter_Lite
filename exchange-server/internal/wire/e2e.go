package wire

import (
	"crypto/ecdh"
	"encoding/base64"
	"encoding/json"
	"errors"
)

// Sealed content (DEVICE-EXCHANGE section 27, ticket 234). The server never opens, checks or changes a
// box: a sealed value is an opaque JSON object it stores and delivers where section 27 puts it. This file
// holds the few names the wire needs for it and the checks of the PUBLIC values the server does keep -
// the device key of item DF - and nothing that touches a box's members.
const (
	// FeatureSealed is the `welcome.features` entry of item DP: the server keeps and delivers `e2eKey`,
	// `sealedAccess`, the `sealed` box of a broadcast record and of a grant, `devices` in `grants`, the
	// replacing `grant` and the clipboard kind `sealed`. A server that lists it does all of these.
	FeatureSealed = "sealed"

	// E2ECapability is the device capability of item DF: the device can seal and open.
	E2ECapability = "e2e-v1"

	// E2EKeyVersion is the only `v` of a device key (item DF).
	E2EKeyVersion = 1

	// SealedContentType is the `contentType` of a sealed relay upload (item DL); the true media type
	// travels inside the key ring.
	SealedContentType = "application/x-fmsx-sealed"

	// ClipKindSealed is the clipboard kind of item DN: `size` is the sealed bytes, `sealed` the box.
	ClipKindSealed = "sealed"

	// e2eKEMLen is the length of the uncompressed SEC1 point of P-256 a device key holds.
	e2eKEMLen = 65
)

// ErrE2EKey is a device key that is not `{v:1, kem}` with a valid P-256 point.
var ErrE2EKey = errors.New("wire: e2eKey is not a version 1 P-256 key")

// E2EKey is the public key a device seals to (item DF): `v` is 1 and `kem` is the 65-byte uncompressed
// SEC1 point of P-256, base64url without padding. It is public - it lets others seal to the device and
// opens nothing - but it is still never printed by a formatter, so a log line cannot carry it by mistake.
type E2EKey struct {
	V   int    `json:"v"`
	KEM string `json:"kem"`
}

// String never prints a member.
func (k E2EKey) String() string { return "wire.E2EKey" }

// GoString never prints a member.
func (k E2EKey) GoString() string { return "wire.E2EKey" }

// Validate checks the shape of the key: version 1, a 65-byte base64url value that starts with 0x04
// and is a point on the curve. The server learns nothing from the key beyond that; it stores it.
func (k *E2EKey) Validate() error {
	if k == nil || k.V != E2EKeyVersion || len(k.KEM) != 87 {
		return ErrE2EKey
	}
	b, err := base64.RawURLEncoding.Strict().DecodeString(k.KEM)
	if err != nil || len(b) != e2eKEMLen || b[0] != 0x04 {
		return ErrE2EKey
	}
	if _, err := ecdh.P256().NewPublicKey(b); err != nil {
		return ErrE2EKey
	}
	return nil
}

// Equal reports whether two keys are the same key (both absent counts as equal).
func (k *E2EKey) Equal(o *E2EKey) bool {
	if k == nil || o == nil {
		return k == o
	}
	return k.V == o.V && k.KEM == o.KEM
}

// Clone returns a copy, or nil.
func (k *E2EKey) Clone() *E2EKey {
	if k == nil {
		return nil
	}
	c := *k
	return &c
}

// HasCapability reports whether a device's capability list names c.
func HasCapability(caps []string, c string) bool {
	for _, x := range caps {
		if x == c {
			return true
		}
	}
	return false
}

// CheckBox is the whole of what the server asks of a box: it is one JSON object that fits a record.
// It never looks at a member - a box is opaque to the server (item DH) - so the check cannot depend on
// the sender's idea of the box's shape.
func CheckBox(raw json.RawMessage) error {
	if len(raw) == 0 || raw[0] != '{' {
		return errors.New("a box is one JSON object")
	}
	if len(raw) > MaxRecord {
		return ErrRecordSize
	}
	return nil
}
