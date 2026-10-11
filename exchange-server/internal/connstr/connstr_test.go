package connstr

import (
	"strings"
	"testing"
)

const fpA = "SHA256:AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8"

func TestRoundTripAndCanonicalOrder(t *testing.T) {
	c, err := New("Exchange.Example.org:22022", fpA, "Home NAS", "Alice")
	if err != nil {
		t.Fatal(err)
	}
	got, err := c.String()
	if err != nil {
		t.Fatal(err)
	}
	want := "FMSEXCH1:x=exchange.example.org%3A22022&fp=SHA256%3AAAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8&n=Home+NAS&u=alice"
	if got != want {
		t.Fatalf("got %q\nwant %q", got, want)
	}
	back, err := Parse(got)
	if err != nil || back != c {
		t.Fatalf("%v %+v", err, back)
	}
}

func TestTheEscapeSetIsTheWHATWGOneNotGosQueryEscape(t *testing.T) {
	// Go's url.QueryEscape writes * as %2A and leaves ~ raw; the form serializer is the other way round.
	if got := Escape("a*b~c d"); got != "a*b%7Ec+d" {
		t.Fatalf("%q", got)
	}
}

func TestIPv6EndpointAndHost(t *testing.T) {
	c, err := New("[2001:0DB8::7]:22022", "", "", "")
	if err != nil || c.Host != "2001:db8::7" || c.Endpoint() != "[2001:db8::7]:22022" {
		t.Fatalf("%v %+v", err, c)
	}
}

func TestRefusalsNameTheirReason(t *testing.T) {
	for in, want := range map[string]string{
		"hello":                                          RefusedNotAString,
		"FMSEXCH2:x=a.b%3A1":                             RefusedNewerMajor,
		"FMSEXCH1:":                                      RefusedMissingX,
		"FMSEXCH1:x=a.b%3A0":                             RefusedBadX,
		"FMSEXCH1:x=a.b%3A1&x=a.b%3A2":                   RefusedRepeatedKey,
		"FMSEXCH1:x=a.b%3A1&n=":                          RefusedBadName,
		"FMSEXCH1:x=a.b%3A1&fp=SHA256%3Aabc":             RefusedBadFP,
		"FMSEXCH1:x=a.b%3A1&u=ab":                        RefusedBadLogin,
		"FMSEXCH1:x=a.b%3A1&n=%G1":                       RefusedBadEscape,
		"FMSEXCH1:x=a.b%3A1 ":                            "", // the trailing space is trimmed: valid
		"FMSEXCH1:x=a.b%3A1&n=a b":                       RefusedWhitespace,
		"FMSEXCH1:x=a.b%3A1&n=é":                         RefusedNotASCII,
		"FMSEXCH1:x=a.b%3A1&z=%ZZ":                       "", // an unknown key is ignored
		"FMSEXCH1:x=a.b%3A1&":                            RefusedMalformedField,
		"FMSEXCH1:x=a.b%3A1&" + strings.Repeat("z", 600): RefusedTooLong,
	} {
		_, err := Parse(in)
		if got := RefusedOf(err); got != want {
			t.Errorf("%q: reason %q, want %q (%v)", in, got, want, err)
		}
	}
}

func TestAWriterRefusesWhatWouldNotFit(t *testing.T) {
	c, err := New("a.b:1", fpA, strings.Repeat("\U0001F600", 64), strings.Repeat("u", 64))
	if err != nil {
		t.Fatal(err)
	}
	if _, err := c.String(); RefusedOf(err) != RefusedTooLong {
		t.Fatalf("%v", err)
	}
}

func TestFileReading(t *testing.T) {
	line := "FMSEXCH1:x=a.b%3A1"
	for name, b := range map[string][]byte{
		"plain": []byte(line), "lf": []byte(line + "\n"), "crlf": []byte(line + "\r\n"),
		"bom": append([]byte("\xEF\xBB\xBF"), line...),
	} {
		if _, err := ParseFile(b); err != nil {
			t.Errorf("%s: %v", name, err)
		}
	}
	if _, err := ParseFile([]byte(line + "\n" + line)); RefusedOf(err) != RefusedFileLines {
		t.Errorf("two lines: %v", err)
	}
	if _, err := ParseFile(make([]byte, MaxFile+1)); RefusedOf(err) != RefusedFileTooLong {
		t.Errorf("too long: %v", err)
	}
	if _, err := ParseFile([]byte{0xff, 0xfe}); RefusedOf(err) != RefusedBadUTF8 {
		t.Errorf("not utf-8: %v", err)
	}
}
