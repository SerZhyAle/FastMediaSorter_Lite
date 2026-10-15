package wire

import (
	"bytes"
	"encoding/hex"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"reflect"
	"strings"
	"testing"
)

var update = flag.Bool("update-golden", false, "rewrite testdata/vectors.json from the encoder")

const (
	idA = "AAAAAAAAAAAAAAAAAAAAAA"
	idB = "BBBBBBBBBBBBBBBBBBBBBA"
	idC = "CCCCCCCCCCCCCCCCCCCCCA"
	tok = "dGhpcy1pcy1hLTMyLWJ5dGUtdG9rZW4tZm9yLXRlc3Q" // 43 characters
)

func intp(n int) *int { return &n }

const (
	sampleResource  = `{"resourceId":"` + idB + `","deviceId":"` + idA + `","kind":"sftp-share","name":"Photos","access":{"descriptor":"FMSSFTP2.secret","root":"/"},"future":{"a":{"b":{"c":1}}}}`
	sampleBroadcast = `{"broadcastId":"` + idC + `","deviceId":"` + idA + `","mode":"AUDIO_ONLY","title":"Mic","descriptor":{"endpoints":[]}}`
	sampleDevice    = `{"deviceId":"` + idA + `","deviceName":"Phone","product":"fms-android","presence":"online"}`
	// 64 lower-case hex characters: the form of a clipboard value's `sha256` (22 item BU).
	sampleSHA = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
)

