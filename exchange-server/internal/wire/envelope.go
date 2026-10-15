package wire

import "encoding/json"

// Envelope types of DEVICE-EXCHANGE 7.2-7.8, in the order of ticket 161 section 4.
const (
	TypeEnroll           = "enroll"
	TypeEnrolled         = "enrolled"
	TypeHello            = "hello"
	TypeWelcome          = "welcome"
	TypeRefused          = "refused"
	TypeKeepalive        = "keepalive"
	TypeBye              = "bye"
	TypeList             = "list"
	TypeDirectory        = "directory"
	TypeSubscribe        = "subscribe"
	TypeChanged          = "changed"
	TypePublish          = "publish"
	TypePublished        = "published"
	TypeUnpublish        = "unpublish"
	TypeBroadcastStart   = "broadcast-start"
	TypeBroadcastStarted = "broadcast-started"
	TypeBroadcastUpdate  = "broadcast-update"
	TypeBroadcastEnd     = "broadcast-end"
	TypePairingRequest   = "pairing-request"
	TypePairingCode      = "pairing-code"
	TypeRevoke           = "revoke"
	TypeConnect          = "connect"
	TypeConnected        = "connected"
	TypeOpen             = "open"
	TypeAttach           = "attach"
	TypeUpload           = "upload"
	TypeUploadAccepted   = "upload-accepted"
	TypeCast             = "cast"
	TypeCastOffer        = "cast-offer"
	TypeCastAnswer       = "cast-answer"
	TypeCastResult       = "cast-result"
	TypeCastStop         = "cast-stop"
	TypeClipboardPut     = "clipboard-put"
	TypeClipboardReady   = "clipboard-ready"
	TypeClipboardStored  = "clipboard-stored"
	TypeClipboardInfo    = "clipboard-info"
	TypeClipboardState   = "clipboard-state"
	TypeClipboardGet     = "clipboard-get"
	TypeClipboardValue   = "clipboard-value"
	TypeClipboardEmpty   = "clipboard-empty"
	TypeRegister         = "register"
	TypePasswordChange   = "password-change"
	TypePasswordChanged  = "password-changed"
	TypeAccountDelete    = "account-delete"
	TypeAccountDeleted   = "account-deleted"
)

// Head is the first two members of every envelope. It is embedded first in every typed envelope, so
// the encoder writes schemaVersion and type before the members of the table, in struct order: the
// byte-stable member order rule of ticket 161 rule 8.
//
// Head also carries the String and GoString of every envelope: a typed value handed to a formatter
// by mistake prints its type and nothing else, because enroll holds a password, enrolled and hello
// a token, and directory a resource's `access`.
type Head struct {
	SchemaVersion int    `json:"schemaVersion"`
	Type          string `json:"type"`
}

// String never prints a member.
func (h Head) String() string { return "wire." + h.Type }

// GoString never prints a member.
func (h Head) GoString() string { return "wire." + h.Type }

// Envelope is any typed envelope. Build one with its struct literal or a constructor; Encode fixes
// its Head, so a caller never writes the schema version or the type by hand.
type Envelope interface {
	// TypeName is the `type` the envelope is encoded with.
	TypeName() string
	head() *Head
	// validate checks the required members; Decode calls it.
	validate() error
}

func (h *Head) head() *Head { return h }

// Unknown is an envelope of a type this wire does not define. A reader ignores it (7.1); Raw is the
// whole body, kept so a relay of an unknown type is possible without understanding it.
type Unknown struct {
	Head
	Raw json.RawMessage `json:"-"`
}

// TypeName is the type that was read.
func (u *Unknown) TypeName() string { return u.Type }
func (u *Unknown) validate() error  { return nil }

// --- opening a stream (7.2) ---

// Enroll is sent once per device with the login and either the password or a pairing code.
type Enroll struct {
	Head
	Login            string          `json:"login"`
	Password         string          `json:"password,omitempty"`
	PairingCode      string          `json:"pairingCode,omitempty"`
	DeviceID         string          `json:"deviceId"`
	DeviceName       string          `json:"deviceName"`
	Product          string          `json:"product"`
	ProductVersion   string          `json:"productVersion,omitempty"`
	Platform         string          `json:"platform,omitempty"`
	Roles            []string        `json:"roles,omitempty"`
	Capabilities     []string        `json:"capabilities,omitempty"`
	Receiver         json.RawMessage `json:"receiver,omitempty"`
	KeepaliveSeconds int             `json:"keepaliveSeconds,omitempty"`
}

