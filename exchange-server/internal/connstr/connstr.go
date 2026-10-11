// Package connstr is the exchange connection string of DEVICE-EXCHANGE (proposal section 5.5,
// PROPOSAL-2026-10-10-exchange-connection-string.md; ticket 187 T-1): the text an operator hands out to
// name one server and the pin of its certificate. It is NOT a credential and carries none.
//
//	FMSEXCH1:x=host%3Aport&fp=SHA256%3A<43 base64>&n=<name>&u=<login>
//
// The package is pure: no network, no file, no clock. Its readings of the points the proposal left open
// follow PROPOSAL-2026-10-10-connection-string-grammar-and-handling.md (StreamsPlayer) and
// PROPOSAL-2026-10-10-connection-string-entries-and-leave.md (FastMediaSorter Android, the contract
// owner) - neither is folded yet, and where the two disagree (the escape set) the owner's wins:
//
//   - A WRITER is held to one byte sequence: the WHATWG application/x-www-form-urlencoded serializer over
//     UTF-8 (A-Z a-z 0-9 * - . _ raw, a space as +, every other byte %XX upper case) and the field order
//     x, fp, n, u. Go's url.QueryEscape is NOT that serializer (it differs on * and ~), hence escape below.
//   - A READER accepts what any conforming or hand-made encoder produces: either case of hex, + for a
//     space, ~ raw, a different key order, an upper-case host or login, a leading zero in the port. It
//     refuses a raw non-ASCII character, a % not followed by two hex digits, a decoded value that is not
//     UTF-8, a field with no = or an empty field, a known key that repeats or breaks its rule, and
//     white space inside. An UNKNOWN key is ignored whatever its value.
//   - 512 is counted on the trimmed text (ASCII once encoded, so characters and bytes are one number);
//     64 on the decoded name in Unicode scalar values; "control characters" is category Cc.
package connstr

import (
	"encoding/base64"
	"fmt"
	"net/netip"
	"strconv"
	"strings"
	"unicode"
	"unicode/utf8"
)

// The numbers of the proposal.
const (
	Prefix    = "FMSEXCH1:"
	MaxText   = 512
	MaxName   = 64
	MaxFile   = 4096
	FileExt   = ".fmsexch"
	minLogin  = 3
	maxLogin  = 64
	maxHost   = 253
	maxLabel  = 63
	fpPrefix  = "SHA256:"
	fpB64Len  = 43             // 32 bytes of unpadded standard base64
	bom       = "\xef\xbb\xbf" // U+FEFF as UTF-8
	keyX      = "x"
	keyFP     = "fp"
	keyName   = "n"
	keyLogin  = "u"
	shareCode = "FMSSFTP"
)

// Reasons a reader refuses with. They are the vocabulary of the conformance vectors: a replay compares
// them by name, and a UI maps each to its own sentence.
const (
	RefusedNotAString     = "not-a-string"    // not a connection string at all (another family's text, a URL, anything)
	RefusedNewerMajor     = "newer-major"     // FMSEXCH followed by a digit other than 1: tell the user to update
	RefusedTooLong        = "too-long"        // over 512 characters (or, on write, over 512 once encoded)
	RefusedWhitespace     = "whitespace"      // white space inside the text
	RefusedNotASCII       = "not-ascii"       // a raw non-ASCII or control character in the text
	RefusedBadEscape      = "bad-escape"      // a % not followed by two hex digits
	RefusedBadUTF8        = "bad-utf8"        // a decoded value (or a file) that is not UTF-8
	RefusedMalformedField = "malformed-field" // an empty field, or a field with no key or no =
	RefusedRepeatedKey    = "repeated-key"    // a known key given twice
	RefusedMissingX       = "missing-x"       // no x field
	RefusedBadX           = "bad-x"           // x is not host:port
	RefusedBadFP          = "bad-fp"          // fp is not SHA256: plus 43 canonical standard base64 characters
	RefusedBadName        = "bad-n"           // n is empty, longer than 64 scalar values, or holds a control character
	RefusedBadLogin       = "bad-u"           // u is not a login of 5.1
	RefusedFileTooLong    = "file-too-long"   // a file over 4096 bytes
	RefusedFileLines      = "file-lines"      // a file with more than one non-empty line
)

// Looks values: what a refused text resembles, where the reader knows.
const (
	LooksShareCode = "share-code"
	LooksURL       = "url"
)

// Error is a refusal. Reason is one of the constants above; Looks is set only with RefusedNotAString.
type Error struct {
	Reason string
	Looks  string
	Detail string
}

func (e *Error) Error() string {
	if e.Detail != "" {
		return "connection string: " + e.Reason + ": " + e.Detail
	}
	return "connection string: " + e.Reason
}

