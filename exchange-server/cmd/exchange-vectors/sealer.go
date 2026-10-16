package main

import (
	"crypto/aes"
	"crypto/cipher"
	"crypto/ecdh"
	"crypto/hmac"
	"crypto/sha256"
	"encoding/base64"
	"encoding/binary"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"

	"fms/exchange-server/internal/wire"
)

// A test-only, deterministic sealer of DEVICE-EXCHANGE section 27 (ticket 234, T-6). The exchange server never
// opens, checks or changes a box - it has no HPKE and needs none - but a transcript whose boxes open with
// nothing would prove only that the server forwards bytes. The boxes of the `sealed-*` transcripts are real:
// they are made here, over the fixed keys of the shared vector file e2e-v1.json (item DH: HPKE Auth mode,
// DHKEM(P-256, HKDF-SHA256), HKDF-SHA256, AES-128-GCM, `info` "fmsx-e2e-v1", `aad` the context, then
// AES-256-GCM of the payload under a content key with the context as associated data), with the ephemeral
// keys, the content keys and the nonces drawn from seeded sources, so the bytes are a function of the code.
// It lives in the generator and ships nowhere; sealer_test.go opens the shared file's own boxes with it, so
// the implementation is held to the other products' independent ones.

// e2eDevice is one of the five devices of e2e-v1.json: its name there, its private key (a replaying product
// opens boxes with it - a real key never leaves its device) and the public key a device declares.
type e2eDevice struct {
	Name string
	Priv string // 32 bytes, hex
	KEM  string // the 65-byte uncompressed SEC1 point, base64url without padding
}

// e2eDevices are copied verbatim from tests/Lite.Tests/TestData/exchange-vectors/e2e-v1.json
// (DEVICE-EXCHANGE 0.29, producer "FastMediaSorter Android, ticket S4240"); TestTheSealerKeysAreTheSharedFilesKeys
// reads that file and fails on any difference.
var e2eDevices = map[string]e2eDevice{
	"phone":    {"phone", "8de947620593691cc83586e8db24bfa306b9d554e7434d453692909a828cfb7c", "BMHBNOi24fTvs6FVK82lwF9b1Kmd1yjW_czRpmXYba0FIgE5tdQz6-ECIg4o1ZsDxuymoQC3aCvBDvmY6UO4iZw"},
	"tablet":   {"tablet", "af37a158c50184b82b3f055834c76431c098ccc4ba78f4677ec9172f8e4676ed", "BJH20sAyR6UWwTxu7X7SDlWMbQUpH5pgoLcYCSYRbQWxF16-963dwUwGIqf_XAWg-BwxoLcC0dgkLJ6743Menfg"},
	"watch":    {"watch", "0dee329261d24b9c22630bf453d1ab6f0039171b5685ded0a20008e9a13e5a95", "BIYFEEvPMqa_BssoofK1bkfPiWYPgnBJzzGhYiUJGeXDCuKE4wbwwY3B4q6Yy-qJwca52eLwbh3ACjxVr3ctyB4"},
	"guest":    {"guest", "b271f3ffd759dae99e95f0e43080c3bac1bc104a81bd6c0b85c01146dd693c44", "BJrHosJAujRIofWgqvW7zwb4YyUMvGdn5mYjqai3ou-RQnl77mueGp0ehE8QDY0G-O4l0GupneV2vXP7x6ZEubA"},
	"stranger": {"stranger", "6f0f39d390df0183ac0c1600a0f8ef657e882926c62208fe16959ab3b2cf7091", "BFW262O-3fyP9Tg3-jzFlE8R9M3WZYHOvkPtvqYKBuuCv0JsniBrPM-um_igxqpr18DohFz2j2E1NFpO_eZgwgE"},
}

// Key is the `e2eKey` member the device declares on enroll and register (item DF).
func (d e2eDevice) Key() *wire.E2EKey { return &wire.E2EKey{V: wire.E2EKeyVersion, KEM: d.KEM} }