// TypeName is "enroll".
func (*Enroll) TypeName() string { return TypeEnroll }

// WelcomeBody is the members `welcome` and `enrolled` share (7.2).
type WelcomeBody struct {
	Account          string   `json:"account"`
	AccountKind      string   `json:"accountKind"`
	PublicEndpoint   string   `json:"publicEndpoint"`
	KeepaliveSeconds int      `json:"keepaliveSeconds"`
	Features         []string `json:"features"`
	ServerVersion    string   `json:"serverVersion"`
}

// Enrolled answers an `enroll`: the device token, once, and every member of `welcome`.
type Enrolled struct {
	Head
	DeviceToken string `json:"deviceToken"`
	WelcomeBody
}

// TypeName is "enrolled".
func (*Enrolled) TypeName() string { return TypeEnrolled }

// Hello opens every later stream of an enrolled device.
type Hello struct {
	Head
	DeviceID         string `json:"deviceId"`
	DeviceToken      string `json:"deviceToken"`
	ProductVersion   string `json:"productVersion,omitempty"`
	KeepaliveSeconds int    `json:"keepaliveSeconds,omitempty"`
}

// TypeName is "hello".
func (*Hello) TypeName() string { return TypeHello }

// Welcome answers a `hello`.
type Welcome struct {
	Head
	WelcomeBody
}

// TypeName is "welcome".
func (*Welcome) TypeName() string { return TypeWelcome }

// Refused is built only by Refuse.
type Refused struct {
	Head
	Reason string `json:"reason"`
}

// TypeName is "refused".
func (*Refused) TypeName() string { return TypeRefused }

// --- the control stream (7.3, 7.4) ---

// Keepalive is sent by a device and echoed by the server.
type Keepalive struct{ Head }

// TypeName is "keepalive".
func (*Keepalive) TypeName() string { return TypeKeepalive }

// Bye is a device going offline on purpose; it is not answered.
type Bye struct{ Head }

// TypeName is "bye".
func (*Bye) TypeName() string { return TypeBye }

// List asks for the directory.
type List struct{ Head }

// TypeName is "list".
func (*List) TypeName() string { return TypeList }

// Directory answers a `list`: every record of the account (section 6 records, verbatim).
type Directory struct {
	Head
	Revision   int64             `json:"revision"`
	Devices    []json.RawMessage `json:"devices"`
	Resources  []json.RawMessage `json:"resources"`
	Broadcasts []json.RawMessage `json:"broadcasts"`
}

// TypeName is "directory".
func (*Directory) TypeName() string { return TypeDirectory }

// Subscribe asks for `changed` pushes from now on; it is not answered.
type Subscribe struct{ Head }

// TypeName is "subscribe".
func (*Subscribe) TypeName() string { return TypeSubscribe }

// Upserts are the records a `changed` adds or replaces.
type Upserts struct {
	Devices    []json.RawMessage `json:"devices"`
	Resources  []json.RawMessage `json:"resources"`
	Broadcasts []json.RawMessage `json:"broadcasts"`
}

// Removals are the ids a `changed` takes out.
type Removals struct {
	DeviceIDs    []string `json:"deviceIds"`
	ResourceIDs  []string `json:"resourceIds"`
	BroadcastIDs []string `json:"broadcastIds"`
}

// Changed is pushed to a subscriber: one per directory transaction, each with its own revision.
type Changed struct {
	Head
	Revision int64    `json:"revision"`
	Upserts  Upserts  `json:"upserts"`
	Removals Removals `json:"removals"`
}

// TypeName is "changed".
func (*Changed) TypeName() string { return TypeChanged }

// Publish offers a resource record (a section 6.2 record; `publicPort` is the 5.5 request).
type Publish struct {
	Head
	Resource json.RawMessage `json:"resource"`
}

// TypeName is "publish".
func (*Publish) TypeName() string { return TypePublish }

