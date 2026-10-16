package main

import (
	"bytes"
	"crypto/sha256"
	"encoding/json"
	"errors"
	"fmt"
	"sort"
	"strings"

	"fms/exchange-server/internal/wire"
)

// What is generated: three JSON files and the provenance that names them.
const (
	framesFileName     = "frames-v2.json"
	recordsFileName    = "records-v1.json"
	provenanceFileName = "PROVENANCE.txt"
	// connFileName (connection-string-v1.json) is declared in connstr.go

	contractID      = "DEVICE-EXCHANGE"
	contractVersion = "1.1"
	producerName    = "Media Exchange Server (Go) of Fast Media Sorter & Sharing for Windows, cmd/exchange-vectors"
)

type framesFile struct {
	Contract        string         `json:"contract"`
	ContractVersion string         `json:"contractVersion"`
	Sections        string         `json:"sections"`
	Rung            int            `json:"rung"`
	Producer        string         `json:"producer"`
	Notes           []string       `json:"notes"`
	Constants       map[string]int `json:"constants"`
	Transcripts     []*transcript  `json:"transcripts"`
	Chunks          []chunkExample `json:"chunks"`
	Sealed          sealedFile     `json:"sealed"`
}

type recordsFile struct {
	Contract        string       `json:"contract"`
	ContractVersion string       `json:"contractVersion"`
	Sections        string       `json:"sections"`
	Rung            int          `json:"rung"`
	Producer        string       `json:"producer"`
	Notes           []string     `json:"notes"`
	Cases           []recordCase `json:"cases"`
}

// chunkExample is one chunk as the wire codec writes it and the verdict the reader gives it.
type chunkExample struct {
	Name       string `json:"name"`
	Hex        string `json:"hex"`
	Flags      int    `json:"flags,omitempty"`
	PayloadHex string `json:"payloadHex,omitempty"`
	Verdict    string `json:"verdict"` // joinable | header | ordinary | ends-the-broadcast | closes-the-stream
	Why        string `json:"why,omitempty"`
}

// chunkExamples are built by the codec itself and judged by its reader: every flag combination of the
// two defined bits, a zero length, and the three ways a chunk closes the stream.
func chunkExamples() []chunkExample {
	var out []chunkExample
	add := func(name string, raw []byte, flags int, payload []byte) {
		ex := chunkExample{Name: name, Hex: hexOf(raw), Flags: flags, PayloadHex: hexOf(payload)}
		c, err := wire.ReadChunk(bytes.NewReader(raw))
		switch {
		case err == nil && c.IsHeader():
			ex.Verdict = "header"
		case err == nil && c.Joinable():
			ex.Verdict = "joinable"
		case err == nil:
			ex.Verdict = "ordinary"
		case errors.Is(err, wire.ErrChunkEnd):
			ex.Verdict = "ends-the-broadcast"
		default:
			ex.Verdict = "closes-the-stream"
			ex.Why = err.Error()
		}
		out = append(out, ex)
	}
	payload := []byte("abc")
	for _, f := range []struct {
		name  string
		flags byte
	}{{"ordinary", 0}, {"joinable", wire.ChunkJoinable}, {"header", wire.ChunkHeader},
		{"header-and-joinable-bits", wire.ChunkHeader | wire.ChunkJoinable}} {
		b, err := wire.AppendChunk(nil, f.flags, payload)
		if err != nil {
			failf("%v", err)
		}
		add("flags-"+f.name, b, int(f.flags), payload)
	}
	b, _ := wire.AppendChunk(nil, wire.ChunkJoinable, nil)
	add("empty-payload", b, int(wire.ChunkJoinable), nil)
	add("zero-length", wire.EndChunk(), 0, nil)
	for _, bad := range []struct {
		name string
		raw  []byte
	}{
		{"undefined-flag-bit", []byte{0, 0, 0, 4, 0x04, 'a', 'b', 'c'}},
		{"undefined-flag-bit-with-defined-ones", []byte{0, 0, 0, 4, 0x81, 'a', 'b', 'c'}},
		{"length-1048577", []byte{0x00, 0x10, 0x00, 0x01, 0x01}},
		{"length-ffffffff", []byte{0xff, 0xff, 0xff, 0xff}},
	} {
		add(bad.name, bad.raw, 0, nil)
	}
	return out
}

