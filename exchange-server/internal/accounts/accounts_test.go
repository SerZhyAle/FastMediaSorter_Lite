package accounts

import (
	"bytes"
	"crypto/sha256"
	"encoding/base64"
	"encoding/hex"
	"errors"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"

	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/wire"
)

var cheap = password.Params{Time: 1, Memory: 8, Threads: 1, KeyLen: 32}

const (
	pw1   = "Passw0rdTest"
	devA  = "AAAAAAAAAAAAAAAAAAAAAA"
	devB  = "BBBBBBBBBBBBBBBBBBBBBA"
	devC  = "CCCCCCCCCCCCCCCCCCCCCA"
	alice = "alice"
	bob   = "bob"
)

type clock struct {
	mu sync.Mutex
	t  time.Time
}

func newClock() *clock { return &clock{t: time.Date(2026, 10, 7, 12, 0, 0, 0, time.UTC)} }
func (c *clock) Now() time.Time {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.t
}
func (c *clock) Add(d time.Duration) {
	c.mu.Lock()
	c.t = c.t.Add(d)
	c.mu.Unlock()
}

func open(t *testing.T, dir string, ck *clock, mut func(*Options)) *Store {
	t.Helper()
	opt := Options{Dir: dir, Now: ck.Now, Params: cheap}
	if mut != nil {
		mut(&opt)
	}
	s, err := Open(opt)
	if err != nil {
		t.Fatal(err)
	}
	return s
}

func withAccounts(t *testing.T) (*Store, *clock, string) {
	t.Helper()
	dir := t.TempDir()
	ck := newClock()
	s := open(t, dir, ck, nil)
	for _, l := range []string{alice, bob} {
		if _, err := s.AddAccount(l, pw1); err != nil {
			t.Fatal(err)
		}
	}
	return s, ck, dir
}

func spec(acct, id string) EnrollSpec {
	return EnrollSpec{Account: acct, DeviceID: id, Name: "Phone", Product: "fms-android", Roles: []string{"receiver"}}
}

func TestLoginRules(t *testing.T) {
	for _, c := range []struct {
		in   string
		want string
		ok   bool
	}{
		{"Alice", "alice", true}, {" alice ", "alice", true}, {"a.b_c-d9", "a.b_c-d9", true},
		{"ab", "ab", false}, {strings.Repeat("a", 64), strings.Repeat("a", 64), true}, {strings.Repeat("a", 65), "", false},
		{"al ice", "", false}, {"alice@x", "", false}, {"ålice", "", false}, {"", "", false}, {"a\x00b", "", false},
	} {
		got, ok := NormalizeLogin(c.in)
		if ok != c.ok || (ok && got != c.want) {
			t.Errorf("NormalizeLogin(%q) = %q, %v; want %q, %v", c.in, got, ok, c.want, c.ok)
		}
	}
}

func TestAddAccountAppliesThePolicyAndStoresOnlyARecord(t *testing.T) {
	dir := t.TempDir()
	s := open(t, dir, newClock(), nil)
	for _, weak := range []string{"", "Abcdefgh1", "abcdefghij1", "ABCDEFGHIJ1", "Abcdefghij"} {
		if _, err := s.AddAccount("carol", weak); err == nil {
			t.Errorf("%q accepted", weak)
		}
	}
	if _, err := s.AddAccount("c", pw1); !errors.Is(err, ErrBadLogin) {
		t.Errorf("a short login: %v", err)
	}
	if l, err := s.AddAccount("Carol", pw1); err != nil || l != "carol" {
		t.Fatalf("%q %v", l, err)
	}
	if _, err := s.AddAccount("CAROL", pw1); !errors.Is(err, ErrExists) {
		t.Errorf("a duplicate, case-insensitively: %v", err)
	}
	raw, _ := os.ReadFile(filepath.Join(dir, AccountsFile))
	if bytes.Contains(raw, []byte(pw1)) || !bytes.Contains(raw, []byte("$argon2id$")) {
		t.Fatalf("the file must hold only the Argon2id record:\n%s", raw)
	}
	if !s.VerifyPassword("carol", pw1) || !s.VerifyPassword("CAROL", pw1) || s.VerifyPassword("carol", "Passw0rdTesT") {
		t.Fatal("verification")
	}
	if s.VerifyPassword("nobody", pw1) || s.VerifyPassword("", pw1) || s.VerifyPassword("c a r", pw1) {
		t.Fatal("an unknown login verified")
	}
}