func refuse(reason, detail string, a ...any) *Error {
	if len(a) > 0 {
		detail = fmt.Sprintf(detail, a...)
	}
	return &Error{Reason: reason, Detail: detail}
}

// RefusedOf returns the reason of a connstr error, or "" for any other error.
func RefusedOf(err error) string {
	if e, ok := err.(*Error); ok {
		return e.Reason
	}
	return ""
}

// Conn is one server named by a string, in canonical form: the host lower-cased (an IPv6 literal in
// its RFC 5952 form, without brackets), the port a number, the login lower-cased.
type Conn struct {
	Host        string
	Port        int
	Fingerprint string // "SHA256:" + 43 characters, or empty
	Name        string // display name, a hint
	Login       string // login hint
}

// Endpoint is the host:port form (an IPv6 literal in brackets) - the shape of publicEndpoint.
func (c Conn) Endpoint() string {
	h := c.Host
	if strings.Contains(h, ":") {
		h = "[" + h + "]"
	}
	return h + ":" + strconv.Itoa(c.Port)
}

// New validates the members an operator surface has and returns the canonical Conn. An empty fp, name or
// login is absent.
func New(endpoint, fingerprint, name, login string) (Conn, error) {
	host, port, err := splitEndpoint(endpoint)
	if err != nil {
		return Conn{}, err
	}
	c := Conn{Host: host, Port: port}
	if fingerprint != "" {
		if err := checkFingerprint(fingerprint); err != nil {
			return Conn{}, err
		}
		c.Fingerprint = fingerprint
	}
	if name != "" {
		if err := checkName(name); err != nil {
			return Conn{}, err
		}
		c.Name = name
	}
	if login != "" {
		l, err := normalizeLogin(login)
		if err != nil {
			return Conn{}, err
		}
		c.Login = l
	}
	return c, nil
}

// String is the canonical text. It fails when the text would exceed 512 characters.
func (c Conn) String() (string, error) {
	if _, err := New(c.Endpoint(), c.Fingerprint, c.Name, c.Login); err != nil {
		return "", err
	}
	var b strings.Builder
	b.WriteString(Prefix)
	b.WriteString(keyX + "=" + escape(c.Endpoint()))
	if c.Fingerprint != "" {
		b.WriteString("&" + keyFP + "=" + escape(c.Fingerprint))
	}
	if c.Name != "" {
		b.WriteString("&" + keyName + "=" + escape(c.Name))
	}
	if c.Login != "" {
		b.WriteString("&" + keyLogin + "=" + escape(c.Login))
	}
	if b.Len() > MaxText {
		return "", refuse(RefusedTooLong, "%d characters once encoded, the limit is %d", b.Len(), MaxText)
	}
	return b.String(), nil
}

// Parse reads the text of a connection string as it arrives from a clipboard, a chat or a QR: one
// leading byte-order mark and the white space around are dropped first.
func Parse(text string) (Conn, error) {
	s := strings.TrimPrefix(text, bom)
	s = strings.TrimFunc(s, unicode.IsSpace)
	if !strings.HasPrefix(s, Prefix) {
		return Conn{}, notAString(s)
	}
	if utf8.RuneCountInString(s) > MaxText {
		return Conn{}, refuse(RefusedTooLong, "more than %d characters", MaxText)
	}
	for _, r := range s {
		switch {
		case unicode.IsSpace(r):
			return Conn{}, refuse(RefusedWhitespace, "white space inside the text")
		case r < 0x21 || r > 0x7E:
			return Conn{}, refuse(RefusedNotASCII, "a raw non-ASCII or control character; values are percent-encoded")
		}
	}
	body := s[len(Prefix):]
	if body == "" {
		return Conn{}, refuse(RefusedMissingX, "no fields")
	}
	vals := map[string]string{}
	for _, f := range strings.Split(body, "&") {
		if f == "" {
			return Conn{}, refuse(RefusedMalformedField, "an empty field")
		}
		k, v, ok := strings.Cut(f, "=")
		if !ok || k == "" {
			return Conn{}, refuse(RefusedMalformedField, "a field with no key or no =")
		}
		switch k {
		case keyX, keyFP, keyName, keyLogin:
			if _, dup := vals[k]; dup {
				return Conn{}, refuse(RefusedRepeatedKey, "%s given twice", k)
			}
			d, err := unescape(v)
			if err != nil {
				return Conn{}, err
			}
			vals[k] = d
		}
		// an unknown key is ignored whatever its value (a later MINOR adds keys)
	}
	x, ok := vals[keyX]
	if !ok {
		return Conn{}, refuse(RefusedMissingX, "no x field")
	}
	// a known key that is present must carry a value: an empty one breaks its rule, it is not "absent"
	for _, k := range []struct{ key, reason string }{{keyFP, RefusedBadFP}, {keyName, RefusedBadName}, {keyLogin, RefusedBadLogin}} {
		if v, present := vals[k.key]; present && v == "" {
			return Conn{}, refuse(k.reason, "%s has no value", k.key)
		}
	}
	return New(x, vals[keyFP], vals[keyName], vals[keyLogin])
}

