package wire

import (
	"bytes"
	"errors"
	"math/rand"
	"sync"
	"testing"
)

// seeded is a deterministic byte source for a vector run: the same seed, the same ids.
type seeded struct {
	mu sync.Mutex
	r  *rand.Rand
}

func (s *seeded) Read(p []byte) (int, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	for i := range p {
		p[i] = byte(s.r.Intn(256))
	}
	return len(p), nil
}

// TestSetRandomMakesIDsAndTokensReproducible is ticket 161 T-5: the vector generator of ticket 205
// draws every id and token from a seeded source, so regenerating gives the same bytes.
func TestSetRandomMakesIDsAndTokensReproducible(t *testing.T) {
	defer SetRandom(nil)
	draw := func(seed int64) (string, string, string) {
		SetRandom(&seeded{r: rand.New(rand.NewSource(seed))})
		return NewID(), NewID(), NewToken()
	}
	a1, a2, a3 := draw(1)
	b1, b2, b3 := draw(1)
	if a1 != b1 || a2 != b2 || a3 != b3 {
		t.Fatal("the same seed must give the same ids and token")
	}
	if a1 == a2 {
		t.Fatal("two draws of one source must differ")
	}
	c1, _, _ := draw(2)
	if c1 == a1 {
		t.Fatal("another seed must give another id")
	}
	if !ValidID(a1) || !ValidToken(a3) {
		t.Fatalf("forms: %q %q", a1, a3)
	}
	// nil restores the system generator
	SetRandom(nil)
	if NewID() == NewID() {
		t.Fatal("the system generator must not repeat")
	}
}

func TestSetRandomIsSafeUnderConcurrentUse(t *testing.T) {
	defer SetRandom(nil)
	var wg sync.WaitGroup
	for i := 0; i < 8; i++ {
		wg.Add(1)
		go func(i int) {
			defer wg.Done()
			for j := 0; j < 200; j++ {
				if j%50 == 0 {
					SetRandom(&seeded{r: rand.New(rand.NewSource(int64(i)))})
				}
				_ = NewID()
				_ = NewToken()
			}
		}(i)
	}
	wg.Wait()
}

// TestEnrollWithANullReceiverIsAnEnrollWithNone is ticket 161's leftover: "receiver":null says
// nothing - it is absent, never a malformed receiver block.
func TestEnrollWithANullReceiverIsAnEnrollWithNone(t *testing.T) {
	body := `{"schemaVersion":2,"type":"enroll","login":"alice","password":"Passw0rdTest","deviceId":"` + idA +
		`","deviceName":"n","product":"fms-windows","receiver":null}`
	e, err := Decode([]byte(body))
	if err != nil {
		t.Fatalf("a null receiver must decode: %v", err)
	}
	if got := e.(*Enroll).Receiver; len(got) != 0 {
		t.Fatalf("a null receiver is absent, got %q", got)
	}
	// and it encodes as absent, so a record rendered from it carries no `receiver` member
	out, _ := Encode(e)
	if bytes.Contains(out, []byte("receiver")) {
		t.Fatalf("the re-encoded enroll must not carry a receiver: %s", out)
	}
	// a block that is not an object is still refused
	for _, bad := range []string{`"receiver":[]`, `"receiver":"x"`, `"receiver":7`, `"receiver":{"modes":"x"}`} {
		b := `{"schemaVersion":2,"type":"enroll","login":"alice","password":"Passw0rdTest","deviceId":"` + idA +
			`","deviceName":"n","product":"fms-windows",` + bad + `}`
		if _, err := Decode([]byte(b)); !errors.Is(err, ErrMalformed) {
			t.Errorf("%s must stay malformed: %v", bad, err)
		}
	}
}

// the frame the vectors and the probe show to the server for its `version-unsupported` is the legacy
// one, and it is read as exactly that
func TestTheLegacyProbeFrameIsRefusedAsTheSupersededWire(t *testing.T) {
	_, err := NewFrameReader(bytes.NewReader(LegacyProbeFrame()), MaxFrame).Read()
	if !errors.Is(err, ErrVersionRefused) {
		t.Fatalf("%v", err)
	}
	if f := LegacyProbeFrame(); len(f) < 5 || f[0] != 0 {
		t.Fatalf("a frame starts with the zero byte of its length: % x", f[:4])
	}
}