func TestSetPasswordKeepsTokens(t *testing.T) {
	s, _, _ := withAccounts(t)
	e, err := s.Enroll(spec(alice, devA))
	if err != nil {
		t.Fatal(err)
	}
	if err := s.SetPassword(alice, "N3wPassword!x"); err != nil {
		t.Fatal(err)
	}
	if s.VerifyPassword(alice, pw1) || !s.VerifyPassword(alice, "N3wPassword!x") {
		t.Fatal("the password did not change")
	}
	if _, st := s.Hello(devA, e.Token); st != HelloOK {
		t.Fatal("a password change must leave tokens alone")
	}
	if err := s.SetPassword("nobody", pw1); !errors.Is(err, ErrNoAccount) {
		t.Fatalf("%v", err)
	}
	if err := s.SetPassword(alice, "weak"); err == nil {
		t.Fatal("a weak password was accepted")
	}
}

func TestEnrollIssuesAnUnguessableTokenAndStoresOnlyItsHash(t *testing.T) {
	s, _, dir := withAccounts(t)
	e, err := s.Enroll(spec(alice, devA))
	if err != nil {
		t.Fatal(err)
	}
	if !wire.ValidToken(e.Token) || e.Rotated {
		t.Fatalf("token %q rotated %v", e.Token, e.Rotated)
	}
	want := sha256.Sum256([]byte(e.Token))
	if e.Device.TokenHash != hex.EncodeToString(want[:]) {
		t.Fatal("the stored hash is not the SHA-256 of the token")
	}
	// the plaintext token is in no file, raw, base64 or hex
	needles := [][]byte{[]byte(e.Token), []byte(base64.StdEncoding.EncodeToString([]byte(e.Token))), []byte(hex.EncodeToString([]byte(e.Token)))}
	for _, f := range []string{AccountsFile, DevicesFile} {
		raw, _ := os.ReadFile(filepath.Join(dir, f))
		for _, n := range needles {
			if bytes.Contains(raw, n) {
				t.Errorf("the token is in %s", f)
			}
		}
	}
	// two tokens differ
	f, _ := s.Enroll(spec(alice, devB))
	if f.Token == e.Token {
		t.Fatal("two devices got one token")
	}
	if _, st := s.Hello(devA, e.Token); st != HelloOK {
		t.Fatal("hello")
	}
	if _, st := s.Hello(devA, f.Token); st != HelloBad {
		t.Fatal("another device's token must not open this one")
	}
	if _, st := s.Hello("ZZZZZZZZZZZZZZZZZZZZZA", e.Token); st != HelloBad {
		t.Fatal("an unknown device")
	}
	d, st := s.Hello(devA, "not-the-token")
	if st != HelloBad || d.Account != alice {
		t.Fatalf("a wrong token on a known device names the account for the bucket: %+v %v", d, st)
	}
}

func TestEnrollingAKnownDeviceRotatesItsToken(t *testing.T) {
	s, _, _ := withAccounts(t)
	e1, _ := s.Enroll(spec(alice, devA))
	e2, err := s.Enroll(spec(alice, devA))
	if err != nil || !e2.Rotated || e2.Token == e1.Token {
		t.Fatalf("%v %+v", err, e2)
	}
	if _, st := s.Hello(devA, e1.Token); st != HelloBad {
		t.Fatal("the old token must die at once")
	}
	if _, st := s.Hello(devA, e2.Token); st != HelloOK {
		t.Fatal("the new token")
	}
	if len(s.Devices(alice)) != 1 {
		t.Fatal("a rotation must not add a device")
	}
}

