package rendezvous_test

import (
	"crypto/rand"
	"encoding/base64"
	"net/http"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/rendezvous"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// DEVICE-EXCHANGE item BF and item DV (ticket 263 T-3, server half): the relay checks a listener's Basic
// credentials against the `verifiers` the broadcaster sent with broadcast-start and broadcast-update. The
// white-box side (what is hashed, when) is in relay_check_internal_test.go; here the real server, real TLS
// and a real HTTP client prove the answers.

const authRounds = 2 // PBKDF2 rounds of these tests (the relay is told the same); the contract's own count is pinned in internal/wire

// authRelay is a relay whose clock a test moves, so that "one check per second" does not cost real seconds.
func authRelay(t *testing.T) (*testkit.Env, *fakeClock) {
	t.Helper()
	clk := newClock()
	env := relayEnv(t, func(rc *rendezvous.RelayConfig) {
		rc.Now = clk.Now
		rc.CheckIterations = authRounds
	})
	return env, clk
}

// authPair makes the entry of one credential pair and tells the Env that none of its parts may reach a log.
func authPair(env *testkit.Env, user, password string) wire.ListenerVerifier {
	salt := make([]byte, wire.VerifierSaltLen)
	if _, err := rand.Read(salt); err != nil {
		panic(err)
	}
	v := wire.NewListenerVerifier(user, password, salt, authRounds)
	env.AddSecret(password)
	env.AddSecret(v.Salt)
	env.AddSecret(v.Hash)
	return v
}

func verifierList(vs ...wire.ListenerVerifier) *[]wire.ListenerVerifier {
	if vs == nil {
		vs = []wire.ListenerVerifier{}
	}
	return &vs
}

// basicLine is a request header line with a pair's credentials; the encoded form is a secret as well.
func basicLine(env *testkit.Env, user, password string) string {
	enc := base64.StdEncoding.EncodeToString([]byte(user + ":" + password))
	env.AddSecret(enc)
	return "Authorization: Basic " + enc
}

const (
	ownPassword  = "s3cret-own-pair-7QxL"
	linkPassword = "s3cret-link-pair-Zp42"
)

// startProtected enrolls a broadcaster, starts a broadcast with the given `verifiers` member (nil: none) and attaches
// its upload with one joinable chunk, so that a listener that passes the check is served.
func startProtected(t *testing.T, env *testkit.Env, verifiers *[]wire.ListenerVerifier) (*testkit.Producer, string, *testkit.Conn) {
	t.Helper()
	p := env.NewProducer("alice", "ssh-secret")
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(p.Close)
	id, err := p.StartBroadcastWith("AUDIO_ONLY", nil, verifiers)
	if err != nil {
		t.Fatal(err)
	}
	up, ans, err := p.Upload(id, "audio/aac")
	if err != nil || up == nil {
		t.Fatalf("upload: %v %+v", err, ans)
	}
	t.Cleanup(func() { _ = up.Close() })
	if err := up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(1, 32)); err != nil {
		t.Fatal(err)
	}
	waitBytesIn(t, env, id, 32)
	return p, id, up
}

func listenWith(t *testing.T, env *testkit.Env, id string, headers ...string) *testkit.Listening {
	t.Helper()
	req := "GET " + wire.ListenPath(id) + " HTTP/1.1\r\nHost: x\r\n"
	for _, h := range headers {
		req += h + "\r\n"
	}
	l, err := testkit.DialRaw(env.Addr, env.Fingerprint, req+"\r\n", "GET")
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(l.Close)
	return l
}

func statusWith(t *testing.T, env *testkit.Env, id string, headers ...string) (int, http.Header) {
	t.Helper()
	l := listenWith(t, env, id, headers...)
	return l.Resp.StatusCode, l.Resp.Header.Clone()
}

// wantChallenge: 401 with exactly one WWW-Authenticate, `Basic realm="fms-broadcast"`, and nothing that tells one
// refusal from another.
func wantChallenge(t *testing.T, what string, code int, h http.Header) {
	t.Helper()
	if code != 401 {
		t.Fatalf("%s: %d, want 401", what, code)
	}
	if v := h.Values("Www-Authenticate"); len(v) != 1 || v[0] != `Basic realm="fms-broadcast"` {
		t.Fatalf("%s: WWW-Authenticate %q", what, v)
	}
	if h.Get("Retry-After") != "" || h.Get("Content-Type") != "" || h.Get("Content-Length") != "0" {
		t.Fatalf("%s: a 401 says nothing else: %v", what, h)
	}
}

