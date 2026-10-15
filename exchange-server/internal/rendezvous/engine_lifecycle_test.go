package rendezvous_test

import (
	"bytes"
	"testing"
	"time"

	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Ticket 224: invitations, registration, the shared invitation password, the guest kind, the account
// cap, password change and account deletion, over the real engine and the real stores.

const (
	newPW    = "N3wPassw0rd!xyz"
	sharedPW = "Shar3dInvite!pw"
)

// noAccounts starts a server with no account at all: everyone arrives by registration.
func noAccounts(extra ...func(*testkit.Options)) testkit.Options {
	o := testkit.Options{Limits: loose(), LoginLimits: loose(), Logins: []string{}}
	for _, f := range extra {
		f(&o)
	}
	return o
}

func mustInvite(t *testing.T, env *testkit.Env, kind string) string {
	t.Helper()
	r, err := env.Ops.CreateInvitation(kind, "")
	if err != nil {
		t.Fatal(err)
	}
	env.AddSecret(r.Code)
	return r.Code
}

func mustRegister(t *testing.T, d *testkit.Device, invitation, shared, login, pw string) *wire.Enrolled {
	t.Helper()
	r, err := d.Register(invitation, shared, login, pw)
	if err != nil {
		t.Fatal(err)
	}
	en, ok := r.(*wire.Enrolled)
	if !ok {
		t.Fatalf("register: %+v", r)
	}
	return en
}

func TestAOneTimeInvitationRegistersOnceAndItsSecondUseLooksLikeAWrongCode(t *testing.T) {
	env := testkit.StartServer(t, noAccounts())
	code := mustInvite(t, env, "member")

	en := mustRegister(t, env.NewDevice("carol"), code, "", "carol", testkit.TestPassword)
	if en.Account != "carol" || en.AccountKind != "member" || !wire.ValidToken(en.DeviceToken) {
		t.Fatalf("enrolled: %+v", en)
	}
	if !env.Accounts.Exists("carol") || len(env.Accounts.Devices("carol")) != 1 {
		t.Fatal("the account and its first device must exist")
	}

	// the used code, a withdrawn code and a code nobody made are ONE answer, byte for byte
	used, _ := env.NewDevice("dave").Register(code, "", "dave", testkit.TestPassword)
	refusedWith(t, used, wire.ReasonInvitationInvalid)
	unknown, _ := env.NewDevice("dave").Register(env.NewID(), "", "dave", testkit.TestPassword)
	refusedWith(t, unknown, wire.ReasonInvitationInvalid)
	withdrawn := mustInvite(t, env, "member")
	for _, iv := range env.Ops.ListInvitations() {
		if !iv.Used {
			if ok, err := env.Ops.WithdrawInvitation(iv.CodeHash[:8]); err != nil || !ok {
				t.Fatalf("withdraw: %v %v", ok, err)
			}
		}
	}
	gone, _ := env.NewDevice("dave").Register(withdrawn, "", "dave", testkit.TestPassword)
	refusedWith(t, gone, wire.ReasonInvitationInvalid)
	if !bytes.Equal(frameBytes(t, used), frameBytes(t, unknown)) || !bytes.Equal(frameBytes(t, used), frameBytes(t, gone)) {
		t.Fatal("a used, an unknown and a withdrawn code must be told apart by nobody")
	}
	if env.Accounts.Exists("dave") {
		t.Fatal("a refused registration must store nothing")
	}
}

func TestAFailedRegistrationConsumesNoInvitation(t *testing.T) {
	env := testkit.StartServer(t, noAccounts(func(o *testkit.Options) { o.Logins = []string{"alice"} }))
	code := mustInvite(t, env, "member")

	r, _ := env.NewDevice("x").Register(code, "", "alice", testkit.TestPassword) // login taken
	refusedWith(t, r, wire.ReasonLoginTaken)
	r, _ = env.NewDevice("x").Register(code, "", "zed", "short") // password policy
	refusedWith(t, r, wire.ReasonPasswordPolicy)
	r, _ = env.NewDevice("x").Register(code, "", "a", testkit.TestPassword) // a login that is no login
	refusedWith(t, r, wire.ReasonLoginTaken)
	for _, iv := range env.Ops.ListInvitations() {
		if iv.Used {
			t.Fatal("a failed registration consumed the invitation")
		}
	}
	mustRegister(t, env.NewDevice("zed"), code, "", "zed", testkit.TestPassword) // the same code still works
}

func TestAnInvitationNeverExpires(t *testing.T) {
	clock := newClock()
	env := testkit.StartServer(t, noAccounts(func(o *testkit.Options) { o.Now = clock.Now }))
	code := mustInvite(t, env, "member")
	clock.Add(366 * 24 * time.Hour)
	mustRegister(t, env.NewDevice("late"), code, "", "late", testkit.TestPassword)
}

func TestTheSharedInvitationPasswordWorksOnlyWhileOnAndStopsAtOnce(t *testing.T) {
	env := testkit.StartServer(t, noAccounts())
	env.AddSecret(sharedPW)

	// off by default: the password is just a wrong code
	r, _ := env.NewDevice("u1").Register("", sharedPW, "user1", testkit.TestPassword)
	refusedWith(t, r, wire.ReasonInvitationInvalid)

	if err := env.Ops.SetSharedInvitation(sharedPW, "member"); err != nil {
		t.Fatal(err)
	}
	en := mustRegister(t, env.NewDevice("u1"), "", sharedPW, "user1", testkit.TestPassword)
	if en.AccountKind != "member" {
		t.Fatalf("kind %q", en.AccountKind)
	}
	// it is not used up: another person registers with it too, as the kind it was set for
	mustRegister(t, env.NewDevice("u2"), "", sharedPW, "user2", testkit.TestPassword)

	// changed: the old one stops at once, the new one works
	const second = "Sec0ndInvite!pw"
	env.AddSecret(second)
	if err := env.Ops.SetSharedInvitation(second, "guest"); err != nil {
		t.Fatal(err)
	}
	r, _ = env.NewDevice("u3").Register("", sharedPW, "user3", testkit.TestPassword)
	refusedWith(t, r, wire.ReasonInvitationInvalid)
	en = mustRegister(t, env.NewDevice("u3"), "", second, "user3", testkit.TestPassword)
	if en.AccountKind != "guest" {
		t.Fatalf("kind %q", en.AccountKind)
	}

	// off: nothing registers by it
	if err := env.Ops.SetSharedInvitation("", ""); err != nil {
		t.Fatal(err)
	}
	r, _ = env.NewDevice("u4").Register("", second, "user4", testkit.TestPassword)
	refusedWith(t, r, wire.ReasonInvitationInvalid)
	if st := env.Ops.SharedInvitationStatus(); st.Enabled {
		t.Fatalf("status: %+v", st)
	}
}

func TestAGuestPublishesNothingAndSeesOnlyWhatWasGrantedToIt(t *testing.T) {
	env := testkit.StartServer(t, noAccounts(func(o *testkit.Options) {
		o.Logins = []string{"host"}
		o.WithRelay = true
		o.Cast = true
	}))
	host := startProducer(t, env, "host")
	defer host.Close()
	resourceID, _, ans, err := host.PublishShare(false, nil)
	if err != nil || ans.TypeName() != wire.TypePublished {
		t.Fatalf("%v %+v", err, ans)
	}

	code := mustInvite(t, env, "guest")
	gd := env.NewDevice("visitor")
	en := mustRegister(t, gd, code, "", "visitor", testkit.TestPassword)
	if en.AccountKind != "guest" {
		t.Fatalf("kind %q", en.AccountKind)
	}
	ctl := gd.Control()

	// the directory of a guest lists its own devices and nothing of the host
	dir, err := ctl.ListDirectory()
	if err != nil {
		t.Fatal(err)
	}
	if len(dir.Resources) != 0 || len(dir.Broadcasts) != 0 {
		t.Fatalf("a guest sees what was never given to it: %+v", dir)
	}
	if findRec(recs(dir.Devices), "deviceId", host.ID) != nil {
		t.Fatal("a guest sees the host's device")
	}

	// a grant makes exactly that resource visible
	if err := env.Ops.AddGrant("host", resourceID, "resource", "visitor"); err != nil {
		t.Fatal(err)
	}
	dir, err = ctl.ListDirectory()
	if err != nil {
		t.Fatal(err)
	}
	if got := recs(dir.Resources); len(got) != 1 || findRec(got, "resourceId", resourceID) == nil {
		t.Fatalf("the granted resource: %+v", dir.Resources)
	}

	// publish, broadcast-start, cast and upload are refused by name
	_, _, ans, err = (&testkit.Producer{Device: gd}).PublishShare(false, nil)
	if err != nil {
		t.Fatal(err)
	}
	refusedWith(t, ans, wire.ReasonNotPermitted)
	r, err := ctl.Request(&wire.BroadcastStart{Broadcast: castBroadcast(env.NewID())}, 3*time.Second, wire.TypeBroadcastStarted, wire.TypeRefused)
	if err != nil {
		t.Fatal(err)
	}
	refusedWith(t, r, wire.ReasonNotPermitted)
	r, err = ctl.Request(&wire.Cast{BroadcastID: env.NewID(), ReceiverDeviceID: env.NewID()}, 3*time.Second, wire.TypeRefused)
	if err != nil {
		t.Fatal(err)
	}
	refusedWith(t, r, wire.ReasonNotPermitted)
	c, up, err := gd.Upload(env.NewID(), "audio/aac")
	if err != nil {
		t.Fatal(err)
	}
	if c != nil {
		c.Close()
	}
	refusedWith(t, up, wire.ReasonNotPermitted)

	// taking the grant back empties the directory again; a second device by a pairing code is allowed
	if err := env.Ops.RemoveGrant("host", resourceID, "visitor"); err != nil {
		t.Fatal(err)
	}
	dir, _ = ctl.ListDirectory()
	if len(dir.Resources) != 0 {
		t.Fatalf("a taken-back grant is still listed: %+v", dir.Resources)
	}
	pc, _, err := env.Ops.PairingCode("visitor")
	if err != nil {
		t.Fatal(err)
	}
	env.AddSecret(pc)
	if r, _ := env.NewDevice("visitor").EnrollWithCode(pc); r == nil || r.TypeName() != wire.TypeEnrolled {
		t.Fatalf("a guest's second device by a pairing code: %+v", r)
	}
}

func TestRegistrationPastTheAccountCapIsRefusedButTheOperatorsAddIsNot(t *testing.T) {
	env := testkit.StartServer(t, noAccounts(func(o *testkit.Options) { o.Logins = []string{"alice"} }))
	one := 1
	if _, err := env.Ops.SetPolicy(&one, nil); err != nil {
		t.Fatal(err)
	}
	code := mustInvite(t, env, "member")

	r, _ := env.NewDevice("x").Register(code, "", "bobby", testkit.TestPassword)
	refusedWith(t, r, wire.ReasonLimitReached)
	for _, iv := range env.Ops.ListInvitations() {
		if iv.Used {
			t.Fatal("a refused registration consumed the invitation")
		}
	}
	// the operator is not bound by the cap he set: he is told, not refused
	if _, over, err := env.Ops.AddAccountKind("carol", testkit.TestPassword, "member"); err != nil || !over {
		t.Fatalf("operator add: over=%v err=%v", over, err)
	}
	// raised, the same invitation registers
	five := 5
	if _, err := env.Ops.SetPolicy(&five, nil); err != nil {
		t.Fatal(err)
	}
	mustRegister(t, env.NewDevice("bobby"), code, "", "bobby", testkit.TestPassword)
}

func TestAPasswordChangeSignsTheOtherDevicesOutAndTheCallerKeepsWorking(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	d1, d2 := env.NewDevice("alice"), env.NewDevice("alice")
	mustEnroll(t, d1)
	mustEnroll(t, d2)
	ctl := d1.Control()
	env.AddSecret(newPW)

	// the old password is required, and a wrong one signs nobody out
	r, err := ctl.Request(&wire.PasswordChange{OldPassword: "Wrongpass1", NewPassword: newPW}, 5*time.Second, wire.TypePasswordChanged, wire.TypeRefused)
	if err != nil {
		t.Fatal(err)
	}
	refusedWith(t, r, wire.ReasonBadCredentials)
	r, err = ctl.Request(&wire.PasswordChange{OldPassword: testkit.TestPassword, NewPassword: "short"}, 5*time.Second, wire.TypePasswordChanged, wire.TypeRefused)
	if err != nil {
		t.Fatal(err)
	}
	refusedWith(t, r, wire.ReasonPasswordPolicy)
	if c, _, _ := d2.Hello(); c == nil {
		t.Fatal("a refused change must sign nobody out")
	} else {
		c.Close()
	}

	r, err = ctl.Request(&wire.PasswordChange{OldPassword: testkit.TestPassword, NewPassword: newPW}, 5*time.Second, wire.TypePasswordChanged, wire.TypeRefused)
	if err != nil {
		t.Fatal(err)
	}
	pc, ok := r.(*wire.PasswordChanged)
	if !ok || !wire.ValidToken(pc.DeviceToken) {
		t.Fatalf("password-change: %+v", r)
	}
	oldToken := d1.Token
	d1.Token = pc.DeviceToken
	env.AddSecret(d1.Token)

	// the caller keeps working with its new token; its old token is dead
	if c, rr, _ := d1.Hello(); c == nil {
		t.Fatalf("the changing device must keep working: %+v", rr)
	} else {
		c.Close()
	}
	_, rr, _ := d1.HelloWith(oldToken)
	refusedWith(t, rr, wire.ReasonBadCredentials)

	// the other device: its stream closed, its record stays, its next hello is `signed-out`
	select {
	case <-d2.Control().Closed():
	case <-time.After(3 * time.Second):
		t.Fatal("the other device's stream must be closed")
	}
	_, rr, _ = d2.Hello()
	refusedWith(t, rr, wire.ReasonSignedOut)
	if len(env.Accounts.Devices("alice")) != 2 {
		t.Fatal("a signed-out device record must stay")
	}

	// the old password no longer enrolls, the new one does - also for the signed-out device itself
	rr, _ = env.NewDevice("alice").Enroll()
	refusedWith(t, rr, wire.ReasonBadCredentials)
	if rr, _ := d2.EnrollWith(newPW); rr == nil || rr.TypeName() != wire.TypeEnrolled {
		t.Fatalf("signing in again with the new password: %+v", rr)
	}
	if c, rr, _ := d2.Hello(); c == nil {
		t.Fatalf("the device that signed in again must be welcome: %+v", rr)
	} else {
		c.Close()
	}
}

func TestAStreamOfWrongPasswordsNeverStopsTheRightOne(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{Limits: loose(), LoginLimits: loose(), Logins: []string{"alice"},
		PaceInterval: 30 * time.Millisecond})
	began := time.Now()
	for i := 0; i < 40; i++ {
		r, err := env.NewDevice("alice").EnrollWith("Wrongpass1")
		if err != nil {
			t.Fatal(err)
		}
		refusedWith(t, r, wire.ReasonBadCredentials) // never `rate-limited`: nothing remembers a failure
	}
	mustEnroll(t, env.NewDevice("alice"))
	if took := time.Since(began); took > 20*time.Second {
		t.Fatalf("the right password was evaluated after %v", took)
	}
}

