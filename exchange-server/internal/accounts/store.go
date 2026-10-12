// Package accounts is the exchange server's store of accounts, enrolled devices and revoked-token
// tombstones (DEVICE-EXCHANGE 5, ticket 162), and the in-memory pairing codes.
//
// Two files in the data directory, written by the server alone and private to this implementation
// (neither is the wire nor a contract; the version member is storeVersion so nothing here is
// confused with the wire's schemaVersion, and a reader refuses a higher number):
//
//	accounts.json   login, the password's Argon2id record, createdAt
//	devices.json    per device: account, id, name, product, version, platform, roles, receiver, the
//	                SHA-256 of its token, enrolledAt, lastSeen - and the tombstones
//
// Nothing secret is ever stored in the clear: a password only as its Argon2id record, a device token
// only as its SHA-256, a pairing code never on disk at all.
package accounts

import (
	"crypto/rand"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"io/fs"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"sync"
	"time"

	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/wire"
)

// File names inside the data directory.
const (
	AccountsFile = "accounts.json"
	DevicesFile  = "devices.json"
	// StoreVersion is the storeVersion both files carry. A reader refuses a higher number.
	StoreVersion = 1
)

// Errors the store reports. The wire's reason for each is the engine's choice, not the store's.
var (
	ErrNoAccount   = errors.New("accounts: no such account")
	ErrExists      = errors.New("accounts: the account exists")
	ErrBadLogin    = errors.New("accounts: a login is 3-64 characters of a-z, 0-9, dot, underscore and hyphen")
	ErrIDCollision = errors.New("accounts: the deviceId is enrolled in another account")
	ErrCapacity    = errors.New("accounts: the account holds the most devices it may")
	ErrNewerStore  = errors.New("accounts: the store was written by a newer server")
)

// Login rules (5.1).
const (
	minLogin = 3
	maxLogin = 64
)

// NormalizeLogin folds a login to lower case and reports whether it is a login an account may have.
func NormalizeLogin(s string) (string, bool) {
	s = strings.ToLower(strings.TrimSpace(s))
	if len(s) < minLogin || len(s) > maxLogin {
		return s, false
	}
	for i := 0; i < len(s); i++ {
		c := s[i]
		switch {
		case c >= 'a' && c <= 'z', c >= '0' && c <= '9', c == '.', c == '_', c == '-':
		default:
			return s, false
		}
	}
	return s, true
}

// Account is one user's space on the deployment.
type Account struct {
	Login        string `json:"login"`
	PasswordHash string `json:"passwordHash"` // the Argon2id record, never the password
	CreatedAt    string `json:"createdAt"`
}

// String never prints the record.
func (a Account) String() string { return "accounts.Account" }

// Device is one enrolled installation.
type Device struct {
	ID             string          `json:"deviceId"`
	Account        string          `json:"account"`
	Name           string          `json:"deviceName"`
	Product        string          `json:"product"`
	ProductVersion string          `json:"productVersion,omitempty"`
	Platform       string          `json:"platform,omitempty"`
	Roles          []string        `json:"roles,omitempty"`
	Capabilities   []string        `json:"capabilities,omitempty"`
	Receiver       json.RawMessage `json:"receiver,omitempty"`
	TokenHash      string          `json:"tokenHash"` // hex SHA-256 of the token
	EnrolledAt     string          `json:"enrolledAt"`
	LastSeen       string          `json:"lastSeen,omitempty"`
}

// String never prints a member.
func (d Device) String() string { return "accounts.Device" }

// Tombstone is the hash of a revoked device's token, kept so the device learns it was removed.
type Tombstone struct {
	DeviceID  string `json:"deviceId"`
	Account   string `json:"account"`
	TokenHash string `json:"tokenHash"`
	RevokedAt string `json:"revokedAt"`
}

// String never prints a member.
func (t Tombstone) String() string { return "accounts.Tombstone" }

type accountsFile struct {
	StoreVersion int       `json:"storeVersion"`
	Accounts     []Account `json:"accounts"`
}

