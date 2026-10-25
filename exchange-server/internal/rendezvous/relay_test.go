package rendezvous_test

import (
	"bytes"
	"errors"
	"fmt"
	"io"
	"net"
	"strings"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"fms/exchange-server/internal/rendezvous"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Ticket 165: the fan-out relay on the wire. The window rules themselves (the ring, the cursors, the
// byte cap) are proved with an injected clock in internal/relay; here the real server, real TLS and a
// real HTTP client prove the handlers around them.

func relayEnv(t *testing.T, mutate func(*rendezvous.RelayConfig), logins ...string) *testkit.Env {
	t.Helper()
	if len(logins) == 0 {
		logins = []string{"alice"}
	}
	o := looseOpts(logins...)
	o.WithRelay = true
	o.PublicEndpoint = "203.0.113.5:22022"
	o.RelayConfig = mutate
	return testkit.StartServer(t, o)
}

// startBroadcast enrolls a broadcaster, starts a broadcast and attaches its upload.
func startBroadcast(t *testing.T, env *testkit.Env, login string, maxListeners *int, contentType string) (*testkit.Producer, string, *testkit.Conn) {
	t.Helper()
	p := env.NewProducer(login, "ssh-secret")
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(p.Close)
	id, err := p.StartBroadcast("AUDIO_ONLY", maxListeners)
	if err != nil {
		t.Fatal(err)
	}
	up, ans, err := p.Upload(id, contentType)
	if err != nil || up == nil {
		t.Fatalf("upload: %v %+v", err, ans)
	}
	t.Cleanup(func() { _ = up.Close() })
	return p, id, up
}

func waitBytesIn(t *testing.T, env *testkit.Env, id string, n int64) {
	t.Helper()
	eventually(t, fmt.Sprintf("%d bytes to reach the relay", n), func() bool {
		_, _, _, in, ok := env.Relay.Held(id)
		return ok && in >= n
	})
}

func listen(t *testing.T, env *testkit.Env, id string) *testkit.Listening {
	t.Helper()
	l, err := testkit.DialListen(env.Addr, env.Fingerprint, id)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(l.Close)
	return l
}

func statusOf(t *testing.T, env *testkit.Env, request string, method string) (int, map[string]string) {
	t.Helper()
	l, err := testkit.DialRaw(env.Addr, env.Fingerprint, request, method)
	if err != nil {
		t.Fatal(err)
	}
	defer l.Close()
	h := map[string]string{}
	for k := range l.Resp.Header {
		h[k] = l.Resp.Header.Get(k)
	}
	return l.Resp.StatusCode, h
}

func getRequest(id string) string {
	return "GET " + wire.ListenPath(id) + " HTTP/1.1\r\nHost: x\r\n\r\n"
}

func TestAListenerGetsTheHeadersTheHeaderChunkAndStartsAtAJoinableChunk(t *testing.T) {
	env := relayEnv(t, nil)
	_, id, up := startBroadcast(t, env, "alice", nil, "video/mp2t")
	must := func(err error) {
		t.Helper()
		if err != nil {
			t.Fatal(err)
		}
	}
	must(up.WriteChunk(wire.ChunkHeader, []byte("PATPMT")))
	must(up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(1, 32)))
	must(up.WriteChunk(0, testkit.SeqPayload(2, 32)))
	must(up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(3, 32)))
	must(up.WriteChunk(0, testkit.SeqPayload(4, 32)))
	waitBytesIn(t, env, id, 6+4*32)

	l := listen(t, env, id)
	r := l.Resp
	if r.StatusCode != 200 || r.Header.Get("Content-Type") != "video/mp2t" || r.Header.Get("Cache-Control") != "no-cache" ||
		r.Header.Get("Accept-Ranges") != "none" || r.Header.Get("Icy-Metaint") != "" {
		t.Fatalf("response: %d %v", r.StatusCode, r.Header)
	}
	if len(r.TransferEncoding) != 1 || r.TransferEncoding[0] != "chunked" {
		t.Fatalf("the body is chunked: %v", r.TransferEncoding)
	}
	hdr, err := l.ReadFull(6, 3*time.Second)
	if err != nil || string(hdr) != "PATPMT" {
		t.Fatalf("the body starts with the header chunk: %q %v", hdr, err)
	}
	if seq, err := l.ReadSeq(32, 3*time.Second); err != nil || seq != 3 {
		t.Fatalf("then it starts at the newest joinable chunk, 3: %d %v", seq, err)
	}
	if seq, _ := l.ReadSeq(32, 3*time.Second); seq != 4 {
		t.Fatalf("and follows in order: %d", seq)
	}
	must(up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(5, 32)))
	if seq, _ := l.ReadSeq(32, 3*time.Second); seq != 5 {
		t.Fatalf("%d", seq)
	}
	// a header replaced mid-stream is not replayed to a listener already past the start
	must(up.WriteChunk(wire.ChunkHeader, []byte("PATPM2")))
	must(up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(6, 32)))
	if seq, _ := l.ReadSeq(32, 3*time.Second); seq != 6 {
		t.Fatalf("the replaced header must not reach a running listener (it read %d next)", seq)
	}
	// and a new listener gets the new one
	l2 := listen(t, env, id)
	if hdr, _ := l2.ReadFull(6, 3*time.Second); string(hdr) != "PATPM2" {
		t.Fatalf("%q", hdr)
	}
}

