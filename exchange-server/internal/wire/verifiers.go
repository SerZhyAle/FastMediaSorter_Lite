package wire

import (
	"crypto/pbkdf2"
	"crypto/sha256"
	"errors"
	"strings"
)

// Listener verifiers of a protected broadcast (DEVICE-EXCHANGE 1.7: item BF of section 20 and item DV of
// LINK-PASSWORD-2026-10-22). `broadcast-start` and `broadcast-update` may carry `verifiers`, a sibling of
// `broadcast` and `maxListeners`: one entry per credential pair that may listen through the relay,
//
//	{"user": "...", "salt": <16 random bytes, base64url>, "hash": <32 bytes, base64url>}
//
// where hash = PBKDF2-HMAC-SHA256(password as the UTF-8 bytes of the Basic credentials, salt, 600000 iterations,
// 32 bytes). The relay never holds a password. The member is a pointer to a slice on purpose: absent is nil
// (on a start, no pair to check; on an update, the held list is unchanged - the 1.6 meaning), `[]` is a pointer
// to an empty slice (the relay checks no listener of that broadcast), anything else replaces the held list
// wholesale.
const (
	// VerifierIterations is the PBKDF2 round count of item BF. It is part of the contract, not of the frame:
	// a verifier made with another count never matches.
	VerifierIterations = 600000

	// VerifierSaltLen and VerifierHashLen are the decoded sizes of `salt` and `hash`.
	VerifierSaltLen = 16
	VerifierHashLen = 32
)

// ErrVerifier is a `verifiers` member the server refuses: too many entries, or an entry whose user, salt or
// hash is out of bounds. The message never carries the offending value.
var ErrVerifier = errors.New("wire: verifiers are out of bounds")

// ListenerVerifier is one entry of `verifiers` as it travels. It is a secret-derived value, so a formatter
// never prints a member (a log line cannot carry one by mistake).
type ListenerVerifier struct {
	User string `json:"user"`
	Salt string `json:"salt"`
	Hash string `json:"hash"`
}

// String never prints a member.
func (ListenerVerifier) String() string { return "wire.ListenerVerifier" }

// GoString never prints a member.
func (ListenerVerifier) GoString() string { return "wire.ListenerVerifier" }

// Verifier is an entry decoded to bytes, as the directory holds it (in memory only, with the broadcast).
type Verifier struct {
	User string
	Salt []byte
	Hash []byte
}

// String never prints a member.
func (Verifier) String() string { return "wire.Verifier" }

// GoString never prints a member.
func (Verifier) GoString() string { return "wire.Verifier" }

// ParseVerifiers checks a `verifiers` list against the bounds of validate.go and decodes it. An empty list is
// valid (and means the relay checks nobody). A user name appears once: a second entry for it could never be
// honoured and could not be revoked alone, so it is refused rather than silently ignored.
func ParseVerifiers(in []ListenerVerifier) ([]Verifier, error) {
	if len(in) > maxVerifiers {
		return nil, ErrVerifier
	}
	out := make([]Verifier, 0, len(in))
	seen := make(map[string]struct{}, len(in))
	for _, v := range in {
		// a Basic user-id cannot hold ':', so an entry with one could never be matched
		if !textOK(v.User, maxVerifierUser) || v.User == "" || strings.Contains(v.User, ":") {
			return nil, ErrVerifier
		}
		if _, dup := seen[v.User]; dup {
			return nil, ErrVerifier
		}
		seen[v.User] = struct{}{}
		salt, err := idEncoding.DecodeString(v.Salt)
		if err != nil || len(salt) != VerifierSaltLen {
			return nil, ErrVerifier
		}
		hash, err := idEncoding.DecodeString(v.Hash)
		if err != nil || len(hash) != VerifierHashLen {
			return nil, ErrVerifier
		}
		out = append(out, Verifier{User: v.User, Salt: salt, Hash: hash})
	}
	return out, nil
}

// DeriveVerifierHash is the one-way value a verifier holds: PBKDF2-HMAC-SHA256 of the password under the
// salt. The relay calls it with VerifierIterations; a broadcaster uses it (or its own implementation of the
// same function) to make an entry; the iteration count is a parameter only so that a test need not pay 600000
// rounds for each of its hashes.
func DeriveVerifierHash(password string, salt []byte, iterations int) []byte {
	// pbkdf2.Key fails only for a non-positive count or an over-long key, neither of which is reachable here
	out, err := pbkdf2.Key(sha256.New, password, salt, iterations, VerifierHashLen)
	if err != nil {
		return make([]byte, VerifierHashLen)
	}
	return out
}

// NewListenerVerifier makes the entry for one credential pair under a salt the caller drew from a random
// source. It is the producer's side of the contract, kept here so the tests of both sides share one definition.
func NewListenerVerifier(user, password string, salt []byte, iterations int) ListenerVerifier {
	return ListenerVerifier{
		User: user,
		Salt: idEncoding.EncodeToString(salt),
		Hash: idEncoding.EncodeToString(DeriveVerifierHash(password, salt, iterations)),
	}
}
