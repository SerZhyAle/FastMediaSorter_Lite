package directory

import (
	"bytes"
	"errors"
	"testing"

	"fms/exchange-server/internal/wire"
)

// Item BF and item DV: a protected broadcast's listener verifiers live in memory with the broadcast. An update
// that carries the member replaces the list wholesale, an empty list included; one that does not leaves it.

// pair makes a verifier for a user whose password is the user name; one iteration is enough here, the directory
// never hashes anything.
func pair(user string) wire.ListenerVerifier {
	salt := bytes.Repeat([]byte{user[0]}, wire.VerifierSaltLen)
	return wire.NewListenerVerifier(user, "pw-"+user, salt, 1)
}

func list(users ...string) *[]wire.ListenerVerifier {
	l := make([]wire.ListenerVerifier, 0, len(users))
	for _, u := range users {
		l = append(l, pair(u))
	}
	return &l
}

func verifierUsers(t *testing.T, e *env, id string) []string {
	t.Helper()
	tg, ok := e.d.ResolveBroadcast(id)
	if !ok {
		t.Fatal("the broadcast is not live")
	}
	var out []string
	for _, v := range tg.Verifiers {
		out = append(out, v.User)
	}
	return out
}

func sameUsers(got []string, want ...string) bool {
	if len(got) != len(want) {
		return false
	}
	for i := range got {
		if got[i] != want[i] {
			return false
		}
	}
	return true
}

func TestAStartWithoutTheMemberHoldsNoVerifier(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	if _, err := e.d.BroadcastStartWith(alice, devA1, broadcast(broB1, ""), nil, nil); err != nil {
		t.Fatal(err)
	}
	if got := verifierUsers(t, e, broB1); len(got) != 0 {
		t.Fatalf("no member on a start is no pair to check: %v", got)
	}
	// the plain entry point of every earlier caller means the same
	if _, err := e.d.BroadcastStart(alice, devA1, broadcast(broB2, ""), nil); err != nil {
		t.Fatal(err)
	}
	if got := verifierUsers(t, e, broB2); len(got) != 0 {
		t.Fatalf("%v", got)
	}
}

func TestAStartWithAListHoldsItInOrderAndAnEmptyListHoldsNothing(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	if _, err := e.d.BroadcastStartWith(alice, devA1, broadcast(broB1, ""), nil, list("own", "guest", "link")); err != nil {
		t.Fatal(err)
	}
	if got := verifierUsers(t, e, broB1); !sameUsers(got, "own", "guest", "link") {
		t.Fatalf("%v", got)
	}
	tg, _ := e.d.ResolveBroadcast(broB1)
	if len(tg.Verifiers[0].Salt) != wire.VerifierSaltLen || len(tg.Verifiers[0].Hash) != wire.VerifierHashLen {
		t.Fatal("the target carries the decoded salt and hash")
	}
	if _, err := e.d.BroadcastStartWith(alice, devA1, broadcast(broB2, ""), nil, list()); err != nil {
		t.Fatal(err)
	}
	if got := verifierUsers(t, e, broB2); len(got) != 0 {
		t.Fatalf("an empty list holds nothing: %v", got)
	}
}

