package wire

import (
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
	if len(e.Roles) > maxRoles {
		return errors.New("too many roles")
	}
	for _, r := range e.Roles {
		if !textOK(r, 32) {
			return errors.New("a role is out of bounds")
		}
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
	return required("contentType", e.ContentType, maxContentType)
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
