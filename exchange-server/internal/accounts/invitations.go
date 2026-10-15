package accounts

import (
	"crypto/sha256"
	"encoding/base64"
	"encoding/hex"
	"errors"
	"io"
	"sort"
	"strings"

	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/wire"
)

// InvitationsFile is the file of invitations in the data directory.
const InvitationsFile = "invitations.json"

// ErrInvitationInvalid is every way an invitation can fail to open a registration: an unknown, a used
// or a withdrawn one-time code, a wrong shared password, a shared password while it is off. They are one
// error because they are one answer on the wire (`invitation-invalid`, 19 item AQ): the answer must not
// tell a used code from one that never existed.
var ErrInvitationInvalid = errors.New("accounts: the invitation does not open a registration")

// Invitation is one stored one-time invitation. The plaintext code is never stored, only its SHA-256
// (19 item AP); the record survives its use as a used entry so the operator's list shows what became of it.
type Invitation struct {
	CodeHash  string `json:"codeHash"` // hex SHA-256 of the 22-character code
	Kind      string `json:"kind"`     // KindMember or KindGuest
	CreatedAt string `json:"createdAt"`
	// ExpectedLogin is the login the operator expects the person to take - a hint written into the
	// invitation string as `u`, never enforced (19 items AP and AR).
	ExpectedLogin string `json:"expectedLogin,omitempty"`
	Used          bool   `json:"used"`
	UsedAt        string `json:"usedAt,omitempty"`
	UsedBy        string `json:"usedBy,omitempty"`
}

// String never prints members.
func (i Invitation) String() string { return "accounts.Invitation" }

type invitationsFile struct {
	StoreVersion       int          `json:"storeVersion"`
	SharedPasswordHash string       `json:"sharedPasswordHash,omitempty"`
	SharedKind         string       `json:"sharedKind,omitempty"`
	Invitations        []Invitation `json:"invitations"`
}

// NormalizeInvitationKind is NormalizeKind for an invitation: the kind of the account it registers.
func NormalizeInvitationKind(k string) (string, error) { return NormalizeKind(k) }

// InvitationHash is the SHA-256 of an invitation code as the log and the store know it. A log line
// carries only the first 8 hex characters of it (DEVICE-EXCHANGE 7.9).
func InvitationHash(code string) string {
	sum := sha256.Sum256([]byte(strings.TrimSpace(code)))
	return hex.EncodeToString(sum[:])
}

func (s *Store) saveInvitationsLocked() error {
	f := invitationsFile{
		StoreVersion:       StoreVersion,
		SharedPasswordHash: s.sharedPasswordHash,
		SharedKind:         s.sharedKind,
		Invitations:        make([]Invitation, 0, len(s.invitations)),
	}
	for _, inv := range s.invitations {
		f.Invitations = append(f.Invitations, *inv)
	}
	sort.Slice(f.Invitations, func(i, j int) bool {
		if f.Invitations[i].CreatedAt != f.Invitations[j].CreatedAt {
			return f.Invitations[i].CreatedAt < f.Invitations[j].CreatedAt
		}
		return f.Invitations[i].CodeHash < f.Invitations[j].CodeHash
	})
	return s.writeAtomic(InvitationsFile, f)
}

// CreateInvitation makes a one-time invitation: 16 random bytes in the share id form. It has no expiry -
// it lives until it registers one account or the operator withdraws it. Only its SHA-256 and kind are
// stored; the code is returned once. expectedLogin is optional and, when given, must be a login of 5.1.
func (s *Store) CreateInvitation(kind, expectedLogin string) (code string, inv Invitation, err error) {
	k, err := NormalizeKind(kind)
	if err != nil {
		return "", Invitation{}, err
	}
	expected := ""
	if strings.TrimSpace(expectedLogin) != "" {
		l, ok := NormalizeLogin(expectedLogin)
		if !ok {
			return "", Invitation{}, ErrBadLogin
		}
		expected = l
	}
	var raw [16]byte
	if _, err := io.ReadFull(s.opt.Rand, raw[:]); err != nil {
		return "", Invitation{}, err
	}
	code = base64.RawURLEncoding.EncodeToString(raw[:])
	hash := InvitationHash(code)

	s.mu.Lock()
	defer s.mu.Unlock()
	inv = Invitation{CodeHash: hash, Kind: k, CreatedAt: wire.FormatTime(s.now()), ExpectedLogin: expected}
	s.invitations[hash] = &inv
	if err := s.saveInvitationsLocked(); err != nil {
		delete(s.invitations, hash)
		return "", Invitation{}, err
	}
	return code, inv, nil
}