func TestListenOutcomesAreTheOnesOfTheTicket(t *testing.T) {
	env := relayEnv(t, nil)
	_, id, up := startBroadcast(t, env, "alice", nil, "audio/aac")
	_ = up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(1, 16))
	waitBytesIn(t, env, id, 16)

	// an unknown id is 404 after the minimum delay (60 ms in the test parameters)
	start := time.Now()
	if code, _ := statusOf(t, env, getRequest(env.NewID()), "GET"); code != 404 {
		t.Fatalf("unknown id: %d", code)
	}
	if d := time.Since(start); d < env.Params.UnavailableDelay-10*time.Millisecond {
		t.Fatalf("a 404 came after %v, before the minimum delay %v", d, env.Params.UnavailableDelay)
	}
	// a path that is not the listen URL is 404; HEAD and any other method 405 and cost no slot
	for _, req := range []string{"GET /other HTTP/1.1\r\nHost: x\r\n\r\n", "GET /v2/b/short/stream HTTP/1.1\r\nHost: x\r\n\r\n",
		"GET /v2/b/" + id + "/stream/x HTTP/1.1\r\nHost: x\r\n\r\n"} {
		if code, _ := statusOf(t, env, req, "GET"); code != 404 {
			t.Fatalf("%q: %d", req, code)
		}
	}
	if code, h := statusOf(t, env, "HEAD "+wire.ListenPath(id)+" HTTP/1.1\r\nHost: x\r\n\r\n", "HEAD"); code != 405 || h["Allow"] != "GET" {
		t.Fatalf("HEAD: %d %v", code, h)
	}
	if code, _ := statusOf(t, env, "POST "+wire.ListenPath(id)+" HTTP/1.1\r\nHost: x\r\nContent-Length: 0\r\n\r\n", "POST"); code != 405 {
		t.Fatalf("POST: %d", code)
	}
	if env.Relay.Listeners(id) != 0 {
		t.Fatal("a refused request costs no listener slot")
	}
	// HTTP/1.1 only: the body is chunked
	if code, _ := statusOf(t, env, "GET "+wire.ListenPath(id)+" HTTP/1.0\r\n\r\n", "GET"); code != 505 {
		t.Fatalf("HTTP/1.0: %d", code)
	}
	// a Range header is ignored: 200 with the whole live stream
	l, err := testkit.DialRaw(env.Addr, env.Fingerprint, "GET "+wire.ListenPath(id)+" HTTP/1.1\r\nHost: x\r\nRange: bytes=100-\r\n\r\n", "GET")
	if err != nil {
		t.Fatal(err)
	}
	defer l.Close()
	if l.Resp.StatusCode != 200 || l.Resp.Header.Get("Content-Range") != "" || l.Resp.Header.Get("Accept-Ranges") != "none" {
		t.Fatalf("Range: %d %v", l.Resp.StatusCode, l.Resp.Header)
	}
	if seq, err := l.ReadSeq(16, 3*time.Second); err != nil || seq != 1 {
		t.Fatalf("%d %v", seq, err)
	}
}

