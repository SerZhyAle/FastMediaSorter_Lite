package rendezvous

import (
	"errors"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/admin"
	"fms/exchange-server/internal/directory"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/wire"
)

// Grants on a device's control stream (DEVICE-EXCHANGE 20 items BD and BE, 27 item DJ). A device of a
// member account gives one resource or one live broadcast to a guest account with `grant`, takes it back
// with `ungrant`, and replaces the box of a grant it already made by sending `grant` again under the same
// grantId. The server keeps the box in memory and hands it to the guest in the granted record; it never
// opens it, and no log line carries it.

// grantPayload picks the one secret member a validated `grant` carries.
func grantPayload(m *wire.Grant) directory.GrantPayload {
	switch {
	case len(m.Sealed) > 0:
		return directory.GrantPayload{Member: "sealed", Raw: m.Sealed}
	case len(m.Access) > 0:
		return directory.GrantPayload{Member: "access", Raw: m.Access}
	}
	return directory.GrantPayload{Member: "credentials", Raw: m.Credentials}
}

// handleGrant answers a `grant` with `granted`, or with `refused`: `not-permitted` from a guest account;
// `unavailable` for an unknown target, a target of another account, a login that is not a guest of this
// deployment, or a link (this server serves none); `id-collision` for a grantId that is held by another
// grant, or a target already given to that guest under another grantId.
func (e *Engine) handleGrant(cs *ControlStream, sess *Session, m *wire.Grant) bool {
	acct, dev := sess.Account, sess.DeviceID
	refuse := func(r wire.Reason, why string) bool {
		e.cfg.Log.Info("grant-refused", logx.Device(dev), logx.Text("reason", string(r)), logx.Text("why", why))
		return cs.Send(wire.Refuse(r)) == nil
	}
	switch {
	case e.cfg.Accounts.IsGuest(acct):
		return refuse(wire.ReasonNotPermitted, "guest")
	case m.Kind == wire.GrantKindLink:
		return refuse(wire.ReasonUnavailable, "link")
	}
	kind, target := accounts.GrantResource, m.ResourceID
	if m.BroadcastID != "" {
		kind, target = accounts.GrantBroadcast, m.BroadcastID
	}
	_, err := e.ops.PutGrant(acct, m.GrantID, target, kind, m.Guest, grantPayload(m))
	switch {
	case err == nil:
		return cs.Send(&wire.Granted{GrantID: m.GrantID}) == nil
	case errors.Is(err, accounts.ErrGrantID):
		return refuse(wire.ReasonIDCollision, "grant-id")
	case errors.Is(err, accounts.ErrNotMember):
		return refuse(wire.ReasonNotPermitted, "not-a-member")
	case errors.Is(err, admin.ErrNoTarget):
		return refuse(wire.ReasonUnavailable, "no-target")
	case errors.Is(err, accounts.ErrNoAccount), errors.Is(err, accounts.ErrNotGuest), errors.Is(err, accounts.ErrBadLogin):
		return refuse(wire.ReasonUnavailable, "not-a-guest")
	}
	e.cfg.Log.Error("grant-failed", logx.Device(dev), logx.Text("error", err.Error()))
	return refuse(wire.ReasonUnavailable, "store")
}

// handleUngrant is `ungrant`: no answer. A grantId the account does not hold does nothing.
func (e *Engine) handleUngrant(sess *Session, m *wire.Ungrant) {
	if err := e.ops.RemoveGrantByID(sess.Account, m.GrantID); err != nil && !errors.Is(err, accounts.ErrNoGrant) {
		e.cfg.Log.Error("ungrant-failed", logx.Device(sess.DeviceID), logx.Text("error", err.Error()))
	}
}

// grantTargetGone is the directory's hook: a resource was withdrawn or a broadcast ended, and the grants of
// that target end with it (20 item BD).
func (e *Engine) grantTargetGone(account, target string, _ bool) {
	e.ops.DropTargetGrants(account, target)
}
