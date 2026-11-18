package wire

// Reason is a `refused.reason` of DEVICE-EXCHANGE 7.10: kebab-case, frozen with the wire. Twenty-three
// exist (the ten of 0.20, the six of 19 - accounts, invitations, passwords -, admin-disabled, the four of 1.12 item EP,
// `note-conflict` of 1.13 item FL and `ended` of 1.10 item DY); a client shows an unknown value as a generic refusal.
type Reason string

// The reasons. Refuse is the only way a `refused` envelope is built, and a source scan
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

	// ReasonAdminDisabled is an `admin-auth` when administration is turned off (DEVICE-EXCHANGE 1.4, ticket 245).
	ReasonAdminDisabled Reason = "admin-disabled"

	// DEVICE-EXCHANGE 1.12 item EP (ticket L0296): the application settings standard.

	// ReasonNotFound is a `settings_get_standard` for a pair that has no standard.
	ReasonNotFound Reason = "not-found"
	// ReasonInvalidPayload is a `settingsPayload` that is not one JSON object of the shape of item EO.
	ReasonInvalidPayload Reason = "invalid-payload"
	// ReasonAppIDMismatch is an `appId` that is not registered or not the one of the device's `product`.
	ReasonAppIDMismatch Reason = "app-id-mismatch"
	// ReasonVersionIncompatible is a `formatVersion` the server does not implement.
	ReasonVersionIncompatible Reason = "version-incompatible"

	// DEVICE-EXCHANGE 1.13 item FL (ticket L0321): the notes of an account.

	// ReasonNoteConflict is a `notes_put` whose `baseRevision` is not the stored record's revision (item FJ). The
	// refusal carries the stored record as `current` and nothing was stored; a client resolves it and never
	// shows it as a failure.
	ReasonNoteConflict Reason = "note-conflict"

	// DEVICE-EXCHANGE 1.10 item DY (ticket L0336): an ended broadcast at a tunnel.

	// ReasonEnded is a tunnel `connect` for a broadcast that ended and is still remembered (item DY).
	ReasonEnded Reason = "ended"
)

// AllReasons is the closed list of 7.10, in the order of the contract.
var AllReasons = []Reason{
	ReasonBadCredentials, ReasonDeviceRevoked, ReasonVersionUnsupported, ReasonTLSRequired,
	ReasonIDCollision, ReasonCapacity, ReasonPortUnavailable, ReasonUnavailable, ReasonRateLimited, ReasonProtectionUnsupported,
	ReasonInvitationInvalid, ReasonLoginTaken, ReasonPasswordPolicy, ReasonLimitReached, ReasonNotPermitted, ReasonSignedOut,
	ReasonAdminDisabled,
	ReasonNotFound, ReasonInvalidPayload, ReasonAppIDMismatch, ReasonVersionIncompatible,
	ReasonNoteConflict,
	ReasonEnded,
}

// Valid reports whether r is one of the twenty-three.
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
		panic("wire: Refuse with a reason outside the twenty-three of DEVICE-EXCHANGE 7.10: " + string(r))
	}
	return &Refused{Head: Head{SchemaVersion, TypeRefused}, Reason: string(r)}
}
