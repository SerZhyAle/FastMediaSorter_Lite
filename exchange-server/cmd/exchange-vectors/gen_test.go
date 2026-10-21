package main

import (
	"bytes"
	"encoding/hex"
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"testing"

	"fms/exchange-server/internal/connstr"
	"fms/exchange-server/internal/wire"
)

// Ticket 205 T-1 acceptance 1 and 2 on the Go side: regenerating gives the same bytes, a hand edit
// fails the suite on the provenance hash, and the committed files are what the generator produces now.

var (
	genOnce  sync.Once
	genFiles map[string][]byte
	genErr   error
)

func generated(t *testing.T) map[string][]byte {
	t.Helper()
	genOnce.Do(func() {
		genFiles, genErr = generate("")
		if genErr == nil {
			genErr = closeErr
		}
	})
	if genErr != nil {
		t.Fatalf("the generator failed: %v", genErr)
	}
	return genFiles
}

func TestRegeneratingGivesTheSameBytes(t *testing.T) {
	first := generated(t)
	second, err := generate("")
	if err != nil || closeErr != nil {
		t.Fatalf("%v %v", err, closeErr)
	}
	for name, b := range first {
		if !bytes.Equal(b, second[name]) {
			t.Errorf("%s differs between two runs: a generated vector must be a function of the code alone", name)
		}
	}
	if len(first) != 5 {
		t.Fatalf("%d files", len(first))
	}
}

func TestBothAdministrationOpeningsCoverTheContract(t *testing.T) {
	var corpus framesFile
	if err := json.Unmarshal(generated(t)[framesFileName], &corpus); err != nil {
		t.Fatal(err)
	}
	wantOps := strings.Fields("status account.list account.add account.passwd account.kind account.remove account.limits device.list device.revoke device.pairing-code invitation.create invitation.list invitation.withdraw shared_invitation.status shared_invitation.set policy.status policy.set grant.list")
	for _, name := range []string{"admin-native", "admin-websocket"} {
		t.Run(name, func(t *testing.T) {
			ops, reasons := map[string]bool{}, map[string]bool{}
			found := false
			for _, tr := range corpus.Transcripts {
				if tr.Name != name {
					continue
				}
				found = true
				for _, step := range tr.Steps {
					var envelope struct{ Type, Op, Reason string }
					if step.JSON == "" {
						continue
					}
					if err := json.Unmarshal([]byte(step.JSON), &envelope); err != nil {
						t.Fatal(err)
					}
					if envelope.Type == "admin-request" {
						ops[envelope.Op] = true
					}
					if envelope.Type == "refused" {
						reasons[envelope.Reason] = true
					}
				}
			}
			if !found {
				t.Fatal("administration transcript missing")
			}
			for _, op := range wantOps {
				if !ops[op] {
					t.Errorf("missing carried operation %s", op)
				}
			}
			for _, reason := range []string{"admin-disabled", "bad-credentials", "rate-limited", "capacity"} {
				if !reasons[reason] {
					t.Errorf("missing refusal %s", reason)
				}
			}
		})
	}
}

func committed(t *testing.T) map[string][]byte {
	t.Helper()
	out := map[string][]byte{}
	for _, name := range append(append([]string(nil), hashedFiles...), provenanceFileName) {
		b, err := os.ReadFile(filepath.Join("..", "..", "testdata", "vectors", name))
		if err != nil {
			t.Fatalf("the committed vectors are missing (run: go run ./cmd/exchange-vectors -out testdata/vectors): %v", err)
		}
		out[name] = b
	}
	return out
}

func TestTheCommittedVectorsAreWhatTheGeneratorProducesAndMatchTheirProvenance(t *testing.T) {
	disk, now := committed(t), generated(t)
	for _, name := range hashedFiles {
		if !bytes.Equal(disk[name], now[name]) {
			lines1 := strings.Split(string(disk[name]), "\n")
			lines2 := strings.Split(string(now[name]), "\n")
			for i := 0; i < len(lines1) && i < len(lines2); i++ {
				if lines1[i] != lines2[i] {
					t.Errorf("%s diff line %d:\ndisk: %s\nnow:  %s", name, i+1, lines1[i], lines2[i])
					break
				}
			}
			t.Errorf("%s is not what the generator produces now: regenerate it (go run ./cmd/exchange-vectors -out testdata/vectors)", name)
		}
	}
	if err := checkProvenance(disk, string(disk[provenanceFileName])); err != nil {
		t.Fatal(err)
	}
}