func frameNotes() []string {
	return []string{
		"Every transcript was produced by the real exchange server (engine, directory, accounts, relay, cast table) over TLS on loopback, with one injected clock instant, seeded id and token sources and a third-party port pool whose binds are not sockets. The bytes are a function of the code and nothing else.",
		"A step is one frame as it crossed the stream above TLS: `json` is the body, `hex` is the whole frame (the 4-byte big-endian length, then the body). A frame is recorded when the script reads or writes it, in the order of the script.",
		"A client replays a transcript by encoding every client->server step to exactly its `hex` and decoding every server->client step to the same values. Ids, tokens and codes are the scenario's; a client under test supplies its own ids and compares the rest.",
		"After `connected` and after `upload-accepted` the bytes are not envelopes: tunnels are recorded up to `connected` and never beyond; an upload continues as `chunk` steps (a 4-byte length counting the flag byte plus the payload, the flag byte, the payload) and the listener's HTTP exchange is an `http` step.",
		"`chunks` lists every flag combination of the two defined bits, a zero length (the clean end of a broadcast) and the chunks that close a stream (an undefined flag bit, a length above 1048576, the largest length word); a chunk of exactly 1048576 bytes of length is the largest accepted and is not listed because of its size.",
		"The frame limit is 16384 bytes of JSON until a stream is authenticated (up to and including welcome or enrolled) and 262144 after it, both ways; a record is at most 16384 bytes of JSON.",
		"Reasons: bad-credentials, device-revoked, version-unsupported, tls-required, id-collision, capacity, port-unavailable, unavailable and rate-limited are produced in the `refusals` transcript (with limit-reached and protection-unsupported); invitation-invalid, login-taken, password-policy, limit-reached (the cap on accounts) and not-permitted are produced in `registration`, signed-out in `password`, and rate-limited by the pace of password checks in `pace`. The refusal that answers a schemaVersion 1 frame carries schemaVersion 2.",
		"`limit-reached` is a count limit of the server or of the account (resources, broadcasts, devices, accounts); `capacity` is the server unable to take more - a record or a frame over its size, the work queue of the password hash full, no room for another pairing code. A client that retries later is right for the second and wrong for the first.",
		"Registration (`register`) carries an invitation by `invitationCode` (one-time) or `sharedPassword` (the shared invitation password), exactly one of them; the contract's prose also names them `invitation` and `invitationPassword`, and the server reads both spellings. `enrolled` carries `accountKind` (`member` or `guest`).",
		"`limit-reached` is a count limit of the server or of the account (resources, broadcasts, devices, accounts, and the clipboard's `clipboardBytes`); `capacity` is the server unable to take more - a record or a frame over its size, the work queue of the password hash full, no room for another pairing code, the account's second overlapping clipboard put. A client that retries later is right for the second and wrong for the first.",
		"The `clipboard` transcript is section 22 (ticket 229): after `clipboard-ready` and after the members of `clipboard-value` the bytes are RAW - one step per byte run, whose `hex` is the bytes exactly as they crossed the stream; a frame assembler stops there. A put's completion is judged against a short probe for bytes beyond the declared size, and a sender that half-closes is judged at once.",
		"This corpus is evidence for sections 5 to 7, 22 and 27 at edition 1.1. The transcripts `sealed-devices`, `sealed-folder`, `sealed-grant` and `sealed-clipboard` run against a server that advertises `sealed` (item DP); every other transcript runs against one that does not, so its `welcome.features` carries no `sealed` and its clipboard kinds are `text`, `image` and `file`.",
		"The `sealed` member of this file lists every box of the `sealed-*` transcripts with the context it was sealed under, the devices it is from and to, and the clear payload it opens to. The server did not make, open or check a box (item DH): the boxes are sealed by the generator over the fixed device keys of the shared vector file e2e-v1.json, so a product that holds that file's private keys opens every one of them. See `sealed.notes`.",
	}
}

func recordNotes() []string {
	return []string{
		"Every case was run against the real exchange server: `input` is what a client sent (the members of the record, or of the enroll envelope for a device), `verdict` is what the server did (accepted, refused with `reason`, or closed: a malformed record closes the stream with no answer), `listed` is the record exactly as an enrolled device of the account reads it from `list`.",
		"The server owns deviceId (forced to the publisher), presence, updatedAt, startedAt (when the client sent none) and the granted publicPort; every other member, known or not, at any depth, comes back verbatim.",
		"A device record is built from the enroll envelope: unknown members of the envelope are not part of the record, a `receiver` block is kept verbatim (an unknown member inside it included), unknown roles are dropped, `\"receiver\":null` is absent.",
		"`drive` is the section 8.1 wrapper around the listed record, and the file name it is written to. A resource carries `access` on Drive only by the user's opt-in (item X of section 15), so the wrapper here is the default: without it. writtenAt is the generation instant; ttlSeconds is the working default of the vectors (1800 for a device or a resource, 120 for a broadcast).",
		"A record is at most 16384 bytes of JSON as sent: exactly 16384 is accepted, one byte more is refused with `capacity` and stores nothing.",
	}
}

