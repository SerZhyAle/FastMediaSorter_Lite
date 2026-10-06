// Package password is the exchange server's password policy and its stored form.
//
// The policy is the owner's (contract sections 1 and 5.2, 2026-10-05) and lives in exactly one place:
// at least 10 characters with an upper-case Latin letter, a lower-case Latin letter and a digit. The
// deployment keeps only a salted Argon2id record in the PHC string format, never the plaintext.
package password

import (
	"crypto/rand"
	"crypto/subtle"
	"encoding/base64"
	"errors"
	"fmt"
	"math/big"
	"strconv"
	"strings"
	"unicode/utf8"

	"golang.org/x/crypto/argon2"
)

// MinLength is the contract's floor, counted in characters.
const MinLength = 10

// GeneratedLength is the length of a generated password (Q138-1).
const GeneratedLength = 20

// Policy refusals. The text is what a UI shows beside the field.
var (
	ErrTooShort  = errors.New("the password must be at least 10 characters long")
	ErrNoUpper   = errors.New("the password must contain an upper-case Latin letter")
	ErrNoLower   = errors.New("the password must contain a lower-case Latin letter")
	ErrNoDigit   = errors.New("the password must contain a digit")
	ErrBadRecord = errors.New("the stored password record is malformed")
)

// Check applies the policy.
func Check(p string) error {
	if utf8.RuneCountInString(p) < MinLength {
		return ErrTooShort
	}
	var up, low, dig bool
	for _, r := range p {
		switch {
		case r >= 'A' && r <= 'Z':
			up = true
		case r >= 'a' && r <= 'z':
			low = true
		case r >= '0' && r <= '9':
			dig = true
		}
	}
	switch {
	case !up:
		return ErrNoUpper
	case !low:
		return ErrNoLower
	case !dig:
		return ErrNoDigit
	}
	return nil
}

// A password without look-alike characters (no I, O, l, 1, 0), because a person types it once per
// producer.
const (
	genUpper = "ABCDEFGHJKLMNPQRSTUVWXYZ"
	genLower = "abcdefghijkmnpqrstuvwxyz"
	genDigit = "23456789"
	genAll   = genUpper + genLower + genDigit
)

// Generate returns a random password that always meets the policy.
func Generate() (string, error) {
	for {
		b := make([]byte, GeneratedLength)
		for i := range b {
			n, err := rand.Int(rand.Reader, big.NewInt(int64(len(genAll))))
			if err != nil {
				return "", err
			}
			b[i] = genAll[n.Int64()]
		}
		if Check(string(b)) == nil {
			return string(b), nil
		}
	}
}

// Params are the Argon2id cost parameters, recorded in the hash string itself so a later change
// still verifies old records (R2 section 4).
type Params struct {
	Time    uint32
	Memory  uint32 // KiB
	Threads uint8
	KeyLen  uint32
}

// DefaultParams is the shipped cost: 64 MiB, 3 passes, 2 lanes.
var DefaultParams = Params{Time: 3, Memory: 64 * 1024, Threads: 2, KeyLen: 32}

const saltLen = 16

// Hash returns the PHC record of p under DefaultParams.
func Hash(p string) (string, error) { return HashWith(p, DefaultParams) }

// HashWith returns the PHC record of p under the given cost (tests use a cheap one).
func HashWith(p string, pr Params) (string, error) {
	salt := make([]byte, saltLen)
	if _, err := rand.Read(salt); err != nil {
		return "", err
	}
	key := argon2.IDKey([]byte(p), salt, pr.Time, pr.Memory, pr.Threads, pr.KeyLen)
	enc := base64.RawStdEncoding
	return fmt.Sprintf("$argon2id$v=%d$m=%d,t=%d,p=%d$%s$%s",
		argon2.Version, pr.Memory, pr.Time, pr.Threads, enc.EncodeToString(salt), enc.EncodeToString(key)), nil
}

type record struct {
	pr   Params
	salt []byte
	key  []byte
}

// Bounds on a record read from a file, so a tampered config cannot ask for a gigabyte of memory.
const (
	maxMemoryKiB = 256 * 1024
	maxTime      = 10
	maxThreads   = 8
)

func parse(rec string) (record, error) {
	var r record
	parts := strings.Split(rec, "$")
	if len(parts) != 6 || parts[0] != "" || parts[1] != "argon2id" {
		return r, ErrBadRecord
	}
	if parts[2] != "v="+strconv.Itoa(argon2.Version) {
		return r, ErrBadRecord
	}
	var m, t, p uint64
	for _, kv := range strings.Split(parts[3], ",") {
		k, v, ok := strings.Cut(kv, "=")
		if !ok {
			return r, ErrBadRecord
		}
		n, err := strconv.ParseUint(v, 10, 32)
		if err != nil {
			return r, ErrBadRecord
		}
		switch k {
		case "m":
			m = n
		case "t":
			t = n
		case "p":
			p = n
		default:
			return r, ErrBadRecord
		}
	}
	if m < 8 || m > maxMemoryKiB || t < 1 || t > maxTime || p < 1 || p > maxThreads {
		return r, ErrBadRecord
	}
	enc := base64.RawStdEncoding
	salt, err1 := enc.DecodeString(parts[4])
	key, err2 := enc.DecodeString(parts[5])
	if err1 != nil || err2 != nil || len(salt) < 8 || len(salt) > 64 || len(key) < 16 || len(key) > 64 {
		return r, ErrBadRecord
	}
	r.pr = Params{Time: uint32(t), Memory: uint32(m), Threads: uint8(p), KeyLen: uint32(len(key))}
	r.salt, r.key = salt, key
	return r, nil
}

// ValidRecord reports whether rec is a well-formed Argon2id record.
func ValidRecord(rec string) error {
	_, err := parse(rec)
	return err
}

// Verify derives the key with the record's own salt and cost and compares in constant time. A
// malformed record verifies nothing.
func Verify(rec, p string) bool {
	r, err := parse(rec)
	if err != nil {
		return false
	}
	got := argon2.IDKey([]byte(p), r.salt, r.pr.Time, r.pr.Memory, r.pr.Threads, r.pr.KeyLen)
	return subtle.ConstantTimeCompare(got, r.key) == 1
}
