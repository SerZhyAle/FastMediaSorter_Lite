package wire

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"strings"
	"testing"
)

// Ticket 234, item DN of DEVICE-EXCHANGE 27: the clipboard kind `sealed` on the wire. A sealed
// `clipboard-put` carries `size` (the sealed bytes) and `sealed` (the box) and nothing that describes the
// content; `clipboard-state` and `clipboard-value` carry the same box back. The box is opaque to the
// server (item DH): whatever it holds it is delivered as it came.

// clipSealedBox is the box of the `one-segment` member of the `clipboard` vectors (e2e-v1.json): a real
// box in the compact form a sender writes, members in the sender's order. The server reads none of it.
const clipSealedBox = `{"v":1,"from":"dev-phone","fromKey":"BMHBNOi24fTvs6FVK82lwF9b1Kmd1yjW_czRpmXYba0FIgE5tdQz6-ECIg4o1ZsDxuymoQC3aCvBDvmY6UO4iZw","n":"BC2hdMS2v4zmsfK-","ct":"6YkXLfZy1UuSVEcgoZBshhxhf_t4lNg-uCTbfefMg2M6NhJeMmzRRuuZvnmgRpWL2VUHbphySPhXvV-PUNgQTS4Qwq7AaOLXFpMoFGW4EqAOsW4c-IBcpD7FnhC-lHwUueTu4Ru6zTSUDDf_JngW6nfVshcqoxhXgDHnGjZFVRwnMhGLAUUK4926ivkOczAqTELkCPcqtAXo36FAweIUrrAig0oUJ0dHFLw5dWWfEGTaay48QPStrr31kfnn","keys":[{"to":"dev-tablet","enc":"BGaIjZ4FjWNcq-clSbHiM3Ruan93aXH51o0iq4BeNOvsfcpqPTvbhO0iuCV3jOw8dFHL6vMUzCVukZn5PPfzXZA","wk":"6mTnH4Hg63mQR--UVIfxNX3m29c574IYNK01g1Tx_CHNjcHyd4iuRbRz_TQ2_sfR"}]}`

// clipPutBody is the JSON body of a clipboard-put with the given members, written by hand so a test can
// send what the encoder would never produce.
func clipPutBody(members string) []byte {
	return []byte(fmt.Sprintf(`{"schemaVersion":%d,"type":"clipboard-put",%s}`, SchemaVersion, members))
}

func mustEncode(t *testing.T, e Envelope) []byte {
	t.Helper()
	b, err := Encode(e)
	if err != nil {
		t.Fatal(err)
	}
	return b
}

func TestASealedPutCarriesItsBoxVerbatimAndNothingThatDescribesTheContent(t *testing.T) {
	put := &ClipboardPut{Kind: ClipKindSealed, Size: 1016, Sealed: json.RawMessage(clipSealedBox)}
	body := mustEncode(t, put)
	s := string(body)
	if !strings.Contains(s, `"kind":"sealed"`) || !strings.Contains(s, `"size":1016`) {
		t.Fatalf("the sealed put lost its kind or size: %s", s)
	}
	if !strings.HasSuffix(s, `"sealed":`+clipSealedBox+`}`) {
		t.Fatalf("the box is not in the put as it was given: %s", s)
	}
	if strings.Contains(s, `"mimeType"`) || strings.Contains(s, `"name"`) {
		t.Fatalf("a sealed put carries no mimeType and no name in clear: %s", s)
	}
	e, err := Decode(body)
	if err != nil {
		t.Fatalf("a sealed put with a box is a valid envelope: %v", err)
	}
	got, ok := e.(*ClipboardPut)
	if !ok || got.Kind != ClipKindSealed || got.Size != 1016 || string(got.Sealed) != clipSealedBox {
		t.Fatalf("the sealed put did not survive the codec: %+v", e)
	}

	// the member order of the sender does not matter to the reader
	e, err = Decode(clipPutBody(`"sealed":` + clipSealedBox + `,"size":1016,"kind":"sealed"`))
	if err != nil {
		t.Fatalf("a sealed put with the box first: %v", err)
	}
	if got := e.(*ClipboardPut); string(got.Sealed) != clipSealedBox {
		t.Fatal("the box moved when its member came first")
	}
}

