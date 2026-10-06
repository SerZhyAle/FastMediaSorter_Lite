package deploy

import (
	"errors"
	"path/filepath"
	"regexp"
	"strings"
	"testing"
)

type fakeProbe struct {
	running    bool
	runningErr error
	held       bool
	heldErr    error
	askedPort  int
}

func (f *fakeProbe) Running() (bool, error) { return f.running, f.runningErr }
func (f *fakeProbe) HoldsPort(p int) (bool, error) {
	f.askedPort = p
	return f.held, f.heldErr
}

func TestCheckBesideServiceRefusesWhileTheServiceRuns(t *testing.T) {
	reason, err := CheckBesideService(&fakeProbe{running: true}, ":22022")
	if err != nil || !strings.Contains(reason, ServiceName) || !strings.Contains(reason, "service wins") {
		t.Fatalf("running service: %q %v", reason, err)
	}
}

func TestCheckBesideServiceRefusesWhileTheServiceHoldsThePort(t *testing.T) {
	p := &fakeProbe{held: true}
	reason, err := CheckBesideService(p, "127.0.0.1:22022")
	if err != nil || !strings.Contains(reason, "22022") || !strings.Contains(reason, "service wins") {
		t.Fatalf("held port: %q %v", reason, err)
	}
	if p.askedPort != 22022 {
		t.Fatalf("asked about port %d", p.askedPort)
	}
}

func TestCheckBesideServiceLetsAConsoleServerStartWhenTheServiceIsAbsent(t *testing.T) {
	if reason, err := CheckBesideService(&fakeProbe{}, ":22022"); reason != "" || err != nil {
		t.Fatalf("%q %v", reason, err)
	}
}

func TestCheckBesideServiceNeverRefusesOnAProbeFailure(t *testing.T) {
	boom := errors.New("access denied")
	reason, err := CheckBesideService(&fakeProbe{runningErr: boom, heldErr: boom}, ":22022")
	if reason != "" || !errors.Is(err, boom) {
		t.Fatalf("a probe that cannot answer must not refuse, only report: %q %v", reason, err)
	}
	// a probe that failed on one question and answered "yes" on the other still refuses
	reason, err = CheckBesideService(&fakeProbe{runningErr: boom, held: true}, ":22022")
	if reason == "" || !errors.Is(err, boom) {
		t.Fatalf("%q %v", reason, err)
	}
}

func TestCheckBesideServiceIgnoresAListenItCannotParse(t *testing.T) {
	p := &fakeProbe{held: true}
	if reason, _ := CheckBesideService(p, "not-an-address"); reason != "" {
		t.Fatalf("an unparsable listen has no port to hold: %q", reason)
	}
}

func TestStopEventNameShape(t *testing.T) {
	re := regexp.MustCompile(`^Local\\FastMediaSorterExchange-Stop-[0-9a-f]{8}$`)
	a := StopEventName(filepath.Join("some", "dir", "exchange.json"))
	if !re.MatchString(a) {
		t.Fatalf("%q", a)
	}
	if a != StopEventName(filepath.Join("some", "dir", "exchange.json")) {
		t.Fatal("the name must be stable")
	}
	if a == StopEventName(filepath.Join("some", "dir", "other.json")) {
		t.Fatal("two configs must not share a stop event")
	}
	// case-folded: the same file spelled in another case is the same config
	if StopEventName(filepath.Join("Some", "DIR", "Exchange.JSON")) != a {
		t.Fatal("the name is over the lower-cased path")
	}
}