func TestALiveBroadcastWithNoUploadYetIs503RetryAfter1(t *testing.T) {
	env := relayEnv(t, nil)
	p := env.NewProducer("alice", "x")
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	defer p.Close()
	id, err := p.StartBroadcast("AUDIO_ONLY", nil)
	if err != nil {
		t.Fatal(err)
	}
	code, h := statusOf(t, env, getRequest(id), "GET")
	if code != 503 || h["Retry-After"] != "1" {
		t.Fatalf("%d %v", code, h)
	}
	// the producer uploads and the same URL answers 200
	up, _, err := p.Upload(id, "audio/aac")
	if err != nil || up == nil {
		t.Fatal(err)
	}
	defer up.Close()
	_ = up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(1, 16))
	waitBytesIn(t, env, id, 16)
	if code, _ := statusOf(t, env, getRequest(id), "GET"); code != 200 {
		t.Fatalf("%d", code)
	}
}

func TestUploadRefusals(t *testing.T) {
	env := relayEnv(t, nil, "alice", "bobby")
	pa, ida, _ := startBroadcast(t, env, "alice", nil, "audio/aac")
	// another device of the SAME account does not own the broadcast
	other := env.NewDevice("alice")
	mustEnroll(t, other)
	oc, err := other.Online()
	if err != nil {
		t.Fatal(err)
	}
	_ = oc
	if up, ans, err := other.Upload(ida, "audio/aac"); err != nil || up != nil {
		t.Fatalf("a device that does not own the broadcast: %v %+v", err, ans)
	} else {
		refusedWith(t, ans, wire.ReasonUnavailable)
	}
	// another account's device, and an unknown id: the same refusal
	stranger := env.NewDevice("bobby")
	mustEnroll(t, stranger)
	if _, err := stranger.Online(); err != nil {
		t.Fatal(err)
	}
	if up, ans, _ := stranger.Upload(ida, "audio/aac"); up != nil {
		t.Fatalf("%+v", ans)
	} else {
		refusedWith(t, ans, wire.ReasonUnavailable)
	}
	if up, ans, _ := pa.Upload(env.NewID(), "audio/aac"); up != nil {
		t.Fatalf("%+v", ans)
	} else {
		refusedWith(t, ans, wire.ReasonUnavailable)
	}
	// a content type that could not be copied into a header closes the stream with no answer
	c, r, err := pa.Hello()
	if err != nil || c == nil {
		t.Fatalf("%v %+v", err, r)
	}
	defer c.Close()
	_ = c.SendRaw(testkit.RawFrame(`{"schemaVersion":2,"type":"upload","broadcastId":"` + ida + `","contentType":"audio/aac\r\nX-Evil: 1"}`))
	if e, err := c.Recv(2 * time.Second); err == nil {
		t.Fatalf("an upload with a bad content type must close the stream, got %+v", e)
	}
}

func TestChunkViolationsCloseTheUploadButNotTheBroadcast(t *testing.T) {
	cases := []struct {
		name string
		raw  []byte
	}{
		{"a flag with an undefined bit", []byte{0, 0, 0, 3, 0x04, 'a', 'b'}},
		{"a length one above 1 MiB", []byte{0x00, 0x10, 0x00, 0x01, 0x01}},
		{"a length that cannot be a chunk", []byte{0xff, 0xff, 0xff, 0xff}},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			env := relayEnv(t, nil)
			_, id, up := startBroadcast(t, env, "alice", nil, "audio/aac")
			_ = up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(1, 16))
			waitBytesIn(t, env, id, 16)
			l := listen(t, env, id)
			if seq, err := l.ReadSeq(16, 3*time.Second); err != nil || seq != 1 {
				t.Fatal(seq, err)
			}
			_ = up.SendRaw(c.raw)
			// the upload stream is closed by the server...
			_ = up.SetReadDeadline(time.Now().Add(3 * time.Second))
			if _, err := up.Read(make([]byte, 16)); err == nil {
				t.Fatal("the stream must be closed")
			}
			// ...and the broadcast stays: its record is in the directory, and it ends only after the grace
			if env.Dir.Counts().Broadcasts != 1 {
				t.Fatal("a dropped upload does not end the broadcast by itself")
			}
		})
	}
}

