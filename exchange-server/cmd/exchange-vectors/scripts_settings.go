package main

import (
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// settingsPayload is the payload of the catalog's vectors 01 and 04 (DEVICE-EXCHANGE 1.12 item EO).
const settingsPayload = `{"version":1,"groups":{"appearance":{"theme":"dark","density":"compact"},"general":{"confirmDelete":true}}}`

func strPtr(s string) *string { return &s }

// scenarioSettingsStandard is the server side of the application settings standard (DEVICE-EXCHANGE 1.12,
// items EN to EP, ticket L0296): the cases 1 to 7 of the amendment's Conformance table that a transcript
// can hold, and `settings` in `welcome.features` (the server half of case 14). Case 8 (a version kept across a restart) and case 9 (erased with the account) are
// tests of the server (internal/rendezvous), since a transcript has one server and one life.
func scenarioSettingsStandard() *transcript {
	r := newRig("settings-standard", rigOpts{settings: true, logins: []string{account, "bobby"}})
	defer mustClose(r)
	tr := r.newTranscript("settings-standard",
		"The application settings standard: one JSON document per account and appId, kept on disk. A member's device saves it with "+
			"settings_save_standard and reads it with settings_get_standard on the control stream; each is answered by exactly one frame. "+
			"The appId is the one the device's product maps to (fms-windows -> fms_win, fms-android -> fms_a); a save replaces the standard "+
			"as a whole and moves version by one; the checks run in the order guest, appId, formatVersion, settingsPayload, stored. "+
			"`welcome.features` carries `settings`.")

	a, aDev, _ := r.enroll(tr, "pc-control", account, "Vector PC", "fms-windows", roles("resource-consumer"), "")
	r.control(a, aDev)

	a.send(&wire.SettingsGetStandard{AppID: "fms_win"})
	a.expectRefused(wire.ReasonNotFound)
	tr.note("case 2: a get for a pair with no standard is not-found")

	a.send(&wire.SettingsSaveStandard{AppID: "fms_win", FormatVersion: intPtr(1), SettingsPayload: strPtr(settingsPayload), Timestamp: "2026-11-10T02:00:00Z"})
	if s := a.expect(wire.TypeSettingsSaved).(*wire.SettingsSaved); s.Version != 1 || s.AppID != "fms_win" {
		failf("the first save: %+v", s)
	}
	a.send(&wire.SettingsGetStandard{AppID: "fms_win"})
	if p := a.expect(wire.TypeSettingsStandardPayload).(*wire.SettingsStandardPayload); p.SettingsPayload != settingsPayload || p.FormatVersion != 1 {
		failf("the get returned another payload: %+v", p)
	}
	tr.note("case 1: the first save is version 1, and a get returns the payload as it was received")

	a.send(&wire.SettingsSaveStandard{AppID: "fms_win", FormatVersion: intPtr(1), SettingsPayload: strPtr(`{"version":1,"groups":{}}`)})
	if s := a.expect(wire.TypeSettingsSaved).(*wire.SettingsSaved); s.Version != 2 {
		failf("the second save: %+v", s)
	}
	tr.note("case 8, first half: the next accepted save is version 2; a save replaces the standard, a group left out is gone")

	a.send(&wire.SettingsSaveStandard{AppID: "fms_win", FormatVersion: intPtr(99), SettingsPayload: strPtr(settingsPayload)})
	a.expectRefused(wire.ReasonVersionIncompatible)
	tr.note("case 3: a formatVersion the server does not implement is version-incompatible")

	a.send(&wire.SettingsSaveStandard{AppID: "fms_a", FormatVersion: intPtr(1), SettingsPayload: strPtr(settingsPayload)})
	a.expectRefused(wire.ReasonAppIDMismatch)
	a.send(&wire.SettingsGetStandard{AppID: "unknown_app"})
	a.expectRefused(wire.ReasonAppIDMismatch)
	tr.note("case 4: an appId that is not the one of the device's product, or not registered, is app-id-mismatch")

	for _, bad := range []string{`[1,2]`, `{"version":2,"groups":{}}`, `{"version":1}`, `{"version":1,"groups":{"general":1}}`} {
		a.send(&wire.SettingsSaveStandard{AppID: "fms_win", FormatVersion: intPtr(1), SettingsPayload: strPtr(bad)})
		a.expectRefused(wire.ReasonInvalidPayload)
	}
	tr.note("case 5: not one object, a version unequal to formatVersion, no groups object and a group that is not an object " +
		"are invalid-payload (a payload above 65536 bytes is a test of the server: its frame would fill this file)")

	a.send(&wire.SettingsGetStandard{AppID: "fms_win"})
	if p := a.expect(wire.TypeSettingsStandardPayload).(*wire.SettingsStandardPayload); p.SettingsPayload != `{"version":1,"groups":{}}` {
		failf("a refusal changed the standard: %+v", p)
	}
	tr.note("a refusal changes nothing: the standard is the one of the last accepted save")

	b, bDev, _ := r.enroll(tr, "bobby-control", "bobby", "Vector PC 2", "fms-windows", roles("resource-consumer"), "")
	r.controlAs("bobby", b, bDev)
	b.send(&wire.SettingsGetStandard{AppID: "fms_win"})
	b.expectRefused(wire.ReasonNotFound)
	tr.note("case 6: the standard of one account is never returned to another")

	p, pDev, _ := r.enroll(tr, "phone-control", account, "Vector phone", "fms-android", roles("resource-consumer"), "")
	r.control(p, pDev)
	p.send(&wire.SettingsGetStandard{AppID: "fms_a"})
	p.expectRefused(wire.ReasonNotFound)
	tr.note("case 6: two appId values of one account keep two standards")

	if _, _, err := r.env.Ops.AddAccountKind("visitor", testkit.TestPassword, "guest"); err != nil {
		failf("guest account: %v", err)
	}
	g, gDev, _ := r.enroll(tr, "guest-control", "visitor", "Vector guest", "fms-windows", roles("resource-consumer"), "")
	r.controlAs("visitor", g, gDev)
	g.send(&wire.SettingsSaveStandard{AppID: "unknown_app", FormatVersion: intPtr(99), SettingsPayload: strPtr("[]")})
	g.expectRefused(wire.ReasonNotPermitted)
	g.send(&wire.SettingsGetStandard{AppID: "fms_win"})
	g.expectRefused(wire.ReasonNotPermitted)
	tr.note("case 7: a guest is not-permitted on both frames, before any other check, and nothing is stored")
	return tr
}
