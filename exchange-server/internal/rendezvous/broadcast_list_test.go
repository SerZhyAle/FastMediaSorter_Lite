package rendezvous_test

import (
	"encoding/json"
	"net"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/directory"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Ticket 244 (devex-delta G3): the local `broadcast.list` is `Ops.ListBroadcasts`, and its listenersCount is
// what the relay and the tunnels hold at that moment - the same number `maxListeners` is measured against.

func listedBroadcast(t *testing.T, env *testkit.Env, id string) (directory.BroadcastView, bool) {
	t.Helper()
	for _, v := range env.Ops.ListBroadcasts("alice", "") {
		if v.BroadcastID == id {
			return v, true
		}
	}
	return directory.BroadcastView{}, false
}

func TestBroadcastListShowsTheRelayListenersThatAreAttachedNow(t *testing.T) {
	env := relayEnv(t, nil)
	_, id, up := startBroadcast(t, env, "alice", nil, "audio/aac")
	_ = up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(1, 16))
	waitBytesIn(t, env, id, 16)

	count := func() int {
		v, ok := listedBroadcast(t, env, id)
		if !ok {
			t.Fatalf("the live broadcast %s is not listed", id)
		}
		return v.ListenersCount
	}
	if n := count(); n != 0 {
		t.Fatalf("no listener attached, the list says %d", n)
	}
	l1, l2, l3 := listen(t, env, id), listen(t, env, id), listen(t, env, id)
	for _, l := range []*testkit.Listening{l1, l2, l3} {
		if _, err := l.ReadSeq(16, 3*time.Second); err != nil {
			t.Fatal(err)
		}
	}
	eventually(t, "the list to show three listeners", func() bool { return count() == 3 })
	if env.Relay.Listeners(id) != 3 {
		t.Fatalf("the relay itself holds %d", env.Relay.Listeners(id))
	}

	l1.Close()
	eventually(t, "the list to show two listeners", func() bool { return count() == 2 })
	l2.Close()
	l3.Close()
	eventually(t, "the list to show none again", func() bool { return count() == 0 })

	// the answer on the wire of the local channel: the count is a member, the bitrate is absent because
	// the relay measures bytes over a life, not a rate
	l4 := listen(t, env, id)
	if _, err := l4.ReadSeq(16, 3*time.Second); err != nil {
		t.Fatal(err)
	}
	eventually(t, "the list to show one listener", func() bool { return count() == 1 })
	raw, err := json.Marshal(env.Ops.ListBroadcasts("alice", ""))
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(raw), `"listenersCount":1`) || strings.Contains(string(raw), "bitrateEstimateKbps") {
		t.Fatalf("the operator's list: %s", raw)
	}
}

func TestBroadcastListCountsTunnelsWithTheRelayListenersAndSurvivesAnEnd(t *testing.T) {
	env := relayEnv(t, nil)
	three := 3
	hold := make(chan struct{})
	p := env.NewProducer("alice", "x")
	p.Handler = func(c net.Conn) { <-hold; _ = c.Close() }
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	defer p.Close()
	defer close(hold)
	id, err := p.StartBroadcast("AUDIO_ONLY", &three)
	if err != nil {
		t.Fatal(err)
	}
	up, _, err := p.Upload(id, "audio/aac")
	if err != nil || up == nil {
		t.Fatal(err)
	}
	defer up.Close()
	_ = up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(1, 16))
	waitBytesIn(t, env, id, 16)

	consumer := env.NewDevice("alice")
	mustEnroll(t, consumer)
	tun, _, err := consumer.Connect(&wire.Connect{BroadcastID: id, Scheme: "http"})
	if err != nil || tun == nil {
		t.Fatalf("the tunnel: %v", err)
	}
	defer tun.Close()
	l1 := listen(t, env, id)
	if _, err := l1.ReadSeq(16, 3*time.Second); err != nil {
		t.Fatal(err)
	}
	eventually(t, "a tunnel and a relay listener to be listed as two", func() bool {
		v, ok := listedBroadcast(t, env, id)
		return ok && v.ListenersCount == 2 && v.MaxListeners == 3
	})

	// the operator ends it: the list no longer has it, and asking again never panics
	if err := env.Ops.EndBroadcast("alice", id); err != nil {
		t.Fatal(err)
	}
	if _, ok := listedBroadcast(t, env, id); ok {
		t.Fatal("an ended broadcast is still listed")
	}
	for i := 0; i < 3; i++ {
		_ = env.Ops.ListBroadcasts("alice", "")
	}
	eventually(t, "the relay to let go of the ended broadcast", func() bool { return env.Relay.Listeners(id) == 0 })
}
