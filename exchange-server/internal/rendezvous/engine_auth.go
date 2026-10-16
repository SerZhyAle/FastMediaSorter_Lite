package rendezvous

import (
	"errors"
	"time"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/directory"
	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/wire"
)

// This file is ticket 162: enroll and hello, and ticket 224: register. All run over TLS only (the state
// machine has already refused a plain stream). A failed sign-in is ONE answer, `bad-credentials`,
// whichever of the login, the password, the code or the token was wrong; the answers that are told only
// to someone who has proven something (`invitation-invalid`, `login-taken`, `signed-out`) are told after
// that proof (DEVICE-EXCHANGE 19 items AQ and AT).

func (e *Engine) welcomeBody(account string, interval int) wire.WelcomeBody {
	return wire.WelcomeBody{Account: account, AccountKind: e.cfg.Accounts.KindOf(account), PublicEndpoint: e.cfg.PublicEndpoint,
		KeepaliveSeconds: interval, Features: e.features(), ServerVersion: e.cfg.ServerVersion}
}

// takeFailure charges a failed-attempt bucket BEFORE the work it protects (the 139 discipline: a
// parallel wave must not pass a "has a token" check before the first failure is counted) and says
// whether there was a token. The caller gives the token back with give() once the attempt succeeds.
// It guards what is not a password check: a pairing code (item J) and a token.
func takeFailure(l *limits.Limiter, key string, c limits.Class) (ok bool, give func()) {
	if !l.Allow(key, c) {
		return false, func() {}
	}
	return true, func() { l.Refund(key, c) }
}

// hashBounded makes the Argon2id record of a new password inside the bound on concurrent memory-hard
// work, so a registration or a password change cannot be the cheap way to exhaust the host. The error
// is the policy's (a password.Err*) or limits.ErrBusy.
func (e *Engine) hashBounded(pw string) (string, error) {
	var rec string
	var herr error
	if _, err := e.cfg.Verifier.Run(func() bool {
		rec, herr = e.cfg.Accounts.HashPassword(pw)
		return herr == nil
	}); err != nil {
		return "", err
	}
	return rec, herr
}

// deviceSpec is what an enroll or a register says about the device.
func deviceSpec(login, deviceID, name, product, version, platform string, roles, caps []string, receiver []byte, key *wire.E2EKey) accounts.EnrollSpec {
	return accounts.EnrollSpec{
		Account: login, DeviceID: deviceID, Name: name, Product: product, ProductVersion: version,
		Platform: platform, Roles: knownRoles(roles), Capabilities: caps, Receiver: receiver, E2EKey: key,
	}
}

// admit makes the directory and the stream know a device that has just been enrolled and builds the
// session of its stream.
func (e *Engine) admit(st *Stream, d accounts.Device, keepalive int) (*Session, time.Duration) {
	e.cfg.Dir.DeviceEnrolled(directory.DeviceInfo{
		ID: d.ID, Account: d.Account, Name: d.Name, Product: d.Product, ProductVersion: d.ProductVersion,
		Platform: d.Platform, Roles: d.Roles, Capabilities: d.Capabilities, Receiver: d.Receiver, E2EKey: d.E2EKey,
	})
	e.track(d.ID, st)
	interval := e.p.clampKeepalive(keepalive)
	return &Session{Account: d.Account, DeviceID: d.ID, Keepalive: interval}, interval
}

