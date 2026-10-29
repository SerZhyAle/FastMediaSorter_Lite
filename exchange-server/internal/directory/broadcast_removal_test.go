package directory

import (
	"sync"
	"testing"

	"fms/exchange-server/internal/wire"
)

// Ticket 271: every broadcast that leaves the directory is reported once, after the lock, with why it
// ended and how the `changed` carrying its removal was fanned out - so a joint session can be read from
// the server log alone.

type removalLog struct {
	mu  sync.Mutex
	got []BroadcastRemoval
}

func (l *removalLog) take() []BroadcastRemoval {
	l.mu.Lock()
	defer l.mu.Unlock()
	out := l.got
	l.got = nil
	return out
}

func newRemovalEnv(t *testing.T) (*env, *removalLog) {
	t.Helper()
	rl := &removalLog{}
	e := newEnv(t, func(o *Options) {
		o.Hooks.BroadcastRemoved = func(account string, r BroadcastRemoval) {
			if account != alice {
				t.Errorf("removal reported for account %q", account)
			}
			rl.mu.Lock()
			rl.got = append(rl.got, r)
			rl.mu.Unlock()
		}
	})
	return e, rl
}

func TestBroadcastRemovalCountsOnlyGuestsWhoTookThatBroadcastRemoval(t *testing.T) {
	e, rl := newRemovalEnv(t)
	prod, cons := e.online(alice, devA1), e.online(alice, devA2)
	e.d.Subscribe(alice, devA2, cons)
	guest := e.online(bob, devB1)
	e.d.Subscribe(bob, devB1, guest)
	e.d.DeviceEnrolled(DeviceInfo{ID: devB2, Account: bob, Name: "Other guest device"})
	full := e.online(bob, devB2)
	e.d.Subscribe(bob, devB2, full)
	if err := full.Send(&wire.Changed{}); err != nil {
		t.Fatal(err)
	}
	full.limit = 1 // this stream cannot take the removal
	for _, id := range []string{broB1, broB2} {
		if _, err := e.d.BroadcastStart(alice, devA1, broadcast(id, ""), nil); err != nil {
			t.Fatal(err)
		}
	}
	e.d.SetGrants(&fakeGrants{all: []Grant{{ID: grantG1, Owner: alice, Guest: bob, Target: broB1, Broadcast: true}}})
	cons.reset()
	guest.reset()

	// Both broadcasts leave in one transaction, but only the first was granted to the guest.
	e.d.Offline(alice, devA1, prod)
	got := rl.take()
	if len(got) != 2 {
		t.Fatalf("want one report per removed broadcast, got %+v", got)
	}
	changes := cons.changes(t)
	if len(changes) != 1 || len(changes[0].Removals.BroadcastIDs) != 2 {
		t.Fatalf("want one owner change removing both broadcasts, got %+v", changes)
	}
	for i, r := range got {
		wantGuest := 0
		if i == 0 {
			wantGuest = 1
		}
		if r.BroadcastID != []string{broB1, broB2}[i] || r.Why != EndOffline || r.Revision != changes[0].Revision ||
			r.Subscribers != 1 || r.Queued != 1 || r.Dropped != 0 || r.GuestQueued != wantGuest {
			t.Fatalf("removal %d: %+v, want guest-queued=%d", i, r, wantGuest)
		}
	}
	guestChanges := guest.changes(t)
	if len(guestChanges) != 1 || len(guestChanges[0].Removals.BroadcastIDs) != 1 || guestChanges[0].Removals.BroadcastIDs[0] != broB1 {
		t.Fatalf("guest must receive only its granted broadcast's removal: %+v", guestChanges)
	}
	if !full.isClosed() {
		t.Fatal("the full guest stream was not dropped")
	}
	// Repeated cleanup must report nothing.
	e.d.BroadcastEnd(alice, devA1, broB1)
	e.d.Offline(alice, devA1, prod)
	if got := rl.take(); len(got) != 0 {
		t.Fatalf("duplicate removal reports: %+v", got)
	}
}

