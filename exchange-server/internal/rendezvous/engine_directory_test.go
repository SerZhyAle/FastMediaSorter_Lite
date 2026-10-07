package rendezvous_test

import (
	"bytes"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Ticket 163 on the wire: presence, list, subscribe, changed, publish, broadcast records.

func startProducer(t *testing.T, env *testkit.Env, login string) *testkit.Producer {
	t.Helper()
	p := env.NewProducer(login, "ssh-secret")
	if err := p.Start(); err != nil {
		t.Fatal(err)
	}
	return p
}

func recs(raws []json.RawMessage) []*wire.Record {
	var out []*wire.Record
	for _, r := range raws {
		rec, err := wire.ParseRecord(r)
		if err != nil {
			panic(err)
		}
		out = append(out, rec)
	}
	return out
}

func findRec(rs []*wire.Record, key, val string) *wire.Record {
	for _, r := range rs {
		if s, _ := r.Str(key); s == val {
			return r
		}
	}
	return nil
}

func TestAcceptance1TwoDevicesSeeEachOthersThroughListAndChanged(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice", "bobby"))
	prod := startProducer(t, env, "alice")

	consumer := env.NewDevice("alice")
	mustEnroll(t, consumer)
	cc, err := consumer.Online()
	if err != nil {
		t.Fatal(err)
	}
	outsider := env.NewDevice("bobby")
	mustEnroll(t, outsider)
	oc, err := outsider.Online()
	if err != nil {
		t.Fatal(err)
	}

	resID, shareID, ans, err := prod.PublishShare(true, nil)
	if err != nil || ans.TypeName() != wire.TypePublished {
		t.Fatalf("%v %+v", err, ans)
	}
	// the subscriber is pushed the change, with the access, and a revision that grows by one
	ch, err := cc.Await(3*time.Second, wire.TypeChanged)
	if err != nil {
		t.Fatal(err)
	}
	c1 := ch.(*wire.Changed)
	if len(c1.Upserts.Resources) != 1 {
		t.Fatalf("changed: %+v", c1)
	}
	r := recs(c1.Upserts.Resources)[0]
	if id, _ := r.Str("resourceId"); id != resID || !r.Has("access") || !r.Has("shareId") {
		t.Fatalf("the consumer's push: %s", r.Marshal())
	}
	if p, _ := r.Str("presence"); p != "online" {
		t.Fatal("presence")
	}

	// a broadcast
	bid := env.NewID()
	bs, err := prod.Control().Request(&wire.BroadcastStart{Broadcast: json.RawMessage(
		`{"broadcastId":"` + bid + `","mode":"AUDIO_ONLY","title":"Mic","descriptor":{"endpoints":[{"transport":"HTTP_ADTS"}]}}`)},
		3*time.Second, wire.TypeBroadcastStarted, wire.TypeRefused)
	if err != nil || bs.TypeName() != wire.TypeBroadcastStarted || bs.(*wire.BroadcastStarted).BroadcastID != bid {
		t.Fatalf("%v %+v", err, bs)
	}
	ch, err = cc.Await(3*time.Second, wire.TypeChanged)
	if err != nil {
		t.Fatal(err)
	}
	c2 := ch.(*wire.Changed)
	if c2.Revision != c1.Revision+1 || len(c2.Upserts.Broadcasts) != 1 {
		t.Fatalf("revisions %d then %d", c1.Revision, c2.Revision)
	}

	// list shows both, the requester's own device included
	dir := listDirectory(t, cc)
	if len(dir.Devices) != 2 || len(dir.Resources) != 1 || len(dir.Broadcasts) != 1 || dir.Revision != c2.Revision {
		t.Fatalf("list: %d devices, %d resources, %d broadcasts, revision %d vs %d", len(dir.Devices), len(dir.Resources), len(dir.Broadcasts), dir.Revision, c2.Revision)
	}
	if d := findRec(recs(dir.Devices), "deviceId", consumer.ID); d == nil {
		t.Fatal("the requester's own device must be listed (hiding it is the consumer's rule)")
	} else if p, _ := d.Str("presence"); p != "online" {
		t.Fatal("presence of the requester")
	}
	// the device record carries what enroll said, the receiver block verbatim, and no token
	pd := findRec(recs(dir.Devices), "deviceId", prod.ID)
	if pd == nil || !pd.Has("receiver") || pd.Has("deviceToken") || pd.Has("tokenHash") {
		t.Fatalf("%v", pd)
	}

	// the other account sees nothing - not on list, not on changed
	odir := listDirectory(t, oc)
	if len(odir.Resources) != 0 || len(odir.Broadcasts) != 0 || len(odir.Devices) != 1 {
		t.Fatalf("bobby sees %d resources, %d broadcasts, %d devices", len(odir.Resources), len(odir.Broadcasts), len(odir.Devices))
	}
	if n := oc.Drain(); n != 0 {
		// the only things bobby's stream may have received are the answers it asked for
		t.Logf("bobby's inbox held %d envelopes", n)
	}
	if _, err := oc.Await(300*time.Millisecond, wire.TypeChanged); err == nil {
		t.Fatal("bobby was pushed a change of alice's directory")
	}
	// and every message: another account's resource cannot be connected by resourceId
	cn, r2, _ := outsider.Connect(&wire.Connect{ResourceID: resID})
	if cn != nil {
		t.Fatal("bobby reached alice's resource")
	}
	refusedWith(t, r2, wire.ReasonUnavailable)
	// the share, being a capability, works for anyone who holds it - and says nothing of whose it is
	if sc, _, err := env.OutsiderConnect(&wire.Connect{ShareID: shareID}); err != nil || sc == nil {
		t.Fatalf("a shareId connect: %v", err)
	} else {
		sc.Close()
	}
	// bobby publishing the same resourceId is his own resource
	bp := env.NewProducer("bobby", "x")
	if err := bp.Start(); err != nil {
		t.Fatal(err)
	}
	rec := wire.NewRecord()
	rec.SetStr("resourceId", resID)
	rec.SetStr("kind", "sftp-share")
	rec.SetStr("name", "Mine")
	if a, _ := bp.Publish(rec.Marshal()); a == nil || a.TypeName() != wire.TypePublished {
		t.Fatalf("the same resourceId in another account must publish: %+v", a)
	}
	if n := len(recs(listDirectory(t, cc).Resources)); n != 1 {
		t.Fatalf("alice sees %d resources", n)
	}
}

func TestPresenceKeepaliveWindowByeAndAReplacedControlStream(t *testing.T) {
	ck := newClock()
	o := looseOpts("alice")
	o.Now = ck.Now
	env := testkit.StartServer(t, o)
	prod := startProducer(t, env, "alice")
	obs := env.NewDevice("alice")
	mustEnroll(t, obs)
	oc, err := obs.Online()
	if err != nil {
		t.Fatal(err)
	}
	_, _, ans, _ := prod.PublishShare(true, nil)
	if ans.TypeName() != wire.TypePublished {
		t.Fatalf("%+v", ans)
	}
	bid := env.NewID()
	prod.Control().Request(&wire.BroadcastStart{Broadcast: json.RawMessage(`{"broadcastId":"` + bid + `","mode":"AUDIO_ONLY","descriptor":{}}`)}, 3*time.Second, wire.TypeBroadcastStarted)
	oc.Drain()

	// every keepalive is answered, and the window is three intervals: 3 x 30 s
	for i := 0; i < 3; i++ {
		ck.Add(25 * time.Second)
		if _, err := prod.Control().Request(&wire.Keepalive{}, 3*time.Second, wire.TypeKeepalive); err != nil {
			t.Fatalf("keepalive %d: %v", i, err)
		}
		time.Sleep(120 * time.Millisecond) // a sweep or two
	}
	if env.Dir.Counts().Online != 2 {
		t.Fatal("a keepalive must extend the window")
	}
	// replaced control stream: carries everything over, and the old one is closed with no message
	old := prod.Control()
	repl, err := prod.Online()
	if err != nil {
		t.Fatal(err)
	}
	select {
	case <-old.Closed():
	case <-time.After(3 * time.Second):
		t.Fatal("the old control stream must be closed")
	}
	if n := len(recs(listDirectory(t, repl).Resources)); n != 1 {
		t.Fatalf("a replaced stream kept %d resources", n)
	}
	if r := recs(listDirectory(t, repl).Resources)[0]; !r.Has("access") {
		t.Fatal("access must carry over a replaced control stream")
	}
	if n := len(listDirectory(t, repl).Broadcasts); n != 1 {
		t.Fatal("the broadcast must carry over")
	}
	if env.Dir.Counts().Online != 2 {
		t.Fatal("a replaced stream is not an offline")
	}
	oc.Drain()

	// three silent intervals of the producer (the observer keeps its own window alive): offline,
	// resources stay listed without access, the broadcast ends
	if _, err := oc.Request(&wire.Keepalive{}, 3*time.Second, wire.TypeKeepalive); err != nil {
		t.Fatal(err)
	}
	ck.Add(60 * time.Second)
	if _, err := oc.Request(&wire.Keepalive{}, 3*time.Second, wire.TypeKeepalive); err != nil {
		t.Fatal(err)
	}
	time.Sleep(120 * time.Millisecond)
	ck.Add(40 * time.Second)
	ch, err := oc.Await(3*time.Second, wire.TypeChanged)
	if err != nil {
		t.Fatal("no changed after the keepalive window:", err)
	}
	c := ch.(*wire.Changed)
	if len(c.Upserts.Devices) != 1 || len(c.Upserts.Resources) != 1 || len(c.Removals.BroadcastIDs) != 1 {
		t.Fatalf("one offline is one changed: %+v %+v", c.Upserts, c.Removals)
	}
	for _, r := range recs(c.Upserts.Resources) {
		if p, _ := r.Str("presence"); p != "offline" || r.Has("access") || r.Has("shareId") {
			t.Fatalf("offline: %s", r.Marshal())
		}
	}
	dir := listDirectory(t, oc)
	if len(dir.Resources) != 1 || len(dir.Broadcasts) != 0 {
		t.Fatalf("after offline: %d resources, %d broadcasts", len(dir.Resources), len(dir.Broadcasts))
	}
	if d := findRec(recs(dir.Devices), "deviceId", prod.ID); d == nil {
		t.Fatal("an offline device stays in the directory")
	} else if p, _ := d.Str("presence"); p != "offline" {
		t.Fatalf("presence %s", p)
	}

	// back online, and bye is an offline on purpose: no answer
	back, err := prod.Online()
	if err != nil {
		t.Fatal(err)
	}
	if _, err := oc.Await(3*time.Second, wire.TypeChanged); err != nil {
		t.Fatal(err)
	}
	oc.Drain()
	if err := back.Send(&wire.Bye{}); err != nil {
		t.Fatal(err)
	}
	if _, err := oc.Await(3*time.Second, wire.TypeChanged); err != nil {
		t.Fatal("bye must take the device offline:", err)
	}
	select {
	case <-back.Closed():
	case <-time.After(3 * time.Second):
		t.Fatal("bye closes the stream")
	}
}

func TestAHelloThatNeverSendsAControlEnvelopeIsNotOnline(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	d := env.NewDevice("alice")
	mustEnroll(t, d)
	d.Close()
	eventually(t, "the enroll stream to end", func() bool { return env.Dir.Counts().Online == 0 })
	c, _, err := d.Hello()
	if err != nil || c == nil {
		t.Fatal(err)
	}
	defer c.Close()
	time.Sleep(100 * time.Millisecond)
	if env.Dir.Counts().Online != 0 {
		t.Fatal("presence is the control stream, not a hello")
	}
}

func TestPublishRefusalsAndReplacement(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	a, b := startProducer(t, env, "alice"), startProducer(t, env, "alice")
	resID, shareID, ans, _ := a.PublishShare(true, nil)
	if ans.TypeName() != wire.TypePublished {
		t.Fatalf("%+v", ans)
	}
	known := func(r wire.Envelope, want wire.Reason) {
		t.Helper()
		f, ok := r.(*wire.Refused)
		if !ok || f.Reason != string(want) || !wire.Reason(f.Reason).Valid() {
			t.Fatalf("want %s, got %+v", want, r)
		}
	}
	// another device holding the resourceId; a held shareId
	rec := wire.NewRecord()
	rec.SetStr("resourceId", resID)
	rec.SetStr("kind", "sftp-share")
	rec.SetStr("name", "x")
	r, _ := b.Publish(rec.Marshal())
	known(r, wire.ReasonIDCollision)
	rec2 := wire.NewRecord()
	rec2.SetStr("resourceId", env.NewID())
	rec2.SetStr("kind", "sftp-share")
	rec2.SetStr("name", "x")
	rec2.SetStr("shareId", shareID)
	r, _ = b.Publish(rec2.Marshal())
	known(r, wire.ReasonIDCollision)
	// the same device publishing a held resourceId replaces it (and is answered `published`)
	rec3 := wire.NewRecord()
	rec3.SetStr("resourceId", resID)
	rec3.SetStr("kind", "sftp-share")
	rec3.SetStr("name", "Renamed")
	r, _ = a.Publish(rec3.Marshal())
	if p, ok := r.(*wire.Published); !ok || p.ResourceID != resID {
		t.Fatalf("%+v", r)
	}
	dir := listDirectory(t, a.Control())
	if len(dir.Resources) != 1 {
		t.Fatalf("a re-publish must replace, not add: %d", len(dir.Resources))
	}
	if n, _ := recs(dir.Resources)[0].Str("name"); n != "Renamed" {
		t.Fatal(n)
	}
	// over 16 KiB: capacity; the stream goes on
	huge := `{"resourceId":"` + env.NewID() + `","kind":"k","name":"n","pad":"` + strings.Repeat("a", wire.MaxRecord) + `"}`
	r, _ = a.Publish(json.RawMessage(huge))
	known(r, wire.ReasonCapacity)
	if _, err := a.Control().Request(&wire.Keepalive{}, 2*time.Second, wire.TypeKeepalive); err != nil {
		t.Fatalf("a refused publish must leave the stream open: %v", err)
	}
	// a malformed record closes the stream
	bad := env.NewProducer("alice", "x")
	if err := bad.Start(); err != nil {
		t.Fatal(err)
	}
	_ = bad.Control().Send(&wire.Publish{Resource: json.RawMessage(`{"resourceId":"short","kind":"k","name":"n"}`)})
	select {
	case <-bad.Control().Closed():
	case <-time.After(3 * time.Second):
		t.Fatal("a record with a bad id form must close the stream")
	}
}

func TestUnknownMembersThreeLevelsDeepSurviveThroughTheWire(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	p := startProducer(t, env, "alice")
	c := env.NewDevice("alice")
	mustEnroll(t, c)
	cc, _ := c.Online()
	rid := env.NewID()
	r, _ := p.Publish(json.RawMessage(`{"resourceId":"` + rid + `","kind":"hologram","name":"n","future":{"a":{"b":{"c":[1,{"d":true}]}}}}`))
	if r.TypeName() != wire.TypePublished {
		t.Fatalf("an unknown kind is stored as is: %+v", r)
	}
	bid := env.NewID()
	br, _ := p.Control().Request(&wire.BroadcastStart{Broadcast: json.RawMessage(
		`{"broadcastId":"` + bid + `","mode":"VIDEO_AUDIO","descriptor":{"endpoints":[],"vendor":{"x":{"y":{"z":9}}}},"newer":{"a":{"b":{"c":0}}}}`)}, 3*time.Second, wire.TypeBroadcastStarted)
	if br.TypeName() != wire.TypeBroadcastStarted {
		t.Fatalf("%+v", br)
	}
	dir := listDirectory(t, cc)
	res := recs(dir.Resources)[0]
	if string(res.Raw("future")) != `{"a":{"b":{"c":[1,{"d":true}]}}}` {
		t.Fatalf("resource: %s", res.Raw("future"))
	}
	bro := recs(dir.Broadcasts)[0]
	if string(bro.Raw("newer")) != `{"a":{"b":{"c":0}}}` || !bytes.Contains(bro.Raw("descriptor"), []byte(`"vendor":{"x":{"y":{"z":9}}}`)) {
		t.Fatalf("broadcast: %s", bro.Marshal())
	}
}

func TestBroadcastStartRulesAndTheMaxListeners(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	a, b := startProducer(t, env, "alice"), startProducer(t, env, "alice")
	bid := env.NewID()
	start := func(p *testkit.Producer, raw string, ml *int) wire.Envelope {
		r, err := p.Control().Request(&wire.BroadcastStart{Broadcast: json.RawMessage(raw), MaxListeners: ml}, 3*time.Second, wire.TypeBroadcastStarted, wire.TypeRefused)
		if err != nil {
			t.Fatal(err)
		}
		return r
	}
	four := 4
	if r := start(a, `{"broadcastId":"`+bid+`","mode":"AUDIO_ONLY","descriptor":{}}`, &four); r.TypeName() != wire.TypeBroadcastStarted {
		t.Fatalf("%+v", r)
	}
	refusedWith(t, start(b, `{"broadcastId":"`+bid+`","mode":"AUDIO_ONLY","descriptor":{}}`, nil), wire.ReasonIDCollision)
	// update replaces, may change the cap, and is not answered
	if err := a.Control().Send(&wire.BroadcastUpdate{Broadcast: json.RawMessage(`{"broadcastId":"` + bid + `","mode":"AUDIO_ONLY","title":"New","descriptor":{}}`)}); err != nil {
		t.Fatal(err)
	}
	// end, and the id is free again
	if err := a.Control().Send(&wire.BroadcastEnd{BroadcastID: bid}); err != nil {
		t.Fatal(err)
	}
	eventually(t, "the broadcast to end", func() bool { return env.Dir.Counts().Broadcasts == 0 })
	if r := start(b, `{"broadcastId":"`+bid+`","mode":"AUDIO_ONLY","descriptor":{}}`, nil); r.TypeName() != wire.TypeBroadcastStarted {
		t.Fatalf("%+v", r)
	}
}

func TestBroadcastStartIsRateLimitedPerDevice(t *testing.T) {
	lc := loose()
	lc.Rates[limits.BroadcastStart] = limits.Rate{PerMinute: 1, Burst: 2}
	env := testkit.StartServer(t, testkit.Options{Limits: loose(), LoginLimits: lc, Logins: []string{"alice"}})
	p := startProducer(t, env, "alice")
	for i := 0; i < 2; i++ {
		r, _ := p.Control().Request(&wire.BroadcastStart{Broadcast: json.RawMessage(`{"broadcastId":"` + env.NewID() + `","mode":"AUDIO_ONLY","descriptor":{}}`)}, 3*time.Second, wire.TypeBroadcastStarted, wire.TypeRefused)
		if r.TypeName() != wire.TypeBroadcastStarted {
			t.Fatalf("%d: %+v", i, r)
		}
	}
	r, _ := p.Control().Request(&wire.BroadcastStart{Broadcast: json.RawMessage(`{"broadcastId":"` + env.NewID() + `","mode":"AUDIO_ONLY","descriptor":{}}`)}, 3*time.Second, wire.TypeBroadcastStarted, wire.TypeRefused)
	refusedWith(t, r, wire.ReasonRateLimited)
}

func TestTheDirectoryBudgetIsEnforcedOnTheWire(t *testing.T) {
	p := testkit.TestParams()
	p.ResourcesPerAccount = 1000
	o := looseOpts("alice")
	o.Params = &p
	env := testkit.StartServer(t, o)
	prod := startProducer(t, env, "alice")
	pad := strings.Repeat("a", 15000)
	accepted := 0
	for i := 0; i < 40; i++ {
		r, err := prod.Publish(json.RawMessage(fmt.Sprintf(`{"resourceId":"%s","kind":"k","name":"n","pad":"%s"}`, env.NewID(), pad)))
		if err != nil {
			t.Fatal(err)
		}
		if f, ok := r.(*wire.Refused); ok {
			if f.Reason != string(wire.ReasonCapacity) {
				t.Fatalf("%+v", f)
			}
			break
		}
		accepted++
	}
	if accepted < 10 || accepted >= 40 {
		t.Fatalf("%d records accepted", accepted)
	}
	// and the answer to `list` fits the authenticated frame: the client decodes it under that limit
	dir := listDirectory(t, prod.Control())
	if len(dir.Resources) != accepted {
		t.Fatalf("%d listed, %d accepted", len(dir.Resources), accepted)
	}
}

func TestASlowSubscriberIsClosedAndTheServerStaysResponsive(t *testing.T) {
	o := looseOpts("alice")
	env := testkit.StartServer(t, o)
	prod := startProducer(t, env, "alice")

	// a subscriber that never reads
	raw, err := testkit.DialTLS(env.Addr, env.Fingerprint)
	if err != nil {
		t.Fatal(err)
	}
	defer raw.Close()
	slow := env.NewDevice("alice")
	mustEnroll(t, slow)
	slow.Control().Close() // the enroll stream is not the one that subscribes
	c, _, err := slow.Hello()
	if err != nil || c == nil {
		t.Fatal(err)
	}
	defer c.Close()
	if err := c.Send(&wire.Subscribe{}); err != nil {
		t.Fatal(err)
	}
	eventually(t, "the slow device to subscribe", func() bool { return env.Dir.Counts().Subscribers == 2 })

	// churn big records: the slow stream's socket buffers fill, then its 256-envelope queue
	pad := strings.Repeat("a", 14000)
	rid := env.NewID()
	deadline := time.Now().Add(60 * time.Second)
	publishes := 0
	for env.Dir.Counts().Subscribers > 1 { // the producer itself stays subscribed
		if time.Now().After(deadline) {
			t.Fatalf("a subscriber that never reads was never closed after %d publishes of 14 KB", publishes)
		}
		publishes++
		r, err := prod.Publish(json.RawMessage(fmt.Sprintf(`{"resourceId":"%s","kind":"k","name":"n","pad":"%s"}`, rid, pad)))
		if err != nil || r.TypeName() != wire.TypePublished {
			t.Fatalf("%v %+v: the directory must stay responsive while a subscriber is slow", err, r)
		}
	}
	// the stream was closed by the server
	_ = c.SetReadDeadline(time.Now().Add(10 * time.Second))
	buf := make([]byte, 1<<16)
	for {
		if _, err := c.Read(buf); err != nil {
			break
		}
	}
	// and the others were not touched
	if _, err := prod.Control().Request(&wire.Keepalive{}, 2*time.Second, wire.TypeKeepalive); err != nil {
		t.Fatal(err)
	}
}

// --- acceptance 2 and 3: a restart, and what is on the disk ---

func TestAcceptance2ARestartServesResourcesOfflineAndTheProducerRestoresAccess(t *testing.T) {
	env := testkit.StartServer(t, looseOpts("alice"))
	prod := startProducer(t, env, "alice")
	cons := env.NewDevice("alice")
	mustEnroll(t, cons)
	resID, shareID, ans, _ := prod.PublishShare(true, nil)
	if ans.TypeName() != wire.TypePublished {
		t.Fatalf("%+v", ans)
	}
	_ = shareID
	env.Stop()

	env2 := testkit.StartServer(t, testkit.Options{DataDir: env.DataDir, Limits: loose(), LoginLimits: loose(), Logins: []string{"alice"}})
	if env2.Fingerprint != env.Fingerprint {
		t.Fatal("the TLS identity must survive a restart")
	}
	prod.Rebind(env2)
	cons.Rebind(env2)
	cc, err := cons.Online()
	if err != nil {
		t.Fatal(err)
	}
	dir := listDirectory(t, cc)
	if len(dir.Resources) != 1 {
		t.Fatalf("%d resources after a restart", len(dir.Resources))
	}
	r := recs(dir.Resources)[0]
	if p, _ := r.Str("presence"); p != "offline" || r.Has("access") || r.Has("shareId") {
		t.Fatalf("a restarted resource is offline with no access: %s", r.Marshal())
	}
	if id, _ := r.Str("resourceId"); id != resID {
		t.Fatal("the resource keeps its id across a restart")
	}
	// the producer returns: online, and republishes (the same device replaces)
	if err := prod.Resume(); err != nil {
		t.Fatal(err)
	}
	cc.Drain()
	rec := wire.NewRecord()
	rec.SetStr("resourceId", resID)
	rec.SetStr("kind", "sftp-share")
	rec.SetStr("name", "Photos")
	rec.SetRaw("access", json.RawMessage(`{"descriptor":"FMSSFTP2.after-restart","root":"/"}`))
	if a, _ := prod.Publish(rec.Marshal()); a.TypeName() != wire.TypePublished {
		t.Fatalf("%+v", a)
	}
	ch, err := cc.Await(3*time.Second, wire.TypeChanged)
	if err != nil {
		t.Fatal(err)
	}
	for {
		c := ch.(*wire.Changed)
		if len(c.Upserts.Resources) == 1 && recs(c.Upserts.Resources)[0].Has("access") {
			break
		}
		if ch, err = cc.Await(3*time.Second, wire.TypeChanged); err != nil {
			t.Fatal("the consumer never saw access again:", err)
		}
	}
	// the device that was enrolled before the restart still has its token
	if c, r, _ := cons.Hello(); c == nil {
		t.Fatalf("hello after a restart: %+v", r)
	} else {
		c.Close()
	}
}

// TestAcceptance3TheDataDirectoryHoldsNoAccessShareIdBroadcastOrSecret is the manifest test of ticket
// 167 rule 2 in its first form: a scenario drives every message of the server so far and then asserts
// that the directory's files are a subset of the allowed list and that no file holds any secret the
// scenario used, as raw bytes and as base64 and hex.
func TestAcceptance3TheDataDirectoryHoldsNoAccessShareIdBroadcastOrSecret(t *testing.T) {
	lo, hi := testkit.FreeRange(3)
	o := looseOpts("alice", "bobby")
	o.PortRange = [2]int{lo, hi}
	env := testkit.StartServer(t, o)
	prod := env.NewProducer("alice", "ssh-secret")
	if err := prod.Start(); err != nil {
		t.Fatal(err)
	}
	const accessSecret = "FMSSFTP2.test-secret-access-string"
	resID, shareID, ans, _ := prod.PublishShare(true, intPtr(0))
	if ans.TypeName() != wire.TypePublished {
		t.Fatalf("%+v", ans)
	}
	bid := env.NewID()
	desc := "descriptor-needle-" + env.NewID()
	prod.Control().Request(&wire.BroadcastStart{Broadcast: json.RawMessage(`{"broadcastId":"` + bid + `","mode":"AUDIO_ONLY","title":"title-needle","descriptor":{"url":"` + desc + `"}}`)}, 3*time.Second, wire.TypeBroadcastStarted)
	code := requestCode(t, prod)
	second := env.NewDevice("alice")
	if r, _ := second.EnrollWithCode(code); r == nil || r.TypeName() != wire.TypeEnrolled {
		t.Fatalf("%+v", r)
	}
	// a tunnel
	cn, _, err := second.Connect(&wire.Connect{ResourceID: resID})
	if err != nil || cn == nil {
		t.Fatalf("connect: %v", err)
	}
	if out, err := testkit.SSHThrough(cn.Conn, "ssh-secret", "x"); err != nil || out != "echo:x" {
		t.Fatalf("%q %v", out, err)
	}
	// a revoke, then a restart of the directory by flushing
	other := env.NewDevice("bobby")
	mustEnroll(t, other)
	env.Ops.RevokeDevice("bobby", other.ID)
	env.Engine.Close()

	seen := scanDataDir(t, env, []string{testkit.TestPassword, accessSecret, shareID, bid, desc, "title-needle", code, prod.Token, second.Token, other.Token})
	allowed := map[string]bool{"accounts.json": true, "devices.json": true, "directory.json": true,
		"tls/cert.pem": true, "tls/key.pem": true}
	for _, f := range seen {
		if !allowed[f] {
			t.Errorf("the data directory holds %s, which is not on the allowed list", f)
		}
	}
	raw, _ := os.ReadFile(filepath.Join(env.DataDir, "directory.json"))
	if !bytes.Contains(raw, []byte(resID)) {
		t.Fatalf("the resource record should be on disk (without its live members):\n%s", raw)
	}
}
