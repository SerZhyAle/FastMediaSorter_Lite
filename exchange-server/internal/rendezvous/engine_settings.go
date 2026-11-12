package rendezvous

import (
	"errors"

	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/settings"
	"fms/exchange-server/internal/wire"
)

// This file is ticket L0296: the application settings standard of DEVICE-EXCHANGE 1.12 (items EN to EP).
// Both frames run on the control stream and are answered by exactly one frame. The checks run in the
// order of item EO - a guest, the appId, the formatVersion, the payload, then (a get) whether a standard
// is stored - so two servers answer one request alike. A refusal changes nothing.

// settingsAppIDCheck is the appId step: registered, and the one the device's product maps to (item EN).
func settingsAppIDCheck(sess *Session, appID string) bool {
	want := wire.SettingsAppIDFor(sess.Product)
	return want != "" && appID == want
}

func (e *Engine) handleSettingsSave(cs *ControlStream, sess *Session, m *wire.SettingsSaveStandard) bool {
	acct, dev := sess.Account, sess.DeviceID
	status := logx.AuditRefusal("incomplete")
	defer func() {
		ev := logx.AuditEvent{Op: "settings.save", Target: m.AppID, Remote: cs.st.raw.RemoteAddr().String(), Transport: cs.st.Transport(), Status: status, Details: "settings standard"}
		auditSession(&ev, sess)
		e.audit(ev)
	}()
	refuse := func(r wire.Reason) bool {
		status = logx.AuditRefusal(string(r))
		e.cfg.Log.Info("settings-save-refused", logx.Device(dev), logx.Login(acct), logx.Text("reason", string(r)))
		return cs.Send(wire.Refuse(r)) == nil
	}
	switch {
	case e.cfg.Accounts.IsGuest(acct):
		return refuse(wire.ReasonNotPermitted)
	case !settingsAppIDCheck(sess, m.AppID):
		return refuse(wire.ReasonAppIDMismatch)
	case *m.FormatVersion != wire.SettingsFormatVersion:
		return refuse(wire.ReasonVersionIncompatible)
	case !settings.CheckPayload(*m.FormatVersion, *m.SettingsPayload, wire.MaxSettingsPayload):
		return refuse(wire.ReasonInvalidPayload)
	}
	st, err := e.cfg.Settings.Save(acct, m.AppID, *m.FormatVersion, *m.SettingsPayload)
	if err != nil {
		e.cfg.Log.Error("settings-save-failed", logx.Device(dev), logx.Login(acct), logx.Text("error", err.Error()))
		return refuse(wire.ReasonUnavailable)
	}
	status = logx.AuditStatusOK
	e.cfg.Log.State("settings-saved", logx.Device(dev), logx.Login(acct), logx.Text("appId", st.AppID),
		logx.Int("version", int(st.Version)), logx.Int("bytes", len(st.Payload)))
	return cs.Send(&wire.SettingsSaved{AppID: st.AppID, Version: st.Version, UpdatedAt: st.UpdatedAt}) == nil
}

func (e *Engine) handleSettingsGet(cs *ControlStream, sess *Session, m *wire.SettingsGetStandard) bool {
	acct, dev := sess.Account, sess.DeviceID
	refuse := func(r wire.Reason) bool {
		e.cfg.Log.Info("settings-get-refused", logx.Device(dev), logx.Login(acct), logx.Text("reason", string(r)))
		return cs.Send(wire.Refuse(r)) == nil
	}
	switch {
	case e.cfg.Accounts.IsGuest(acct):
		return refuse(wire.ReasonNotPermitted)
	case !settingsAppIDCheck(sess, m.AppID):
		return refuse(wire.ReasonAppIDMismatch)
	}
	st, err := e.cfg.Settings.Get(acct, m.AppID)
	if errors.Is(err, settings.ErrNotFound) {
		return refuse(wire.ReasonNotFound)
	}
	if err != nil {
		return refuse(wire.ReasonUnavailable)
	}
	return cs.Send(&wire.SettingsStandardPayload{AppID: st.AppID, FormatVersion: st.FormatVersion,
		SettingsPayload: st.Payload, UpdatedAt: st.UpdatedAt}) == nil
}
