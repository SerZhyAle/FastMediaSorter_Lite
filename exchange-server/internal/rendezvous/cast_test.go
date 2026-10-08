package rendezvous_test

import (
	"encoding/json"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Ticket 166: cast routing. A scripted caster and scripted receivers complete the accept, decline,
// timeout, offline and stop flows, and the account boundary leaks nothing.

func castEnv(t *testing.T, mutate func(*testkit.Options), logins ...string) *testkit.Env {
	t.Helper()
	if len(logins) == 0 {
		logins = []string{"alice"}
	}
	o := looseOpts(logins...)
	o.Cast = true
	o.PublicEndpoint = "203.0.113.5:22022"
	if mutate != nil {
		mutate(&o)
	}
	return testkit.StartServer(t, o)
}

// castBroadcast is a broadcast record that smuggles an `access` member and an unknown nested member:
// the offer must carry the second and never the first.
func castBroadcast(id string) json.RawMessage {
	return json.RawMessage(`{"broadcastId":"` + id + `","mode":"AUDIO_ONLY","title":"Mic","future":{"a":{"b":1}},` +
		`"access":{"descriptor":"FMSSFTP2.broadcast-access-needle"},"descriptor":{"endpoints":[]}}`)
}

// caster enrolls a device that starts a broadcast, and a receiver device (role `receiver`) online.
func castPair(t *testing.T, env *testkit.Env, login string) (caster *testkit.Producer, bid string, receiver *testkit.Device, rc *testkit.Control) {
	t.Helper()
	caster = startProducer(t, env, login)
	t.Cleanup(caster.Close)
	bid = env.NewID()
	env.AddSecret("broadcast-access-needle")
	r, err := caster.Control().Request(&wire.BroadcastStart{Broadcast: castBroadcast(bid)}, 3*time.Second, wire.TypeBroadcastStarted, wire.TypeRefused)
	if err != nil || r.TypeName() != wire.TypeBroadcastStarted {
		t.Fatalf("%v %+v", err, r)
	}
	receiver = env.NewDevice(login)
	mustEnroll(t, receiver)
	var err2 error
	if rc, err2 = receiver.OnlineQuietly(); err2 != nil {
		t.Fatal(err2)
	}
	t.Cleanup(receiver.Close)
	return
}

func awaitResult(t *testing.T, c *testkit.Control) *wire.CastResult {
	t.Helper()
	e, err := c.Await(3*time.Second, wire.TypeCastResult)
	if err != nil {
		t.Fatal(err)
	}
	return e.(*wire.CastResult)
}

func awaitOffer(t *testing.T, c *testkit.Control) *wire.CastOffer {
	t.Helper()
	e, err := c.Await(3*time.Second, wire.TypeCastOffer)
	if err != nil {
		t.Fatal(err)
	}
	return e.(*wire.CastOffer)
}

func TestACastIsOfferedToItsReceiverOnlyWithoutAccessAndAnswered(t *testing.T) {
	env := castEnv(t, nil)
	caster, bid, rcv, rc := castPair(t, env, "alice")
	bystander := env.NewDevice("alice")
	mustEnroll(t, bystander)
	bc, err := bystander.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	defer bystander.Close()

	if err := caster.Control().Send(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: rcv.ID}); err != nil {
		t.Fatal(err)
	}
	offer := awaitOffer(t, rc)
	if offer.FromDeviceID != caster.ID {
		t.Fatalf("from %q", offer.FromDeviceID)
	}
	rec, err := wire.ParseRecord(offer.Broadcast)
	if err != nil {
		t.Fatal(err)
	}
	if id, _ := rec.Str("broadcastId"); id != bid || rec.Has("access") {
		t.Fatalf("the offer carries the broadcast record as stored, never `access`: %s", offer.Broadcast)
	}
	if strings.Contains(string(offer.Broadcast), "broadcast-access-needle") {
		t.Fatal("access leaked into the offer")
	}
	if !strings.Contains(string(offer.Broadcast), `"future":{"a":{"b":1}}`) {
		t.Fatalf("unknown members of the record travel verbatim: %s", offer.Broadcast)
	}
	// nobody else saw the offer
	if _, err := bc.Await(300*time.Millisecond, wire.TypeCastOffer); err == nil {
		t.Fatal("an offer must reach only its receiver")
	}
	// the caster has no answer yet
	if _, err := caster.Control().Await(200*time.Millisecond, wire.TypeCastResult); err == nil {
		t.Fatal("no result before the answer")
	}
	if env.Engine.PendingCasts() != 1 {
		t.Fatalf("%d pending", env.Engine.PendingCasts())
	}

	// accept
	if err := rc.Send(&wire.CastAnswer{BroadcastID: bid, Accepted: true}); err != nil {
		t.Fatal(err)
	}
	res := awaitResult(t, caster.Control())
	if !res.Accepted || res.Reason != "" || res.BroadcastID != bid || res.ReceiverDeviceID != rcv.ID {
		t.Fatalf("%+v", res)
	}
	if env.Engine.PendingCasts() != 0 {
		t.Fatal("an answered offer leaves the table")
	}
	// a second answer for the same offer is dropped: nothing arrives
	_ = rc.Send(&wire.CastAnswer{BroadcastID: bid, Accepted: false, Reason: "declined"})
	if _, err := caster.Control().Await(300*time.Millisecond, wire.TypeCastResult); err == nil {
		t.Fatal("an answer to no offer is dropped")
	}
}