// ParseFile reads the content of a .fmsexch file: UTF-8, one optional byte-order mark, one line, one
// optional trailing line break, at most 4096 bytes. The caller reads at most MaxFile+1 bytes.
func ParseFile(b []byte) (Conn, error) {
	if len(b) > MaxFile {
		return Conn{}, refuse(RefusedFileTooLong, "more than %d bytes", MaxFile)
	}
	if !utf8.Valid(b) {
		return Conn{}, refuse(RefusedBadUTF8, "the file is not UTF-8")
	}
	s := strings.TrimPrefix(string(b), bom)
	var content string
	for _, line := range strings.FieldsFunc(s, func(r rune) bool { return r == '\n' || r == '\r' }) {
		if strings.TrimFunc(line, unicode.IsSpace) == "" {
			continue
		}
		if content != "" {
			return Conn{}, refuse(RefusedFileLines, "more than one non-empty line")
		}
		content = line
	}
	return Parse(content)
}

func notAString(s string) *Error {
	e := &Error{Reason: RefusedNotAString}
	switch {
	case len(s) >= len("FMSEXCH2:") && strings.HasPrefix(s, "FMSEXCH") && s[7] >= '2' && s[7] <= '9' && s[8] == ':':
		e.Reason = RefusedNewerMajor
	case strings.HasPrefix(s, shareCode):
		e.Looks = LooksShareCode
	case strings.Contains(s, "://"):
		e.Looks = LooksURL
	}
	return e
}

// ---- values -------------------------------------------------------------------------------------

// splitEndpoint reads host:port into the canonical host and the port.
func splitEndpoint(s string) (string, int, error) {
	bad := func(why string) (string, int, error) { return "", 0, refuse(RefusedBadX, "%s", why) }
	if s == "" {
		return bad("empty")
	}
	var host, port string
	if strings.HasPrefix(s, "[") {
		end := strings.Index(s, "]")
		if end < 0 || end+1 >= len(s) || s[end+1] != ':' {
			return bad("an IPv6 literal is [address]:port")
		}
		host, port = s[1:end], s[end+2:]
		a, err := netip.ParseAddr(host)
		if err != nil || !a.Is6() || a.Zone() != "" {
			return bad("the bracketed host is not an IPv6 address without a zone")
		}
		host = a.String()
	} else {
		i := strings.LastIndex(s, ":")
		if i < 0 {
			return bad("no port")
		}
		host, port = s[:i], s[i+1:]
		if strings.Contains(host, ":") {
			return bad("an IPv6 literal needs brackets")
		}
		h, err := canonicalHost(host)
		if err != nil {
			return "", 0, err
		}
		host = h
	}
	n, err := parsePort(port)
	if err != nil {
		return "", 0, err
	}
	return host, n, nil
}

func parsePort(s string) (int, error) {
	if s == "" || len(s) > 5 {
		return 0, refuse(RefusedBadX, "the port is 1 to 5 digits")
	}
	for i := 0; i < len(s); i++ {
		if s[i] < '0' || s[i] > '9' {
			return 0, refuse(RefusedBadX, "the port is digits only")
		}
	}
	n, _ := strconv.Atoi(s)
	if n < 1 || n > 65535 {
		return 0, refuse(RefusedBadX, "the port is 1 to 65535")
	}
	return n, nil
}

// canonicalHost accepts a DNS name (labels of letters, digits and hyphen, 1-63 characters, no trailing
// dot, an internationalised name only in its xn-- form) or an IPv4 dotted quad. A name made only of digits
// and dots is read as IPv4 or refused, never as a DNS name.
func canonicalHost(h string) (string, error) {
	if h == "" || len(h) > maxHost {
		return "", refuse(RefusedBadX, "the host is empty or too long")
	}
	allDigitsDots := true
	for i := 0; i < len(h); i++ {
		c := h[i]
		if !(c >= '0' && c <= '9' || c == '.') {
			allDigitsDots = false
		}
	}
	if allDigitsDots {
		a, err := netip.ParseAddr(h)
		if err != nil || !a.Is4() {
			return "", refuse(RefusedBadX, "a name of digits and dots must be an IPv4 address")
		}
		return a.String(), nil
	}
	for _, label := range strings.Split(h, ".") {
		if label == "" || len(label) > maxLabel {
			return "", refuse(RefusedBadX, "a host label is 1 to 63 characters")
		}
		if label[0] == '-' || label[len(label)-1] == '-' {
			return "", refuse(RefusedBadX, "a host label does not begin or end with a hyphen")
		}
		for i := 0; i < len(label); i++ {
			c := label[i]
			if !(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-') {
				return "", refuse(RefusedBadX, "a host holds only letters, digits and hyphens")
			}
		}
	}
	return strings.ToLower(h), nil
}

