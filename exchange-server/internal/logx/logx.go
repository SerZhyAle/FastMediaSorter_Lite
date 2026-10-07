// Package logx is the only logger of the module. Its API takes typed values so that an id or a
// secret cannot reach a log line by accident (DEVICE-EXCHANGE 7.9, tickets 139 and 167):
//
//   - Device, Resource, Share, Broadcast, Tunnel and Login render an id by the first 8 hex characters
//     of its SHA-256, never the id;
//   - Text(key, s) strips control characters and caps the value at 1024 bytes;
//   - there is no way to pass a raw string field.
//
// A source-scan test (guard_test.go) fails when another package imports log or log/slog or
// prints to the standard streams outside cmd/.
package logx

import (
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"io"
	"net"
	"os"
	"strconv"
	"strings"
	"sync"
	"time"
	"unicode/utf8"
)

// MaxText is the cap on one Text value, in bytes (the worker's security-log rule).
const MaxText = 1024

// Field is one key=value pair of a line. Build it with Share, Addr, Port, Int or Text.
type Field struct{ key, val string }

// hashed renders an id as the first 8 hex characters of its SHA-256 under a fixed key. Every id type
// below goes through it; there is no constructor that takes a raw secret, so a token, a password, a
// pairing code, an `access` string or a descriptor has nothing to be handed to (DEVICE-EXCHANGE 7.9,
// ticket 167 rule 4).
func hashed(key, id string) Field {
	sum := sha256.Sum256([]byte(id))
	return Field{key, hex.EncodeToString(sum[:4])}
}

// Share names a share id by the first 8 hex characters of the SHA-256 of its id.
func Share(id string) Field { return hashed("share", id) }

// Device names a device by the hashed form of its deviceId.
func Device(id string) Field { return hashed("device", id) }

// Resource names a resource by the hashed form of its resourceId.
func Resource(id string) Field { return hashed("resource", id) }

// Broadcast names a broadcast by the hashed form of its broadcastId.
func Broadcast(id string) Field { return hashed("broadcast", id) }

// Tunnel names a tunnel by the hashed form of its tunnelId.
func Tunnel(id string) Field { return hashed("tunnel", id) }

// Login names an account by the hashed form of its login, folded to lower case like every login. It
// is hashed because a person who types a password into the login field must not leave it in a log.
func Login(login string) Field { return hashed("login", strings.ToLower(login)) }

// Addr renders a client address. Only the IP is kept: a port tells nothing a log needs.
func Addr(a string) Field {
	if host, _, err := net.SplitHostPort(a); err == nil {
		a = host
	}
	return Field{"addr", clean(a, 64)}
}

// Port renders a TCP port number.
func Port(p int) Field { return Field{"port", strconv.Itoa(p)} }

// Int renders a number under a fixed key (counts, sizes).
func Int(key string, n int) Field { return Field{cleanKey(key), strconv.Itoa(n)} }

// Text renders free text: control characters replaced, capped at MaxText bytes.
func Text(key, s string) Field { return Field{cleanKey(key), clean(s, MaxText)} }

func cleanKey(k string) string {
	var b strings.Builder
	for _, r := range k {
		switch {
		case r >= 'a' && r <= 'z', r >= 'A' && r <= 'Z', r >= '0' && r <= '9', r == '_', r == '-':
			b.WriteRune(r)
		default:
			b.WriteByte('_')
		}
	}
	if b.Len() == 0 {
		return "_"
	}
	return b.String()
}

func clean(s string, max int) string {
	var b strings.Builder
	for _, r := range s {
		if r < 0x20 || r == 0x7f || r == utf8.RuneError {
			b.WriteByte(' ')
			continue
		}
		b.WriteRune(r)
	}
	out := b.String()
	if len(out) > max {
		out = out[:max]
		for !utf8.ValidString(out) {
			out = out[:len(out)-1]
		}
	}
	return out
}

// Logger writes tab-separated lines: time, level, event, then key=value fields.
type Logger struct {
	mu   sync.Mutex
	w    io.Writer
	file *os.File
	path string
	max  int64
	size int64
	now  func() time.Time
}

// New writes to w with no rotation.
func New(w io.Writer) *Logger { return &Logger{w: w, now: time.Now} }

// Open appends to the file at path and rotates it once it would pass maxBytes: the file is renamed
// to path+".1", replacing the previous one, and a new file is started. maxBytes <= 0 disables it.
func Open(path string, maxBytes int64) (*Logger, error) {
	l := &Logger{path: path, max: maxBytes, now: time.Now}
	if err := l.openFile(); err != nil {
		return nil, err
	}
	return l, nil
}

func (l *Logger) openFile() error {
	f, err := os.OpenFile(l.path, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0o600)
	if err != nil {
		return err
	}
	if st, err := f.Stat(); err == nil {
		l.size = st.Size()
	}
	l.file, l.w = f, f
	return nil
}

// Close closes a file logger.
func (l *Logger) Close() error {
	if l == nil {
		return nil
	}
	l.mu.Lock()
	defer l.mu.Unlock()
	if l.file != nil {
		err := l.file.Close()
		l.file = nil
		return err
	}
	return nil
}

// Info, Warn and Error write one line. Safe on a nil Logger, which discards.
func (l *Logger) Info(event string, f ...Field)  { l.write("INFO", event, f) }
func (l *Logger) Warn(event string, f ...Field)  { l.write("WARN", event, f) }
func (l *Logger) Error(event string, f ...Field) { l.write("ERROR", event, f) }

func (l *Logger) write(level, event string, fields []Field) {
	if l == nil {
		return
	}
	var b strings.Builder
	b.WriteString(l.now().UTC().Format("2006-01-02T15:04:05.000Z"))
	b.WriteByte('\t')
	b.WriteString(level)
	b.WriteByte('\t')
	b.WriteString(clean(event, 128))
	for _, f := range fields {
		b.WriteByte('\t')
		b.WriteString(f.key)
		b.WriteByte('=')
		b.WriteString(f.val)
	}
	b.WriteByte('\n')
	line := b.String()

	l.mu.Lock()
	defer l.mu.Unlock()
	if l.w == nil {
		return
	}
	if l.file != nil && l.max > 0 && l.size+int64(len(line)) > l.max && l.size > 0 {
		l.rotate()
	}
	n, _ := io.WriteString(l.w, line)
	l.size += int64(n)
}

func (l *Logger) rotate() {
	_ = l.file.Close()
	l.file, l.w = nil, nil
	_ = os.Remove(l.path + ".1")
	if err := os.Rename(l.path, l.path+".1"); err != nil {
		// Could not rotate: keep appending rather than lose lines.
		_ = l.openFile()
		return
	}
	l.size = 0
	if err := l.openFile(); err != nil {
		fmt.Fprint(io.Discard, err)
	}
}