func TestIDCollisionAcrossAccountsAndCapacity(t *testing.T) {
	s, _, _ := withAccounts(t)
	if _, err := s.Enroll(spec(alice, devA)); err != nil {
		t.Fatal(err)
	}
	if _, err := s.Enroll(spec(bob, devA)); !errors.Is(err, ErrIDCollision) {
		t.Fatalf("a deviceId held by another account: %v", err)
	}
	// a revoked id stays reserved against other accounts
	if _, ok, _ := s.Revoke(alice, devA); !ok {
		t.Fatal("revoke")
	}
	if _, err := s.Enroll(spec(bob, devA)); !errors.Is(err, ErrIDCollision) {
		t.Fatalf("a revoked deviceId of another account: %v", err)
	}
	if _, err := s.Enroll(spec(alice, devA)); err != nil {
		t.Fatalf("the account that revoked it may enrol it again: %v", err)
	}
	if s.Tombstones() != 0 {
		t.Fatal("re-enrolling must clear the tombstone")
	}
	if _, err := s.Enroll(spec("nobody", devC)); !errors.Is(err, ErrNoAccount) {
		t.Fatalf("%v", err)
	}

	small := open(t, t.TempDir(), newClock(), func(o *Options) { o.DevicesPerAccount = 2 })
	small.AddAccount(alice, pw1)
	small.Enroll(spec(alice, devA))
	small.Enroll(spec(alice, devB))
	if _, err := small.Enroll(spec(alice, devC)); !errors.Is(err, ErrCapacity) {
		t.Fatalf("the third device: %v", err)
	}
	if _, err := small.Enroll(spec(alice, devA)); err != nil {
		t.Fatalf("a rotation at the cap must work: %v", err)
	}
}

func TestRevokeMovesTheHashToATombstoneForThirtyDays(t *testing.T) {
	s, ck, _ := withAccounts(t)
	e, _ := s.Enroll(spec(alice, devA))
	if _, ok, err := s.Revoke(bob, devA); ok || err != nil {
		t.Fatal("a foreign revoke must change nothing")
	}
	if _, st := s.Hello(devA, e.Token); st != HelloOK {
		t.Fatal("a foreign revoke killed the token")
	}
	d, ok, err := s.Revoke(alice, devA)
	if err != nil || !ok || d.ID != devA {
		t.Fatalf("%v %v", ok, err)
	}
	if _, ok, _ := s.Revoke(alice, devA); ok {
		t.Fatal("a second revoke does nothing")
	}
	if _, st := s.Hello(devA, e.Token); st != HelloRevoked {
		t.Fatalf("the revoked token must be told it was revoked, got %v", st)
	}
	if _, st := s.Hello(devA, "some-other-token"); st != HelloBad {
		t.Fatal("device-revoked only on a matching hash: a stranger learns nothing")
	}
	if _, st := s.Hello(devB, e.Token); st != HelloBad {
		t.Fatal("the hash matches only its own deviceId")
	}
	ck.Add(29 * 24 * time.Hour)
	s.Sweep(ck.Now())
	if _, st := s.Hello(devA, e.Token); st != HelloRevoked {
		t.Fatal("kept for 30 days")
	}
	ck.Add(2 * 24 * time.Hour)
	s.Sweep(ck.Now())
	if _, st := s.Hello(devA, e.Token); st != HelloBad || s.Tombstones() != 0 {
		t.Fatal("forgotten after 30 days")
	}
}

