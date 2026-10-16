// Package accounts is the exchange server's store of accounts, enrolled devices and revoked-token
// tombstones (DEVICE-EXCHANGE 5, ticket 162), and the in-memory pairing codes.
//
// Four files in the data directory, written by the server alone and private to this implementation
// (none is the wire nor a contract; the version member is storeVersion so nothing here is
// confused with the wire's schemaVersion, and a reader refuses a higher number):
//
//	accounts.json     login, the password's Argon2id record, createdAt, the kind (member or guest),
//	                  the account's own limits
//	devices.json      per device: account, id, name, product, version, platform, roles, receiver, the
//	                  SHA-256 of its token, enrolledAt, lastSeen, signedOutAt - and the tombstones
//	invitations.json  per one-time invitation: the SHA-256 of its code, kind, created, used - and the
//	                  shared invitation password's Argon2id record (19 items AP and AQ)
//	grants.json       what a member gave to a guest: the target id, the guest, the giver (19 item AO)
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
	ErrNoDevice    = errors.New("accounts: no such device")
	ErrExists      = errors.New("accounts: the account exists")
	ErrBadLogin    = errors.New("accounts: a login is 3-64 characters of a-z, 0-9, dot, underscore and hyphen")
	ErrIDCollision = errors.New("accounts: the deviceId is enrolled in another account")
	ErrCapacity    = errors.New("accounts: the account holds the most devices it may")
	ErrNewerStore  = errors.New("accounts: the store was written by a newer server")
	// ErrLimit is a request that would cross a limit of the operator (19 item AS).
	ErrLimit = errors.New("accounts: a limit of the account or the deployment would be crossed")
	// ErrBadKind is an account kind that is neither member nor guest.
	ErrBadKind = errors.New("accounts: the account kind is member or guest")
	// ErrBadLimit is a limit that is not a number of at least zero.
	ErrBadLimit = errors.New("accounts: a limit is a number of at least zero")
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

// Account kinds (DEVICE-EXCHANGE 19 item AO). A member is the account of 0.20 unchanged; a guest is given
// things and offers nothing.
const (
	KindMember = "member"
	KindGuest  = "guest"
)

// NormalizeKind folds the spellings of a kind to one of the two. An empty kind is a member, and so are
// the words an earlier build wrote (`full`, `family`), so a store from before 46 reads as it did.
func NormalizeKind(k string) (string, error) {
	switch strings.ToLower(strings.TrimSpace(k)) {
	case "", KindMember, "family", "full":
		return KindMember, nil
	case KindGuest:
		return KindGuest, nil
	}
	return "", ErrBadKind
}

// AccountLimits holds per-account limits overrides (rule 5).
type AccountLimits struct {
	DevicesPerAccount    *int   `json:"devicesPerAccount,omitempty"`
	ResourcesPerAccount  *int   `json:"resourcesPerAccount,omitempty"`
	BroadcastsPerAccount *int   `json:"broadcastsPerAccount,omitempty"`
	BroadcastsPerDevice  *int   `json:"broadcastsPerDevice,omitempty"`
	ClipboardMaxBytes    *int64 `json:"clipboardMaxBytes,omitempty"`
}

