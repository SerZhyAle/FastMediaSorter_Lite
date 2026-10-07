package wire

import (
	"bufio"
	"bytes"
	"encoding/json"
	"errors"
	"strings"
	"testing"
	"time"
)

func bufioReader(s string) *bufio.Reader { return bufio.NewReader(strings.NewReader(s)) }

func TestRecordKeepsUnknownMembersThreeLevelsDeepVerbatim(t *testing.T) {
	in := `{"resourceId":"` + idB + `","kind":"sftp-share","name":"x","future":{"a":{"b":{"c":[1,2,{"d":"<&>"}]}}},"access":{"descriptor":"D"}}`
	r, err := ParseRecord([]byte(in))
	if err != nil {
		t.Fatal(err)
	}
	r.SetStr("deviceId", idA)
	r.Delete("access")
	out := r.Marshal()
	var back map[string]json.RawMessage
	if err := json.Unmarshal(out, &back); err != nil {
		t.Fatalf("%s: %v", out, err)
	}
	if string(back["future"]) != `{"a":{"b":{"c":[1,2,{"d":"<&>"}]}}}` {
		t.Fatalf("an unknown member did not survive: %s", back["future"])
	}
	if _, ok := back["access"]; ok {
		t.Fatal("a deleted member survived")
	}
	if s, _ := r.Str("deviceId"); s != idA {
		t.Fatalf("deviceId: %q", s)
	}
	// members are sorted, so the bytes are stable
	if !strings.HasPrefix(string(out), `{"deviceId":`) || bytes.Index(out, []byte(`"kind"`)) > bytes.Index(out, []byte(`"name"`)) {
		t.Fatalf("members are not sorted: %s", out)
	}
	again, _ := ParseRecord(out)
	if !bytes.Equal(again.Marshal(), out) {
		t.Fatal("Marshal is not a fixed point")
	}
}

func TestRecordBounds(t *testing.T) {
	big := `{"x":"` + strings.Repeat("a", MaxRecord) + `"}`
	if _, err := ParseRecord([]byte(big)); !errors.Is(err, ErrRecordSize) {
		t.Fatalf("a record above 16 KiB: %v", err)
	}
	pad := MaxRecord - len(`{"x":""}`)
	exact := `{"x":"` + strings.Repeat("a", pad) + `"}`
	if len(exact) != MaxRecord {
		t.Fatalf("test record is %d", len(exact))
	}
	if _, err := ParseRecord([]byte(exact)); err != nil {
		t.Fatalf("a record of exactly 16 KiB: %v", err)
	}
	for _, bad := range []string{``, `[]`, `"x"`, `1`, `null`, `{"a":1} x`, `{`} {
		if _, err := ParseRecord([]byte(bad)); !errors.Is(err, ErrRecordShape) {
			t.Errorf("%q: %v", bad, err)
		}
	}
}

func TestRecordTypedAccessorsAndTime(t *testing.T) {
	r, _ := ParseRecord([]byte(`{"n":5,"s":"v","bad":{"x":1}}`))
	if n, ok := r.Int("n"); !ok || n != 5 {
		t.Fatal("Int")
	}
	if _, ok := r.Int("s"); ok {
		t.Fatal("Int of a string")
	}
	if _, ok := r.Str("bad"); ok {
		t.Fatal("Str of an object")
	}
	if r.Has("zz") || r.Raw("zz") != nil {
		t.Fatal("absent member")
	}
	r.SetInt("p", 40007)
	r.SetTime("t", time.Date(2026, 10, 7, 12, 0, 0, 999, time.FixedZone("x", 3600)))
	if s, _ := r.Str("t"); s != "2026-10-07T11:00:00Z" {
		t.Fatalf("time: %q", s)
	}
	c := r.Clone()
	c.Delete("n")
	if !r.Has("n") {
		t.Fatal("Clone shares members")
	}
	if tm, err := ParseTime("2026-10-07T11:00:00Z"); err != nil || tm.Hour() != 11 {
		t.Fatalf("ParseTime: %v", err)
	}
}

func TestCheckReceiver(t *testing.T) {
	good := `{"modes":["AUDIO_ONLY","VIDEO_AUDIO"],"transports":["HTTP_ADTS"],"plays":[{"mode":"AUDIO_ONLY","transport":"HTTP_ADTS"}],"unknown":{"x":1}}`
	if err := CheckReceiver([]byte(good)); err != nil {
		t.Fatalf("a good receiver: %v", err)
	}
	bad := []string{
		`[]`, `"x"`, `null`,
		`{"modes":"AUDIO_ONLY"}`,
		`{"modes":[1]}`,
		`{"modes":["a","b","c","d","e","f","g","h","i"]}`,
		`{"transports":["` + strings.Repeat("x", 17) + `"]}`,
		`{"plays":"x"}`,
		`{"plays":[{"mode":"A"}]}`,
		`{"plays":[{"mode":"A","transport":"` + strings.Repeat("x", 17) + `"}]}`,
		`{"plays":[{"mode":"A","transport":"B"},{"mode":"A","transport":"B"},{"mode":"A","transport":"B"},{"mode":"A","transport":"B"},{"mode":"A","transport":"B"},{"mode":"A","transport":"B"},{"mode":"A","transport":"B"},{"mode":"A","transport":"B"},{"mode":"A","transport":"B"}]}`,
	}
	for _, b := range bad {
		if err := CheckReceiver([]byte(b)); err == nil {
			t.Errorf("%.60q accepted", b)
		}
	}
}
