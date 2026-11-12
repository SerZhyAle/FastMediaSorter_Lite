package rendezvous_test

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/settings"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Ticket L0296: the application settings standard of DEVICE-EXCHANGE 1.12 (items EN to EP) over TLS on
// loopback - the cases of the amendment's Conformance table a transcript cannot hold (8, 9, the bound of
// 5, the closed stream of item EO) and the feature of case 14.

const stdPayload = `{"version":1,"groups":{"appearance":{"theme":"dark"},"general":{"confirmDelete":true}}}`

func settingsOpts(logins ...string) testkit.Options {
	o := looseOpts(logins...)
	o.Settings = true
	return o
}

func intOf(n int) *int       { return &n }
func strOf(s string) *string { return &s }

func saveStd(t *testing.T, ctl *testkit.Control, app, payload string) wire.Envelope {
	t.Helper()
	ans, err := ctl.Request(&wire.SettingsSaveStandard{AppID: app, FormatVersion: intOf(1), SettingsPayload: strOf(payload)},
		3*time.Second, wire.TypeSettingsSaved, wire.TypeRefused)
	if err != nil {
		t.Fatal(err)
	}
	return ans
}

func savedVersion(t *testing.T, ans wire.Envelope) int64 {
	t.Helper()
	s, ok := ans.(*wire.SettingsSaved)
	if !ok {
		t.Fatalf("want settings_saved, got %+v", ans)
	}
	return s.Version
}

// TestSettingsFeatureIsListedOnlyWhenTheStoreExists is the server half of case 14: `settings` is in
// `welcome.features` exactly when the server keeps standards, and a server without them ignores the frames.
func TestSettingsFeatureIsListedOnlyWhenTheStoreExists(t *testing.T) {
	on := testkit.StartServer(t, settingsOpts("alice"))
	d := on.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	_, w, err := d.Hello()
	if err != nil {
		t.Fatal(err)
	}
	if wl, ok := w.(*wire.Welcome); !ok || !strings.Contains(","+strings.Join(wl.Features, ",")+",", ",settings,") {
		t.Fatalf("a server with the store must list settings: %+v", w)
	}

	off := testkit.StartServer(t, looseOpts("alice"))
	o := off.NewDevice("alice")
	if _, err := o.Enroll(); err != nil {
		t.Fatal(err)
	}
	ctl, err := o.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	if _, err := ctl.Request(&wire.SettingsGetStandard{AppID: "fms_win"}, 500*time.Millisecond, wire.TypeSettingsStandardPayload, wire.TypeRefused); err == nil {
		t.Fatal("a server without settings answered a settings frame")
	}
	if _, err := ctl.Request(&wire.Keepalive{}, 3*time.Second, wire.TypeKeepalive); err != nil {
		t.Fatalf("the ignored frame must leave the stream open: %v", err)
	}
}

// TestSettingsVersionSurvivesARestartAndTheAccountTakesItsStandards is cases 8 and 9.
func TestSettingsVersionSurvivesARestartAndTheAccountTakesItsStandards(t *testing.T) {
	env := testkit.StartServer(t, settingsOpts("alice", "bob"))
	d := env.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	ctl, err := d.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	if v := savedVersion(t, saveStd(t, ctl, "fms_win", stdPayload)); v != 1 {
		t.Fatalf("first save: version %d", v)
	}
	if v := savedVersion(t, saveStd(t, ctl, "fms_win", stdPayload)); v != 2 {
		t.Fatalf("second save: version %d", v)
	}
	ctl.Close()
	env.Stop()

	env2 := testkit.StartServer(t, func() testkit.Options { o := settingsOpts("alice", "bob"); o.DataDir = env.DataDir; return o }())
	d.Rebind(env2)
	ctl, err = d.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	if v := savedVersion(t, saveStd(t, ctl, "fms_win", stdPayload)); v != 3 {
		t.Fatalf("the save after a restart: version %d, want 3", v)
	}
	ans, err := ctl.Request(&wire.SettingsGetStandard{AppID: "fms_win"}, 3*time.Second, wire.TypeSettingsStandardPayload, wire.TypeRefused)
	if err != nil {
		t.Fatal(err)
	}
	if p, ok := ans.(*wire.SettingsStandardPayload); !ok || p.SettingsPayload != stdPayload {
		t.Fatalf("the get after a restart: %+v", ans)
	}

	b := env2.NewDevice("bob")
	if _, err := b.Enroll(); err != nil {
		t.Fatal(err)
	}
	bc, err := b.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	savedVersion(t, saveStd(t, bc, "fms_win", stdPayload))
	if err := env2.Ops.RemoveAccount("alice"); err != nil {
		t.Fatal(err)
	}
	if _, err := env2.Settings.Get("alice", "fms_win"); err != settings.ErrNotFound {
		t.Fatalf("a removed account kept its standard: %v", err)
	}
	if _, err := env2.Settings.Get("bob", "fms_win"); err != nil {
		t.Fatalf("another account's standard went with it: %v", err)
	}
	raw, err := os.ReadFile(filepath.Join(env2.DataDir, settings.File))
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(string(raw), `"account": "alice"`) {
		t.Fatal("the removed account's standard is still on disk")
	}
}

