package cast

import (
	"errors"
	"testing"
	"time"
)

type clock struct{ t time.Time }

func (c *clock) Now() time.Time          { return c.t }
func (c *clock) Advance(d time.Duration) { c.t = c.t.Add(d) }

func newClock() *clock { return &clock{t: time.Date(2026, 10, 7, 12, 0, 0, 0, time.UTC)} }

func TestAnOfferIsAnsweredOnceAndOnlyBeforeItsTime(t *testing.T) {
	c := newClock()
	tb := New(c.Now, 60*time.Second, 16)
	if err := tb.Add("alice", "b1", "rcv", "cst"); err != nil {
		t.Fatal(err)
	}
	if got := tb.Answer("b1", "rcv"); len(got) != 1 || got[0].Caster != "cst" || got[0].Account != "alice" {
		t.Fatalf("%+v", got)
	}
	if got := tb.Answer("b1", "rcv"); len(got) != 0 {
		t.Fatal("an offer is answered once")
	}
	if tb.Len() != 0 {
		t.Fatal("the table is empty after the answer")
	}
	// a late answer is dropped, and Expire reports the timeout
	_ = tb.Add("alice", "b1", "rcv", "cst")
	c.Advance(61 * time.Second)
	if got := tb.Answer("b1", "rcv"); len(got) != 0 {
		t.Fatalf("a late answer must be dropped: %+v", got)
	}
	if dead := tb.Expire(); len(dead) != 1 || dead[0].Receiver != "rcv" {
		t.Fatalf("%+v", dead)
	}
	if tb.Len() != 0 {
		t.Fatal("empty after expiry")
	}
}

func TestTheTimeoutIsExactlyTheWorkingDefault(t *testing.T) {
	c := newClock()
	tb := New(c.Now, 0, 0)
	if tb.Timeout() != 60*time.Second {
		t.Fatalf("%v", tb.Timeout())
	}
	_ = tb.Add("a", "b", "r", "c")
	c.Advance(59 * time.Second)
	if len(tb.Expire()) != 0 {
		t.Fatal("not yet")
	}
	c.Advance(time.Second)
	if len(tb.Expire()) != 1 {
		t.Fatal("at 60 s")
	}
}

func TestAtMostSixteenPerAccountThenCapacityAndAnotherAccountIsUntouched(t *testing.T) {
	c := newClock()
	tb := New(c.Now, time.Minute, 0)
	for i := 0; i < 16; i++ {
		if err := tb.Add("alice", "b", string(rune('a'+i)), "c"); err != nil {
			t.Fatalf("%d: %v", i, err)
		}
	}
	if err := tb.Add("alice", "b", "zz", "c"); !errors.Is(err, ErrCapacity) {
		t.Fatalf("%v", err)
	}
	if err := tb.Add("bobby", "b2", "x", "y"); err != nil {
		t.Fatalf("another account is not charged: %v", err)
	}
	// the same key again replaces, it does not count twice, and it restarts the timeout
	c.Advance(30 * time.Second)
	if err := tb.Add("alice", "b", "a", "c"); err != nil {
		t.Fatalf("a repeated offer: %v", err)
	}
	c.Advance(40 * time.Second)
	// 15 untouched offers and bobby's expire at 60 s; the repeated one restarted at 30 s and lives until 90 s
	if dead := tb.Expire(); len(dead) != 16 {
		t.Fatalf("%d expired", len(dead))
	}
	if tb.Len() != 1 {
		t.Fatalf("the restarted offer remains: %d", tb.Len())
	}
	if err := tb.Add("alice", "b", "zz", "c"); err != nil {
		t.Fatalf("a freed slot is usable: %v", err)
	}
}

func TestEntriesDieWithEitherStreamAndWithTheBroadcast(t *testing.T) {
	c := newClock()
	tb := New(c.Now, time.Minute, 0)
	_ = tb.Add("alice", "b1", "r1", "c1")
	_ = tb.Add("alice", "b1", "r2", "c1")
	_ = tb.Add("alice", "b2", "r1", "c2")
	_ = tb.Add("alice", "b3", "r3", "c3")

	// the caster c1 goes: its offers are dropped, nobody is told
	if got := tb.DropDevice("c1"); len(got) != 0 {
		t.Fatalf("a dropped caster leaves nothing to report: %+v", got)
	}
	if tb.Len() != 2 {
		t.Fatalf("%d", tb.Len())
	}
	// the receiver r1 goes: the offer made to it is returned, so its caster can be told
	if got := tb.DropDevice("r1"); len(got) != 1 || got[0].Caster != "c2" {
		t.Fatalf("%+v", got)
	}
	// the broadcast ends
	tb.DropBroadcast("b3")
	if tb.Len() != 0 {
		t.Fatalf("%d left", tb.Len())
	}
}

func TestAnAnswerGoesToEveryCasterThatOfferedThatBroadcastToThatReceiver(t *testing.T) {
	c := newClock()
	tb := New(c.Now, time.Minute, 0)
	_ = tb.Add("alice", "b1", "r1", "c1")
	_ = tb.Add("alice", "b1", "r1", "c2")
	_ = tb.Add("alice", "b1", "r2", "c1")
	got := tb.Answer("b1", "r1")
	if len(got) != 2 || got[0].Caster != "c1" || got[1].Caster != "c2" {
		t.Fatalf("%+v", got)
	}
	if tb.Len() != 1 {
		t.Fatal("the offer to another receiver stays")
	}
}
