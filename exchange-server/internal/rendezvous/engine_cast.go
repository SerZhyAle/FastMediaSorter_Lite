package rendezvous

import (
	"encoding/json"
	"regexp"

	"fms/exchange-server/internal/cast"
	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/wire"
)

// This file is ticket 166: cast routing (DEVICE-EXCHANGE 7.8 and item R). A device of an account
// asks one named receiver of the SAME account to play a broadcast of that account; the server routes
// the offer, the answer and the stop and decides nothing about what the receiver can play. State is
// the table of internal/cast: pending offers, in memory, three ids and a deadline each.

// reasonForm is what a reason a receiver sends must look like to be passed on as it is; anything else
// (an empty one, a long one, one with odd characters) is a plain "declined". The contract says the
// reasons a client may send are `declined` and `unsupported` and that an unknown one is shown
// generically - so it is passed on, but never as arbitrary bytes.
var reasonForm = regexp.MustCompile(`^[a-z][a-z0-9-]{0,31}$`)

func castReason(accepted bool, sent string) string {
	if accepted {
		return ""
	}
	if reasonForm.MatchString(sent) {
		return sent
	}
	return wire.CastDeclined
}

// handleCast is `cast {broadcastId, receiverDeviceId}` from an enrolled control stream. It returns
// whether the stream goes on.
func (e *Engine) handleCast(cs *ControlStream, sess *Session, m *wire.Cast) bool {
	acct, dev := sess.Account, sess.DeviceID
	if !e.cfg.LoginLimiter.Allow(dev, limits.Cast) {
		return cs.Send(wire.Refuse(wire.ReasonRateLimited)) == nil
	}
	// the broadcast must be live and in the caller's account; another account's, an ended one and an
	// unknown one are one answer
	record, _, ok := e.cfg.Dir.LiveBroadcast(acct, m.BroadcastID)
	if !ok {
		e.cfg.Log.Info("cast-refused", logx.Device(dev), logx.Text("why", "unavailable"))
		return cs.Send(wire.Refuse(wire.ReasonUnavailable)) == nil
	}
	// the receiver must be a device of the same account that declared the role and is online. An
	// offline one, one of another account, an unknown one and one with no `receiver` role are ONE
	// answer - the same bytes, at once - so account membership leaks nothing (rule 1, rule 4)
	rsink, ok := e.cfg.Dir.Receiver(acct, m.ReceiverDeviceID)
	if !ok {
		e.cfg.Log.Info("cast-unavailable", logx.Device(dev), logx.Broadcast(m.BroadcastID))
		return cs.Send(&wire.CastResult{BroadcastID: m.BroadcastID, ReceiverDeviceID: m.ReceiverDeviceID,
			Accepted: false, Reason: wire.CastUnavailable}) == nil
	}
	if err := e.cast.Add(acct, m.BroadcastID, m.ReceiverDeviceID, dev); err != nil {
		return cs.Send(wire.Refuse(wire.ReasonRateLimited)) == nil
	}
	// the offer carries the record as stored and never `access` (item R)
	if err := rsink.Send(&wire.CastOffer{Broadcast: withoutAccess(record), FromDeviceID: dev}); err != nil {
		e.cast.Answer(m.BroadcastID, m.ReceiverDeviceID) // nobody will answer: take the offer back
		return cs.Send(&wire.CastResult{BroadcastID: m.BroadcastID, ReceiverDeviceID: m.ReceiverDeviceID,
			Accepted: false, Reason: wire.CastUnavailable}) == nil
	}
	e.cfg.Log.Info("cast-offered", logx.Device(dev), logx.Device(m.ReceiverDeviceID), logx.Broadcast(m.BroadcastID))
	return true
}

// withoutAccess drops a top-level `access` member from a broadcast record: a cast offer never carries
// one. A broadcast record has none by contract (6.3), so this changes bytes only for a producer that
// put one there.
func withoutAccess(raw json.RawMessage) json.RawMessage {
	rec, err := wire.ParseRecord(raw)
	if err != nil || !rec.Has("access") {
		return raw
	}
	rec.Delete("access")
	return rec.Marshal()
}

// handleCastAnswer is the receiver's `cast-answer`: forwarded to every caster that has an offer
// pending for this (broadcast, receiver) as a `cast-result`. An answer with no pending offer - a late
// one, or one nobody asked for - is dropped.
func (e *Engine) handleCastAnswer(sess *Session, m *wire.CastAnswer) {
	e.expireCasts() // an offer past its time is a timeout, whatever the answer says now
	for _, p := range e.cast.Answer(m.BroadcastID, sess.DeviceID) {
		e.sendCastResult(p, m.Accepted, castReason(m.Accepted, m.Reason))
	}
	e.cfg.Log.Info("cast-answered", logx.Device(sess.DeviceID), logx.Broadcast(m.BroadcastID))
}

// handleCastStop forwards `cast-stop` to the other end of the pair, inside the account. A caster's
// stop goes to the receiver it names; a receiver stopping by itself (its own id in the envelope) goes
// to the broadcaster of that broadcast, if it is still live.
func (e *Engine) handleCastStop(sess *Session, m *wire.CastStop) {
	acct, dev := sess.Account, sess.DeviceID
	var target string
	if m.ReceiverDeviceID != dev {
		target = m.ReceiverDeviceID
	} else if _, owner, ok := e.cfg.Dir.LiveBroadcast(acct, m.BroadcastID); ok {
		target = owner
	}
	if target == "" {
		return
	}
	if sink, ok := e.cfg.Dir.Peer(acct, target); ok {
		_ = sink.Send(&wire.CastStop{BroadcastID: m.BroadcastID, ReceiverDeviceID: m.ReceiverDeviceID})
		e.cfg.Log.Info("cast-stopped", logx.Device(dev), logx.Broadcast(m.BroadcastID))
	}
}

func (e *Engine) sendCastResult(p cast.Pending, accepted bool, reason string) {
	if sink, ok := e.cfg.Dir.Peer(p.Account, p.Caster); ok {
		_ = sink.Send(&wire.CastResult{BroadcastID: p.BroadcastID, ReceiverDeviceID: p.Receiver, Accepted: accepted, Reason: reason})
	}
}

// expireCasts turns every offer whose time has passed into `cast-result {timeout}` for its caster.
// It runs on the engine's sweep and before an answer is taken.
func (e *Engine) expireCasts() {
	for _, p := range e.cast.Expire() {
		e.cfg.Log.Info("cast-timeout", logx.Device(p.Caster), logx.Broadcast(p.BroadcastID))
		e.sendCastResult(p, false, wire.CastTimeout)
	}
}

// castDeviceGone runs when a device's control stream ends: offers it made are dropped, and the casters
// of offers made to it are told the receiver is unavailable - no answer will ever come.
func (e *Engine) castDeviceGone(deviceID string) {
	for _, p := range e.cast.DropDevice(deviceID) {
		e.sendCastResult(p, false, wire.CastUnavailable)
	}
}

// PendingCasts is how many cast offers wait for an answer (a count, never a list).
func (e *Engine) PendingCasts() int { return e.cast.Len() }