func TestAHandEditFailsOnTheProvenanceHash(t *testing.T) {
	files := generated(t)
	prov := string(files[provenanceFileName])
	if err := checkProvenance(files, prov); err != nil {
		t.Fatal(err)
	}
	edited := map[string][]byte{}
	for k, v := range files {
		edited[k] = append([]byte(nil), v...)
	}
	edited[framesFileName] = bytes.Replace(edited[framesFileName], []byte("tls-required"), []byte("tls-needed!!"), 1)
	if err := checkProvenance(edited, prov); err == nil || !strings.Contains(err.Error(), framesFileName) {
		t.Fatalf("a hand edit of the frames must fail the provenance check: %v", err)
	}
	edited = map[string][]byte{framesFileName: files[framesFileName], recordsFileName: append(append([]byte(nil), files[recordsFileName]...), ' ')}
	if err := checkProvenance(edited, prov); err == nil || !strings.Contains(err.Error(), recordsFileName) {
		t.Fatalf("a hand edit of the records must fail the provenance check: %v", err)
	}
}

func TestTheCheckModeIsOneVerdictLineAndFailsOnADifference(t *testing.T) {
	dir := t.TempDir()
	for name, b := range committed(t) {
		if err := os.WriteFile(filepath.Join(dir, name), b, 0o644); err != nil {
			t.Fatal(err)
		}
	}
	var out, errb bytes.Buffer
	if code := run([]string{"-check", "-out", dir}, &out, &errb); code != 0 || !strings.HasPrefix(out.String(), "exchange-vectors: PASS") || strings.Count(out.String(), "\n") != 1 {
		t.Fatalf("%d %q %q", code, out.String(), errb.String())
	}
	b, _ := os.ReadFile(filepath.Join(dir, framesFileName))
	_ = os.WriteFile(filepath.Join(dir, framesFileName), bytes.Replace(b, []byte("bad-credentials"), []byte("bad-credentiale"), 1), 0o644)
	out.Reset()
	if code := run([]string{"-check", "-out", dir}, &out, &errb); code != 1 || !strings.HasPrefix(out.String(), "exchange-vectors: FAIL") {
		t.Fatalf("%d %q", code, out.String())
	}
	out.Reset()
	if code := run([]string{"-check", "-out", filepath.Join(dir, "nowhere")}, &out, &errb); code != 2 || !strings.Contains(out.String(), "COULD NOT VERIFY") {
		t.Fatalf("no files is could-not-verify, never a pass: %d %q", code, out.String())
	}
}

// --- what the vectors must contain ---

type framesDoc struct {
	Transcripts []struct {
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
				Flags int    `json:"flags"`
				Hex   string `json:"hex"`
			} `json:"chunk"`
			HTTP *struct {
				Status int `json:"status"`
			} `json:"http"`
		} `json:"steps"`
	} `json:"transcripts"`
	Chunks []struct {
		Name    string `json:"name"`
		Verdict string `json:"verdict"`
	} `json:"chunks"`
}

