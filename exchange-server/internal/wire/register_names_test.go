package wire

import (
	"bytes"
	"strings"
	"testing"
)

// DEVICE-EXCHANGE 19 item AQ names the two invitation members of `register` `invitation` and
// `invitationPassword`. This server first used `invitationCode` and `sharedPassword` (the shape of the
// generated transcripts of ticket 205) and keeps reading both: a client built to either reading registers.
// A frame decodes to the names it carries and encodes back to the same bytes (the vectors rely on it); the
// engine reads the invitation through Register.Code and the shared password through Register.Shared.

func registerFrame(invitationMembers string) []byte {
	return []byte(`{"schemaVersion":2,"type":"register",` + invitationMembers + `"login":"bob","password":"Passw0rdTest",` +
		`"deviceId":"` + idB + `","deviceName":"Phone","product":"fms-android"}`)
}

func TestRegisterReadsEachSpellingOfItsInvitationMembers(t *testing.T) {
	cases := []struct {
		name, members, wantCode, wantShared string
	}{
		{"invitation (item AQ)", `"invitation":"` + idC + `",`, idC, ""},
		{"invitationCode (the first spelling)", `"invitationCode":"` + idC + `",`, idC, ""},
		{"invitationPassword (item AQ)", `"invitationPassword":"Shared-pw-1",`, "", "Shared-pw-1"},
		{"sharedPassword (the first spelling)", `"sharedPassword":"Shared-pw-1",`, "", "Shared-pw-1"},
	}
	for _, c := range cases {
		frame := registerFrame(c.members)
		e, err := Decode(frame)
		if err != nil {
			t.Errorf("%s: %v", c.name, err)
			continue
		}
		reg, ok := e.(*Register)
		if !ok {
			t.Errorf("%s: decoded as %T", c.name, e)
			continue
		}
		if reg.Code() != c.wantCode || reg.Shared() != c.wantShared {
			t.Errorf("%s: the engine would read code %q and shared password %q, want %q and %q",
				c.name, reg.Code(), reg.Shared(), c.wantCode, c.wantShared)
		}
		again, err := Encode(e)
		if err != nil || !bytes.Equal(again, frame) {
			t.Errorf("%s: the frame does not encode back to its own bytes\n got  %s\n want %s (%v)", c.name, again, frame, err)
		}
	}
}

func TestRegisterRefusesAFrameThatNamesAnInvitationTwiceOrBothKinds(t *testing.T) {
	for name, members := range map[string]string{
		"the code under both spellings":     `"invitation":"` + idC + `","invitationCode":"` + idC + `",`,
		"the password under both spellings": `"invitationPassword":"a-b-c-1","sharedPassword":"a-b-c-1",`,
		"a code and a password":             `"invitation":"` + idC + `","invitationPassword":"a-b-c-1",`,
		"a code and a password, old names":  `"invitationCode":"` + idC + `","sharedPassword":"a-b-c-1",`,
		"a code and a password, mixed":      `"invitation":"` + idC + `","sharedPassword":"a-b-c-1",`,
		"neither":                           ``,
		"a code that is not an id":          `"invitation":"short",`,
		"a password above the bound":        `"invitationPassword":"` + strings.Repeat("a", maxPassword+1) + `",`,
	} {
		if _, err := Decode(registerFrame(members)); err == nil {
			t.Errorf("%s: want a malformed frame", name)
		}
	}
}