// generate runs every scenario and renders the files. The returned map is name -> bytes.
func generate(commit string) (files map[string][]byte, err error) {
	defer func() {
		if r := recover(); r != nil {
			if f, ok := r.(genFailure); ok {
				err = f
				return
			}
			panic(r)
		}
	}()
	var transcripts []*transcript
	sealedCases = nil
	for _, sc := range []func() *transcript{scenarioEnrollAndHello, scenarioPairing, scenarioRefusals, scenarioDirectory,
		scenarioTunnels, scenarioRelay, scenarioCast, scenarioRegistration, scenarioPasswordAndSignOut, scenarioPace,
		scenarioClipboard, scenarioSealedDevices, scenarioSealedFolder, scenarioSealedGrant, scenarioSealedClipboard} {
		transcripts = append(transcripts, sc())
	}
	frames := framesFile{
		Contract: contractID, ContractVersion: contractVersion, Sections: "5, 6, 7.1-7.10, 22, 27", Rung: 2, Producer: producerName,
		Notes: frameNotes(),
		Constants: map[string]int{"maxFrame": wire.MaxFrame, "maxFrameAuth": wire.MaxFrameAuth, "maxRecord": wire.MaxRecord,
			"maxChunkLength": wire.MaxChunkLen, "schemaVersion": wire.SchemaVersion},
		Transcripts: transcripts, Chunks: chunkExamples(), Sealed: sealedMember(),
	}
	records := recordsFile{
		Contract: contractID, ContractVersion: contractVersion, Sections: "6, 7.4, 8.1", Rung: 1, Producer: producerName,
		Notes: recordNotes(), Cases: generateRecords(),
	}
	files = map[string][]byte{}
	if files[framesFileName], err = render(frames); err != nil {
		return nil, err
	}
	if files[recordsFileName], err = render(records); err != nil {
		return nil, err
	}
	if files[connFileName], err = render(connVectors()); err != nil {
		return nil, err
	}
	if files["protection-v1.json"], err = render(map[string]any{
		"contract": contractID, "contractVersion": contractVersion,
		"sections": "17 AG-AL", "producer": producerName,
		"transcripts": []*transcript{scenarioProtection()},
	}); err != nil {
		return nil, err
	}
	files[provenanceFileName] = []byte(provenance(files, commit))
	return files, nil
}

// render writes a value as indented JSON with no HTML escaping and a final newline.
func render(v any) ([]byte, error) {
	var b bytes.Buffer
	enc := json.NewEncoder(&b)
	enc.SetEscapeHTML(false)
	enc.SetIndent("", "  ")
	if err := enc.Encode(v); err != nil {
		return nil, err
	}
	return b.Bytes(), nil
}

// provenance is PROVENANCE.txt: the generator, the command, the commit it ran at and the SHA-256 of each
// generated file. The hashes are what makes a hand edit visible.
func provenance(files map[string][]byte, commit string) string {
	if commit == "" {
		commit = "not recorded"
	}
	var b strings.Builder
	fmt.Fprintf(&b, "Exchange conformance vectors for %s %s (rungs 1 and 2, the connection string of ticket 187, the clipboard of section 22, the sealed content of section 27).\n\n", contractID, contractVersion)
	b.WriteString("generator: cmd/exchange-vectors in the exchange-server module of the Fast Media Sorter & Sharing for Windows repository\n")
	b.WriteString("command:   go run ./cmd/exchange-vectors -out testdata/vectors\n")
	fmt.Fprintf(&b, "commit:    %s\n\n", commit)
	b.WriteString("These files are generated, never edited: a hand edit changes a hash below and fails the suite; the only way to change them is to\n")
	b.WriteString("change the server (or the script) and run the command again. Regenerating without a change gives the same bytes.\n\n")
	b.WriteString("sha256:\n")
	names := append([]string(nil), hashedFiles...)
	sort.Strings(names)
	for _, n := range names {
		sum := sha256.Sum256(files[n])
		fmt.Fprintf(&b, "  %-18s %x\n", n, sum)
	}
	return b.String()
}

// checkProvenance verifies that the files carry the hashes PROVENANCE.txt names.
func checkProvenance(files map[string][]byte, prov string) error {
	for _, n := range hashedFiles {
		sum := sha256.Sum256(files[n])
		want := fmt.Sprintf("%x", sum)
		found := false
		for _, line := range strings.Split(prov, "\n") {
			f := strings.Fields(line)
			if len(f) == 2 && f[0] == n {
				found = true
				if f[1] != want {
					return fmt.Errorf("%s was edited: its SHA-256 is %s, PROVENANCE.txt says %s", n, want, f[1])
				}
			}
		}
		if !found {
			return fmt.Errorf("PROVENANCE.txt names no hash for %s", n)
		}
	}
	return nil
}

// closeErr collects what a scenario's rig reported when it was closed.
var closeErr error

func mustClose(r *rig) {
	if err := r.close(); err != nil && closeErr == nil {
		closeErr = err
	}
}

// hashedFiles are the generated vector files PROVENANCE.txt names a hash for, in the order it lists them.
var hashedFiles = []string{framesFileName, recordsFileName, connFileName, "protection-v1.json"}