// Account is one user's space on the deployment.
type Account struct {
	Login        string         `json:"login"`
	PasswordHash string         `json:"passwordHash"` // the Argon2id record, never the password
	CreatedAt    string         `json:"createdAt"`
	Kind         string         `json:"kind,omitempty"` // "member" (the default) or "guest"
	Limits       *AccountLimits `json:"limits,omitempty"`
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
	// E2EKey is the device's public key for sealing (27 item DF): public, kept with the record, listed in
	// the directory, and fixed for the life of the deviceId.
	E2EKey     *wire.E2EKey `json:"e2eKey,omitempty"`
	TokenHash  string       `json:"tokenHash"` // hex SHA-256 of the token
	EnrolledAt string       `json:"enrolledAt"`
	LastSeen   string       `json:"lastSeen,omitempty"`
	// SignedOutAt is set when the account's password changed: the record stays, the token in TokenHash is
	// dead, and a `hello` with it is `signed-out` until the hash is forgotten 30 days later (19 item AT).
	SignedOutAt string `json:"signedOutAt,omitempty"`
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
	// Sleep overrides time.Sleep in the pacer (useful for tests).
	Sleep func(time.Duration)
	// PaceInterval is the time between two password checks of one login or address: 0 is the contract's one
	// second (19 item AW), a negative value switches pacing off (tests that are not about the pace).
	PaceInterval time.Duration
	// WriteTemp creates the temporary file of a store write (a test replaces it).
	WriteTemp func(name string, data []byte, perm os.FileMode) error
	// Policy is the operator's cap on accounts and the limits a new account starts with; nil is
	// DefaultPolicy with DevicesPerAccount above.
	Policy *Policy
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

	invitations        map[string]*Invitation // by codeHash
	sharedPasswordHash string
	sharedKind         string

	grants map[string]*Grant // by grantKey: giver, target and guest

	policy Policy // the operator's cap on accounts and the limits a new account starts with

	codes *codes
	pacer *Pacer
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
	s := &Store{
		opt: opt, now: opt.Now,
		accounts:    map[string]*Account{},
		devices:     map[string]*Device{},
		tombstones:  map[string]*Tombstone{},
		invitations: map[string]*Invitation{},
		grants:      map[string]*Grant{},
	}
	s.policy = DefaultPolicy()
	s.policy.Defaults.Devices = opt.DevicesPerAccount
	if opt.Policy != nil {
		s.policy = *opt.Policy
		if s.policy.MaxAccounts <= 0 {
			s.policy.MaxAccounts = DefaultMaxAccounts
		}
	}
	s.codes = newCodes(opt.Now, opt.Rand, opt.CodesPerAccount, opt.CodeLifetime)
	pacerNow := opt.Now
	if opt.Sleep == nil {
		pacerNow = time.Now
	}
	s.pacer = newPacer(pacerNow, opt.Sleep, opt.PaceInterval)

	for _, name := range []string{AccountsFile, DevicesFile, InvitationsFile, GrantsFile} {
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
		if k, err := NormalizeKind(a.Kind); err == nil {
			a.Kind = k
		} else {
			a.Kind = KindMember
		}
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
	var invFile invitationsFile
	if err := readJSON(filepath.Join(opt.Dir, InvitationsFile), &invFile); err != nil {
		return nil, err
	}
	if invFile.StoreVersion > StoreVersion {
		return nil, fmt.Errorf("%s: %w", InvitationsFile, ErrNewerStore)
	}
	s.sharedPasswordHash = invFile.SharedPasswordHash
	s.sharedKind = invFile.SharedKind
	for i := range invFile.Invitations {
		inv := invFile.Invitations[i]
		s.invitations[inv.CodeHash] = &inv
	}
	var gFile grantsFile
	if err := readJSON(filepath.Join(opt.Dir, GrantsFile), &gFile); err != nil {
		return nil, err
	}
	if gFile.StoreVersion > StoreVersion {
		return nil, fmt.Errorf("%s: %w", GrantsFile, ErrNewerStore)
	}
	newGrantIDs := false
	for i := range gFile.Grants {
		g := gFile.Grants[i]
		if g.Kind == "" {
			g.Kind = GrantResource
		}
		if g.GrantID == "" { // a row written before grantIds existed (item BD): the id is fixed now and kept
			g.GrantID = wire.NewID()
			newGrantIDs = true
		}
		s.grants[grantKey(g.GrantedBy, g.TargetID, g.Account)] = &g
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
	if newGrantIDs && werr == nil {
		werr = s.saveGrantsLocked()
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
	Kind      string
	Limits    *AccountLimits
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
		k := a.Kind
		if k == "" {
			k = KindMember
		}
		out = append(out, AccountInfo{
			Login: a.Login, CreatedAt: a.CreatedAt, Devices: n[a.Login],
			Kind: k, Limits: a.Limits,
		})
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

// Account returns a copy of an account by login.
func (s *Store) Account(login string) (Account, bool) {
	login, ok := NormalizeLogin(login)
	if !ok {
		return Account{}, false
	}
	s.mu.RLock()
	defer s.mu.RUnlock()
	a := s.accounts[login]
	if a == nil {
		return Account{}, false
	}
	return *a, true
}

// KindOf is the kind of an account, member for a login that is none: the answer a `welcome` carries.
func (s *Store) KindOf(login string) string {
	login, ok := NormalizeLogin(login)
	if !ok {
		return KindMember
	}
	s.mu.RLock()
	defer s.mu.RUnlock()
	if a := s.accounts[login]; a != nil && a.Kind == KindGuest {
		return KindGuest
	}
	return KindMember
}

// IsGuest reports whether the account is a guest account.
func (s *Store) IsGuest(login string) bool { return s.KindOf(login) == KindGuest }

// SetAccountLimits sets limits overrides for an account (nil clears them).
func (s *Store) SetAccountLimits(login string, lim *AccountLimits) error {
	login, ok := NormalizeLogin(login)
	if !ok {
		return ErrNoAccount
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	if err := lim.Validate(); err != nil {
		return err
	}
	if lim.IsEmpty() {
		lim = nil
	}
	a := s.accounts[login]
	if a == nil {
		return ErrNoAccount
	}
	old := a.Limits
	a.Limits = lim
	if err := s.saveAccountsLocked(); err != nil {
		a.Limits = old
		return err
	}
	return nil
}

// AccountLimitsFor returns an account's limits override (or nil if none).
func (s *Store) AccountLimitsFor(login string) *AccountLimits {
	login, ok := NormalizeLogin(login)
	if !ok {
		return nil
	}
	s.mu.RLock()
	defer s.mu.RUnlock()
	a := s.accounts[login]
	if a == nil {
		return nil
	}
	return a.Limits
}

// Pacer returns the pacer of password checks (19 item AW).
func (s *Store) Pacer() *Pacer {
	return s.pacer
}

// HashPassword checks a password against the policy of 1.2 and returns its Argon2id record. It is
// the memory-hard step of a registration and of a password change, so a caller that serves a stream
// runs it inside the bound on concurrent verifications.
func (s *Store) HashPassword(pw string) (string, error) {
	if err := password.Check(pw); err != nil {
		return "", err
	}
	return password.HashWith(pw, s.opt.Params)
}

// AddAccount creates a member account. The password must meet the policy; only its Argon2id record is kept.
func (s *Store) AddAccount(login, pw string) (string, error) {
	return s.AddAccountWithKind(login, pw, KindMember)
}

// AddAccountWithKind creates an account of a kind ("member" or "guest"). It is the operator's way in: the
// cap of item AS binds `register` and the accounts the operator creates count toward it, but the operator
// is never refused here - the surface says that the count is over the setting (AccountCount, Policy).
func (s *Store) AddAccountWithKind(login, pw, kind string) (string, error) {
	login, ok := NormalizeLogin(login)
	if !ok {
		return "", ErrBadLogin
	}
	k, err := NormalizeKind(kind)
	if err != nil {
		return "", err
	}
	rec, err := s.HashPassword(pw)
	if err != nil {
		return "", err
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.accounts[login] != nil {
		return login, ErrExists
	}
	s.accounts[login] = &Account{Login: login, PasswordHash: rec, CreatedAt: wire.FormatTime(s.now()), Kind: k}
	if err := s.saveAccountsLocked(); err != nil {
		delete(s.accounts, login)
		return login, err
	}
	return login, nil
}

// SetKind changes the kind of an account and reports whether it changed. A guest holds no grant it made
// and a member holds no grant given to it, so the grants that no longer mean anything are dropped. The
// caller (admin.Ops) withdraws what a new guest had published.
func (s *Store) SetKind(login, kind string) (changed bool, err error) {
	login, ok := NormalizeLogin(login)
	if !ok {
		return false, ErrNoAccount
	}
	k, err := NormalizeKind(kind)
	if err != nil {
		return false, err
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	a := s.accounts[login]
	if a == nil {
		return false, ErrNoAccount
	}
	if a.Kind == k {
		return false, nil
	}
	old := a.Kind
	a.Kind = k
	if err := s.saveAccountsLocked(); err != nil {
		a.Kind = old
		return false, err
	}
	if s.removeGrantsLocked(login) {
		return true, s.saveGrantsLocked()
	}
	return true, nil
}

// signOutLocked marks every device of the account except one as signed out (the token dies, the record
// stays: 19 items AT and AV) and burns the pairing codes of the account. It returns the devices whose
// streams the caller must close.
func (s *Store) signOutLocked(login, except string) []string {
	now := wire.FormatTime(s.now())
	var ids []string
	for id, d := range s.devices {
		if d.Account != login || id == except {
			continue
		}
		if d.SignedOutAt == "" {
			d.SignedOutAt = now
		}
		ids = append(ids, id)
	}
	s.codes.forget(login)
	sort.Strings(ids)
	return ids
}

// SetPassword is the operator's reset: a new password and EVERY device of the account signed out, no
// device spared (19 item AT). It returns the signed-out devices.
func (s *Store) SetPassword(login, pw string) ([]string, error) {
	login, ok := NormalizeLogin(login)
	if !ok {
		return nil, ErrNoAccount
	}
	rec, err := s.HashPassword(pw)
	if err != nil {
		return nil, err
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	a := s.accounts[login]
	if a == nil {
		return nil, ErrNoAccount
	}
	old := a.PasswordHash
	a.PasswordHash = rec
	if err := s.saveAccountsLocked(); err != nil {
		a.PasswordHash = old
		return nil, err
	}
	ids := s.signOutLocked(login, "")
	return ids, s.saveDevicesLocked()
}

// ChangePassword is a person's change with the old password (19 item AT). The caller has already
// verified the old password and made the Argon2id record of the new one (HashPassword). The calling
// device receives a fresh token and stays signed in; every other device of the account is signed out
// and the pairing codes are burned. It returns the new token and the devices to close.
func (s *Store) ChangePassword(login, newRecord, callerDeviceID string) (newToken string, signedOut []string, err error) {
	login, ok := NormalizeLogin(login)
	if !ok {
		return "", nil, ErrNoAccount
	}
	token := wire.NewTokenFrom(s.opt.Rand)
	s.mu.Lock()
	defer s.mu.Unlock()
	a := s.accounts[login]
	if a == nil {
		return "", nil, ErrNoAccount
	}
	caller := s.devices[callerDeviceID]
	if caller == nil || caller.Account != login {
		return "", nil, ErrNoDevice
	}
	oldRec, oldHash, oldOut := a.PasswordHash, caller.TokenHash, caller.SignedOutAt
	a.PasswordHash = newRecord
	if err := s.saveAccountsLocked(); err != nil {
		a.PasswordHash = oldRec
		return "", nil, err
	}
	caller.TokenHash = hashToken(token)
	caller.SignedOutAt = ""
	caller.LastSeen = wire.FormatTime(s.now())
	ids := s.signOutLocked(login, callerDeviceID)
	if err := s.saveDevicesLocked(); err != nil {
		caller.TokenHash, caller.SignedOutAt = oldHash, oldOut
		return "", nil, err
	}
	return token, ids, nil
}

// RemoveAccount erases an account and everything of it the store holds: its devices, the tombstones of
// its revoked and signed-out tokens, its pairing codes, every grant it made or was given, and the
// invitation records that name it (19 item AU). The operator's removal and a person's deletion both end
// here, so the two can never erase different sets. It returns the devices that were enrolled.
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
	grants := s.removeGrantsLocked(login)
	invs := s.forgetInvitationsOfLocked(login)

	// every file is written even when one fails: memory is already clean, and the first error is reported
	var first error
	keep := func(err error) {
		if err != nil && first == nil {
			first = err
		}
	}
	keep(s.saveAccountsLocked())
	keep(s.saveDevicesLocked())
	if grants {
		keep(s.saveGrantsLocked())
	}
	if invs {
		keep(s.saveInvitationsLocked())
	}
	return gone, first
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
	E2EKey         *wire.E2EKey // optional, already validated by the wire (27 item DF)
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

// addDeviceLocked checks and applies one enrollment in memory: the collision rules, the account's own
// device limit and the dead token of a device that was signed out. Nothing is written; the caller saves
// and, on failure, hands the three returned values to undoDeviceLocked.
func (s *Store) addDeviceLocked(spec EnrollSpec, token string) (d, old *Device, prevTomb *Tombstone, err error) {
	if s.accounts[spec.Account] == nil {
		return nil, nil, nil, ErrNoAccount
	}
	old = s.devices[spec.DeviceID]
	if old != nil && old.Account != spec.Account {
		return nil, nil, nil, ErrIDCollision
	}
	if t := s.tombstones[spec.DeviceID]; t != nil && t.Account != spec.Account {
		return nil, nil, nil, ErrIDCollision
	}
	// A key lives exactly as long as its deviceId (27 item DF): a known device that is kept with a key
	// enrolls again with that same key (a token rotation) or not at all. Another key, or none, is a
	// collision and the record stays as it was. A device kept without a key (enrolled before it had
	// one) may present its first.
	if old != nil && old.E2EKey != nil && !old.E2EKey.Equal(spec.E2EKey) {
		return nil, nil, nil, ErrIDCollision
	}
	if old == nil {
		if lim := s.policy.Resolve(s.accounts[spec.Account].Limits).Devices; lim > 0 {
			n := 0
			for _, x := range s.devices {
				if x.Account == spec.Account {
					n++
				}
			}
			if n >= lim {
				return nil, nil, nil, ErrLimit
			}
		}
	}
	now := wire.FormatTime(s.now())
	d = &Device{
		ID: spec.DeviceID, Account: spec.Account, Name: spec.Name, Product: spec.Product,
		ProductVersion: spec.ProductVersion, Platform: spec.Platform, Roles: spec.Roles, Capabilities: append([]string(nil), spec.Capabilities...), Receiver: spec.Receiver,
		E2EKey:    spec.E2EKey.Clone(),
		TokenHash: hashToken(token), EnrolledAt: now, LastSeen: now,
	}
	if old != nil {
		d.EnrolledAt = old.EnrolledAt
	}
	prevTomb = s.tombstones[spec.DeviceID]
	delete(s.tombstones, spec.DeviceID) // a device the account revoked may be enrolled again
	s.devices[d.ID] = d
	return d, old, prevTomb, nil
}

func (s *Store) undoDeviceLocked(d, old *Device, prevTomb *Tombstone) {
	if old != nil {
		s.devices[d.ID] = old
	} else {
		delete(s.devices, d.ID)
	}
	if prevTomb != nil {
		s.tombstones[d.ID] = prevTomb
	}
}

// Enroll adds a device to an account, or rotates the token of one the account already has (a device
// that was signed out enrolls again this way). A deviceId enrolled in another account (or revoked from
// one) is ErrIDCollision; an account at its device limit is ErrLimit. The token is 32 random bytes,
// base64url without padding, and only its SHA-256 is stored.
func (s *Store) Enroll(spec EnrollSpec) (Enrolled, error) {
	token := wire.NewTokenFrom(s.opt.Rand)
	s.mu.Lock()
	defer s.mu.Unlock()
	d, old, prevTomb, err := s.addDeviceLocked(spec, token)
	if err != nil {
		return Enrolled{}, err
	}
	if err := s.saveDevicesLocked(); err != nil {
		s.undoDeviceLocked(d, old, prevTomb)
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
	// HelloSignedOut: the token is the device's own but died through a password change or a reset
	// (19 item AT). The answer is told only to a holder of the dead token.
	HelloSignedOut
)

// Hello checks a device's token: the SHA-256 of the presented token against the stored hash, in
// constant time. An unknown deviceId runs a comparison against a dummy hash, so the time does not
// tell a deviceId that exists from one that does not. A token that is not the stored one but IS the
// hash of a revoked device is HelloRevoked - and only then, so the reason cannot be used to learn
// that a deviceId ever existed. The dead token of a signed-out device is HelloSignedOut in the same way.
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
		if d.SignedOutAt != "" {
			return Device{ID: d.ID, Account: d.Account}, HelloSignedOut
		}
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
	// the dead token of a signed-out device is forgotten like a revoked one (19 item AT): the device
	// stays, its next `hello` is bad-credentials and it enrolls again
	for _, d := range s.devices {
		if d.SignedOutAt == "" {
			continue
		}
		at, err := wire.ParseTime(d.SignedOutAt)
		if err != nil || now.Sub(at) > s.opt.TombstoneLifetime {
			d.TokenHash, d.SignedOutAt = "", ""
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
