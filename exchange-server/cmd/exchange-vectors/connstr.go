package main

import (
	"crypto/sha256"
	"encoding/base64"
	"strings"

	"fms/exchange-server/internal/connstr"
)

// connection-string-v1.json: the exchange connection string of ticket 187 / the proposal
// PROPOSAL-2026-10-10-exchange-connection-string.md, as the REAL codec (internal/connstr) writes and
// reads it. Every case carries the verdict the generator EXPECTED, and the generator stops when the code
// disagrees: the file proves what the code does AND that it is what the rules say.

const connFileName = "connection-string-v1.json"

type connFile struct {
	Contract        string         `json:"contract"`
	ContractVersion string         `json:"contractVersion"`
	Proposal        string         `json:"proposal"`
	Sections        string         `json:"sections"`
	Producer        string         `json:"producer"`
	Notes           []string       `json:"notes"`
	Limits          map[string]int `json:"limits"`
	Cases           []connCase     `json:"cases"`
}

// connCase is one input and what a conforming reader does with it. A `text` case is read as the text of
// a string (clipboard, chat, QR); a `file` case is the byte content of a .fmsexch file. Input is given as
// `input` when it is printable ASCII and as `inputHex` (the exact bytes) otherwise.
type connCase struct {
	Name      string      `json:"name"`
	Kind      string      `json:"kind"` // text | file
	Note      string      `json:"note,omitempty"`
	Input     string      `json:"input,omitempty"`
	InputHex  string      `json:"inputHex,omitempty"`
	Verdict   string      `json:"verdict"` // valid | tolerated | refused
	Reason    string      `json:"reason,omitempty"`
	Looks     string      `json:"looks,omitempty"`
	Canonical string      `json:"canonical,omitempty"` // what a writer produces for it (equals input for valid)
	Fields    *connFields `json:"fields,omitempty"`
}

type connFields struct {
	X    string `json:"x"` // host:port, an IPv6 literal in brackets
	Host string `json:"host"`
	Port int    `json:"port"`
	FP   string `json:"fp,omitempty"`
	N    string `json:"n,omitempty"`
	U    string `json:"u,omitempty"`
	I    string `json:"i,omitempty"` // the one-time invitation of an invitation string (19 item AR)
}

func connNotes() []string {
	return []string{
		"The cases were run against the codec of the exchange-server module (internal/connstr) and each one was checked against the verdict this file states before it was written: a disagreement stops the generator.",
		"verdict `valid`: the input is the canonical text; a writer given `fields` produces exactly `input`. `tolerated`: a reader accepts it and a writer given `fields` produces `canonical`, which differs from the input. `refused`: a reader refuses the WHOLE string with `reason`; nothing is partially read.",
		"Canonical writing: prefix `FMSEXCH1:`, then `x`, `fp`, `n`, `u`, `i` in that order, `&`-separated; each value is escaped with the WHATWG application/x-www-form-urlencoded byte serializer over UTF-8 (A-Z a-z 0-9 * - . _ raw, a space as +, every other byte %XX with upper-case hex). Go's url.QueryEscape is NOT this serializer (it differs on * and ~): the generator uses its own.",
		"The reader drops one leading U+FEFF and the white space around the text, then needs the exact prefix `FMSEXCH1:` (case-sensitive). 512 is counted on that trimmed text; 64 on the decoded name in Unicode scalar values; control characters are category Cc.",
		"`x` is a DNS name (labels of letters, digits and hyphen, 1-63 characters, no trailing dot; written lower case), an IPv4 dotted quad or an IPv6 literal in brackets without a zone (written in RFC 5952 form), then `:` and a port of 1 to 5 digits, 1-65535 (a leading zero is tolerated and dropped on writing). A name of digits and dots is read as IPv4 or refused.",
		"`fp` is `SHA256:` and exactly 43 characters of the standard base64 alphabet without padding, the last one carrying no stray bits. The URL-safe alphabet, a padded value, a lower-case prefix and a raw + (which form decoding reads as a space) are refused.",
		"`u` is the login form of 5.1 (3-64 of a-z 0-9 . _ -); an upper-case input is tolerated and written lower case. An unknown key is ignored whatever its value; a known key that repeats or breaks its rule, a field with no = or no key, and an empty field refuse the string.",
		"`i` (an invitation string, 19 item AR) is a one-time invitation code: exactly 22 characters of the URL-safe base64 alphabet that decode to 16 bytes with no stray bits. It is a secret to the person who holds the string - the one that the string exists to carry - and reads like every other known key; the standard alphabet, a padded value, a short or a long one, and one that carries stray bits are refused with `bad-i`.",
		"A file is UTF-8, at most 4096 bytes, one non-empty line; one leading byte-order mark and the line breaks around the line are tolerated. A writer writes no byte-order mark and no trailing line break.",
		"The reasons are the vocabulary of this file (not-a-string, newer-major, too-long, whitespace, not-ascii, bad-escape, bad-utf8, malformed-field, repeated-key, missing-x, bad-x, bad-fp, bad-n, bad-u, file-too-long, file-lines); `looks` names what a not-a-string text resembles (share-code, url).",
		"The fingerprints are fixed test values (SHA-256 of fixed words); the second one contains both + and / in its base64.",
	}
}

