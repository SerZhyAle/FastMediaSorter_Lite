package deploy

import (
	"errors"
	"reflect"
	"testing"
)

func TestKeepAwakeLifecycle(t *testing.T) {
	for _, enabled := range []bool{false, true} {
		var calls []bool
		release, active, err := keepAwake(enabled, func(on bool) error { calls = append(calls, on); return nil }, func() {}, func() {})
		if err != nil || active != enabled {
			t.Fatalf("active=%v err=%v", active, err)
		}
		release()
		release()
		if enabled && !reflect.DeepEqual(calls, []bool{true, false}) {
			t.Fatal(calls)
		}
		if !enabled && len(calls) != 0 {
			t.Fatal(calls)
		}
	}
}

func TestKeepAwakeFailureIsReported(t *testing.T) {
	release, active, err := keepAwake(true, func(bool) error { return errors.New("denied") }, func() {}, func() {})
	release()
	if active || err == nil {
		t.Fatal("failed request reported as active")
	}
}