func TestTheFramesCoverEveryEnvelopeEveryReasonAndEveryOpening(t *testing.T) {
	var doc framesDoc
	if err := json.Unmarshal(generated(t)[framesFileName], &doc); err != nil {
		t.Fatal(err)
	}
	types, reasons := map[string]bool{}, map[string]bool{}
	var plain, tlsStreams int
	statuses := map[int]bool{}
	closed := 0
	for _, tr := range doc.Transcripts {
		for _, s := range tr.Streams {
			if s.Transport == "plain" {
				plain++
			} else {
				tlsStreams++
			}
		}
		for _, st := range tr.Steps {
			if st.Closed {
				closed++
			}
			if st.HTTP != nil {
				statuses[st.HTTP.Status] = true
			}
			if st.JSON == "" {
				continue
			}
			var m struct {
				Type   string `json:"type"`
				Reason string `json:"reason"`
			}
			if err := json.Unmarshal([]byte(st.JSON), &m); err != nil {
				t.Fatalf("%s: %v", tr.Name, err)
			}
			types[m.Type] = true
			if m.Type == wire.TypeRefused {
				reasons[m.Reason] = true
			}
		}
	}
	for _, typ := range wire.Types() {
		if !types[typ] {
			t.Errorf("no frame of type %q in any transcript", typ)
		}
	}
	for _, r := range wire.AllReasons {
		if !reasons[string(r)] {
			t.Errorf("the reason %q is not produced by any transcript", r)
		}
	}
	if plain == 0 || tlsStreams == 0 || closed == 0 {
		t.Errorf("both openings and the server's close are needed: %d plain, %d tls, %d closes", plain, tlsStreams, closed)
	}
	for _, want := range []int{200, 404, 405, 503} {
		if !statuses[want] {
			t.Errorf("no HTTP exchange with status %d", want)
		}
	}
	// the chunk examples: every flag combination, the end, and the ways a stream is closed
	verdicts := map[string]string{}
	for _, c := range doc.Chunks {
		verdicts[c.Name] = c.Verdict
	}
	for name, want := range map[string]string{"flags-ordinary": "ordinary", "flags-joinable": "joinable", "flags-header": "header",
		"flags-header-and-joinable-bits": "header", "zero-length": "ends-the-broadcast", "undefined-flag-bit": "closes-the-stream",
		"length-1048577": "closes-the-stream", "length-ffffffff": "closes-the-stream"} {
		if verdicts[name] != want {
			t.Errorf("chunk example %s: %q, want %q", name, verdicts[name], want)
		}
	}
}

// DEVICE-EXCHANGE item AQ names the two invitation members of `register` `invitation` and
// `invitationPassword`, and a client sends those: every register step of the corpus uses them, exactly one
// of the two, and none uses the first spellings the server also reads (`invitationCode`, `sharedPassword`).
func TestEveryRegisterStepUsesTheContractsMemberNames(t *testing.T) {
	var doc framesDoc
	if err := json.Unmarshal(generated(t)[framesFileName], &doc); err != nil {
		t.Fatal(err)
	}
	n := 0
	for _, tr := range doc.Transcripts {
		for i, st := range tr.Steps {
			if st.JSON == "" {
				continue
			}
			var m map[string]json.RawMessage
			if err := json.Unmarshal([]byte(st.JSON), &m); err != nil {
				t.Fatalf("%s step %d: %v", tr.Name, i, err)
			}
			if string(m["type"]) != `"register"` {
				continue
			}
			n++
			for _, old := range []string{"invitationCode", "sharedPassword"} {
				if _, ok := m[old]; ok {
					t.Errorf("%s step %d: a register frame with the first spelling %s: %s", tr.Name, i, old, st.JSON)
				}
			}
			_, code := m["invitation"]
			_, shared := m["invitationPassword"]
			if code == shared {
				t.Errorf("%s step %d: a register frame carries exactly one of invitation and invitationPassword: %s", tr.Name, i, st.JSON)
			}
		}
	}
	if n < 10 {
		t.Errorf("only %d register steps were found", n)
	}
}

