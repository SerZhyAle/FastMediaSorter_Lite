package rendezvous

import (
	"bytes"
	"encoding/base64"
	"strings"
	"sync"
	"testing"
	"time"

	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/wire"
)

// White-box tests of the relay's listener check (item BF, item DV): what is hashed, when, and how often. The
// wire-level tests of the same item are in relay_auth_test.go.

type checkClock struct {
	mu sync.Mutex
	t  time.Time
}

func (c *checkClock) Now() time.Time {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.t
}

func (c *checkClock) Add(d time.Duration) {
	c.mu.Lock()
	c.t = c.t.Add(d)
	c.mu.Unlock()
}

const checkRounds = 2 // PBKDF2 rounds of these tests: the shapes are under test, not the cost

func basicHeader(user, password string) string {
	return "Basic " + base64.StdEncoding.EncodeToString([]byte(user+":"+password))
}

func checkVerifiersOf(users ...string) []wire.Verifier {
	var out []wire.Verifier
	for i, u := range users {
		salt := bytes.Repeat([]byte{byte(i + 1)}, wire.VerifierSaltLen)
		out = append(out, wire.Verifier{User: u, Salt: salt, Hash: wire.DeriveVerifierHash("pw-"+u, salt, checkRounds)})
	}
	return out
}

func newCheckRelay(clk *checkClock) *Relay {
	return NewRelay(RelayConfig{Now: clk.Now, CheckIterations: checkRounds})
}

func TestNoVerifierMeansNothingIsCheckedWithOrWithoutAHeader(t *testing.T) {
	r := newCheckRelay(&checkClock{t: time.Unix(1000, 0)})
	for _, vs := range [][]wire.Verifier{nil, {}} {
		for _, h := range []string{"", basicHeader("anyone", "anything"), "Digest realm=x", "Basic !!!"} {
			if got := r.checkListener("id", "1.2.3.4", h, vs); got != checkAdmit {
				t.Fatalf("%q with %d verifiers: %v", h, len(vs), got)
			}
		}
	}
	if r.Checks() != 0 {
		t.Fatal("nothing was hashed")
	}
}

func TestAChallengeCostsNothingAndTheRetryWithAPairIsOneCheck(t *testing.T) {
	r := newCheckRelay(&checkClock{t: time.Unix(1000, 0)})
	vs := checkVerifiersOf("own")
	for _, h := range []string{"", "Bearer abc", "Basic", "Basic !!!", "Basic " + base64.StdEncoding.EncodeToString([]byte("no-colon")),
		basicHeader("", "pw-own"), "Basic " + base64.StdEncoding.EncodeToString([]byte("own:"+strings.Repeat("x", maxBasicPassword+1)))} {
		if got := r.checkListener("id", "1.2.3.4", h, vs); got != checkChallenge {
			t.Fatalf("%q: %v", h, got)
		}
	}
	if r.Checks() != 0 || !r.checks.Has("id\x001.2.3.4", limits.ListenCheck) {
		t.Fatal("a request without usable credentials must hash nothing and must not use the second's token")
	}
	// the very next request, with the pair, is served: the challenge did not eat the token
	if got := r.checkListener("id", "1.2.3.4", basicHeader("own", "pw-own"), vs); got != checkAdmit {
		t.Fatalf("%v", got)
	}
	if r.Checks() != 1 {
		t.Fatalf("one check: %d", r.Checks())
	}
}

func TestOnlyTheRightPairPassesAndTheSchemeNameIsCaseInsensitive(t *testing.T) {
	clk := &checkClock{t: time.Unix(1000, 0)}
	r := newCheckRelay(clk)
	vs := checkVerifiersOf("own", "link")
	step := func(h string, want listenCheck) {
		t.Helper()
		clk.Add(1100 * time.Millisecond)
		if got := r.checkListener("id", "1.2.3.4", h, vs); got != want {
			t.Fatalf("%q: %v, want %v", h, got, want)
		}
	}
	step(basicHeader("own", "pw-own"), checkAdmit)
	step(basicHeader("link", "pw-link"), checkAdmit)
	step("bAsIc "+strings.TrimPrefix(basicHeader("own", "pw-own"), "Basic "), checkAdmit)
	step(basicHeader("own", "pw-link"), checkDenied)
	step(basicHeader("link", "pw-own"), checkDenied)
	step(basicHeader("own", ""), checkDenied)
	step(basicHeader("own", "PW-OWN"), checkDenied)
	step(basicHeader("nobody", "pw-own"), checkDenied)
	step(basicHeader("OWN", "pw-own"), checkDenied) // a user name is compared as bytes
	// a colon inside the password is the password's
	vs2 := []wire.Verifier{{User: "u", Salt: bytes.Repeat([]byte{9}, wire.VerifierSaltLen)}}
	vs2[0].Hash = wire.DeriveVerifierHash("a:b:c", vs2[0].Salt, checkRounds)
	clk.Add(1100 * time.Millisecond)
	if got := r.checkListener("id", "1.2.3.4", basicHeader("u", "a:b:c"), vs2); got != checkAdmit {
		t.Fatalf("a password with colons: %v", got)
	}
}

