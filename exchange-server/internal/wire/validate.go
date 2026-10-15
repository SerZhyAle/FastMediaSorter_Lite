package wire

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"unicode/utf8"
)

// Bounds on a value a frame may carry. They are checked by Decode so a handler never sees a member
// it would have to bound itself; a violation closes the stream like any malformed frame (161).
const (
	maxLogin       = 256 // far above the 64 an account may have: a longer one is simply unknown
	maxPassword    = 1024
	maxCode        = 64
	maxToken       = 128
	maxDeviceName  = 64
	maxProduct     = 32
	maxPlatform    = 32
	maxVersion     = 64
	maxContentType = 128
	maxTunnelID    = 64
	maxRoles       = 8
)

// KnownRoles are the four device roles of DEVICE-EXCHANGE 6.1; an enroll keeps only these.
var KnownRoles = []string{"resource-producer", "resource-consumer", "broadcaster", "receiver"}

// validate on Head is the default: an envelope with no required member.
func (h *Head) validate() error { return nil }

func textOK(s string, max int) bool {
	if len(s) > max || !utf8.ValidString(s) {
		return false
	}
	for _, r := range s {
		if r < 0x20 || r == 0x7f {
			return false
		}
	}
	return true
}

func required(name, s string, max int) error {
	if s == "" {
		return fmt.Errorf("%s is required", name)
	}
	if !textOK(s, max) {
		return fmt.Errorf("%s is out of bounds", name)
	}
	return nil
}

func (e *Enroll) validate() error {
	if err := required("login", e.Login, maxLogin); err != nil {
		return err
	}
	switch {
	case e.Password != "" && e.PairingCode != "":
		return errors.New("a password and a pairing code together")
	case e.Password == "" && e.PairingCode == "":
		return errors.New("a password or a pairing code is required")
	case len(e.Password) > maxPassword || !utf8.ValidString(e.Password):
		return errors.New("password is out of bounds")
	case len(e.PairingCode) > maxCode:
		return errors.New("pairingCode is out of bounds")
	}
	if !ValidID(e.DeviceID) {
		return errors.New("deviceId is not an id")
	}
	if err := required("deviceName", e.DeviceName, maxDeviceName); err != nil {
		return err
	}
	if err := required("product", e.Product, maxProduct); err != nil {
		return err
	}
	if !textOK(e.ProductVersion, maxVersion) || !textOK(e.Platform, maxPlatform) {
		return errors.New("productVersion or platform is out of bounds")
	}
	if len(e.Capabilities) > maxRoles {
		return errors.New("too many capabilities")
	}
	for _, capability := range e.Capabilities {
		if !textOK(capability, 32) {
			return errors.New("a capability is out of bounds")
		}
	}
	if len(e.Roles) > maxRoles {
		return errors.New("too many roles")
	}
	for _, r := range e.Roles {
		if !textOK(r, 32) {
			return errors.New("a role is out of bounds")
		}
	}
	// "receiver":null is a member that says nothing: it is absent, never a malformed block.
	if t := bytes.TrimSpace(e.Receiver); string(t) == "null" {
		e.Receiver = nil
	}
	if len(e.Receiver) > 0 {
		if err := CheckReceiver(e.Receiver); err != nil {
			return err
		}
	}
	return nil
}

func (e *Hello) validate() error {
	if !ValidID(e.DeviceID) {
		return errors.New("deviceId is not an id")
	}
	if err := required("deviceToken", e.DeviceToken, maxToken); err != nil {
		return err
	}
	if !textOK(e.ProductVersion, maxVersion) {
		return errors.New("productVersion is out of bounds")
	}
	return nil
}

func (e *Enrolled) validate() error { return required("deviceToken", e.DeviceToken, maxToken) }

func (e *Refused) validate() error { return required("reason", e.Reason, 64) }

func (e *Publish) validate() error {
	if len(e.Resource) == 0 || e.Resource[0] != '{' {
		return errors.New("resource is required")
	}
	return nil
}

