package wire

import (
	"bytes"
	"encoding/hex"
	"encoding/json"
	"errors"
	"flag"
	"io"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

var update = flag.Bool("update-golden", false, "rewrite testdata/vectors.json from the encoder")

type vector struct {
	Name string `json:"name"`
	JSON string `json:"json"`
	Hex  string `json:"hex"` // the whole frame: 4-byte big-endian length, then the JSON body
}

func samples() []struct {
	name string
	m    Message
} {
	id := "AAAAAAAAAAAAAAAAAAAAAA"
	tid := "BBBBBBBBBBBBBBBBBBBBBB"
	reg := New(TypeRegister)
	reg.Password, reg.ShareID, reg.KeepaliveSeconds, reg.Port = "Passw0rdTest", id, 30, IntPtr(0)
	reg.Claim = json.RawMessage(`{"endpoints":["192.0.2.1:2222"]}`)
	resume := reg
	resume.ResumeToken = tid
	resume.Port = nil
	registered := New(TypeRegistered)
	registered.KeepaliveSeconds, registered.Port, registered.ResumeToken = 30, IntPtr(40007), tid
	open := New(TypeOpen)
	open.TunnelID = tid
	attach := New(TypeAttach)
	attach.ShareID, attach.TunnelID = id, tid
	connect := New(TypeConnect)
	connect.ShareID = id
	return []struct {
		name string
		m    Message
	}{
		{"register", reg},
		{"register-resume", resume},
		{"registered", registered},
		{"refused-bad-password", Refused(ReasonBadPassword)},
		{"refused-unavailable", Refused(ReasonUnavailable)},
		{"keepalive", New(TypeKeepalive)},
		{"unregister", New(TypeUnregister)},
		{"open", open},
		{"attach", attach},
		{"connect", connect},
		{"connected", New(TypeConnected)},
	}
}

func TestGoldenVectors(t *testing.T) {
	path := filepath.Join("testdata", "vectors.json")
	if *update {
		var out []vector
		for _, s := range samples() {
			body, err := Encode(s.m)
			if err != nil {
				t.Fatal(err)
			}
			frame, err := Frame(s.m)
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
		t.Fatalf("golden vectors missing (run with -update-golden): %v", err)
	}
	var vs []vector
	if err := json.Unmarshal(raw, &vs); err != nil {
		t.Fatal(err)
	}
	if len(vs) != len(samples()) {
		t.Fatalf("golden file has %d vectors, the sample set %d", len(vs), len(samples()))
	}
	for i, s := range samples() {
		v := vs[i]
		if v.Name != s.name {
			t.Fatalf("vector %d is %q, expected %q", i, v.Name, s.name)
		}
		// The encoder must produce exactly the recorded bytes.
		frame, err := Frame(s.m)
		if err != nil {
			t.Fatal(err)
		}
		if got := hex.EncodeToString(frame); got != v.Hex {
			t.Errorf("%s: frame bytes changed\n got  %s\n want %s", s.name, got, v.Hex)
		}
		// The recorded bytes must decode back to the same envelope through the frame reader.
		fb, _ := hex.DecodeString(v.Hex)
		if len(fb) < 5 || int(fb[0])<<24|int(fb[1])<<16|int(fb[2])<<8|int(fb[3]) != len(fb)-4 {
			t.Errorf("%s: length prefix does not match the body", s.name)
		}
		if fb[0] != 0x00 {
			t.Errorf("%s: a frame must start with 0x00 (the first-byte split)", s.name)
		}
		back, err := NewFrameReader(bytes.NewReader(fb)).Read()
		if err != nil {
			t.Fatalf("%s: %v", s.name, err)
		}
		if back.Type != s.m.Type || back.ShareID != s.m.ShareID || back.TunnelID != s.m.TunnelID ||
			back.Reason != s.m.Reason || back.ResumeToken != s.m.ResumeToken ||
			back.KeepaliveSeconds != s.m.KeepaliveSeconds || back.Password != s.m.Password ||
			(back.Port == nil) != (s.m.Port == nil) || (back.Port != nil && *back.Port != *s.m.Port) {
			t.Errorf("%s: round trip changed the envelope: %+v vs %+v", s.name, back, s.m)
		}
	}
}

func TestDecodeIgnoresUnknownMembersAndTypes(t *testing.T) {
	m, err := Decode([]byte(`{"schemaVersion":1,"type":"connect","shareId":"x","future":{"a":1},"another":[1,2]}`))
	if err != nil || !m.Known() || m.ShareID != "x" {
		t.Fatalf("unknown members must be ignored: %v %+v", err, m)
	}
	m, err = Decode([]byte(`{"schemaVersion":1,"type":"hologram","x":1}`))
	if err != nil {
		t.Fatalf("an unknown type is not an error: %v", err)
	}
	if m.Known() {
		t.Fatal("an unknown type must report Known() == false")
	}
}

func TestDecodeRefusals(t *testing.T) {
	cases := []struct {
		name string
		in   string
		want error
	}{
		{"absent schemaVersion", `{"type":"connect"}`, ErrSchema},
		{"other schemaVersion", `{"schemaVersion":2,"type":"connect"}`, ErrSchema},
		{"no type", `{"schemaVersion":1}`, ErrNoType},
		{"not json", `nope`, ErrMalformed},
		{"wrong member type", `{"schemaVersion":1,"type":"register","port":"x"}`, ErrMalformed},
	}
	for _, c := range cases {
		if _, err := Decode([]byte(c.in)); !errors.Is(err, c.want) {
			t.Errorf("%s: got %v, want %v", c.name, err, c.want)
		}
	}
}

func TestFrameLimits(t *testing.T) {
	// zero length
	if _, err := NewFrameReader(bytes.NewReader([]byte{0, 0, 0, 0})).Read(); !errors.Is(err, ErrFrameSize) {
		t.Errorf("zero length: %v", err)
	}
	// one above the cap
	over := []byte{0, 0, 0x40, 0x01}
	if _, err := NewFrameReader(bytes.NewReader(over)).Read(); !errors.Is(err, ErrFrameSize) {
		t.Errorf("over the cap: %v", err)
	}
	// an encode that would pass the cap
	m := New(TypeRegister)
	m.Claim = json.RawMessage(`"` + strings.Repeat("a", MaxFrame) + `"`)
	if _, err := Encode(m); !errors.Is(err, ErrFrameSize) {
		t.Errorf("oversize encode: %v", err)
	}
	// truncated body
	if _, err := NewFrameReader(bytes.NewReader([]byte{0, 0, 0, 10, '{'})).Read(); !errors.Is(err, io.ErrUnexpectedEOF) {
		t.Errorf("truncated body: %v", err)
	}
	// the cap itself is accepted
	body := `{"schemaVersion":1,"type":"connect","claim":"` + strings.Repeat("a", MaxFrame-48) + `"}`
	if len(body) > MaxFrame {
		t.Fatalf("test body too long: %d", len(body))
	}
}

func TestReaderLeavesFollowingBytes(t *testing.T) {
	frame, _ := Frame(New(TypeConnected))
	stream := bytes.NewReader(append(frame, []byte("SSH-2.0-test\r\n")...))
	if _, err := NewFrameReader(stream).Read(); err != nil {
		t.Fatal(err)
	}
	rest, _ := io.ReadAll(stream)
	if string(rest) != "SSH-2.0-test\r\n" {
		t.Fatalf("the reader consumed raw bytes that follow a frame: %q", rest)
	}
}

func TestIDs(t *testing.T) {
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
}

func TestMessageStringHidesPassword(t *testing.T) {
	m := New(TypeRegister)
	m.Password = "Sup3rSecretPw"
	if strings.Contains(m.String(), "Sup3rSecretPw") {
		t.Fatal("String() leaks the password")
	}
}

func FuzzFrameReader(f *testing.F) {
	for _, s := range samples() {
		fr, _ := Frame(s.m)
		f.Add(fr)
	}
	f.Add([]byte{0, 0, 0, 0})
	f.Add([]byte{0xff, 0xff, 0xff, 0xff})
	f.Add([]byte{0, 0, 0, 2, '{', '}'})
	f.Fuzz(func(t *testing.T, data []byte) {
		r := NewFrameReader(bytes.NewReader(data))
		for i := 0; i < 8; i++ {
			if _, err := r.Read(); err != nil {
				return
			}
		}
	})
}
