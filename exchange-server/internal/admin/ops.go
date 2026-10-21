// Package admin is how an operator changes accounts and devices: the operations themselves (Ops),
// which the running server and the offline command line share, and the local channel by which a
// second program reaches a running server (ticket 162 section 4, Q162-1).
//
// Ops works on the accounts store and the directory only. Live, the engine has wired the directory's
// hooks, so a revoke closes the device's streams and releases its ports; offline, there are no
// streams and the same calls just edit the two stores, and the command flushes the directory after.
package admin

import (
	"errors"
	"sort"
	"sync"
	"time"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/clipboard"
	"fms/exchange-server/internal/directory"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/wire"
)

// Clipboard is what the operator's operations touch of the clipboard slots (ticket 229, spec 246): the
// erasure that goes with an account removal and with an account becoming a guest, the counts
// of the status, and metadata inspection. Nil for an offline command line, which holds no slots of its own.
type Clipboard interface {
	Erase(account string)
	Counts() (values int, bytes int64)
	Info(account string) *clipboard.ClipboardSlotView
}

// Ops are the operator's operations.
type Ops struct {
	Accounts *accounts.Store
	Dir      *directory.Dir
	Log      *logx.Logger
	// Live is true only for the Ops of a running engine. What lives in the memory of the engine's store (the
	// pairing codes) is meaningless to an offline command that opens a store of its own, so those operations
	// refuse there instead of handing out a code that dies with the process.
	Live             bool
	KeepAwakeActive  bool
	SaveHostSettings func(bool, int) error
	ReadHostSettings func() (bool, int)
	// SaveLimits writes the deployment-wide policy (the cap on accounts and the defaults a new account
	// starts with) where the server reads it at start, exchange.json. Nil: the policy is kept for this run
	// only, and the answer says so.
	SaveLimits func(accounts.Policy) error
	// Clipboard is the running server's clipboard table (ticket 229); nil offline.
	Clipboard Clipboard
	// Audit is the in-memory audit tracker for admin sessions and change history (spec 246 Group D).
	Audit    *AuditTracker
	AuditLog *logx.Audit
	auditMu  sync.Mutex
	// Sealed is true when the running server advertises `sealed` (DEVICE-EXCHANGE 27 item DP, ticket 234): a
	// surface that shows the operator statement then shows the honest text (internal/statement) instead of
	// the points of a server that cannot seal. A flag, never a key or a box.
	Sealed bool
}

// ErrNotLive is the refusal of an operation that needs the running server.
var ErrNotLive = errors.New("this operation needs the running server")

// ErrBadPolicy is a deployment policy that is not at least one account and limits of at least zero.
var ErrBadPolicy = errors.New("the cap on accounts is at least 1 and every limit at least 0")

// ErrNoTarget is a grant that names a resource or a broadcast the giving account does not hold.
var ErrNoTarget = errors.New("the account holds no such resource or broadcast")

// PairingCodeView is what the operator gets: the code, once, and when it stops being valid.
type PairingCodeView struct {
	Code      string `json:"code"`
	ExpiresAt string `json:"expiresAt"`
}

// PairingCode issues a single-use pairing code for an account (ten minutes, five outstanding at most). The
// log gets the account's short hash and never the code.
func (o *Ops) PairingCode(login string) (string, time.Time, error) {
	if !o.Live {
		return "", time.Time{}, ErrNotLive
	}
	l, ok := accounts.NormalizeLogin(login)
	if !ok || !o.Accounts.Exists(l) {
		return "", time.Time{}, accounts.ErrNoAccount
	}
	code, expires, err := o.Accounts.NewPairingCode(l)
	if err != nil {
		return "", time.Time{}, err
	}
	o.Log.State("pairing-code-issued", logx.Login(l))
	return code, expires, nil
}