func TestAnUpdateWithAListReplacesItAndWithoutTheMemberLeavesIt(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	if _, err := e.d.BroadcastStartWith(alice, devA1, broadcast(broB1, ""), nil, list("own", "link")); err != nil {
		t.Fatal(err)
	}
	// no member: the list stays, the record moves on
	if err := e.d.BroadcastUpdateWith(alice, devA1, broadcast(broB1, `,"title":"Second"`), nil, nil); err != nil {
		t.Fatal(err)
	}
	if got := verifierUsers(t, e, broB1); !sameUsers(got, "own", "link") {
		t.Fatalf("an update without the member must leave the list: %v", got)
	}
	// the older entry point is an update without the member too
	if err := e.d.BroadcastUpdate(alice, devA1, broadcast(broB1, `,"title":"Third"`), nil); err != nil {
		t.Fatal(err)
	}
	if got := verifierUsers(t, e, broB1); !sameUsers(got, "own", "link") {
		t.Fatalf("%v", got)
	}
	// a new list replaces the old one wholesale: this is how one pair is revoked alone
	if err := e.d.BroadcastUpdateWith(alice, devA1, broadcast(broB1, ""), nil, list("own")); err != nil {
		t.Fatal(err)
	}
	if got := verifierUsers(t, e, broB1); !sameUsers(got, "own") {
		t.Fatalf("%v", got)
	}
	// an empty list opens the broadcast
	if err := e.d.BroadcastUpdateWith(alice, devA1, broadcast(broB1, ""), nil, list()); err != nil {
		t.Fatal(err)
	}
	if got := verifierUsers(t, e, broB1); len(got) != 0 {
		t.Fatalf("an empty list on an update checks nobody: %v", got)
	}
	// and a later update without the member does not bring the old pairs back
	if err := e.d.BroadcastUpdateWith(alice, devA1, broadcast(broB1, ""), nil, nil); err != nil {
		t.Fatal(err)
	}
	if got := verifierUsers(t, e, broB1); len(got) != 0 {
		t.Fatalf("%v", got)
	}
}

func TestAStartOfAHeldIdReplacesTheListAndMalformedOnesChangeNothing(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	e.online(alice, devA2)
	if _, err := e.d.BroadcastStartWith(alice, devA1, broadcast(broB1, ""), nil, list("own")); err != nil {
		t.Fatal(err)
	}
	bad := []wire.ListenerVerifier{{User: "own", Salt: "short", Hash: "short"}}
	if _, err := e.d.BroadcastStartWith(alice, devA1, broadcast(broB1, ""), nil, &bad); !errors.Is(err, ErrMalformed) {
		t.Fatalf("a malformed start: %v", err)
	}
	if err := e.d.BroadcastUpdateWith(alice, devA1, broadcast(broB1, ""), nil, &bad); !errors.Is(err, ErrMalformed) {
		t.Fatalf("a malformed update: %v", err)
	}
	if got := verifierUsers(t, e, broB1); !sameUsers(got, "own") {
		t.Fatalf("a refused member must change nothing: %v", got)
	}
	// another device of the account may not touch the list of a broadcast it does not own
	if err := e.d.BroadcastUpdateWith(alice, devA2, broadcast(broB1, ""), nil, list()); err != nil {
		t.Fatal(err)
	}
	if got := verifierUsers(t, e, broB1); !sameUsers(got, "own") {
		t.Fatalf("a foreign update changed the list: %v", got)
	}
	// the owner starting the same id again is a new announcement: its member (here none) is the whole truth
	if _, err := e.d.BroadcastStartWith(alice, devA1, broadcast(broB1, `,"title":"Again"`), nil, nil); err != nil {
		t.Fatal(err)
	}
	if got := verifierUsers(t, e, broB1); len(got) != 0 {
		t.Fatalf("a restart without the member holds no pair: %v", got)
	}
}

func TestVerifiersEndWithTheBroadcastAndNeverReachTheDirectory(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	v := list("own")
	if _, err := e.d.BroadcastStartWith(alice, devA1, broadcast(broB1, ""), nil, v); err != nil {
		t.Fatal(err)
	}
	// the listing is the record as sent: neither the salt nor the hash of an entry is in it
	for _, raw := range mustBroadcasts(e, alice) {
		if bytes.Contains(raw, []byte((*v)[0].Salt)) || bytes.Contains(raw, []byte((*v)[0].Hash)) || bytes.Contains(raw, []byte("verifiers")) {
			t.Fatalf("the directory record carries a verifier: %s", raw)
		}
	}
	e.d.BroadcastEnd(alice, devA1, broB1)
	if _, ok := e.d.ResolveBroadcast(broB1); ok {
		t.Fatal("an ended broadcast resolved")
	}
	// the same id announced again by the same device starts with a clean slate
	if _, err := e.d.BroadcastStartWith(alice, devA1, broadcast(broB1, ""), nil, nil); err != nil {
		t.Fatal(err)
	}
	if got := verifierUsers(t, e, broB1); len(got) != 0 {
		t.Fatalf("an ended broadcast's verifiers came back: %v", got)
	}
}