type devicesFile struct {
	StoreVersion int         `json:"storeVersion"`
	Devices      []Device    `json:"devices"`
	Tombstones   []Tombstone `json:"tombstones"`
}

// Options configure Open.
type Options struct {
	Dir string
	Now func() time.Time
	// Params is the Argon2id cost of new records and of the dummy one (default password.DefaultParams;
	// a test passes a cheap one).
	Params password.Params
	// TombstoneLifetime is how long a revoked token's hash is kept (item I, working default 30 days).
	TombstoneLifetime time.Duration
	// DevicesPerAccount caps enrolled devices per account (working default 64).
	DevicesPerAccount int
	// CodesPerAccount caps outstanding pairing codes per account (working default 5) and CodeLifetime is
	// how long one lives (10 minutes): 5.3 and ticket 162 rule 6.
	CodesPerAccount int
	CodeLifetime    time.Duration
	// Rand is the random source of tokens and pairing codes (default crypto/rand).
	Rand io.Reader
	// WriteTemp creates the temporary file of a store write (a test replaces it).
	WriteTemp func(name string, data []byte, perm os.FileMode) error
}

// Store is the accounts and devices of one deployment.
type Store struct {
	opt   Options
	now   func() time.Time
	dummy string // an Argon2id record of a password nobody knows: what an unknown login is checked against

	mu         sync.RWMutex
	accounts   map[string]*Account // by login
	devices    map[string]*Device  // by deviceId
	tombstones map[string]*Tombstone
	dirtyLast  bool      // lastSeen changed in memory since the last write
	lastWrite  time.Time // of the last lastSeen write

	codes *codes
}

// Open reads the two files (an absent file is an empty store) and returns the store. It removes the
// temporary files a killed write may have left, and tombstones past their lifetime.
func Open(opt Options) (*Store, error) {
	if opt.Now == nil {
		opt.Now = time.Now
	}
	if opt.Params == (password.Params{}) {
		opt.Params = password.DefaultParams
	}
	if opt.TombstoneLifetime <= 0 {
		opt.TombstoneLifetime = 30 * 24 * time.Hour
	}
	if opt.DevicesPerAccount <= 0 {
		opt.DevicesPerAccount = 64
	}
	if opt.CodesPerAccount <= 0 {
		opt.CodesPerAccount = 5
	}
	if opt.CodeLifetime <= 0 {
		opt.CodeLifetime = 10 * time.Minute
	}
	if opt.Rand == nil {
		opt.Rand = rand.Reader
	}
	if opt.WriteTemp == nil {
		opt.WriteTemp = os.WriteFile
	}
	s := &Store{opt: opt, now: opt.Now,
		accounts: map[string]*Account{}, devices: map[string]*Device{}, tombstones: map[string]*Tombstone{}}
	s.codes = newCodes(opt.Now, opt.Rand, opt.CodesPerAccount, opt.CodeLifetime)

	for _, name := range []string{AccountsFile, DevicesFile} {
		_ = os.Remove(filepath.Join(opt.Dir, name+".tmp"))
	}
	var af accountsFile
	if err := readJSON(filepath.Join(opt.Dir, AccountsFile), &af); err != nil {
		return nil, err
	}
	if af.StoreVersion > StoreVersion {
		return nil, fmt.Errorf("%s: %w", AccountsFile, ErrNewerStore)
	}
	for i := range af.Accounts {
		a := af.Accounts[i]
		s.accounts[a.Login] = &a
	}
	var df devicesFile
	if err := readJSON(filepath.Join(opt.Dir, DevicesFile), &df); err != nil {
		return nil, err
	}
	if df.StoreVersion > StoreVersion {
		return nil, fmt.Errorf("%s: %w", DevicesFile, ErrNewerStore)
	}
	for i := range df.Devices {
		d := df.Devices[i]
		s.devices[d.ID] = &d
	}
	for i := range df.Tombstones {
		t := df.Tombstones[i]
		s.tombstones[t.DeviceID] = &t
	}

	// the dummy record: the same cost as a real one, a password that cannot be guessed
	junk := make([]byte, 24)
	if _, err := io.ReadFull(opt.Rand, junk); err != nil {
		return nil, err
	}
	rec, err := password.HashWith(hex.EncodeToString(junk), opt.Params)
	if err != nil {
		return nil, err
	}
	s.dummy = rec

	s.mu.Lock()
	changed := s.expireTombstonesLocked(s.now())
	var werr error
	if changed {
		werr = s.saveDevicesLocked()
	}
	s.mu.Unlock()
	if werr != nil {
		return nil, werr
	}
	return s, nil
}