func (d e2eDevice) private() *ecdh.PrivateKey {
	raw, err := hex.DecodeString(d.Priv)
	if err != nil {
		failf("device %s: %v", d.Name, err)
	}
	k, err := ecdh.P256().NewPrivateKey(raw)
	if err != nil {
		failf("device %s: %v", d.Name, err)
	}
	return k
}

// party is a device in a scenario: its name in e2e-v1.json and the deviceId the scenario's server knows it by.
type party struct {
	Dev e2eDevice
	ID  string
}

const (
	hpkeInfo     = "fmsx-e2e-v1"
	hpkeModeAuth = 0x02
)

// --- RFC 9180: HKDF-SHA256, the labeled forms, DHKEM(P-256), the Auth-mode key schedule ---

func hkdfExtract(salt, ikm []byte) []byte {
	m := hmac.New(sha256.New, salt)
	m.Write(ikm)
	return m.Sum(nil)
}

func hkdfExpand(prk, info []byte, n int) []byte {
	var out, t []byte
	for i := byte(1); len(out) < n; i++ {
		m := hmac.New(sha256.New, prk)
		m.Write(t)
		m.Write(info)
		m.Write([]byte{i})
		t = m.Sum(nil)
		out = append(out, t...)
	}
	return out[:n]
}

func labeledExtract(suite, salt []byte, label string, ikm []byte) []byte {
	li := append(append(append([]byte("HPKE-v1"), suite...), label...), ikm...)
	return hkdfExtract(salt, li)
}

func labeledExpand(suite, prk []byte, label string, info []byte, n int) []byte {
	li := []byte{byte(n >> 8), byte(n)}
	li = append(append(append(append(li, "HPKE-v1"...), suite...), label...), info...)
	return hkdfExpand(prk, li, n)
}

var (
	kemSuite  = []byte{'K', 'E', 'M', 0x00, 0x10}
	hpkeSuite = []byte{'H', 'P', 'K', 'E', 0x00, 0x10, 0x00, 0x01, 0x00, 0x01}
)

func kemSecret(dh, kemContext []byte) []byte {
	prk := labeledExtract(kemSuite, nil, "eae_prk", dh)
	return labeledExpand(kemSuite, prk, "shared_secret", kemContext, 32)
}

// keySchedule is the Auth-mode schedule with no PSK: the AEAD key and the base nonce.
func keySchedule(shared, info []byte) (key, nonce []byte) {
	pskIDHash := labeledExtract(hpkeSuite, nil, "psk_id_hash", nil)
	infoHash := labeledExtract(hpkeSuite, nil, "info_hash", info)
	ctx := append(append([]byte{hpkeModeAuth}, pskIDHash...), infoHash...)
	secret := labeledExtract(hpkeSuite, shared, "secret", nil)
	return labeledExpand(hpkeSuite, secret, "key", ctx, 16), labeledExpand(hpkeSuite, secret, "base_nonce", ctx, 12)
}

func gcm(key []byte) cipher.AEAD {
	b, err := aes.NewCipher(key)
	if err != nil {
		failf("aes: %v", err)
	}
	g, err := cipher.NewGCM(b)
	if err != nil {
		failf("gcm: %v", err)
	}
	return g
}

// authEncap is SealAuth of RFC 9180 for one recipient, with the sender's ephemeral key given.
func authEncap(skE, skS *ecdh.PrivateKey, pkR *ecdh.PublicKey, aad, ck []byte) (enc, wk []byte, err error) {
	d1, err := skE.ECDH(pkR)
	if err != nil {
		return nil, nil, err
	}
	d2, err := skS.ECDH(pkR)
	if err != nil {
		return nil, nil, err
	}
	enc = skE.PublicKey().Bytes()
	kctx := append(append(append([]byte(nil), enc...), pkR.Bytes()...), skS.PublicKey().Bytes()...)
	key, nonce := keySchedule(kemSecret(append(d1, d2...), kctx), []byte(hpkeInfo))
	return enc, gcm(key).Seal(nil, nonce, ck, aad), nil
}

