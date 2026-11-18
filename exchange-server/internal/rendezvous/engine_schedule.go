package rendezvous

import (
	"crypto/sha256"
	"encoding/hex"

	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/wire"
)

// This file is ticket L0338: remote schedule routing over the control stream
// (DEVICE-EXCHANGE 1.15 items FQ to FV). The server forwards sealed requests and answers
// between devices of the same account, never opens or logs a box, and keeps nothing.

func shortRequestHash(requestID string) string {
	sum := sha256.Sum256([]byte(requestID))
	return hex.EncodeToString(sum[:4])
}

func (e *Engine) handleScheduleRequest(cs *ControlStream, sess *Session, m *wire.ScheduleRequest) bool {
	acct, dev := sess.Account, sess.DeviceID

	// Check 1: a guest account is refused with a schedule-result {reason: "not-permitted"}
	if e.cfg.Accounts.IsGuest(acct) {
		e.cfg.Log.Info("schedule-refused", logx.Device(dev), logx.Text("why", "guest"), logx.Text("req", shortRequestHash(m.RequestID)))
		_ = cs.Send(&wire.ScheduleResult{
			FromDeviceID: m.ToDeviceID,
			RequestID:    m.RequestID,
			Reason:       wire.ScheduleReasonNotPermitted,
		})
		return true
	}

	// Check 2: frame members (checked during decode and validate)

	// Check 3: whether toDeviceId is a device of the account (unavailable)
	// Check 4: schedule-v1 capability (unsupported)
	// Check 5: presence (offline -> unavailable)
	sink, reason := e.cfg.Dir.ScheduleTarget(acct, m.ToDeviceID)
	if reason != "" {
		e.cfg.Log.Info("schedule-refused", logx.Device(dev), logx.Text("why", reason), logx.Text("req", shortRequestHash(m.RequestID)))
		_ = cs.Send(&wire.ScheduleResult{
			FromDeviceID: m.ToDeviceID,
			RequestID:    m.RequestID,
			Reason:       reason,
		})
		return true
	}

	// Forward unchanged to target device control stream, adding fromDeviceId
	fwd := &wire.ScheduleRequest{
		FromDeviceID: dev,
		ToDeviceID:   m.ToDeviceID,
		RequestID:    m.RequestID,
		Box:          m.Box,
	}
	if err := sink.Send(fwd); err != nil {
		e.cfg.Log.Info("schedule-send-failed", logx.Device(dev), logx.Text("req", shortRequestHash(m.RequestID)))
		_ = cs.Send(&wire.ScheduleResult{
			FromDeviceID: m.ToDeviceID,
			RequestID:    m.RequestID,
			Reason:       wire.ScheduleReasonUnavailable,
		})
		return true
	}
	e.cfg.Log.Info("schedule-forwarded", logx.Device(dev), logx.Device(m.ToDeviceID), logx.Text("req", shortRequestHash(m.RequestID)))
	return true
}

func (e *Engine) handleScheduleResult(sess *Session, m *wire.ScheduleResult) bool {
	acct, dev := sess.Account, sess.DeviceID
	// Answering device to server: toDeviceId (the requester), requestId, box.
	// The server forwards it to the requester's control stream with fromDeviceId (the answering device) in place of toDeviceId.
	// A schedule-result from a device whose toDeviceId is not an online device of the account is dropped with no answer.
	sink := e.cfg.Dir.DeviceSink(acct, m.ToDeviceID)
	if sink == nil {
		e.cfg.Log.Info("schedule-result-dropped", logx.Device(dev), logx.Device(m.ToDeviceID), logx.Text("req", shortRequestHash(m.RequestID)))
		return true
	}
	fwd := &wire.ScheduleResult{
		FromDeviceID: dev,
		RequestID:    m.RequestID,
		Reason:       m.Reason,
		Box:          m.Box,
	}
	_ = sink.Send(fwd)
	e.cfg.Log.Info("schedule-result-forwarded", logx.Device(dev), logx.Device(m.ToDeviceID), logx.Text("req", shortRequestHash(m.RequestID)))
	return true
}