func TestDeclineUnsupportedAndAnOddReasonAreForwarded(t *testing.T) {
	env := castEnv(t, nil)
	caster, bid, rcv, rc := castPair(t, env, "alice")
	for _, c := range []struct{ sent, want string }{
		{"declined", "declined"}, {"unsupported", "unsupported"}, {"nope-not-sure", "nope-not-sure"},
		{"", "declined"}, {"A very long reason with CAPITALS and spaces " + strings.Repeat("x", 100), "declined"},
	} {
		_ = caster.Control().Send(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: rcv.ID})
		awaitOffer(t, rc)
		_ = rc.Send(&wire.CastAnswer{BroadcastID: bid, Accepted: false, Reason: c.sent})
		res := awaitResult(t, caster.Control())
		if res.Accepted || res.Reason != c.want {
			t.Fatalf("sent %q: %+v, want reason %q", c.sent, res, c.want)
		}
	}
}

func TestAnOfflineReceiverAndAForeignOneAreTheSameAnswerAtOnce(t *testing.T) {
	env := castEnv(t, nil, "alice", "bobby")
	caster, bid, offline, _ := castPair(t, env, "alice")
	offline.Close()
	eventually(t, "the receiver to go offline", func() bool { return !env.Dir.DeviceOnline("alice", offline.ID) })
	// a receiver of another account, online with the role
	foreign := env.NewDevice("bobby")
	mustEnroll(t, foreign)
	fc, err := foreign.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	defer foreign.Close()
	// a device of the account with no receiver role, online
	norole := env.NewDevice("alice")
	norole.Roles = []string{"resource-consumer"}
	mustEnroll(t, norole)
	if _, err := norole.OnlineQuietly(); err != nil {
		t.Fatal(err)
	}
	defer norole.Close()
	// and a device that does not exist at all
	ghost := env.NewID()

	var frames []string
	var timings []time.Duration
	for _, id := range []string{offline.ID, foreign.ID, norole.ID, ghost} {
		start := time.Now()
		if err := caster.Control().Send(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: id}); err != nil {
			t.Fatal(err)
		}
		res := awaitResult(t, caster.Control())
		timings = append(timings, time.Since(start))
		if res.Accepted || res.Reason != wire.CastUnavailable || res.ReceiverDeviceID != id || res.BroadcastID != bid {
			t.Fatalf("%s: %+v", id, res)
		}
		// the bytes, with the one member the caster itself named taken out, are the same
		res.ReceiverDeviceID = "X"
		b, _ := wire.Encode(res)
		frames = append(frames, string(b))
	}
	for i := 1; i < len(frames); i++ {
		if frames[i] != frames[0] {
			t.Fatalf("a foreign, role-less or unknown receiver must answer with the bytes of an offline one:\n%s\n%s", frames[0], frames[i])
		}
	}
	for _, d := range timings {
		if d > 500*time.Millisecond {
			t.Fatalf("`unavailable` is answered at once, not after %v", d)
		}
	}
	// the foreign receiver saw nothing at all, and nothing is pending
	if _, err := fc.Await(300*time.Millisecond, wire.TypeCastOffer); err == nil {
		t.Fatal("a cast never crosses an account")
	}
	if env.Engine.PendingCasts() != 0 {
		t.Fatalf("%d pending", env.Engine.PendingCasts())
	}
}

