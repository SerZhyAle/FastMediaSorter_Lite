package admin

import "testing"

func TestHostSettingsAreValidatedBeforeSaving(t *testing.T) {
	on, days := true, 7
	called := 0
	ops := &Ops{SaveHostSettings: func(got bool, n int) error {
		called++
		if !got || n != 7 {
			t.Fatal(got, n)
		}
		return nil
	}}
	if !ops.Handle(Request{Op: OpHostSettings, KeepAwake: &on, LogRetentionDays: &days}).OK || called != 1 {
		t.Fatal("valid setting not saved")
	}
	days = 0
	if ops.Handle(Request{Op: OpHostSettings, KeepAwake: &on, LogRetentionDays: &days}).OK || called != 1 {
		t.Fatal("invalid setting saved")
	}
	if ops.Handle(Request{Op: OpHostSettings}).OK {
		t.Fatal("missing settings accepted")
	}
}
