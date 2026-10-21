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

func TestSetPasswordSignsOutEveryDevice(t *testing.T) {
	s, _, _ := withAccounts(t)
	e, err := s.Enroll(spec(alice, devA))
	if err != nil {
		t.Fatal(err)
	}
	f, err := s.Enroll(spec(alice, devB))
	if err != nil {
		t.Fatal(err)
	}
	ids, err := s.SetPassword(alice, "N3wPassword!x")
	if err != nil {
		t.Fatal(err)
	}
	if len(ids) != 2 || ids[0] != devA || ids[1] != devB {
		t.Fatalf("every device is reported as signed out: %v", ids)
	}
	if s.VerifyPassword(alice, pw1) || !s.VerifyPassword(alice, "N3wPassword!x") {
		t.Fatal("the password did not change")
	}
	// signed out, not removed (19 items AT and AV): the token is dead, the record stays
	if _, st := s.Hello(devA, e.Token); st != HelloSignedOut {
		t.Fatalf("a password reset signs out every device, got %v", st)
	}
	if _, st := s.Hello(devB, f.Token); st != HelloSignedOut {
		t.Fatalf("a password reset spares no device, got %v", st)
	}
	if len(s.Devices(alice)) != 2 {
		t.Fatal("a sign-out must keep the device records")
	}
	if s.Tombstones() != 0 {
		t.Fatal("a sign-out is not a revoke: nothing moves to a tombstone")
	}
	if _, err := s.SetPassword("nobody", pw1); !errors.Is(err, ErrNoAccount) {
		t.Fatalf("%v", err)
	}
	if _, err := s.SetPassword(alice, "weak"); err == nil {
		t.Fatal("a weak password was accepted")
	}
	// the device that signs in again with the new password is enrolled under the same id: record kept
	g, err := s.Enroll(spec(alice, devA))
	if err != nil || !g.Rotated {
		t.Fatalf("re-enrolling a signed-out device rotates it: %+v %v", g, err)
	}
	if _, st := s.Hello(devA, g.Token); st != HelloOK {
		t.Fatalf("the fresh token must work, got %v", st)
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
	if _, err := small.Enroll(spec(alice, devC)); !errors.Is(err, ErrLimit) {
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

// --- invitations, registration, password change, grants, limits and pace (ticket 224) ---

// register runs the store half of a registration the way the engine does: one-time code, then the
// account and its first device in one step.
func register(t *testing.T, s *Store, login, pw, kind, code, dev string) (Enrolled, error) {
	t.Helper()
	rec, err := s.HashPassword(pw)
	if err != nil {
		t.Fatal(err)
	}
	return s.RegisterAccount(RegisterSpec{Login: login, PasswordRecord: rec, Kind: kind, InvitationCode: code, Device: spec(login, dev)})
}

func TestOneTimeInvitationRegistersOnceAndNeverExpires(t *testing.T) {
	s, ck, dir := withAccounts(t)
	code, inv, err := s.CreateInvitation("guest", "")
	if err != nil {
		t.Fatal(err)
	}
	if len(code) != 22 {
		t.Fatalf("an invitation code is 16 random bytes in the 22-character id form, got %d (%s)", len(code), code)
	}
	if inv.Kind != KindGuest || inv.Used || inv.CodeHash != InvitationHash(code) {
		t.Fatalf("unexpected invitation: %+v", inv)
	}

	// only the hash is on disk (19 item AP)
	raw, err := os.ReadFile(filepath.Join(dir, InvitationsFile))
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(string(raw), code) || !strings.Contains(string(raw), inv.CodeHash) {
		t.Fatal("the invitations file must hold the hash and never the code")
	}

	ck.Add(366 * 24 * time.Hour) // no expiry: a year and a day later it still registers
	kind, err := s.OneTimeInvitation(code)
	if err != nil || kind != KindGuest {
		t.Fatalf("a year-old invitation: %q %v", kind, err)
	}
	e, err := register(t, s, "carol", "C4rolPassw0rd", kind, code, devC)
	if err != nil {
		t.Fatal(err)
	}
	if !s.IsGuest("carol") || e.Token == "" || e.Device.Account != "carol" {
		t.Fatalf("the account was not created as a guest with its first device: %+v", e.Device)
	}
	if !s.VerifyPassword("carol", "C4rolPassw0rd") {
		t.Fatal("the chosen password does not verify")
	}

	// used: the same answer as a code that never existed
	_, errUsed := register(t, s, "dave", "D4vePassw0rd", kind, code, devB)
	_, errWrong := register(t, s, "dave", "D4vePassw0rd", kind, "wrongcode1234567890123", devB)
	if !errors.Is(errUsed, ErrInvitationInvalid) || !errors.Is(errWrong, ErrInvitationInvalid) {
		t.Fatalf("a used and a wrong code are one answer: %v / %v", errUsed, errWrong)
	}
	if _, err := s.OneTimeInvitation(code); !errors.Is(err, ErrInvitationInvalid) {
		t.Fatalf("a used code looks up as invalid: %v", err)
	}
	if s.Exists("dave") {
		t.Fatal("a refused registration created an account")
	}

	// the record stays, as a used one, and names who used it
	list := s.ListInvitations()
	if len(list) != 1 || !list[0].Used || list[0].UsedBy != "carol" || list[0].UsedAt == "" {
		t.Fatalf("the used invitation is listed as used by carol: %+v", list)
	}
}

func TestAFailedRegistrationConsumesNothing(t *testing.T) {
	dir := t.TempDir()
	ck := newClock()
	pol := DefaultPolicy()
	pol.MaxAccounts = 2
	s := open(t, dir, ck, func(o *Options) { o.Policy = &pol })
	for _, l := range []string{alice, bob} {
		if _, err := s.AddAccount(l, pw1); err != nil {
			t.Fatal(err)
		}
	}
	code, _, _ := s.CreateInvitation("member", "")

	// the cap
	if _, err := register(t, s, "carol", "C4rolPassw0rd", KindMember, code, devC); !errors.Is(err, ErrLimit) {
		t.Fatalf("the cap refuses the registration: %v", err)
	}
	if _, err := s.OneTimeInvitation(code); err != nil {
		t.Fatalf("the refused registration must not use the code: %v", err)
	}

	// the operator is never refused by the cap; it only counts (19 item AS)
	if _, err := s.AddAccount("opsadded", pw1); err != nil {
		t.Fatalf("the operator adds past the cap: %v", err)
	}
	if got := s.AccountCount(); got != 3 {
		t.Fatalf("count %d", got)
	}
	pol.MaxAccounts = 10
	s.SetPolicy(pol)

	// a taken login
	if _, err := register(t, s, alice, "C4rolPassw0rd", KindMember, code, devC); !errors.Is(err, ErrExists) {
		t.Fatalf("a taken login: %v", err)
	}
	// a deviceId that belongs to another account
	if _, err := s.Enroll(spec(alice, devA)); err != nil {
		t.Fatal(err)
	}
	if _, err := register(t, s, "carol", "C4rolPassw0rd", KindMember, code, devA); !errors.Is(err, ErrIDCollision) {
		t.Fatalf("a foreign deviceId: %v", err)
	}
	if s.Exists("carol") {
		t.Fatal("a failed registration left an account behind")
	}
	if _, err := s.OneTimeInvitation(code); err != nil {
		t.Fatalf("no refusal may use the code: %v", err)
	}

	// and now it works, once
	if _, err := register(t, s, "carol", "C4rolPassw0rd", KindMember, code, devC); err != nil {
		t.Fatal(err)
	}
	if got := s.AccountCount(); got != 4 {
		t.Fatalf("count %d", got)
	}
}

func TestRegistrationIsAllOrNothingWhenAWriteFails(t *testing.T) {
	dir := t.TempDir()
	ck := newClock()
	failInvitations := false
	s := open(t, dir, ck, func(o *Options) {
		o.WriteTemp = func(name string, data []byte, perm os.FileMode) error {
			if failInvitations && strings.HasSuffix(name, InvitationsFile+".tmp") {
				failInvitations = false // once: the rollback rewrites the file
				return errors.New("disk full")
			}
			return os.WriteFile(name, data, perm)
		}
	})
	code, _, err := s.CreateInvitation("member", "")
	if err != nil {
		t.Fatal(err)
	}
	failInvitations = true
	if _, err := register(t, s, "carol", "C4rolPassw0rd", KindMember, code, devC); err == nil {
		t.Fatal("a failed write was reported as a registration")
	}
	if s.Exists("carol") || len(s.Devices("carol")) != 0 {
		t.Fatal("the account or its device survived the failed registration")
	}
	if _, err := s.OneTimeInvitation(code); err != nil {
		t.Fatalf("the code must be unused: %v", err)
	}
	again := open(t, dir, ck, nil)
	if again.Exists("carol") || len(again.AllDevices()) != 0 {
		t.Fatal("a restart found a half-registered account")
	}
	if _, err := again.OneTimeInvitation(code); err != nil {
		t.Fatalf("a restart found the code used: %v", err)
	}
}

func TestInvitationWithdrawalByPrefix(t *testing.T) {
	s, _, _ := withAccounts(t)
	code, inv, err := s.CreateInvitation("member", "newcomer")
	if err != nil {
		t.Fatal(err)
	}
	if inv.ExpectedLogin != "newcomer" {
		t.Fatalf("the login the operator expects is kept as a hint: %+v", inv)
	}
	if ok, _ := s.WithdrawInvitation(inv.CodeHash[:3]); ok {
		t.Fatal("a prefix of three characters must name nothing")
	}
	ok, err := s.WithdrawInvitation(inv.CodeHash[:8])
	if err != nil || !ok {
		t.Fatalf("withdraw by the eight characters the list shows: %v %v", ok, err)
	}
	if _, err := s.OneTimeInvitation(code); !errors.Is(err, ErrInvitationInvalid) {
		t.Fatalf("a withdrawn code is one more way to be invalid: %v", err)
	}
	if ok, _ := s.WithdrawInvitation(inv.CodeHash[:8]); ok {
		t.Fatal("a second withdrawal removes nothing")
	}
	if _, _, err := s.CreateInvitation("member", "no spaces!"); !errors.Is(err, ErrBadLogin) {
		t.Fatalf("an expected login is a login of 5.1: %v", err)
	}
	if _, _, err := s.CreateInvitation("admin", ""); !errors.Is(err, ErrBadKind) {
		t.Fatalf("a kind is member or guest: %v", err)
	}
}

func TestSharedInvitationPasswordOnlyWhileOnAndStopsAtOnce(t *testing.T) {
	s, _, dir := withAccounts(t)
	if on, _ := s.SharedInvitationStatus(); on {
		t.Fatal("the shared invitation password is off by default")
	}
	if _, err := s.VerifySharedInvitation("SharedSecret123!"); !errors.Is(err, ErrInvitationInvalid) {
		t.Fatalf("a shared password while it is off: %v", err)
	}
	const first, second = "SharedSecret123!", "OtherSecret456!x"
	if err := s.SetSharedInvitation(first, KindGuest); err != nil {
		t.Fatal(err)
	}
	on, kind := s.SharedInvitationStatus()
	if !on || kind != KindGuest {
		t.Fatalf("status %v %q", on, kind)
	}
	raw, _ := os.ReadFile(filepath.Join(dir, InvitationsFile))
	if strings.Contains(string(raw), first) {
		t.Fatal("the shared password is on disk in clear")
	}
	if k, err := s.VerifySharedInvitation(first); err != nil || k != KindGuest {
		t.Fatalf("the shared password: %q %v", k, err)
	}
	if _, err := s.VerifySharedInvitation("not-the-password"); !errors.Is(err, ErrInvitationInvalid) {
		t.Fatalf("a wrong shared password: %v", err)
	}

	// a one-time code keeps working whatever the shared password does
	code, _, _ := s.CreateInvitation("member", "")

	// changing it stops the old one at once and touches no account
	if err := s.SetSharedInvitation(second, KindMember); err != nil {
		t.Fatal(err)
	}
	if _, err := s.VerifySharedInvitation(first); !errors.Is(err, ErrInvitationInvalid) {
		t.Fatalf("the old shared password must stop at once: %v", err)
	}
	if k, err := s.VerifySharedInvitation(second); err != nil || k != KindMember {
		t.Fatalf("%q %v", k, err)
	}
	// off
	if err := s.SetSharedInvitation("", ""); err != nil {
		t.Fatal(err)
	}
	if _, err := s.VerifySharedInvitation(second); !errors.Is(err, ErrInvitationInvalid) {
		t.Fatalf("a switched-off shared password: %v", err)
	}
	if _, err := s.OneTimeInvitation(code); err != nil {
		t.Fatalf("a one-time code is independent of the shared password: %v", err)
	}
	// a weak shared password is not accepted
	if err := s.SetSharedInvitation("weak", KindMember); err == nil {
		t.Fatal("a weak shared password was accepted")
	}
	// a registration by the shared password has no code to consume
	if err := s.SetSharedInvitation(first, KindMember); err != nil {
		t.Fatal(err)
	}
	auth, err := s.AuthorizeSharedInvitation(first)
	if err != nil {
		t.Fatal(err)
	}
	registerShared := func(login, pw, dev string) (Enrolled, error) {
		rec, err := s.HashPassword(pw)
		if err != nil {
			t.Fatal(err)
		}
		return s.RegisterAccount(RegisterSpec{Login: login, PasswordRecord: rec, SharedInvitation: auth, Device: spec(login, dev)})
	}
	if _, err := registerShared("frank", "Fr4nkPassw0rd", devC); err != nil {
		t.Fatal(err)
	}
	if _, err := registerShared("gina", "G1naPassw0rd", devB); err != nil {
		t.Fatal("a shared password registers many accounts, not one")
	}
}

func TestChangePasswordKeepsTheCallerAndSignsOutTheRest(t *testing.T) {
	s, _, _ := withAccounts(t)
	eA, _ := s.Enroll(spec(alice, devA))
	eB, _ := s.Enroll(spec(alice, devB))
	eC, _ := s.Enroll(spec(bob, devC))

	rec, err := s.HashPassword("BrandNewPass123!")
	if err != nil {
		t.Fatal(err)
	}
	if _, _, err := s.ChangePassword(alice, rec, devC); !errors.Is(err, ErrNoDevice) {
		t.Fatalf("a device of another account is no caller: %v", err)
	}
	if _, _, err := s.ChangePassword(alice, rec, "ZZZZZZZZZZZZZZZZZZZZZA"); !errors.Is(err, ErrNoDevice) {
		t.Fatalf("an unknown caller: %v", err)
	}
	if !s.VerifyPassword(alice, pw1) {
		t.Fatal("a refused change altered the password")
	}

	newToken, out, err := s.ChangePassword(alice, rec, devA)
	if err != nil {
		t.Fatal(err)
	}
	if newToken == "" || newToken == eA.Token {
		t.Fatalf("the caller gets a fresh token: %q", newToken)
	}
	if len(out) != 1 || out[0] != devB {
		t.Fatalf("exactly the other devices of the account are signed out: %v", out)
	}
	if _, st := s.Hello(devA, newToken); st != HelloOK {
		t.Fatalf("the calling device stays signed in with its new token: %v", st)
	}
	if _, st := s.Hello(devA, eA.Token); st != HelloBad {
		t.Fatalf("its old token is dead: %v", st)
	}
	if _, st := s.Hello(devB, eB.Token); st != HelloSignedOut {
		t.Fatalf("the other device is told it was signed out: %v", st)
	}
	if len(s.Devices(alice)) != 2 {
		t.Fatal("the device record stays after a sign-out (19 item AV)")
	}
	if _, st := s.Hello(devC, eC.Token); st != HelloOK {
		t.Fatal("another account's device was touched")
	}
	if s.VerifyPassword(alice, pw1) || !s.VerifyPassword(alice, "BrandNewPass123!") {
		t.Fatal("the password did not change")
	}
}

func TestDevicesAreKeptWhateverTheirSilence(t *testing.T) {
	s, ck, _ := withAccounts(t)
	e, _ := s.Enroll(spec(alice, devA))
	ck.Add(400 * 24 * time.Hour)
	if err := s.Sweep(ck.Now()); err != nil {
		t.Fatal(err)
	}
	if len(s.Devices(alice)) != 1 {
		t.Fatal("a device silent for more than a year is still listed (19 item AV)")
	}
	if _, st := s.Hello(devA, e.Token); st != HelloOK {
		t.Fatal("and its token still works")
	}
}

func TestRemoveAccountErasesEverythingTheStoreHoldsOfIt(t *testing.T) {
	s, _, dir := withAccounts(t)
	if _, err := s.AddAccountWithKind("guest1", pw1, KindGuest); err != nil {
		t.Fatal(err)
	}
	s.Enroll(spec(alice, devA))
	s.Enroll(spec(alice, devB))
	s.Revoke(alice, devB) // a tombstone of alice's
	if _, _, err := s.NewPairingCode(alice); err != nil {
		t.Fatal(err)
	}
	const res = "RRRRRRRRRRRRRRRRRRRRRA"
	if err := s.AddGrant(alice, res, GrantResource, "guest1"); err != nil {
		t.Fatal(err)
	}
	// alice registered through an invitation of her own: its used record names her
	code, _, _ := s.CreateInvitation("member", "")
	if _, err := register(t, s, "aliceb", "Al1ceBPassw0rd", KindMember, code, "DDDDDDDDDDDDDDDDDDDDDA"); err != nil {
		t.Fatal(err)
	}

	gone, err := s.RemoveAccount("aliceb")
	if err != nil || len(gone) != 1 {
		t.Fatalf("%v %v", gone, err)
	}
	if len(s.ListInvitations()) != 0 {
		t.Fatal("the used invitation record of a deleted account must go with it")
	}

	gone, err = s.RemoveAccount(alice)
	if err != nil || len(gone) != 1 || gone[0].ID != devA {
		t.Fatalf("%v %v", gone, err)
	}
	if s.HasGrant(res, "guest1") || len(s.GrantsForGuest("guest1")) != 0 {
		t.Fatal("a grant the account made must go with it")
	}
	if s.OutstandingCodes(alice) != 0 {
		t.Fatal("a pairing code of the account survived")
	}
	// the whole data directory: no file mentions the login any more
	entries, err := os.ReadDir(dir)
	if err != nil {
		t.Fatal(err)
	}
	for _, en := range entries {
		raw, err := os.ReadFile(filepath.Join(dir, en.Name()))
		if err != nil {
			t.Fatal(err)
		}
		if strings.Contains(string(raw), `"alice"`) || strings.Contains(string(raw), devA) || strings.Contains(string(raw), devB) {
			t.Fatalf("%s still names the deleted account or its devices", en.Name())
		}
	}
	// the login is free again, and so are the device ids
	if _, err := s.AddAccount(alice, pw1); err != nil {
		t.Fatalf("a deleted login is free: %v", err)
	}
	if _, err := s.Enroll(spec(bob, devA)); err != nil {
		t.Fatalf("a deleted account's device id is free: %v", err)
	}
}

func TestGrantRules(t *testing.T) {
	s, _, _ := withAccounts(t)
	if _, err := s.AddAccountWithKind("guest1", pw1, KindGuest); err != nil {
		t.Fatal(err)
	}
	if _, err := s.AddAccountWithKind("guest2", pw1, KindGuest); err != nil {
		t.Fatal(err)
	}
	if !s.IsGuest("guest1") || s.IsGuest(alice) || s.KindOf(alice) != KindMember {
		t.Fatal("kinds")
	}
	const res, brd = "RRRRRRRRRRRRRRRRRRRRRA", "BBBBBBBBBBBBBBBBBBBBBA"

	if err := s.AddGrant(alice, res, GrantResource, "guest1"); err != nil {
		t.Fatal(err)
	}
	if err := s.AddGrant(alice, res, GrantResource, "guest1"); !errors.Is(err, ErrGrantHeld) {
		t.Fatalf("a grant twice: %v", err)
	}
	if err := s.AddGrant(alice, res, GrantResource, bob); !errors.Is(err, ErrNotGuest) {
		t.Fatalf("giving to a member: %v", err)
	}
	if err := s.AddGrant("guest1", res, GrantResource, "guest2"); !errors.Is(err, ErrNotMember) {
		t.Fatalf("a guest gives nothing: %v", err)
	}
	if err := s.AddGrant(alice, "short", GrantResource, "guest1"); !errors.Is(err, ErrBadGrantTarget) {
		t.Fatalf("a malformed target: %v", err)
	}
	if err := s.AddGrant(alice, res, "folder", "guest1"); !errors.Is(err, ErrBadGrantTarget) {
		t.Fatalf("an unknown grant kind: %v", err)
	}
	if err := s.AddGrant("nobody", res, GrantResource, "guest1"); !errors.Is(err, ErrNoAccount) {
		t.Fatalf("an unknown giver: %v", err)
	}
	if err := s.AddGrant(alice, brd, GrantBroadcast, "guest1"); err != nil {
		t.Fatal(err)
	}
	if err := s.AddGrant(bob, res, GrantResource, "guest1"); err != nil {
		t.Fatalf("two members may hold the same resource id: %v", err)
	}
	got := s.GrantsForGuest("guest1")
	if len(got) != 3 || got[0].GrantedBy != alice || got[0].TargetID != brd || got[0].Kind != GrantBroadcast {
		t.Fatalf("what guest1 holds: %+v", got)
	}
	if guests := s.GuestsOf(alice, res); len(guests) != 1 || guests[0] != "guest1" {
		t.Fatalf("%v", guests)
	}
	if len(s.GrantsBy(alice)) != 2 {
		t.Fatalf("%+v", s.GrantsBy(alice))
	}

	if _, err := s.RemoveGrant(alice, res, "guest1"); err != nil {
		t.Fatal(err)
	}
	if _, err := s.RemoveGrant(alice, res, "guest1"); !errors.Is(err, ErrNoGrant) {
		t.Fatalf("a grant taken back twice: %v", err)
	}
	if !s.HasGrant(res, "guest1") {
		t.Fatal("bob's grant of the same id must survive alice's removal")
	}

	// alice becomes a guest: what she gave means nothing any more
	changed, err := s.SetKind(alice, KindGuest)
	if err != nil || !changed {
		t.Fatalf("%v %v", changed, err)
	}
	if len(s.GrantsBy(alice)) != 0 {
		t.Fatal("a guest holds no grant it made")
	}
	if _, err := s.SetKind(alice, "root"); !errors.Is(err, ErrBadKind) {
		t.Fatalf("%v", err)
	}
}

func TestStoreSurvivesAReopenWithInvitationsGrantsAndTheSharedPassword(t *testing.T) {
	s, ck, dir := withAccounts(t)
	if _, err := s.AddAccountWithKind("guest1", pw1, KindGuest); err != nil {
		t.Fatal(err)
	}
	const res = "RRRRRRRRRRRRRRRRRRRRRA"
	s.AddGrant(alice, res, GrantResource, "guest1")
	unused, _, _ := s.CreateInvitation("member", "")
	used, _, _ := s.CreateInvitation("guest", "")
	if _, err := register(t, s, "carol", "C4rolPassw0rd", KindGuest, used, devC); err != nil {
		t.Fatal(err)
	}
	s.SetSharedInvitation("SharedSecret123!", KindMember)

	again := open(t, dir, ck, nil)
	if k, err := again.OneTimeInvitation(unused); err != nil || k != KindMember {
		t.Fatalf("an unused code survives a restart: %q %v", k, err)
	}
	if _, err := again.OneTimeInvitation(used); !errors.Is(err, ErrInvitationInvalid) {
		t.Fatalf("a used code stays used across a restart: %v", err)
	}
	if _, err := again.VerifySharedInvitation("SharedSecret123!"); err != nil {
		t.Fatalf("the shared password survives a restart: %v", err)
	}
	if !again.HasGrant(res, "guest1") || !again.IsGuest("carol") {
		t.Fatal("grants or kinds did not survive")
	}
}

func TestPolicyAndPerAccountLimits(t *testing.T) {
	s, _, _ := withAccounts(t)
	p := s.Policy()
	if p.MaxAccounts != 10 || p.Defaults.ClipboardBytes != 5<<30 {
		t.Fatalf("the defaults of item AS are ten accounts and 5 GiB: %+v", p)
	}
	if s.LimitsFor(alice).ClipboardBytes != 5<<30 || s.LimitsFor("nobody").Devices != p.Defaults.Devices {
		t.Fatal("an account without overrides gets the defaults")
	}

	zero, three := int64(0), 3
	if err := s.SetAccountLimits(alice, &AccountLimits{ClipboardMaxBytes: &zero, DevicesPerAccount: &three}); err != nil {
		t.Fatal(err)
	}
	l := s.LimitsFor(alice)
	if l.ClipboardBytes != 0 || l.Devices != 3 || l.Resources != p.Defaults.Resources {
		t.Fatalf("overrides lay over the defaults: %+v", l)
	}
	if s.LimitsFor(bob).ClipboardBytes != 5<<30 {
		t.Fatal("an override leaked to another account")
	}
	neg := -1
	if err := s.SetAccountLimits(alice, &AccountLimits{DevicesPerAccount: &neg}); !errors.Is(err, ErrBadLimit) {
		t.Fatalf("a negative limit: %v", err)
	}
	if err := s.SetAccountLimits("nobody", nil); !errors.Is(err, ErrNoAccount) {
		t.Fatalf("%v", err)
	}
	if err := s.SetAccountLimits(alice, nil); err != nil || s.AccountLimitsFor(alice) != nil {
		t.Fatalf("nil clears the overrides: %v", err)
	}

	// a limit lowered under the holdings refuses only the next enrollment, nothing is trimmed
	s.Enroll(spec(alice, devA))
	s.Enroll(spec(alice, devB))
	one := 1
	s.SetAccountLimits(alice, &AccountLimits{DevicesPerAccount: &one})
	if len(s.Devices(alice)) != 2 {
		t.Fatal("lowering a limit removed devices")
	}
	if _, err := s.Enroll(spec(alice, devC)); !errors.Is(err, ErrLimit) {
		t.Fatalf("the next device is refused: %v", err)
	}
	if _, err := s.Enroll(spec(alice, devA)); err != nil {
		t.Fatalf("a device already enrolled re-enrolls at the limit: %v", err)
	}

	if k, err := NormalizeKind("family"); err != nil || k != KindMember {
		t.Fatalf("an earlier build's word reads as member: %q %v", k, err)
	}
}

func TestPacerIsOnePasswordCheckASecondAndNeverALockout(t *testing.T) {
	ck := newClock()
	var slept time.Duration
	sleep := func(d time.Duration) {
		slept += d
		ck.Add(d)
	}
	p := newPacer(ck.Now, sleep, 0)

	// first check: no wait; second right behind it waits its turn; a third would wait two slots and is
	// turned away without claiming anything
	k := LoginKey("alice")
	if w, ok := p.Reserve(k); !ok || w != 0 {
		t.Fatalf("first: %v %v", w, ok)
	}
	if w, ok := p.Reserve(k); !ok || w != time.Second {
		t.Fatalf("second waits one slot: %v %v", w, ok)
	}
	if _, ok := p.Reserve(k); ok {
		t.Fatal("a third check inside the window is turned away, not queued")
	}
	// a turned-away check claimed nothing: a second later the next one is only one slot behind
	ck.Add(time.Second)
	if w, ok := p.Reserve(k); !ok || w != time.Second {
		t.Fatalf("after the window: %v %v", w, ok)
	}

	// the login and the address are independent keys, and a check is paced by both
	q := newPacer(ck.Now, sleep, 0)
	if !q.Wait("alice", "10.0.0.1") {
		t.Fatal("first")
	}
	before := slept
	if !q.Wait("bob", "10.0.0.2") || slept != before {
		t.Fatal("another login from another address is not paced by the first")
	}
	if !q.Wait("carol", "10.0.0.1") || slept-before != time.Second {
		t.Fatalf("another login from the same address waits its second: slept %v", slept-before)
	}
	if !q.Wait("alice", "10.0.0.3") {
		t.Fatal("alice from a new address still waits only her own slot")
	}

	// nothing remembers a failure: a hundred checks in a row are all evaluated, one a second, and the
	// right password still verifies at the end
	r := newPacer(ck.Now, sleep, 0)
	start := ck.Now()
	for i := 0; i < 100; i++ {
		if !r.Wait("alice", "10.0.0.9") {
			t.Fatalf("check %d was turned away: there is no lockout", i)
		}
	}
	if got := ck.Now().Sub(start); got != 99*time.Second {
		t.Fatalf("a hundred checks take 99 slots of waiting, got %v", got)
	}
	s, _, _ := withAccounts(t)
	if !s.VerifyPassword(alice, pw1) {
		t.Fatal("the right password is evaluated whatever came before it")
	}

	// off: a test that is not about the pace
	off := newPacer(ck.Now, sleep, -1)
	for i := 0; i < 5; i++ {
		if w, ok := off.Reserve(k); !ok || w != 0 {
			t.Fatalf("pacing off: %v %v", w, ok)
		}
	}
}

func TestPacerWaitAddrKeyedByAddressAlone(t *testing.T) {
	ck := newClock()
	var slept time.Duration
	sleep := func(d time.Duration) {
		slept += d
		ck.Add(d)
	}
	p := newPacer(ck.Now, sleep, 0)

	// first check: no wait
	if !p.WaitAddr("10.0.0.1") || slept != 0 {
		t.Fatal("first check on 10.0.0.1 should not wait")
	}
	// second check on same address waits one slot
	before := slept
	if !p.WaitAddr("10.0.0.1") || slept-before != time.Second {
		t.Fatalf("second check on 10.0.0.1 should wait 1s, slept %v", slept-before)
	}
	// third check arrives while second is running: wait would be > 1 interval, turned away!
	// to test turned away: if Reserve is called before time advanced:
	// currently ck.Now() was advanced by 1s in sleep, so the slot at start+1s is claimed (until start+2s).
	// If two checks arrive at the same time:
	w1, ok1 := p.Reserve(AddrKey("10.0.0.2"))
	if !ok1 || w1 != 0 {
		t.Fatalf("first reserve: %v %v", w1, ok1)
	}
	w2, ok2 := p.Reserve(AddrKey("10.0.0.2"))
	if !ok2 || w2 != time.Second {
		t.Fatalf("second reserve: %v %v", w2, ok2)
	}
	_, ok3 := p.Reserve(AddrKey("10.0.0.2"))
	if ok3 {
		t.Fatal("third reserve on same address within window should be rate-limited")
	}

	// a different address is completely independent
	if !p.WaitAddr("10.0.0.3") {
		t.Fatal("independent address should not be locked out")
	}
}