func TestTheLargestChunkIsAcceptedAndATwoPartChunkIsReassembled(t *testing.T) {
	env := relayEnv(t, nil)
	_, id, up := startBroadcast(t, env, "alice", nil, "audio/aac")
	big := bytes.Repeat([]byte{0x5a}, wire.MaxChunkLen-1)
	buf, err := wire.AppendChunk(nil, wire.ChunkJoinable, big)
	if err != nil {
		t.Fatal(err)
	}
	// split across several writes, with the length header cut in two
	for _, part := range [][]byte{buf[:2], buf[2:5], buf[5:70000], buf[70000:]} {
		if err := up.SendRaw(part); err != nil {
			t.Fatal(err)
		}
		time.Sleep(5 * time.Millisecond)
	}
	waitBytesIn(t, env, id, int64(len(big)))
	l := listen(t, env, id)
	got, err := l.ReadFull(len(big), 10*time.Second)
	if err != nil || !bytes.Equal(got, big) {
		t.Fatalf("the 1 MiB chunk must come through intact: %v", err)
	}
}

func TestEveryWayAMediaBroadcastEndsClosesEveryListenerCleanly(t *testing.T) {
	type ender struct {
		name string
		end  func(t *testing.T, env *testkit.Env, p *testkit.Producer, id string, up *testkit.Conn)
	}
	enders := []ender{
		{"a zero-length chunk", func(t *testing.T, env *testkit.Env, p *testkit.Producer, id string, up *testkit.Conn) {
			_ = up.EndUpload()
		}},
		{"broadcast-end", func(t *testing.T, env *testkit.Env, p *testkit.Producer, id string, up *testkit.Conn) {
			_ = p.EndBroadcast(id)
		}},
		{"the loss of the control stream", func(t *testing.T, env *testkit.Env, p *testkit.Producer, id string, up *testkit.Conn) {
			p.Control().Close()
		}},
		{"a revoke", func(t *testing.T, env *testkit.Env, p *testkit.Producer, id string, up *testkit.Conn) {
			if _, err := env.Ops.RevokeDevice("alice", p.ID); err != nil {
				t.Fatal(err)
			}
		}},
	}
	for _, en := range enders {
		t.Run(en.name, func(t *testing.T) {
			env := relayEnv(t, nil)
			p, id, up := startBroadcast(t, env, "alice", nil, "audio/aac")
			_ = up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(1, 16))
			waitBytesIn(t, env, id, 16)
			var ls []*testkit.Listening
			for i := 0; i < 3; i++ {
				l := listen(t, env, id)
				if _, err := l.ReadSeq(16, 3*time.Second); err != nil {
					t.Fatal(err)
				}
				ls = append(ls, l)
			}
			if env.Relay.Listeners(id) != 3 {
				t.Fatalf("%d listeners", env.Relay.Listeners(id))
			}
			en.end(t, env, p, id, up)
			for i, l := range ls {
				rest, err := l.ReadToEnd(5 * time.Second)
				if err != nil {
					t.Fatalf("listener %d: the body must end cleanly with the final chunk, not be cut: %v (%d bytes)", i, err, len(rest))
				}
			}
			eventually(t, "the relay to drop the broadcast", func() bool { return env.Relay.Broadcasts() == 0 && env.Relay.ListenersTotal() == 0 })
			eventually(t, "the record to leave the directory", func() bool { return env.Dir.Counts().Broadcasts == 0 })
			// the id is now unknown
			if code, _ := statusOf(t, env, getRequest(id), "GET"); code != 404 {
				t.Fatalf("an ended broadcast is 404: %d", code)
			}
		})
	}
}

func TestASecondUploadReplacesTheFirstAndListenersContinueAfterTheNewHeader(t *testing.T) {
	env := relayEnv(t, nil)
	p, id, up1 := startBroadcast(t, env, "alice", nil, "audio/aac")
	_ = up1.WriteChunk(wire.ChunkHeader, []byte("H-ONE"))
	_ = up1.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(1, 16))
	waitBytesIn(t, env, id, 5+16)
	l := listen(t, env, id)
	if hdr, _ := l.ReadFull(5, 3*time.Second); string(hdr) != "H-ONE" {
		t.Fatalf("%q", hdr)
	}
	if seq, _ := l.ReadSeq(16, 3*time.Second); seq != 1 {
		t.Fatal(seq)
	}
	// the same device uploads again: the first stream is closed, the listener stays
	up2, ans, err := p.Upload(id, "audio/aac")
	if err != nil || up2 == nil {
		t.Fatalf("%v %+v", err, ans)
	}
	defer up2.Close()
	_ = up1.SetReadDeadline(time.Now().Add(3 * time.Second))
	if _, err := up1.Read(make([]byte, 8)); err == nil {
		t.Fatal("the replaced upload's stream must be closed")
	}
	_ = up2.WriteChunk(wire.ChunkHeader, []byte("H-TWO"))
	_ = up2.WriteChunk(0, testkit.SeqPayload(90, 16)) // not joinable: skipped
	_ = up2.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(100, 16))
	hdr, err := l.ReadFull(5, 3*time.Second)
	if err != nil || string(hdr) != "H-TWO" {
		t.Fatalf("the listener continues after the NEW header: %q %v", hdr, err)
	}
	if seq, err := l.ReadSeq(16, 3*time.Second); err != nil || seq != 100 {
		t.Fatalf("and from the next joinable chunk of the new upload: %d %v", seq, err)
	}
	if env.Relay.Listeners(id) != 1 {
		t.Fatal("the listener stayed")
	}
}

