package rendezvous

import (
	"errors"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/directory"
	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/wire"
)

// This file is ticket 163 on the wire side: a device's control stream - presence, keepalive, list,
// subscribe, publish, unpublish, the broadcast records - plus pairing and revoke of ticket 162. The
// directory itself is internal/directory; here is only what a handler call does with it.

// Control serves a device's control stream until it ends: a `bye`, an envelope invalid in the state,
// a missed keepalive window (the sweep closes the stream) or a drop. The device's presence IS this
// stream: online while it runs, offline after it.
func (e *Engine) Control(cs *ControlStream, first wire.Envelope) {
	sess := cs.Session()
	if !e.cfg.Dir.Online(sess.Account, sess.DeviceID, cs) {
		return // the device was revoked between its hello and now
	}
	e.registerCtl(cs, sess.Keepalive)
	e.cfg.Accounts.Touch(sess.DeviceID)
	e.cfg.Log.Info("online", logx.Device(sess.DeviceID), logx.Addr(cs.Addr()))
	defer func() {
		e.dropCtl(cs)
		e.cfg.Dir.Offline(sess.Account, sess.DeviceID, cs)
		e.cfg.Accounts.Touch(sess.DeviceID)
		e.cfg.Log.Info("offline", logx.Device(sess.DeviceID))
	}()
	env := first
	for {
		e.touchCtl(cs)
		if !e.handle(cs, sess, env) {
			return
		}
		next, err := cs.Next()
		if err != nil {
			return
		}
		env = next
	}
}

// refuseOrClose answers a directory error with its refusal and keeps the stream; an error that is no
// refusal (a malformed record, a device that is gone) closes it.
func (e *Engine) refuseOrClose(cs *ControlStream, err error) bool {
	if r := directory.Reason(err); r != "" {
		return cs.Send(wire.Refuse(r)) == nil
	}
	return false
}

// handle runs one envelope of a control stream and reports whether the stream goes on.
func (e *Engine) handle(cs *ControlStream, sess *Session, env wire.Envelope) bool {
	acct, dev := sess.Account, sess.DeviceID
	switch m := env.(type) {
	case *wire.Keepalive:
		e.cfg.Accounts.Touch(dev)
		return cs.Send(&wire.Keepalive{}) == nil
	case *wire.Bye:
		return false
	case *wire.List:
		rev, ds, rs, bs := e.cfg.Dir.Snapshot(acct)
		return cs.Send(&wire.Directory{Revision: rev, Devices: ds, Resources: rs, Broadcasts: bs}) == nil
	case *wire.Subscribe:
		e.cfg.Dir.Subscribe(acct, dev, cs)
		return true
	case *wire.Publish:
		res, err := e.cfg.Dir.Publish(acct, dev, m.Resource)
		if err != nil {
			e.cfg.Log.Info("publish-refused", logx.Device(dev), logx.Text("why", publishWhy(err)))
			return e.refuseOrClose(cs, err)
		}
		port := 0
		if res.PublicPort != nil {
			port = *res.PublicPort
		}
		e.cfg.Log.Info("published", logx.Device(dev), logx.Resource(res.ResourceID), logx.Int("port", port))
		return cs.Send(&wire.Published{ResourceID: res.ResourceID, PublicPort: res.PublicPort}) == nil
	case *wire.Unpublish:
		e.cfg.Dir.Unpublish(acct, dev, m.ResourceID)
		return true
	case *wire.BroadcastStart:
		if !e.cfg.LoginLimiter.Allow(dev, limits.BroadcastStart) {
			return cs.Send(wire.Refuse(wire.ReasonRateLimited)) == nil
		}
		id, err := e.cfg.Dir.BroadcastStart(acct, dev, m.Broadcast, m.MaxListeners)
		if err != nil {
			e.cfg.Log.Info("broadcast-refused", logx.Device(dev), logx.Text("why", publishWhy(err)))
			return e.refuseOrClose(cs, err)
		}
		e.cfg.Log.Info("broadcast-started", logx.Device(dev), logx.Broadcast(id))
		return cs.Send(&wire.BroadcastStarted{BroadcastID: id}) == nil
	case *wire.BroadcastUpdate:
		if err := e.cfg.Dir.BroadcastUpdate(acct, dev, m.Broadcast, m.MaxListeners); err != nil {
			return e.refuseOrClose(cs, err)
		}
		return true
	case *wire.BroadcastEnd:
		e.cfg.Dir.BroadcastEnd(acct, dev, m.BroadcastID)
		return true
	case *wire.PairingRequest:
		if !e.cfg.LoginLimiter.Allow(acct, limits.PairingRequest) {
			return cs.Send(wire.Refuse(wire.ReasonRateLimited)) == nil
		}
		code, exp, err := e.cfg.Accounts.NewPairingCode(acct)
		switch {
		case errors.Is(err, accounts.ErrCapacity):
			return cs.Send(wire.Refuse(wire.ReasonCapacity)) == nil
		case err != nil:
			return false
		}
		e.cfg.Log.Info("pairing-code-issued", logx.Device(dev))
		return cs.Send(&wire.PairingCode{Code: code, ExpiresAt: wire.FormatTime(exp)}) == nil
	case *wire.Revoke:
		// Any enrolled device of the account may revoke another - or itself: the trust among an
		// account's devices is flat, an accepted limit of v1 (item Q). An unknown or foreign device
		// does nothing and is not answered; a real one is reported through `changed` and the log.
		e.cfg.Log.Info("revoke-requested", logx.Device(dev))
		if _, err := e.ops.RevokeDevice(acct, m.DeviceID); err != nil {
			e.cfg.Log.Error("revoke-failed", logx.Device(dev), logx.Text("error", err.Error()))
		}
		return true
	case *wire.Cast, *wire.CastAnswer, *wire.CastStop:
		return true // cast routing is ticket 166's; until it is built these are ignored, never an error
	}
	return false // a known envelope that is invalid on a control stream closes it
}

// publishWhy is a short, secret-free word for a refused publish, for the log.
func publishWhy(err error) string {
	switch {
	case errors.Is(err, directory.ErrIDCollision):
		return "id-collision"
	case errors.Is(err, directory.ErrCapacity), errors.Is(err, wire.ErrRecordSize):
		return "capacity"
	case errors.Is(err, directory.ErrPortUnavail):
		return "port-unavailable"
	case errors.Is(err, directory.ErrMalformed):
		return "malformed"
	}
	return "other"
}