// samples is one envelope of every type the wire defines, with members that exercise the codec.
func samples() []struct {
	name string
	e    Envelope
} {
	body := WelcomeBody{Account: "alice", AccountKind: "member", PublicEndpoint: "203.0.113.5:22022", KeepaliveSeconds: 30,
		Features: []string{"directory", "tunnel", "pairing"}, ServerVersion: "26.10.7.1200"}
	return []struct {
		name string
		e    Envelope
	}{
		{"enroll-password", &Enroll{Login: "alice", Password: "Passw0rdTest", DeviceID: idA, DeviceName: "Phone",
			Product: "fms-android", ProductVersion: "1.2", Platform: "android", Roles: []string{"resource-producer", "receiver"},
			Receiver: json.RawMessage(`{"modes":["AUDIO_ONLY"],"transports":["HTTP_ADTS"]}`), KeepaliveSeconds: 45}},
		{"enroll-code", &Enroll{Login: "alice", PairingCode: "ABCD2345", DeviceID: idA, DeviceName: "Watch", Product: "fms-wear"}},
		{"enrolled", &Enrolled{DeviceToken: tok, WelcomeBody: body}},
		{"hello", &Hello{DeviceID: idA, DeviceToken: tok, ProductVersion: "1.2", KeepaliveSeconds: 30}},
		{"welcome", &Welcome{WelcomeBody: body}},
		{"refused", Refuse(ReasonBadCredentials)},
		{"keepalive", &Keepalive{}},
		{"bye", &Bye{}},
		{"list", &List{}},
		{"directory", &Directory{Revision: 7, Devices: []json.RawMessage{json.RawMessage(sampleDevice)},
			Resources: []json.RawMessage{json.RawMessage(sampleResource)}, Broadcasts: []json.RawMessage{json.RawMessage(sampleBroadcast)}}},
		{"subscribe", &Subscribe{}},
		{"changed", &Changed{Revision: 8, Upserts: Upserts{Resources: []json.RawMessage{json.RawMessage(sampleResource)}},
			Removals: Removals{BroadcastIDs: []string{idC}}}},
		{"publish", &Publish{Resource: json.RawMessage(sampleResource)}},
		{"published", &Published{ResourceID: idB, PublicPort: intp(40007)}},
		{"unpublish", &Unpublish{ResourceID: idB}},
		{"broadcast-start", &BroadcastStart{Broadcast: json.RawMessage(sampleBroadcast), MaxListeners: intp(4)}},
		{"broadcast-started", &BroadcastStarted{BroadcastID: idC}},
		{"broadcast-update", &BroadcastUpdate{Broadcast: json.RawMessage(sampleBroadcast), MaxListeners: intp(0)}},
		{"broadcast-end", &BroadcastEnd{BroadcastID: idC}},
		{"pairing-request", &PairingRequest{}},
		{"pairing-code", &PairingCode{Code: "ABCD2345", ExpiresAt: "2026-10-07T12:10:00Z"}},
		{"revoke", &Revoke{DeviceID: idB}},
		{"connect-resource", &Connect{ResourceID: idB}},
		{"connect-share", &Connect{ShareID: idB}},
		{"connect-broadcast", &Connect{BroadcastID: idC, Scheme: "rtsp"}},
		{"connected", &Connected{}},
		{"open", &Open{TunnelID: idC, ResourceID: idB}},
		{"attach", &Attach{TunnelID: idC}},
		{"upload", &Upload{BroadcastID: idC, ContentType: "audio/aac"}},
		{"upload-accepted", &UploadAccepted{}},
		{"cast", &Cast{BroadcastID: idC, ReceiverDeviceID: idB}},
		{"cast-offer", &CastOffer{Broadcast: json.RawMessage(sampleBroadcast), FromDeviceID: idA}},
		{"cast-answer", &CastAnswer{BroadcastID: idC, Accepted: false, Reason: CastDeclined}},
		{"cast-result", &CastResult{BroadcastID: idC, ReceiverDeviceID: idB, Accepted: false, Reason: CastUnavailable}},
		{"cast-stop", &CastStop{BroadcastID: idC, ReceiverDeviceID: idB}},
		{"clipboard-put-text", &ClipboardPut{Kind: "text", Size: 11}},
		{"clipboard-put-image", &ClipboardPut{Kind: "image", Size: 4096, MimeType: "image/png", Name: "screenshot"}},
		{"clipboard-put-file", &ClipboardPut{Kind: "file", Size: 123456, MimeType: "application/pdf", Name: "notes.txt"}},
		{"clipboard-ready", &ClipboardReady{}},
		{"clipboard-stored", &ClipboardStored{ClipID: idB, SHA256: sampleSHA, StoredAt: "2026-10-07T12:00:00Z"}},
		{"clipboard-info", &ClipboardInfo{}},
		{"clipboard-state", &ClipboardState{Value: &ClipboardValueBody{ClipID: idB, Size: 123456, SHA256: sampleSHA,
			StoredAt: "2026-10-07T12:00:00Z", SourceDeviceID: idA, SourceDeviceName: "Phone",
			Kind: "file", MimeType: "application/pdf", Name: "notes.txt"}}},
		{"clipboard-get", &ClipboardGet{ClipID: idB}},
		{"clipboard-get-unnamed", &ClipboardGet{}},
		{"clipboard-value", &ClipboardValue{ClipboardValueBody: ClipboardValueBody{ClipID: idB, Size: 11, SHA256: sampleSHA,
			StoredAt: "2026-10-07T12:00:00Z", SourceDeviceID: idA, SourceDeviceName: "Phone", Kind: "text"}}},
		{"clipboard-empty", &ClipboardEmpty{}},
		{"register", &Register{InvitationCode: idC, Login: "bob", Password: "Passw0rdTest", DeviceID: idB, DeviceName: "Phone", Product: "fms-android"}},
		{"password-change", &PasswordChange{OldPassword: "OldPassw0rd1", NewPassword: "NewPassw0rd1"}},
		{"password-changed", &PasswordChanged{DeviceToken: tok}},
		{"account-delete", &AccountDelete{Password: "Passw0rdTest"}},
		{"account-deleted", &AccountDeleted{}},
	}
}

type vector struct {
	Name string `json:"name"`
	JSON string `json:"json"`
	Hex  string `json:"hex"` // the whole frame: 4-byte big-endian length, then the JSON body
}