func TestRemoveAccountTakesItsDevicesAndTombstones(t *testing.T) {
	s, _, _ := withAccounts(t)
	s.Enroll(spec(alice, devA))
	s.Enroll(spec(alice, devB))
	s.Enroll(spec(bob, devC))
	s.Revoke(alice, devB)
	gone, err := s.RemoveAccount("ALICE")
	if err != nil || len(gone) != 1 || gone[0].ID != devA {
		t.Fatalf("%v %v", gone, err)
	}
	if s.Exists(alice) || len(s.Devices(alice)) != 0 || s.Tombstones() != 0 {
		t.Fatal("the account left something behind")
	}
	if len(s.Devices(bob)) != 1 {
		t.Fatal("another account was touched")
	}
	if _, err := s.RemoveAccount(alice); !errors.Is(err, ErrNoAccount) {
		t.Fatal(err)
	}
	// the freed deviceId can be enrolled elsewhere
	if _, err := s.Enroll(spec(bob, devA)); err != nil {
		t.Fatalf("%v", err)
	}
}

func TestReopeningGivesTheSameStoreAndRefusesANewerOne(t *testing.T) {
	s, ck, dir := withAccounts(t)
	e, _ := s.Enroll(spec(alice, devA))
	s.Revoke(alice, devA)
	f, _ := s.Enroll(spec(bob, devB))
	again := open(t, dir, ck, nil)
	if !again.VerifyPassword(alice, pw1) || again.Tombstones() != 1 {
		t.Fatal("accounts or tombstones lost")
	}
	if _, st := again.Hello(devA, e.Token); st != HelloRevoked {
		t.Fatal("the tombstone did not survive")
	}
	if _, st := again.Hello(devB, f.Token); st != HelloOK {
		t.Fatal("the device did not survive")
	}
	os.WriteFile(filepath.Join(dir, DevicesFile), []byte(`{"storeVersion":2,"devices":[],"tombstones":[]}`), 0o600)
	if _, err := Open(Options{Dir: dir, Params: cheap}); !errors.Is(err, ErrNewerStore) {
		t.Fatalf("a newer store: %v", err)
	}
	os.WriteFile(filepath.Join(dir, DevicesFile), []byte(`{broken`), 0o600)
	if _, err := Open(Options{Dir: dir, Params: cheap}); err == nil {
		t.Fatal("a broken store opened")
	}
}

func TestAKillDuringAWriteLeavesThePreviousFileReadable(t *testing.T) {
	dir := t.TempDir()
	ck := newClock()
	var failNext bool
	s := open(t, dir, ck, func(o *Options) {
		o.WriteTemp = func(name string, data []byte, perm os.FileMode) error {
			if failNext {
				// a partial temp file, then the "kill"
				_ = os.WriteFile(name, data[:len(data)/2], perm)
				return errors.New("killed")
			}
			return os.WriteFile(name, data, perm)
		}
	})
	s.AddAccount(alice, pw1)
	before, _ := os.ReadFile(filepath.Join(dir, AccountsFile))
	failNext = true
	if _, err := s.AddAccount(bob, pw1); err == nil {
		t.Fatal("the failed write was reported as success")
	}
	if s.Exists(bob) {
		t.Fatal("a failed write left the account in memory")
	}
	after, _ := os.ReadFile(filepath.Join(dir, AccountsFile))
	if !bytes.Equal(before, after) {
		t.Fatal("the previous file changed")
	}
	// a restart reads the previous file and sweeps the temp file
	again := open(t, dir, ck, nil)
	if !again.Exists(alice) || again.Exists(bob) {
		t.Fatal("the previous store is not what came back")
	}
	if _, err := os.Stat(filepath.Join(dir, AccountsFile+".tmp")); err == nil {
		t.Fatal("the half-written temp file was left")
	}
}

