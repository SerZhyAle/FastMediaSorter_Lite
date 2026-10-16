package accounts

import (
	"errors"
	"sort"
	"strings"

	"fms/exchange-server/internal/wire"
)

// GrantsFile is the file of grants in the data directory.
const GrantsFile = "grants.json"

// Errors for grants.
var (
	ErrNoGrant        = errors.New("accounts: no such grant")
	ErrGrantHeld      = errors.New("accounts: grant already exists")
	ErrNotGuest       = errors.New("accounts: the account given to is not a guest account")
	ErrNotMember      = errors.New("accounts: only a member account gives")
	ErrBadGrantTarget = errors.New("accounts: a grant gives a resource or a broadcast")
	// ErrGrantID is a grantId already held by another grant (of another account, or of the same account for
	// another target or guest): a `grant` that names it is not the replacement of item DJ.
	ErrGrantID = errors.New("accounts: the grantId is held by another grant")
)

// What a grant gives: the Kind of a Grant.
const (
	GrantResource  = "resource"
	GrantBroadcast = "broadcast"
)

// Grant gives ONE resource or ONE broadcast of a member account to ONE guest account (DEVICE-EXCHANGE 19
// item AO, 20 item BD). The grant is the guest's whole view of the account that gave it.
type Grant struct {
	GrantID   string `json:"grantId,omitempty"` // the id the giving device chose (item BD); an older file's rows get one on load
	TargetID  string `json:"targetId"`          // resourceId or broadcastId
	Kind      string `json:"kind,omitempty"`    // GrantResource or GrantBroadcast
	Account   string `json:"account"`           // the guest account's login
	GrantedBy string `json:"grantedBy"`         // the giving account's login
	GrantedAt string `json:"grantedAt"`
}

// String never prints members.
func (g Grant) String() string { return "accounts.Grant" }

type grantsFile struct {
	StoreVersion int     `json:"storeVersion"`
	Grants       []Grant `json:"grants"`
}

// grantKey includes the giver: two accounts may hold a target of the same id (a broadcast id is unique
// per deployment, a resource id per account), and a grant of one must never be taken for the other's.
func grantKey(owner, targetID, guestAccount string) string {
	return strings.ToLower(strings.TrimSpace(owner)) + "\x00" + targetID + "\x00" + strings.ToLower(strings.TrimSpace(guestAccount))
}

func (s *Store) saveGrantsLocked() error {
	f := grantsFile{
		StoreVersion: StoreVersion,
		Grants:       make([]Grant, 0, len(s.grants)),
	}
	for _, g := range s.grants {
		f.Grants = append(f.Grants, *g)
	}
	sort.Slice(f.Grants, func(i, j int) bool {
		a, b := f.Grants[i], f.Grants[j]
		if a.Account != b.Account {
			return a.Account < b.Account
		}
		if a.GrantedBy != b.GrantedBy {
			return a.GrantedBy < b.GrantedBy
		}
		return a.TargetID < b.TargetID
	})
	return s.writeAtomic(GrantsFile, f)
}

// AddGrant gives a resource or a broadcast (kind) of the member account `owner` to the guest account
// `guest`. The giver must be a member and the receiver a guest of this deployment; whether the target
// exists is the directory's to say, the store only records who may see what.
func (s *Store) AddGrant(owner, targetID, kind, guest string) error {
	_, _, err := s.putGrant(owner, wire.NewID(), targetID, kind, guest, false)
	return err
}

// PutGrant is the `grant` of a device (item BD, amended by item DJ): it records a new grant under the
// grantId the device chose, or - when the giving account already holds that grantId for the same target and
// guest - finds the grant it replaces (replaced is true) and changes nothing in the store: a grant's box is
// not the store's. A grantId held by another grant, or a target and guest already given under another
// grantId, is ErrGrantID.
func (s *Store) PutGrant(owner, grantID, targetID, kind, guest string) (g Grant, replaced bool, err error) {
	return s.putGrant(owner, grantID, targetID, kind, guest, true)
}

