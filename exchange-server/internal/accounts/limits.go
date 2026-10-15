package accounts

// Limits are the caps one account is held to (DEVICE-EXCHANGE 19 item AS). For the four counts a zero is
// "no cap"; ClipboardBytes is the exception: 0 turns the clipboard off (22 item BZ), so a deployment that
// wants no clipboard cap sets a number large enough for its disk.
type Limits struct {
	Devices             int   // enrolled devices
	Resources           int   // published resources
	Broadcasts          int   // live broadcasts in the account
	BroadcastsPerDevice int   // live broadcasts one device may hold
	ClipboardBytes      int64 // the largest clipboard value of the account (22 item BZ)
}

// DefaultClipboardBytes is the clipboard limit a deployment starts with: 5 GiB, the one limit that is
// set by default (item AS).
const DefaultClipboardBytes int64 = 5 << 30

// DefaultMaxAccounts is the cap on the accounts of a deployment, 10 unless the operator sets another
// (item AS).
const DefaultMaxAccounts = 10

// Validate reports whether every limit that is set is a number of at least zero. A nil override is valid.
func (l *AccountLimits) Validate() error {
	if l == nil {
		return nil
	}
	for _, n := range []*int{l.DevicesPerAccount, l.ResourcesPerAccount, l.BroadcastsPerAccount, l.BroadcastsPerDevice} {
		if n != nil && *n < 0 {
			return ErrBadLimit
		}
	}
	if l.ClipboardMaxBytes != nil && *l.ClipboardMaxBytes < 0 {
		return ErrBadLimit
	}
	return nil
}

// IsEmpty reports whether the override sets nothing (a nil override sets nothing).
func (l *AccountLimits) IsEmpty() bool {
	return l == nil || (l.DevicesPerAccount == nil && l.ResourcesPerAccount == nil && l.BroadcastsPerAccount == nil &&
		l.BroadcastsPerDevice == nil && l.ClipboardMaxBytes == nil)
}

// Merge lays the members that are set in patch over l and returns the result; l is not changed.
func (l AccountLimits) Merge(patch *AccountLimits) AccountLimits {
	if patch == nil {
		return l
	}
	if patch.DevicesPerAccount != nil {
		l.DevicesPerAccount = patch.DevicesPerAccount
	}
	if patch.ResourcesPerAccount != nil {
		l.ResourcesPerAccount = patch.ResourcesPerAccount
	}
	if patch.BroadcastsPerAccount != nil {
		l.BroadcastsPerAccount = patch.BroadcastsPerAccount
	}
	if patch.BroadcastsPerDevice != nil {
		l.BroadcastsPerDevice = patch.BroadcastsPerDevice
	}
	if patch.ClipboardMaxBytes != nil {
		l.ClipboardMaxBytes = patch.ClipboardMaxBytes
	}
	return l
}

// Policy is what the operator sets for the whole deployment: the cap on accounts, and the limits a new
// account starts with. An account's own overrides (AccountLimits) sit on top of Defaults.
type Policy struct {
	MaxAccounts int
	Defaults    Limits
}

// DefaultPolicy is the working policy of a deployment whose operator set nothing: ten accounts, the
// deployment caps of ticket 167 and the 5 GiB clipboard.
func DefaultPolicy() Policy {
	return Policy{MaxAccounts: DefaultMaxAccounts, Defaults: Limits{
		Devices: 64, Resources: 256, Broadcasts: 16, BroadcastsPerDevice: 4, ClipboardBytes: DefaultClipboardBytes,
	}}
}

// Resolve lays one account's overrides over the defaults.
func (p Policy) Resolve(o *AccountLimits) Limits {
	l := p.Defaults
	if o == nil {
		return l
	}
	if o.DevicesPerAccount != nil {
		l.Devices = *o.DevicesPerAccount
	}
	if o.ResourcesPerAccount != nil {
		l.Resources = *o.ResourcesPerAccount
	}
	if o.BroadcastsPerAccount != nil {
		l.Broadcasts = *o.BroadcastsPerAccount
	}
	if o.BroadcastsPerDevice != nil {
		l.BroadcastsPerDevice = *o.BroadcastsPerDevice
	}
	if o.ClipboardMaxBytes != nil {
		l.ClipboardBytes = *o.ClipboardMaxBytes
	}
	return l
}

// Policy returns the policy in force.
func (s *Store) Policy() Policy {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.policy
}

// SetPolicy replaces the policy. It changes what the next request is checked against and never trims
// what an account already holds (item AS: a limit lowered below the holdings refuses only the next
// request). A zero MaxAccounts is the default of 10.
func (s *Store) SetPolicy(p Policy) {
	if p.MaxAccounts <= 0 {
		p.MaxAccounts = DefaultMaxAccounts
	}
	s.mu.Lock()
	s.policy = p
	s.mu.Unlock()
}

// LimitsFor resolves the limits of one account (the defaults for a login that is no account).
func (s *Store) LimitsFor(login string) Limits {
	login, _ = NormalizeLogin(login)
	s.mu.RLock()
	defer s.mu.RUnlock()
	var o *AccountLimits
	if a := s.accounts[login]; a != nil {
		o = a.Limits
	}
	return s.policy.Resolve(o)
}

// AccountCount is how many accounts the deployment holds.
func (s *Store) AccountCount() int {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return len(s.accounts)
}