// every frame of every transcript is a frame the Go codec reads and writes back to the same bytes:
// the vectors and the codec cannot drift apart
func TestEveryFrameDecodesAndReencodesToTheSameBytes(t *testing.T) {
	var doc framesDoc
	if err := json.Unmarshal(generated(t)[framesFileName], &doc); err != nil {
		t.Fatal(err)
	}
	n := 0
	for _, tr := range doc.Transcripts {
		for i, st := range tr.Steps {
			if st.JSON == "" {
				continue
			}
			raw, err := hex.DecodeString(st.Hex)
			if err != nil || string(raw[4:]) != st.JSON {
				t.Fatalf("%s step %d: the hex is not the frame of the json", tr.Name, i)
			}
			e, err := wire.NewFrameReader(bytes.NewReader(raw), wire.MaxFrameAuth).Read()
			if err == wire.ErrVersionRefused {
				continue // the frame of the superseded wire
			}
			if err != nil {
				t.Fatalf("%s step %d (%s): %v", tr.Name, i, st.JSON, err)
			}
			again, err := wire.Frame(e)
			if err != nil || !bytes.Equal(again, raw) {
				t.Errorf("%s step %d: re-encoding the decoded frame changes the bytes\n got  %s\n want %s", tr.Name, i, again, raw)
			}
			n++
		}
	}
	if n < 100 {
		t.Fatalf("only %d frames were checked", n)
	}
}

type recordsDoc struct {
	Cases []struct {
		Name    string `json:"name"`
		Kind    string `json:"kind"`
		Verdict string `json:"verdict"`
		Reason  string `json:"reason"`
		Listed  string `json:"listed"`
		Drive   *struct {
			FileName string `json:"fileName"`
			Text     string `json:"text"`
		} `json:"drive"`
	} `json:"cases"`
}