func (e *Unpublish) validate() error { return required("resourceId", e.ResourceID, maxTunnelID) }

func (e *BroadcastStart) validate() error {
	if len(e.Broadcast) == 0 || e.Broadcast[0] != '{' {
		return errors.New("broadcast is required")
	}
	return nil
}

func (e *BroadcastUpdate) validate() error {
	if len(e.Broadcast) == 0 || e.Broadcast[0] != '{' {
		return errors.New("broadcast is required")
	}
	return nil
}

func (e *BroadcastEnd) validate() error { return required("broadcastId", e.BroadcastID, maxTunnelID) }

func (e *BroadcastStarted) validate() error {
	return required("broadcastId", e.BroadcastID, maxTunnelID)
}

func (e *Published) validate() error { return required("resourceId", e.ResourceID, maxTunnelID) }

func (e *PairingCode) validate() error { return required("code", e.Code, maxCode) }

func (e *Revoke) validate() error { return required("deviceId", e.DeviceID, maxTunnelID) }

// validate holds the rule of 164 rule 1 and 7.5: exactly one id, `scheme` with a broadcast and only
// with a broadcast. The ids are NOT checked for their form: a malformed id is answered `unavailable`
// like an unknown one, after the same delay, never by a closed stream that would tell them apart.
func (e *Connect) validate() error {
	n := 0
	for _, id := range []string{e.ResourceID, e.ShareID, e.BroadcastID} {
		if id != "" {
			n++
			if !textOK(id, maxTunnelID) {
				return errors.New("an id is out of bounds")
			}
		}
	}
	if n != 1 {
		return errors.New("connect needs exactly one id")
	}
	if e.BroadcastID != "" {
		if e.Scheme != "http" && e.Scheme != "rtsp" {
			return errors.New("a broadcast connect needs scheme http or rtsp")
		}
	} else if e.Scheme != "" {
		return errors.New("scheme belongs to a broadcast connect")
	}
	return nil
}

func (e *Open) validate() error {
	if err := required("tunnelId", e.TunnelID, maxTunnelID); err != nil {
		return err
	}
	return nil
}

func (e *Attach) validate() error { return required("tunnelId", e.TunnelID, maxTunnelID) }

func (e *Upload) validate() error {
	if err := required("broadcastId", e.BroadcastID, maxTunnelID); err != nil {
		return err
	}
	if err := required("contentType", e.ContentType, maxContentType); err != nil {
		return err
	}
	// The content type is copied into a response header and nothing else (ticket 165 rule 1).
	if !ValidContentType(e.ContentType) {
		return errors.New("contentType is not a media type")
	}
	return nil
}

func (e *Cast) validate() error {
	if err := required("broadcastId", e.BroadcastID, maxTunnelID); err != nil {
		return err
	}
	return required("receiverDeviceId", e.ReceiverDeviceID, maxTunnelID)
}

func (e *CastOffer) validate() error {
	if len(e.Broadcast) == 0 || e.Broadcast[0] != '{' {
		return errors.New("broadcast is required")
	}
	return required("fromDeviceId", e.FromDeviceID, maxTunnelID)
}

func (e *CastAnswer) validate() error { return required("broadcastId", e.BroadcastID, maxTunnelID) }

func (e *CastResult) validate() error {
	if err := required("broadcastId", e.BroadcastID, maxTunnelID); err != nil {
		return err
	}
	return required("receiverDeviceId", e.ReceiverDeviceID, maxTunnelID)
}

func (e *CastStop) validate() error {
	if err := required("broadcastId", e.BroadcastID, maxTunnelID); err != nil {
		return err
	}
	return required("receiverDeviceId", e.ReceiverDeviceID, maxTunnelID)
}

