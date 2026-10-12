package directory

import (
	"errors"
	"fms/exchange-server/internal/wire"
	"testing"
)

func TestProtectedAccessIsFilteredPerEnrolledDeviceOnListAndPush(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	observer := e.online(alice, devA2)
	e.d.Subscribe(alice, devA2, observer)
	e.publish(alice, devA1, resource(resR1, devA1, `,"protection":{"scheme":"pin-v1"},"shareId":"`+shrS1+`"`))
	_, _, records, _ := e.d.SnapshotFor(alice, devA2)
	check := func(raw []byte, access bool) {
		t.Helper()
		r, err := wire.ParseRecord(raw)
		if err != nil || !r.Has("protection") || r.Has("access") != access {
			t.Fatal("protection or access gate mismatch")
		}
		if !access && (r.Has("shareId") || r.Has("publicPort")) {
			t.Fatal("unsupported device received an alternate access path")
		}
	}
	check(records[0], false)
	changes := observer.changes(t)
	check(changes[len(changes)-1].Upserts.Resources[0], false)
	info := devs()[1]
	info.Capabilities = []string{wire.PinCapability}
	e.d.DeviceEnrolled(info)
	observer = e.online(alice, devA2)
	e.d.Subscribe(alice, devA2, observer)
	_, _, records, _ = e.d.SnapshotFor(alice, devA2)
	check(records[0], true)
	e.publish(alice, devA1, resource(resR1, devA1, `,"protection":{"scheme":"pin-v1"}`))
	changes = observer.changes(t)
	check(changes[len(changes)-1].Upserts.Resources[0], true)
	_, _, records, _ = e.d.SnapshotFor(alice, "unknown")
	check(records[0], false)
}

func TestUnknownOrSecretBearingProtectionIsRefusedWithoutPublishing(t *testing.T) {
	e := newEnv(t, nil)
	e.online(alice, devA1)
	for _, protection := range []string{`null`, `[]`, `{}`, `{"scheme":"future"}`, `{"scheme":"future","scheme":"pin-v1"}`, `{"scheme":"pin-v1","pin":"1234"}`} {
		_, err := e.d.Publish(alice, devA1, resource(resR1, devA1, `,"protection":`+protection))
		if !errors.Is(err, ErrProtectionUnsupported) || Reason(err) != wire.ReasonProtectionUnsupported {
			t.Fatal("unsafe protection was not refused")
		}
	}
	_, _, records, _ := e.d.SnapshotFor(alice, devA1)
	if len(records) != 0 {
		t.Fatal("a refused protected resource was published")
	}
}
