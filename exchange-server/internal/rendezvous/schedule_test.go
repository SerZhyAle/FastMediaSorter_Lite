package rendezvous_test

import (
	"encoding/json"
	"strings"
	"testing"
	"time"

	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Ticket L0338: remote schedule routing over the exchange (DEVICE-EXCHANGE 1.15 items FQ to FV).
// The server forwards schedule-request and schedule-result frames without opening boxes,
// enforces the check order of item FQ, lists `schedule` only with `sealed`, and keeps nothing.

func scheduleEnv(t *testing.T, mutate func(*testkit.Options), logins ...string) *testkit.Env {
	t.Helper()
	if len(logins) == 0 {
		logins = []string{"alice"}
	}
	o := looseOpts(logins...)
	o.Sealed = true
	o.Schedule = true
	if mutate != nil {
		mutate(&o)
	}
	return testkit.StartServer(t, o)
}

func TestScheduleFeatureAdvertisedOnlyWithSealed(t *testing.T) {
	cases := []struct {
		name     string
		sealed   bool
		schedule bool
		want     bool
	}{
		{"both on", true, true, true},
		{"schedule on sealed off", false, true, false},
		{"schedule off sealed on", true, false, false},
		{"both off", false, false, false},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			env := scheduleEnv(t, func(o *testkit.Options) {
				o.Sealed = tc.sealed
				o.Schedule = tc.schedule
			})
			d := env.NewDevice("alice")
			en := mustEnroll(t, d)
			hasSchedule := strings.Contains(","+strings.Join(en.Features, ",")+",", ","+wire.FeatureSchedule+",")
			if hasSchedule != tc.want {
				t.Fatalf("features %v: got schedule=%v, want %v", en.Features, hasSchedule, tc.want)
			}
		})
	}
}

func TestScheduleFramesIgnoredWhenFeatureOff(t *testing.T) {
	env := scheduleEnv(t, func(o *testkit.Options) {
		o.Schedule = false
	})
	d := env.NewDevice("alice")
	mustEnroll(t, d)
	ctl, err := d.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(d.Close)

	// An envelope of a type the server does not list is ignored like an unknown type (7.1, EO, FQ).
	// The stream stays open.
	req := &wire.ScheduleRequest{
		ToDeviceID: "any-dev",
		RequestID:  "req-1",
		Box:        json.RawMessage(`{"v":1}`),
	}
	if err := ctl.Send(req); err != nil {
		t.Fatalf("send: %v", err)
	}
	// Send a keepalive to prove stream remains alive and responsive
	if _, err := ctl.Request(&wire.Keepalive{}, 2*time.Second, wire.TypeKeepalive); err != nil {
		t.Fatalf("keepalive after ignored schedule frame: %v", err)
	}
}

func TestScheduleRequestRefusedForGuest(t *testing.T) {
	env := scheduleEnv(t, nil, "alice")
	// Make a guest account
	if _, _, err := env.Ops.AddAccountKind("visitor", testkit.TestPassword, "guest"); err != nil {
		t.Fatal(err)
	}
	d := env.NewDevice("visitor")
	mustEnroll(t, d)
	ctl, err := d.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(d.Close)

	req := &wire.ScheduleRequest{
		ToDeviceID: "target-device",
		RequestID:  "req-guest-1",
		Box:        json.RawMessage(`{"v":1}`),
	}
	r, err := ctl.Request(req, 3*time.Second, wire.TypeScheduleResult, wire.TypeRefused)
	if err != nil {
		t.Fatalf("request: %v", err)
	}
	res, ok := r.(*wire.ScheduleResult)
	if !ok {
		t.Fatalf("expected *wire.ScheduleResult, got %T (%+v)", r, r)
	}
	if res.Reason != wire.ScheduleReasonNotPermitted {
		t.Fatalf("expected reason %q, got %q", wire.ScheduleReasonNotPermitted, res.Reason)
	}
	if res.RequestID != "req-guest-1" {
		t.Fatalf("expected requestId req-guest-1, got %q", res.RequestID)
	}
	if res.FromDeviceID != "target-device" {
		t.Fatalf("expected fromDeviceId target-device, got %q", res.FromDeviceID)
	}
	if len(res.Box) != 0 {
		t.Fatalf("refusal must carry no box, got %s", string(res.Box))
	}
}

