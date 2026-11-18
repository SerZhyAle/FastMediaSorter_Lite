package wire

import (
	"encoding/json"
	"testing"
)

func TestScheduleRequestCodecAndValidation(t *testing.T) {
	req := &ScheduleRequest{
		ToDeviceID: "dev-target-1",
		RequestID:  "req-12345",
		Box:        json.RawMessage(`{"v":1,"ct":"abc"}`),
	}
	data, err := Encode(req)
	if err != nil {
		t.Fatalf("encode: %v", err)
	}
	dec, err := Decode(data)
	if err != nil {
		t.Fatalf("decode: %v", err)
	}
	r, ok := dec.(*ScheduleRequest)
	if !ok {
		t.Fatalf("expected *ScheduleRequest, got %T", dec)
	}
	if r.ToDeviceID != req.ToDeviceID || r.RequestID != req.RequestID || string(r.Box) != string(req.Box) {
		t.Fatalf("mismatch: %+v vs %+v", r, req)
	}

	// Validation tests
	badReqID := &ScheduleRequest{
		ToDeviceID: "dev-target-1",
		RequestID:  "bad request id with spaces",
		Box:        json.RawMessage(`{"v":1}`),
	}
	if _, err := Encode(badReqID); err == nil {
		t.Error("expected error for bad requestId with spaces")
	}

	missingBox := &ScheduleRequest{
		ToDeviceID: "dev-target-1",
		RequestID:  "valid-req-id",
	}
	if _, err := Encode(missingBox); err == nil {
		t.Error("expected error for missing box")
	}
}

func TestScheduleResultCodecAndValidation(t *testing.T) {
	resOk := &ScheduleResult{
		FromDeviceID: "dev-answering-1",
		RequestID:    "req-12345",
		Box:          json.RawMessage(`{"v":1,"ct":"xyz"}`),
	}
	data, err := Encode(resOk)
	if err != nil {
		t.Fatalf("encode: %v", err)
	}
	dec, err := Decode(data)
	if err != nil {
		t.Fatalf("decode: %v", err)
	}
	r, ok := dec.(*ScheduleResult)
	if !ok {
		t.Fatalf("expected *ScheduleResult, got %T", dec)
	}
	if r.FromDeviceID != resOk.FromDeviceID || r.RequestID != resOk.RequestID || string(r.Box) != string(resOk.Box) {
		t.Fatalf("mismatch: %+v vs %+v", r, resOk)
	}

	resErr := &ScheduleResult{
		FromDeviceID: "dev-answering-1",
		RequestID:    "req-12345",
		Reason:       ScheduleReasonUnavailable,
	}
	data2, err := Encode(resErr)
	if err != nil {
		t.Fatalf("encode: %v", err)
	}
	dec2, err := Decode(data2)
	if err != nil {
		t.Fatalf("decode: %v", err)
	}
	r2, ok := dec2.(*ScheduleResult)
	if !ok || r2.Reason != ScheduleReasonUnavailable {
		t.Fatalf("expected ScheduleReasonUnavailable, got %+v", dec2)
	}

	// Missing both box and reason
	badRes := &ScheduleResult{
		FromDeviceID: "dev-answering-1",
		RequestID:    "req-12345",
	}
	if _, err := Encode(badRes); err == nil {
		t.Error("expected error when neither box nor reason is provided")
	}
}