func TestAProtectedBroadcastServesAValidPairAndChallengesEveryoneElse(t *testing.T) {
	env, clk := authRelay(t)
	own, link := authPair(env, "own", ownPassword), authPair(env, "link", linkPassword)
	_, id, up := startProtected(t, env, verifierList(own, link))
	step := func() { clk.Add(1100 * time.Millisecond) } // each credentialed attempt gets its own second here

	// no Authorization: the challenge. It hashes nothing and spends nothing.
	code, h := statusWith(t, env, id)
	wantChallenge(t, "no Authorization", code, h)
	for _, line := range []string{"Authorization: Bearer abc", "Authorization: Digest username=\"own\"", "Authorization: Basic", "Authorization: Basic !!!",
		"Authorization: Basic " + base64.StdEncoding.EncodeToString([]byte("no-colon"))} {
		code, h = statusWith(t, env, id, line)
		wantChallenge(t, line, code, h)
	}
	if env.Relay.Checks() != 0 {
		t.Fatalf("nothing usable was sent, nothing was hashed: %d", env.Relay.Checks())
	}

	// credentials that do not match: the same 401, one check each
	for name, line := range map[string]string{
		"an unknown user":     basicLine(env, "nobody", ownPassword),
		"a wrong password":    basicLine(env, "own", "not-the-password"),
		"the other pair's":    basicLine(env, "own", linkPassword),
		"an empty password":   basicLine(env, "own", ""),
		"another user's case": basicLine(env, "OWN", ownPassword),
	} {
		step()
		code, h = statusWith(t, env, id, line)
		wantChallenge(t, name, code, h)
	}
	if env.Relay.Checks() != 5 {
		t.Fatalf("each refused pair costs exactly one check: %d", env.Relay.Checks())
	}
	if env.Relay.Listeners(id) != 0 {
		t.Fatal("a refused request costs no listener slot")
	}

	// both right pairs are served, and the stream is the broadcast's own
	for _, line := range []string{basicLine(env, "own", ownPassword), basicLine(env, "link", linkPassword)} {
		step()
		l := listenWith(t, env, id, line)
		if l.Resp.StatusCode != 200 || l.Resp.Header.Get("Content-Type") != "audio/aac" || l.Resp.Header.Get("Www-Authenticate") != "" {
			t.Fatalf("a right pair: %d %v", l.Resp.StatusCode, l.Resp.Header)
		}
		if err := up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(2, 32)); err != nil {
			t.Fatal(err)
		}
		if seq, err := l.ReadSeq(32, 3*time.Second); err != nil || (seq != 1 && seq != 2) {
			t.Fatalf("the served stream: %d %v", seq, err)
		}
	}

	// the log carries a count of nothing that could be tested against a guess
	for _, ln := range env.Log.Lines() {
		for _, bad := range []string{"Authorization", "Basic ", "WWW-Authenticate", own.Salt, own.Hash, link.Hash, ownPassword, linkPassword} {
			if strings.Contains(ln, bad) {
				t.Fatalf("a log line carries %q: %s", bad, ln)
			}
		}
	}
	denied := 0
	for _, ln := range env.Log.Lines() {
		if strings.Contains(ln, "listen-denied") {
			denied++
		}
	}
	if denied != 5 {
		t.Fatalf("one listen-denied line per refused pair, none for a plain challenge: %d", denied)
	}
}

func TestAnEmptyListChecksNobodyAndSoDoesAMissingMember(t *testing.T) {
	env, _ := authRelay(t)
	_, idEmpty, _ := startProtected(t, env, verifierList()) // "verifiers": []
	_, idAbsent, _ := startProtected(t, env, nil)           // no member at all
	for name, id := range map[string]string{"an empty list": idEmpty, "no member": idAbsent} {
		for _, headers := range [][]string{nil, {basicLine(env, "anyone", "anything")}, {"Authorization: Bearer x"}, {"Authorization: Basic !!!"}} {
			l := listenWith(t, env, id, headers...)
			if l.Resp.StatusCode != 200 || l.Resp.Header.Get("Www-Authenticate") != "" {
				t.Fatalf("%s with %v: %d %v", name, headers, l.Resp.StatusCode, l.Resp.Header)
			}
		}
	}
	if env.Relay.Checks() != 0 {
		t.Fatalf("a broadcast with no verifier hashes nothing: %d", env.Relay.Checks())
	}
}

