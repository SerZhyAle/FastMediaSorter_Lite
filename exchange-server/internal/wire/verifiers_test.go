package wire

import (
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"strings"
	"testing"

	xpbkdf2 "golang.org/x/crypto/pbkdf2"
)

// Item BF and item DV: the `verifiers` member of broadcast-start and broadcast-update.

func verifierSalt(b byte) []byte { return bytes.Repeat([]byte{b}, VerifierSaltLen) }

// goodVerifier is an entry made with one round: these tests are about shapes, not about the cost of a hash.
func goodVerifier(user string, b byte) ListenerVerifier {
	return NewListenerVerifier(user, "password-of-"+user, verifierSalt(b), 1)
}

func startBody(members string) []byte {
	return []byte(`{"schemaVersion":2,"type":"broadcast-start","broadcast":{"broadcastId":"BBBBBBBBBBBBBBBBBBBBBA"}` + members + `}`)
}

func updateBody(members string) []byte {
	return []byte(`{"schemaVersion":2,"type":"broadcast-update","broadcast":{"broadcastId":"BBBBBBBBBBBBBBBBBBBBBA"}` + members + `}`)
}

func verifiersJSON(t *testing.T, vs ...ListenerVerifier) string {
	t.Helper()
	if vs == nil {
		vs = []ListenerVerifier{}
	}
	b, err := json.Marshal(vs)
	if err != nil {
		t.Fatal(err)
	}
	return `,"verifiers":` + string(b)
}

// The relay's derivation is PBKDF2-HMAC-SHA256: the published vectors for it (the SHA-256 companions of RFC 6070),
// and an independent implementation at the count the contract fixes.
func TestTheDerivationIsPBKDF2HMACSHA256AtTheContractsCount(t *testing.T) {
	for _, c := range []struct {
		iter int
		want string
	}{
		{1, "120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b"},
		{4096, "c5e478d59288c841aa530db6845c4c8d962893a001ce4e11a4963873aa98134a"},
	} {
		if got := hex.EncodeToString(DeriveVerifierHash("password", []byte("salt"), c.iter)); got != c.want {
			t.Errorf("%d rounds: %s", c.iter, got)
		}
	}
	if VerifierIterations != 600000 || VerifierSaltLen != 16 || VerifierHashLen != 32 {
		t.Fatalf("the constants of item BF moved: %d %d %d", VerifierIterations, VerifierSaltLen, VerifierHashLen)
	}
	salt := verifierSalt(7)
	want := xpbkdf2.Key([]byte("a longer password, with ünïcode"), salt, 600000, 32, sha256.New)
	if got := DeriveVerifierHash("a longer password, with ünïcode", salt, VerifierIterations); !bytes.Equal(got, want) {
		t.Fatal("the derivation at 600000 rounds disagrees with an independent PBKDF2")
	}
}

func TestAMemberAbsentNullEmptyAndListedAreFourDifferentThings(t *testing.T) {
	for _, mk := range []func(string) []byte{startBody, updateBody} {
		var verifiers func(Envelope) *[]ListenerVerifier = func(e Envelope) *[]ListenerVerifier {
			switch v := e.(type) {
			case *BroadcastStart:
				return v.Verifiers
			case *BroadcastUpdate:
				return v.Verifiers
			}
			t.Fatalf("%T", e)
			return nil
		}
		decode := func(members string) *[]ListenerVerifier {
			t.Helper()
			e, err := Decode(mk(members))
			if err != nil {
				t.Fatalf("%s: %v", members, err)
			}
			return verifiers(e)
		}
		if decode("") != nil {
			t.Error("no member is nil: no pair on a start, no change on an update")
		}
		if decode(`,"verifiers":null`) != nil {
			t.Error("null says nothing, like an absent member")
		}
		if v := decode(`,"verifiers":[]`); v == nil || len(*v) != 0 {
			t.Error("an empty list is a list: the relay checks nobody")
		}
		if v := decode(verifiersJSON(t, goodVerifier("own", 1), goodVerifier("link", 2))); v == nil || len(*v) != 2 || (*v)[1].User != "link" {
			t.Errorf("a list: %v", v)
		}
	}
}