func (s *Store) putGrant(owner, grantID, targetID, kind, guest string, byDevice bool) (Grant, bool, error) {
	owner, ok := NormalizeLogin(owner)
	if !ok {
		return Grant{}, false, ErrNoAccount
	}
	guest, ok = NormalizeLogin(guest)
	if !ok {
		return Grant{}, false, ErrBadLogin
	}
	if kind != GrantResource && kind != GrantBroadcast || !wire.ValidID(targetID) || !wire.ValidID(grantID) {
		return Grant{}, false, ErrBadGrantTarget
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	o, g := s.accounts[owner], s.accounts[guest]
	if o == nil || g == nil {
		return Grant{}, false, ErrNoAccount
	}
	if o.Kind == KindGuest {
		return Grant{}, false, ErrNotMember
	}
	if g.Kind != KindGuest {
		return Grant{}, false, ErrNotGuest
	}
	key := grantKey(owner, targetID, guest)
	if held := s.grants[key]; held != nil {
		switch {
		case !byDevice:
			return Grant{}, false, ErrGrantHeld
		case held.GrantID == grantID && held.Kind == kind:
			return *held, true, nil
		}
		return Grant{}, false, ErrGrantID
	}
	for _, other := range s.grants {
		if other.GrantID == grantID {
			return Grant{}, false, ErrGrantID
		}
	}
	made := &Grant{GrantID: grantID, TargetID: targetID, Kind: kind, Account: guest, GrantedBy: owner, GrantedAt: wire.FormatTime(s.now())}
	s.grants[key] = made
	if err := s.saveGrantsLocked(); err != nil {
		delete(s.grants, key)
		return Grant{}, false, err
	}
	return *made, false, nil
}

// GrantByID finds a grant of one giving account by the grantId (item BD).
func (s *Store) GrantByID(owner, grantID string) (Grant, bool) {
	owner, ok := NormalizeLogin(owner)
	if !ok {
		return Grant{}, false
	}
	s.mu.RLock()
	defer s.mu.RUnlock()
	for _, g := range s.grants {
		if g.GrantedBy == owner && g.GrantID == grantID {
			return *g, true
		}
	}
	return Grant{}, false
}

// RemoveGrantByID takes a grant back by its grantId and returns it; ErrNoGrant when the account holds none.
func (s *Store) RemoveGrantByID(owner, grantID string) (Grant, error) {
	g, ok := s.GrantByID(owner, grantID)
	if !ok {
		return Grant{}, ErrNoGrant
	}
	return s.RemoveGrant(g.GrantedBy, g.TargetID, g.Account)
}

// RemoveGrant takes a grant back and returns it (so the directory can tell the guest what it lost).
func (s *Store) RemoveGrant(owner, targetID, guest string) (Grant, error) {
	owner, ok := NormalizeLogin(owner)
	if !ok {
		return Grant{}, ErrNoGrant
	}
	guest, ok = NormalizeLogin(guest)
	if !ok {
		return Grant{}, ErrNoGrant
	}
	key := grantKey(owner, targetID, guest)
	s.mu.Lock()
	defer s.mu.Unlock()
	old := s.grants[key]
	if old == nil {
		return Grant{}, ErrNoGrant
	}
	delete(s.grants, key)
	if err := s.saveGrantsLocked(); err != nil {
		s.grants[key] = old
		return Grant{}, err
	}
	return *old, nil
}

// GrantsForGuest lists every grant given to a guest account, by giver and target.
func (s *Store) GrantsForGuest(guestAccount string) []Grant {
	guest, ok := NormalizeLogin(guestAccount)
	if !ok {
		return nil
	}
	s.mu.RLock()
	defer s.mu.RUnlock()
	var out []Grant
	for _, g := range s.grants {
		if g.Account == guest {
			out = append(out, *g)
		}
	}
	sort.Slice(out, func(i, j int) bool {
		if out[i].GrantedBy != out[j].GrantedBy {
			return out[i].GrantedBy < out[j].GrantedBy
		}
		return out[i].TargetID < out[j].TargetID
	})
	return out
}

// GrantsBy lists what an owner account gave to anyone, by guest and target.
func (s *Store) GrantsBy(owner string) []Grant {
	owner, ok := NormalizeLogin(owner)
	if !ok {
		return nil
	}
	s.mu.RLock()
	defer s.mu.RUnlock()
	var out []Grant
	for _, g := range s.grants {
		if g.GrantedBy == owner {
			out = append(out, *g)
		}
	}
	sort.Slice(out, func(i, j int) bool {
		if out[i].Account != out[j].Account {
			return out[i].Account < out[j].Account
		}
		return out[i].TargetID < out[j].TargetID
	})
	return out
}

// GuestsOf lists the guest accounts that hold a grant of one target of an owner.
func (s *Store) GuestsOf(owner, target string) []string {
	owner = strings.ToLower(strings.TrimSpace(owner))
	s.mu.RLock()
	defer s.mu.RUnlock()
	var out []string
	for _, g := range s.grants {
		if g.TargetID == target && g.GrantedBy == owner {
			out = append(out, g.Account)
		}
	}
	sort.Strings(out)
	return out
}

// GrantsOf lists the grants of one target of an owner, by guest.
func (s *Store) GrantsOf(owner, target string) []Grant {
	owner = strings.ToLower(strings.TrimSpace(owner))
	s.mu.RLock()
	defer s.mu.RUnlock()
	var out []Grant
	for _, g := range s.grants {
		if g.TargetID == target && g.GrantedBy == owner {
			out = append(out, *g)
		}
	}
	sort.Slice(out, func(i, j int) bool { return out[i].Account < out[j].Account })
	return out
}

// GrantFor finds the grant of one target to one guest, whoever gave it. A resource id is unique only
// inside its account, so when two givers hold the same id the one that sorts first answers - which is
// why a guest's connect by resourceId is only as sharp as the id space; a broadcast id is unique.
func (s *Store) GrantFor(targetID, guestAccount string) (Grant, bool) {
	guest, ok := NormalizeLogin(guestAccount)
	if !ok {
		return Grant{}, false
	}
	s.mu.RLock()
	defer s.mu.RUnlock()
	var best *Grant
	for _, g := range s.grants {
		if g.TargetID == targetID && g.Account == guest && (best == nil || g.GrantedBy < best.GrantedBy) {
			best = g
		}
	}
	if best == nil {
		return Grant{}, false
	}
	return *best, true
}

// HasGrant reports whether a target is given to a guest account.
func (s *Store) HasGrant(targetID, guestAccount string) bool {
	_, ok := s.GrantFor(targetID, guestAccount)
	return ok
}

// removeGrantsLocked drops every grant made by or made to login and reports whether any went. The caller
// writes grants.json.
func (s *Store) removeGrantsLocked(login string) bool {
	login = strings.ToLower(strings.TrimSpace(login))
	if login == "" {
		return false
	}
	changed := false
	for k, g := range s.grants {
		if g.Account == login || g.GrantedBy == login {
			delete(s.grants, k)
			changed = true
		}
	}
	return changed
}
