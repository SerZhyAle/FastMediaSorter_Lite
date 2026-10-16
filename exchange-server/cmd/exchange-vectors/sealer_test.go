package main

import (
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"os"
	"path/filepath"
	"reflect"
	"strings"
	"testing"
)

// Ticket 234 T-6: the sealer of the `sealed-*` transcripts is held to the shared vector file e2e-v1.json,
// which other products made independently (the Android app), and every box of the generated corpus is
// opened with the private keys of that file.

// sharedFile is the shape of e2e-v1.json the tests need.
type sharedFile struct {
	Devices []struct {
		Name       string `json:"name"`
		DeviceID   string `json:"deviceId"`
		PrivateKey string `json:"privateKey"`
		E2EKey     struct {
			V   int    `json:"v"`
			KEM string `json:"kem"`
		} `json:"e2eKey"`
	} `json:"devices"`
	Boxes []struct {
		Name    string          `json:"name"`
		Context string          `json:"context"`
		From    string          `json:"from"`
		To      []string        `json:"to"`
		Payload json.RawMessage `json:"payload"`
		Box     json.RawMessage `json:"box"`
	} `json:"boxes"`
	Failing []struct {
		Name                 string          `json:"name"`
		Box                  string          `json:"box"`
		OpenAs               string          `json:"openAs"`
		Context              string          `json:"context"`
		ReaderTrustsForPhone string          `json:"readerTrustsForPhone"`
		ReplacedBox          json.RawMessage `json:"replacedBox"`
		Expected             string          `json:"expected"`
	} `json:"failing"`
	Clipboard []struct {
		Name         string `json:"name"`
		ContentKey   string `json:"contentKey"`
		ValueKey     string `json:"valueKey"`
		ClearSize    int    `json:"clearSize"`
		ClearSHA256  string `json:"clearSha256"`
		SealedSize   int    `json:"sealedSize"`
		SealedSHA256 string `json:"sealedSha256"`
	} `json:"clipboard"`
}

// loadShared reads the vendored copy of the shared file; a checkout without the viewer's tests skips.
func loadShared(t *testing.T) *sharedFile {
	t.Helper()
	p := filepath.Join("..", "..", "..", "tests", "Lite.Tests", "TestData", "exchange-vectors", "e2e-v1.json")
	b, err := os.ReadFile(p)
	if err != nil {
		t.Skipf("the shared vector file is not in this checkout: %v", err)
	}
	var f sharedFile
	if err := json.Unmarshal(b, &f); err != nil {
		t.Fatal(err)
	}
	return &f
}

func (f *sharedFile) device(t *testing.T, name string) (id string, d e2eDevice) {
	t.Helper()
	for _, x := range f.Devices {
		if x.Name == name {
			return x.DeviceID, e2eDevice{Name: x.Name, Priv: x.PrivateKey, KEM: x.E2EKey.KEM}
		}
	}
	t.Fatalf("the shared file has no device %q", name)
	return "", e2eDevice{}
}

func sameValue(t *testing.T, a, b []byte) bool {
	t.Helper()
	var x, y any
	if err := json.Unmarshal(a, &x); err != nil {
		t.Fatalf("%s: %v", a, err)
	}
	if err := json.Unmarshal(b, &y); err != nil {
		t.Fatalf("%s: %v", b, err)
	}
	return reflect.DeepEqual(x, y)
}

func TestTheSealerKeysAreTheSharedFilesKeys(t *testing.T) {
	f := loadShared(t)
	if len(f.Devices) != len(e2eDevices) {
		t.Fatalf("the shared file has %d devices, the sealer %d", len(f.Devices), len(e2eDevices))
	}
	for _, x := range f.Devices {
		d, ok := e2eDevices[x.Name]
		if !ok {
			t.Fatalf("the sealer has no device %q", x.Name)
		}
		if d.Priv != x.PrivateKey || d.KEM != x.E2EKey.KEM || d.Name != x.Name {
			t.Errorf("device %s differs from the shared file", x.Name)
		}
		pub := base64Raw(d.private().PublicKey().Bytes())
		if pub != d.KEM {
			t.Errorf("device %s: the private key's public point is %s, the declared key %s", x.Name, pub, d.KEM)
		}
	}
}

func base64Raw(b []byte) string { return b64.EncodeToString(b) }