func TestTheRecordCasesHaveTheVerdictsTheContractSays(t *testing.T) {
	var doc recordsDoc
	if err := json.Unmarshal(generated(t)[recordsFileName], &doc); err != nil {
		t.Fatal(err)
	}
	type want struct{ verdict, reason string }
	expect := map[string]want{
		"device-minimal": {"accepted", ""}, "device-full": {"accepted", ""}, "device-unknown-role": {"accepted", ""},
		"device-unknown-product": {"accepted", ""}, "device-receiver-null": {"accepted", ""}, "device-receiver-too-many-modes": {"closed", ""},
		"resource-minimal": {"accepted", ""}, "resource-full": {"accepted", ""}, "resource-unknown-members-deep": {"accepted", ""},
		"resource-unknown-kind": {"accepted", ""}, "resource-16KiB-exact": {"refused", "capacity"}, "resource-16KiB-plus-1": {"refused", "capacity"},
		"resource-delivered-16KiB-exact": {"accepted", ""}, "resource-delivered-16KiB-plus-1": {"refused", "capacity"},
		"resource-held-by-another-device": {"refused", "id-collision"}, "resource-published-again-by-its-device": {"accepted", ""},
		"resource-share-id-held-by-another": {"refused", "id-collision"}, "resource-not-an-id": {"closed", ""},
		"broadcast-minimal": {"accepted", ""}, "broadcast-full": {"accepted", ""}, "broadcast-unknown-members-deep": {"accepted", ""},
		"broadcast-without-descriptor": {"closed", ""}, "broadcast-16KiB-plus-1": {"refused", "capacity"}, "broadcast-held": {"accepted", ""},
		"broadcast-id-held-by-another-device": {"refused", "id-collision"}, "broadcast-started-again-by-its-device": {"accepted", ""},
	}
	seen := map[string]bool{}
	for _, c := range doc.Cases {
		w, ok := expect[c.Name]
		if !ok {
			t.Errorf("an unexpected case %q: add what the contract says it must do", c.Name)
			continue
		}
		seen[c.Name] = true
		if c.Verdict != w.verdict || c.Reason != w.reason {
			t.Errorf("%s: %s/%s, want %s/%s", c.Name, c.Verdict, c.Reason, w.verdict, w.reason)
		}
		if len(c.Listed) > wire.MaxRecord {
			t.Errorf("%s: the server lists a record of %d bytes, above the bound of %d that every reader enforces (item BS)", c.Name, len(c.Listed), wire.MaxRecord)
		}
		if (c.Verdict == "accepted") != (len(c.Listed) > 0) || (c.Verdict == "accepted") != (c.Drive != nil) {
			t.Errorf("%s: an accepted record is listed and has its Drive file, a refused one has neither", c.Name)
		}
		if c.Drive != nil {
			var wrap struct {
				SchemaVersion int             `json:"schemaVersion"`
				Type          string          `json:"type"`
				WrittenAt     string          `json:"writtenAt"`
				TTL           int             `json:"ttlSeconds"`
				Record        json.RawMessage `json:"record"`
			}
			if err := json.Unmarshal([]byte(c.Drive.Text), &wrap); err != nil || wrap.Type != c.Kind || wrap.TTL == 0 || len(wrap.Record) == 0 ||
				!strings.HasPrefix(c.Drive.FileName, "fmsx-"+c.Kind+"-") || !strings.HasSuffix(c.Drive.FileName, ".json") {
				t.Errorf("%s: the Drive file is wrong: %v %+v", c.Name, err, c.Drive)
			}
			if c.Kind == "resource" && strings.Contains(c.Drive.Text, `"access"`) {
				t.Errorf("%s: a Drive resource record carries access only by the user's opt-in", c.Name)
			}
		}
		switch c.Name {
		case "device-receiver-null":
			if strings.Contains(c.Listed, `"receiver":`) {
				t.Errorf("a null receiver is absent from the listed record: %s", c.Listed)
			}
		case "device-unknown-role":
			if strings.Contains(c.Listed, "time-traveller") {
				t.Errorf("an unknown role is dropped: %s", c.Listed)
			}
			if !strings.Contains(c.Listed, `"roles":["receiver","clipboard"]`) {
				t.Errorf("the role clipboard (item BV) is kept, in the order it was sent: %s", c.Listed)
			}
		case "resource-unknown-members-deep":
			if !strings.Contains(c.Listed, `"future":{"a":{"b":{"c":[1,2,{"d":null}]}}}`) {
				t.Errorf("unknown members must come back verbatim: %s", c.Listed)
			}
		case "resource-full":
			if !strings.Contains(c.Listed, `"publicPort":40000`) || !strings.Contains(c.Listed, `"presence":"online"`) ||
				strings.Contains(c.Listed, "ignored-the-server-forces") {
				t.Errorf("the server owns publicPort, presence and deviceId: %s", c.Listed)
			}
		case "resource-delivered-16KiB-exact":
			// item BS: the bound is the record as delivered. The plain record is widest once its device is
			// offline (a byte longer), so while it is online it is listed one byte under the bound.
			if len(c.Listed) != 16383 {
				t.Errorf("the largest accepted record is listed at %d bytes, want 16383 (16384 once its device is offline)", len(c.Listed))
			}
		case "resource-16KiB-exact", "resource-delivered-16KiB-plus-1":
			if c.Listed != "" {
				t.Errorf("%s is refused and listed nowhere", c.Name)
			}
		}
	}
	for name := range expect {
		if !seen[name] {
			t.Errorf("the case %s is missing", name)
		}
	}
}

// Ticket 187 T-1: the connection-string file is replayed from its JSON, the way a second implementation
// reads it - input bytes, then verdict, reason, the members and the canonical text a writer produces.
func TestConnectionStringVectorsReplayThroughTheCodec(t *testing.T) {
	var doc connFile
	if err := json.Unmarshal(generated(t)[connFileName], &doc); err != nil {
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
			t.Errorf("%s: %v", c.Name, err)
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
	}
	for _, v := range []string{"valid", "tolerated", "refused"} {
		if counts[v] < 10 {
			t.Errorf("only %d %s cases", counts[v], v)
		}
	}
	// the cases the proposals of 2026-10-10 asked for are all there
	for _, n := range []string{"with-fp-plus-slash", "name-escapes", "ipv6-literal", "port-leading-zero", "login-upper-case",
		"lower-case-hex", "fp-url-safe-alphabet", "fp-padded", "fp-lower-prefix", "raw-non-ascii", "name-empty",
		"field-without-equals", "file-bom", "file-two-lines", "file-4097", "share-code", "newer-major", "length-512"} {
		if !seen[n] {
			t.Errorf("the vector %s is missing", n)
		}
	}
}