func (e *Register) validate() error {
	// The contract's prose names (`invitation`, `invitationPassword`) are folded into the fold's names; the
	// same member twice, under both names, is a malformed frame like "an invitation and a password".
	if e.Invitation != "" {
		if e.InvitationCode != "" {
			return errors.New("invitation given twice")
		}
		e.InvitationCode, e.Invitation = e.Invitation, ""
	}
	if e.InvitationPassword != "" {
		if e.SharedPassword != "" {
			return errors.New("invitationPassword given twice")
		}
		e.SharedPassword, e.InvitationPassword = e.InvitationPassword, ""
	}
	if err := required("login", e.Login, maxLogin); err != nil {
		return err
	}
	switch {
	case e.InvitationCode != "" && e.SharedPassword != "":
		return errors.New("an invitation and an invitationPassword together")
	case e.InvitationCode == "" && e.SharedPassword == "":
		return errors.New("an invitation or an invitationPassword is required")
	case e.InvitationCode != "" && !ValidID(e.InvitationCode):
		return errors.New("invitation is not 22 characters of 16 bytes")
	case len(e.SharedPassword) > maxPassword || !utf8.ValidString(e.SharedPassword):
		return errors.New("invitationPassword is out of bounds")
	}
	if len(e.Password) == 0 || len(e.Password) > maxPassword || !utf8.ValidString(e.Password) {
		return errors.New("password is out of bounds")
	}
	if !ValidID(e.DeviceID) {
		return errors.New("deviceId is not an id")
	}
	if err := required("deviceName", e.DeviceName, maxDeviceName); err != nil {
		return err
	}
	if err := required("product", e.Product, maxProduct); err != nil {
		return err
	}
	if !textOK(e.ProductVersion, maxVersion) || !textOK(e.Platform, maxPlatform) {
		return errors.New("productVersion or platform is out of bounds")
	}
	if len(e.Capabilities) > maxRoles {
		return errors.New("too many capabilities")
	}
	for _, capability := range e.Capabilities {
		if !textOK(capability, 32) {
			return errors.New("a capability is out of bounds")
		}
	}
	if len(e.Roles) > maxRoles {
		return errors.New("too many roles")
	}
	for _, r := range e.Roles {
		if !textOK(r, 32) {
			return errors.New("a role is out of bounds")
		}
	}
	if t := bytes.TrimSpace(e.Receiver); string(t) == "null" {
		e.Receiver = nil
	}
	if len(e.Receiver) > 0 {
		var m map[string]json.RawMessage
		if err := json.Unmarshal(e.Receiver, &m); err != nil {
			return errors.New("receiver is not a JSON object")
		}
	}
	return nil
}

func (e *PasswordChange) validate() error {
	if len(e.OldPassword) == 0 || len(e.OldPassword) > maxPassword || !utf8.ValidString(e.OldPassword) {
		return errors.New("oldPassword is out of bounds")
	}
	if len(e.NewPassword) == 0 || len(e.NewPassword) > maxPassword || !utf8.ValidString(e.NewPassword) {
		return errors.New("newPassword is out of bounds")
	}
	return nil
}

func (e *PasswordChanged) validate() error {
	if !ValidToken(e.DeviceToken) {
		return errors.New("deviceToken is not a token")
	}
	return nil
}

func (e *AccountDelete) validate() error {
	if len(e.Password) == 0 || len(e.Password) > maxPassword || !utf8.ValidString(e.Password) {
		return errors.New("password is out of bounds")
	}
	return nil
}

func (e *AccountDeleted) validate() error { return nil }

// --- the clipboard (section 22) ---

// Bounds of item BW: a name is at most 255 bytes of UTF-8, a mimeType at most 127 in the
// type/subtype form. A clipboard-put that breaks any of these is an invalid envelope and closes
// the stream (7.1) - the same treatment as every member outside its bounds (item AN).
const (
	maxClipName = 255
	maxMimeType = 127
)

// ClipKinds are the kinds of one clipboard value (item BU). A fourth kind arrives with the sealed
// amendment and is not this server's until then.
var ClipKinds = []string{"text", "image", "file"}

