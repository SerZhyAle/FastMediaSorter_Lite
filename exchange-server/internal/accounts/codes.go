package accounts

import (
	"crypto/sha256"
	"crypto/subtle"
	"io"
	"strings"
	"sync"
	"time"
)

// Pairing codes (DEVICE-EXCHANGE 5.3, ticket 162 rule 6). A code is 8 characters of Crockford base32
// built from 5 random bytes - 40 bits, 5 per character, so there is no modulo bias - valid ten
// minutes, single use. They live in memory only, as SHA-256, never on disk: a restart loses them.

const crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"

// failuresToBurn is how many failed pairing enrolls of one login, inside a validity window, burn
// every active code of that login (item J: a wrong code is not any issued code, so attempts are
// counted per login).
const failuresToBurn = 5

type codeEntry struct {
	hash    [sha256.Size]byte
	expires time.Time
}

type loginCodes struct {
	active   []codeEntry
	failures []time.Time
}

type codes struct {
	mu       sync.Mutex
	now      func() time.Time
	rand     io.Reader
	max      int
	lifetime time.Duration
	byLogin  map[string]*loginCodes
}

func newCodes(now func() time.Time, r io.Reader, max int, lifetime time.Duration) *codes {
	return &codes{now: now, rand: r, max: max, lifetime: lifetime, byLogin: map[string]*loginCodes{}}
}

// NormalizeCode is what a person's typing becomes before a comparison: upper case, spaces and
// hyphens removed, I and L read as 1, O as 0.
func NormalizeCode(s string) string {
	var b strings.Builder
	for _, r := range strings.ToUpper(s) {
		switch r {
		case ' ', '-', '\t':
			continue
		case 'I', 'L':
			r = '1'
		case 'O':
			r = '0'
		}
		b.WriteRune(r)
	}
	return b.String()
}

func (c *codes) active(lc *loginCodes, now time.Time) []codeEntry {
	kept := lc.active[:0]
	for _, e := range lc.active {
		if now.Before(e.expires) {
			kept = append(kept, e)
		}
	}
	lc.active = kept
	return kept
}

func (c *codes) forget(login string) {
	c.mu.Lock()
	delete(c.byLogin, login)
	c.mu.Unlock()
}

func (c *codes) sweep(now time.Time) {
	c.mu.Lock()
	defer c.mu.Unlock()
	for login, lc := range c.byLogin {
		c.active(lc, now)
		keep := lc.failures[:0]
		for _, f := range lc.failures {
			if now.Sub(f) < c.lifetime {
				keep = append(keep, f)
			}
		}
		lc.failures = keep
		if len(lc.active) == 0 && len(lc.failures) == 0 {
			delete(c.byLogin, login)
		}
	}
}

// NewPairingCode issues a code for an account that exists. It is ErrCapacity while the account has
// its maximum of outstanding codes.
func (s *Store) NewPairingCode(account string) (code string, expires time.Time, err error) {
	if !s.Exists(account) {
		return "", time.Time{}, ErrNoAccount
	}
	c := s.codes
	c.mu.Lock()
	defer c.mu.Unlock()
	now := c.now()
	lc := c.byLogin[account]
	if lc == nil {
		lc = &loginCodes{}
		c.byLogin[account] = lc
	}
	if len(c.active(lc, now)) >= c.max {
		return "", time.Time{}, ErrCapacity
	}
	var raw [5]byte
	if _, err := io.ReadFull(c.rand, raw[:]); err != nil {
		return "", time.Time{}, err
	}
	var v uint64
	for _, b := range raw {
		v = v<<8 | uint64(b)
	}
	out := make([]byte, 8)
	for i := 7; i >= 0; i-- {
		out[i] = crockford[v&31]
		v >>= 5
	}
	code = string(out)
	expires = now.Add(c.lifetime)
	lc.active = append(lc.active, codeEntry{hash: sha256.Sum256([]byte(code)), expires: expires})
	return code, expires, nil
}

// RedeemPairingCode compares a typed code against every active code of the account in constant time
// and burns the one that matched. A wrong code counts as a failed pairing enroll of the login: five
// inside a validity window burn every active code of it. An account that does not exist is false and
// remembers nothing, so a guessing stranger cannot make the table grow.
func (s *Store) RedeemPairingCode(account, typed string) bool {
	if !s.Exists(account) {
		return false
	}
	c := s.codes
	c.mu.Lock()
	defer c.mu.Unlock()
	now := c.now()
	lc := c.byLogin[account]
	if lc == nil {
		lc = &loginCodes{}
		c.byLogin[account] = lc
	}
	want := sha256.Sum256([]byte(NormalizeCode(typed)))
	live := c.active(lc, now)
	match := -1
	for i := range live {
		if subtle.ConstantTimeCompare(want[:], live[i].hash[:]) == 1 {
			match = i
		}
	}
	if match >= 0 {
		lc.active = append(live[:match], live[match+1:]...)
		return true
	}
	keep := lc.failures[:0]
	for _, f := range lc.failures {
		if now.Sub(f) < c.lifetime {
			keep = append(keep, f)
		}
	}
	lc.failures = append(keep, now)
	if len(lc.failures) >= failuresToBurn {
		lc.active = nil
		lc.failures = nil
	}
	return false
}

// OutstandingCodes is how many unexpired codes an account holds (a count, never the codes).
func (s *Store) OutstandingCodes(account string) int {
	c := s.codes
	c.mu.Lock()
	defer c.mu.Unlock()
	lc := c.byLogin[account]
	if lc == nil {
		return 0
	}
	return len(c.active(lc, c.now()))
}