// Enroll is `enroll` (5.2): the login and the password or a pairing code, then a token that is returned
// once. A password check is paced (item AW: at most one a second per login and per address, no counter
// and no lockout) and runs inside the bound on memory-hard work, an unknown login against a dummy record;
// a pairing code keeps the failed-attempt buckets of item J.
func (e *Engine) Enroll(st *Stream, m *wire.Enroll) Result {
	addr := st.Addr()
	login, loginOK := accounts.NormalizeLogin(m.Login)
	refused := func(r wire.Reason) Result {
		e.cfg.Log.Info("enroll-refused", logx.Login(login), logx.Addr(addr), logx.Text("reason", string(r)))
		return Result{Refuse: r}
	}

	var ok bool
	if m.Password != "" {
		if !e.cfg.Accounts.Pacer().Wait(login, addr) {
			return refused(wire.ReasonRateLimited)
		}
		// the memory-hard hash runs inside the bound; for a login that is no account it runs against
		// a dummy record, so the answer's time does not tell a login from a password
		verified, err := e.cfg.Verifier.Run(func() bool { return e.cfg.Accounts.VerifyPassword(m.Login, m.Password) })
		if err != nil { // our own busy state is not the client's failure
			return refused(wire.ReasonCapacity)
		}
		ok = verified
	} else {
		okAddr, giveAddr := takeFailure(e.cfg.Limiter, addr, limits.FailedAuth)
		if !okAddr {
			return refused(wire.ReasonRateLimited)
		}
		okLogin, giveLogin := takeFailure(e.cfg.LoginLimiter, login, limits.FailedEnroll)
		if !okLogin {
			giveAddr()
			return refused(wire.ReasonRateLimited)
		}
		ok = loginOK && e.cfg.Accounts.RedeemPairingCode(login, m.PairingCode)
		if ok {
			giveAddr()
			giveLogin()
		}
	}
	if !ok {
		return refused(wire.ReasonBadCredentials)
	}

	en, err := e.cfg.Accounts.Enroll(deviceSpec(login, m.DeviceID, m.DeviceName, m.Product, m.ProductVersion, m.Platform, m.Roles, m.Capabilities, m.Receiver, m.E2EKey))
	switch {
	case errors.Is(err, accounts.ErrIDCollision):
		e.cfg.Log.Info("enroll-refused", logx.Login(login), logx.Device(m.DeviceID), logx.Text("reason", string(wire.ReasonIDCollision)))
		return Result{Refuse: wire.ReasonIDCollision}
	case errors.Is(err, accounts.ErrLimit):
		e.cfg.Log.Info("enroll-refused", logx.Login(login), logx.Device(m.DeviceID), logx.Text("reason", string(wire.ReasonLimitReached)))
		return Result{Refuse: wire.ReasonLimitReached}
	case err != nil:
		e.cfg.Log.Error("enroll-failed", logx.Login(login), logx.Text("error", err.Error()))
		return Result{Silent: true}
	}

	// the directory learns of the device; a rotated token closes the old streams (through the hook)
	sess, interval := e.admit(st, en.Device, m.KeepaliveSeconds)
	e.cfg.Log.Info("enrolled", logx.Login(login), logx.Device(en.Device.ID), logx.Addr(addr), logx.Int("rotated", boolInt(en.Rotated)))
	return Result{
		Session: sess,
		Reply:   &wire.Enrolled{DeviceToken: en.Token, WelcomeBody: e.welcomeBody(login, int(interval/time.Second))},
	}
}

