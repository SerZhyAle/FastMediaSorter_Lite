package wire

import (
	"encoding/json"
	"errors"
)

// FeatureSchedule is the `welcome`/`enrolled` feature of DEVICE-EXCHANGE 1.15 item FQ:
// listed only together with FeatureSealed.
const FeatureSchedule = "schedule"

// CapabilitySchedule is the capability declared at enroll by a device that answers schedule requests.
const CapabilitySchedule = "schedule-v1"

// The reasons of a `schedule-result` the server sends when it cannot deliver a request (DEVICE-EXCHANGE
// 1.15 item FQ). They travel in clear in the result and are NOT `refused` reasons of 7.10 - the server
// never answers these frames with a bare `refused` - so they stay plain strings beside the closed Reason
// list, as the Cast* reasons do, and that list does not change.
const (
	// ScheduleReasonNotPermitted is a request of a `guest` account (item AO).
	ScheduleReasonNotPermitted = "not-permitted"
	// ScheduleReasonUnavailable is a target that is not a device of the account, or is offline - one
	// answer for both, so the frame does not tell whether an id exists.
	ScheduleReasonUnavailable = "unavailable"
	// ScheduleReasonUnsupported is a target device that does not declare `schedule-v1`.
	ScheduleReasonUnsupported = "unsupported"
)

const (
	TypeScheduleRequest = "schedule-request"
	TypeScheduleResult  = "schedule-result"
)

// ScheduleRequest is the requester-to-server request (and forwarded server-to-device).
// Carries toDeviceId, requestId, box, and when forwarded adds fromDeviceId.
type ScheduleRequest struct {
	Head
	FromDeviceID string          `json:"fromDeviceId,omitempty"`
	ToDeviceID   string          `json:"toDeviceId"`
	RequestID    string          `json:"requestId"`
	Box          json.RawMessage `json:"box"`
}

// TypeName is "schedule-request".
func (*ScheduleRequest) TypeName() string { return TypeScheduleRequest }

func (m *ScheduleRequest) validate() error {
	if err := required("toDeviceId", m.ToDeviceID, maxTunnelID); err != nil {
		return err
	}
	if err := validateRequestID(m.RequestID); err != nil {
		return err
	}
	if len(m.Box) == 0 || m.Box[0] != '{' {
		return errors.New("box: must be a JSON object")
	}
	return nil
}

// ScheduleResult is the answer in two hops:
// - answering device to server: toDeviceId (the requester), requestId, box.
// - server to requester: fromDeviceId (the answering device), requestId, box or reason.
type ScheduleResult struct {
	Head
	FromDeviceID string          `json:"fromDeviceId,omitempty"`
	ToDeviceID   string          `json:"toDeviceId,omitempty"`
	RequestID    string          `json:"requestId"`
	Reason       string          `json:"reason,omitempty"`
	Box          json.RawMessage `json:"box,omitempty"`
}

// TypeName is "schedule-result".
func (*ScheduleResult) TypeName() string { return TypeScheduleResult }

func (m *ScheduleResult) validate() error {
	if err := validateRequestID(m.RequestID); err != nil {
		return err
	}
	if len(m.Box) == 0 && m.Reason == "" {
		return errors.New("either box or reason is required")
	}
	if len(m.Box) != 0 && m.Box[0] != '{' {
		return errors.New("box: must be a JSON object")
	}
	return nil
}

func validateRequestID(s string) error {
	if len(s) == 0 || len(s) > 64 {
		return errors.New("requestId: must be 1 to 64 characters")
	}
	for i := 0; i < len(s); i++ {
		c := s[i]
		if !((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-') {
			return errors.New("requestId: invalid character")
		}
	}
	return nil
}
