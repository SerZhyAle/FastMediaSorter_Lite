package main

import (
	"bytes"
	"crypto/sha256"
	"encoding/binary"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io/fs"
	"net"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/connstr"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Specification 252 T-1: the corpora of DEVICE-EXCHANGE that the contracts catalog holds and this product has
// adopted by reference are vendored byte for byte in testdata/catalog/ (never generated here, never edited:
// cmd/exchange-vectors writes only testdata/vectors/) and replayed against the server's own implementation the way
// gen_test.go replays the generated ones: the frame codec for the frames files, internal/connstr for the
// connection strings, and - because the records file is a request/verdict corpus - the real server (the rig of this
// command) for the records.

const catalogDir = "../../testdata/catalog"

// the catalog corpora, as they are named under testdata/catalog/
var (
	catalogFramesFiles = []string{"server-frames-v2.json", "fmsw-224/frames-v2.json", "fmsw-229/frames-v2.json",
		"fmsw-234/frames-v2.json", "fmsw-245/frames-v2.json"}
	catalogConnFile    = "fmsw-224/connection-string-v1.json"
	catalogRecordsFile = "server-records-v1.json"
)

type catalogLine struct{ sha, local, source string }

// catalogProvenance reads the vendored list: `<sha256>  <path under testdata/catalog>  <catalog-relative source>`.
func catalogProvenance(t *testing.T) []catalogLine {
	t.Helper()
	b, err := os.ReadFile(filepath.Join(catalogDir, "PROVENANCE.txt"))
	if err != nil {
		t.Fatalf("testdata/catalog/PROVENANCE.txt is missing: %v", err)
	}
	var out []catalogLine
	for _, line := range strings.Split(string(b), "\n") {
		f := strings.Fields(strings.TrimRight(line, "\r"))
		if len(f) != 3 || len(f[0]) != 64 {
			continue
		}
		if _, err := hex.DecodeString(f[0]); err != nil {
			continue
		}
		out = append(out, catalogLine{sha: f[0], local: f[1], source: f[2]})
	}
	if len(out) == 0 {
		t.Fatal("testdata/catalog/PROVENANCE.txt lists no file")
	}
	return out
}

func sha256Hex(b []byte) string {
	sum := sha256.Sum256(b)
	return hex.EncodeToString(sum[:])
}

// catalogFile returns the bytes of a vendored file after checking them against PROVENANCE.txt: no replay ever
// reads a corpus whose hash is not the listed one.
func catalogFile(t *testing.T, local string) []byte {
	t.Helper()
	var want string
	for _, l := range catalogProvenance(t) {
		if l.local == local {
			want = l.sha
		}
	}
	if want == "" {
		t.Fatalf("%s is not listed in testdata/catalog/PROVENANCE.txt", local)
	}
	b, err := os.ReadFile(filepath.Join(catalogDir, filepath.FromSlash(local)))
	if err != nil {
		t.Fatalf("%v", err)
	}
	if got := sha256Hex(b); got != want {
		t.Fatalf("%s: SHA-256 %s, PROVENANCE.txt says %s: a vendored catalog file is never edited", local, got, want)
	}
	return b
}

func TestTheVendoredCatalogCorporaAreWhatTheProvenanceLists(t *testing.T) {
	lines := catalogProvenance(t)
	listed := map[string]catalogLine{}
	for _, l := range lines {
		if _, dup := listed[l.local]; dup {
			t.Errorf("%s is listed twice", l.local)
		}
		listed[l.local] = l
		if l.source != "device-exchange/vectors/"+l.local {
			t.Errorf("%s: the catalog-relative source is %q, want device-exchange/vectors/%s", l.local, l.source, l.local)
		}
	}
	onDisk := map[string]bool{}
	err := filepath.WalkDir(catalogDir, func(p string, d fs.DirEntry, err error) error {
		if err != nil || d.IsDir() {
			return err
		}
		rel, _ := filepath.Rel(catalogDir, p)
		rel = filepath.ToSlash(rel)
		if rel == "PROVENANCE.txt" || rel == ".gitattributes" {
			return nil
		}
		onDisk[rel] = true
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}
	for rel := range onDisk {
		if _, ok := listed[rel]; !ok {
			t.Errorf("%s is in testdata/catalog but not in PROVENANCE.txt", rel)
		}
	}
	for rel, l := range listed {
		if !onDisk[rel] {
			t.Errorf("%s is listed in PROVENANCE.txt but missing", rel)
			continue
		}
		b, err := os.ReadFile(filepath.Join(catalogDir, filepath.FromSlash(rel)))
		if err != nil {
			t.Errorf("%v", err)
			continue
		}
		if got := sha256Hex(b); got != l.sha {
			t.Errorf("%s: SHA-256 %s, PROVENANCE.txt says %s (a vendored file is never edited; line endings must not be converted either)", rel, got, l.sha)
		}
	}
	// the catalog's own PROVENANCE.txt of an fmsw-* folder names the hash of every file of the generated set it came
	// from; for the files the catalog keeps, that hash is the file's
	checked := 0
	for rel := range listed {
		if !strings.HasSuffix(rel, "/PROVENANCE.txt") {
			continue
		}
		dir := strings.TrimSuffix(rel, "PROVENANCE.txt")
		b := catalogFile(t, rel)
		inList := false
		for _, line := range strings.Split(string(b), "\n") {
			line = strings.TrimRight(line, "\r")
			if strings.HasPrefix(line, "sha256:") {
				inList = true
				continue
			}
			f := strings.Fields(line)
			if !inList || len(f) != 2 {
				continue
			}
			local := dir + f[0]
			if _, vendored := listed[local]; !vendored {
				t.Logf("%s names %s, which the catalog folder does not keep (not vendored)", rel, f[0])
				continue
			}
			if got := listed[local].sha; got != f[1] {
				t.Errorf("%s says %s has SHA-256 %s, the vendored file has %s", rel, f[0], f[1], got)
			}
			checked++
		}
	}
	if checked < 5 {
		t.Errorf("only %d hashes of the catalog's own provenance files could be cross-checked", checked)
	}
}

// --- the connection strings: replayed through internal/connstr ---

func TestTheCatalogConnectionStringCorpusReplaysThroughTheCodec(t *testing.T) {
	var doc connFile
	if err := json.Unmarshal(catalogFile(t, catalogConnFile), &doc); err != nil {
		t.Fatal(err)
	}
	if len(doc.Cases) < 100 {
		t.Fatalf("%d cases: the file is meant to be broad", len(doc.Cases))
	}
	seen := map[string]bool{}
	counts := map[string]int{}
	for _, c := range doc.Cases {
		if seen[c.Name] {
			t.Errorf("two cases are called %s", c.Name)
		}
		seen[c.Name] = true
		counts[c.Verdict]++
		raw := []byte(c.Input)
		if c.InputHex != "" {
			b, err := hex.DecodeString(c.InputHex)
			if err != nil {
				t.Fatalf("%s: %v", c.Name, err)
			}
			raw = b
		}
		var got connstr.Conn
		var err error
		if c.Kind == "file" {
			got, err = connstr.ParseFile(raw)
		} else {
			got, err = connstr.Parse(string(raw))
		}
		if c.Verdict == "refused" {
			e, ok := err.(*connstr.Error)
			if !ok || e.Reason != c.Reason || e.Looks != c.Looks {
				t.Errorf("%s: want refusal %s/%s, got %v", c.Name, c.Reason, c.Looks, err)
			}
			continue
		}
		if err != nil {
			t.Errorf("%s: want verdict %s, the reader refused: %v", c.Name, c.Verdict, err)
			continue
		}
		if c.Fields == nil || got.Endpoint() != c.Fields.X || got.Host != c.Fields.Host || got.Port != c.Fields.Port ||
			got.Fingerprint != c.Fields.FP || got.Name != c.Fields.N || got.Login != c.Fields.U || got.Invitation != c.Fields.I {
			t.Errorf("%s: members differ: %+v vs %+v", c.Name, got, c.Fields)
		}
		text, err := got.String()
		if err != nil || text != c.Canonical {
			t.Errorf("%s: a writer produces %q (%v), the vector says %q", c.Name, text, err, c.Canonical)
		}
		if c.Verdict == "valid" && c.Kind == "text" && text != string(raw) {
			t.Errorf("%s: a valid case is its own canonical text", c.Name)
		}
		if c.Verdict == "tolerated" && c.Kind == "text" && text == string(raw) {
			t.Errorf("%s: a tolerated input differs from its canonical text", c.Name)
		}
	}
	for _, v := range []string{"valid", "tolerated", "refused"} {
		if counts[v] == 0 {
			t.Errorf("no %s case", v)
		}
	}
	t.Logf("%d cases replayed: %v", len(doc.Cases), counts)
}

// --- the frames files: replayed through the frame and chunk codec ---

type catalogFramesDoc struct {
	Contract        string         `json:"contract"`
	ContractVersion string         `json:"contractVersion"`
	Constants       map[string]int `json:"constants"`
	Transcripts     []struct {
		Name    string `json:"name"`
		Streams []struct {
			ID        string `json:"id"`
			Transport string `json:"transport"`
		} `json:"streams"`
		Steps []struct {
			Stream    string `json:"stream"`
			Direction string `json:"direction"`
			JSON      string `json:"json"`
			Hex       string `json:"hex"`
			Closed    bool   `json:"closed"`
			Chunk     *struct {
				Flags      int    `json:"flags"`
				PayloadHex string `json:"payloadHex"`
				Hex        string `json:"hex"`
			} `json:"chunk"`
			HTTP *struct {
				Status int `json:"status"`
			} `json:"http"`
		} `json:"steps"`
	} `json:"transcripts"`
	Chunks []struct {
		Name       string `json:"name"`
		Hex        string `json:"hex"`
		Flags      int    `json:"flags"`
		PayloadHex string `json:"payloadHex"`
		Verdict    string `json:"verdict"`
	} `json:"chunks"`
}

func TestEveryCatalogFramesCorpusReplaysThroughTheFrameCodec(t *testing.T) {
	for _, local := range catalogFramesFiles {
		t.Run(local, func(t *testing.T) {
			var doc catalogFramesDoc
			if err := json.Unmarshal(catalogFile(t, local), &doc); err != nil {
				t.Fatal(err)
			}
			for name, want := range map[string]int{"maxFrame": wire.MaxFrame, "maxFrameAuth": wire.MaxFrameAuth, "maxRecord": wire.MaxRecord,
				"maxChunkLength": wire.MaxChunkLen, "schemaVersion": wire.SchemaVersion} {
				if doc.Constants[name] != want {
					t.Errorf("constant %s: the corpus says %d, the server's codec has %d", name, doc.Constants[name], want)
				}
			}
			frames, chunks, raws, https, closes, superseded, predating := 0, 0, 0, 0, 0, 0, 0
			for _, tr := range doc.Transcripts {
				streams := map[string]bool{}
				for _, s := range tr.Streams {
					streams[s.ID] = true
				}
				for i, st := range tr.Steps {
					where := fmt.Sprintf("%s step %d", tr.Name, i)
					if st.Stream != "" && !streams[st.Stream] {
						t.Errorf("%s: stream %q is not one of the transcript's streams", where, st.Stream)
					}
					switch {
					case st.JSON != "":
						raw, err := hex.DecodeString(st.Hex)
						if err != nil || len(raw) < 4 || string(raw[4:]) != st.JSON || int(binary.BigEndian.Uint32(raw)) != len(raw)-4 {
							t.Errorf("%s: the hex is not the 4-byte length and the body of the json", where)
							continue
						}
						e, err := wire.NewFrameReader(bytes.NewReader(raw), wire.MaxFrameAuth).Read()
						if err == wire.ErrVersionRefused {
							superseded++ // the frame of the superseded wire, which the server answers with schemaVersion 2
							continue
						}
						if err != nil {
							t.Errorf("%s (%s): the server's codec refuses the frame: %v", where, st.Direction, err)
							continue
						}
						if st.Direction == "server->client" && !wire.Known(e) {
							t.Errorf("%s: a server->client frame of a type the codec does not know: %s", where, st.JSON)
						}
						again, err := wire.Frame(e)
						if err == nil && !bytes.Equal(again, raw) && (e.TypeName() == wire.TypeEnrolled || e.TypeName() == wire.TypeWelcome) &&
							!strings.Contains(st.JSON, `"accountKind"`) && strings.Replace(string(again[4:]), `"accountKind":"",`, "", 1) == st.JSON {
							// a frame of an edition before `accountKind`: the codec reads it and, written again, adds the member the
							// server always sends; the bytes are otherwise the same
							predating++
							continue
						}
						if err != nil || !bytes.Equal(again, raw) {
							t.Errorf("%s (%s): re-encoding the decoded frame changes the bytes\n got  %s\n want %s", where, st.Direction, again, raw)
						}
						frames++
					case st.Chunk != nil:
						raw, err := hex.DecodeString(st.Chunk.Hex)
						if err != nil {
							t.Errorf("%s: %v", where, err)
							continue
						}
						c, err := wire.ReadChunk(bytes.NewReader(raw))
						if bytes.Equal(raw, wire.EndChunk()) {
							// the broadcaster's clean end: the reader reports it as the end, not as a chunk
							if !errors.Is(err, wire.ErrChunkEnd) || st.Chunk.Flags != 0 || st.Chunk.PayloadHex != "" {
								t.Errorf("%s: the zero-length chunk is read as %+v (%v), want the end of the broadcast", where, c, err)
							}
						} else if err != nil || int(c.Flags) != st.Chunk.Flags || hex.EncodeToString(c.Payload) != st.Chunk.PayloadHex {
							t.Errorf("%s: the chunk codec reads %+v (%v), the corpus says flags %d payload %s", where, c, err, st.Chunk.Flags, st.Chunk.PayloadHex)
						}
						chunks++
					case st.HTTP != nil:
						https++ // an HTTP exchange is the relay's, not the codec's
					case st.Closed:
						closes++
					case st.Hex != "":
						if _, err := hex.DecodeString(st.Hex); err != nil {
							t.Errorf("%s: raw bytes that are not hex: %v", where, err)
						}
						raws++ // the raw byte run of a clipboard value: not frames, nothing for the codec to read
					}
				}
			}
			for _, c := range doc.Chunks {
				raw, err := hex.DecodeString(c.Hex)
				if err != nil {
					t.Errorf("chunk example %s: %v", c.Name, err)
					continue
				}
				ch, err := wire.ReadChunk(bytes.NewReader(raw))
				var got string
				switch {
				case err == nil && ch.IsHeader():
					got = "header"
				case err == nil && ch.Joinable():
					got = "joinable"
				case err == nil:
					got = "ordinary"
				case errors.Is(err, wire.ErrChunkEnd):
					got = "ends-the-broadcast"
				default:
					got = "closes-the-stream"
				}
				if got != c.Verdict {
					t.Errorf("chunk example %s: the server's chunk reader says %s, the corpus %s", c.Name, got, c.Verdict)
				}
				if err == nil && (int(ch.Flags) != c.Flags || hex.EncodeToString(ch.Payload) != c.PayloadHex) {
					t.Errorf("chunk example %s: flags/payload differ from the corpus", c.Name)
				}
			}
			if frames+predating < 100 || len(doc.Chunks) < 8 {
				t.Errorf("only %d frames and %d chunk examples were replayed", frames+predating, len(doc.Chunks))
			}
			// The catalog says the 0.17 server frames corpus predates later additive members (DEVICE-EXCHANGE item BS,
			// "Artifacts"); its `enrolled` and `welcome` frames carry no accountKind. Pinned: the later corpora have none
			// such, and a change of the number here is a change of the corpus or of the codec.
			if want := map[string]int{"server-frames-v2.json": 21}[local]; predating != want {
				t.Errorf("%d frames predate accountKind, want %d", predating, want)
			}
			t.Logf("%s (contractVersion %s): %d transcripts, %d frames re-encoded to the same bytes, %d more that differ only by the accountKind member the edition did not have, %d legacy-wire frames, %d chunk steps, %d http, %d raw runs, %d closes, %d chunk examples",
				local, doc.ContractVersion, len(doc.Transcripts), frames, predating, superseded, chunks, https, raws, closes, len(doc.Chunks))
		})
	}
}

// The transcripts are not replayed against a running server. A step list records the frames that crossed the
// streams, in the order the SCRIPT saw them; what the script did between two steps is not in it - the injected clock
// it advanced (the pace, session-lapse and offline-grace transcripts), the waits for a server-side effect
// (rig.until, rig.online, rig.subscribed), the relay and tunnel TCP sides it served itself, the third-party port pool -
// and each scenario builds its own rig (rigOpts) with its own seeded sources. A generic player would have to
// re-implement scripts.go, scripts_sealed.go and scripts_admin.go, which is what cmd/exchange-vectors itself already is:
// regenerating them and comparing with testdata/vectors is TestTheCommittedVectorsAreWhatTheGeneratorProducesAndMatchTheirProvenance.
func TestTheCatalogFramesTranscriptsAreNotReplayedAgainstARunningServer(t *testing.T) {
	t.Skip("a transcript omits the out-of-band script actions (clock advance, waits for server state, relay/tunnel TCP sides, port pool, per-scenario rig options and seeds); " +
		"the frames are replayed through the codec instead (TestEveryCatalogFramesCorpusReplaysThroughTheFrameCodec)")
}

// --- the records: replayed against the real server ---

// who sends a case is the generator's script, not part of the corpus (records.go generateRecords): another device of
// the account sends these three, a throwaway device (a case that ends in a closed stream must not take a working
// device with it) sends the two that are expected to close, and a broadcast is ended at once unless it is kept live
// for the cases that follow.
var (
	recordsSentByB     = map[string]bool{"resource-held-by-another-device": true, "resource-share-id-held-by-another": true, "broadcast-id-held-by-another-device": true}
	recordsThrowaway   = map[string]bool{"resource-not-an-id": true, "broadcast-without-descriptor": true}
	recordsKeptLive    = map[string]bool{"broadcast-held": true, "broadcast-id-held-by-another-device": true, "broadcast-started-again-by-its-device": true}
	errRecordsNoAnswer = errors.New("no answer and no close within 5 s")
)

// recordsStale are the cases of the catalog's server-records-v1.json (edition 0.17) that the catalog itself declares
// stale: DEVICE-EXCHANGE item BS says `resource-16KiB-exact` (a 16384-byte input, recorded as accepted and listed at
// 16475 bytes) is refused `capacity` under that item, "the item prevails ... A reader replaying the file treats that
// one case by this item". The replay therefore pins the item's verdict for that case and says so; any other
// difference fails.
var recordsStale = map[string]struct{ verdict, reason string }{
	"resource-16KiB-exact": {"refused", "capacity"},
}

type recordReplay struct {
	verdict, reason string
	listed          string // as the server listed it, with the publisher's deviceId replaced by the corpus's
	drive           *driveFile
	err             error
}

// replayRecords drives the real server (the rig of the generator, the same seed and enrolments) with the `input` of
// every case, in the order of the corpus, and returns what the server did.
func replayRecords(cases []recordCase) (out []recordReplay, err error) {
	defer func() {
		if r := recover(); r != nil {
			if f, ok := r.(genFailure); ok {
				err = f
				return
			}
			panic(r)
		}
	}()
	r := newRig("records", rigOpts{pool: true, logins: []string{account}})
	defer func() {
		if cerr := r.close(); cerr != nil && err == nil {
			err = cerr
		}
	}()
	tr := r.newTranscript("records-replay", "")
	a, aDev, _ := r.enroll(tr, "a", account, "Records PC", "fms-windows", roles("resource-producer", "broadcaster"), "")
	r.control(a, aDev)
	b, bDev, _ := r.enroll(tr, "b", account, "Records phone", "fms-android", roles("resource-producer", "broadcaster"), "")
	r.control(b, bDev)

	list := func() *wire.Directory {
		a.send(&wire.List{})
		return a.expect(wire.TypeDirectory).(*wire.Directory)
	}
	find := func(raws []json.RawMessage, key, id string) string {
		for _, raw := range raws {
			var m map[string]json.RawMessage
			if err := json.Unmarshal(raw, &m); err != nil {
				failf("%v", err)
			}
			if string(m[key]) == fmt.Sprintf("%q", id) {
				return string(raw)
			}
		}
		return ""
	}
	// answer reads what the server does with a request: an envelope, or the close of the stream, or nothing
	answer := func(s *sess) (wire.Envelope, error) {
		_ = s.c.SetReadDeadline(time.Now().Add(5 * time.Second))
		e, err := s.c.Recv(5 * time.Second)
		if err != nil {
			var ne net.Error
			if errors.As(err, &ne) && ne.Timeout() {
				return nil, errRecordsNoAnswer
			}
			return nil, nil // closed
		}
		return e, nil
	}
	member := func(input, key string) string {
		var m map[string]json.RawMessage
		if json.Unmarshal([]byte(input), &m) != nil {
			return ""
		}
		var s string
		_ = json.Unmarshal(m[key], &s)
		return s
	}
	corpusDeviceID := func(c recordCase) string {
		var m struct {
			DeviceID string `json:"deviceId"`
		}
		_ = json.Unmarshal([]byte(c.Listed), &m)
		return m.DeviceID
	}

	for _, c := range cases {
		var res recordReplay
		switch c.Kind {
		case "device":
			id := r.newID()
			body := fmt.Sprintf(`{"schemaVersion":%d,"type":"enroll","login":%q,"password":%q,"deviceId":%q,%s}`,
				wire.SchemaVersion, account, testkit.TestPassword, id, strings.TrimSuffix(strings.TrimPrefix(c.Input, "{"), "}"))
			s := r.open(tr, "dev-"+c.Name, "tls", "")
			s.sendFrame(testkit.RawFrame(body))
			e, aerr := answer(s)
			switch v := e.(type) {
			case *wire.Enrolled:
				s.auth()
				s.send(&wire.Keepalive{})
				s.expect(wire.TypeKeepalive)
				r.online(account, id)
				res.verdict = "accepted"
				res.listed = find(list().Devices, "deviceId", id)
				if res.listed == "" {
					res.err = fmt.Errorf("the device is accepted but not listed")
				}
				res.listed = strings.ReplaceAll(res.listed, id, corpusDeviceID(c))
				res.drive = wrapDrive("device", corpusDeviceID(c), json.RawMessage(res.listed))
			case *wire.Refused:
				res.verdict, res.reason = "refused", v.Reason
			case nil:
				res.verdict = "closed"
				if aerr != nil {
					res.verdict, res.err = "no-answer", aerr
				}
			default:
				res.err = fmt.Errorf("the enroll was answered with %s", e.TypeName())
			}
			s.close()
		case "resource", "broadcast":
			s, publisher := a, aDev
			switch {
			case recordsSentByB[c.Name]:
				s, publisher = b, bDev
			case recordsThrowaway[c.Name]:
				s, publisher, _ = r.enroll(tr, "throwaway-"+c.Name, account, "Throwaway "+c.Name, "fms-windows", roles("resource-producer", "broadcaster"), "")
			}
			var idKey, dirKey string
			if c.Kind == "resource" {
				idKey = "resourceId"
				s.send(&wire.Publish{Resource: json.RawMessage(c.Input)})
			} else {
				idKey = "broadcastId"
				s.send(&wire.BroadcastStart{Broadcast: json.RawMessage(c.Input)})
			}
			e, aerr := answer(s)
			switch v := e.(type) {
			case *wire.Published, *wire.BroadcastStarted:
				res.verdict = "accepted"
				id := member(c.Input, idKey)
				d := list()
				raws := d.Resources
				dirKey = "resourceId"
				if c.Kind == "broadcast" {
					raws, dirKey = d.Broadcasts, "broadcastId"
				}
				res.listed = find(raws, dirKey, id)
				if res.listed == "" {
					res.err = fmt.Errorf("the record is accepted but not listed")
				}
				res.listed = strings.ReplaceAll(res.listed, publisher, corpusDeviceID(c))
				res.drive = wrapDrive(c.Kind, id, json.RawMessage(res.listed))
				if c.Kind == "broadcast" && !recordsKeptLive[c.Name] {
					s.send(&wire.BroadcastEnd{BroadcastID: id})
					r.until("the broadcast to end", func() bool { return r.env.Dir.Counts().Broadcasts == 0 })
				}
			case *wire.Refused:
				res.verdict, res.reason = "refused", v.Reason
			case nil:
				res.verdict = "closed"
				if aerr != nil {
					res.verdict, res.err = "no-answer", aerr
				}
			default:
				res.err = fmt.Errorf("the request was answered with %s", e.TypeName())
			}
			if recordsThrowaway[c.Name] {
				s.close()
			}
		default:
			res.err = fmt.Errorf("a case of kind %q", c.Kind)
		}
		out = append(out, res)
	}
	return out, nil
}

func TestTheCatalogRecordsCorpusReplaysAgainstTheRealServer(t *testing.T) {
	var doc recordsFile
	if err := json.Unmarshal(catalogFile(t, catalogRecordsFile), &doc); err != nil {
		t.Fatal(err)
	}
	if len(doc.Cases) < 20 {
		t.Fatalf("%d cases", len(doc.Cases))
	}
	got, err := replayRecords(doc.Cases)
	if err != nil {
		t.Errorf("the replay stopped after %d of %d cases: %v", len(got), len(doc.Cases), err)
	}
	t.Logf("server-records-v1.json: contractVersion %s, %d cases, %d replayed", doc.ContractVersion, len(doc.Cases), len(got))
	for i, c := range doc.Cases {
		t.Run(c.Name, func(t *testing.T) {
			if i >= len(got) {
				t.Fatalf("not replayed")
			}
			g := got[i]
			if g.err != nil {
				t.Errorf("%v", g.err)
			}
			if stale, ok := recordsStale[c.Name]; ok {
				if g.verdict != stale.verdict || g.reason != stale.reason || g.listed != "" || g.drive != nil {
					t.Errorf("the server did %s/%s (listed %q), item BS says %s/%s and nothing stored", g.verdict, g.reason, g.listed, stale.verdict, stale.reason)
				}
				t.Logf("STALE CASE: the corpus (edition %s) records %s/%s, the server does %s/%s as DEVICE-EXCHANGE item BS says; the catalog calls this case stale",
					doc.ContractVersion, c.Verdict, c.Reason, g.verdict, g.reason)
				return
			}
			if g.verdict != c.Verdict || g.reason != c.Reason {
				t.Errorf("the server did %s/%s, the corpus says %s/%s", g.verdict, g.reason, c.Verdict, c.Reason)
				return
			}
			if g.listed != c.Listed {
				t.Errorf("the server lists\n got  %s\n want %s", clip(g.listed), clip(c.Listed))
			}
			switch {
			case (g.drive == nil) != (c.Drive == nil):
				t.Errorf("the Drive file of an accepted record: got %v, the corpus has %v", g.drive != nil, c.Drive != nil)
			case g.drive != nil && (g.drive.FileName != c.Drive.FileName || g.drive.Text != c.Drive.Text):
				t.Errorf("the Drive file differs\n got  %s\n want %s", clip(g.drive.Text), clip(c.Drive.Text))
			}
		})
	}
}

func clip(s string) string {
	if len(s) > 400 {
		return s[:400] + fmt.Sprintf(" ..(%d bytes)", len(s))
	}
	return s
}