// waitStatus polls a listen URL (a broadcast-update is not answered) until it answers want; every poll
// moves the relay's clock a second on, so the rate limit is never what a poll meets.
func waitStatus(t *testing.T, env *testkit.Env, clk *fakeClock, id string, want int, headers ...string) {
	t.Helper()
	eventually(t, "the listen URL to answer", func() bool {
		clk.Add(1100 * time.Millisecond)
		code, _ := statusWith(t, env, id, headers...)
		return code == want
	})
}

// sync waits until the server has applied everything the broadcaster's control stream sent before it.
func syncControl(t *testing.T, p *testkit.Producer) {
	t.Helper()
	if _, err := p.Control().ListDirectory(); err != nil {
		t.Fatal(err)
	}
}

func TestAnUpdateReplacesTheListWholesaleAndOneWithoutTheMemberLeavesIt(t *testing.T) {
	env, clk := authRelay(t)
	own, link, guest := authPair(env, "own", ownPassword), authPair(env, "link", linkPassword), authPair(env, "guest", "s3cret-guest-pair-9Kd")
	p, id, _ := startProtected(t, env, verifierList(own, link))
	ownLine, linkLine, guestLine := basicLine(env, "own", ownPassword), basicLine(env, "link", linkPassword), basicLine(env, "guest", "s3cret-guest-pair-9Kd")

	clk.Add(1100 * time.Millisecond)
	if code, _ := statusWith(t, env, id, ownLine); code != 200 {
		t.Fatalf("before the update: %d", code)
	}

	// an update with no member changes the record and leaves the list as it was
	if err := p.UpdateBroadcast(id, "AUDIO_ONLY", "A new title", nil); err != nil {
		t.Fatal(err)
	}
	syncControl(t, p)
	for _, line := range []string{ownLine, linkLine} {
		clk.Add(1100 * time.Millisecond)
		if code, _ := statusWith(t, env, id, line); code != 200 {
			t.Fatalf("an update without the member must keep the list: %d", code)
		}
	}
	clk.Add(1100 * time.Millisecond)
	if code, _ := statusWith(t, env, id, guestLine); code != 401 {
		t.Fatalf("the guest was never listed: %d", code)
	}

	// an update with a new list replaces it wholesale: the old pair is refused, the new accepted. This is how
	// one pair is revoked alone.
	if err := p.UpdateBroadcast(id, "AUDIO_ONLY", "A new title", verifierList(own, guest)); err != nil {
		t.Fatal(err)
	}
	syncControl(t, p)
	waitStatus(t, env, clk, id, 401, linkLine)
	for name, c := range map[string]struct {
		line string
		want int
	}{"own, kept": {ownLine, 200}, "guest, added": {guestLine, 200}, "link, revoked": {linkLine, 401}} {
		clk.Add(1100 * time.Millisecond)
		if code, _ := statusWith(t, env, id, c.line); code != c.want {
			t.Fatalf("%s: %d, want %d", name, code, c.want)
		}
	}

	// an update with [] opens the broadcast: nobody is checked, with or without a header
	if err := p.UpdateBroadcast(id, "AUDIO_ONLY", "A new title", verifierList()); err != nil {
		t.Fatal(err)
	}
	syncControl(t, p)
	checks := env.Relay.Checks()
	if code, _ := statusWith(t, env, id); code != 200 {
		t.Fatalf("an empty list opens the broadcast: %d", code)
	}
	if code, _ := statusWith(t, env, id, linkLine); code != 200 {
		t.Fatalf("an empty list admits a revoked pair as well: %d", code)
	}
	if env.Relay.Checks() != checks {
		t.Fatal("an open broadcast hashes nothing")
	}

	// and a later update without the member does not bring a pair back
	if err := p.UpdateBroadcast(id, "AUDIO_ONLY", "Again", nil); err != nil {
		t.Fatal(err)
	}
	syncControl(t, p)
	if code, _ := statusWith(t, env, id); code != 200 {
		t.Fatalf("%d", code)
	}

	// and a list again closes it again
	if err := p.UpdateBroadcast(id, "AUDIO_ONLY", "Again", verifierList(link)); err != nil {
		t.Fatal(err)
	}
	syncControl(t, p)
	waitStatus(t, env, clk, id, 401)
	clk.Add(1100 * time.Millisecond)
	if code, _ := statusWith(t, env, id, linkLine); code != 200 {
		t.Fatalf("the new pair after a change back: %d", code)
	}
}