func TestADroppedUploadHasTheGracePeriodToComeBackAndThenEndsTheListeners(t *testing.T) {
	env := relayEnv(t, func(c *rendezvous.RelayConfig) { c.Grace = 600 * time.Millisecond })
	p, id, up := startBroadcast(t, env, "alice", nil, "audio/aac")
	_ = up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(1, 16))
	waitBytesIn(t, env, id, 16)
	l := listen(t, env, id)
	if _, err := l.ReadSeq(16, 3*time.Second); err != nil {
		t.Fatal(err)
	}
	_ = up.Close() // the upload drops
	eventually(t, "the upload to be seen as gone", func() bool { u, _, _, _, ok := env.Relay.Held(id); return ok && !u })
	// inside the grace period: the listener is kept, a new listener is told to retry, the record stays
	if code, h := statusOf(t, env, getRequest(id), "GET"); code != 503 || h["Retry-After"] != "1" {
		t.Fatalf("%d %v", code, h)
	}
	if env.Dir.Counts().Broadcasts != 1 {
		t.Fatal("the record stays while the control stream lives")
	}
	// the producer comes back inside the period: the listener continues
	up2, _, err := p.Upload(id, "audio/aac")
	if err != nil || up2 == nil {
		t.Fatal(err)
	}
	_ = up2.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(2, 16))
	if seq, err := l.ReadSeq(16, 3*time.Second); err != nil || seq != 2 {
		t.Fatalf("%d %v", seq, err)
	}
	// it drops again and does not come back: the bodies end cleanly after the grace period
	_ = up2.Close()
	rest, err := l.ReadToEnd(5 * time.Second)
	if err != nil || len(rest) != 0 {
		t.Fatalf("the body ends cleanly after the grace period: %v %d", err, len(rest))
	}
	eventually(t, "the relay to be torn down", func() bool { return env.Relay.Broadcasts() == 0 })
	if env.Dir.Counts().Broadcasts != 1 {
		t.Fatal("the grace period tears the relay down, not the record: its LAN endpoints still work")
	}
	if code, _ := statusOf(t, env, getRequest(id), "GET"); code != 503 {
		t.Fatalf("a live broadcast with no relay is 503 again: %d", code)
	}
}

func TestMaxListenersCountsRelayListenersAndTunnelsTogether(t *testing.T) {
	env := relayEnv(t, nil)
	two := 2
	hold := make(chan struct{})
	p := env.NewProducer("alice", "x")
	p.Handler = func(c net.Conn) { <-hold; _ = c.Close() }
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	defer p.Close()
	defer close(hold)
	id, err := p.StartBroadcast("AUDIO_ONLY", &two)
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

	// one tunnel for the broadcast...
	consumer := env.NewDevice("alice")
	mustEnroll(t, consumer)
	tun, _, err := consumer.Connect(&wire.Connect{BroadcastID: id, Scheme: "http"})
	if err != nil || tun == nil {
		t.Fatalf("the tunnel: %v", err)
	}
	defer tun.Close()
	// ...and one relay listener fill the cap of 2
	l1 := listen(t, env, id)
	if _, err := l1.ReadSeq(16, 3*time.Second); err != nil {
		t.Fatal(err)
	}
	// the third is 503 with Retry-After: 2, whether it comes by relay or by tunnel
	code, h := statusOf(t, env, getRequest(id), "GET")
	if code != 503 || h["Retry-After"] != "2" {
		t.Fatalf("a relay listener over maxListeners: %d %v", code, h)
	}
	if c2, r, _ := env.OutsiderConnect(&wire.Connect{BroadcastID: id, Scheme: "http"}); c2 != nil {
		t.Fatalf("a tunnel over maxListeners must be refused: %+v", r)
	} else {
		refusedWith(t, r, wire.ReasonUnavailable)
	}
	// a freed slot is usable
	l1.Close()
	eventually(t, "the listener slot to be freed", func() bool { return env.Relay.Listeners(id) == 0 })
	l3 := listen(t, env, id)
	if l3.Resp.StatusCode != 200 {
		t.Fatalf("%d", l3.Resp.StatusCode)
	}
}