func TestAnUnknownUserCostsTheSameHashAsAWrongPassword(t *testing.T) {
	clk := &checkClock{t: time.Unix(1000, 0)}
	r := newCheckRelay(clk)
	vs := checkVerifiersOf("own")
	before := r.Checks()
	clk.Add(1100 * time.Millisecond)
	if got := r.checkListener("id", "1.2.3.4", basicHeader("own", "wrong"), vs); got != checkDenied {
		t.Fatal(got)
	}
	afterWrong := r.Checks()
	clk.Add(1100 * time.Millisecond)
	if got := r.checkListener("id", "1.2.3.4", basicHeader("nobody", "wrong"), vs); got != checkDenied {
		t.Fatal(got)
	}
	if afterWrong-before != 1 || r.Checks()-afterWrong != 1 {
		t.Fatalf("a wrong password and an unknown user must each run exactly one PBKDF2: %d, %d", afterWrong-before, r.Checks()-afterWrong)
	}
	// the dummy can never be the answer: the relay's dummy hash is not what any password derives to
	if r.pairMatches(vs, "nobody", "") || r.pairMatches(nil, "own", "pw-own") {
		t.Fatal("an unknown user matched")
	}
}

func TestASecondCheckInsideASecondIsRefusedWithoutHashing(t *testing.T) {
	clk := &checkClock{t: time.Unix(1000, 0)}
	r := newCheckRelay(clk)
	vs := checkVerifiersOf("own")
	right, wrong := basicHeader("own", "pw-own"), basicHeader("own", "wrong")

	if got := r.checkListener("b1", "1.2.3.4", wrong, vs); got != checkDenied {
		t.Fatal(got)
	}
	if r.Checks() != 1 {
		t.Fatalf("%d", r.Checks())
	}
	// the right pair, 900 ms later, is refused: it is the second check of the second, and it hashes nothing
	clk.Add(900 * time.Millisecond)
	if got := r.checkListener("b1", "1.2.3.4", right, vs); got != checkDenied {
		t.Fatalf("a second check inside a second: %v", got)
	}
	if r.Checks() != 1 {
		t.Fatalf("the refused check hashed: %d", r.Checks())
	}
	// another source address, and another broadcast, have their own second
	if got := r.checkListener("b1", "5.6.7.8", right, vs); got != checkAdmit {
		t.Fatalf("another source: %v", got)
	}
	if got := r.checkListener("b2", "1.2.3.4", right, vs); got != checkAdmit {
		t.Fatalf("another broadcast: %v", got)
	}
	// and the first one's second ends
	clk.Add(200 * time.Millisecond)
	if got := r.checkListener("b1", "1.2.3.4", right, vs); got != checkAdmit {
		t.Fatalf("after the second: %v", got)
	}
	// a right password spends the token as a wrong one does
	if got := r.checkListener("b1", "1.2.3.4", right, vs); got != checkDenied {
		t.Fatalf("a success must not refund: %v", got)
	}
	if r.Checks() != 4 {
		t.Fatalf("%d", r.Checks())
	}
}

func TestAFullCheckPoolIsNeitherAnAdmissionNorADenial(t *testing.T) {
	clk := &checkClock{t: time.Unix(1000, 0)}
	r := newCheckRelay(clk)
	r.pool = limits.NewVerifier(nil, 1, 20*time.Millisecond)
	release := r.pool.Hold()
	defer release()
	if got := r.checkListener("id", "1.2.3.4", basicHeader("own", "pw-own"), checkVerifiersOf("own")); got != checkBusy {
		t.Fatalf("%v", got)
	}
	if r.Checks() != 0 {
		t.Fatal("a check that found no slot hashed")
	}
}

func TestTheDummyIsRandomPerRelayAndTheDefaultsAreTheContractsOwn(t *testing.T) {
	a, b := newCheckRelay(&checkClock{}), newCheckRelay(&checkClock{})
	if bytes.Equal(a.dummySalt, b.dummySalt) || bytes.Equal(a.dummyHash, b.dummyHash) || len(a.dummySalt) != wire.VerifierSaltLen || len(a.dummyHash) != wire.VerifierHashLen {
		t.Fatal("the dummy salt and hash are random bytes of the right size")
	}
	d := NewRelay(RelayConfig{})
	if d.iterations != 600000 {
		t.Fatalf("the default must be the contract's 600000 rounds: %d", d.iterations)
	}
	if r := limits.ListenCheckRate(); r.PerMinute != 60 || r.Burst != 1 {
		t.Fatalf("one check per second: %+v", r)
	}
}

func TestBasicCredentialsReadsTheHeaderOfRFC7617(t *testing.T) {
	for _, c := range []struct {
		in, user, pw string
		ok           bool
	}{
		{basicHeader("a", "b"), "a", "b", true},
		{"  " + basicHeader("a", "b") + "  ", "a", "b", true},
		{basicHeader("a", "p:q"), "a", "p:q", true},
		{basicHeader("a", ""), "a", "", true},
		{basicHeader("ü", "ß"), "ü", "ß", true},
		{basicHeader("", "b"), "", "", false},
		{"Basic", "", "", false},
		{"Basic   ", "", "", false},
		{"Digest username=a", "", "", false},
		{"Basic not base64!", "", "", false},
		{"", "", "", false},
	} {
		u, p, ok := basicCredentials(c.in)
		if u != c.user || p != c.pw || ok != c.ok {
			t.Errorf("%q: %q %q %v", c.in, u, p, ok)
		}
	}
}