func TestDeletingAnAccountErasesWhatTheOperatorsRemovalErases(t *testing.T) {
	env := testkit.StartServer(t, noAccounts(func(o *testkit.Options) { o.Logins = []string{"alice", "bobby", "keeper"} }))

	build := func(login string) (*testkit.Producer, string, string) {
		p := startProducer(t, env, login)
		rid, _, ans, err := p.PublishShare(false, nil)
		if err != nil || ans.TypeName() != wire.TypePublished {
			t.Fatalf("%v %+v", err, ans)
		}
		code, _, err := env.Ops.PairingCode(login)
		if err != nil {
			t.Fatal(err)
		}
		env.AddSecret(code)
		return p, rid, code
	}
	pa, ridA, codeA := build("alice")
	pb, ridB, codeB := build("bobby")
	pk, ridK, _ := build("keeper")
	defer pk.Close()
	other := env.NewDevice("alice")
	mustEnroll(t, other)

	// a wrong password deletes nothing
	r, err := pa.Control().Request(&wire.AccountDelete{Password: "Wrongpass1"}, 5*time.Second, wire.TypeAccountDeleted, wire.TypeRefused)
	if err != nil {
		t.Fatal(err)
	}
	refusedWith(t, r, wire.ReasonBadCredentials)
	if !env.Accounts.Exists("alice") {
		t.Fatal("a wrong password must delete nothing")
	}

	// the person deletes alice; the operator removes bobby
	r, err = pa.Control().Request(&wire.AccountDelete{Password: testkit.TestPassword}, 5*time.Second, wire.TypeAccountDeleted, wire.TypeRefused)
	if err != nil || r.TypeName() != wire.TypeAccountDeleted {
		t.Fatalf("account-delete: %v %+v", err, r)
	}
	if err := env.Ops.RemoveAccount("bobby"); err != nil {
		t.Fatal(err)
	}
	for _, c := range []interface{ Closed() <-chan struct{} }{pa.Control(), pb.Control(), other.Control()} {
		select {
		case <-c.Closed():
		case <-time.After(3 * time.Second):
			t.Fatal("every stream of a deleted account must close")
		}
	}

	// in memory: the same result either way, and the account that stayed is untouched
	for _, l := range []string{"alice", "bobby"} {
		if env.Accounts.Exists(l) || len(env.Accounts.Devices(l)) != 0 {
			t.Fatalf("%s is still held by the account store", l)
		}
		if _, _, rs, _ := env.Dir.SnapshotFor(l, ""); len(rs) != 0 {
			t.Fatalf("%s is still held by the directory", l)
		}
	}
	if !env.Accounts.Exists("keeper") {
		t.Fatal("another account was touched")
	}
	if _, _, rs, _ := env.Dir.SnapshotFor("keeper", pk.ID); len(rs) != 1 {
		t.Fatal("another account's resource was touched")
	}
	// no device of the two signs in again, and a pairing code of them is dead
	for _, d := range []*testkit.Device{pa.Device, pb.Device, other} {
		_, rr, _ := d.Hello()
		refusedWith(t, rr, wire.ReasonBadCredentials)
	}
	rr, _ := env.NewDevice("alice").EnrollWithCode(codeA)
	refusedWith(t, rr, wire.ReasonBadCredentials)
	rr, _ = env.NewDevice("bobby").EnrollWithCode(codeB)
	refusedWith(t, rr, wire.ReasonBadCredentials)

	// on disk: nothing of either account is left in the data directory
	env.Engine.Close() // flushes
	var needles []string
	for _, p := range []*testkit.Producer{pa, pb} {
		needles = append(needles, p.ID, p.Token)
	}
	needles = append(needles, other.ID, other.Token, "alice", "bobby", ridA, ridB)
	scanDataDir(t, env, needles)
	_ = ridK
}

func TestADeviceSilentForAYearIsStillListedAndStillWelcome(t *testing.T) {
	clock := newClock()
	env := testkit.StartServer(t, testkit.Options{Limits: loose(), LoginLimits: loose(), Logins: []string{"alice"}, Now: clock.Now})
	d := env.NewDevice("alice")
	mustEnroll(t, d)
	d.Close()
	clock.Add(400 * 24 * time.Hour)
	devs, err := env.Ops.ListDevices("alice")
	if err != nil || len(devs) != 1 {
		t.Fatalf("a device silent for a year must still be listed: %v %+v", err, devs)
	}
	if c, r, _ := d.Hello(); c == nil {
		t.Fatalf("its token must still work: %+v", r)
	} else {
		c.Close()
	}
}