// AddAccount creates a member account under the password policy and returns its normalised login.
func (o *Ops) AddAccount(login, pw string) (string, error) {
	l, _, err := o.AddAccountKind(login, pw, accounts.KindMember)
	return l, err
}

// AddAccountKind creates an account of a kind. The operator is never refused for the cap of 19 item AS
// (it binds `register`): overCap says that the count now exceeds it, so a surface can tell the operator.
func (o *Ops) AddAccountKind(login, pw, kind string) (l string, overCap bool, err error) {
	l, err = o.Accounts.AddAccountWithKind(login, pw, kind)
	if err != nil {
		return l, false, err
	}
	k := o.Accounts.KindOf(l)
	o.Log.State("account-added", logx.Login(l), logx.Text("kind", k))
	return l, o.Accounts.AccountCount() > o.Accounts.Policy().MaxAccounts, nil
}

// SetPassword is the operator's reset: a new password, and EVERY device of the account signed out, no
// device spared (19 item AT). The devices stay; their streams close and their next `hello` is answered
// `signed-out`. It reports how many devices were signed out.
func (o *Ops) SetPassword(login, pw string) (int, error) {
	l, ok := accounts.NormalizeLogin(login)
	if !ok || !o.Accounts.Exists(l) {
		return 0, accounts.ErrNoAccount
	}
	ids, err := o.Accounts.SetPassword(l, pw)
	if err != nil {
		return 0, err
	}
	for _, id := range ids {
		o.Dir.SignOutDevice(l, id)
	}
	o.Log.State("password-reset", logx.Login(l), logx.Int("signedOut", len(ids)))
	return len(ids), nil
}

// SetKind changes the kind of an account (19 item AO). An account that becomes a guest stops offering: what
// it published is withdrawn at once and what it gave goes; an account that becomes a member loses what it
// was given. Every guest concerned is told through its directory stream.
func (o *Ops) SetKind(login, kind string) (changed bool, err error) {
	l, ok := accounts.NormalizeLogin(login)
	if !ok || !o.Accounts.Exists(l) {
		return false, accounts.ErrNoAccount
	}
	k, err := accounts.NormalizeKind(kind)
	if err != nil {
		return false, err
	}
	if o.Accounts.KindOf(l) == k {
		return false, nil
	}
	received := o.Accounts.GrantsForGuest(l)
	if k == accounts.KindGuest {
		o.Dir.OwnerGone(l) // the guests of this account are told before the store forgets who they are
	}
	changed, err = o.Accounts.SetKind(l, k)
	if err != nil || !changed {
		return changed, err
	}
	if k == accounts.KindGuest {
		o.Dir.WithdrawAll(l)
		// a guest holds no clipboard slot: erased when the kind becomes a guest (22 item BU)
		if o.Clipboard != nil {
			o.Clipboard.Erase(l)
		}
	}
	for _, g := range received {
		o.Dir.GrantRemoved(DirectoryGrant(g), true)
	}
	o.Log.State("account-kind-changed", logx.Login(l), logx.Text("kind", k))
	return true, nil
}

// RemoveAccount deletes an account: every device is revoked in one go, its records and streams go, and
// every guest that was given something of it is told. A person's own deletion ends in the same place (19
// item AU), so the two can never erase different sets.
func (o *Ops) RemoveAccount(login string) error {
	l, ok := accounts.NormalizeLogin(login)
	if !ok {
		return accounts.ErrNoAccount
	}
	if !o.Accounts.Exists(l) {
		return accounts.ErrNoAccount
	}
	received := o.Accounts.GrantsForGuest(l)
	o.Dir.OwnerGone(l)
	_, err := o.Accounts.RemoveAccount(l)
	if errors.Is(err, accounts.ErrNoAccount) {
		return err
	}
	// even when the store could not be written the account is gone from memory: take it out of the directory too
	o.Dir.RemoveAccount(l)
	// the clipboard is erased with the account, the same code path for the holder's deletion and
	// the operator's removal (22 item BU, ticket 229 rule 8)
	if o.Clipboard != nil {
		o.Clipboard.Erase(l)
	}
	for _, g := range received {
		o.Dir.GrantRemoved(DirectoryGrant(g), true)
	}
	if err != nil {
		return err
	}
	o.Log.State("account-removed", logx.Login(l))
	return nil
}