// the sealer opens what the Android app sealed: that holds HPKE (Auth mode, P-256) to two independent implementations
func TestTheSealerOpensTheSharedFilesBoxes(t *testing.T) {
	f := loadShared(t)
	for _, c := range f.Boxes {
		_, from := f.device(t, c.From)
		for _, to := range c.To {
			id, d := f.device(t, to)
			payload, _, err := openBox(c.Box, id, d.private(), from.KEM, c.Context)
			if err != nil {
				t.Errorf("box %s as %s: %v", c.Name, to, err)
				continue
			}
			if !sameValue(t, payload, c.Payload) {
				t.Errorf("box %s as %s: opens to %s, the file says %s", c.Name, to, payload, c.Payload)
			}
		}
	}
}

func TestTheSealerFailsWhereTheSharedFileSaysItFails(t *testing.T) {
	f := loadShared(t)
	want := map[string]string{"NO_ENTRY": "no entry", "UNTRUSTED_SENDER": "untrusted fromKey", "BAD_TAG": "bad tag"}
	for _, c := range f.Failing {
		var box json.RawMessage
		for _, b := range f.Boxes {
			if b.Name == c.Box {
				box = b.Box
			}
		}
		if len(c.ReplacedBox) > 0 {
			box = c.ReplacedBox
		}
		id, d := f.device(t, c.OpenAs)
		_, trusted := f.device(t, "phone")
		if c.ReaderTrustsForPhone != "phone" {
			_, trusted = f.device(t, c.ReaderTrustsForPhone)
		}
		_, _, err := openBox(box, id, d.private(), trusted.KEM, c.Context)
		if err == nil || !strings.Contains(err.Error(), want[c.Expected]) {
			t.Errorf("failing case %s: %v, want %q (%s)", c.Name, err, want[c.Expected], c.Expected)
		}
	}
}

// the clipboard's bytes: the value key, the segments and the nonces are the file's
func TestTheSealerMakesTheSharedFilesClipboardBytes(t *testing.T) {
	f := loadShared(t)
	for _, c := range f.Clipboard {
		ck, err := hex.DecodeString(c.ContentKey)
		if err != nil {
			t.Fatal(err)
		}
		if got := hex.EncodeToString(clipValueKey(ck)); got != c.ValueKey {
			t.Errorf("%s: the value key is %s, the file says %s", c.Name, got, c.ValueKey)
		}
		clear := make([]byte, c.ClearSize)
		for i := range clear {
			clear[i] = byte(i % 251)
		}
		if sum := sha256.Sum256(clear); hex.EncodeToString(sum[:]) != c.ClearSHA256 {
			t.Fatalf("%s: the clear bytes are not 'byte i is i mod 251'", c.Name)
		}
		sealed := sealClipboard(ck, clear)
		sum := sha256.Sum256(sealed)
		if len(sealed) != c.SealedSize || hex.EncodeToString(sum[:]) != c.SealedSHA256 {
			t.Errorf("%s: sealed %d bytes %x, the file says %d bytes %s", c.Name, len(sealed), sum, c.SealedSize, c.SealedSHA256)
		}
		back, err := openClipboard(ck, sealed)
		if err != nil || !bytes.Equal(back, clear) {
			t.Errorf("%s: the sealed bytes do not open to the clear bytes: %v", c.Name, err)
		}
	}
}

// sealedDoc is the `sealed` member of the generated frames file with the steps beside it.
type sealedDoc struct {
	Transcripts []struct {
		Name  string `json:"name"`
		Steps []struct {
			JSON   string `json:"json"`
			Hex    string `json:"hex"`
			Closed bool   `json:"closed"`
		} `json:"steps"`
	} `json:"transcripts"`
	Sealed struct {
		Devices []struct {
			Name   string `json:"name"`
			E2EKey struct {
				V   int    `json:"v"`
				KEM string `json:"kem"`
			} `json:"e2eKey"`
		} `json:"devices"`
		Cases []struct {
			Name       string          `json:"name"`
			Transcript string          `json:"transcript"`
			Context    string          `json:"context"`
			From       sealedParty     `json:"from"`
			To         []sealedParty   `json:"to"`
			Payload    json.RawMessage `json:"payload"`
			Box        json.RawMessage `json:"box"`
			Clipboard  *sealedClip     `json:"clipboard"`
		} `json:"cases"`
	} `json:"sealed"`
}