// checkFingerprint holds fp to "SHA256:" and exactly 43 characters of the STANDARD base64 alphabet, no
// padding, whose last character carries no stray bits - one spelling per leaf.
func checkFingerprint(fp string) error {
	if !strings.HasPrefix(fp, fpPrefix) || len(fp) != len(fpPrefix)+fpB64Len {
		return refuse(RefusedBadFP, "SHA256: and 43 base64 characters")
	}
	raw, err := base64.RawStdEncoding.Strict().DecodeString(fp[len(fpPrefix):])
	if err != nil || len(raw) != 32 {
		return refuse(RefusedBadFP, "not the canonical unpadded standard base64 of 32 bytes")
	}
	return nil
}

func checkName(n string) error {
	if n == "" {
		return refuse(RefusedBadName, "empty")
	}
	if !utf8.ValidString(n) {
		return refuse(RefusedBadName, "not UTF-8")
	}
	if utf8.RuneCountInString(n) > MaxName {
		return refuse(RefusedBadName, "more than %d characters", MaxName)
	}
	for _, r := range n {
		if unicode.Is(unicode.Cc, r) {
			return refuse(RefusedBadName, "a control character")
		}
	}
	return nil
}

// normalizeLogin is 5.1: 3-64 characters of a-z 0-9 . _ -, compared case-insensitively, written lower case.
func normalizeLogin(s string) (string, error) {
	l := strings.ToLower(s)
	if len(l) < minLogin || len(l) > maxLogin {
		return "", refuse(RefusedBadLogin, "a login is %d-%d characters", minLogin, maxLogin)
	}
	for i := 0; i < len(l); i++ {
		c := l[i]
		if !(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '.' || c == '_' || c == '-') {
			return "", refuse(RefusedBadLogin, "a login is a-z, 0-9, dot, underscore and hyphen")
		}
	}
	return l, nil
}

// ---- percent encoding ---------------------------------------------------------------------------

// Escape is the canonical value serializer, exported so a surface and a test write values the one way.
func Escape(s string) string { return escape(s) }

const upperHex = "0123456789ABCDEF"

// escape is the WHATWG application/x-www-form-urlencoded byte serializer over UTF-8.
func escape(s string) string {
	var b strings.Builder
	for i := 0; i < len(s); i++ {
		c := s[i]
		switch {
		case c >= 'A' && c <= 'Z', c >= 'a' && c <= 'z', c >= '0' && c <= '9',
			c == '*', c == '-', c == '.', c == '_':
			b.WriteByte(c)
		case c == ' ':
			b.WriteByte('+')
		default:
			b.WriteByte('%')
			b.WriteByte(upperHex[c>>4])
			b.WriteByte(upperHex[c&15])
		}
	}
	return b.String()
}

func unhex(c byte) (byte, bool) {
	switch {
	case c >= '0' && c <= '9':
		return c - '0', true
	case c >= 'a' && c <= 'f':
		return c - 'a' + 10, true
	case c >= 'A' && c <= 'F':
		return c - 'A' + 10, true
	}
	return 0, false
}

// unescape is form decoding: % with hex of either case, + as a space; the result must be UTF-8.
func unescape(s string) (string, error) {
	out := make([]byte, 0, len(s))
	for i := 0; i < len(s); i++ {
		switch c := s[i]; c {
		case '+':
			out = append(out, ' ')
		case '%':
			if i+2 >= len(s) {
				return "", refuse(RefusedBadEscape, "a % without two hex digits after it")
			}
			hi, ok1 := unhex(s[i+1])
			lo, ok2 := unhex(s[i+2])
			if !ok1 || !ok2 {
				return "", refuse(RefusedBadEscape, "a % not followed by two hex digits")
			}
			out = append(out, hi<<4|lo)
			i += 2
		default:
			out = append(out, c)
		}
	}
	if !utf8.Valid(out) {
		return "", refuse(RefusedBadUTF8, "a decoded value is not UTF-8")
	}
	return string(out), nil
}