// RevokeDevice kills a device's token and removes it from the directory (162 rule 7). It reports
// whether the account had the device; a device of another account is not found, so a revoke can
// never reach across accounts.
func (o *Ops) RevokeDevice(login, deviceID string) (bool, error) {
	l, ok := accounts.NormalizeLogin(login)
	if !ok {
		return false, nil
	}
	_, found, err := o.Accounts.Revoke(l, deviceID)
	if err != nil {
		return false, err
	}
	if !found {
		return false, nil
	}
	o.Dir.RevokeDevice(l, deviceID)
	o.Log.State("device-revoked", logx.Login(l), logx.Device(deviceID))
	return true, nil
}

// LimitsView is a set of limits as the operator sees them. For the four counts 0 is "no cap"; for
// clipboardBytes 0 turns the clipboard off (22 item BZ).
type LimitsView struct {
	Devices             int   `json:"devices"`
	Resources           int   `json:"resources"`
	Broadcasts          int   `json:"broadcasts"`
	BroadcastsPerDevice int   `json:"broadcastsPerDevice"`
	ClipboardBytes      int64 `json:"clipboardBytes"`
}

func limitsView(l accounts.Limits) LimitsView {
	return LimitsView{Devices: l.Devices, Resources: l.Resources, Broadcasts: l.Broadcasts,
		BroadcastsPerDevice: l.BroadcastsPerDevice, ClipboardBytes: l.ClipboardBytes}
}

// AccountView is what the operator sees of an account: never a record.
type AccountView struct {
	Login     string                  `json:"login"`
	CreatedAt string                  `json:"createdAt"`
	Devices   int                     `json:"devices"`
	Kind      string                  `json:"kind,omitempty"`
	Limits    *accounts.AccountLimits `json:"limits,omitempty"`    // the account's own overrides
	Effective LimitsView              `json:"effective,omitempty"` // what is in force: overrides over the defaults
}

// DeviceView is what the operator sees of a device: never a token or its hash.
type DeviceView struct {
	DeviceID       string   `json:"deviceId"`
	Name           string   `json:"deviceName"`
	Product        string   `json:"product"`
	ProductVersion string   `json:"productVersion,omitempty"`
	Platform       string   `json:"platform,omitempty"`
	Roles          []string `json:"roles,omitempty"`
	EnrolledAt     string   `json:"enrolledAt"`
	LastSeen       string   `json:"lastSeen,omitempty"`
	Online         bool     `json:"online"`
	// SignedOut is true while the device's token is dead through a password change or a reset (19 item AT):
	// the record stays until the operator or the account deletes it (item AV).
	SignedOut bool `json:"signedOut,omitempty"`
}

// ListAccounts lists the accounts by login.
func (o *Ops) ListAccounts() []AccountView {
	out := []AccountView{}
	for _, a := range o.Accounts.Accounts() {
		out = append(out, AccountView{
			Login: a.Login, CreatedAt: a.CreatedAt, Devices: a.Devices,
			Kind: a.Kind, Limits: a.Limits, Effective: limitsView(o.Accounts.LimitsFor(a.Login)),
		})
	}
	return out
}