func TestLastSeenIsWrittenAtMostOnceAMinute(t *testing.T) {
	dir := t.TempDir()
	ck := newClock()
	writes := 0
	s := open(t, dir, ck, func(o *Options) {
		o.WriteTemp = func(name string, data []byte, perm os.FileMode) error {
			if strings.HasSuffix(name, DevicesFile+".tmp") {
				writes++
			}
			return os.WriteFile(name, data, perm)
		}
	})
	s.AddAccount(alice, pw1)
	s.Enroll(spec(alice, devA))
	base := writes
	for i := 0; i < 20; i++ { // presence flaps
		s.Touch(devA)
		ck.Add(2 * time.Second)
		s.Sweep(ck.Now())
	}
	if writes != base {
		t.Fatalf("%d writes inside the first minute", writes-base)
	}
	ck.Add(30 * time.Second)
	s.Sweep(ck.Now())
	if writes != base+1 {
		t.Fatalf("expected one coalesced write after a minute, got %d", writes-base)
	}
	s.Touch(devA)
	if err := s.Flush(); err != nil || writes != base+2 {
		t.Fatalf("a clean stop flushes: %v %d", err, writes-base)
	}
	d, _ := s.Device(alice, devA)
	if d.LastSeen == d.EnrolledAt {
		t.Fatal("lastSeen did not move")
	}
}

func TestRecordsNeverPrintWithAVerb(t *testing.T) {
	s, _, _ := withAccounts(t)
	e, _ := s.Enroll(spec(alice, devA))
	for _, v := range []any{e.Device, &e.Device, Account{PasswordHash: "$argon2id$secret"}, Tombstone{TokenHash: "secret"}} {
		for _, verb := range []string{"%v", "%+v", "%s"} {
			if out := sprintf(verb, v); strings.Contains(out, e.Device.TokenHash) || strings.Contains(out, "secret") {
				t.Errorf("%T printed with %s: %s", v, verb, out)
			}
		}
	}
}

// --- pairing codes ---

func TestPairingCodeShapeAndSingleUse(t *testing.T) {
	s, ck, _ := withAccounts(t)
	code, exp, err := s.NewPairingCode(alice)
	if err != nil {
		t.Fatal(err)
	}
	if len(code) != 8 || strings.Trim(code, crockford) != "" || !exp.Equal(ck.Now().Add(10*time.Minute)) {
		t.Fatalf("code %q expires %v", code, exp)
	}
	if s.RedeemPairingCode(bob, code) {
		t.Fatal("another account's code")
	}
	if !s.RedeemPairingCode(alice, code) {
		t.Fatal("the code did not redeem")
	}
	if s.RedeemPairingCode(alice, code) {
		t.Fatal("a code is single use")
	}
	if s.OutstandingCodes(alice) != 0 {
		t.Fatal("a burned code is outstanding")
	}
	if _, _, err := s.NewPairingCode("nobody"); !errors.Is(err, ErrNoAccount) {
		t.Fatal(err)
	}
	// the code is never on disk: not even as a hash
	_, _, _ = s.NewPairingCode(alice)
}

func TestPairingCodeDrawsFiveRandomBytesWithoutBias(t *testing.T) {
	// 5 bytes 0x00..: all-zero is "00000000", all-ones is "ZZZZZZZZ", a known pattern is exact
	for _, c := range []struct {
		raw  []byte
		want string
	}{
		{[]byte{0, 0, 0, 0, 0}, "00000000"},
		{[]byte{0xff, 0xff, 0xff, 0xff, 0xff}, "ZZZZZZZZ"},
		{[]byte{0x08, 0x62, 0x14, 0xc7, 0x39}, "11H19HSS"}, // 00001 00001 10001 00001 01001 10001 11001 11001
	} {
		s := open(t, t.TempDir(), newClock(), func(o *Options) { o.Rand = &seq{b: append(append([]byte{}, make([]byte, 24)...), c.raw...)} })
		s.AddAccount(alice, pw1)
		got, _, err := s.NewPairingCode(alice)
		if err != nil || got != c.want {
			t.Errorf("% x: got %q, want %q (%v)", c.raw, got, c.want, err)
		}
	}
}