func TestZeroMaxListenersMeansNoCapOfTheProducersOwnButTheDeploymentCeilingHolds(t *testing.T) {
	env := relayEnv(t, func(c *rendezvous.RelayConfig) { c.ListenersPerBcast = 3; c.ListenersTotal = 100 })
	_, id, up := startBroadcast(t, env, "alice", nil, "audio/aac")
	_ = up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(1, 16))
	waitBytesIn(t, env, id, 16)
	for i := 0; i < 3; i++ {
		if l := listen(t, env, id); l.Resp.StatusCode != 200 {
			t.Fatalf("listener %d: %d", i, l.Resp.StatusCode)
		}
	}
	if code, h := statusOf(t, env, getRequest(id), "GET"); code != 503 || h["Retry-After"] != "2" {
		t.Fatalf("over the deployment's ceiling: %d %v", code, h)
	}
}

func TestTheDeploymentTotalOfRelayListenersIsCapped(t *testing.T) {
	env := relayEnv(t, func(c *rendezvous.RelayConfig) { c.ListenersPerBcast = 50; c.ListenersTotal = 4 })
	var ids []string
	for i := 0; i < 2; i++ {
		_, id, up := startBroadcast(t, env, "alice", nil, "audio/aac")
		_ = up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(1, 16))
		waitBytesIn(t, env, id, 16)
		ids = append(ids, id)
	}
	for i := 0; i < 4; i++ {
		if l := listen(t, env, ids[i%2]); l.Resp.StatusCode != 200 {
			t.Fatalf("%d", l.Resp.StatusCode)
		}
	}
	if code, _ := statusOf(t, env, getRequest(ids[0]), "GET"); code != 503 {
		t.Fatalf("over the total: %d", code)
	}
}

