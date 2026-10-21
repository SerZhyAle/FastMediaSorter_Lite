package rendezvous_test

import (
	"sync/atomic"
	"testing"
	"time"

	"fms/exchange-server/internal/rendezvous"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

func TestConcurrentBroadcastsFromOneDeviceKeepSelectionAndRelayIsolated(t *testing.T) {
	env := relayEnv(t, func(c *rendezvous.RelayConfig) { c.WriteTimeout = 200 * time.Millisecond })
	p, first, up1 := startBroadcast(t, env, "alice", nil, "audio/aac")
	second, err := p.StartBroadcast("AUDIO_ONLY", nil)
	if err != nil {
		t.Fatal(err)
	}
	up2, _, err := p.Upload(second, "audio/aac")
	if err != nil || up2 == nil {
		t.Fatalf("second upload: %v", err)
	}
	defer up2.Close()
	dir, err := p.Control().ListDirectory()
	if err != nil || len(dir.Broadcasts) != 2 {
		t.Fatalf("concurrent directory: %+v %v", dir, err)
	}
	const size = 256 << 10
	if err := up1.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(0, size)); err != nil {
		t.Fatal(err)
	}
	if err := up2.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(1000, size)); err != nil {
		t.Fatal(err)
	}
	waitBytesIn(t, env, first, size)
	waitBytesIn(t, env, second, size)
	_ = listen(t, env, first) // Deliberately never read this listener's body.
	good := listen(t, env, second)
	var received atomic.Uint32
	done := make(chan struct{})
	go func() {
		defer close(done)
		for {
			seq, err := good.ReadSeq(size, 5*time.Second)
			if err != nil {
				return
			}
			if seq < 1000 {
				received.Store(1)
				return
			} // another broadcast's bytes
			received.Store(seq)
		}
	}()
	defer func() { good.Close(); <-done }()
	for i := 1; i <= 160; i++ {
		if err := up1.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(uint32(i), size)); err != nil {
			t.Fatal(err)
		}
		if err := up2.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(uint32(1000+i), size)); err != nil {
			t.Fatal(err)
		}
	}
	eventually(t, "the independent listener to reach its own last chunk", func() bool { return received.Load() == 1160 })
	eventually(t, "the stalled listener to be removed", func() bool { return env.Relay.Listeners(first) == 0 })
	if err := p.EndBroadcast(first); err != nil {
		t.Fatal(err)
	}
	eventually(t, "only the selected broadcast to end", func() bool { return env.Dir.Counts().Broadcasts == 1 })
	dir, err = p.Control().ListDirectory()
	if err != nil || len(dir.Broadcasts) != 1 {
		t.Fatalf("resnapshot: %+v %v", dir, err)
	}
	if err := up2.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(2000, size)); err != nil {
		t.Fatal(err)
	}
	eventually(t, "the remaining broadcast to keep playing", func() bool { return received.Load() == 2000 })
}