func readJSON(path string, v any) error {
	data, err := os.ReadFile(path)
	if errors.Is(err, fs.ErrNotExist) {
		return nil
	}
	if err != nil {
		return err
	}
	if err := json.Unmarshal(data, v); err != nil {
		return fmt.Errorf("%s is not valid JSON: %w", filepath.Base(path), err)
	}
	return nil
}

// --- persistence: temp file plus rename, so a kill leaves the previous file whole ---

func (s *Store) writeAtomic(name string, v any) error {
	data, err := json.MarshalIndent(v, "", "  ")
	if err != nil {
		return err
	}
	data = append(data, '\n')
	path := filepath.Join(s.opt.Dir, name)
	if err := os.MkdirAll(s.opt.Dir, 0o700); err != nil {
		return err
	}
	tmp := path + ".tmp"
	if err := s.opt.WriteTemp(tmp, data, 0o600); err != nil {
		return err
	}
	if err := os.Rename(tmp, path); err != nil {
		_ = os.Remove(tmp)
		return err
	}
	return nil
}

func (s *Store) saveAccountsLocked() error {
	f := accountsFile{StoreVersion: StoreVersion, Accounts: []Account{}}
	for _, a := range s.accounts {
		f.Accounts = append(f.Accounts, *a)
	}
	sort.Slice(f.Accounts, func(i, j int) bool { return f.Accounts[i].Login < f.Accounts[j].Login })
	return s.writeAtomic(AccountsFile, f)
}

func (s *Store) saveDevicesLocked() error {
	f := devicesFile{StoreVersion: StoreVersion, Devices: []Device{}, Tombstones: []Tombstone{}}
	for _, d := range s.devices {
		f.Devices = append(f.Devices, *d)
	}
	for _, t := range s.tombstones {
		f.Tombstones = append(f.Tombstones, *t)
	}
	sort.Slice(f.Devices, func(i, j int) bool {
		if f.Devices[i].Account != f.Devices[j].Account {
			return f.Devices[i].Account < f.Devices[j].Account
		}
		return f.Devices[i].ID < f.Devices[j].ID
	})
	sort.Slice(f.Tombstones, func(i, j int) bool { return f.Tombstones[i].DeviceID < f.Tombstones[j].DeviceID })
	s.dirtyLast = false
	s.lastWrite = s.now()
	return s.writeAtomic(DevicesFile, f)
}

// --- accounts (5.1) ---

// AccountInfo is what an administrator may see of an account: never a record.
type AccountInfo struct {
	Login     string
	CreatedAt string
	Devices   int
}

// HasAccounts reports whether at least one account exists.
func (s *Store) HasAccounts() bool {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return len(s.accounts) > 0
}

// Accounts lists the accounts by login.
func (s *Store) Accounts() []AccountInfo {
	s.mu.RLock()
	defer s.mu.RUnlock()
	n := map[string]int{}
	for _, d := range s.devices {
		n[d.Account]++
	}
	out := make([]AccountInfo, 0, len(s.accounts))
	for _, a := range s.accounts {
		out = append(out, AccountInfo{Login: a.Login, CreatedAt: a.CreatedAt, Devices: n[a.Login]})
	}
	sort.Slice(out, func(i, j int) bool { return out[i].Login < out[j].Login })
	return out
}