// acceptance 1: three listeners, one of them throttled: the two fast ones keep their delay, the slow
// one skips and stays within the window, the upload is never slowed.
func TestAThrottledListenerSkipsTheFastOnesAreUntouchedAndTheUploadIsNeverSlowed(t *testing.T) {
	env := relayEnv(t, func(c *rendezvous.RelayConfig) { c.Window = 300 * time.Millisecond; c.WriteTimeout = 10 * time.Second })
	_, id, up := startBroadcast(t, env, "alice", nil, "audio/aac")
	const size = 32 << 10
	_ = up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(0, size))
	waitBytesIn(t, env, id, size)

	type result struct {
		seqs []uint32
		err  error
	}
	var lns []*testkit.Listening
	slowSkipped := make(chan struct{}, 1)
	run := func(delay time.Duration, stop <-chan struct{}) <-chan result {
		l := listen(t, env, id)
		lns = append(lns, l)
		out := make(chan result, 1)
		go func() {
			var r result
			for {
				select {
				case <-stop:
					out <- r
					return
				default:
				}
				seq, err := l.ReadSeq(size, 5*time.Second)
				if err != nil {
					r.err = err
					out <- r
					return
				}
				if delay > 0 && len(r.seqs) > 0 && seq > r.seqs[len(r.seqs)-1]+1 {
					select {
					case slowSkipped <- struct{}{}:
					default:
					}
				}
				r.seqs = append(r.seqs, seq)
				if delay > 0 {
					time.Sleep(delay)
				}
			}
		}()
		return out
	}
	stop := make(chan struct{})
	fast1, fast2, slow := run(0, stop), run(0, stop), run(25*time.Millisecond, stop)
	eventually(t, "three listeners", func() bool { return env.Relay.Listeners(id) == 3 })

	// the broadcaster paces itself at 4 ms a chunk (8 MB/s); its own write time is the measure
	const total = 400
	start := time.Now()
	var worst time.Duration
	for i := 1; i <= total; i++ {
		t0 := time.Now()
		if err := up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(uint32(i), size)); err != nil {
			t.Fatal(err)
		}
		if d := time.Since(t0); d > worst {
			worst = d
		}
		time.Sleep(4 * time.Millisecond)
	}
	elapsed := time.Since(start)
	// The kernel may buffer a contiguous prefix larger than one relay window. Wait for the
	// observable skip rather than assuming every OS has drained that prefix after 300 ms.
	select {
	case <-slowSkipped:
	case <-time.After(5 * time.Second):
	}
	close(stop)
	for _, l := range lns {
		l.Close() // unblocks a reader that is waiting for the next chunk
	}
	if elapsed > total*4*time.Millisecond+4*time.Second {
		t.Fatalf("the upload took %v for a %v plan: a slow listener slowed it (worst single write %v)", elapsed, total*4*time.Millisecond, worst)
	}
	for name, ch := range map[string]<-chan result{"fast1": fast1, "fast2": fast2} {
		r := <-ch
		if len(r.seqs) < total/2 {
			t.Fatalf("%s saw only %d chunks (%v)", name, len(r.seqs), r.err)
		}
		for i := 1; i < len(r.seqs); i++ {
			if r.seqs[i] != r.seqs[i-1]+1 {
				t.Fatalf("%s: a fast listener must never skip: %d then %d", name, r.seqs[i-1], r.seqs[i])
			}
		}
		if last := r.seqs[len(r.seqs)-1]; last < total-5 {
			t.Fatalf("%s ended %d chunks behind the live edge", name, total-int(last))
		}
	}
	rs := <-slow
	if rs.err != nil && !errors.Is(rs.err, io.EOF) && !strings.Contains(rs.err.Error(), "deadline") {
		t.Logf("slow listener ended with %v", rs.err)
	}
	skipped := 0
	for i := 1; i < len(rs.seqs); i++ {
		if rs.seqs[i] <= rs.seqs[i-1] {
			t.Fatalf("a listener never goes back: %d then %d", rs.seqs[i-1], rs.seqs[i])
		}
		if rs.seqs[i] != rs.seqs[i-1]+1 {
			skipped++
		}
	}
	if skipped == 0 {
		t.Fatalf("a listener at a third of the stream's rate must skip chunks, it saw %d of %d in order", len(rs.seqs), total)
	}
	if len(rs.seqs) >= total {
		t.Fatalf("the slow listener cannot have seen every chunk: %d", len(rs.seqs))
	}
}

func TestAPeerThatNeverReadsIsDisconnectedByItsWriteDeadlineAndNobodyIsSlowed(t *testing.T) {
	env := relayEnv(t, func(c *rendezvous.RelayConfig) { c.WriteTimeout = 500 * time.Millisecond })
	_, id, up := startBroadcast(t, env, "alice", nil, "audio/aac")
	const size = 256 << 10
	_ = up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(0, size))
	waitBytesIn(t, env, id, size)
	dead := listen(t, env, id) // reads its response head and never reads again
	good := listen(t, env, id)
	var goodSeqs atomic.Int64
	go func() {
		for {
			if _, err := good.ReadSeq(size, 10*time.Second); err != nil {
				return
			}
			goodSeqs.Add(1)
		}
	}()
	eventually(t, "two listeners", func() bool { return env.Relay.Listeners(id) == 2 })
	_ = dead
	start := time.Now()
	for i := 1; i <= 120; i++ {
		if err := up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(uint32(i), size)); err != nil {
			t.Fatal(err)
		}
		time.Sleep(5 * time.Millisecond)
	}
	if d := time.Since(start); d > 8*time.Second {
		t.Fatalf("the upload took %v: a peer that never reads must not slow it", d)
	}
	eventually(t, "the peer that never reads to be disconnected", func() bool { return env.Relay.Listeners(id) == 1 })
	if goodSeqs.Load() < 100 {
		t.Fatalf("the reading listener saw %d chunks", goodSeqs.Load())
	}
}