// Published answers a `publish` with the granted third-party port, when one was asked for.
type Published struct {
	Head
	ResourceID string `json:"resourceId"`
	PublicPort *int   `json:"publicPort,omitempty"`
}

// TypeName is "published".
func (*Published) TypeName() string { return TypePublished }

// Unpublish takes a resource out of the directory; it is not answered.
type Unpublish struct {
	Head
	ResourceID string `json:"resourceId"`
}

// TypeName is "unpublish".
func (*Unpublish) TypeName() string { return TypeUnpublish }

// BroadcastStart announces a live broadcast (a section 6.3 record) and its listener cap.
type BroadcastStart struct {
	Head
	Broadcast    json.RawMessage `json:"broadcast"`
	MaxListeners *int            `json:"maxListeners,omitempty"`
}

// TypeName is "broadcast-start".
func (*BroadcastStart) TypeName() string { return TypeBroadcastStart }

// BroadcastStarted answers a `broadcast-start`.
type BroadcastStarted struct {
	Head
	BroadcastID string `json:"broadcastId"`
}

// TypeName is "broadcast-started".
func (*BroadcastStarted) TypeName() string { return TypeBroadcastStarted }

// BroadcastUpdate replaces a live broadcast record and, when present, its listener cap (item M).
type BroadcastUpdate struct {
	Head
	Broadcast    json.RawMessage `json:"broadcast"`
	MaxListeners *int            `json:"maxListeners,omitempty"`
}

// TypeName is "broadcast-update".
func (*BroadcastUpdate) TypeName() string { return TypeBroadcastUpdate }

// BroadcastEnd ends a live broadcast; it is not answered.
type BroadcastEnd struct {
	Head
	BroadcastID string `json:"broadcastId"`
}

// TypeName is "broadcast-end".
func (*BroadcastEnd) TypeName() string { return TypeBroadcastEnd }

// PairingRequest asks for a one-time pairing code.
type PairingRequest struct{ Head }

// TypeName is "pairing-request".
func (*PairingRequest) TypeName() string { return TypePairingRequest }

// PairingCode answers a `pairing-request`.
type PairingCode struct {
	Head
	Code      string `json:"code"`
	ExpiresAt string `json:"expiresAt"`
}

// TypeName is "pairing-code".
func (*PairingCode) TypeName() string { return TypePairingCode }

// Revoke kills another device's token (or this device's own); it is not answered.
type Revoke struct {
	Head
	DeviceID string `json:"deviceId"`
}

// TypeName is "revoke".
func (*Revoke) TypeName() string { return TypeRevoke }

// --- tunnels (7.5) ---

// Connect opens a tunnel: exactly one of the three ids, `scheme` only with a broadcast.
type Connect struct {
	Head
	ResourceID  string `json:"resourceId,omitempty"`
	ShareID     string `json:"shareId,omitempty"`
	BroadcastID string `json:"broadcastId,omitempty"`
	Scheme      string `json:"scheme,omitempty"`
}

// TypeName is "connect".
func (*Connect) TypeName() string { return TypeConnect }

// Connected is the last envelope a consumer's stream carries before the bridge: after it the bytes
// are the tunnelled service's.
type Connected struct{ Head }

// TypeName is "connected".
func (*Connected) TypeName() string { return TypeConnected }

// Open is sent to a producer on its control stream: a tunnel waits for an `attach`.
type Open struct {
	Head
	TunnelID    string `json:"tunnelId"`
	ResourceID  string `json:"resourceId,omitempty"`
	ShareID     string `json:"shareId,omitempty"`
	BroadcastID string `json:"broadcastId,omitempty"`
	Scheme      string `json:"scheme,omitempty"`
}

// TypeName is "open".
func (*Open) TypeName() string { return TypeOpen }

// Attach is a producer's data stream naming the tunnel it serves.
type Attach struct {
	Head
	TunnelID string `json:"tunnelId"`
}

// TypeName is "attach".
func (*Attach) TypeName() string { return TypeAttach }

// --- the relay (7.6) ---

// Upload starts a broadcaster's chunk stream.
type Upload struct {
	Head
	BroadcastID string `json:"broadcastId"`
	ContentType string `json:"contentType"`
}

// TypeName is "upload".
func (*Upload) TypeName() string { return TypeUpload }