// every box the corpus holds opens, as every recipient it names, with that device's private key, to the payload it lists,
// and the box is in a frame of the transcript it names byte for byte
func TestEverySealedBoxOfTheCorpusOpensAndIsInItsTranscript(t *testing.T) {
	var doc sealedDoc
	if err := json.Unmarshal(generated(t)[framesFileName], &doc); err != nil {
		t.Fatal(err)
	}
	if len(doc.Sealed.Cases) < 10 {
		t.Fatalf("only %d sealed boxes in the corpus", len(doc.Sealed.Cases))
	}
	kems := map[string]string{}
	for _, d := range doc.Sealed.Devices {
		if e2eDevices[d.Name].KEM != d.E2EKey.KEM {
			t.Errorf("device %s: the corpus lists another key than the sealer holds", d.Name)
		}
		kems[d.Name] = d.E2EKey.KEM
	}
	for _, c := range doc.Sealed.Cases {
		var compact bytes.Buffer
		if err := json.Compact(&compact, c.Box); err != nil {
			t.Fatalf("%s: %v", c.Name, err)
		}
		found := false
		for _, tr := range doc.Transcripts {
			if tr.Name != c.Transcript {
				continue
			}
			for _, st := range tr.Steps {
				if strings.Contains(st.JSON, compact.String()) {
					found = true
				}
			}
		}
		if !found {
			t.Errorf("%s: the box is in no frame of the transcript %s", c.Name, c.Transcript)
		}
		if len(c.To) == 0 {
			t.Errorf("%s: no recipient", c.Name)
		}
		for _, to := range c.To {
			payload, ck, err := openBox(c.Box, to.DeviceID, e2eDevices[to.Device].private(), kems[c.From.Device], c.Context)
			if err != nil {
				t.Errorf("%s as %s: %v", c.Name, to.Device, err)
				continue
			}
			if !sameValue(t, payload, c.Payload) {
				t.Errorf("%s as %s: opens to %s, the corpus says %s", c.Name, to.Device, payload, c.Payload)
			}
			if c.Clipboard != nil {
				checkClipboardCase(t, &doc, c.Transcript, c.Name, ck, c.Clipboard)
			}
		}
		// a stranger's key is not among the recipients: the entry for it is missing
		if _, _, err := openBox(c.Box, "not-a-device-of-the-box", e2eDevices["stranger"].private(), kems[c.From.Device], c.Context); err == nil {
			t.Errorf("%s: opened by a device that is not a recipient", c.Name)
		}
		// the context is part of what is sealed: the same box in another place does not open
		if _, _, err := openBox(c.Box, c.To[0].DeviceID, e2eDevices[c.To[0].Device].private(), kems[c.From.Device], c.Context+"x"); err == nil {
			t.Errorf("%s: opened under another context", c.Name)
		}
	}
}

func checkClipboardCase(t *testing.T, doc *sealedDoc, transcript, name string, ck []byte, c *sealedClip) {
	t.Helper()
	found := 0
	for _, tr := range doc.Transcripts {
		if tr.Name != transcript {
			continue
		}
		for _, st := range tr.Steps {
			if st.JSON != "" || st.Hex == "" || st.Closed {
				continue
			}
			raw, err := hex.DecodeString(st.Hex)
			if err != nil {
				t.Fatal(err)
			}
			sum := sha256.Sum256(raw)
			if hex.EncodeToString(sum[:]) != c.SealedSHA256 {
				continue
			}
			found++
			clear, err := openClipboard(ck, raw)
			if err != nil || string(clear) != c.Clear {
				t.Errorf("%s: the raw sealed bytes open to %q (%v), the corpus says %q", name, clear, err, c.Clear)
			}
			if len(raw) != c.SealedSize {
				t.Errorf("%s: %d sealed bytes, the corpus says %d", name, len(raw), c.SealedSize)
			}
		}
	}
	if found == 0 {
		t.Errorf("%s: no raw step of %s holds the sealed bytes", name, transcript)
	}
}

// the corpus has a box for each place section 27 puts one: the sealed folder, the sealed broadcast, the grant (a folder and a
// broadcast) and the clipboard
func TestTheSealedCorpusHasABoxForEveryPlace(t *testing.T) {
	var doc sealedDoc
	if err := json.Unmarshal(generated(t)[framesFileName], &doc); err != nil {
		t.Fatal(err)
	}
	places := map[string]bool{}
	for _, c := range doc.Sealed.Cases {
		switch {
		case strings.HasPrefix(c.Context, "access:"):
			places["access"] = true
		case strings.HasPrefix(c.Context, "broadcast:"):
			places["broadcast"] = true
		case strings.HasPrefix(c.Context, "grant:"):
			places["grant"] = true
			if bytes.Contains(c.Payload, []byte(`"credentials"`)) {
				places["grant-of-a-broadcast"] = true
			}
		case c.Context == "clipboard":
			places["clipboard"] = true
		}
	}
	for _, want := range []string{"access", "broadcast", "grant", "grant-of-a-broadcast", "clipboard"} {
		if !places[want] {
			t.Errorf("no sealed box for %s", want)
		}
	}
}