func TestEverySampleCoversATypeAndEveryTypeHasASample(t *testing.T) {
	seen := map[string]bool{}
	for _, s := range samples() {
		seen[s.e.TypeName()] = true
	}
	for _, typ := range Types() {
		if !seen[typ] {
			t.Errorf("no sample for type %q", typ)
		}
		if New(typ) == nil {
			t.Errorf("type %q has no constructor", typ)
		}
	}
	if len(Types()) != len(registry) {
		t.Errorf("Types() lists %d, the registry holds %d", len(Types()), len(registry))
	}
}

func TestGoldenFrames(t *testing.T) {
	path := filepath.Join("testdata", "vectors.json")
	if *update {
		var out []vector
		for _, s := range samples() {
			body, err := Encode(s.e)
			if err != nil {
				t.Fatalf("%s: %v", s.name, err)
			}
			frame, err := Frame(s.e)
			if err != nil {
				t.Fatal(err)
			}
			out = append(out, vector{s.name, string(body), hex.EncodeToString(frame)})
		}
		b, _ := json.MarshalIndent(out, "", "  ")
		if err := os.MkdirAll("testdata", 0o755); err != nil {
			t.Fatal(err)
		}
		if err := os.WriteFile(path, append(b, '\n'), 0o644); err != nil {
			t.Fatal(err)
		}
	}
	raw, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("golden frames missing (run with -update-golden): %v", err)
	}
	var vs []vector
	if err := json.Unmarshal(raw, &vs); err != nil {
		t.Fatal(err)
	}
	if len(vs) != len(samples()) {
		t.Fatalf("golden file has %d frames, the sample set %d", len(vs), len(samples()))
	}
	for i, s := range samples() {
		v := vs[i]
		if v.Name != s.name {
			t.Fatalf("frame %d is %q, expected %q", i, v.Name, s.name)
		}
		frame, err := Frame(s.e)
		if err != nil {
			t.Fatal(err)
		}
		if got := hex.EncodeToString(frame); got != v.Hex {
			t.Errorf("%s: frame bytes changed\n got  %s\n want %s", s.name, got, v.Hex)
		}
		fb, _ := hex.DecodeString(v.Hex)
		if len(fb) < 5 || int(fb[0])<<24|int(fb[1])<<16|int(fb[2])<<8|int(fb[3]) != len(fb)-4 || fb[0] != 0x00 {
			t.Errorf("%s: length prefix does not match the body, or the frame does not start with 0x00", s.name)
		}
		// the recorded bytes decode back to the same typed envelope
		back, err := NewFrameReader(bytes.NewReader(fb), MaxFrameAuth).Read()
		if err != nil {
			t.Fatalf("%s: %v", s.name, err)
		}
		want := s.e
		*want.head() = Head{SchemaVersion, want.TypeName()}
		if !reflect.DeepEqual(normalizeForCompare(back), normalizeForCompare(want)) {
			t.Errorf("%s: round trip changed the envelope:\n got  %+v\n want %+v", s.name, back, want)
		}
		// and encoding what was decoded gives the same bytes: the codec is a fixed point
		again, err := Frame(back)
		if err != nil || !bytes.Equal(again, fb) {
			t.Errorf("%s: re-encoding the decoded envelope changed the bytes (%v)", s.name, err)
		}
	}
}

// normalizeForCompare turns the nil slices an encoder fills with empty ones into one form.
func normalizeForCompare(e Envelope) Envelope {
	b, _ := Encode(e)
	out, _ := Decode(b)
	return out
}