// UploadAccepted answers an `upload`; chunks follow.
type UploadAccepted struct{ Head }

// TypeName is "upload-accepted".
func (*UploadAccepted) TypeName() string { return TypeUploadAccepted }

// --- cast (7.8) ---

// Cast asks the server to offer a broadcast to one named receiver of the same account.
type Cast struct {
	Head
	BroadcastID      string `json:"broadcastId"`
	ReceiverDeviceID string `json:"receiverDeviceId"`
}

// TypeName is "cast".
func (*Cast) TypeName() string { return TypeCast }

// CastOffer is forwarded to the receiver; it carries the broadcast record and never `access`.
type CastOffer struct {
	Head
	Broadcast    json.RawMessage `json:"broadcast"`
	FromDeviceID string          `json:"fromDeviceId"`
}

// TypeName is "cast-offer".
func (*CastOffer) TypeName() string { return TypeCastOffer }

// CastAnswer is the receiver's reply to an offer.
type CastAnswer struct {
	Head
	BroadcastID string `json:"broadcastId"`
	Accepted    bool   `json:"accepted"`
	Reason      string `json:"reason,omitempty"`
}

// TypeName is "cast-answer".
func (*CastAnswer) TypeName() string { return TypeCastAnswer }

// CastResult is forwarded to the caster.
type CastResult struct {
	Head
	BroadcastID      string `json:"broadcastId"`
	ReceiverDeviceID string `json:"receiverDeviceId"`
	Accepted         bool   `json:"accepted"`
	Reason           string `json:"reason,omitempty"`
}

// TypeName is "cast-result".
func (*CastResult) TypeName() string { return TypeCastResult }

// CastStop stops a cast; it is forwarded as is.
type CastStop struct {
	Head
	BroadcastID      string `json:"broadcastId"`
	ReceiverDeviceID string `json:"receiverDeviceId"`
}

// TypeName is "cast-stop".
func (*CastStop) TypeName() string { return TypeCastStop }

// --- the clipboard (section 22) ---

// ClipboardPut opens a clipboard upload on a data stream (22 item BW): the kind, the exact size of
// the raw bytes that follow `clipboard-ready`, and, for an image or a file, their media type and
// name.
type ClipboardPut struct {
	Head
	Kind     string `json:"kind"`
	Size     int64  `json:"size"`
	MimeType string `json:"mimeType,omitempty"`
	Name     string `json:"name,omitempty"`
}

// TypeName is "clipboard-put".
func (*ClipboardPut) TypeName() string { return TypeClipboardPut }

// ClipboardReady answers a `clipboard-put` that the server will take; exactly Size raw bytes follow.
type ClipboardReady struct{ Head }

// TypeName is "clipboard-ready".
func (*ClipboardReady) TypeName() string { return TypeClipboardReady }

// ClipboardStored answers a finished upload: the value replaced the slot at its last byte (22
// items BW and BX).
type ClipboardStored struct {
	Head
	ClipID   string `json:"clipId"`
	SHA256   string `json:"sha256"`
	StoredAt string `json:"storedAt"`
}

// TypeName is "clipboard-stored".
func (*ClipboardStored) TypeName() string { return TypeClipboardStored }

// ClipboardInfo asks what is on the account's clipboard, on the control stream (22 item BY).
type ClipboardInfo struct{ Head }

// TypeName is "clipboard-info".
func (*ClipboardInfo) TypeName() string { return TypeClipboardInfo }

// ClipboardValueBody are the members `clipboard-state`'s `value` and `clipboard-value` share (22
// items BU and BY): what the server set, then the sender's metadata. The server never looks inside
// a value; the kind is metadata.
type ClipboardValueBody struct {
	ClipID           string `json:"clipId,omitempty"`
	Size             int64  `json:"size,omitempty"`
	SHA256           string `json:"sha256,omitempty"`
	StoredAt         string `json:"storedAt,omitempty"`
	SourceDeviceID   string `json:"sourceDeviceId,omitempty"`
	SourceDeviceName string `json:"sourceDeviceName,omitempty"`
	Kind             string `json:"kind,omitempty"`
	MimeType         string `json:"mimeType,omitempty"`
	Name             string `json:"name,omitempty"`
}