func TestACastNeedsALiveBroadcastOfTheCallersOwnAccount(t *testing.T) {
	env := castEnv(t, nil, "alice", "bobby")
	_, bid, rcv, _ := castPair(t, env, "alice")
	// bobby's device casts alice's broadcast to alice's receiver: nothing of alice's is visible to it
	other := startProducer(t, env, "bobby")
	defer other.Close()
	r, err := other.Control().Request(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: rcv.ID}, 3*time.Second, wire.TypeRefused, wire.TypeCastResult)
	if err != nil {
		t.Fatal(err)
	}
	refusedWith(t, r, wire.ReasonUnavailable)
	// an unknown broadcast is the same
	r, _ = other.Control().Request(&wire.Cast{BroadcastID: env.NewID(), ReceiverDeviceID: rcv.ID}, 3*time.Second, wire.TypeRefused, wire.TypeCastResult)
	refusedWith(t, r, wire.ReasonUnavailable)
	// an ended broadcast of the caster's own account
	owner := startProducer(t, env, "alice")
	defer owner.Close()
	ended := env.NewID()
	owner.Control().Request(&wire.BroadcastStart{Broadcast: castBroadcast(ended)}, 3*time.Second, wire.TypeBroadcastStarted)
	_ = owner.Control().Send(&wire.BroadcastEnd{BroadcastID: ended})
	eventually(t, "the broadcast to end", func() bool { return env.Dir.Counts().Broadcasts == 1 })
	r, _ = owner.Control().Request(&wire.Cast{BroadcastID: ended, ReceiverDeviceID: rcv.ID}, 3*time.Second, wire.TypeRefused, wire.TypeCastResult)
	refusedWith(t, r, wire.ReasonUnavailable)
	if env.Engine.PendingCasts() != 0 {
		t.Fatal("nothing pending")
	}
}

func TestAReceiverThatDoesNotAnswerTimesOutAndALateAnswerIsDropped(t *testing.T) {
	p := testkit.TestParams()
	p.CastTimeout = 400 * time.Millisecond
	env := castEnv(t, func(o *testkit.Options) { o.Params = &p })
	caster, bid, rcv, rc := castPair(t, env, "alice")
	_ = caster.Control().Send(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: rcv.ID})
	awaitOffer(t, rc)
	res := awaitResult(t, caster.Control())
	if res.Accepted || res.Reason != wire.CastTimeout || res.ReceiverDeviceID != rcv.ID {
		t.Fatalf("%+v", res)
	}
	if env.Engine.PendingCasts() != 0 {
		t.Fatal("a timed out offer leaves the table")
	}
	// the receiver answers late: dropped
	_ = rc.Send(&wire.CastAnswer{BroadcastID: bid, Accepted: true})
	if _, err := caster.Control().Await(400*time.Millisecond, wire.TypeCastResult); err == nil {
		t.Fatal("a late answer must be dropped")
	}
}

func TestTheTimeoutIsDrivenByTheInjectedClock(t *testing.T) {
	clock := newClock()
	env := castEnv(t, func(o *testkit.Options) { o.Now = clock.Now })
	caster, bid, rcv, rc := castPair(t, env, "alice")
	_ = caster.Control().Send(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: rcv.ID})
	awaitOffer(t, rc)
	clock.Add(59 * time.Second)
	time.Sleep(200 * time.Millisecond) // several sweeps: nothing is due
	if _, err := caster.Control().Await(100*time.Millisecond, wire.TypeCastResult); err == nil {
		t.Fatal("not before 60 s")
	}
	clock.Add(2 * time.Second)
	res := awaitResult(t, caster.Control())
	if res.Reason != wire.CastTimeout {
		t.Fatalf("%+v", res)
	}
}

