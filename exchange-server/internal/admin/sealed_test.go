package admin

import (
	"encoding/json"
	"reflect"
	"strings"
	"testing"
)

// Ticket 234, item DQ of DEVICE-EXCHANGE 27: an administration report never carries a box, a sealed value,
// a key ring, a key or chunk bytes. The server holds them as opaque bytes for the devices; the operator's
// pipe has no reason to show them, and a field that could hold one is the first step of a leak. The reports
// are typed views, so the guarantee is structural and is held here on the types themselves:
//   - no member of any view has a name that belongs to sealed content;
//   - no member can hold raw bytes or a free-form value (a []byte, a json.RawMessage, an interface, a map),
//     so a box cannot be copied into a view by accident either.

// sealedNames are the JSON member names that belong to sealed content or to the keys around it (27 items
// DH, DI, DL, DM). A boolean of one of these names only says that something is so and carries nothing.
var sealedNames = []string{"sealed", "sealedaccess", "box", "keyring", "ring", "e2ekey", "kem", "fromkey", "ct", "wk",
	"enc", "n", "keys", "chunk", "payload", "relaykey", "tunnelpin", "access", "credentials", "verifier", "verifiers",
	"token", "tokenhash", "password", "secret", "content", "bytes"}

// reportTypes are every shape an administration answer is built of (the answers' `data`).
func reportTypes() []reflect.Type {
	return []reflect.Type{
		reflect.TypeOf(Status{}), reflect.TypeOf(AccountView{}), reflect.TypeOf(DeviceView{}),
		reflect.TypeOf(InvitationView{}), reflect.TypeOf(CreateInvitationResult{}), reflect.TypeOf(SharedInvitationView{}),
		reflect.TypeOf(PairingCodeView{}), reflect.TypeOf(LimitsView{}), reflect.TypeOf(PolicyView{}), reflect.TypeOf(GrantView{}),
	}
}

func walkMembers(report func(string), ty reflect.Type, path string, seen map[reflect.Type]bool) {
	for ty.Kind() == reflect.Pointer || ty.Kind() == reflect.Slice || ty.Kind() == reflect.Array {
		ty = ty.Elem()
	}
	if ty.Kind() != reflect.Struct || seen[ty] {
		return
	}
	seen[ty] = true
	for i := 0; i < ty.NumField(); i++ {
		f := ty.Field(i)
		if !f.IsExported() {
			continue
		}
		name := strings.Split(f.Tag.Get("json"), ",")[0]
		if name == "-" {
			continue
		}
		if name == "" {
			name = f.Name
		}
		where := path + "." + name
		kind := f.Type.Kind()
		switch {
		case kind == reflect.Interface, kind == reflect.Map:
			report(where + " is a free-form value: a view must have a fixed shape that cannot carry a box")
		case (kind == reflect.Slice || kind == reflect.Array) && f.Type.Elem().Kind() == reflect.Uint8:
			report(where + " holds raw bytes: a view must not be able to carry sealed bytes")
		}
		if kind != reflect.Bool {
			for _, bad := range sealedNames {
				if strings.EqualFold(name, bad) {
					report(where + " is named like sealed content")
				}
			}
		}
		walkMembers(report, f.Type, where, seen)
	}
}

func TestNoAdministrationViewHasAMemberThatCouldCarryASealedValueOrAKey(t *testing.T) {
	seen := map[reflect.Type]bool{}
	for _, ty := range reportTypes() {
		walkMembers(func(m string) { t.Error(m) }, ty, ty.Name(), seen)
	}
	if len(seen) < len(reportTypes()) {
		t.Fatalf("the walk saw %d types of %d: it proves nothing", len(seen), len(reportTypes()))
	}
}

// The walk catches what it is meant to: a view with a box member, a free-form member or a bytes member.
func TestTheViewWalkRefusesAMemberThatWouldCarryABox(t *testing.T) {
	type leaky struct {
		Sealed json.RawMessage `json:"sealedAccess"`
		Any    any             `json:"extra"`
		Bytes  []byte          `json:"blob"`
		Key    string          `json:"e2eKey"`
		Flag   bool            `json:"sealed"`
	}
	var found []string
	walkMembers(func(m string) { found = append(found, m) }, reflect.TypeOf(leaky{}), "leaky", map[reflect.Type]bool{})
	// the box member is named like sealed content and is raw bytes (2), the free-form one and the bytes one
	// are one finding each, the key is named like one, and the boolean flag is no finding at all
	if len(found) != 5 {
		t.Fatalf("a view that can carry a box must be found, and a flag must not: %d findings: %v", len(found), found)
	}
}

// What the status says about sealing is one flag: the surface that shows the operator statement picks the
// honest text by it (ticket 234 item DE, internal/statement).
func TestTheStatusSaysWhetherTheServerSealsAndNothingMore(t *testing.T) {
	o, _ := newOps(t)
	if o.Status().Sealed {
		t.Fatal("a server that does not advertise sealed says so")
	}
	o.Sealed = true
	st := o.Status()
	if !st.Sealed {
		t.Fatal("a server that advertises sealed says so")
	}
	b, err := json.Marshal(st)
	if err != nil {
		t.Fatal(err)
	}
	var members map[string]any
	if err := json.Unmarshal(b, &members); err != nil {
		t.Fatal(err)
	}
	if v, ok := members["sealed"].(bool); !ok || !v {
		t.Fatalf("the status carries `sealed` as a boolean: %s", b)
	}
}