func TestAFloodOfListenersLeavesTheUploadAtFullRate(t *testing.T) {
	env := relayEnv(t, func(c *rendezvous.RelayConfig) { c.WriteTimeout = 10 * time.Second })
	_, id, up := startBroadcast(t, env, "alice", nil, "audio/aac")
	_ = up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(0, 4096))
	waitBytesIn(t, env, id, 4096)
	const n = 100
	var wg sync.WaitGroup
	var got atomic.Int64
	var lns []*testkit.Listening
	for i := 0; i < n; i++ {
		l := listen(t, env, id)
		lns = append(lns, l)
		wg.Add(1)
		go func() {
			defer wg.Done()
			buf := make([]byte, 64<<10)
			for { // blocks until data arrives or the connection is closed below
				m, err := l.Resp.Body.Read(buf)
				got.Add(int64(m))
				if err != nil {
					return
				}
			}
		}()
	}
	eventually(t, "the flood to attach", func() bool { return env.Relay.Listeners(id) == n })
	const chunks, size = 800, 8192
	start := time.Now()
	for i := 1; i <= chunks; i++ {
		if err := up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(uint32(i), size)); err != nil {
			t.Fatal(err)
		}
	}
	waitBytesIn(t, env, id, int64(4096+chunks*size))
	if d := time.Since(start); d > 20*time.Second {
		t.Fatalf("the upload of %d chunks took %v with %d listeners attached", chunks, d, n)
	}
	time.Sleep(300 * time.Millisecond)
	for _, l := range lns {
		l.Close()
	}
	wg.Wait()
	if got.Load() == 0 {
		t.Fatal("the listeners received nothing")
	}
}

func TestAnUploadAboveTheCeilingIsClosedWithCapacity(t *testing.T) {
	env := relayEnv(t, func(c *rendezvous.RelayConfig) {
		c.MaxUploadBitsPerSec = 2_000_000 // 250 kB/s
		c.RateWindow = 400 * time.Millisecond
	})
	_, id, up := startBroadcast(t, env, "alice", nil, "audio/aac")
	payload := bytes.Repeat([]byte{1}, 16<<10)
	var closedAt int
	for i := 0; i < 400; i++ { // ~ 1 MB/s for a second
		if err := up.WriteChunk(wire.ChunkJoinable, payload); err != nil {
			closedAt = i
			break
		}
		time.Sleep(15 * time.Millisecond)
		_ = up.SetReadDeadline(time.Now().Add(time.Millisecond))
		if e, err := readEnvelope(up); err == nil {
			f, ok := e.(*wire.Refused)
			if !ok || f.Reason != string(wire.ReasonCapacity) {
				t.Fatalf("%+v", e)
			}
			closedAt = i
			break
		}
	}
	if closedAt == 0 {
		t.Fatal("a sustained upload above the ceiling must be closed with capacity")
	}
	_ = id
}

func readEnvelope(c *testkit.Conn) (wire.Envelope, error) { return c.Recv(2 * time.Millisecond) }

func TestAnUploadUnderTheCeilingIsNotTouched(t *testing.T) {
	env := relayEnv(t, func(c *rendezvous.RelayConfig) {
		c.MaxUploadBitsPerSec = 8_000_000
		c.RateWindow = 300 * time.Millisecond
	})
	_, id, up := startBroadcast(t, env, "alice", nil, "audio/aac")
	payload := bytes.Repeat([]byte{1}, 1<<10)
	for i := 0; i < 80; i++ { // ~ 70 kB/s for 1.2 s
		if err := up.WriteChunk(wire.ChunkJoinable, payload); err != nil {
			t.Fatal(err)
		}
		time.Sleep(15 * time.Millisecond)
	}
	waitBytesIn(t, env, id, 80<<10)
	if u, _, _, _, _ := env.Relay.Held(id); !u {
		t.Fatal("an upload under the ceiling stays attached")
	}
}

func TestTheRelayLogsCountersAndNeverAnId(t *testing.T) {
	env := relayEnv(t, nil)
	p, id, up := startBroadcast(t, env, "alice", nil, "audio/aac")
	for i := 0; i < 5; i++ {
		_ = up.WriteChunk(wire.ChunkJoinable, testkit.SeqPayload(uint32(i), 64))
	}
	waitBytesIn(t, env, id, 5*64)
	l := listen(t, env, id)
	_, _ = l.ReadSeq(64, 3*time.Second)
	_ = p.EndBroadcast(id)
	_, _ = l.ReadToEnd(3 * time.Second)
	eventually(t, "the relay-ended line", func() bool {
		for _, ln := range env.Log.Lines() {
			if strings.Contains(ln, "relay-ended") && strings.Contains(ln, "bytes-in=320") && strings.Contains(ln, "listeners=") {
				return true
			}
		}
		return false
	})
	for _, ln := range env.Log.Lines() {
		if strings.Contains(ln, id) {
			t.Fatalf("a log line carries the broadcast id: %q", ln)
		}
	}
}