func TestCastStopIsForwardedToThePairOnly(t *testing.T) {
	env := castEnv(t, nil)
	caster, bid, rcv, rc := castPair(t, env, "alice")
	third := env.NewDevice("alice")
	mustEnroll(t, third)
	tc, err := third.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	defer third.Close()

	_ = caster.Control().Send(&wire.CastStop{BroadcastID: bid, ReceiverDeviceID: rcv.ID})
	e, err := rc.Await(3*time.Second, wire.TypeCastStop)
	if err != nil {
		t.Fatal(err)
	}
	if s := e.(*wire.CastStop); s.BroadcastID != bid || s.ReceiverDeviceID != rcv.ID {
		t.Fatalf("it is forwarded as it is: %+v", s)
	}
	if _, err := tc.Await(300*time.Millisecond, wire.TypeCastStop); err == nil {
		t.Fatal("a stop reaches the pair only")
	}
	if _, err := caster.Control().Await(200*time.Millisecond, wire.TypeCastStop); err == nil {
		t.Fatal("not echoed to its sender")
	}
	// the receiver stopping by itself reaches the broadcaster
	_ = rc.Send(&wire.CastStop{BroadcastID: bid, ReceiverDeviceID: rcv.ID})
	if _, err := caster.Control().Await(3*time.Second, wire.TypeCastStop); err != nil {
		t.Fatalf("a receiver's own stop goes to the caster: %v", err)
	}
}

func TestCastStopNeverCrossesAnAccount(t *testing.T) {
	env := castEnv(t, nil, "alice", "bobby")
	_, bid, rcv, rc := castPair(t, env, "alice")
	other := startProducer(t, env, "bobby")
	defer other.Close()
	_ = other.Control().Send(&wire.CastStop{BroadcastID: bid, ReceiverDeviceID: rcv.ID})
	if _, err := rc.Await(400*time.Millisecond, wire.TypeCastStop); err == nil {
		t.Fatal("another account's stop must not arrive")
	}
}

func TestACastToTheCastersOwnDeviceIsRoutedLikeAnyOther(t *testing.T) {
	env := castEnv(t, nil)
	caster, bid, _, _ := castPair(t, env, "alice")
	_ = caster.Control().Send(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: caster.ID})
	offer := awaitOffer(t, caster.Control())
	if offer.FromDeviceID != caster.ID {
		t.Fatalf("%+v", offer)
	}
	_ = caster.Control().Send(&wire.CastAnswer{BroadcastID: bid, Accepted: true})
	if res := awaitResult(t, caster.Control()); !res.Accepted || res.ReceiverDeviceID != caster.ID {
		t.Fatalf("%+v", res)
	}
}

func TestThePendingTableIsEmptyAfterEitherStreamDiesAndAfterTheBroadcastEnds(t *testing.T) {
	env := castEnv(t, nil)
	// the receiver's stream dies: the caster is told, the table is empty
	caster, bid, rcv, rc := castPair(t, env, "alice")
	_ = caster.Control().Send(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: rcv.ID})
	awaitOffer(t, rc)
	rcv.Close()
	res := awaitResult(t, caster.Control())
	if res.Accepted || res.Reason != wire.CastUnavailable {
		t.Fatalf("%+v", res)
	}
	eventually(t, "the table to empty", func() bool { return env.Engine.PendingCasts() == 0 })

	// the caster's stream dies: the table is empty, the receiver gets nothing more
	caster2, bid2, rcv2, rc2 := castPair(t, env, "alice")
	_ = caster2.Control().Send(&wire.Cast{BroadcastID: bid2, ReceiverDeviceID: rcv2.ID})
	awaitOffer(t, rc2)
	caster2.Control().Close()
	eventually(t, "the table to empty", func() bool { return env.Engine.PendingCasts() == 0 })

	// the broadcast ends: the offer is dropped
	caster3, bid3, rcv3, rc3 := castPair(t, env, "alice")
	_ = caster3.Control().Send(&wire.Cast{BroadcastID: bid3, ReceiverDeviceID: rcv3.ID})
	awaitOffer(t, rc3)
	_ = caster3.Control().Send(&wire.BroadcastEnd{BroadcastID: bid3})
	eventually(t, "the table to empty", func() bool { return env.Engine.PendingCasts() == 0 })
	// a revoke of the receiver clears too
	caster4, bid4, rcv4, rc4 := castPair(t, env, "alice")
	_ = caster4.Control().Send(&wire.Cast{BroadcastID: bid4, ReceiverDeviceID: rcv4.ID})
	awaitOffer(t, rc4)
	if _, err := env.Ops.RevokeDevice("alice", rcv4.ID); err != nil {
		t.Fatal(err)
	}
	eventually(t, "the table to empty", func() bool { return env.Engine.PendingCasts() == 0 })
}