func TestBroadcastRemovalReportsWhyAndFanOut(t *testing.T) {
	e, rl := newRemovalEnv(t)
	prod, cons := e.online(alice, devA1), e.online(alice, devA2)
	e.d.Subscribe(alice, devA1, prod)
	e.d.Subscribe(alice, devA2, cons)
	for _, id := range []string{broB1, broB2} {
		if _, err := e.d.BroadcastStart(alice, devA1, broadcast(id, ""), nil); err != nil {
			t.Fatal(err)
		}
	}
	if got := rl.take(); len(got) != 0 {
		t.Fatalf("a start reported a removal: %+v", got)
	}

	// the envelope: both subscribers take the changed
	e.d.BroadcastEnd(alice, devA1, broB1)
	got := rl.take()
	if len(got) != 1 {
		t.Fatalf("broadcast-end reported %d removals", len(got))
	}
	r := got[0]
	if r.BroadcastID != broB1 || r.DeviceID != devA1 || r.Why != EndBroadcastEnd || r.Subscribers != 2 || r.Queued != 2 || r.Dropped != 0 {
		t.Fatalf("broadcast-end: %+v", r)
	}
	last := r.Revision
	if cs := cons.changes(t); len(cs) == 0 || cs[len(cs)-1].Revision != last || len(cs[len(cs)-1].Removals.BroadcastIDs) != 1 {
		t.Fatalf("the reported revision is not the changed the consumer took (%d)", last)
	}

	// an end that is not the device's own is ignored and reports nothing
	e.d.BroadcastEnd(alice, devA2, broB2)
	if got := rl.take(); len(got) != 0 {
		t.Fatalf("a foreign end reported: %+v", got)
	}

	// a subscriber that cannot take the push is counted as dropped
	cons.mu.Lock()
	cons.closed = true
	cons.mu.Unlock()

	// the producer's control stream ends without broadcast-end: the broadcast ends as an offline, and the
	// producer's own stream is no subscriber any more by then
	e.d.Offline(alice, devA1, prod)
	got = rl.take()
	if len(got) != 1 {
		t.Fatalf("offline reported %d removals", len(got))
	}
	r = got[0]
	if r.BroadcastID != broB2 || r.Why != EndOffline || r.Subscribers != 1 || r.Queued != 0 || r.Dropped != 1 || r.Revision <= last {
		t.Fatalf("offline: %+v", r)
	}
}

func TestBroadcastRemovalReasonsOfTheOtherPaths(t *testing.T) {
	e, rl := newRemovalEnv(t)
	e.online(alice, devA1)
	start := func(id string) {
		t.Helper()
		if _, err := e.d.BroadcastStart(alice, devA1, broadcast(id, ""), nil); err != nil {
			t.Fatal(err)
		}
	}
	expect := func(why string) {
		t.Helper()
		got := rl.take()
		if len(got) != 1 || got[0].Why != why || got[0].Subscribers != 0 || got[0].Queued != 0 {
			t.Fatalf("want one %q removal with nobody subscribed, got %+v", why, got)
		}
	}

	start(broB1)
	e.d.BroadcastEndWhy(alice, devA1, broB1, EndUploadEnd)
	expect(EndUploadEnd)

	start(broB1)
	if err := e.d.EndBroadcast(alice, broB1); err != nil {
		t.Fatal(err)
	}
	expect(EndAdmin)

	start(broB1)
	e.d.WithdrawAll(alice)
	expect(EndWithdrawn)

	start(broB1)
	if !e.d.RevokeDevice(alice, devA1) {
		t.Fatal("revoke")
	}
	expect(EndRevoked)
}

func TestBroadcastRemovalHookRunsAfterUnlockAndTeardown(t *testing.T) {
	var d *Dir
	gone, removed := 0, 0
	e := newEnv(t, func(o *Options) {
		o.Hooks.BroadcastGone = func(account, id, device string) {
			if d.Counts().Broadcasts != 0 {
				t.Error("the record must leave before teardown")
			}
			gone++
		}
		o.Hooks.BroadcastRemoved = func(account string, r BroadcastRemoval) {
			if d.Counts().Broadcasts != 0 || gone != 1 {
				t.Error("the report must follow unlock and teardown")
			}
			removed++
		}
	})
	d = e.d
	e.online(alice, devA1)
	if _, err := d.BroadcastStart(alice, devA1, broadcast(broB1, ""), nil); err != nil {
		t.Fatal(err)
	}
	d.BroadcastEnd(alice, devA1, broB1)
	d.BroadcastEnd(alice, devA1, broB1)
	if gone != 1 || removed != 1 {
		t.Fatalf("want one teardown and report, got %d and %d", gone, removed)
	}
}