func TestMemberOrderIsTheTableOrderAndSchemaVersionAndTypeComeFirst(t *testing.T) {
	for _, s := range samples() {
		body, err := Encode(s.e)
		if err != nil {
			t.Fatal(err)
		}
		if !strings.HasPrefix(string(body), `{"schemaVersion":2,"type":"`+s.e.TypeName()+`"`) {
			t.Errorf("%s: schemaVersion and type are not first: %s", s.name, body)
		}
	}
	e := &Enroll{Login: "a", Password: "p", DeviceID: idA, DeviceName: "n", Product: "x", Platform: "pl", ProductVersion: "v"}
	body, _ := Encode(e)
	order := []string{`"login"`, `"password"`, `"deviceId"`, `"deviceName"`, `"product"`, `"productVersion"`, `"platform"`}
	last := -1
	for _, k := range order {
		i := strings.Index(string(body), k)
		if i < last {
			t.Fatalf("members out of table order at %s: %s", k, body)
		}
		last = i
	}
}

func TestEncodeFixesTheHeadWhateverTheCallerWrote(t *testing.T) {
	e := &Keepalive{Head: Head{SchemaVersion: 9, Type: "bogus"}}
	body, err := Encode(e)
	if err != nil || string(body) != `{"schemaVersion":2,"type":"keepalive"}` {
		t.Fatalf("%s %v", body, err)
	}
	if _, err := Encode(nil); !errors.Is(err, ErrBadMessage) {
		t.Fatalf("nil: %v", err)
	}
	var nilKeep *Keepalive
	if _, err := Encode(nilKeep); !errors.Is(err, ErrBadMessage) {
		t.Fatalf("typed nil: %v", err)
	}
	if _, err := Encode(&Unknown{}); !errors.Is(err, ErrBadMessage) {
		t.Fatalf("unknown: %v", err)
	}
}

func TestDecodeIgnoresUnknownMembersAndTypes(t *testing.T) {
	e, err := Decode([]byte(`{"schemaVersion":2,"type":"connect","shareId":"x","future":{"a":1},"another":[1,2]}`))
	c, ok := e.(*Connect)
	if err != nil || !ok || c.ShareID != "x" {
		t.Fatalf("unknown members must be ignored: %v %+v", err, e)
	}
	e, err = Decode([]byte(`{"schemaVersion":2,"type":"hologram","x":1}`))
	if err != nil {
		t.Fatalf("an unknown type is not an error: %v", err)
	}
	if Known(e) {
		t.Fatal("an unknown type must report Known() == false")
	}
	if u := e.(*Unknown); u.Type != "hologram" || !bytes.Contains(u.Raw, []byte(`"x":1`)) {
		t.Fatalf("unknown envelope: %+v", u)
	}
}