func TestTheStateAndTheValueDeliverTheBoxBackByteForByte(t *testing.T) {
	body := ClipboardValueBody{
		ClipID: "BBBBBBBBBBBBBBBBBBBBBA", Size: 1016, SHA256: strings.Repeat("ab", 32), StoredAt: "2026-10-16T08:00:00Z",
		SourceDeviceID: "AAAAAAAAAAAAAAAAAAAAAA", SourceDeviceName: "Phone",
		Kind: ClipKindSealed, Sealed: json.RawMessage(clipSealedBox),
	}
	for name, e := range map[string]Envelope{
		"clipboard-state": &ClipboardState{Value: &body},
		"clipboard-value": &ClipboardValue{ClipboardValueBody: body},
	} {
		b := mustEncode(t, e)
		if !bytes.Contains(b, []byte(`"sealed":`+clipSealedBox)) {
			t.Fatalf("%s: the box is not in the frame as it was stored: %s", name, b)
		}
		if bytes.Contains(b, []byte(`"mimeType"`)) || bytes.Contains(b, []byte(`"name"`)) {
			t.Fatalf("%s: a sealed value shows a mimeType or a name: %s", name, b)
		}
		back, err := Decode(b)
		if err != nil {
			t.Fatalf("%s: %v", name, err)
		}
		var got *ClipboardValueBody
		switch v := back.(type) {
		case *ClipboardState:
			got = v.Value
		case *ClipboardValue:
			got = &v.ClipboardValueBody
		}
		if got == nil || got.Kind != ClipKindSealed || string(got.Sealed) != clipSealedBox {
			t.Fatalf("%s: the kind or the box changed on the way: %+v", name, got)
		}
	}
}

// A box is an opaque member (item DH): the server must hand it back as it came whatever it holds, so a
// reader that checks `ct` against the bytes it received is never confronted with a re-marshalled box -
// no reordered members, no changed escapes, no number turned into a float, no HTML escaping.
func TestAnyBoxIsDeliveredAsItCame(t *testing.T) {
	boxes := map[string]string{
		"real box":              clipSealedBox,
		"empty object":          `{}`,
		"members not sorted":    `{"z":1,"a":2,"m":{"y":1,"b":2}}`,
		"duplicate member":      `{"a":1,"a":2}`,
		"json escapes":          `{"s":"é\/\"\\ 😀 \n"}`,
		"raw utf-8":             `{"s":"é 日本語 😀"}`,
		"html characters":       `{"s":"<b>&amp;</b>"}`,
		"line separators":       "{\"s\":\"a b c\"}",
		"numbers kept as typed": `{"big":18446744073709551616,"f":1.0,"e":1E2,"neg":-0,"z":0.10}`,
		"nested":                `{"keys":[{"to":"a"},{"to":"b"}],"x":{"y":{"z":[]}},"t":true,"n":null}`,
	}
	for name, box := range boxes {
		put := mustEncode(t, &ClipboardPut{Kind: ClipKindSealed, Size: 17, Sealed: json.RawMessage(box)})
		e, err := Decode(put)
		if err != nil {
			t.Fatalf("%s: %v", name, err)
		}
		held := e.(*ClipboardPut).Sealed
		if string(held) != box {
			t.Errorf("%s: the put changed the box:\n got %s\nwant %s", name, held, box)
		}
		state := mustEncode(t, &ClipboardState{Value: &ClipboardValueBody{Kind: ClipKindSealed, Size: 17, Sealed: held}})
		e, err = Decode(state)
		if err != nil {
			t.Fatalf("%s: %v", name, err)
		}
		if got := e.(*ClipboardState).Value.Sealed; string(got) != box {
			t.Errorf("%s: the state changed the box:\n got %s\nwant %s", name, got, box)
		}
	}
}