// Register is `register` (19 item AQ): the account and its first device in one exchange, by an
// invitation. The checks run in the contract order and the first failure is the answer, with nothing
// stored and nothing consumed: the rate, the invitation, the cap on accounts, the login, the password
// policy. The account, its first device and the use of the invitation are applied together or not at all.
func (e *Engine) Register(st *Stream, m *wire.Register) Result {
	addr := st.Addr()
	login, loginOK := accounts.NormalizeLogin(m.Login)
	refused := func(r wire.Reason) Result {
		e.cfg.Log.Info("register-refused", logx.Login(login), logx.Addr(addr), logx.Text("reason", string(r)))
		return Result{Refuse: r}
	}

	// 1. the rate: a password-like secret is about to be checked
	if !e.cfg.Accounts.Pacer().Wait(m.Login, addr) {
		return refused(wire.ReasonRateLimited)
	}

	// 2. the invitation: a one-time code (used only by the registration that succeeds), or the shared
	// password (a memory-hard check, inside the bound)
	var kind, code string
	var shared *accounts.SharedInvitationAuthorization
	if m.InvitationCode != "" {
		k, err := e.cfg.Accounts.OneTimeInvitation(m.InvitationCode)
		if err != nil {
			e.cfg.Log.Info("register-refused", logx.Login(login), logx.Addr(addr), logx.Invitation(m.InvitationCode),
				logx.Text("reason", string(wire.ReasonInvitationInvalid)))
			return Result{Refuse: wire.ReasonInvitationInvalid}
		}
		kind, code = k, m.InvitationCode
	} else {
		var k string
		ok, err := e.cfg.Verifier.Run(func() bool {
			var verr error
			shared, verr = e.cfg.Accounts.AuthorizeSharedInvitation(m.SharedPassword)
			if verr == nil {
				k = shared.Kind()
			}
			return verr == nil
		})
		if err != nil {
			return refused(wire.ReasonCapacity)
		}
		if !ok {
			return refused(wire.ReasonInvitationInvalid)
		}
		kind = k
	}

	// 3. the cap on accounts
	if e.cfg.Accounts.AccountCount() >= e.cfg.Accounts.Policy().MaxAccounts {
		return refused(wire.ReasonLimitReached)
	}

	// 4. the login: its form, and that it is free (told only now that the invitation is proven)
	if !loginOK || e.cfg.Accounts.Exists(login) {
		return refused(wire.ReasonLoginTaken)
	}

	// 5. the password policy
	if err := password.Check(m.Password); err != nil {
		return refused(wire.ReasonPasswordPolicy)
	}
	rec, err := e.hashBounded(m.Password)
	switch {
	case errors.Is(err, limits.ErrBusy):
		return refused(wire.ReasonCapacity)
	case err != nil:
		return refused(wire.ReasonPasswordPolicy)
	}

	en, err := e.cfg.Accounts.RegisterAccount(accounts.RegisterSpec{
		Login: login, PasswordRecord: rec, Kind: kind, InvitationCode: code, SharedInvitation: shared,
		Device: deviceSpec(login, m.DeviceID, m.DeviceName, m.Product, m.ProductVersion, m.Platform, m.Roles, m.Capabilities, m.Receiver, m.E2EKey),
	})
	switch {
	case errors.Is(err, accounts.ErrInvitationInvalid):
		return refused(wire.ReasonInvitationInvalid) // used or withdrawn between the check and the write
	case errors.Is(err, accounts.ErrLimit):
		return refused(wire.ReasonLimitReached)
	case errors.Is(err, accounts.ErrExists):
		return refused(wire.ReasonLoginTaken)
	case errors.Is(err, accounts.ErrIDCollision):
		e.cfg.Log.Info("register-refused", logx.Login(login), logx.Device(m.DeviceID), logx.Text("reason", string(wire.ReasonIDCollision)))
		return Result{Refuse: wire.ReasonIDCollision}
	case err != nil:
		e.cfg.Log.Error("register-failed", logx.Login(login), logx.Text("error", err.Error()))
		return Result{Silent: true}
	}

	sess, interval := e.admit(st, en.Device, m.KeepaliveSeconds)
	if code != "" {
		e.cfg.Log.Info("registered", logx.Login(login), logx.Device(en.Device.ID), logx.Addr(addr), logx.Invitation(code), logx.Text("kind", kind))
	} else {
		e.cfg.Log.Info("registered", logx.Login(login), logx.Device(en.Device.ID), logx.Addr(addr), logx.Text("via", "shared-invitation"), logx.Text("kind", kind))
	}
	return Result{
		Session: sess,
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
// compared in constant time. A token that matches the hash of a revoked device is `device-revoked`; the
// dead token of a device that a password change or a reset signed out is `signed-out` (19 item AT); in
// every other failure it is `bad-credentials`, so the reason cannot tell a stranger that a deviceId
// ever existed. A hello is not a password check: it keeps its failed-attempt buckets, charged to the
// address and, once the account is known, to the account.
func (e *Engine) Hello(st *Stream, m *wire.Hello) Result {
	addr := st.Addr()
	okAddr, giveAddr := takeFailure(e.cfg.Limiter, addr, limits.FailedAuth)
	if !okAddr {
		e.cfg.Log.Info("hello-refused", logx.Device(m.DeviceID), logx.Addr(addr), logx.Text("reason", string(wire.ReasonRateLimited)))
		return Result{Refuse: wire.ReasonRateLimited}
	}
	dev, status := e.cfg.Accounts.Hello(m.DeviceID, m.DeviceToken)
	switch status {
	case accounts.HelloOK:
		giveAddr()
		interval := e.p.clampKeepalive(m.KeepaliveSeconds)
		e.track(dev.ID, st)
		return Result{
			Session: &Session{Account: dev.Account, DeviceID: dev.ID, Keepalive: interval},
			Reply:   &wire.Welcome{WelcomeBody: e.welcomeBody(dev.Account, int(interval/time.Second))},
		}
	case accounts.HelloSignedOut:
		// the holder of the token this device was given is no guesser: it learns why and signs in again
		giveAddr()
		e.cfg.Log.Info("hello-refused", logx.Device(m.DeviceID), logx.Addr(addr), logx.Text("reason", string(wire.ReasonSignedOut)))
		return Result{Refuse: wire.ReasonSignedOut}
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