func TestASecondCheckInsideASecondIsAnswered401WithoutHashing(t *testing.T) {
	env, clk := authRelay(t)
	own := authPair(env, "own", ownPassword)
	_, id, _ := startProtected(t, env, verifierList(own))
	wrong, right := basicLine(env, "own", "not-the-password"), basicLine(env, "own", ownPassword)

	clk.Add(1100 * time.Millisecond)
	code, h := statusWith(t, env, id, wrong)
	wantChallenge(t, "the first check", code, h)
	if env.Relay.Checks() != 1 {
		t.Fatalf("%d", env.Relay.Checks())
	}
	// the right pair a moment later is refused, exactly as a wrong one is - and nothing is hashed for it
	clk.Add(300 * time.Millisecond)
	code, h = statusWith(t, env, id, right)
	wantChallenge(t, "the second check inside the second", code, h)
	if env.Relay.Checks() != 1 {
		t.Fatalf("the refused check hashed: %d", env.Relay.Checks())
	}
	// the challenge itself is free even then: a client asks without credentials, is told, and asks again
	code, h = statusWith(t, env, id)
	wantChallenge(t, "a request with no credentials", code, h)
	// and once the second is over the right pair is served
	clk.Add(800 * time.Millisecond)
	if code, _ := statusWith(t, env, id, right); code != 200 {
		t.Fatalf("after the second: %d", code)
	}
	if env.Relay.Checks() != 2 {
		t.Fatalf("%d", env.Relay.Checks())
	}
}

func TestTheChallengeComesBeforeAnythingElseTheRelayCouldTell(t *testing.T) {
	env, clk := authRelay(t)
	own := authPair(env, "own", ownPassword)
	// a protected broadcast whose upload is not attached yet: an unknown listener must not learn that from a 503
	p := env.NewProducer("alice", "ssh-secret")
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(p.Close)
	id, err := p.StartBroadcastWith("AUDIO_ONLY", nil, verifierList(own))
	if err != nil {
		t.Fatal(err)
	}
	code, h := statusWith(t, env, id)
	wantChallenge(t, "no upload yet, no credentials", code, h)
	clk.Add(1100 * time.Millisecond)
	code, h = statusWith(t, env, id, basicLine(env, "own", "not-the-password"))
	wantChallenge(t, "no upload yet, a wrong pair", code, h)
	// the right pair is told the truth: the producer has not uploaded yet
	clk.Add(1100 * time.Millisecond)
	code, h = statusWith(t, env, id, basicLine(env, "own", ownPassword))
	if code != 503 || h.Get("Retry-After") != "1" {
		t.Fatalf("a right pair meets the ordinary 503: %d %v", code, h)
	}
}

func TestAnUnknownBroadcastIsStillA404EvenWithCredentials(t *testing.T) {
	env, _ := authRelay(t)
	code, h := statusWith(t, env, env.NewID(), basicLine(env, "own", ownPassword))
	if code != 404 || h.Get("Www-Authenticate") != "" {
		t.Fatalf("%d %v", code, h)
	}
}

func TestHEADOnAProtectedListenURLIsStill405(t *testing.T) {
	env, _ := authRelay(t)
	own := authPair(env, "own", ownPassword)
	_, id, _ := startProtected(t, env, verifierList(own))
	if code, h := statusOf(t, env, "HEAD "+wire.ListenPath(id)+" HTTP/1.1\r\nHost: x\r\n\r\n", "HEAD"); code != 405 || h["Allow"] != "GET" {
		t.Fatalf("%d %v", code, h)
	}
}