// The one thing the codec does to a box is drop the whitespace between tokens (RawMessage is compacted
// by the JSON encoder, as for every raw member of this wire): never a member moved, a byte of a string or
// a number touched.
func TestABoxWithWhitespaceIsOnlyCompacted(t *testing.T) {
	pretty := "{\n  \"v\": 1,\n  \"z\": [1, 2],\n  \"a\": \"x y\",\n  \"n\": 1.50\n}"
	e, err := Decode(clipPutBody(`"kind":"sealed","size":9,"sealed":` + pretty))
	if err != nil {
		t.Fatal(err)
	}
	held := e.(*ClipboardPut).Sealed
	if string(held) != pretty {
		t.Fatalf("the decoder changed the box:\n got %q\nwant %q", held, pretty)
	}
	var want bytes.Buffer
	if err := json.Compact(&want, []byte(pretty)); err != nil {
		t.Fatal(err)
	}
	out := mustEncode(t, &ClipboardState{Value: &ClipboardValueBody{Kind: ClipKindSealed, Size: 9, Sealed: held}})
	back, err := Decode(out)
	if err != nil {
		t.Fatal(err)
	}
	if got := back.(*ClipboardState).Value.Sealed; !bytes.Equal(got, want.Bytes()) {
		t.Fatalf("a box with whitespace is not its compact form on the way out:\n got %s\nwant %s", got, want.Bytes())
	}
}

func TestASealedPutThatBreaksItemDNIsAnInvalidEnvelope(t *testing.T) {
	const box = `{"v":1,"n":"NEEDLE-nonce","ct":"NEEDLE-ct","keys":[]}`
	cases := map[string]string{
		"a mimeType":               `"kind":"sealed","size":9,"mimeType":"text/needle-mime","sealed":` + box,
		"a name":                   `"kind":"sealed","size":9,"name":"needle-name.txt","sealed":` + box,
		"a mimeType and a name":    `"kind":"sealed","size":9,"mimeType":"image/png","name":"needle-name.png","sealed":` + box,
		"no box":                   `"kind":"sealed","size":9`,
		"a null box":               `"kind":"sealed","size":9,"sealed":null`,
		"an empty string box":      `"kind":"sealed","size":9,"sealed":""`,
		"a string box":             `"kind":"sealed","size":9,"sealed":"NEEDLE-box"`,
		"an array box":             `"kind":"sealed","size":9,"sealed":[{"n":"NEEDLE-nonce"}]`,
		"a number box":             `"kind":"sealed","size":9,"sealed":5`,
		"a boolean box":            `"kind":"sealed","size":9,"sealed":true`,
		"a box above one record":   `"kind":"sealed","size":9,"sealed":{"pad":"NEEDLE-` + strings.Repeat("a", MaxRecord) + `"}`,
		"size 0":                   `"kind":"sealed","size":0,"sealed":` + box,
		"a negative size":          `"kind":"sealed","size":-1,"sealed":` + box,
		"no size":                  `"kind":"sealed","sealed":` + box,
		"a size that is no number": `"kind":"sealed","size":"9","sealed":` + box,
		"a text with a box":        `"kind":"text","size":9,"sealed":` + box,
		"an image with a box":      `"kind":"image","size":9,"mimeType":"image/png","sealed":` + box,
		"a file with a box":        `"kind":"file","size":9,"mimeType":"application/pdf","name":"needle-name.pdf","sealed":` + box,
		"a null box on a text":     `"kind":"text","size":9,"sealed":null`,
		"a kind in other letters":  `"kind":"Sealed","size":9,"sealed":` + box,
		"a kind with a space":      `"kind":"sealed ","size":9,"sealed":` + box,
	}
	for name, members := range cases {
		e, err := Decode(clipPutBody(members))
		if err == nil {
			t.Errorf("a sealed put with %s was accepted: %+v", name, e)
			continue
		}
		if !errors.Is(err, ErrMalformed) {
			t.Errorf("a sealed put with %s: %v is not a malformed envelope (which closes the stream, 7.1)", name, err)
		}
		if strings.Contains(strings.ToLower(err.Error()), "needle") {
			t.Errorf("a sealed put with %s: the error text carries what the put carried: %q", name, err)
		}
	}
}

