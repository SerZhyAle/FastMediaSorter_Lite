package password

import (
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// The vectors file is shared: the Share Manager's own test reads the same JSON (ticket 142), so the
// floor lives in one place and cannot drift between the binary and the UI.
type policyVector struct {
	Password string `json:"password"`
	Accept   bool   `json:"accept"`
	Why      string `json:"why"`
}

func TestPolicyVectors(t *testing.T) {
	raw, err := os.ReadFile(filepath.Join("testdata", "password-policy.json"))
	if err != nil {
		t.Fatal(err)
	}
	var vs []policyVector
	if err := json.Unmarshal(raw, &vs); err != nil {
		t.Fatal(err)
	}
	if len(vs) < 6 {
		t.Fatalf("only %d vectors", len(vs))
	}
	for _, v := range vs {
		err := Check(v.Password)
		if v.Accept && err != nil {
			t.Errorf("%q (%s) must be accepted, got %v", v.Password, v.Why, err)
		}
		if !v.Accept && err == nil {
			t.Errorf("%q (%s) must be refused", v.Password, v.Why)
		}
	}
}

func TestPolicyReasons(t *testing.T) {
	cases := map[string]error{
		"Abcdefgh1":    ErrTooShort,
		"abcdefghij1":  ErrNoUpper,
		"ABCDEFGHIJ1":  ErrNoLower,
		"Abcdefghij":   ErrNoDigit,
		"Абвгдежзий1A": ErrNoLower, // Cyrillic letters do not count as Latin lower-case
	}
	for p, want := range cases {
		if err := Check(p); !errors.Is(err, want) {
			t.Errorf("Check(%q) = %v, want %v", p, err, want)
		}
	}
	if err := Check("Abcdefghi1"); err != nil {
		t.Errorf("exactly ten characters must pass: %v", err)
	}
}

func TestGenerateMeetsPolicyAndAvoidsLookAlikes(t *testing.T) {
	seen := map[string]bool{}
	for i := 0; i < 300; i++ {
		p, err := Generate()
		if err != nil {
			t.Fatal(err)
		}
		if len(p) != GeneratedLength || Check(p) != nil {
			t.Fatalf("generated %q fails the policy", p)
		}
		if strings.ContainsAny(p, "IOl10") {
			t.Fatalf("generated %q has a look-alike character", p)
		}
		seen[p] = true
	}
	if len(seen) < 300 {
		t.Fatal("generated passwords repeat")
	}
}

var cheap = Params{Time: 1, Memory: 8, Threads: 1, KeyLen: 32}

func TestHashVerifyRoundTrip(t *testing.T) {
	rec, err := HashWith("Passw0rdTest", cheap)
	if err != nil {
		t.Fatal(err)
	}
	if !strings.HasPrefix(rec, "$argon2id$v=19$m=8,t=1,p=1$") || strings.Contains(rec, "Passw0rdTest") {
		t.Fatalf("unexpected record %q", rec)
	}
	if !Verify(rec, "Passw0rdTest") || Verify(rec, "Passw0rdTesT") || Verify(rec, "") {
		t.Fatal("verify result wrong")
	}
	rec2, _ := HashWith("Passw0rdTest", cheap)
	if rec == rec2 {
		t.Fatal("the salt must be random per record")
	}
	if ValidRecord(rec) != nil {
		t.Fatal("a fresh record is valid")
	}
}

func TestDefaultParamsRecordVerifies(t *testing.T) {
	rec, err := Hash("Passw0rdTest")
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(rec, "m=65536,t=3,p=2") {
		t.Fatalf("shipped cost changed: %q", rec)
	}
	if !Verify(rec, "Passw0rdTest") {
		t.Fatal("default record does not verify")
	}
}

func TestMalformedRecordsVerifyNothing(t *testing.T) {
	good, _ := HashWith("Passw0rdTest", cheap)
	bad := []string{
		"", "plaintext", "$argon2i$v=19$m=8,t=1,p=1$c2FsdHNhbHQ$aGFzaGhhc2hoYXNoaGFzaA",
		strings.Replace(good, "v=19", "v=16", 1),
		strings.Replace(good, "m=8", "m=99999999", 1), // a tampered config must not ask for gigabytes
		strings.Replace(good, "t=1", "t=0", 1),
		strings.Replace(good, "p=1", "p=200", 1),
		good + "$extra",
		"$argon2id$v=19$m=8,t=1,p=1$!!$!!",
	}
	for _, r := range bad {
		if ValidRecord(r) == nil {
			t.Errorf("record %q must be invalid", r)
		}
		if Verify(r, "Passw0rdTest") {
			t.Errorf("record %q verified", r)
		}
	}
}
