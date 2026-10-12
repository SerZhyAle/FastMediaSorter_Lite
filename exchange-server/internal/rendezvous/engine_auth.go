package rendezvous

import (
	"errors"
	"time"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/directory"
	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/wire"
)

// This file is ticket 162: enroll and hello. Both run over TLS only (the state machine has already
// refused a plain stream) and every failure is ONE answer, `bad-credentials`, whichever of the login,
// the password, the code or the token was wrong.

func (e *Engine) welcomeBody(account string, interval int) wire.WelcomeBody {
	return wire.WelcomeBody{Account: account, PublicEndpoint: e.cfg.PublicEndpoint, KeepaliveSeconds: interval,
		Features: e.features(), ServerVersion: e.cfg.ServerVersion}
}

// takeFailure charges a failed-attempt bucket BEFORE the work it protects (the 139 discipline: a
// parallel wave must not pass a "has a token" check before the first failure is counted) and says
// whether there was a token. The caller gives the token back with give() once the attempt succeeds.
func takeFailure(l *limits.Limiter, key string, c limits.Class) (ok bool, give func()) {
	if !l.Allow(key, c) {
		return false, func() {}
	}
	return true, func() { l.Refund(key, c) }
}

// Enroll is `enroll` (5.2): the login and the password or a pairing code, the buckets charged before
// any hash, an unknown login verified against a dummy record, then a token that is returned once.
func (e *Engine) Enroll(st *Stream, m *wire.Enroll) Result {
	addr := st.Addr()
	login, loginOK := accounts.NormalizeLogin(m.Login)
	limKey := login // folded; a login that is no login at all is still a key, bounded by the table's LRU

	okAddr, giveAddr := takeFailure(e.cfg.Limiter, addr, limits.FailedAuth)
	if !okAddr {
		e.cfg.Log.Info("enroll-refused", logx.Login(login), logx.Addr(addr), logx.Text("reason", string(wire.ReasonRateLimited)))
		return Result{Refuse: wire.ReasonRateLimited}
	}
	okLogin, giveLogin := takeFailure(e.cfg.LoginLimiter, limKey, limits.FailedEnroll)
	if !okLogin {
		giveAddr()
		e.cfg.Log.Info("enroll-refused", logx.Login(login), logx.Addr(addr), logx.Text("reason", string(wire.ReasonRateLimited)))
		return Result{Refuse: wire.ReasonRateLimited}
	}

	var ok bool
	if m.Password != "" {
		// the memory-hard hash runs inside the bound; for a login that is no account it runs against
		// a dummy record, so the answer's time does not tell a login from a password
		verified, err := e.cfg.Verifier.Run(func() bool { return e.cfg.Accounts.VerifyPassword(m.Login, m.Password) })
		if err != nil { // our own busy state is not the client's failure
			giveAddr()
			giveLogin()
			return Result{Refuse: wire.ReasonCapacity}
		}
		ok = verified
	} else {
		ok = loginOK && e.cfg.Accounts.RedeemPairingCode(login, m.PairingCode)
	}
	if !ok {
		e.cfg.Log.Info("enroll-refused", logx.Login(login), logx.Addr(addr), logx.Text("reason", string(wire.ReasonBadCredentials)))
		return Result{Refuse: wire.ReasonBadCredentials}
	}
	giveAddr()
	giveLogin()

	en, err := e.cfg.Accounts.Enroll(accounts.EnrollSpec{
		Account: login, DeviceID: m.DeviceID, Name: m.DeviceName, Product: m.Product,
		ProductVersion: m.ProductVersion, Platform: m.Platform, Roles: knownRoles(m.Roles), Capabilities: m.Capabilities, Receiver: m.Receiver,
	})
	switch {
	case errors.Is(err, accounts.ErrIDCollision):
		e.cfg.Log.Info("enroll-refused", logx.Login(login), logx.Device(m.DeviceID), logx.Text("reason", string(wire.ReasonIDCollision)))
		return Result{Refuse: wire.ReasonIDCollision}
	case errors.Is(err, accounts.ErrCapacity):
		e.cfg.Log.Info("enroll-refused", logx.Login(login), logx.Device(m.DeviceID), logx.Text("reason", string(wire.ReasonCapacity)))
		return Result{Refuse: wire.ReasonCapacity}
	case err != nil:
		e.cfg.Log.Error("enroll-failed", logx.Login(login), logx.Text("error", err.Error()))
		return Result{Silent: true}
	}

	// the directory learns of the device; a rotated token closes the old streams (through the hook)
	d := en.Device
	e.cfg.Dir.DeviceEnrolled(directory.DeviceInfo{
		ID: d.ID, Account: d.Account, Name: d.Name, Product: d.Product, ProductVersion: d.ProductVersion,
		Platform: d.Platform, Roles: d.Roles, Capabilities: d.Capabilities, Receiver: d.Receiver,
	})
	e.track(d.ID, st)

	interval := e.p.clampKeepalive(m.KeepaliveSeconds)
	e.cfg.Log.Info("enrolled", logx.Login(login), logx.Device(d.ID), logx.Addr(addr), logx.Int("rotated", boolInt(en.Rotated)))
	return Result{
		Session: &Session{Account: login, DeviceID: d.ID, Keepalive: interval},
		Reply:   &wire.Enrolled{DeviceToken: en.Token, WelcomeBody: e.welcomeBody(login, int(interval/time.Second))},
	}
}

func boolInt(b bool) int {
	if b {
		return 1
	}
	return 0
}

// knownRoles keeps only the four roles of 6.1.
func knownRoles(in []string) []string {
	var out []string
	for _, r := range in {
		for _, k := range wire.KnownRoles {
			if r == k {
				out = append(out, r)
				break
			}
		}
	}
	return out
}

// Hello is `hello` (5.2): the device found by deviceId alone, the SHA-256 of the presented token
// compared in constant time. A token that matches the hash of a revoked device is `device-revoked`; in
// every other failure it is `bad-credentials`, so the reason cannot tell a stranger that a deviceId
// ever existed. Failures are charged to the address and, once the account is known, to the account.
func (e *Engine) Hello(st *Stream, m *wire.Hello) Result {
	addr := st.Addr()
	okAddr, giveAddr := takeFailure(e.cfg.Limiter, addr, limits.FailedAuth)
	if !okAddr {
		e.cfg.Log.Info("hello-refused", logx.Device(m.DeviceID), logx.Addr(addr), logx.Text("reason", string(wire.ReasonRateLimited)))
		return Result{Refuse: wire.ReasonRateLimited}
	}
	dev, status := e.cfg.Accounts.Hello(m.DeviceID, m.DeviceToken)
	if status == accounts.HelloOK {
		giveAddr()
		interval := e.p.clampKeepalive(m.KeepaliveSeconds)
		e.track(dev.ID, st)
		return Result{
			Session: &Session{Account: dev.Account, DeviceID: dev.ID, Keepalive: interval},
			Reply:   &wire.Welcome{WelcomeBody: e.welcomeBody(dev.Account, int(interval/time.Second))},
		}
	}
	reason := wire.ReasonBadCredentials
	if status == accounts.HelloRevoked {
		reason = wire.ReasonDeviceRevoked
	}
	if dev.Account != "" {
		if ok, _ := takeFailure(e.cfg.LoginLimiter, dev.Account, limits.FailedHello); !ok {
			reason = wire.ReasonRateLimited
		}
	}
	e.cfg.Log.Info("hello-refused", logx.Device(m.DeviceID), logx.Addr(addr), logx.Text("reason", string(reason)))
	return Result{Refuse: reason}
}