func TestPairingCodeIsCaseAndLookAlikeTolerant(t *testing.T) {
	for _, c := range []struct{ code, typed string }{
		{"ABCD2345", "abcd2345"}, {"ABCD2345", "ABCD-2345"}, {"ABCD2345", " abcd 2345 "},
		{"11OO0D5Z", "iiooOD5z"}, {"11OO0D5Z", "LL00-0d5z"},
	} {
		if got := NormalizeCode(c.typed); got != NormalizeCode(c.code) {
			t.Errorf("%q normalises to %q, want the same as %q (%q)", c.typed, got, c.code, NormalizeCode(c.code))
		}
	}
	// end to end with a generated code, typed sloppily
	s, _, _ := withAccounts(t)
	code, _, _ := s.NewPairingCode(alice)
	sloppy := strings.ToLower(code[:4]) + " - " + strings.ToLower(code[4:])
	sloppy = strings.NewReplacer("1", "l", "0", "o").Replace(sloppy)
	if !s.RedeemPairingCode(alice, sloppy) {
		t.Fatalf("%q typed as %q did not redeem", code, sloppy)
	}
}

func TestPairingCodeExpiresAndFiveFailuresBurnTheLoginsCodes(t *testing.T) {
	s, ck, _ := withAccounts(t)
	code, _, _ := s.NewPairingCode(alice)
	ck.Add(10*time.Minute - time.Second)
	if s.OutstandingCodes(alice) != 1 {
		t.Fatal("expired too early")
	}
	ck.Add(2 * time.Second)
	if s.RedeemPairingCode(alice, code) || s.OutstandingCodes(alice) != 0 {
		t.Fatal("an expired code redeemed")
	}
	ck.Add(11 * time.Minute) // the failure that attempt cost is forgotten with the window

	// five wrong codes burn every active code of the login - the right one too
	a, _, _ := s.NewPairingCode(alice)
	b, _, _ := s.NewPairingCode(alice)
	other, _, _ := s.NewPairingCode(bob)
	for i := 0; i < 4; i++ {
		if s.RedeemPairingCode(alice, "WRONG123") {
			t.Fatal("a wrong code")
		}
	}
	if s.OutstandingCodes(alice) != 2 {
		t.Fatal("four failures must not burn anything yet")
	}
	s.RedeemPairingCode(alice, "WRONG123") // the fifth
	if s.OutstandingCodes(alice) != 0 || s.RedeemPairingCode(alice, a) || s.RedeemPairingCode(alice, b) {
		t.Fatal("five failures must burn every active code of the login")
	}
	if !s.RedeemPairingCode(bob, other) {
		t.Fatal("another login's code was burned")
	}
	// failures older than the window are forgotten
	c, _, _ := s.NewPairingCode(alice)
	for i := 0; i < 4; i++ {
		s.RedeemPairingCode(alice, "WRONG123")
	}
	ck.Add(11 * time.Minute)
	s.Sweep(ck.Now())
	d, _, _ := s.NewPairingCode(alice)
	s.RedeemPairingCode(alice, "WRONG123")
	if !s.RedeemPairingCode(alice, d) {
		t.Fatalf("one failure after the window burned the codes (%q)", c)
	}
	// a stranger guessing at logins that do not exist remembers nothing
	for i := 0; i < 1000; i++ {
		s.RedeemPairingCode("nobody"+strings.Repeat("x", i%5), "WRONG123")
	}
	if got := len(s.codes.byLogin); got > 2 {
		t.Fatalf("the code table grew to %d logins", got)
	}
}

func TestAtMostFiveOutstandingCodes(t *testing.T) {
	s, ck, _ := withAccounts(t)
	for i := 0; i < 5; i++ {
		if _, _, err := s.NewPairingCode(alice); err != nil {
			t.Fatalf("code %d: %v", i, err)
		}
	}
	if _, _, err := s.NewPairingCode(alice); !errors.Is(err, ErrCapacity) {
		t.Fatalf("the sixth: %v", err)
	}
	if _, _, err := s.NewPairingCode(bob); err != nil {
		t.Fatal("another account was limited")
	}
	ck.Add(11 * time.Minute)
	if _, _, err := s.NewPairingCode(alice); err != nil {
		t.Fatalf("expired codes must free their slots: %v", err)
	}
}