// ListDevices lists an account's devices by id.
func (o *Ops) ListDevices(login string) ([]DeviceView, error) {
	l, ok := accounts.NormalizeLogin(login)
	if !ok || !o.Accounts.Exists(l) {
		return nil, accounts.ErrNoAccount
	}
	out := []DeviceView{}
	for _, d := range o.Accounts.Devices(l) {
		out = append(out, DeviceView{DeviceID: d.ID, Name: d.Name, Product: d.Product, ProductVersion: d.ProductVersion,
			Platform: d.Platform, Roles: d.Roles, EnrolledAt: d.EnrolledAt, LastSeen: d.LastSeen,
			Online: o.Dir.DeviceOnline(l, d.ID), SignedOut: d.SignedOutAt != ""})
	}
	sort.Slice(out, func(i, j int) bool { return out[i].DeviceID < out[j].DeviceID })
	return out, nil
}

// Status is what a status request may show: counts, never a list and never a secret.
type Status struct {
	Accounts         int    `json:"accounts"`
	MaxAccounts      int    `json:"maxAccounts"`
	Devices          int    `json:"devices"`
	Online           int    `json:"online"`
	Resources        int    `json:"resources"`
	Broadcasts       int    `json:"broadcasts"`
	Tombstones       int    `json:"tombstones"`
	Clipboards       int    `json:"clipboards"`     // accounts holding a clipboard value now (ticket 229)
	ClipboardBytes   int64  `json:"clipboardBytes"` // bytes the slots hold now: values and uploads in flight
	Version          string `json:"version,omitempty"`
	KeepAwakeActive  bool   `json:"keepAwakeActive"`
	KeepAwake        bool   `json:"keepAwake"`
	LogRetentionDays int    `json:"logRetentionDays"`
	Sealed           bool   `json:"sealed"` // the server advertises `sealed`: the honest statement applies (ticket 234)
}

// Status returns the totals.
func (o *Ops) Status() Status {
	c := o.Dir.Counts()
	s := Status{Accounts: o.Accounts.AccountCount(), MaxAccounts: o.Accounts.Policy().MaxAccounts,
		Devices: len(o.Accounts.AllDevices()), Online: c.Online,
		Resources: c.Resources, Broadcasts: c.Broadcasts, Tombstones: o.Accounts.Tombstones(), KeepAwakeActive: o.KeepAwakeActive, Sealed: o.Sealed}
	if o.Clipboard != nil {
		s.Clipboards, s.ClipboardBytes = o.Clipboard.Counts()
	}
	if o.ReadHostSettings != nil {
		s.KeepAwake, s.LogRetentionDays = o.ReadHostSettings()
	}
	return s
}

// DeviceIDOK reports whether s has the form of a deviceId (the CLI checks it before asking).
func DeviceIDOK(s string) bool { return wire.ValidID(s) }

// --- invitations (19 items AP and AQ) ---

// InvitationView is what the operator sees of an invitation: never the plaintext code.
type InvitationView struct {
	CodeHash      string `json:"codeHash"`
	Kind          string `json:"kind"`
	CreatedAt     string `json:"createdAt"`
	ExpectedLogin string `json:"expectedLogin,omitempty"`
	Used          bool   `json:"used"`
	UsedAt        string `json:"usedAt,omitempty"`
	UsedBy        string `json:"usedBy,omitempty"`
}

func invitationView(inv accounts.Invitation) InvitationView {
	return InvitationView{CodeHash: inv.CodeHash, Kind: inv.Kind, CreatedAt: inv.CreatedAt,
		ExpectedLogin: inv.ExpectedLogin, Used: inv.Used, UsedAt: inv.UsedAt, UsedBy: inv.UsedBy}
}

// CreateInvitationResult is returned once upon creation: the code, which the server keeps no copy of.
type CreateInvitationResult struct {
	Code       string         `json:"code"`
	Invitation InvitationView `json:"invitation"`
}

// SharedInvitationView is the status of the shared invitation password.
type SharedInvitationView struct {
	Enabled bool   `json:"enabled"`
	Kind    string `json:"kind,omitempty"`
}