func TestDecodeRefusals(t *testing.T) {
	cases := []struct {
		name string
		in   string
		want error
	}{
		{"absent schemaVersion", `{"type":"keepalive"}`, ErrSchema},
		{"future schemaVersion", `{"schemaVersion":3,"type":"keepalive"}`, ErrSchema},
		{"zero schemaVersion", `{"schemaVersion":0,"type":"keepalive"}`, ErrSchema},
		{"the legacy wire", `{"schemaVersion":1,"type":"register","shareId":"x"}`, ErrVersionRefused},
		{"the legacy wire, any type", `{"schemaVersion":1,"type":"keepalive"}`, ErrVersionRefused},
		{"no type", `{"schemaVersion":2}`, ErrNoType},
		{"not json", `nope`, ErrMalformed},
		{"an array", `[1]`, ErrMalformed},
		{"wrong member type", `{"schemaVersion":2,"type":"published","resourceId":"x","publicPort":"x"}`, ErrMalformed},
		{"enroll without a login", `{"schemaVersion":2,"type":"enroll","password":"p","deviceId":"` + idA + `","deviceName":"n","product":"x"}`, ErrMalformed},
		{"enroll without a secret", `{"schemaVersion":2,"type":"enroll","login":"a","deviceId":"` + idA + `","deviceName":"n","product":"x"}`, ErrMalformed},
		{"enroll with both secrets", `{"schemaVersion":2,"type":"enroll","login":"a","password":"p","pairingCode":"c","deviceId":"` + idA + `","deviceName":"n","product":"x"}`, ErrMalformed},
		{"enroll with a bad device id", `{"schemaVersion":2,"type":"enroll","login":"a","password":"p","deviceId":"short","deviceName":"n","product":"x"}`, ErrMalformed},
		{"enroll with a control character", `{"schemaVersion":2,"type":"enroll","login":"a","password":"p","deviceId":"` + idA + `","deviceName":"n\u0001","product":"x"}`, ErrMalformed},
		{"enroll with a long device name", `{"schemaVersion":2,"type":"enroll","login":"a","password":"p","deviceId":"` + idA + `","deviceName":"` + strings.Repeat("n", 65) + `","product":"x"}`, ErrMalformed},
		{"enroll with an oversize receiver list", `{"schemaVersion":2,"type":"enroll","login":"a","password":"p","deviceId":"` + idA + `","deviceName":"n","product":"x","receiver":{"modes":["1","2","3","4","5","6","7","8","9"]}}`, ErrMalformed},
		{"hello without a token", `{"schemaVersion":2,"type":"hello","deviceId":"` + idA + `"}`, ErrMalformed},
		{"publish without a resource", `{"schemaVersion":2,"type":"publish"}`, ErrMalformed},
		{"publish with a resource that is not an object", `{"schemaVersion":2,"type":"publish","resource":"x"}`, ErrMalformed},
		{"connect with no id", `{"schemaVersion":2,"type":"connect"}`, ErrMalformed},
		{"connect with two ids", `{"schemaVersion":2,"type":"connect","resourceId":"a","shareId":"b"}`, ErrMalformed},
		{"connect by broadcast without a scheme", `{"schemaVersion":2,"type":"connect","broadcastId":"a"}`, ErrMalformed},
		{"connect by broadcast with another scheme", `{"schemaVersion":2,"type":"connect","broadcastId":"a","scheme":"ftp"}`, ErrMalformed},
		{"connect by resource with a scheme", `{"schemaVersion":2,"type":"connect","resourceId":"a","scheme":"http"}`, ErrMalformed},
		{"attach without a tunnel", `{"schemaVersion":2,"type":"attach"}`, ErrMalformed},
		{"upload without a content type", `{"schemaVersion":2,"type":"upload","broadcastId":"a"}`, ErrMalformed},
	}
	for _, c := range cases {
		if _, err := Decode([]byte(c.in)); !errors.Is(err, c.want) {
			t.Errorf("%s: got %v, want %v", c.name, err, c.want)
		}
	}
}

func TestAMalformedConnectIdIsStillAConnect(t *testing.T) {
	// A malformed id is answered `unavailable` like an unknown one (164 rule 5); the codec must let it through.
	e, err := Decode([]byte(`{"schemaVersion":2,"type":"connect","resourceId":"not-an-id"}`))
	if err != nil || e.(*Connect).ResourceID != "not-an-id" {
		t.Fatalf("%v %+v", err, e)
	}
}