func TestEncodeKeepsTheOldBytesWithoutTheMemberAndWritesAnEmptyListWhenAsked(t *testing.T) {
	rec := json.RawMessage(`{"broadcastId":"BBBBBBBBBBBBBBBBBBBBBA"}`)
	// no vector moves: an envelope that sends no `verifiers` is byte for byte what it was
	b, err := Encode(&BroadcastStart{Broadcast: rec})
	if err != nil || string(b) != `{"schemaVersion":2,"type":"broadcast-start","broadcast":{"broadcastId":"BBBBBBBBBBBBBBBBBBBBBA"}}` {
		t.Fatalf("%s %v", b, err)
	}
	b, err = Encode(&BroadcastUpdate{Broadcast: rec})
	if err != nil || strings.Contains(string(b), "verifiers") {
		t.Fatalf("%s %v", b, err)
	}
	empty := []ListenerVerifier{}
	for _, e := range []Envelope{&BroadcastStart{Broadcast: rec, Verifiers: &empty}, &BroadcastUpdate{Broadcast: rec, Verifiers: &empty}} {
		b, err = Encode(e)
		if err != nil || !strings.HasSuffix(string(b), `,"verifiers":[]}`) {
			t.Fatalf("an empty list must be written as []: %s %v", b, err)
		}
	}
	one := []ListenerVerifier{goodVerifier("own", 3)}
	b, _ = Encode(&BroadcastUpdate{Broadcast: rec, Verifiers: &one})
	back, err := Decode(b)
	if err != nil || len(*back.(*BroadcastUpdate).Verifiers) != 1 || (*back.(*BroadcastUpdate).Verifiers)[0] != one[0] {
		t.Fatalf("round trip: %v", err)
	}
}

func TestTheCapOfVerifiersIsEnforcedOnBothEnvelopes(t *testing.T) {
	make64 := func(n int) []ListenerVerifier {
		out := make([]ListenerVerifier, n)
		for i := range out {
			out[i] = goodVerifier(fmt.Sprintf("user-%03d", i), byte(i))
		}
		return out
	}
	for name, mk := range map[string]func(string) []byte{"start": startBody, "update": updateBody} {
		if _, err := Decode(mk(verifiersJSON(t, make64(maxVerifiers)...))); err != nil {
			t.Errorf("%s: exactly the cap is accepted: %v", name, err)
		}
		if _, err := Decode(mk(verifiersJSON(t, make64(maxVerifiers+1)...))); !errors.Is(err, ErrMalformed) {
			t.Errorf("%s: one above the cap: %v", name, err)
		}
	}
	if maxVerifiers != 64 {
		t.Fatalf("the cap changed to %d: the code comment in validate.go and the report of ticket 263 name 64", maxVerifiers)
	}
}