// authDecap is OpenAuth of RFC 9180: the recipient's private key, the sender's public key `fromKey`.
func authDecap(enc []byte, skR *ecdh.PrivateKey, pkS *ecdh.PublicKey, aad, wk []byte) ([]byte, error) {
	pkE, err := ecdh.P256().NewPublicKey(enc)
	if err != nil {
		return nil, err
	}
	d1, err := skR.ECDH(pkE)
	if err != nil {
		return nil, err
	}
	d2, err := skR.ECDH(pkS)
	if err != nil {
		return nil, err
	}
	kctx := append(append(append([]byte(nil), enc...), skR.PublicKey().Bytes()...), pkS.Bytes()...)
	key, nonce := keySchedule(kemSecret(append(d1, d2...), kctx), []byte(hpkeInfo))
	return gcm(key).Open(nil, nonce, wk, aad)
}

// --- the box (item DH) ---

type boxKey struct {
	To  string `json:"to"`
	Enc string `json:"enc"`
	WK  string `json:"wk"`
}

type boxDoc struct {
	V       int      `json:"v"`
	From    string   `json:"from"`
	FromKey string   `json:"fromKey"`
	N       string   `json:"n"`
	CT      string   `json:"ct"`
	Keys    []boxKey `json:"keys"`
}

var b64 = base64.RawURLEncoding

// newPrivate draws a P-256 private key from a deterministic source (a scalar out of range is drawn again).
func newPrivate(src io.Reader) *ecdh.PrivateKey {
	for {
		raw := make([]byte, 32)
		if _, err := io.ReadFull(src, raw); err != nil {
			failf("seed: %v", err)
		}
		if k, err := ecdh.P256().NewPrivateKey(raw); err == nil {
			return k
		}
	}
}

// sealBox seals payload (one JSON object) for the recipients with the sender's key. The content key, the
// nonce and the ephemeral keys come from a source seeded with label, which names the box and no other.
// It returns the box as the member the wire carries, and the content key (a clipboard value needs it).
func sealBox(label string, from party, to []party, context string, payload string) (box json.RawMessage, ck []byte) {
	if !json.Valid([]byte(payload)) || payload[0] != '{' {
		failf("box %s: the payload is not one JSON object", label)
	}
	src := newSeeded("sealed-box/" + label)
	ck, n := make([]byte, 32), make([]byte, 12)
	if _, err := io.ReadFull(src, ck); err != nil {
		failf("%v", err)
	}
	if _, err := io.ReadFull(src, n); err != nil {
		failf("%v", err)
	}
	doc := boxDoc{V: 1, From: from.ID, FromKey: from.Dev.KEM, N: b64.EncodeToString(n),
		CT: b64.EncodeToString(gcm(ck).Seal(nil, n, []byte(payload), []byte(context)))}
	skS := from.Dev.private()
	for _, r := range to {
		rawKey, err := b64.DecodeString(r.Dev.KEM)
		if err != nil {
			failf("%v", err)
		}
		pkR, err := ecdh.P256().NewPublicKey(rawKey)
		if err != nil {
			failf("%v", err)
		}
		enc, wk, err := authEncap(newPrivate(src), skS, pkR, []byte(context), ck)
		if err != nil {
			failf("box %s: %v", label, err)
		}
		doc.Keys = append(doc.Keys, boxKey{To: r.ID, Enc: b64.EncodeToString(enc), WK: b64.EncodeToString(wk)})
	}
	raw, err := json.Marshal(doc)
	if err != nil {
		failf("%v", err)
	}
	if err := wire.CheckBox(raw); err != nil {
		failf("box %s: %v", label, err)
	}
	return raw, ck
}