func TestCastLimitsAreExhaustedThenAnUnrelatedDeviceStillWorks(t *testing.T) {
	// per device: 3 casts, then rate-limited; the stream stays open
	lc := loose()
	lc.Rates[limits.Cast] = limits.Rate{PerMinute: 1, Burst: 3}
	env := castEnv(t, func(o *testkit.Options) { o.LoginLimits = lc }, "alice", "bobby")
	caster, bid, rcv, rc := castPair(t, env, "alice")
	for i := 0; i < 3; i++ {
		_ = caster.Control().Send(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: rcv.ID})
		awaitOffer(t, rc)
	}
	r, err := caster.Control().Request(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: rcv.ID}, 3*time.Second, wire.TypeRefused, wire.TypeCastOffer, wire.TypeCastResult)
	if err != nil {
		t.Fatal(err)
	}
	refusedWith(t, r, wire.ReasonRateLimited)
	if _, err := caster.Control().Request(&wire.Keepalive{}, 2*time.Second, wire.TypeKeepalive); err != nil {
		t.Fatal("a refused cast leaves the stream open:", err)
	}
	// an unrelated device of another account is untouched by that bucket
	bc, bbid, brcv, brc := castPair(t, env, "bobby")
	_ = bc.Control().Send(&wire.Cast{BroadcastID: bbid, ReceiverDeviceID: brcv.ID})
	awaitOffer(t, brc)
}

func TestAtMostSixteenPendingOffersPerAccount(t *testing.T) {
	p := testkit.TestParams()
	p.CastPerAccount = 3
	env := castEnv(t, func(o *testkit.Options) { o.Params = &p }, "alice", "bobby")
	caster, bid, _, _ := castPair(t, env, "alice")
	var rcvs []*testkit.Device
	var rcs []*testkit.Control
	for i := 0; i < 4; i++ {
		d := env.NewDevice("alice")
		mustEnroll(t, d)
		c, err := d.OnlineQuietly()
		if err != nil {
			t.Fatal(err)
		}
		defer d.Close()
		rcvs, rcs = append(rcvs, d), append(rcs, c)
	}
	for i := 0; i < 3; i++ {
		_ = caster.Control().Send(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: rcvs[i].ID})
		awaitOffer(t, rcs[i])
	}
	r, err := caster.Control().Request(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: rcvs[3].ID}, 3*time.Second, wire.TypeRefused, wire.TypeCastOffer)
	if err != nil {
		t.Fatal(err)
	}
	refusedWith(t, r, wire.ReasonRateLimited)
	if _, err := rcs[3].Await(200*time.Millisecond, wire.TypeCastOffer); err == nil {
		t.Fatal("no offer goes out past the limit")
	}
	// another account has its own count
	bc, bbid, brcv, brc := castPair(t, env, "bobby")
	_ = bc.Control().Send(&wire.Cast{BroadcastID: bbid, ReceiverDeviceID: brcv.ID})
	awaitOffer(t, brc)
	// an answer frees a slot
	_ = rcs[0].Send(&wire.CastAnswer{BroadcastID: bid, Accepted: true})
	awaitResult(t, caster.Control())
	_ = caster.Control().Send(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: rcvs[3].ID})
	awaitOffer(t, rcs[3])
}

func TestCastIsListedInWelcomeWhenBuiltAndNothingIsWrittenToDisk(t *testing.T) {
	env := castEnv(t, nil)
	d := env.NewDevice("alice")
	en := mustEnroll(t, d)
	if !strings.Contains(strings.Join(en.Features, ","), "cast") {
		t.Fatalf("%v", en.Features)
	}
	caster, bid, rcv, rc := castPair(t, env, "alice")
	_ = caster.Control().Send(&wire.Cast{BroadcastID: bid, ReceiverDeviceID: rcv.ID})
	awaitOffer(t, rc)
	_ = rc.Send(&wire.CastAnswer{BroadcastID: bid, Accepted: true})
	awaitResult(t, caster.Control())
	env.Engine.Close()
	// no file of the data directory mentions the broadcast, a device pair or a cast
	needles := []string{bid, "broadcast-access-needle", "cast-offer", "cast-answer"}
	scanDataDir(t, env, needles)
}