func TestABoxThatIsNotJSONLeavesNoTraceInTheError(t *testing.T) {
	for _, members := range []string{
		`"kind":"sealed","size":9,"sealed":{"v":1,"n":"NEEDLE-nonce",`,
		`"kind":"sealed","size":9,"sealed":{"v":1,"n":NEEDLE-nonce}`,
		`"kind":"sealed","size":9,"sealed":{'n':'NEEDLE-nonce'}`,
	} {
		_, err := Decode(clipPutBody(members))
		if !errors.Is(err, ErrMalformed) {
			t.Errorf("%s: %v is not a malformed envelope", members, err)
			continue
		}
		if strings.Contains(strings.ToLower(err.Error()), "needle") {
			t.Errorf("the error text carries a piece of the box: %q", err)
		}
	}
}

func TestTheBoundsOfASealedPut(t *testing.T) {
	// one record of JSON is the most a box may hold (CheckBox: the box fits a record); the check is on bytes
	pad := func(total int) string { return `{"p":"` + strings.Repeat("a", total-8) + `"}` }
	for _, tc := range []struct {
		name string
		box  string
		size string
		ok   bool
	}{
		{"a box of exactly one record", pad(MaxRecord), "9", true},
		{"a box one byte above one record", pad(MaxRecord + 1), "9", false},
		{"the smallest sealed value", clipSealedBox, "1", true},
		{"a sealed value of 5 GiB", clipSealedBox, "5368709120", true},
	} {
		_, err := Decode(clipPutBody(`"kind":"sealed","size":` + tc.size + `,"sealed":` + tc.box))
		if tc.ok != (err == nil) {
			t.Errorf("%s: accepted = %v, error %v", tc.name, err == nil, err)
		}
	}
}

func TestTheBodyOfAStateAndAValueFollowsTheSameRules(t *testing.T) {
	const prefix = `"clipId":"BBBBBBBBBBBBBBBBBBBBBA","size":9,"sha256":"` +
		"abababababababababababababababababababababababababababababababab" + `","storedAt":"2026-10-16T08:00:00Z"`
	bodies := map[string]struct {
		members string
		ok      bool
	}{
		"a sealed value with its box":      {`,"kind":"sealed","sealed":` + clipSealedBox, true},
		"a sealed value with no box":       {`,"kind":"sealed"`, false},
		"a sealed value with a mimeType":   {`,"kind":"sealed","mimeType":"text/plain","sealed":` + clipSealedBox, false},
		"a sealed value with a name":       {`,"kind":"sealed","name":"a.txt","sealed":` + clipSealedBox, false},
		"a sealed value with a string box": {`,"kind":"sealed","sealed":"x"`, false},
		"a text with a box":                {`,"kind":"text","sealed":` + clipSealedBox, false},
		"an image with a box":              {`,"kind":"image","mimeType":"image/png","sealed":` + clipSealedBox, false},
		"a body with no kind and a box":    {`,"sealed":` + clipSealedBox, false},
		"a text":                           {`,"kind":"text"`, true},
		"an image":                         {`,"kind":"image","mimeType":"image/png"`, true},
		"a file":                           {`,"kind":"file","mimeType":"application/pdf","name":"a.pdf"`, true},
	}
	for name, tc := range bodies {
		for _, env := range []string{
			fmt.Sprintf(`{"schemaVersion":%d,"type":"clipboard-value",%s%s}`, SchemaVersion, prefix, tc.members),
			fmt.Sprintf(`{"schemaVersion":%d,"type":"clipboard-state","value":{%s%s}}`, SchemaVersion, prefix, tc.members),
		} {
			_, err := Decode([]byte(env))
			if tc.ok != (err == nil) {
				t.Errorf("%s in %s: accepted = %v, error %v", name, env[:60], err == nil, err)
			}
		}
	}
}

func TestTheKindsOfAClipboardValue(t *testing.T) {
	want := []string{"text", "image", "file", "sealed"}
	if len(ClipKinds) != len(want) {
		t.Fatalf("ClipKinds = %v", ClipKinds)
	}
	for i, k := range want {
		if ClipKinds[i] != k {
			t.Fatalf("ClipKinds = %v, want %v", ClipKinds, want)
		}
	}
}