func TestFrameLimitsFollowTheStreamState(t *testing.T) {
	frameOf := func(n int) []byte {
		// a Keepalive with a unknown member padded to n bytes of body
		pad := n - len(`{"schemaVersion":2,"type":"keepalive","p":""}`)
		body := `{"schemaVersion":2,"type":"keepalive","p":"` + strings.Repeat("a", pad) + `"}`
		if len(body) != n {
			t.Fatalf("test body is %d, want %d", len(body), n)
		}
		out := []byte{byte(n >> 24), byte(n >> 16), byte(n >> 8), byte(n)}
		return append(out, body...)
	}
	read := func(frame []byte, limit int) error {
		_, err := NewFrameReader(bytes.NewReader(frame), limit).Read()
		return err
	}
	// before authentication
	if err := read(frameOf(MaxFrame), MaxFrame); err != nil {
		t.Errorf("a frame of exactly %d bytes before authentication: %v", MaxFrame, err)
	}
	if err := read(frameOf(MaxFrame+1), MaxFrame); !errors.Is(err, ErrFrameSize) {
		t.Errorf("one above the pre-auth limit: %v", err)
	}
	// after authentication
	if err := read(frameOf(MaxFrameAuth), MaxFrameAuth); err != nil {
		t.Errorf("a frame of exactly %d bytes after authentication: %v", MaxFrameAuth, err)
	}
	if err := read(frameOf(MaxFrameAuth+1), MaxFrameAuth); !errors.Is(err, ErrFrameSize) {
		t.Errorf("one above the authenticated limit: %v", err)
	}
	// the limit changes in place, as a stream authenticates
	fr := NewFrameReader(bytes.NewReader(append(frameOf(MaxFrame+1), frameOf(MaxFrame+1)...)), MaxFrame)
	if _, err := fr.Read(); !errors.Is(err, ErrFrameSize) {
		t.Fatalf("first: %v", err)
	}
	// zero length
	if err := read([]byte{0, 0, 0, 0}, MaxFrameAuth); !errors.Is(err, ErrFrameSize) {
		t.Errorf("zero length: %v", err)
	}
	// 0xffffffff must not overflow into an allocation
	if err := read([]byte{0xff, 0xff, 0xff, 0xff}, MaxFrameAuth); !errors.Is(err, ErrFrameSize) {
		t.Errorf("a 4 GiB length: %v", err)
	}
	// truncated body
	if err := read([]byte{0, 0, 0, 10, '{'}, MaxFrameAuth); !errors.Is(err, io.ErrUnexpectedEOF) {
		t.Errorf("truncated body: %v", err)
	}
	// an empty stream is a clean EOF
	if err := read(nil, MaxFrameAuth); !errors.Is(err, io.EOF) {
		t.Errorf("empty stream: %v", err)
	}
}

func TestWriterHoldsTheLimitInForce(t *testing.T) {
	big := &Directory{Resources: []json.RawMessage{json.RawMessage(`{"x":"` + strings.Repeat("a", MaxFrame) + `"}`)}}
	var sink bytes.Buffer
	fw := NewFrameWriter(&sink, MaxFrame)
	if err := fw.Write(big); !errors.Is(err, ErrFrameSize) {
		t.Fatalf("a body above the pre-auth limit must not be written: %v", err)
	}
	if sink.Len() != 0 {
		t.Fatal("a refused frame wrote bytes")
	}
	fw.SetLimit(MaxFrameAuth)
	if err := fw.Write(big); err != nil || sink.Len() <= MaxFrame {
		t.Fatalf("after authentication the same envelope fits: %v (%d bytes)", err, sink.Len())
	}
	tooBig := &Directory{Resources: []json.RawMessage{json.RawMessage(`{"x":"` + strings.Repeat("a", MaxFrameAuth) + `"}`)}}
	if err := fw.Write(tooBig); !errors.Is(err, ErrFrameSize) {
		t.Fatalf("a body above the authenticated limit: %v", err)
	}
}

func TestReaderLeavesFollowingBytes(t *testing.T) {
	frame, _ := Frame(&Connected{})
	stream := bytes.NewReader(append(frame, []byte("SSH-2.0-test\r\n")...))
	if _, err := NewFrameReader(stream, MaxFrame).Read(); err != nil {
		t.Fatal(err)
	}
	rest, _ := io.ReadAll(stream)
	if string(rest) != "SSH-2.0-test\r\n" {
		t.Fatalf("the reader consumed raw bytes that follow a frame: %q", rest)
	}
}

func TestVersionRefusalCarriesTheServersOwnSchemaVersion(t *testing.T) {
	body, err := Encode(Refuse(ReasonVersionUnsupported))
	if err != nil || string(body) != `{"schemaVersion":2,"type":"refused","reason":"version-unsupported"}` {
		t.Fatalf("%s %v", body, err)
	}
}

