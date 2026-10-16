package wire

import (
	"encoding/json"
	"errors"
)

// Grants on the wire (DEVICE-EXCHANGE 20 items BD and BE, 27 item DJ). A device of a `member` account gives
// one resource or one broadcast to a `guest` account of the same deployment; the grant carries what the
// guest needs to reach it - `access` or `credentials` in the clear for a client without the capability,
// `sealed` (a box with the context `grant:<grantId>`) for one that has it. The server never opens the box.
const (
	TypeGrant   = "grant"
	TypeGranted = "granted"
	TypeUngrant = "ungrant"

	// GrantKindLink is the `kind` of a public link with a password (item BE). It is parsed so that it can be
	// answered; this server does not serve links.
	GrantKindLink = "link"
)

// Grant gives a resource or a broadcast to a guest account. Exactly one of ResourceID and BroadcastID is
// set, and exactly one of Access (a resource), Credentials (a broadcast) and Sealed (either). A Grant that
// names a grantId its account already holds replaces that grant's box and changes nothing else (item DJ).
// The payload members are secrets: they live in memory only, and no formatter prints them.
type Grant struct {
	Head
	GrantID     string          `json:"grantId"`
	Kind        string          `json:"kind,omitempty"`
	Guest       string          `json:"guest,omitempty"`
	ResourceID  string          `json:"resourceId,omitempty"`
	BroadcastID string          `json:"broadcastId,omitempty"`
	ShareID     string          `json:"shareId,omitempty"`
	Access      json.RawMessage `json:"access,omitempty"`
	Credentials json.RawMessage `json:"credentials,omitempty"`
	Sealed      json.RawMessage `json:"sealed,omitempty"`
}

// TypeName is "grant".
func (*Grant) TypeName() string { return TypeGrant }

// Granted answers a `grant`.
type Granted struct {
	Head
	GrantID string `json:"grantId"`
}

// TypeName is "granted".
func (*Granted) TypeName() string { return TypeGranted }

// Ungrant takes a grant back; any device of the owning account may send it. It is not answered.
type Ungrant struct {
	Head
	GrantID string `json:"grantId"`
}

// TypeName is "ungrant".
func (*Ungrant) TypeName() string { return TypeUngrant }

// checkPayload is one payload member of a grant: when it is there it is one JSON object that fits a
// record. The server never looks inside - a box is opaque (item DH), and so is a clear descriptor.
func checkPayload(name string, raw json.RawMessage) error {
	if len(raw) == 0 {
		return nil
	}
	if err := CheckBox(raw); err != nil {
		return errors.New(name + " is one JSON object that fits a record")
	}
	return nil
}

func (e *Grant) validate() error {
	if !ValidID(e.GrantID) {
		return errors.New("grantId is not an id")
	}
	switch e.Kind {
	case "", GrantKindLink:
	default:
		return errors.New("kind is absent or link")
	}
	if e.ResourceID != "" && !ValidID(e.ResourceID) || e.BroadcastID != "" && !ValidID(e.BroadcastID) {
		return errors.New("resourceId or broadcastId is not an id")
	}
	if (e.ResourceID == "") == (e.BroadcastID == "") {
		return errors.New("exactly one of resourceId and broadcastId")
	}
	if e.Kind == GrantKindLink {
		// a link names no guest and gives a resource under a share id of its own (item BE)
		if e.Guest != "" || e.BroadcastID != "" || (e.ShareID != "" && !ValidID(e.ShareID)) {
			return errors.New("a link gives a resource, names no guest")
		}
		return nil
	}
	if err := required("guest", e.Guest, maxLogin); err != nil {
		return err
	}
	for _, p := range []struct {
		name string
		raw  json.RawMessage
	}{{"access", e.Access}, {"credentials", e.Credentials}, {"sealed", e.Sealed}} {
		if err := checkPayload(p.name, p.raw); err != nil {
			return err
		}
	}
	n := 0
	for _, raw := range []json.RawMessage{e.Access, e.Credentials, e.Sealed} {
		if len(raw) > 0 {
			n++
		}
	}
	switch {
	case n != 1:
		return errors.New("exactly one of access, credentials and sealed")
	case len(e.Access) > 0 && e.ResourceID == "":
		return errors.New("access is the payload of a resource")
	case len(e.Credentials) > 0 && e.BroadcastID == "":
		return errors.New("credentials are the payload of a broadcast")
	}
	return nil
}

func (e *Granted) validate() error { return grantIDOK(e.GrantID) }
func (e *Ungrant) validate() error { return grantIDOK(e.GrantID) }

func grantIDOK(id string) error {
	if !ValidID(id) {
		return errors.New("grantId is not an id")
	}
	return nil
}
