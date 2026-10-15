package wire

// Reason is a `refused.reason` of DEVICE-EXCHANGE 7.10: kebab-case, frozen with the wire. Sixteen
// exist (the ten of 0.20 and the six of 19 - accounts, invitations, passwords); a client shows an
// unknown value as a generic refusal.
type Reason string

// The sixteen reasons. Refuse is the only way a `refused` envelope is built, and a source scan
// (guard_test.go) fails any other construction and any Refuse argument that is not one of these.
const (
	ReasonBadCredentials        Reason = "bad-credentials"
	ReasonDeviceRevoked         Reason = "device-revoked"
	ReasonVersionUnsupported    Reason = "version-unsupported"
	ReasonTLSRequired           Reason = "tls-required"
	ReasonIDCollision           Reason = "id-collision"
	ReasonCapacity              Reason = "capacity"
	ReasonPortUnavailable       Reason = "port-unavailable"
	ReasonUnavailable           Reason = "unavailable"
	ReasonRateLimited           Reason = "rate-limited"
	ReasonProtectionUnsupported Reason = "protection-unsupported"

	// DEVICE-EXCHANGE 19 (0.21): registration by invitation, kinds of account, limits, passwords.

	// ReasonInvitationInvalid is an unknown, used or withdrawn one-time invitation, a wrong shared
	// invitation password, or a shared password while it is off - one answer for all of them (item AQ).
	ReasonInvitationInvalid Reason = "invitation-invalid"
	// ReasonLoginTaken is a registration whose login exists; told only after the invitation was proven.
	ReasonLoginTaken Reason = "login-taken"
	// ReasonPasswordPolicy is a new password outside the policy of 1.2 (register, password-change).
	ReasonPasswordPolicy Reason = "password-policy"
	// ReasonLimitReached is a request that would cross a limit of the operator (item AS).
	ReasonLimitReached Reason = "limit-reached"
	// ReasonNotPermitted is an operation the account's kind does not allow - a guest's publish (item AO).
	ReasonNotPermitted Reason = "not-permitted"
	// ReasonSignedOut is a `hello` with a token that died through a password change or reset (item AT).
	ReasonSignedOut Reason = "signed-out"
)

// AllReasons is the closed list of 7.10, in the order of the contract.
var AllReasons = []Reason{
	ReasonBadCredentials, ReasonDeviceRevoked, ReasonVersionUnsupported, ReasonTLSRequired,
	ReasonIDCollision, ReasonCapacity, ReasonPortUnavailable, ReasonUnavailable, ReasonRateLimited, ReasonProtectionUnsupported,
	ReasonInvitationInvalid, ReasonLoginTaken, ReasonPasswordPolicy, ReasonLimitReached, ReasonNotPermitted, ReasonSignedOut,
}

// Valid reports whether r is one of the sixteen.
func (r Reason) Valid() bool {
	for _, k := range AllReasons {
		if r == k {
			return true
		}
	}
	return false
}

// The reasons a receiver may put in `cast-result` and `cast-answer` (DEVICE-EXCHANGE 7.8 and item
// R). They are not `refused` reasons.
const (
	CastUnavailable = "unavailable"
	CastDeclined    = "declined"
	CastTimeout     = "timeout"
	CastUnsupported = "unsupported"
)

// Refuse builds a `refused` envelope. The refusal carries the server's own schemaVersion (item P),
// which is also what answers a peer that spoke the legacy one.
func Refuse(r Reason) *Refused {
	if !r.Valid() {
		panic("wire: Refuse with a reason outside the sixteen of DEVICE-EXCHANGE 7.10: " + string(r))
	}
	return &Refused{Head: Head{SchemaVersion, TypeRefused}, Reason: string(r)}
}