// ValidClipName reports whether s is a name a clipboard value may carry: at most 255 bytes of
// UTF-8, no slash, no backslash, no control character, never "." or "..".
func ValidClipName(s string) bool {
	if s == "" || len(s) > maxClipName || !utf8.ValidString(s) || s == "." || s == ".." {
		return false
	}
	for i := 0; i < len(s); i++ {
		if c := s[i]; c < 0x20 || c == 0x7f || c == '/' || c == '\\' {
			return false
		}
	}
	return true
}

// ValidClipMimeType reports whether s is a media type in the type/subtype form, at most 127 bytes
// of printable ASCII with exactly one slash and no empty side.
func ValidClipMimeType(s string) bool {
	if s == "" || len(s) > maxMimeType {
		return false
	}
	slash := -1
	for i := 0; i < len(s); i++ {
		c := s[i]
		switch {
		case c == '/':
			if slash >= 0 {
				return false
			}
			slash = i
		case c < '!' || c > '~':
			return false // space and control characters are not a media type
		}
	}
	return slash > 0 && slash < len(s)-1
}

func (e *ClipboardPut) validate() error {
	switch e.Kind {
	case "text":
		if e.MimeType != "" {
			return errors.New("a text carries no mimeType")
		}
		if e.Name != "" {
			return errors.New("a text carries no name")
		}
	case "image":
		if !ValidClipMimeType(e.MimeType) {
			return errors.New("an image needs a mimeType")
		}
		if e.Name != "" && !ValidClipName(e.Name) {
			return errors.New("name is out of bounds")
		}
	case "file":
		if !ValidClipMimeType(e.MimeType) {
			return errors.New("a file needs a mimeType")
		}
		if !ValidClipName(e.Name) {
			return errors.New("a file needs a name")
		}
	default:
		return errors.New("kind is text, image or file")
	}
	if e.Size < 1 {
		return errors.New("size is at least 1")
	}
	return nil
}

func (e *ClipboardStored) validate() error {
	if !ValidID(e.ClipID) {
		return errors.New("clipId is not an id")
	}
	if !ValidSHA256(e.SHA256) {
		return errors.New("sha256 is not a SHA-256")
	}
	if _, err := ParseTime(e.StoredAt); err != nil {
		return errors.New("storedAt is not a time")
	}
	return nil
}

func (e *ClipboardGet) validate() error {
	if e.ClipID != "" && !ValidID(e.ClipID) {
		return errors.New("clipId is not an id")
	}
	return nil
}

func bodyValidate(v *ClipboardValueBody) error {
	if v.ClipID != "" && !ValidID(v.ClipID) {
		return errors.New("clipId is not an id")
	}
	if v.Size < 0 {
		return errors.New("size is out of bounds")
	}
	if v.SHA256 != "" && !ValidSHA256(v.SHA256) {
		return errors.New("sha256 is not a SHA-256")
	}
	if v.StoredAt != "" {
		if _, err := ParseTime(v.StoredAt); err != nil {
			return errors.New("storedAt is not a time")
		}
	}
	if !textOK(v.SourceDeviceID, maxTunnelID) || !textOK(v.SourceDeviceName, maxDeviceName) {
		return errors.New("a source device member is out of bounds")
	}
	switch v.Kind {
	case "":
		if v.MimeType != "" || v.Name != "" {
			return errors.New("metadata without a kind")
		}
	case "text":
		if v.MimeType != "" || v.Name != "" {
			return errors.New("a text carries no mimeType and no name")
		}
	case "image":
		if !ValidClipMimeType(v.MimeType) {
			return errors.New("an image needs a mimeType")
		}
		if v.Name != "" && !ValidClipName(v.Name) {
			return errors.New("name is out of bounds")
		}
	case "file":
		if !ValidClipMimeType(v.MimeType) {
			return errors.New("a file needs a mimeType")
		}
		if !ValidClipName(v.Name) {
			return errors.New("a file needs a name")
		}
	default:
		return errors.New("kind is text, image or file")
	}
	return nil
}

func (e *ClipboardState) validate() error {
	if e.Value == nil {
		return nil
	}
	return bodyValidate(e.Value)
}

func (e *ClipboardValue) validate() error { return bodyValidate(&e.ClipboardValueBody) }