// fpOf builds a fingerprint of the product's text form from 32 bytes.
func fpOf(b []byte) string { return "SHA256:" + base64.RawStdEncoding.EncodeToString(b) }

func connVectors() connFile {
	sumA := sha256.Sum256([]byte("fms exchange vectors: a leaf"))
	fpA := fpOf(sumA[:])
	sumB := sha256.Sum256([]byte("fms exchange vectors: a leaf with plus and slash"))
	bytesB := append([]byte{0xfb, 0xef, 0xbe, 0xff, 0xff, 0xff}, sumB[:26]...)
	fpB := fpOf(bytesB)
	if !strings.Contains(fpB, "+") || !strings.Contains(fpB, "/") {
		failf("the second fingerprint must hold + and /")
	}
	const (
		v4   = "192.0.2.10:22022"
		dns  = "exchange.example.org:22022"
		v6   = "[2001:db8::7]:22022"
		pre  = connstr.Prefix
		xv4  = pre + "x=192.0.2.10%3A22022"
		xdns = pre + "x=exchange.example.org%3A22022"
	)
	var cases []connCase

	input := func(c *connCase, raw []byte) {
		printable := true
		for _, b := range raw {
			if b < 0x20 || b > 0x7e {
				printable = false
			}
		}
		if printable {
			c.Input = string(raw)
		} else {
			c.InputHex = hexOf(raw)
		}
	}
	read := func(kind string, raw []byte) (connstr.Conn, error) {
		if kind == "file" {
			return connstr.ParseFile(raw)
		}
		return connstr.Parse(string(raw))
	}
	fields := func(c connstr.Conn) *connFields {
		return &connFields{X: c.Endpoint(), Host: c.Host, Port: c.Port, FP: c.Fingerprint, N: c.Name, U: c.Login, I: c.Invitation}
	}

	// valid: built from the members, so the vector shows what a writer writes.
	validInv := func(name, note, endpoint, fp, n, u, inv string) string {
		c, err := connstr.NewWithInvitation(endpoint, fp, n, u, inv)
		if err != nil {
			failf("%s: %v", name, err)
		}
		text, err := c.String()
		if err != nil {
			failf("%s: %v", name, err)
		}
		back, err := connstr.Parse(text)
		if err != nil || back != c {
			failf("%s: the canonical text does not read back to the same members (%v)", name, err)
		}
		cs := connCase{Name: name, Kind: "text", Note: note, Verdict: "valid", Canonical: text, Fields: fields(c)}
		input(&cs, []byte(text))
		cases = append(cases, cs)
		return text
	}
	valid := func(name, note, endpoint, fp, n, u string) string {
		return validInv(name, note, endpoint, fp, n, u, "")
	}
	// tolerated: a reader accepts, a writer writes the canonical text.
	tolerated := func(name, note, kind string, raw []byte) {
		c, err := read(kind, raw)
		if err != nil {
			failf("%s: expected to be tolerated, got %v", name, err)
		}
		text, err := c.String()
		if err != nil {
			failf("%s: %v", name, err)
		}
		if kind == "text" && text == string(raw) {
			failf("%s: it is canonical already, it is not a tolerated input", name)
		}
		cs := connCase{Name: name, Kind: kind, Note: note, Verdict: "tolerated", Canonical: text, Fields: fields(c)}
		input(&cs, raw)
		cases = append(cases, cs)
	}
	refused := func(name, note, kind string, raw []byte, reason, looks string) {
		_, err := read(kind, raw)
		if err == nil {
			failf("%s: expected to be refused (%s), it was read", name, reason)
		}
		e, ok := err.(*connstr.Error)
		if !ok || e.Reason != reason || e.Looks != looks {
			failf("%s: expected %s/%s, got %v", name, reason, looks, err)
		}
		cs := connCase{Name: name, Kind: kind, Note: note, Verdict: "refused", Reason: reason, Looks: looks}
		input(&cs, raw)
		cases = append(cases, cs)
	}
	T := func(name, note, s, reason string) { refused(name, note, "text", []byte(s), reason, "") }
	esc := connstr.Escape

	// ---- valid -------------------------------------------------------------------------------
	valid("ipv4", "the smallest string", v4, "", "", "")
	valid("hostname", "a DNS name, written lower case", dns, "", "", "")
	valid("ipv6-literal", "an IPv6 literal in brackets; colons and brackets are escaped", v6, "", "", "")
	valid("port-1", "the lowest port", "example.org:1", "", "", "")
	valid("port-65535", "the highest port", "example.org:65535", "", "", "")
	valid("with-fp", "a self-signed deployment: the leaf's pin travels with the address", v4, fpA, "", "")
	valid("with-fp-plus-slash", "an fp whose base64 holds + and /: both are percent-encoded, a raw + would read as a space", dns, fpB, "", "")
	valid("with-name", "a display name; the space is written +", dns, "", "Home NAS", "")
	valid("name-escapes", "a name with ~ (written %7E), * (raw), a space, a Latin and a Cyrillic letter", dns, "", "Home~NAS* é Ж", "")
	valid("with-login", "the login hint, upper case never written", dns, "", "", "alice")
	valid("all-members", "x, fp, n and u in the one order", dns, fpB, "Office", "bob.smith")
	valid("ipv6-with-fp-name-login", "an IPv6 endpoint with every member", v6, fpA, "Studio", "carol_2")
	// invitation strings: an invitation code is 16 bytes of base64url, 22 characters, none of them padded
	sumI := sha256.Sum256([]byte("fms exchange vectors: an invitation"))
	invA := base64.RawURLEncoding.EncodeToString(sumI[:16])
	rawB := []byte{0xfb, 0xff, 0xbf, 0xfe, 0xff, 0xef, 0xfb, 0xff, 0xbf, 0xfe, 0xff, 0xef, 0xfb, 0xff, 0xbf, 0xfe}
	invB := base64.RawURLEncoding.EncodeToString(rawB)
	if !strings.Contains(invB, "-") || !strings.Contains(invB, "_") {
		failf("the second invitation must hold - and _")
	}
	validInv("with-invitation", "an invitation string: the address and the one-time code (22 characters of base64url)", dns, "", "", "", invA)
	validInv("invitation-url-safe-alphabet", "an invitation code holding - and _, written raw: both belong to the form-urlencoded unreserved set", dns, "", "", "", invB)
	validInv("invitation-all-members", "x, fp, n, u and i in the one order; the login is the one the operator expects the person to take", dns, fpA, "Office", "carol", invA)
	validInv("invitation-ipv6", "an IPv6 endpoint with an invitation", v6, fpB, "", "", invB)
	valid("name-64-ascii", "a name of exactly 64 characters", dns, "", strings.Repeat("n", 64), "")
	valid("name-64-cyrillic", "64 scalar values of two bytes each: 64 characters, 384 once encoded", dns, "", strings.Repeat("ж", 64), "")
	valid("name-4-byte-scalars", "a name of 4-byte scalar values (the count is scalar values, not bytes or UTF-16 units)", dns, "", strings.Repeat("\U0001F600", 12), "")
	// exactly 512 characters: search a name (64 scalars: j two-byte letters and 64-j ASCII) and a login length
	{
		found := false
	search:
		for j := 0; j <= 64; j++ {
			for l := 3; l <= 64; l++ {
				n := strings.Repeat("é", j) + strings.Repeat("a", 64-j)
				c, err := connstr.New(dns, fpB, n, strings.Repeat("u", l))
				if err != nil {
					continue
				}
				text, err := c.String()
				if err == nil && len(text) == connstr.MaxText {
					valid("length-512", "exactly 512 characters: the longest string a writer may write", dns, fpB, n, strings.Repeat("u", l))
					found = true
					break search
				}
			}
		}
		if !found {
			failf("no combination reaches exactly 512 characters")
		}
	}

	// ---- tolerated ---------------------------------------------------------------------------
	T2 := func(name, note, s string) { tolerated(name, note, "text", []byte(s)) }
	T2("white-space-around", "a clipboard or a chat adds white space and a line break", "  "+xv4+"\r\n")
	T2("leading-bom", "one leading byte-order mark is dropped before trimming", "\xef\xbb\xbf"+xv4)
	T2("lower-case-hex", "percent hex of either case is read", pre+"x=192.0.2.10%3a22022")
	T2("raw-colon", "an unescaped colon in x is read; a writer escapes it", pre+"x=192.0.2.10:22022")
	T2("space-as-percent-20", "%20 for a space is read; a writer writes +", xdns+"&n=Home%20NAS")
	T2("unknown-trailing-key", "a later MINOR adds keys at the end: ignored", xv4+"&z=1")
	T2("unknown-key-any-value", "an unknown key is ignored whatever its value, even one that would be a bad escape", xv4+"&zz=%ZZ%&q=")
	T2("later-key-order", "keys in another order are read; a writer writes the one order", pre+"n=Office&u=alice&fp="+esc(fpA)+"&x="+esc(dns))
	T2("port-leading-zero", "a leading zero is tolerated and dropped", pre+"x=example.org%3A00080")
	T2("host-upper-case", "the host is written lower case", pre+"x=Exchange.Example.ORG%3A22022")
	T2("login-upper-case", "5.1 compares logins case-insensitively: written lower case", xdns+"&u=Alice")
	T2("ipv6-expanded", "an IPv6 literal is written in its RFC 5952 form", pre+"x="+esc("[2001:0DB8:0:0:0:0:0:7]:22022"))
	T2("tilde-raw", "a raw ~ is read; the WHATWG serializer writes %7E", xdns+"&n=Home~NAS")
	T2("invitation-first", "the invitation before x: read, written last", pre+"i="+invA+"&x="+esc(dns))
	T2("invitation-escaped", "%2D and %5F for - and _ are read; a writer writes them raw", xdns+"&i="+strings.NewReplacer("-", "%2D", "_", "%5F").Replace(invB))
	T2("star-escaped", "%2A for * is read; the WHATWG serializer writes * raw", xdns+"&n=Home%2ANAS")

	// ---- refused: the text is not this ---------------------------------------------------------
	T("empty-text", "nothing", "", connstr.RefusedNotAString)
	T("plain-text", "any other text", "hello world", connstr.RefusedNotAString)
	T("lower-case-prefix", "the prefix is case-sensitive", "fmsexch1:x=192.0.2.10%3A22022", connstr.RefusedNotAString)
	refused("share-code", "a share code of another family is not 'update the program'", "text", []byte("FMSSFTP2:AbCdEf"), connstr.RefusedNotAString, connstr.LooksShareCode)
	refused("url", "a bare URL", "text", []byte("https://exchange.example.org:22022/"), connstr.RefusedNotAString, connstr.LooksURL)
	T("newer-major", "FMSEXCH and another digit: tell the user to update, never a partial read", "FMSEXCH2:x=192.0.2.10%3A22022&k=v", connstr.RefusedNewerMajor)
	T("major-zero", "a digit below 1 is not 'update': it is not a string of this contract", "FMSEXCH0:x=192.0.2.10%3A22022", connstr.RefusedNotAString)

	// ---- refused: structure ----------------------------------------------------------------------
	T("no-fields", "the prefix alone", pre, connstr.RefusedMissingX)
	T("only-fp", "no x", pre+"fp="+esc(fpA), connstr.RefusedMissingX)
	T("only-unknown", "no x", pre+"z=1", connstr.RefusedMissingX)
	T("whitespace-inside", "white space inside is refused", xv4+" &n=a", connstr.RefusedWhitespace)
	T("tab-inside", "a tab inside", pre+"x=192.0.2.10%3A22022&n=a\tb", connstr.RefusedWhitespace)
	T("raw-non-ascii", "a raw non-ASCII character: values are percent-encoded", xdns+"&n=café", connstr.RefusedNotASCII)
	T("raw-control", "a raw control character", xdns+"&n=a\x01b", connstr.RefusedNotASCII)
	T("empty-field", "an empty field between two ampersands", xdns+"&&n=a", connstr.RefusedMalformedField)
	T("trailing-ampersand", "a trailing &", xdns+"&", connstr.RefusedMalformedField)
	T("leading-ampersand", "a leading &", pre+"&x=192.0.2.10%3A22022", connstr.RefusedMalformedField)
	T("field-without-equals", "a field with no =", xdns+"&foo", connstr.RefusedMalformedField)
	T("field-without-key", "a field with no key", xdns+"&=v", connstr.RefusedMalformedField)
	T("repeated-x", "a known key twice", xv4+"&x=192.0.2.11%3A22022", connstr.RefusedRepeatedKey)
	T("repeated-name", "a known key twice", xdns+"&n=a&n=b", connstr.RefusedRepeatedKey)
	T("bad-escape", "a % not followed by two hex digits", xdns+"&n=%G1", connstr.RefusedBadEscape)
	T("escape-at-end", "a % at the very end", xdns+"&n=a%", connstr.RefusedBadEscape)
	T("escape-short", "a % with one digit", xdns+"&n=a%4", connstr.RefusedBadEscape)
	T("decoded-not-utf8", "a decoded value that is not UTF-8", xdns+"&n=%FF%FE", connstr.RefusedBadUTF8)
	// over 512: a valid string padded with an unknown key
	T("over-512", "513 characters, valid in every other way", xdns+"&z="+strings.Repeat("a", connstr.MaxText+1-len(xdns+"&z=")), connstr.RefusedTooLong)

	// ---- refused: x --------------------------------------------------------------------------------
	T("x-empty", "x with no value", pre+"x=", connstr.RefusedBadX)
	T("x-no-port", "no port", pre+"x=example.org", connstr.RefusedBadX)
	T("x-port-0", "port 0", pre+"x=example.org%3A0", connstr.RefusedBadX)
	T("x-port-65536", "port above 65535", pre+"x=example.org%3A65536", connstr.RefusedBadX)
	T("x-port-6-digits", "a port of six digits", pre+"x=example.org%3A022022", connstr.RefusedBadX)
	T("x-port-letters", "a port that is not digits", pre+"x=example.org%3Aab", connstr.RefusedBadX)
	T("x-port-sign", "a signed port", pre+"x=example.org%3A%2B80", connstr.RefusedBadX)
	T("x-empty-host", "no host", pre+"x=%3A22022", connstr.RefusedBadX)
	T("x-host-with-path", "a host with a path", pre+"x="+esc("example.org/path:22022"), connstr.RefusedBadX)
	T("x-host-with-user", "a host with user information", pre+"x="+esc("bob@example.org:22022"), connstr.RefusedBadX)
	T("x-trailing-dot", "a trailing dot", pre+"x="+esc("example.org.:22022"), connstr.RefusedBadX)
	T("x-label-64", "a label of 64 characters", pre+"x="+esc(strings.Repeat("a", 64)+".example:22022"), connstr.RefusedBadX)
	T("x-label-hyphen", "a label that begins with a hyphen", pre+"x="+esc("-a.example:22022"), connstr.RefusedBadX)
	T("x-underscore", "an underscore is not in a host", pre+"x="+esc("a_b.example:22022"), connstr.RefusedBadX)
	T("x-digits-only", "a name of digits is IPv4 or nothing", pre+"x="+esc("12345:22022"), connstr.RefusedBadX)
	T("x-ipv4-range", "an IPv4 octet above 255", pre+"x="+esc("999.1.1.1:22022"), connstr.RefusedBadX)
	T("x-ipv4-three-parts", "digits and dots that are not a dotted quad", pre+"x="+esc("1.2.3:22022"), connstr.RefusedBadX)
	T("x-ipv6-no-brackets", "an IPv6 literal needs brackets", pre+"x="+esc("2001:db8::7:22022"), connstr.RefusedBadX)
	T("x-ipv6-unclosed", "an unclosed bracket", pre+"x="+esc("[2001:db8::7:22022"), connstr.RefusedBadX)
	T("x-ipv6-zone", "an IPv6 literal with a zone id", pre+"x="+esc("[fe80::1%eth0]:22022"), connstr.RefusedBadX)
	T("x-ipv4-in-brackets", "an IPv4 address in brackets", pre+"x="+esc("[192.0.2.10]:22022"), connstr.RefusedBadX)
	T("x-idn-host", "an internationalised name only in its xn-- form: percent-encoded UTF-8 is refused", pre+"x="+esc("bücher.example:22022"), connstr.RefusedBadX)

	// ---- refused: fp, n, u -------------------------------------------------------------------------
	urlSafe := strings.NewReplacer("+", "-", "/", "_").Replace(fpB)
	stray := fpA[:len(fpA)-1] + string(strayChar(fpA[len(fpA)-1]))
	T("fp-url-safe-alphabet", "the URL-safe alphabet", xdns+"&fp="+esc(urlSafe), connstr.RefusedBadFP)
	T("fp-padded", "a padded value", xdns+"&fp="+esc(fpA+"="), connstr.RefusedBadFP)
	T("fp-lower-prefix", "a lower-case prefix", xdns+"&fp="+esc("sha256:"+fpA[7:]), connstr.RefusedBadFP)
	T("fp-short", "fewer than 43 characters", xdns+"&fp="+esc(fpA[:len(fpA)-1]), connstr.RefusedBadFP)
	T("fp-raw-plus", "a raw + decodes to a space", xdns+"&fp="+strings.ReplaceAll(esc(fpB), "%2B", "+"), connstr.RefusedBadFP)
	T("fp-stray-bits", "the last character carries bits no 32-byte value has", xdns+"&fp="+esc(stray), connstr.RefusedBadFP)
	T("fp-empty", "fp with no value", xdns+"&fp=", connstr.RefusedBadFP)
	T("name-empty", "n with no value", xdns+"&n=", connstr.RefusedBadName)
	T("name-65", "65 characters", xdns+"&n="+strings.Repeat("n", 65), connstr.RefusedBadName)
	T("name-control", "a control character (category Cc) after decoding", xdns+"&n=a%0Ab", connstr.RefusedBadName)
	T("login-2", "two characters", xdns+"&u=ab", connstr.RefusedBadLogin)
	T("login-65", "65 characters", xdns+"&u="+strings.Repeat("u", 65), connstr.RefusedBadLogin)
	T("login-space", "a space", xdns+"&u=al%20ce", connstr.RefusedBadLogin)
	T("login-at", "an @", xdns+"&u="+esc("alice@example.org"), connstr.RefusedBadLogin)
	T("login-empty", "u with no value", xdns+"&u=", connstr.RefusedBadLogin)

	// ---- refused: i ----------------------------------------------------------------------------------
	const urlAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"
	li := strings.IndexByte(urlAlphabet, invA[21])
	if li < 0 {
		failf("not a base64url character: %q", invA[21])
	}
	invStray := invA[:21] + string(urlAlphabet[li|1])
	if invStray == invA {
		invStray = invA[:21] + string(urlAlphabet[li|2])
	}
	T("i-empty", "i with no value", xdns+"&i=", connstr.RefusedBadInv)
	T("i-21", "one character short", xdns+"&i="+invA[:21], connstr.RefusedBadInv)
	T("i-23", "one character long", xdns+"&i="+invA+"A", connstr.RefusedBadInv)
	T("i-standard-alphabet", "the standard alphabet (+ and /) is not the one of the code", xdns+"&i="+esc(base64.RawStdEncoding.EncodeToString(rawB)), connstr.RefusedBadInv)
	T("i-padded", "a padded value of 24 characters", xdns+"&i="+esc(base64.URLEncoding.EncodeToString(sumI[:16])), connstr.RefusedBadInv)
	T("i-stray-bits", "the last character carries bits no 16-byte value has", xdns+"&i="+invStray, connstr.RefusedBadInv)
	T("i-repeated", "a known key twice", xdns+"&i="+invA+"&i="+invB, connstr.RefusedRepeatedKey)

	// ---- files -------------------------------------------------------------------------------------
	line := xdns
	file := func(name, note string, raw []byte, verdict, reason string) {
		if verdict == "refused" {
			refused(name, note, "file", raw, reason, "")
		} else {
			tolerated(name, note, "file", raw)
		}
	}
	// a canonical file is the text with no byte-order mark and no trailing break: "valid", written as such
	{
		c, err := connstr.ParseFile([]byte(line))
		if err != nil {
			failf("file-canonical: %v", err)
		}
		cs := connCase{Name: "file-canonical", Kind: "file", Note: "a .fmsexch file as a writer writes it: the text and nothing else", Verdict: "valid", Canonical: line, Fields: fields(c)}
		input(&cs, []byte(line))
		cases = append(cases, cs)
	}
	file("file-lf", "one trailing line break", []byte(line+"\n"), "tolerated", "")
	file("file-crlf", "a Windows line break", []byte(line+"\r\n"), "tolerated", "")
	file("file-bom", "a text editor's byte-order mark", append([]byte("\xEF\xBB\xBF"), []byte(line+"\r\n")...), "tolerated", "")
	file("file-blank-lines-around", "blank lines around the one line", []byte("\r\n\r\n"+line+"\r\n\r\n"), "tolerated", "")
	file("file-4096", "exactly 4096 bytes: a valid line then white space", append([]byte(line), []byte(strings.Repeat(" ", connstr.MaxFile-len(line)))...), "tolerated", "")
	file("file-4097", "one byte over", append([]byte(line), []byte(strings.Repeat(" ", connstr.MaxFile+1-len(line)))...), "refused", connstr.RefusedFileTooLong)
	file("file-two-lines", "a second non-empty line", []byte(line+"\r\n"+line+"\r\n"), "refused", connstr.RefusedFileLines)
	file("file-invalid-utf8", "bytes that are not UTF-8", []byte("FMSEXCH1:x=192.0.2.10%3A22022&n=\xff\xfe"), "refused", connstr.RefusedBadUTF8)
	file("file-empty", "no bytes", []byte{}, "refused", connstr.RefusedNotAString)
	refused("file-other-text", "another family's text in a .fmsexch file", "file", []byte("FMSSFTP2:AbCdEf\r\n"), connstr.RefusedNotAString, connstr.LooksShareCode)

	return connFile{
		Contract: contractID, ContractVersion: contractVersion,
		Proposal: "PROPOSAL-2026-10-10-exchange-connection-string.md (read with the grammar and canonical-bytes points of the proposals filed by StreamsPlayer and FastMediaSorter Android the same day)",
		Sections: "5.5 (proposed)", Producer: producerName,
		Notes:  connNotes(),
		Limits: map[string]int{"maxText": connstr.MaxText, "maxName": connstr.MaxName, "maxFile": connstr.MaxFile},
		Cases:  cases,
	}
}

// strayChar returns a base64 character whose low two bits are not zero, so a 43-character value that
// ends in it is not a canonical encoding of 32 bytes.
func strayChar(last byte) byte {
	const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"
	i := strings.IndexByte(alphabet, last)
	if i < 0 {
		failf("not a base64 character: %q", last)
	}
	return alphabet[i|1]
}