func TestReasonsAreTheSixteenOfTheContract(t *testing.T) {
	want := []string{"bad-credentials", "device-revoked", "version-unsupported", "tls-required", "id-collision",
		"capacity", "port-unavailable", "unavailable", "rate-limited", "protection-unsupported",
		"invitation-invalid", "login-taken", "password-policy", "limit-reached", "not-permitted", "signed-out"}
	if len(AllReasons) != 16 {
		t.Fatalf("%d reasons", len(AllReasons))
	}
	for i, r := range AllReasons {
		if string(r) != want[i] || !r.Valid() {
			t.Errorf("reason %d is %q", i, r)
		}
	}
	if Reason("nope").Valid() {
		t.Error("an unlisted reason is valid")
	}
	defer func() {
		if recover() == nil {
			t.Error("Refuse accepted a reason outside the sixteen")
		}
	}()
	Refuse(Reason("nope"))
}

func TestIDsAndTokens(t *testing.T) {
	id := NewID()
	if !ValidID(id) {
		t.Fatalf("a fresh id is invalid: %q", id)
	}
	if NewID() == id {
		t.Fatal("two ids collided")
	}
	for _, bad := range []string{"", "short", id + "A", strings.Repeat("!", 22)} {
		if ValidID(bad) {
			t.Errorf("ValidID(%q) = true", bad)
		}
	}
	// 22 characters whose last one carries non-zero trailing bits decode only non-strictly.
	if ValidID(strings.Repeat("A", 21) + "B") {
		t.Error("an id with non-zero trailing bits must be rejected (strict base64url)")
	}
	token := NewToken()
	if !ValidToken(token) || len(token) != 43 || strings.ContainsAny(token, "=+/") {
		t.Fatalf("token form: %q", token)
	}
	if ValidToken(token[:42]) || ValidToken(token+"A") {
		t.Error("a token of the wrong length is valid")
	}
	// an injected source gives stable bytes
	a := NewIDFrom(bytes.NewReader(bytes.Repeat([]byte{7}, 16)))
	b := NewIDFrom(bytes.NewReader(bytes.Repeat([]byte{7}, 16)))
	if a != b || !ValidID(a) {
		t.Fatalf("injected source: %q %q", a, b)
	}
}

// TestNoEnvelopePrintsAMember is the String guard of ticket 167 rule 5 for the wire types: every
// envelope, as a value and as a pointer, through every verb, prints no member - enroll holds a
// password, enrolled and hello a token, directory an `access`.
func TestNoEnvelopePrintsAMember(t *testing.T) {
	secrets := []string{"Passw0rdTest", tok, "FMSSFTP2.secret", "ABCD2345", "descriptor"}
	for _, s := range samples() {
		for _, v := range []any{s.e, reflect.ValueOf(s.e).Elem().Interface()} {
			for _, verb := range []string{"%v", "%+v", "%#v", "%s", "%q", "%x"} {
				out := fmt.Sprintf(verb, v)
				for _, sec := range secrets {
					if strings.Contains(out, sec) {
						t.Errorf("%s printed with %s carries %q: %s", s.name, verb, sec, out)
					}
				}
			}
		}
	}
	r, _ := ParseRecord([]byte(sampleResource))
	for _, verb := range []string{"%v", "%+v", "%#v", "%s"} {
		if out := fmt.Sprintf(verb, r); strings.Contains(out, "FMSSFTP2") {
			t.Errorf("a record printed with %s carries its access: %s", verb, out)
		}
	}
}