func TestMalformedEntriesAreRefusedLikeAnyMalformedFrame(t *testing.T) {
	good := goodVerifier("own", 1)
	mut := func(f func(*ListenerVerifier)) ListenerVerifier { v := good; f(&v); return v }
	enc := idEncoding
	cases := map[string]ListenerVerifier{
		"no user":                    mut(func(v *ListenerVerifier) { v.User = "" }),
		"a colon in the user":        mut(func(v *ListenerVerifier) { v.User = "a:b" }),
		"a user over the bound":      mut(func(v *ListenerVerifier) { v.User = strings.Repeat("u", maxVerifierUser+1) }),
		"a control character":        mut(func(v *ListenerVerifier) { v.User = "a\nb" }),
		"no salt":                    mut(func(v *ListenerVerifier) { v.Salt = "" }),
		"a short salt":               mut(func(v *ListenerVerifier) { v.Salt = enc.EncodeToString(make([]byte, VerifierSaltLen-1)) }),
		"a long salt":                mut(func(v *ListenerVerifier) { v.Salt = enc.EncodeToString(make([]byte, VerifierSaltLen+1)) }),
		"a padded salt":              mut(func(v *ListenerVerifier) { v.Salt += "==" }),
		"a salt in the std alphabet": mut(func(v *ListenerVerifier) { v.Salt = strings.Repeat("+", 22) }),
		"no hash":                    mut(func(v *ListenerVerifier) { v.Hash = "" }),
		"a short hash":               mut(func(v *ListenerVerifier) { v.Hash = enc.EncodeToString(make([]byte, VerifierHashLen-1)) }),
		"a long hash":                mut(func(v *ListenerVerifier) { v.Hash = enc.EncodeToString(make([]byte, VerifierHashLen+1)) }),
		"a hash that is not base64":  mut(func(v *ListenerVerifier) { v.Hash = strings.Repeat("!", 43) }),
	}
	for name, v := range cases {
		for kind, mk := range map[string]func(string) []byte{"start": startBody, "update": updateBody} {
			if _, err := Decode(mk(verifiersJSON(t, good, v))); !errors.Is(err, ErrMalformed) {
				t.Errorf("%s (%s): %v", name, kind, err)
			}
			if msg := fmt.Sprint(decodeErr(mk(verifiersJSON(t, v)))); strings.Contains(msg, v.Salt) && v.Salt != "" || strings.Contains(msg, v.Hash) && v.Hash != "" {
				t.Errorf("%s (%s): the error names a value: %s", name, kind, msg)
			}
		}
	}
	for name, members := range map[string]string{
		"two entries for one user": verifiersJSON(t, good, good),
		"a null entry":             `,"verifiers":[null]`,
		"an object":                `,"verifiers":{}`,
		"a string":                 `,"verifiers":"x"`,
		"a number":                 `,"verifiers":1`,
		"entries of other shapes":  `,"verifiers":["own"]`,
	} {
		if _, err := Decode(startBody(members)); !errors.Is(err, ErrMalformed) {
			t.Errorf("%s: %v", name, err)
		}
	}
}

func decodeErr(body []byte) error { _, err := Decode(body); return err }

func TestAVerifierNeverPrintsItsMembers(t *testing.T) {
	v := goodVerifier("own", 9)
	parsed, err := ParseVerifiers([]ListenerVerifier{v})
	if err != nil {
		t.Fatal(err)
	}
	for _, shown := range []string{
		fmt.Sprintf("%v %+v %#v %s", v, v, v, v), fmt.Sprintf("%v %+v %#v %s", parsed[0], parsed[0], parsed[0], parsed[0]),
		fmt.Sprintf("%v", []ListenerVerifier{v}), fmt.Sprintf("%+v", parsed),
	} {
		if strings.Contains(shown, v.Salt) || strings.Contains(shown, v.Hash) || strings.Contains(shown, "own") {
			t.Fatalf("a formatter printed a member: %s", shown)
		}
	}
}

func TestParseVerifiersDecodesAndKeepsTheOrder(t *testing.T) {
	if out, err := ParseVerifiers(nil); err != nil || len(out) != 0 {
		t.Fatalf("%v %v", out, err)
	}
	// JSON cannot carry invalid UTF-8 through (the decoder substitutes U+FFFD), so this one is tried on the parser
	bad := goodVerifier("own", 1)
	bad.User = "a\xffb"
	if _, err := ParseVerifiers([]ListenerVerifier{bad}); !errors.Is(err, ErrVerifier) {
		t.Fatalf("invalid UTF-8 in a user: %v", err)
	}
	a, b := goodVerifier("own", 1), goodVerifier("guest", 2)
	out, err := ParseVerifiers([]ListenerVerifier{a, b})
	if err != nil || len(out) != 2 || out[0].User != "own" || out[1].User != "guest" {
		t.Fatalf("%v %v", out, err)
	}
	if !bytes.Equal(out[0].Salt, verifierSalt(1)) || !bytes.Equal(out[0].Hash, DeriveVerifierHash("password-of-own", verifierSalt(1), 1)) {
		t.Fatal("the entry is not decoded to the bytes it was made from")
	}
}