// Exists reports whether the (normalised) login is an account.
func (s *Store) Exists(login string) bool {
	login, ok := NormalizeLogin(login)
	if !ok {
		return false
	}
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.accounts[login] != nil
}

// AddAccount creates an account. The password must meet the policy; only its Argon2id record is kept.
func (s *Store) AddAccount(login, pw string) (string, error) {
	login, ok := NormalizeLogin(login)
	if !ok {
		return "", ErrBadLogin
	}
	if err := password.Check(pw); err != nil {
		return "", err
	}
	rec, err := password.HashWith(pw, s.opt.Params)
	if err != nil {
		return "", err
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.accounts[login] != nil {
		return login, ErrExists
	}
	s.accounts[login] = &Account{Login: login, PasswordHash: rec, CreatedAt: wire.FormatTime(s.now())}
	if err := s.saveAccountsLocked(); err != nil {
		delete(s.accounts, login)
		return login, err
	}
	return login, nil
}

// SetPassword changes an account's password. Tokens are left alone (9: a password change does not
// sign devices out; the administrator asks for that separately).
func (s *Store) SetPassword(login, pw string) error {
	login, ok := NormalizeLogin(login)
	if !ok {
		return ErrNoAccount
	}
	if err := password.Check(pw); err != nil {
		return err
	}
	rec, err := password.HashWith(pw, s.opt.Params)
	if err != nil {
		return err
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	a := s.accounts[login]
	if a == nil {
		return ErrNoAccount
	}
	old := a.PasswordHash
	a.PasswordHash = rec
	if err := s.saveAccountsLocked(); err != nil {
		a.PasswordHash = old
		return err
	}
	return nil
}

// RemoveAccount deletes an account with every device and tombstone of it, and returns the devices
// that were enrolled, so the caller can close their streams and drop their records.
func (s *Store) RemoveAccount(login string) ([]Device, error) {
	login, ok := NormalizeLogin(login)
	if !ok {
		return nil, ErrNoAccount
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.accounts[login] == nil {
		return nil, ErrNoAccount
	}
	var gone []Device
	for id, d := range s.devices {
		if d.Account == login {
			gone = append(gone, *d)
			delete(s.devices, id)
		}
	}
	for id, t := range s.tombstones {
		if t.Account == login {
			delete(s.tombstones, id)
		}
	}
	sort.Slice(gone, func(i, j int) bool { return gone[i].ID < gone[j].ID })
	delete(s.accounts, login)
	s.codes.forget(login)
	if err := s.saveAccountsLocked(); err != nil {
		return gone, err
	}
	return gone, s.saveDevicesLocked()
}

// VerifyPassword checks a password against an account's record, or - for a login that is no
// account - against the dummy record, so the work and the time are the same and the answer does not
// tell a login from a password (ticket 162 rule 2). It is the function run inside the bound on
// concurrent verifications.
func (s *Store) VerifyPassword(login, pw string) bool {
	rec := s.dummy
	known := false
	if l, ok := NormalizeLogin(login); ok {
		s.mu.RLock()
		if a := s.accounts[l]; a != nil {
			rec, known = a.PasswordHash, true
		}
		s.mu.RUnlock()
	}
	ok := password.Verify(rec, pw)
	return ok && known
}

// --- devices (5.2) ---

// EnrollSpec is what an enroll carries about the device.
type EnrollSpec struct {
	Account        string // the normalised login of an account that exists
	DeviceID       string
	Name           string
	Product        string
	ProductVersion string
	Platform       string
	Roles          []string
	Capabilities   []string
	Receiver       json.RawMessage
}

// Enrolled is the outcome of a successful Enroll.
type Enrolled struct {
	Device  Device
	Token   string // the plaintext token, returned once and never kept
	Rotated bool   // the account already had this device: its old token died
}

func hashToken(token string) string {
	sum := sha256.Sum256([]byte(token))
	return hex.EncodeToString(sum[:])
}

// Enroll adds a device to an account, or rotates the token of one the account already has. A
// deviceId enrolled in another account (or revoked from one) is ErrIDCollision; an account at its
// device cap is ErrCapacity. The token is 32 random bytes, base64url without padding, and only its
// SHA-256 is stored.
func (s *Store) Enroll(spec EnrollSpec) (Enrolled, error) {
	token := wire.NewTokenFrom(s.opt.Rand)
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.accounts[spec.Account] == nil {
		return Enrolled{}, ErrNoAccount
	}
	old := s.devices[spec.DeviceID]
	if old != nil && old.Account != spec.Account {
		return Enrolled{}, ErrIDCollision
	}
	if t := s.tombstones[spec.DeviceID]; t != nil && t.Account != spec.Account {
		return Enrolled{}, ErrIDCollision
	}
	if old == nil {
		n := 0
		for _, d := range s.devices {
			if d.Account == spec.Account {
				n++
			}
		}
		if n >= s.opt.DevicesPerAccount {
			return Enrolled{}, ErrCapacity
		}
	}
	now := wire.FormatTime(s.now())
	d := &Device{
		ID: spec.DeviceID, Account: spec.Account, Name: spec.Name, Product: spec.Product,
		ProductVersion: spec.ProductVersion, Platform: spec.Platform, Roles: spec.Roles, Capabilities: append([]string(nil), spec.Capabilities...), Receiver: spec.Receiver,
		TokenHash: hashToken(token), EnrolledAt: now, LastSeen: now,
	}
	if old != nil {
		d.EnrolledAt = old.EnrolledAt
	}
	prevTomb := s.tombstones[spec.DeviceID]
	delete(s.tombstones, spec.DeviceID) // a device the account revoked may be enrolled again
	s.devices[d.ID] = d
	if err := s.saveDevicesLocked(); err != nil {
		if old != nil {
			s.devices[d.ID] = old
		} else {
			delete(s.devices, d.ID)
		}
		if prevTomb != nil {
			s.tombstones[spec.DeviceID] = prevTomb
		}
		return Enrolled{}, err
	}
	return Enrolled{Device: *d, Token: token, Rotated: old != nil}, nil
}

// HelloStatus is the verdict on a presented token.
type HelloStatus int

const (
	// HelloOK: the token is the device's.
	HelloOK HelloStatus = iota
	// HelloBad: unknown device or wrong token - one answer, whichever it was.
	HelloBad
	// HelloRevoked: the token matches the hash of a revoked device (item I).
	HelloRevoked
)

// Hello checks a device's token: the SHA-256 of the presented token against the stored hash, in
// constant time. An unknown deviceId runs a comparison against a dummy hash, so the time does not
// tell a deviceId that exists from one that does not. A token that is not the stored one but IS the
// hash of a revoked device is HelloRevoked - and only then, so the reason cannot be used to learn
// that a deviceId ever existed.
func (s *Store) Hello(deviceID, token string) (Device, HelloStatus) {
	got := hashToken(token)
	s.mu.RLock()
	defer s.mu.RUnlock()
	d := s.devices[deviceID]
	want := dummyHash
	if d != nil {
		want = d.TokenHash
	}
	match := subtle.ConstantTimeCompare([]byte(got), []byte(want)) == 1
	if d != nil && match {
		return *d, HelloOK
	}
	if t := s.tombstones[deviceID]; t != nil && subtle.ConstantTimeCompare([]byte(got), []byte(t.TokenHash)) == 1 {
		return Device{ID: deviceID, Account: t.Account}, HelloRevoked
	}
	if d != nil {
		return Device{Account: d.Account}, HelloBad // the account is known: the failure is charged to it too
	}
	return Device{}, HelloBad
}

var dummyHash = hashToken("not-a-token-anyone-holds")

// Device returns one enrolled device of an account.
func (s *Store) Device(account, id string) (Device, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	d := s.devices[id]
	if d == nil || d.Account != account {
		return Device{}, false
	}
	return *d, true
}

// DeviceByID returns an enrolled device in any account (deviceId is unique per deployment).
func (s *Store) DeviceByID(id string) (Device, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	d := s.devices[id]
	if d == nil {
		return Device{}, false
	}
	return *d, true
}

// Devices lists an account's devices by id.
func (s *Store) Devices(account string) []Device {
	s.mu.RLock()
	defer s.mu.RUnlock()
	var out []Device
	for _, d := range s.devices {
		if d.Account == account {
			out = append(out, *d)
		}
	}
	sort.Slice(out, func(i, j int) bool { return out[i].ID < out[j].ID })
	return out
}

// AllDevices lists every enrolled device.
func (s *Store) AllDevices() []Device {
	s.mu.RLock()
	defer s.mu.RUnlock()
	out := make([]Device, 0, len(s.devices))
	for _, d := range s.devices {
		out = append(out, *d)
	}
	sort.Slice(out, func(i, j int) bool { return out[i].ID < out[j].ID })
	return out
}

// Revoke kills a device's token: the hash moves to a tombstone, the device record goes. Only a
// device of the same account is revoked; anything else is (Device{}, false, nil) and changes nothing.
func (s *Store) Revoke(account, deviceID string) (Device, bool, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	d := s.devices[deviceID]
	if d == nil || d.Account != account {
		return Device{}, false, nil
	}
	tomb := &Tombstone{DeviceID: d.ID, Account: d.Account, TokenHash: d.TokenHash, RevokedAt: wire.FormatTime(s.now())}
	delete(s.devices, deviceID)
	s.tombstones[deviceID] = tomb
	if err := s.saveDevicesLocked(); err != nil {
		s.devices[deviceID] = d
		delete(s.tombstones, deviceID)
		return Device{}, false, err
	}
	return *d, true, nil
}

// Touch records that a device was seen. It changes memory only; the coalesced write (at most one a
// minute) is Sweep's, so presence flaps cost no disk.
func (s *Store) Touch(deviceID string) {
	s.mu.Lock()
	if d := s.devices[deviceID]; d != nil {
		d.LastSeen = wire.FormatTime(s.now())
		s.dirtyLast = true
	}
	s.mu.Unlock()
}

// Tombstones returns how many revoked-token hashes are held (a count, never a list).
func (s *Store) Tombstones() int {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return len(s.tombstones)
}

func (s *Store) expireTombstonesLocked(now time.Time) bool {
	changed := false
	for id, t := range s.tombstones {
		at, err := wire.ParseTime(t.RevokedAt)
		if err != nil || now.Sub(at) > s.opt.TombstoneLifetime {
			delete(s.tombstones, id)
			changed = true
		}
	}
	return changed
}

// Sweep forgets expired tombstones and expired pairing codes, and writes lastSeen at most once a
// minute. It returns an error only for a failed write.
func (s *Store) Sweep(now time.Time) error {
	s.codes.sweep(now)
	s.mu.Lock()
	defer s.mu.Unlock()
	changed := s.expireTombstonesLocked(now)
	if s.dirtyLast && now.Sub(s.lastWrite) >= time.Minute {
		changed = true
	}
	if !changed {
		return nil
	}
	return s.saveDevicesLocked()
}

// Flush writes everything that is only in memory (a clean stop).
func (s *Store) Flush() error {
	s.mu.Lock()
	defer s.mu.Unlock()
	if !s.dirtyLast {
		return nil
	}
	return s.saveDevicesLocked()
}

// Peek counts the accounts and devices on disk without opening the store: it writes nothing, takes no
// lock and hashes nothing, so `inspect` can report on a directory a server is running from.
func Peek(dir string) (accountCount, deviceCount int, err error) {
	var af accountsFile
	if err := readJSON(filepath.Join(dir, AccountsFile), &af); err != nil {
		return 0, 0, err
	}
	var df devicesFile
	if err := readJSON(filepath.Join(dir, DevicesFile), &df); err != nil {
		return 0, 0, err
	}
	return len(af.Accounts), len(df.Devices), nil
}
