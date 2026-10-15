package directory

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"

	"fms/exchange-server/internal/wire"
)

const (
	devA1 = "AAAAAAAAAAAAAAAAAAAAAA"
	devA2 = "AAAAAAAAAAAAAAAAAAAAAQ"
	devB1 = "AAAAAAAAAAAAAAAAAAAAAg"
	resR1 = "RRRRRRRRRRRRRRRRRRRRRA"
	resR2 = "RRRRRRRRRRRRRRRRRRRRRQ"
	shrS1 = "SSSSSSSSSSSSSSSSSSSSSA"
	broB1 = "BBBBBBBBBBBBBBBBBBBBBA"
	broB2 = "BBBBBBBBBBBBBBBBBBBBBQ"
	alice = "alice"
	bob   = "bobby"
)

// fakeSink is a control stream: it keeps the frames it was handed and can be made to overflow.
type fakeSink struct {
	mu      sync.Mutex
	frames  [][]byte
	closed  bool
	limit   int // 0: unbounded
	name    string
	onClose func()
}

func (f *fakeSink) Send(e wire.Envelope) error {
	frame, err := wire.Frame(e)
	if err != nil {
		return err
	}
	return f.SendFrame(frame)
}

func (f *fakeSink) SendFrame(b []byte) error {
	f.mu.Lock()
	defer f.mu.Unlock()
	if f.closed {
		return errors.New("closed")
	}
	if f.limit > 0 && len(f.frames) >= f.limit {
		f.closed = true
		return errors.New("full")
	}
	f.frames = append(f.frames, append([]byte(nil), b...))
	return nil
}

func (f *fakeSink) Close() {
	f.mu.Lock()
	was := f.closed
	f.closed = true
	f.mu.Unlock()
	if !was && f.onClose != nil {
		f.onClose()
	}
}

func (f *fakeSink) isClosed() bool { f.mu.Lock(); defer f.mu.Unlock(); return f.closed }

// changes decodes the changed envelopes received so far.
func (f *fakeSink) changes(t *testing.T) []*wire.Changed {
	t.Helper()
	f.mu.Lock()
	defer f.mu.Unlock()
	var out []*wire.Changed
	for _, fr := range f.frames {
		e, err := wire.NewFrameReader(bytes.NewReader(fr), wire.MaxFrameAuth).Read()
		if err != nil {
			t.Fatal(err)
		}
		if c, ok := e.(*wire.Changed); ok {
			out = append(out, c)
		}
	}
	return out
}

func (f *fakeSink) reset() { f.mu.Lock(); f.frames = nil; f.mu.Unlock() }

// fakePorts is a port pool in a map.
type fakePorts struct {
	mu       sync.Mutex
	leases   map[string]int
	next     int
	fail     bool
	released []string
}

func newPorts() *fakePorts { return &fakePorts{leases: map[string]int{}, next: 40000} }

func (p *fakePorts) Grant(account, id string, requested *int) (int, error) {
	p.mu.Lock()
	defer p.mu.Unlock()
	key := account + "/" + id
	if requested == nil {
		if _, ok := p.leases[key]; ok {
			delete(p.leases, key)
			p.released = append(p.released, key)
		}
		return 0, nil
	}
	if p.fail {
		return 0, errors.New("none free")
	}
	if cur, ok := p.leases[key]; ok && (*requested == 0 || *requested == cur) {
		return cur, nil
	}
	port := *requested
	if port == 0 {
		p.next++
		port = p.next
	}
	if old, ok := p.leases[key]; ok && old != port {
		p.released = append(p.released, key)
	}
	p.leases[key] = port
	return port, nil
}

func (p *fakePorts) Release(account, id string) {
	p.mu.Lock()
	defer p.mu.Unlock()
	key := account + "/" + id
	if _, ok := p.leases[key]; ok {
		delete(p.leases, key)
		p.released = append(p.released, key)
	}
}

func (p *fakePorts) held(account, id string) bool {
	p.mu.Lock()
	defer p.mu.Unlock()
	_, ok := p.leases[account+"/"+id]
	return ok
}

type hookLog struct {
	mu        sync.Mutex
	resGone   []string
	broGone   []string
	closeDevs []string
}

func (h *hookLog) hooks() Hooks {
	return Hooks{
		ResourceGone:  func(a, id, dev string) { h.mu.Lock(); h.resGone = append(h.resGone, a+"/"+id); h.mu.Unlock() },
		BroadcastGone: func(a, id, dev string) { h.mu.Lock(); h.broGone = append(h.broGone, a+"/"+id); h.mu.Unlock() },
		CloseDevice:   func(id string) { h.mu.Lock(); h.closeDevs = append(h.closeDevs, id); h.mu.Unlock() },
	}
}

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
func (c *clock) Add(d time.Duration) { c.mu.Lock(); c.t = c.t.Add(d); c.mu.Unlock() }

type env struct {
	t     *testing.T
	d     *Dir
	ck    *clock
	ports *fakePorts
	hooks *hookLog
	dir   string
}

func devs() []DeviceInfo {
	return []DeviceInfo{
		{ID: devA1, Account: alice, Name: "Phone", Product: "fms-android", Roles: []string{"resource-producer"}},
		{ID: devA2, Account: alice, Name: "PC", Product: "fms-windows", Roles: []string{"resource-consumer", "receiver"},
			Receiver: json.RawMessage(`{"modes":["AUDIO_ONLY"],"transports":["HTTP_ADTS"],"x":{"y":{"z":1}}}`)},
		{ID: devB1, Account: bob, Name: "Tablet", Product: "streamsplayer-windows"},
	}
}

func newEnv(t *testing.T, mut func(*Options)) *env {
	t.Helper()
	e := &env{t: t, ck: newClock(), ports: newPorts(), hooks: &hookLog{}, dir: t.TempDir()}
	opt := Options{Dir: e.dir, Now: e.ck.Now, Ports: e.ports, Hooks: e.hooks.hooks()}
	if mut != nil {
		mut(&opt)
	}
	e.d = New(opt)
	if err := e.d.Load(devs()); err != nil {
		t.Fatal(err)
	}
	return e
}