func TestScheduleRequestTargetUnavailableAndUnsupported(t *testing.T) {
	env := scheduleEnv(t, nil, "alice")

	// Target 1: not a device of the account -> unavailable
	reqDev := env.NewDevice("alice")
	mustEnroll(t, reqDev)
	reqCtl, err := reqDev.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(reqDev.Close)

	r, err := reqCtl.Request(&wire.ScheduleRequest{
		ToDeviceID: "dev-does-not-exist",
		RequestID:  "req-unavail-1",
		Box:        json.RawMessage(`{"v":1}`),
	}, 3*time.Second, wire.TypeScheduleResult)
	if err != nil {
		t.Fatalf("request non-existent target: %v", err)
	}
	res1 := r.(*wire.ScheduleResult)
	if res1.Reason != wire.ScheduleReasonUnavailable {
		t.Fatalf("expected unavailable for non-existent device, got %q", res1.Reason)
	}

	// Target 2: device of the account, but does NOT declare schedule-v1 -> unsupported
	noSchedDev := env.NewDevice("alice")
	noSchedDev.Capabilities = []string{"e2e-v1"} // no schedule-v1
	mustEnroll(t, noSchedDev)
	noSchedCtl, err := noSchedDev.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(noSchedDev.Close)

	r2, err := reqCtl.Request(&wire.ScheduleRequest{
		ToDeviceID: noSchedDev.ID,
		RequestID:  "req-unsupp-1",
		Box:        json.RawMessage(`{"v":1}`),
	}, 3*time.Second, wire.TypeScheduleResult)
	if err != nil {
		t.Fatalf("request target without schedule-v1: %v", err)
	}
	res2 := r2.(*wire.ScheduleResult)
	if res2.Reason != wire.ScheduleReasonUnsupported {
		t.Fatalf("expected unsupported for target without capability, got %q", res2.Reason)
	}

	// Target 3: device has schedule-v1, but is OFFLINE -> unavailable
	schedDev := env.NewDevice("alice")
	schedDev.Capabilities = []string{"schedule-v1", "e2e-v1"}
	mustEnroll(t, schedDev)
	// Do not bring schedDev online (stays offline)

	r3, err := reqCtl.Request(&wire.ScheduleRequest{
		ToDeviceID: schedDev.ID,
		RequestID:  "req-offline-1",
		Box:        json.RawMessage(`{"v":1}`),
	}, 3*time.Second, wire.TypeScheduleResult)
	if err != nil {
		t.Fatalf("request offline target: %v", err)
	}
	res3 := r3.(*wire.ScheduleResult)
	if res3.Reason != wire.ScheduleReasonUnavailable {
		t.Fatalf("expected unavailable for offline target, got %q", res3.Reason)
	}
	_ = noSchedCtl
}

func TestScheduleRequestForwardingAndResultRoundTrip(t *testing.T) {
	env := scheduleEnv(t, nil, "alice")

	requester := env.NewDevice("alice")
	mustEnroll(t, requester)
	reqCtl, err := requester.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(requester.Close)

	target := env.NewDevice("alice")
	target.Capabilities = []string{"schedule-v1", "e2e-v1"}
	mustEnroll(t, target)
	targetCtl, err := target.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(target.Close)

	// Requester sends schedule-request
	boxPayload := `{"v":1,"ct":"opaque-sealed-data"}`
	err = reqCtl.Send(&wire.ScheduleRequest{
		ToDeviceID: target.ID,
		RequestID:  "req-fwd-99",
		Box:        json.RawMessage(boxPayload),
	})
	if err != nil {
		t.Fatalf("send request: %v", err)
	}

	// Target receives the forwarded schedule-request with fromDeviceId added
	r, err := targetCtl.Await(3*time.Second, wire.TypeScheduleRequest)
	if err != nil {
		t.Fatalf("target await request: %v", err)
	}
	fwdReq := r.(*wire.ScheduleRequest)
	if fwdReq.FromDeviceID != requester.ID {
		t.Fatalf("forwarded fromDeviceId: got %q, want %q", fwdReq.FromDeviceID, requester.ID)
	}
	if fwdReq.ToDeviceID != target.ID {
		t.Fatalf("forwarded toDeviceId: got %q, want %q", fwdReq.ToDeviceID, target.ID)
	}
	if fwdReq.RequestID != "req-fwd-99" {
		t.Fatalf("forwarded requestId: got %q, want req-fwd-99", fwdReq.RequestID)
	}
	if string(fwdReq.Box) != boxPayload {
		t.Fatalf("forwarded box: got %s, want %s", string(fwdReq.Box), boxPayload)
	}

	// Target answers with schedule-result addressed to requester
	resultBox := `{"v":1,"outcome":"ok","operations":[]}`
	err = targetCtl.Send(&wire.ScheduleResult{
		ToDeviceID: requester.ID,
		RequestID:  "req-fwd-99",
		Box:        json.RawMessage(resultBox),
	})
	if err != nil {
		t.Fatalf("target send result: %v", err)
	}

	// Requester receives forwarded schedule-result with fromDeviceId = target.ID
	r2, err := reqCtl.Await(3*time.Second, wire.TypeScheduleResult)
	if err != nil {
		t.Fatalf("requester await result: %v", err)
	}
	fwdRes := r2.(*wire.ScheduleResult)
	if fwdRes.FromDeviceID != target.ID {
		t.Fatalf("result fromDeviceId: got %q, want %q", fwdRes.FromDeviceID, target.ID)
	}
	if fwdRes.RequestID != "req-fwd-99" {
		t.Fatalf("result requestId: got %q, want req-fwd-99", fwdRes.RequestID)
	}
	if string(fwdRes.Box) != resultBox {
		t.Fatalf("result box: got %s, want %s", string(fwdRes.Box), resultBox)
	}
}

func TestScheduleResultDroppedWhenRequesterOffline(t *testing.T) {
	env := scheduleEnv(t, nil, "alice")

	target := env.NewDevice("alice")
	target.Capabilities = []string{"schedule-v1", "e2e-v1"}
	mustEnroll(t, target)
	targetCtl, err := target.OnlineQuietly()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(target.Close)

	// Target sends a schedule-result for a device that is offline or does not exist
	err = targetCtl.Send(&wire.ScheduleResult{
		ToDeviceID: "non-existent-device",
		RequestID:  "req-orphan-1",
		Box:        json.RawMessage(`{"v":1,"outcome":"ok"}`),
	})
	if err != nil {
		t.Fatalf("send result: %v", err)
	}

	// Stream remains healthy and open
	if _, err := targetCtl.Request(&wire.Keepalive{}, 2*time.Second, wire.TypeKeepalive); err != nil {
		t.Fatalf("keepalive failed: %v", err)
	}
}