func TestHTTPOpening(t *testing.T) {
	read := func(s string) (HTTPRequest, error) { return ReadHTTPRequest(bufioReader(s)) }
	r, err := read("GET /v2/b/" + idC + "/stream?x=1 HTTP/1.1\r\nHost: a\r\nRange: bytes=0-\r\n\r\n")
	if err != nil || r.Method != "GET" || r.Path != "/v2/b/"+idC+"/stream" {
		t.Fatalf("%+v %v", r, err)
	}
	id, ok := ParseListenPath(r.Path)
	if !ok || id != idC {
		t.Fatalf("listen path: %q %v", id, ok)
	}
	for _, p := range []string{"/", "/v2/b/", "/v2/b//stream", "/v2/b/x/y/stream", "/v2/b/x/streams", "/v1/b/x/stream"} {
		if _, ok := ParseListenPath(p); ok {
			t.Errorf("%q is a listen path", p)
		}
	}
	for _, bad := range []string{"GET /\r\n\r\n", "NOPE\r\n\r\n", "GET / SPDY/3\r\n\r\n", "GET / HTTP/1.1\r\nHost: a\r\n", strings.Repeat("a", 9000)} {
		if _, err := read(bad); !errors.Is(err, ErrHTTPHead) {
			t.Errorf("%.20q accepted: %v", bad, err)
		}
	}
	if b := string(HTTPResponse(405, 0)); !strings.HasPrefix(b, "HTTP/1.1 405 Method Not Allowed\r\n") || !strings.Contains(b, "Allow: GET") {
		t.Errorf("405: %q", b)
	}
	if b := string(HTTPResponse(503, 5)); !strings.Contains(b, "Retry-After: 5") {
		t.Errorf("503: %q", b)
	}
}

func TestDirectoryFitsTheAuthenticatedFrame(t *testing.T) {
	rec := json.RawMessage(`{"x":"` + strings.Repeat("a", 1000) + `"}`)
	var many []json.RawMessage
	for i := 0; i < 100; i++ {
		many = append(many, rec)
	}
	if !DirectoryFits(nil, many, nil) {
		t.Fatal("100 KiB must fit")
	}
	for i := 0; i < 200; i++ {
		many = append(many, rec)
	}
	if DirectoryFits(nil, many, nil) {
		t.Fatal("300 KiB must not fit")
	}
	// the boundary is exact: grow one record until the next byte does not fit
	base := []json.RawMessage{}
	d := &Directory{Revision: 1<<62 - 1}
	overhead, _ := Encode(d)
	fill := MaxFrameAuth - len(overhead) - len(`""`) - 2
	for fill > 0 {
		n := fill
		if n > 15000 {
			n = 15000
		}
		base = append(base, json.RawMessage(`"`+strings.Repeat("a", n)+`"`))
		fill -= n + 3
	}
	if !DirectoryFits(nil, base, nil) {
		t.Fatal("a directory just under the frame limit must fit")
	}
	base[0] = json.RawMessage(`"` + strings.Repeat("a", 15003) + `"`)
	if DirectoryFits(nil, append(base, base...), nil) {
		t.Fatal("a directory over the frame limit fitted")
	}
}

func FuzzFrameReader(f *testing.F) {
	for _, s := range samples() {
		fr, _ := Frame(s.e)
		f.Add(fr)
	}
	f.Add([]byte{0, 0, 0, 0})
	f.Add([]byte{0xff, 0xff, 0xff, 0xff})
	f.Add([]byte{0, 0, 0, 2, '{', '}'})
	f.Fuzz(func(t *testing.T, data []byte) {
		r := NewFrameReader(bytes.NewReader(data), MaxFrameAuth)
		for i := 0; i < 8; i++ {
			if _, err := r.Read(); err != nil {
				return
			}
		}
	})
}

func FuzzDecode(f *testing.F) {
	for _, s := range samples() {
		b, _ := Encode(s.e)
		f.Add(b)
	}
	f.Add([]byte(`{"schemaVersion":1,"type":"register"}`))
	f.Add([]byte(`{"schemaVersion":2,"type":"connect","resourceId":"a","shareId":"b"}`))
	f.Fuzz(func(t *testing.T, data []byte) {
		e, err := Decode(data)
		if err != nil {
			return
		}
		// whatever decodes can be fed back to the encoder without a panic
		if Known(e) {
			_, _ = Encode(e)
		}
	})
}