// ClipboardState answers a `clipboard-info`: the one value, or no `value` when it is empty. A
// guest's clipboard is answered empty (22 item BY).
type ClipboardState struct {
	Head
	Value *ClipboardValueBody `json:"value,omitempty"`
}

// TypeName is "clipboard-state".
func (*ClipboardState) TypeName() string { return TypeClipboardState }

// ClipboardGet asks for the value on a data stream, optionally naming the clipId a client saw
// before (22 item BY). The answer is `clipboard-value` with the raw bytes after it,
// `clipboard-empty`, or `refused`.
type ClipboardGet struct {
	Head
	ClipID string `json:"clipId,omitempty"`
}

// TypeName is "clipboard-get".
func (*ClipboardGet) TypeName() string { return TypeClipboardGet }

// ClipboardValue answers a `clipboard-get`: the members of `clipboard-state`'s value, then exactly
// Size raw bytes, then the server closes the stream.
type ClipboardValue struct {
	Head
	ClipboardValueBody
}

// TypeName is "clipboard-value".
func (*ClipboardValue) TypeName() string { return TypeClipboardValue }

// ClipboardEmpty answers a `clipboard-get` on an empty clipboard; the server closes the stream.
type ClipboardEmpty struct{ Head }

// TypeName is "clipboard-empty".
func (*ClipboardEmpty) TypeName() string { return TypeClipboardEmpty }

// --- ticket 224 account lifecycle: registration, passwords, deletion ---

// Register is the account and its first device in one exchange, by invitation (DEVICE-EXCHANGE 19 item AQ):
// exactly one of InvitationCode (a one-time invitation, 22 characters that decode to 16 bytes) or
// SharedPassword (the shared invitation password), then the login and password chosen and every
// device member of `enroll`. The names are the S4214 fold's (invitationCode, sharedPassword - the shape
// the generated transcripts of 205 already carry). The contract's own prose of item AQ calls the same
// two members `invitation` and `invitationPassword`; a frame that uses those is folded into the two
// above by validate, so a client built to either reading registers (the engine reads only the two).
type Register struct {
	Head
	InvitationCode     string          `json:"invitationCode,omitempty"`
	SharedPassword     string          `json:"sharedPassword,omitempty"`
	Invitation         string          `json:"invitation,omitempty"`         // alias of InvitationCode, folded by validate
	InvitationPassword string          `json:"invitationPassword,omitempty"` // alias of SharedPassword, folded by validate
	Login              string          `json:"login"`
	Password           string          `json:"password"`
	DeviceID           string          `json:"deviceId"`
	DeviceName         string          `json:"deviceName"`
	Product            string          `json:"product"`
	ProductVersion     string          `json:"productVersion,omitempty"`
	Platform           string          `json:"platform,omitempty"`
	Roles              []string        `json:"roles,omitempty"`
	Capabilities       []string        `json:"capabilities,omitempty"`
	Receiver           json.RawMessage `json:"receiver,omitempty"`
	KeepaliveSeconds   int             `json:"keepaliveSeconds,omitempty"`
}

// TypeName is "register".
func (*Register) TypeName() string { return TypeRegister }

// PasswordChange changes an account's password from an authenticated control stream (ticket 224).
type PasswordChange struct {
	Head
	OldPassword string `json:"oldPassword"`
	NewPassword string `json:"newPassword"`
}

// TypeName is "password-change".
func (*PasswordChange) TypeName() string { return TypePasswordChange }

// PasswordChanged answers a successful password change, providing a fresh device token for this device (ticket 224).
type PasswordChanged struct {
	Head
	DeviceToken string `json:"deviceToken"`
}

// TypeName is "password-changed".
func (*PasswordChanged) TypeName() string { return TypePasswordChanged }

// AccountDelete deletes the caller's account, confirmed by the current password (ticket 224).
type AccountDelete struct {
	Head
	Password string `json:"password"`
}

// TypeName is "account-delete".
func (*AccountDelete) TypeName() string { return TypeAccountDelete }

// AccountDeleted answers a successful account deletion right before streams close (ticket 224).
type AccountDeleted struct {
	Head
}

// TypeName is "account-deleted".
func (*AccountDeleted) TypeName() string { return TypeAccountDeleted }