// TestSettingsPayloadAboveTheBoundIsRefusedAndTheFrameFits is the bound of case 5: one byte above 65536 is
// invalid-payload, the frame that carries it is within the authenticated limit, and exactly 65536 is kept.
func TestSettingsPayloadAboveTheBoundIsRefusedAndTheFrameFits(t *testing.T) {
	env := testkit.StartServer(t, settingsOpts("alice"))
	d := env.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	ctl, err := d.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	head, tail := `{"version":1,"groups":{"general":{"k":"`, `"}}}`
	fill := wire.MaxSettingsPayload - len(head) - len(tail)
	exact := head + strings.Repeat("a", fill) + tail
	over := head + strings.Repeat("a", fill+1) + tail
	if _, err := wire.Frame(&wire.SettingsSaveStandard{AppID: "fms_win", FormatVersion: intOf(1), SettingsPayload: strOf(over)}); err != nil {
		t.Fatalf("the frame of an oversize payload must fit item T: %v", err)
	}
	refusedWith(t, saveStd(t, ctl, "fms_win", over), wire.ReasonInvalidPayload)
	if v := savedVersion(t, saveStd(t, ctl, "fms_win", exact)); v != 1 {
		t.Fatalf("a payload of exactly 65536 bytes: version %d", v)
	}
}

// TestSettingsFrameWithoutARequiredMemberClosesTheStream is item EO: a frame that lacks a required member
// or gives one the wrong type is invalid, closed with no refusal.
func TestSettingsFrameWithoutARequiredMemberClosesTheStream(t *testing.T) {
	env := testkit.StartServer(t, settingsOpts("alice"))
	d := env.NewDevice("alice")
	if _, err := d.Enroll(); err != nil {
		t.Fatal(err)
	}
	for _, body := range []string{
		`{"schemaVersion":2,"type":"settings_save_standard","appId":"fms_win","settingsPayload":"{}"}`,
		`{"schemaVersion":2,"type":"settings_save_standard","appId":"fms_win","formatVersion":1}`,
		`{"schemaVersion":2,"type":"settings_save_standard","appId":"fms_win","formatVersion":"1","settingsPayload":"{}"}`,
		`{"schemaVersion":2,"type":"settings_get_standard"}`,
	} {
		ctl, err := d.OnlineQuietly()
		if err != nil {
			t.Fatal(err)
		}
		if err := ctl.Conn.SendRaw(testkit.RawFrame(body)); err != nil {
			t.Fatal(err)
		}
		select {
		case <-ctl.Closed():
		case <-time.After(3 * time.Second):
			t.Fatalf("%s: the stream must be closed", body)
		}
		if ans, err := ctl.Await(0, wire.TypeRefused); err == nil {
			t.Fatalf("%s: a malformed frame is closed with no answer, got %+v", body, ans)
		}
	}
}

// TestSettingsStoreCheckPayload holds the shape check of item EO without a server.
func TestSettingsStoreCheckPayload(t *testing.T) {
	for _, c := range []struct {
		payload string
		want    bool
	}{
		{stdPayload, true},
		{`{"version":1,"groups":{}}`, true},
		{` {"version":1,"groups":{"a":{}}} `, true},
		{`{"version":2,"groups":{}}`, false},
		{`{"version":"1","groups":{}}`, false},
		{`{"version":1.0,"groups":{}}`, false},
		{`{"version":1,"groups":[]}`, false},
		{`{"version":1,"groups":{"a":[]}}`, false},
		{`{"version":1,"groups":{"a":null}}`, false},
		{`{"version":1,"groups":{}} {}`, false},
		{`null`, false},
		{``, false},
		{`{"groups":{}}`, false},
	} {
		if got := settings.CheckPayload(1, c.payload, wire.MaxSettingsPayload); got != c.want {
			t.Errorf("%q: %v, want %v", c.payload, got, c.want)
		}
	}
}