func (e *env) online(account, id string) *fakeSink {
	s := &fakeSink{name: id}
	if !e.d.Online(account, id, s) {
		e.t.Fatalf("device %s unknown", id)
	}
	return s
}

func resource(id, dev string, extra string) json.RawMessage {
	return json.RawMessage(`{"resourceId":"` + id + `","deviceId":"` + dev + `","kind":"sftp-share","name":"Photos",` +
		`"access":{"descriptor":"FMSSFTP2.secret-descriptor","root":"/"}` + extra + `}`)
}

func broadcast(id string, extra string) json.RawMessage {
	return json.RawMessage(`{"broadcastId":"` + id + `","mode":"AUDIO_ONLY","title":"Mic","descriptor":{"endpoints":[{"transport":"HTTP_ADTS"}]}` + extra + `}`)
}

func (e *env) publish(account, dev string, raw json.RawMessage) PublishResult {
	e.t.Helper()
	r, err := e.d.Publish(account, dev, raw)
	if err != nil {
		e.t.Fatalf("publish: %v", err)
	}
	return r
}

func recordsOf(raws []json.RawMessage) []*wire.Record {
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

func (e *env) resources(account string) []*wire.Record {
	_, _, rs, _ := e.d.Snapshot(account)
	return recordsOf(rs)
}

// --- isolation ---

func TestTwoAccountsNeverSeeEachOther(t *testing.T) {
	e := newEnv(t, nil)
	a1, b1 := e.online(alice, devA1), e.online(bob, devB1)
	e.d.Subscribe(alice, devA1, a1)
	e.d.Subscribe(bob, devB1, b1)
	b1.reset()
	e.publish(alice, devA1, resource(resR1, devA1, `,"shareId":"`+shrS1+`"`))
	if _, err := e.d.BroadcastStart(alice, devA1, broadcast(broB1, ""), nil); err != nil {
		t.Fatal(err)
	}

	_, bd, br, bb := e.d.Snapshot(bob)
	if len(br) != 0 || len(bb) != 0 || len(bd) != 1 {
		t.Fatalf("bob's directory holds %d resources, %d broadcasts, %d devices", len(br), len(bb), len(bd))
	}
	_, ad, ar, ab := e.d.Snapshot(alice)
	if len(ar) != 1 || len(ab) != 1 || len(ad) != 2 {
		t.Fatalf("alice's directory: %d %d %d", len(ar), len(ab), len(ad))
	}
	if len(b1.changes(t)) != 0 {
		t.Fatal("bob's stream was pushed alice's changes")
	}
	if _, ok := e.d.ResolveResource(bob, resR1); ok {
		t.Fatal("bob resolved alice's resourceId")
	}
	if _, ok := e.d.ResolveResource(alice, resR1); !ok {
		t.Fatal("alice cannot resolve her own")
	}
	// bob cannot touch alice's records
	e.d.Unpublish(bob, devB1, resR1)
	e.d.BroadcastEnd(bob, devB1, broB1)
	if len(e.resources(alice)) != 1 {
		t.Fatal("bob removed alice's resource")
	}
	if e.d.RevokeDevice(bob, devA1) {
		t.Fatal("bob revoked alice's device")
	}
	// a resourceId is looked up only inside an account: bob may use the same number
	e.publish(bob, devB1, resource(resR1, devB1, ""))
	if len(e.resources(bob)) != 1 || len(e.resources(alice)) != 1 {
		t.Fatal("the same resourceId in two accounts must be two resources")
	}
	// but a shareId is a capability, unique per deployment
	if _, err := e.d.Publish(bob, devB1, resource(resR2, devB1, `,"shareId":"`+shrS1+`"`)); !errors.Is(err, ErrIDCollision) {
		t.Fatalf("a shareId held by another account's resource: %v", err)
	}
	// and so is a broadcastId
	if _, err := e.d.BroadcastStart(bob, devB1, broadcast(broB1, ""), nil); !errors.Is(err, ErrIDCollision) {
		t.Fatalf("a broadcastId held by another account: %v", err)
	}
	// the share resolves without naming an account, to the right producer
	tg, ok := e.d.ResolveShare(shrS1)
	if !ok || tg.Account != alice || tg.DeviceID != devA1 || tg.Ctrl != Sink(a1) {
		t.Fatalf("share: %+v %v", tg, ok)
	}
}

// --- a replaced control stream, offline, restart ---

func TestAReplacedControlStreamKeepsResourcesAccessAndBroadcasts(t *testing.T) {
	e := newEnv(t, nil)
	s1 := e.online(alice, devA1)
	e.publish(alice, devA1, resource(resR1, devA1, `,"shareId":"`+shrS1+`","publicPort":0`))
	if _, err := e.d.BroadcastStart(alice, devA1, broadcast(broB1, ""), nil); err != nil {
		t.Fatal(err)
	}
	rev := e.d.Revision(alice)

	s2 := &fakeSink{}
	if !e.d.Online(alice, devA1, s2) {
		t.Fatal("online")
	}
	if !s1.isClosed() {
		t.Fatal("the old control stream must be closed")
	}
	if e.d.Revision(alice) != rev {
		t.Fatal("replacing a stream is not a directory change")
	}
	rs := e.resources(alice)
	if len(rs) != 1 || !rs[0].Has("access") || rs[0].Has("shareId") == false {
		t.Fatalf("the resource lost its access or shareId: %v", rs)
	}
	if p, _ := rs[0].Str("presence"); p != "online" {
		t.Fatal("presence")
	}
	if _, _, _, bs := e.d.Snapshot(alice); len(bs) != 1 {
		t.Fatal("the broadcast did not carry over")
	}
	// the dead stream's exit must not take the device offline
	e.d.Offline(alice, devA1, s1)
	if e.d.Counts().Online != 1 || e.d.Revision(alice) != rev {
		t.Fatal("a stale stream took the device offline")
	}
	// a stale stream cannot subscribe or publish for the device either
	e.d.Subscribe(alice, devA1, s1)
	if e.d.Counts().Subscribers != 0 {
		t.Fatal("a replaced stream subscribed")
	}
	if tg, ok := e.d.ResolveResource(alice, resR1); !ok || tg.Ctrl != Sink(s2) {
		t.Fatal("tunnels must reach the new stream")
	}
}

func TestOfflineDropsAccessEndsBroadcastsAndIsOneTransaction(t *testing.T) {
	e := newEnv(t, nil)
	prod := e.online(alice, devA1)
	watch := e.online(alice, devA2)
	e.d.Subscribe(alice, devA2, watch)
	e.publish(alice, devA1, resource(resR1, devA1, `,"shareId":"`+shrS1+`","publicPort":0`))
	e.publish(alice, devA1, resource(resR2, devA1, ""))
	e.d.BroadcastStart(alice, devA1, broadcast(broB1, ""), nil)
	e.d.BroadcastStart(alice, devA1, broadcast(broB2, ""), nil)
	if !e.ports.held(alice, resR1) {
		t.Fatal("no lease")
	}
	watch.reset()
	rev := e.d.Revision(alice)

	e.d.Offline(alice, devA1, prod)

	if e.d.Revision(alice) != rev+1 {
		t.Fatalf("one offline is one transaction: %d -> %d", rev, e.d.Revision(alice))
	}
	chs := watch.changes(t)
	if len(chs) != 1 || chs[0].Revision != rev+1 {
		t.Fatalf("pushes: %d", len(chs))
	}
	c := chs[0]
	if len(c.Upserts.Devices) != 1 || len(c.Upserts.Resources) != 2 || len(c.Removals.BroadcastIDs) != 2 {
		t.Fatalf("the changed holds %d devices, %d resources up and %d broadcasts out", len(c.Upserts.Devices), len(c.Upserts.Resources), len(c.Removals.BroadcastIDs))
	}
	for _, r := range recordsOf(c.Upserts.Resources) {
		if p, _ := r.Str("presence"); p != "offline" || r.Has("access") || r.Has("shareId") || r.Has("publicPort") {
			t.Fatalf("an offline resource must show offline and no live members: %s", r.Marshal())
		}
	}
	if len(e.resources(alice)) != 2 {
		t.Fatal("offline keeps the resources in the directory")
	}
	if e.ports.held(alice, resR1) {
		t.Fatal("the port lease outlived the device")
	}
	if _, ok := e.d.ResolveShare(shrS1); ok {
		t.Fatal("the shareId must not resolve while the producer is offline")
	}
	if _, ok := e.d.ResolveResource(alice, resR2); ok {
		t.Fatal("an offline resource resolved")
	}
	if _, _, _, bs := e.d.Snapshot(alice); len(bs) != 0 {
		t.Fatal("broadcasts must end with their device")
	}
	if len(e.hooks.resGone) != 2 || len(e.hooks.broGone) != 2 {
		t.Fatalf("hooks: %v %v", e.hooks.resGone, e.hooks.broGone)
	}
	// coming back: online again, still no access until it publishes
	e.online(alice, devA1)
	for _, r := range e.resources(alice) {
		if p, _ := r.Str("presence"); p != "online" || r.Has("access") {
			t.Fatalf("a returning producer's resources are online without access: %s", r.Marshal())
		}
	}
	// a second offline of an already offline device is nothing
	e.d.Offline(alice, devA2, watch)
	r2 := e.d.Revision(alice)
	e.d.Offline(alice, devA2, watch)
	if e.d.Revision(alice) != r2 {
		t.Fatal("offline twice changed the revision twice")
	}
}

func TestARestartServesEveryResourceOfflineWithoutAccessOrShareId(t *testing.T) {
	e := newEnv(t, nil)
	prod := e.online(alice, devA1)
	e.publish(alice, devA1, resource(resR1, devA1, `,"shareId":"`+shrS1+`","publicPort":0,"future":{"a":{"b":{"c":1}}}`))
	e.d.BroadcastStart(alice, devA1, broadcast(broB1, ""), nil)
	_ = prod
	if err := e.d.Flush(); err != nil {
		t.Fatal(err)
	}
	persisted := e.d.Revision(alice)

	// the file holds the record and the revision - and none of the live members
	raw, err := os.ReadFile(filepath.Join(e.dir, File))
	if err != nil {
		t.Fatal(err)
	}
	for _, needle := range []string{"secret-descriptor", "FMSSFTP2", shrS1, broB1, `"access"`, `"shareId"`, `"publicPort"`, `"presence"`} {
		if bytes.Contains(raw, []byte(needle)) {
			t.Errorf("directory.json holds %q:\n%s", needle, raw)
		}
	}
	var flat bytes.Buffer
	_ = json.Compact(&flat, raw)
	if !bytes.Contains(raw, []byte(resR1)) || !bytes.Contains(flat.Bytes(), []byte(`"c":1`)) {
		t.Fatalf("directory.json lost the record or its unknown members:\n%s", raw)
	}

	// kill, restart
	d2 := New(Options{Dir: e.dir, Now: e.ck.Now, Ports: e.ports})
	if err := d2.Load(devs()); err != nil {
		t.Fatal(err)
	}
	if d2.Revision(alice) != persisted+1 {
		t.Fatalf("revision after a start is persisted + 1: %d, persisted %d", d2.Revision(alice), persisted)
	}
	_, _, rs, bs := d2.Snapshot(alice)
	if len(rs) != 1 || len(bs) != 0 {
		t.Fatalf("restart: %d resources, %d broadcasts", len(rs), len(bs))
	}
	rec := recordsOf(rs)[0]
	if p, _ := rec.Str("presence"); p != "offline" || rec.Has("access") || rec.Has("shareId") || rec.Has("publicPort") {
		t.Fatalf("a restarted resource is offline with no live member: %s", rec.Marshal())
	}
	if string(rec.Raw("future")) != `{"a":{"b":{"c":1}}}` {
		t.Fatalf("an unknown member did not survive the disk: %s", rec.Raw("future"))
	}
	if _, ok := d2.ResolveShare(shrS1); ok {
		t.Fatal("a shareId survived a restart")
	}
	// the producer comes back and publishes again: its consumer sees access again
	s := &fakeSink{}
	d2.Online(alice, devA1, s)
	if _, err := d2.Publish(alice, devA1, resource(resR1, devA1, "")); err != nil {
		t.Fatalf("the same device publishing a held resourceId replaces it: %v", err)
	}
	_, _, rs, _ = d2.Snapshot(alice)
	if !recordsOf(rs)[0].Has("access") {
		t.Fatal("access did not come back")
	}
}

func TestTheRevisionIsPersistedPlusOneAfterALostWrite(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	e.publish(alice, devA1, resource(resR1, devA1, ""))
	if err := e.d.Flush(); err != nil {
		t.Fatal(err)
	}
	persisted := e.d.Revision(alice)
	for i := 0; i < 5; i++ { // changes whose write is lost: the process dies before the next tick
		e.d.Unpublish(alice, devA1, resR1)
		e.publish(alice, devA1, resource(resR1, devA1, ""))
	}
	if e.d.Revision(alice) <= persisted+5 {
		t.Fatalf("the revision did not grow: %d", e.d.Revision(alice))
	}
	d2 := New(Options{Dir: e.dir, Now: e.ck.Now})
	if err := d2.Load(devs()); err != nil {
		t.Fatal(err)
	}
	if d2.Revision(alice) != persisted+1 {
		t.Fatalf("after a lost write the start revision is the persisted one plus one: %d vs %d", d2.Revision(alice), persisted)
	}
}

func TestRevisionIsWrittenLazilyButResourcesAtOnce(t *testing.T) {
	writes := 0
	e := newEnv(t, func(o *Options) {
		o.WriteTemp = func(name string, data []byte, perm os.FileMode) error {
			writes++
			return os.WriteFile(name, data, perm)
		}
	})
	e.online(alice, devA1)
	if err := e.d.Persist(e.ck.Now()); err != nil {
		t.Fatal(err)
	}
	base := writes
	// presence flaps change the revision only: at most one write a second
	for i := 0; i < 10; i++ {
		s := &fakeSink{}
		e.d.Online(alice, devA2, s)
		e.d.Offline(alice, devA2, s)
		e.d.Persist(e.ck.Now())
	}
	if writes != base {
		t.Fatalf("%d writes inside a second for a revision alone", writes-base)
	}
	e.ck.Add(1100 * time.Millisecond)
	e.d.Persist(e.ck.Now())
	if writes != base+1 {
		t.Fatalf("the lazy revision write: %d", writes-base)
	}
	// a resource record is written at once
	e.publish(alice, devA1, resource(resR1, devA1, ""))
	e.d.Persist(e.ck.Now())
	if writes != base+2 {
		t.Fatalf("a resource must be written at once: %d", writes-base)
	}
}

// --- changed: revisions, gaps, slow subscribers ---

func TestEveryTransactionIsOneRevisionAndOnePush(t *testing.T) {
	e := newEnv(t, nil)
	prod := e.online(alice, devA1)
	obs := e.online(alice, devA2)
	e.d.Subscribe(alice, devA2, obs)
	for i := 0; i < 12; i++ {
		id := wire.NewID()
		e.publish(alice, devA1, resource(id, devA1, ""))
	}
	e.d.BroadcastStart(alice, devA1, broadcast(broB1, ""), nil)
	obs.reset()
	rev := e.d.Revision(alice)

	if !e.d.RevokeDevice(alice, devA1) {
		t.Fatal("revoke")
	}
	_ = prod
	chs := obs.changes(t)
	if len(chs) != 1 || chs[0].Revision != rev+1 {
		t.Fatalf("a revoke that removes fourteen things is one revision and one changed: %d pushes, revision %d -> %d", len(chs), rev, e.d.Revision(alice))
	}
	c := chs[0]
	if len(c.Removals.DeviceIDs) != 1 || len(c.Removals.ResourceIDs) != 12 || len(c.Removals.BroadcastIDs) != 1 {
		t.Fatalf("removals: %+v", c.Removals)
	}
	if len(e.hooks.closeDevs) != 1 || e.hooks.closeDevs[0] != devA1 || !prod.isClosed() {
		t.Fatal("the revoked device's streams must be closed")
	}
	if e.ports.held(alice, resR1) || len(e.hooks.resGone) != 12 {
		t.Fatalf("hooks: %d resources gone", len(e.hooks.resGone))
	}
	if len(e.resources(alice)) != 0 || e.d.Counts().Devices != 2 {
		t.Fatal("the revoked device's records are still listed")
	}
}

// resyncClient is a client that trusts a gap in the revision to mean it missed something.
type resyncClient struct {
	last    int64
	resyncs int
}

func (c *resyncClient) apply(rev int64, list func() int64) {
	if c.last != 0 && rev != c.last+1 {
		c.resyncs++
		c.last = list()
		return
	}
	c.last = rev
}

func TestAGapInChangedIsDetectableAndAListResyncs(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	obs := &fakeSink{limit: 3}
	if !e.d.Online(alice, devA2, obs) {
		t.Fatal()
	}
	e.d.Subscribe(alice, devA2, obs)
	cl := &resyncClient{}
	list := func() int64 { r, _, _, _ := e.d.Snapshot(alice); return r }
	cl.last = list()
	for i := 0; i < 8; i++ {
		e.publish(alice, devA1, resource(wire.NewID(), devA1, ""))
	}
	// the stream took three pushes, then overflowed: it is closed, and unsubscribed
	if !obs.isClosed() || e.d.Counts().Subscribers != 0 {
		t.Fatalf("a slow subscriber must be closed and dropped (closed %v, %d subscribers)", obs.isClosed(), e.d.Counts().Subscribers)
	}
	chs := obs.changes(t)
	if len(chs) != 3 {
		t.Fatalf("%d pushes", len(chs))
	}
	for _, c := range chs {
		cl.apply(c.Revision, list)
	}
	if cl.resyncs != 0 {
		t.Fatal("contiguous pushes are no gap")
	}
	// the client reconnects, and is told revision N: that is a gap against the last it saw
	cl.apply(e.d.Revision(alice), list)
	if cl.resyncs != 1 || cl.last != e.d.Revision(alice) {
		t.Fatalf("the gap was not noticed: %+v", cl)
	}
}

func TestASlowSubscriberDoesNotGrowMemoryOrBlockTheDirectory(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	var subs []*fakeSink
	for i := 0; i < 2; i++ {
		s := &fakeSink{limit: 2}
		id := []string{devA2, devA1}[i]
		if i == 1 {
			continue
		}
		e.d.Online(alice, id, s)
		e.d.Subscribe(alice, id, s)
		subs = append(subs, s)
	}
	done := make(chan struct{})
	go func() {
		for i := 0; i < 200; i++ {
			e.publish(alice, devA1, resource(wire.NewID(), devA1, ""))
		}
		close(done)
	}()
	select {
	case <-done:
	case <-time.After(10 * time.Second):
		t.Fatal("a slow subscriber stalled the directory")
	}
	s := subs[0]
	s.mu.Lock()
	n := len(s.frames)
	s.mu.Unlock()
	if n > 2 {
		t.Fatalf("the sink took %d frames past its bound", n)
	}
}

// --- publish ---

func TestUnknownMembersThreeLevelsDeepSurviveResourceAndBroadcast(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	e.publish(alice, devA1, resource(resR1, devA1, `,"future":{"a":{"b":{"c":[1,{"d":"<&>"}]}}}`))
	if _, err := e.d.BroadcastStart(alice, devA1, broadcast(broB1, `,"future":{"x":{"y":{"z":true}}}`), nil); err != nil {
		t.Fatal(err)
	}
	_, _, rs, bs := e.d.Snapshot(alice)
	if r := recordsOf(rs)[0]; string(r.Raw("future")) != `{"a":{"b":{"c":[1,{"d":"<&>"}]}}}` {
		t.Fatalf("resource: %s", r.Raw("future"))
	}
	if b := recordsOf(bs)[0]; string(b.Raw("future")) != `{"x":{"y":{"z":true}}}` ||
		!bytes.Contains(b.Raw("descriptor"), []byte(`"transport":"HTTP_ADTS"`)) {
		t.Fatalf("broadcast: %s", b.Marshal())
	}
	// the device record keeps its receiver block verbatim too
	_, ds, _, _ := e.d.Snapshot(alice)
	for _, r := range recordsOf(ds) {
		if id, _ := r.Str("deviceId"); id == devA2 && string(r.Raw("receiver")) != `{"modes":["AUDIO_ONLY"],"transports":["HTTP_ADTS"],"x":{"y":{"z":1}}}` {
			t.Fatalf("receiver: %s", r.Raw("receiver"))
		}
	}
}

func TestPublishChecksTheRecordButDoesNotInterpretIt(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	bad := map[string]string{
		"not an object":      `"x"`,
		"no resourceId":      `{"deviceId":"` + devA1 + `","kind":"sftp-share","name":"n"}`,
		"a bad resourceId":   `{"resourceId":"short","kind":"sftp-share","name":"n"}`,
		"no kind":            `{"resourceId":"` + resR1 + `","name":"n"}`,
		"no name":            `{"resourceId":"` + resR1 + `","kind":"sftp-share"}`,
		"a bad shareId":      `{"resourceId":"` + resR1 + `","kind":"k","name":"n","shareId":"nope"}`,
		"a bad port":         `{"resourceId":"` + resR1 + `","kind":"k","name":"n","publicPort":"x"}`,
		"a negative port":    `{"resourceId":"` + resR1 + `","kind":"k","name":"n","publicPort":-1}`,
		"a port above 65535": `{"resourceId":"` + resR1 + `","kind":"k","name":"n","publicPort":70000}`,
	}
	for name, raw := range bad {
		if _, err := e.d.Publish(alice, devA1, json.RawMessage(raw)); !errors.Is(err, ErrMalformed) {
			t.Errorf("%s: %v", name, err)
		}
	}
	// over 16 KiB: capacity
	big := `{"resourceId":"` + resR1 + `","kind":"k","name":"n","pad":"` + strings.Repeat("a", wire.MaxRecord) + `"}`
	_, err := e.d.Publish(alice, devA1, json.RawMessage(big))
	if Reason(err) != wire.ReasonCapacity {
		t.Fatalf("a record above 16 KiB: %v", err)
	}
	// exactly 16 KiB is accepted
	pad := wire.MaxRecord - len(`{"resourceId":"`+resR1+`","kind":"k","name":"n","pad":""}`)
	exact := `{"resourceId":"` + resR1 + `","kind":"k","name":"n","pad":"` + strings.Repeat("a", pad) + `"}`
	if _, err := e.d.Publish(alice, devA1, json.RawMessage(exact)); err != nil {
		t.Fatalf("a record of exactly 16 KiB: %v", err)
	}
	// an unknown kind is stored as is
	if _, err := e.d.Publish(alice, devA1, json.RawMessage(`{"resourceId":"`+resR2+`","kind":"hologram","name":"n"}`)); err != nil {
		t.Fatalf("kinds are additive: %v", err)
	}
	// a device that is not online (no control stream) cannot publish
	if _, err := e.d.Publish(alice, devA2, resource(resR2, devA2, "")); !errors.Is(err, ErrNoDevice) {
		t.Fatalf("an offline publisher: %v", err)
	}
	if _, err := e.d.Publish("nobody", devA1, resource(resR1, devA1, "")); !errors.Is(err, ErrNoDevice) {
		t.Fatalf("an unknown account: %v", err)
	}
}

func TestDeviceIDIsForcedToThePublisherAndUpdatedAtIsServers(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	e.publish(alice, devA1, resource(resR1, devA2, `,"updatedAt":"1999-01-01T00:00:00Z"`)) // claims another device
	r := e.resources(alice)[0]
	if id, _ := r.Str("deviceId"); id != devA1 {
		t.Fatalf("deviceId %q", id)
	}
	if at, _ := r.Str("updatedAt"); at != wire.FormatTime(e.ck.Now()) {
		t.Fatalf("updatedAt %q", at)
	}
}

func TestPublishCollisionsAndReplacement(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	e.online(alice, devA2)
	e.publish(alice, devA1, resource(resR1, devA1, `,"shareId":"`+shrS1+`"`))
	// another device of the account holding the resourceId
	if _, err := e.d.Publish(alice, devA2, resource(resR1, devA2, "")); !errors.Is(err, ErrIDCollision) {
		t.Fatalf("a resourceId held by another device: %v", err)
	}
	// a shareId held by another resource
	if _, err := e.d.Publish(alice, devA2, resource(resR2, devA2, `,"shareId":"`+shrS1+`"`)); !errors.Is(err, ErrIDCollision) {
		t.Fatalf("a held shareId: %v", err)
	}
	// the same device replaces, and may change or drop its shareId
	rev := e.d.Revision(alice)
	e.publish(alice, devA1, resource(resR1, devA1, `,"name2":"changed"`))
	if _, ok := e.d.ResolveShare(shrS1); ok {
		t.Fatal("a dropped shareId must stop resolving")
	}
	if e.d.Revision(alice) != rev+1 || len(e.resources(alice)) != 1 {
		t.Fatal("a replacement is one transaction and one resource")
	}
	// the freed shareId can be taken by another resource
	if _, err := e.d.Publish(alice, devA2, resource(resR2, devA2, `,"shareId":"`+shrS1+`"`)); err != nil {
		t.Fatalf("a freed shareId: %v", err)
	}
	if _, err := e.d.Publish(alice, devA1, resource(resR1, devA1, `,"shareId":"`+shrS1+`"`)); !errors.Is(err, ErrIDCollision) {
		t.Fatalf("taking a shareId another resource now holds: %v", err)
	}
}

func TestPublicPortIsGrantedBeforePublishedAndTheRecordShowsIt(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	r := e.publish(alice, devA1, resource(resR1, devA1, `,"publicPort":0`))
	if r.PublicPort == nil || *r.PublicPort <= 0 || !e.ports.held(alice, resR1) {
		t.Fatalf("%+v", r)
	}
	rec := e.resources(alice)[0]
	if n, _ := rec.Int("publicPort"); n != *r.PublicPort {
		t.Fatalf("the record must show the granted port %d, not the request: %s", *r.PublicPort, rec.Marshal())
	}
	// asking again for the number it was granted keeps it
	r2 := e.publish(alice, devA1, resource(resR1, devA1, fmt.Sprintf(`,"publicPort":%d`, *r.PublicPort)))
	if r2.PublicPort == nil || *r2.PublicPort != *r.PublicPort {
		t.Fatalf("a producer that asks again for its port: %+v", r2)
	}
	// not asking releases it
	r3 := e.publish(alice, devA1, resource(resR1, devA1, ""))
	if r3.PublicPort != nil || e.ports.held(alice, resR1) || e.resources(alice)[0].Has("publicPort") {
		t.Fatalf("a record that asks for no port holds none: %+v", r3)
	}
	// unpublish releases
	e.publish(alice, devA1, resource(resR1, devA1, `,"publicPort":0`))
	e.d.Unpublish(alice, devA1, resR1)
	if e.ports.held(alice, resR1) {
		t.Fatal("unpublish must release the port")
	}
	// no port free: port-unavailable, and nothing is stored
	e.ports.fail = true
	rev := e.d.Revision(alice)
	if _, err := e.d.Publish(alice, devA1, resource(resR2, devA1, `,"publicPort":0`)); Reason(err) != wire.ReasonPortUnavailable {
		t.Fatalf("%v", err)
	}
	if e.d.Revision(alice) != rev || len(e.resources(alice)) != 0 {
		t.Fatal("a refused publish must store nothing")
	}
	// a Dir with no pool refuses a port request too
	d := New(Options{Now: e.ck.Now})
	d.Load(devs())
	d.Online(alice, devA1, &fakeSink{})
	if _, err := d.Publish(alice, devA1, resource(resR1, devA1, `,"publicPort":0`)); Reason(err) != wire.ReasonPortUnavailable {
		t.Fatalf("no pool: %v", err)
	}
}

func TestUnpublishIsForTheOwnerOnlyAndIgnoresTheRest(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	e.online(alice, devA2)
	e.publish(alice, devA1, resource(resR1, devA1, ""))
	e.d.Unpublish(alice, devA2, resR1) // not this device's
	e.d.Unpublish(alice, devA1, resR2) // unknown
	if len(e.resources(alice)) != 1 {
		t.Fatal("a foreign or unknown unpublish removed something")
	}
	rev := e.d.Revision(alice)
	e.d.Unpublish(alice, devA1, resR1)
	if len(e.resources(alice)) != 0 || e.d.Revision(alice) != rev+1 || len(e.hooks.resGone) != 1 {
		t.Fatal("unpublish")
	}
}

func TestTheDirectoryBudgetRefusesWithCapacityAndTheFrameStillFits(t *testing.T) {
	e := newEnv(t, func(o *Options) {
		o.Caps = Caps{ResourcesPerAccount: 1000, BroadcastsPerAccount: 16, BroadcastsPerDevice: 4}
	})
	e.online(alice, devA1)
	pad := strings.Repeat("a", 15000)
	accepted := 0
	for i := 0; i < 40; i++ {
		_, err := e.d.Publish(alice, devA1, json.RawMessage(fmt.Sprintf(`{"resourceId":"%s","kind":"k","name":"n","pad":"%s"}`, wire.NewID(), pad)))
		if err != nil {
			if Reason(err) != wire.ReasonCapacity {
				t.Fatalf("%v", err)
			}
			break
		}
		accepted++
	}
	if accepted < 10 || accepted >= 40 {
		t.Fatalf("%d records of 15 KB accepted: the budget is a frame of %d", accepted, wire.MaxFrameAuth)
	}
	rev, ds, rs, bs := e.d.Snapshot(alice)
	body, err := wire.Encode(&wire.Directory{Revision: rev, Devices: ds, Resources: rs, Broadcasts: bs})
	if err != nil || len(body) > wire.MaxFrameAuth {
		t.Fatalf("the directory the server accepted does not fit a frame: %d bytes, %v", len(body), err)
	}
	// a broadcast that would not fit is refused the same way
	_, err = e.d.BroadcastStart(alice, devA1, broadcast(broB1, `,"pad":"`+pad+`"`), nil)
	if Reason(err) != wire.ReasonCapacity {
		t.Fatalf("a broadcast past the budget: %v", err)
	}
}

func TestCaps(t *testing.T) {
	e := newEnv(t, func(o *Options) {
		o.Caps = Caps{ResourcesPerAccount: 3, BroadcastsPerAccount: 3, BroadcastsPerDevice: 2}
	})
	e.online(alice, devA1)
	e.online(alice, devA2)
	for i := 0; i < 3; i++ {
		e.publish(alice, devA1, resource(wire.NewID(), devA1, ""))
	}
	if _, err := e.d.Publish(alice, devA1, resource(wire.NewID(), devA1, "")); !errors.Is(err, ErrLimit) {
		t.Fatalf("the fourth resource: %v", err)
	}
	// replacing at the cap is fine
	first := e.resources(alice)[0]
	id, _ := first.Str("resourceId")
	e.publish(alice, devA1, resource(id, devA1, ""))
	// broadcasts: 2 per device, 3 per account
	for i := 0; i < 2; i++ {
		if _, err := e.d.BroadcastStart(alice, devA1, broadcast(wire.NewID(), ""), nil); err != nil {
			t.Fatal(err)
		}
	}
	if _, err := e.d.BroadcastStart(alice, devA1, broadcast(wire.NewID(), ""), nil); !errors.Is(err, ErrLimit) {
		t.Fatalf("the third broadcast of a device: %v", err)
	}
	if _, err := e.d.BroadcastStart(alice, devA2, broadcast(wire.NewID(), ""), nil); err != nil {
		t.Fatal(err)
	}
	if _, err := e.d.BroadcastStart(alice, devA2, broadcast(wire.NewID(), ""), nil); !errors.Is(err, ErrLimit) {
		t.Fatalf("the fourth broadcast of the account: %v", err)
	}
}

// --- broadcasts ---

func TestBroadcastRecordsLiveInMemoryAndFollowTheRules(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	e.online(alice, devA2)
	for name, raw := range map[string]string{
		"no mode":                            `{"broadcastId":"` + broB1 + `","descriptor":{}}`,
		"no descriptor":                      `{"broadcastId":"` + broB1 + `","mode":"AUDIO_ONLY"}`,
		"a descriptor that is not an object": `{"broadcastId":"` + broB1 + `","mode":"AUDIO_ONLY","descriptor":"x"}`,
		"a bad id":                           `{"broadcastId":"nope","mode":"AUDIO_ONLY","descriptor":{}}`,
	} {
		if _, err := e.d.BroadcastStart(alice, devA1, json.RawMessage(raw), nil); !errors.Is(err, ErrMalformed) {
			t.Errorf("%s: %v", name, err)
		}
	}
	for _, n := range []int{-1, 10001} {
		n := n
		if _, err := e.d.BroadcastStart(alice, devA1, broadcast(broB1, ""), &n); !errors.Is(err, ErrMalformed) {
			t.Errorf("maxListeners %d: %v", n, err)
		}
	}
	four, zero, top := 4, 0, 10000
	if id, err := e.d.BroadcastStart(alice, devA1, broadcast(broB1, ""), &four); err != nil || id != broB1 {
		t.Fatal(err)
	}
	if tg, ok := e.d.ResolveBroadcast(broB1); !ok || tg.MaxListeners != 4 || tg.DeviceID != devA1 {
		t.Fatalf("%+v", tg)
	}
	rec := recordsOf(mustBroadcasts(e, alice))[0]
	if s, _ := rec.Str("startedAt"); s != wire.FormatTime(e.ck.Now()) {
		t.Fatalf("startedAt is stamped by the server when the producer sends none: %q", s)
	}
	if id, _ := rec.Str("deviceId"); id != devA1 {
		t.Fatal("deviceId")
	}
	// another device may not take the id; the same device replaces
	if _, err := e.d.BroadcastStart(alice, devA2, broadcast(broB1, ""), nil); !errors.Is(err, ErrIDCollision) {
		t.Fatalf("%v", err)
	}
	if _, err := e.d.BroadcastStart(alice, devA1, broadcast(broB1, `,"title":"Second"`), &top); err != nil {
		t.Fatalf("a restart of a held broadcastId by its device: %v", err)
	}
	if len(mustBroadcasts(e, alice)) != 1 {
		t.Fatal("a replacement must not add a broadcast")
	}
	// update: replaces the record and, when present, the cap; foreign and unknown are ignored
	e.d.BroadcastUpdate(alice, devA1, broadcast(broB1, `,"title":"Third"`), &zero)
	if tg, _ := e.d.ResolveBroadcast(broB1); tg.MaxListeners != 0 {
		t.Fatal("maxListeners on an update")
	}
	e.d.BroadcastUpdate(alice, devA1, broadcast(broB1, `,"title":"Fourth"`), nil)
	if tg, _ := e.d.ResolveBroadcast(broB1); tg.MaxListeners != 0 {
		t.Fatal("an update without maxListeners must leave it")
	}
	if r := recordsOf(mustBroadcasts(e, alice))[0]; func() string { s, _ := r.Str("title"); return s }() != "Fourth" {
		t.Fatal("the update did not replace the record")
	}
	if err := e.d.BroadcastUpdate(alice, devA2, broadcast(broB1, `,"title":"Hijack"`), nil); err != nil {
		t.Fatal(err)
	}
	if r := recordsOf(mustBroadcasts(e, alice))[0]; func() string { s, _ := r.Str("title"); return s }() != "Fourth" {
		t.Fatal("another device updated the broadcast")
	}
	if err := e.d.BroadcastUpdate(alice, devA1, broadcast(broB2, ""), nil); err != nil {
		t.Fatal("an update of an unknown broadcast is ignored")
	}
	// end: the owner only
	e.d.BroadcastEnd(alice, devA2, broB1)
	if len(mustBroadcasts(e, alice)) != 1 {
		t.Fatal("a foreign end")
	}
	e.d.BroadcastEnd(alice, devA1, broB1)
	if len(mustBroadcasts(e, alice)) != 0 {
		t.Fatal("end")
	}
	if _, ok := e.d.ResolveBroadcast(broB1); ok {
		t.Fatal("an ended broadcast resolved")
	}
	if len(e.hooks.broGone) != 1 {
		t.Fatalf("hook: %v", e.hooks.broGone)
	}
	// and no broadcast is ever on disk
	e.d.BroadcastStart(alice, devA1, broadcast(broB2, ""), nil)
	e.d.Flush()
	raw, _ := os.ReadFile(filepath.Join(e.dir, File))
	if bytes.Contains(raw, []byte(broB2)) || bytes.Contains(raw, []byte("Mic")) {
		t.Fatalf("a broadcast reached the disk:\n%s", raw)
	}
}

func mustBroadcasts(e *env, account string) []json.RawMessage {
	_, _, _, bs := e.d.Snapshot(account)
	return bs
}

// --- devices, revoke, removal ---

func TestDeviceEnrolledAddsOfflineAndARotationDropsTheOldStream(t *testing.T) {
	e := newEnv(t, nil)
	nd := DeviceInfo{ID: "AAAAAAAAAAAAAAAAAAAAAw", Account: alice, Name: "Watch", Product: "fms-wear"}
	obs := e.online(alice, devA2)
	e.d.Subscribe(alice, devA2, obs)
	obs.reset()
	e.d.DeviceEnrolled(nd)
	chs := obs.changes(t)
	if len(chs) != 1 || len(chs[0].Upserts.Devices) != 1 {
		t.Fatalf("a new device is announced: %d", len(chs))
	}
	if p, _ := recordsOf(chs[0].Upserts.Devices)[0].Str("presence"); p != "offline" {
		t.Fatal("a new device is offline until its control stream arrives")
	}

	// a rotated token: the device's control stream closes, its resources lose access, hooks fire
	prod := e.online(alice, devA1)
	e.publish(alice, devA1, resource(resR1, devA1, `,"publicPort":0`))
	e.d.BroadcastStart(alice, devA1, broadcast(broB1, ""), nil)
	obs.reset()
	e.d.DeviceEnrolled(devs()[0])
	if !prod.isClosed() || e.ports.held(alice, resR1) {
		t.Fatal("a rotation must close the old stream and release the lease")
	}
	if len(e.hooks.closeDevs) != 1 || e.hooks.closeDevs[0] != devA1 {
		t.Fatalf("CloseDevice: %v", e.hooks.closeDevs)
	}
	r := e.resources(alice)[0]
	if p, _ := r.Str("presence"); p != "offline" || r.Has("access") {
		t.Fatal("the rotated device's resource must be offline without access")
	}
	if len(mustBroadcasts(e, alice)) != 0 {
		t.Fatal("its broadcast must end")
	}
	if e.d.Counts().Devices != 4 {
		t.Fatalf("%d devices", e.d.Counts().Devices)
	}
	// the dead stream's exit is a no-op
	e.d.Offline(alice, devA1, prod)
	// Online of an unknown device is refused
	if e.d.Online(alice, "ZZZZZZZZZZZZZZZZZZZZZA", &fakeSink{}) {
		t.Fatal("an unknown device went online")
	}
}

func TestRemoveAccountDropsTheWholeHub(t *testing.T) {
	e := newEnv(t, nil)
	s1 := e.online(alice, devA1)
	s2 := e.online(alice, devA2)
	e.d.Subscribe(alice, devA2, s2)
	b := e.online(bob, devB1)
	e.publish(alice, devA1, resource(resR1, devA1, `,"shareId":"`+shrS1+`","publicPort":0`))
	e.d.BroadcastStart(alice, devA1, broadcast(broB1, ""), nil)
	e.publish(bob, devB1, resource(resR2, devB1, ""))
	e.d.RemoveAccount(alice)
	if !s1.isClosed() || !s2.isClosed() || b.isClosed() {
		t.Fatal("exactly the account's streams close")
	}
	if e.ports.held(alice, resR1) {
		t.Fatal("port")
	}
	if _, ok := e.d.ResolveShare(shrS1); ok {
		t.Fatal("share")
	}
	if _, ok := e.d.ResolveBroadcast(broB1); ok {
		t.Fatal("broadcast")
	}
	if len(e.hooks.closeDevs) != 2 {
		t.Fatalf("CloseDevice: %v", e.hooks.closeDevs)
	}
	if rev, ds, rs, bs := e.d.Snapshot(alice); rev != 0 || len(ds)+len(rs)+len(bs) != 0 {
		t.Fatal("the hub is still there")
	}
	if len(e.resources(bob)) != 1 {
		t.Fatal("another account was touched")
	}
	e.d.Flush()
	raw, _ := os.ReadFile(filepath.Join(e.dir, File))
	if bytes.Contains(raw, []byte(resR1)) || bytes.Contains(raw, []byte(`"alice"`)) {
		t.Fatalf("the removed account is still on disk:\n%s", raw)
	}
}

func TestLoadDropsRecordsOfDevicesThatNoLongerExist(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	e.publish(alice, devA1, resource(resR1, devA1, ""))
	e.d.Flush()
	d2 := New(Options{Dir: e.dir, Now: e.ck.Now})
	if err := d2.Load([]DeviceInfo{devs()[1], devs()[2]}); err != nil { // devA1 was revoked offline
		t.Fatal(err)
	}
	if _, _, rs, _ := d2.Snapshot(alice); len(rs) != 0 {
		t.Fatal("a record of a removed device came back")
	}
	if err := d2.Flush(); err != nil {
		t.Fatal(err)
	}
	raw, _ := os.ReadFile(filepath.Join(e.dir, File))
	if bytes.Contains(raw, []byte(resR1)) {
		t.Fatal("and it must leave the disk")
	}
}

func TestLoadRefusesANewerOrBrokenStore(t *testing.T) {
	dir := t.TempDir()
	os.WriteFile(filepath.Join(dir, File), []byte(`{"storeVersion":2}`), 0o600)
	if err := New(Options{Dir: dir}).Load(nil); !errors.Is(err, ErrNewerStore) {
		t.Fatalf("%v", err)
	}
	os.WriteFile(filepath.Join(dir, File), []byte(`{broken`), 0o600)
	if err := New(Options{Dir: dir}).Load(nil); err == nil {
		t.Fatal("a broken store loaded")
	}
}

func TestReasonMapping(t *testing.T) {
	for err, want := range map[error]wire.Reason{
		ErrIDCollision: wire.ReasonIDCollision, ErrCapacity: wire.ReasonCapacity, wire.ErrRecordSize: wire.ReasonCapacity,
		ErrPortUnavail: wire.ReasonPortUnavailable, ErrMalformed: "", ErrNoDevice: "", nil: "",
	} {
		if got := Reason(err); got != want {
			t.Errorf("Reason(%v) = %q, want %q", err, got, want)
		}
	}
}