// CreateInvitation creates a one-time invitation of a kind, with the login the operator expects the
// person to take (optional, a hint written into the invitation string as `u`, never enforced).
func (o *Ops) CreateInvitation(kind, expectedLogin string) (CreateInvitationResult, error) {
	code, inv, err := o.Accounts.CreateInvitation(kind, expectedLogin)
	if err != nil {
		return CreateInvitationResult{}, err
	}
	o.Log.State("invitation-created", logx.Text("kind", inv.Kind), logx.Invitation(code))
	return CreateInvitationResult{Code: code, Invitation: invitationView(inv)}, nil
}

// ListInvitations lists all invitations, oldest first.
func (o *Ops) ListInvitations() []InvitationView {
	invs := o.Accounts.ListInvitations()
	out := make([]InvitationView, 0, len(invs))
	for _, inv := range invs {
		out = append(out, invitationView(inv))
	}
	return out
}

// WithdrawInvitation deletes an invitation named by its hash or an unambiguous prefix of it.
func (o *Ops) WithdrawInvitation(codeHashOrPrefix string) (bool, error) {
	ok, err := o.Accounts.WithdrawInvitation(codeHashOrPrefix)
	if err != nil {
		return false, err
	}
	if ok {
		o.Log.State("invitation-withdrawn", logx.Text("hash", shortHash(codeHashOrPrefix)))
	}
	return ok, nil
}

func shortHash(h string) string {
	if len(h) > 8 {
		return h[:8]
	}
	return h
}

// SharedInvitationStatus returns whether the shared invitation password is on and the kind it registers.
func (o *Ops) SharedInvitationStatus() SharedInvitationView {
	enabled, kind := o.Accounts.SharedInvitationStatus()
	return SharedInvitationView{Enabled: enabled, Kind: kind}
}

// SetSharedInvitation sets the shared invitation password (under the policy of 1.2) and the kind it
// registers, or turns it off with an empty password. It touches no account already registered.
func (o *Ops) SetSharedInvitation(pw, kind string) error {
	if err := o.Accounts.SetSharedInvitation(pw, kind); err != nil {
		return err
	}
	if pw == "" {
		o.Log.State("shared-invitation-off")
	} else {
		_, k := o.Accounts.SharedInvitationStatus()
		o.Log.State("shared-invitation-set", logx.Text("kind", k))
	}
	return nil
}

// --- limits (19 item AS) ---

// SetAccountLimits sets the overrides of one account; nil clears them. A limit lowered below what the
// account holds refuses only the next request and trims nothing.
func (o *Ops) SetAccountLimits(login string, lim *accounts.AccountLimits) error {
	l, ok := accounts.NormalizeLogin(login)
	if !ok || !o.Accounts.Exists(l) {
		return accounts.ErrNoAccount
	}
	if err := o.Accounts.SetAccountLimits(l, lim); err != nil {
		return err
	}
	o.Log.State("account-limits-changed", logx.Login(l))
	return nil
}

// PolicyView is the deployment policy and how full it is.
type PolicyView struct {
	MaxAccounts int        `json:"maxAccounts"`
	Accounts    int        `json:"accounts"`
	Defaults    LimitsView `json:"defaults"`
	// Persisted is false when the change was made for this run only (nothing to write it to).
	Persisted bool `json:"persisted"`
}

// PolicyStatus is the policy in force.
func (o *Ops) PolicyStatus() PolicyView {
	p := o.Accounts.Policy()
	return PolicyView{MaxAccounts: p.MaxAccounts, Accounts: o.Accounts.AccountCount(), Defaults: limitsView(p.Defaults), Persisted: true}
}