// openBox opens a box as the device `me` (its deviceId and private key). The sender's key is the one the
// reader trusts for `from` (item DG); the context is the one the reader expects for the place it found the
// box in. It returns the payload and the content key.
func openBox(box json.RawMessage, meID string, me *ecdh.PrivateKey, trustedFromKey, context string) (payload, ck []byte, err error) {
	var doc boxDoc
	if err := json.Unmarshal(box, &doc); err != nil {
		return nil, nil, err
	}
	if doc.V != 1 {
		return nil, nil, errors.New("unknown version")
	}
	if doc.FromKey != trustedFromKey {
		return nil, nil, errors.New("untrusted fromKey")
	}
	var entry *boxKey
	for i := range doc.Keys {
		if doc.Keys[i].To == meID {
			entry = &doc.Keys[i]
		}
	}
	if entry == nil {
		return nil, nil, errors.New("no entry")
	}
	rawS, err := b64.DecodeString(doc.FromKey)
	if err != nil {
		return nil, nil, err
	}
	pkS, err := ecdh.P256().NewPublicKey(rawS)
	if err != nil {
		return nil, nil, err
	}
	enc, err := b64.DecodeString(entry.Enc)
	if err != nil {
		return nil, nil, err
	}
	wk, err := b64.DecodeString(entry.WK)
	if err != nil {
		return nil, nil, err
	}
	if ck, err = authDecap(enc, me, pkS, []byte(context), wk); err != nil {
		return nil, nil, fmt.Errorf("bad tag: %w", err)
	}
	n, err := b64.DecodeString(doc.N)
	if err != nil {
		return nil, nil, err
	}
	ct, err := b64.DecodeString(doc.CT)
	if err != nil {
		return nil, nil, err
	}
	if payload, err = gcm(ck).Open(nil, n, ct, []byte(context)); err != nil {
		return nil, nil, fmt.Errorf("bad tag: %w", err)
	}
	return payload, ck, nil
}

// --- the bytes of a sealed clipboard value (item DN) ---

const clipSegment = 65536

// clipValueKey is HKDF-SHA256 over the box's content key with an empty salt and `info` "fmsx-e2e-v1 clipboard".
func clipValueKey(ck []byte) []byte {
	return hkdfExpand(hkdfExtract(nil, ck), []byte("fmsx-e2e-v1 clipboard"), 32)
}

func clipNonce(i uint64, last bool) []byte {
	n := make([]byte, 12)
	binary.BigEndian.PutUint64(n[3:], i)
	if last {
		n[11] = 1
	}
	return n
}

// sealClipboard splits the clear bytes into segments of 65536 (the last of 1 to 65536) and seals each
// under the value key with the nonce of its index and last flag and no associated data.
func sealClipboard(ck, clear []byte) []byte {
	g := gcm(clipValueKey(ck))
	var out []byte
	for i, off := uint64(0), 0; ; i++ {
		end := off + clipSegment
		last := end >= len(clear)
		if last {
			end = len(clear)
		}
		out = g.Seal(out, clipNonce(i, last), clear[off:end], nil)
		off = end
		if last {
			return out
		}
	}
}

// openClipboard is the receiver's side: every segment must open, the last must be marked last.
func openClipboard(ck, sealed []byte) ([]byte, error) {
	g := gcm(clipValueKey(ck))
	var out []byte
	for i, off := uint64(0), 0; ; i++ {
		if off >= len(sealed) {
			return nil, errors.New("the last segment is missing")
		}
		end := off + clipSegment + g.Overhead()
		last := end >= len(sealed)
		if last {
			end = len(sealed)
		}
		seg, err := g.Open(nil, clipNonce(i, last), sealed[off:end], nil)
		if err != nil {
			return nil, fmt.Errorf("bad tag: %w", err)
		}
		out = append(out, seg...)
		off = end
		if last {
			return out, nil
		}
	}
}
