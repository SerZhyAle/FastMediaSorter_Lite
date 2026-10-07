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

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/directory"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/wire"
)

// Ops are the operator's operations.
type Ops struct {
	Accounts *accounts.Store
	Dir      *directory.Dir
	Log      *logx.Logger
}

// AddAccount creates an account under the password policy and returns its normalised login.
func (o *Ops) AddAccount(login, pw string) (string, error) {
	l, err := o.Accounts.AddAccount(login, pw)
	if err == nil {
		o.Log.Info("account-added", logx.Login(l))
	}
	return l, err
}

// SetPassword changes an account's password. Tokens are left alone unless signOut is true, which
// revokes every device of the account (ticket 162 rule 9).
func (o *Ops) SetPassword(login, pw string, signOut bool) error {
	l, ok := accounts.NormalizeLogin(login)
	if !ok || !o.Accounts.Exists(l) {
		return accounts.ErrNoAccount
	}
	if err := o.Accounts.SetPassword(l, pw); err != nil {
		return err
	}
	o.Log.Info("password-changed", logx.Login(l))
	if signOut {
		for _, d := range o.Accounts.Devices(l) {
			if _, err := o.RevokeDevice(l, d.ID); err != nil {
				return err
			}
		}
	}
	return nil
}

// RemoveAccount deletes an account: every device is revoked in one go, its records and streams go.
func (o *Ops) RemoveAccount(login string) error {
	l, ok := accounts.NormalizeLogin(login)
	if !ok {
		return accounts.ErrNoAccount
	}
	_, err := o.Accounts.RemoveAccount(l)
	if errors.Is(err, accounts.ErrNoAccount) {
		return err
	}
	// even when the store could not be written the account is gone from memory: take it out of the directory too
	o.Dir.RemoveAccount(l)
	if err != nil {
		return err
	}
	o.Log.Info("account-removed", logx.Login(l))
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
	o.Log.Info("device-revoked", logx.Login(l), logx.Device(deviceID))
	return true, nil
}

// AccountView is what the operator sees of an account: never a record.
type AccountView struct {
	Login     string `json:"login"`
	CreatedAt string `json:"createdAt"`
	Devices   int    `json:"devices"`
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
}

// ListAccounts lists the accounts by login.
func (o *Ops) ListAccounts() []AccountView {
	out := []AccountView{}
	for _, a := range o.Accounts.Accounts() {
		out = append(out, AccountView{Login: a.Login, CreatedAt: a.CreatedAt, Devices: a.Devices})
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
			Online: o.Dir.DeviceOnline(l, d.ID)})
	}
	sort.Slice(out, func(i, j int) bool { return out[i].DeviceID < out[j].DeviceID })
	return out, nil
}

// Status is what a status request may show: counts, never a list and never a secret.
type Status struct {
	Accounts   int    `json:"accounts"`
	Devices    int    `json:"devices"`
	Online     int    `json:"online"`
	Resources  int    `json:"resources"`
	Broadcasts int    `json:"broadcasts"`
	Tombstones int    `json:"tombstones"`
	Version    string `json:"version,omitempty"`
}

// Status returns the totals.
func (o *Ops) Status() Status {
	c := o.Dir.Counts()
	return Status{Accounts: len(o.Accounts.Accounts()), Devices: len(o.Accounts.AllDevices()), Online: c.Online,
		Resources: c.Resources, Broadcasts: c.Broadcasts, Tombstones: o.Accounts.Tombstones()}
}

// DeviceIDOK reports whether s has the form of a deviceId (the CLI checks it before asking).
func DeviceIDOK(s string) bool { return wire.ValidID(s) }