// SetPolicy changes the cap on accounts and the defaults a new account starts with. Members left nil
// stay. It never trims what exists: a cap lowered below the count refuses only the next registration.
func (o *Ops) SetPolicy(maxAccounts *int, defaults *accounts.AccountLimits) (PolicyView, error) {
	cur := o.Accounts.Policy()
	next := cur
	if maxAccounts != nil {
		if *maxAccounts < 1 {
			return PolicyView{}, ErrBadPolicy
		}
		next.MaxAccounts = *maxAccounts
	}
	if err := defaults.Validate(); err != nil {
		return PolicyView{}, ErrBadPolicy
	}
	if defaults != nil {
		if defaults.DevicesPerAccount != nil {
			next.Defaults.Devices = *defaults.DevicesPerAccount
		}
		if defaults.ResourcesPerAccount != nil {
			next.Defaults.Resources = *defaults.ResourcesPerAccount
		}
		if defaults.BroadcastsPerAccount != nil {
			next.Defaults.Broadcasts = *defaults.BroadcastsPerAccount
		}
		if defaults.BroadcastsPerDevice != nil {
			next.Defaults.BroadcastsPerDevice = *defaults.BroadcastsPerDevice
		}
		if defaults.ClipboardMaxBytes != nil {
			next.Defaults.ClipboardBytes = *defaults.ClipboardMaxBytes
		}
	}
	persisted := true
	if o.SaveLimits != nil {
		if err := o.SaveLimits(next); err != nil {
			return PolicyView{}, err
		}
	} else {
		persisted = false
	}
	o.Accounts.SetPolicy(next)
	o.Log.State("policy-changed", logx.Int("maxAccounts", next.MaxAccounts))
	v := o.PolicyStatus()
	v.Persisted = persisted
	return v, nil
}

// --- grants (19 item AO, 20 item BD) ---

// GrantView is one thing given to a guest account.
type GrantView struct {
	GrantID   string `json:"grantId,omitempty"`
	TargetID  string `json:"targetId"`
	Kind      string `json:"kind"`
	Guest     string `json:"guest"`
	GrantedBy string `json:"grantedBy"`
	GrantedAt string `json:"grantedAt"`
}

func grantView(g accounts.Grant) GrantView {
	return GrantView{GrantID: g.GrantID, TargetID: g.TargetID, Kind: g.Kind, Guest: g.Account, GrantedBy: g.GrantedBy, GrantedAt: g.GrantedAt}
}

// DirectoryGrant is a grant of the accounts store in the shape the directory works with.
func DirectoryGrant(g accounts.Grant) directory.Grant {
	return directory.Grant{ID: g.GrantID, Owner: g.GrantedBy, Guest: g.Account, Target: g.TargetID, Broadcast: g.Kind == accounts.GrantBroadcast}
}

// heldTarget reports ErrNoTarget unless the giving account holds the resource, or the live broadcast, now.
func (o *Ops) heldTarget(owner, targetID, kind string) error {
	switch kind {
	case accounts.GrantResource:
		if _, held := o.Dir.ResourceRecord(owner, targetID); !held {
			return ErrNoTarget
		}
	case accounts.GrantBroadcast:
		if _, _, held := o.Dir.LiveBroadcast(owner, targetID); !held {
			return ErrNoTarget
		}
	}
	return nil
}

// AddGrant gives a resource or a broadcast of a member account to a guest account. The target must be
// held by the giving account right now; the guest is told through its directory stream.
func (o *Ops) AddGrant(owner, targetID, kind, guest string) error {
	ol, ok := accounts.NormalizeLogin(owner)
	if !ok || !o.Accounts.Exists(ol) {
		return accounts.ErrNoAccount
	}
	gl, ok := accounts.NormalizeLogin(guest)
	if !ok || !o.Accounts.Exists(gl) {
		return accounts.ErrNoAccount
	}
	if err := o.heldTarget(ol, targetID, kind); err != nil {
		return err
	}
	if err := o.Accounts.AddGrant(ol, targetID, kind, gl); err != nil {
		return err
	}
	// the store chose the grantId; the directory needs it to name the grant to the guest and to the member
	if g, ok := o.grantOf(ol, targetID, gl); ok {
		o.Dir.GrantAdded(DirectoryGrant(g), nil, false)
	}
	o.Log.State("grant-added", logx.Login(ol), grantTarget(kind, targetID))
	return nil
}