// ListInvitations lists every invitation, oldest first. The plaintext code is never part of it.
func (s *Store) ListInvitations() []Invitation {
	s.mu.RLock()
	defer s.mu.RUnlock()
	out := make([]Invitation, 0, len(s.invitations))
	for _, inv := range s.invitations {
		out = append(out, *inv)
	}
	sort.Slice(out, func(i, j int) bool {
		if out[i].CreatedAt != out[j].CreatedAt {
			return out[i].CreatedAt < out[j].CreatedAt
		}
		return out[i].CodeHash < out[j].CodeHash
	})
	return out
}

// WithdrawInvitation removes an invitation named by its hash or by an unambiguous prefix of it (the
// list shows eight characters). It reports whether one was removed. An ambiguous prefix removes nothing.
func (s *Store) WithdrawInvitation(codeHashOrPrefix string) (bool, error) {
	p := strings.ToLower(strings.TrimSpace(codeHashOrPrefix))
	if len(p) < 4 {
		return false, nil
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	var target string
	for h := range s.invitations {
		if h == p || strings.HasPrefix(h, p) {
			if target != "" {
				return false, nil
			}
			target = h
		}
	}
	if target == "" {
		return false, nil
	}
	inv := s.invitations[target]
	delete(s.invitations, target)
	if err := s.saveInvitationsLocked(); err != nil {
		s.invitations[target] = inv
		return false, err
	}
	return true, nil
}

// SetSharedInvitation turns the shared invitation password on (pw is under the password policy; kind is
// the kind it registers) or off (pw == ""). Changing or turning it off invalidates the old one at once
// and touches no account already registered; one-time codes keep working either way (item AP).
func (s *Store) SetSharedInvitation(pw, kind string) error {
	k, err := NormalizeKind(kind)
	if err != nil {
		return err
	}
	var rec string
	if pw != "" {
		rec, err = s.HashPassword(pw)
		if err != nil {
			return err
		}
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	oldHash, oldKind := s.sharedPasswordHash, s.sharedKind
	s.sharedPasswordHash, s.sharedKind = rec, k
	if rec == "" {
		s.sharedKind = ""
	}
	if err := s.saveInvitationsLocked(); err != nil {
		s.sharedPasswordHash, s.sharedKind = oldHash, oldKind
		return err
	}
	return nil
}

// SharedInvitationStatus reports whether a shared invitation password is on and the kind it registers.
func (s *Store) SharedInvitationStatus() (enabled bool, kind string) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.sharedPasswordHash != "", s.sharedKind
}

// OneTimeInvitation looks a typed one-time code up WITHOUT using it: the kind it would register, or
// ErrInvitationInvalid for an unknown, a used and a withdrawn one alike. It is the first step of a
// registration; the invitation is consumed only by RegisterAccount, so a registration that fails later
// consumes nothing (item AQ).
func (s *Store) OneTimeInvitation(code string) (kind string, err error) {
	h := InvitationHash(code)
	s.mu.RLock()
	defer s.mu.RUnlock()
	inv := s.invitations[h]
	if inv == nil || inv.Used {
		return "", ErrInvitationInvalid
	}
	return inv.Kind, nil
}

// VerifySharedInvitation checks a typed shared invitation password. With the shared password off it
// still runs one verification against the dummy record, so the time does not tell "off" from "wrong";
// both are ErrInvitationInvalid. It is memory-hard, so a caller that serves a stream runs it inside
// the bound on concurrent verifications.
func (s *Store) VerifySharedInvitation(pw string) (kind string, err error) {
	auth, err := s.AuthorizeSharedInvitation(pw)
	if err != nil {
		return "", err
	}
	return auth.kind, nil
}

// SharedInvitationAuthorization binds a successful password check to the exact shared invitation
// setting checked. RegisterAccount rechecks it under its write lock, so switching the setting off or
// replacing it invalidates even a registration that is already hashing its account password.
type SharedInvitationAuthorization struct {
	store  *Store
	record string
	kind   string
}

func (a *SharedInvitationAuthorization) String() string {
	return "accounts.SharedInvitationAuthorization"
}

// Kind is the account kind proven by this password check.
func (a *SharedInvitationAuthorization) Kind() string { return a.kind }

// AuthorizeSharedInvitation verifies the password inside the caller's memory-hard work bound.
func (s *Store) AuthorizeSharedInvitation(pw string) (*SharedInvitationAuthorization, error) {
	s.mu.RLock()
	rec, k := s.sharedPasswordHash, s.sharedKind
	s.mu.RUnlock()
	on := rec != ""
	if !on {
		rec = s.dummy
	}
	ok := password.Verify(rec, pw)
	if !ok || !on {
		return nil, ErrInvitationInvalid
	}
	if k == "" {
		k = KindMember
	}
	return &SharedInvitationAuthorization{store: s, record: rec, kind: k}, nil
}

// RegisterSpec is a registration that has passed every check of 19 item AQ except the last write.
type RegisterSpec struct {
	Login string // normalised, of the form of 5.1
	// PasswordRecord is the Argon2id record of the chosen password (HashPassword).
	PasswordRecord string
	Kind           string
	// InvitationCode is the one-time code to consume; empty for a registration by the shared password.
	InvitationCode string
	// SharedInvitation is required when no one-time code is supplied. Its fields cannot be forged by
	// callers outside this package; the account kind comes from the authorization, not the request.
	SharedInvitation *SharedInvitationAuthorization
	Device           EnrollSpec // Account is filled by RegisterAccount
}

// RegisterAccount creates the account, enrolls its first device and consumes the one-time invitation as ONE
// step: all of it is applied or none of it is (item AQ - "created together or not at all"). The checks
// the engine made first are repeated under the lock, because another stream may have won in between:
//
//	ErrInvitationInvalid  the one-time code was used/withdrawn, or the shared setting changed meanwhile
//	ErrLimit              the cap on accounts was reached meanwhile
//	ErrExists             the login was taken meanwhile
//	ErrIDCollision        the deviceId is enrolled in, or was revoked from, another account
//
// Nothing is consumed on any of them.
func (s *Store) RegisterAccount(spec RegisterSpec) (Enrolled, error) {
	k, err := NormalizeKind(spec.Kind)
	if err != nil {
		return Enrolled{}, err
	}
	token := wire.NewTokenFrom(s.opt.Rand)
	s.mu.Lock()
	defer s.mu.Unlock()

	var inv *Invitation
	if spec.InvitationCode != "" {
		inv = s.invitations[InvitationHash(spec.InvitationCode)]
		if inv == nil || inv.Used {
			return Enrolled{}, ErrInvitationInvalid
		}
		k = inv.Kind
	} else {
		auth := spec.SharedInvitation
		if auth == nil || auth.store != s || auth.record == "" ||
			auth.record != s.sharedPasswordHash || auth.kind != s.sharedKind {
			return Enrolled{}, ErrInvitationInvalid
		}
		k = auth.kind
	}
	if len(s.accounts) >= s.policy.MaxAccounts {
		return Enrolled{}, ErrLimit
	}
	if s.accounts[spec.Login] != nil {
		return Enrolled{}, ErrExists
	}
	now := wire.FormatTime(s.now())
	acct := &Account{Login: spec.Login, PasswordHash: spec.PasswordRecord, CreatedAt: now, Kind: k}
	s.accounts[spec.Login] = acct
	dev := spec.Device
	dev.Account = spec.Login
	d, old, prevTomb, err := s.addDeviceLocked(dev, token)
	if err != nil {
		delete(s.accounts, spec.Login)
		return Enrolled{}, err
	}
	var usedBefore Invitation
	if inv != nil {
		usedBefore = *inv
		inv.Used, inv.UsedAt, inv.UsedBy = true, now, spec.Login
	}
	// roll everything back, in memory and in the files already written, if any write fails
	rollback := func() {
		delete(s.accounts, spec.Login)
		s.undoDeviceLocked(d, old, prevTomb)
		if inv != nil {
			*inv = usedBefore
		}
		_ = s.saveAccountsLocked()
		_ = s.saveDevicesLocked()
		if inv != nil {
			_ = s.saveInvitationsLocked()
		}
	}
	if err := s.saveAccountsLocked(); err != nil {
		rollback()
		return Enrolled{}, err
	}
	if err := s.saveDevicesLocked(); err != nil {
		rollback()
		return Enrolled{}, err
	}
	if inv != nil {
		if err := s.saveInvitationsLocked(); err != nil {
			rollback()
			return Enrolled{}, err
		}
	}
	return Enrolled{Device: *d, Token: token}, nil
}

// forgetInvitationsOfLocked erases the used-invitation records that name a login: an account that is
// deleted leaves nothing behind, and its login becomes free (19 item AU). It reports whether any went.
func (s *Store) forgetInvitationsOfLocked(login string) bool {
	changed := false
	for h, inv := range s.invitations {
		if inv.UsedBy == login {
			delete(s.invitations, h)
			changed = true
		}
	}
	return changed
}