// grantOf finds the grant of one target to one guest that a given account made.
func (o *Ops) grantOf(owner, targetID, guest string) (accounts.Grant, bool) {
	for _, g := range o.Accounts.GrantsOf(owner, targetID) {
		if g.Account == guest {
			return g, true
		}
	}
	return accounts.Grant{}, false
}

// PutGrant is the `grant` of a device (20 item BD, amended by 27 item DJ): the member account `owner`
// gives a resource or a live broadcast to the guest account `guest`, under the grantId the device chose,
// with the payload p - the box, or the clear access or credentials - kept in memory for the guest. A grantId
// the account already holds for the same target and guest replaces the payload of that grant and changes
// nothing else (replaced). The error is one the caller turns into a refusal: ErrNoTarget, ErrNoAccount (an
// account that does not exist), accounts.ErrNotGuest, ErrNotMember, ErrGrantID.
func (o *Ops) PutGrant(owner, grantID, targetID, kind, guest string, p directory.GrantPayload) (replaced bool, err error) {
	ol, ok := accounts.NormalizeLogin(owner)
	if !ok || !o.Accounts.Exists(ol) {
		return false, accounts.ErrNoAccount
	}
	gl, ok := accounts.NormalizeLogin(guest)
	if !ok || !o.Accounts.Exists(gl) {
		return false, accounts.ErrNoAccount
	}
	if err := o.heldTarget(ol, targetID, kind); err != nil {
		return false, err
	}
	g, replaced, err := o.Accounts.PutGrant(ol, grantID, targetID, kind, gl)
	if err != nil {
		return false, err
	}
	o.Dir.GrantAdded(DirectoryGrant(g), &p, replaced)
	if replaced {
		o.Log.State("grant-replaced", logx.Login(ol), grantTarget(kind, targetID))
	} else {
		o.Log.State("grant-added", logx.Login(ol), grantTarget(kind, targetID))
	}
	return replaced, nil
}

// RemoveGrantByID is the `ungrant` of a device: any device of the owning account takes a grant back by its
// grantId. An unknown grantId, or one of another account, is accounts.ErrNoGrant and changes nothing.
func (o *Ops) RemoveGrantByID(owner, grantID string) error {
	g, err := o.Accounts.RemoveGrantByID(owner, grantID)
	if err != nil {
		return err
	}
	o.Dir.GrantRemoved(DirectoryGrant(g), true)
	o.Log.State("grant-removed", logx.Login(g.GrantedBy), grantTarget(g.Kind, g.TargetID))
	return nil
}

// DropTargetGrants takes back every grant of a target that has just left the directory - a resource
// withdrawn, a broadcast ended (20 item BD). The guests were told with the target's own removal, so only
// the member's own devices are told here.
func (o *Ops) DropTargetGrants(owner, targetID string) {
	for _, g := range o.Accounts.GrantsOf(owner, targetID) {
		if _, err := o.Accounts.RemoveGrant(g.GrantedBy, g.TargetID, g.Account); err != nil {
			continue
		}
		o.Dir.GrantRemoved(DirectoryGrant(g), false)
		o.Log.State("grant-ended", logx.Login(g.GrantedBy), grantTarget(g.Kind, g.TargetID))
	}
}

// RemoveGrant takes a grant back; the guest is told.
func (o *Ops) RemoveGrant(owner, targetID, guest string) error {
	g, err := o.Accounts.RemoveGrant(owner, targetID, guest)
	if err != nil {
		return err
	}
	o.Dir.GrantRemoved(DirectoryGrant(g), true)
	o.Log.State("grant-removed", logx.Login(g.GrantedBy), grantTarget(g.Kind, g.TargetID))
	return nil
}

// ListGrants lists what a guest account was given (guest set) or what a member account gave (owner set).
func (o *Ops) ListGrants(owner, guest string) []GrantView {
	var src []accounts.Grant
	switch {
	case guest != "":
		src = o.Accounts.GrantsForGuest(guest)
	case owner != "":
		src = o.Accounts.GrantsBy(owner)
	}
	out := make([]GrantView, 0, len(src))
	for _, g := range src {
		out = append(out, grantView(g))
	}
	return out
}

// grantTarget names what a grant gives in a log line, by the hashed form of its id.
func grantTarget(kind, id string) logx.Field {
	if kind == accounts.GrantBroadcast {
		return logx.Broadcast(id)
	}
	return logx.Resource(id)
}

// ListBroadcasts lists active broadcasts under an account (spec 246 Group A).
func (o *Ops) ListBroadcasts(login, deviceID string) []directory.BroadcastView {
	if o.Dir == nil {
		return []directory.BroadcastView{}
	}
	l, ok := accounts.NormalizeLogin(login)
	if !ok {
		return []directory.BroadcastView{}
	}
	return o.Dir.ListBroadcasts(l, deviceID)
}

// EndBroadcast administratively stops an active broadcast (spec 246 Group A).
func (o *Ops) EndBroadcast(login, broadcastID string) error {
	if !o.Live {
		return ErrNotLive
	}
	l, ok := accounts.NormalizeLogin(login)
	if !ok || !o.Accounts.Exists(l) {
		return accounts.ErrNoAccount
	}
	if err := o.Dir.EndBroadcast(l, broadcastID); err != nil {
		return err
	}
	o.Log.State("broadcast-ended-admin", logx.Login(l), logx.Broadcast(broadcastID))
	return nil
}

// ListResources lists published resources of an account (spec 246 Group B).
func (o *Ops) ListResources(login string) []directory.ResourceView {
	if o.Dir == nil {
		return []directory.ResourceView{}
	}
	l, ok := accounts.NormalizeLogin(login)
	if !ok {
		return []directory.ResourceView{}
	}
	return o.Dir.ListResources(l)
}

// UnpublishResource administratively revokes a published resource (spec 246 Group B).
func (o *Ops) UnpublishResource(login, resourceID string) error {
	if !o.Live {
		return ErrNotLive
	}
	l, ok := accounts.NormalizeLogin(login)
	if !ok || !o.Accounts.Exists(l) {
		return accounts.ErrNoAccount
	}
	if err := o.Dir.UnpublishResource(l, resourceID); err != nil {
		return err
	}
	o.Log.State("resource-unpublished-admin", logx.Login(l), logx.Resource(resourceID))
	return nil
}

// GetClipboardInfo inspects an account's clipboard slot metadata (spec 246 Group C).
func (o *Ops) GetClipboardInfo(login string) (*clipboard.ClipboardSlotView, error) {
	if !o.Live || o.Clipboard == nil {
		return &clipboard.ClipboardSlotView{Present: false}, nil
	}
	l, ok := accounts.NormalizeLogin(login)
	if !ok || !o.Accounts.Exists(l) {
		return nil, accounts.ErrNoAccount
	}
	return o.Clipboard.Info(l), nil
}

// EraseClipboard clears an account's clipboard slot (spec 246 Group C).
func (o *Ops) EraseClipboard(login string) error {
	if !o.Live || o.Clipboard == nil {
		return nil
	}
	l, ok := accounts.NormalizeLogin(login)
	if !ok || !o.Accounts.Exists(l) {
		return accounts.ErrNoAccount
	}
	o.Clipboard.Erase(l)
	o.Log.State("clipboard-erased", logx.Login(l))
	return nil
}

// ListAudit returns recent administrative audit events and active admin sessions (spec 246 Group D).
func (o *Ops) ListAudit() AdminAuditView {
	if o.Audit != nil {
		return o.Audit.View()
	}
	return AdminAuditView{
		ActiveSessions: []AdminSessionView{},
		Records:        []AdminAuditRecord{},
	}
}
