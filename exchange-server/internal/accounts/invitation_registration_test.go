package accounts

import (
	"errors"
	"testing"
)

// Pause after the expensive invitation check, change the operator's setting, then finish the
// registration. This deterministically exercises the interleaving without scheduler timing.
func TestRegistrationRechecksSharedInvitationAtCommit(t *testing.T) {
	for _, change := range []struct {
		name, password, kind string
	}{
		{"disabled", "", ""},
		{"replaced", "ReplacementPass123!", KindGuest},
		{"same-password-new-kind", "SharedSecret123!", KindGuest},
		{"same-password-same-kind", "SharedSecret123!", KindMember},
	} {
		t.Run(change.name, func(t *testing.T) {
			s, _, dir := withAccounts(t)
			if err := s.SetSharedInvitation("SharedSecret123!", KindMember); err != nil {
				t.Fatal(err)
			}
			auth, err := s.AuthorizeSharedInvitation("SharedSecret123!")
			if err != nil {
				t.Fatal(err)
			}
			rec, err := s.HashPassword("NewAccountPass123!")
			if err != nil {
				t.Fatal(err)
			}
			if err := s.SetSharedInvitation(change.password, change.kind); err != nil {
				t.Fatal(err)
			}
			_, err = s.RegisterAccount(RegisterSpec{Login: "carol", PasswordRecord: rec,
				SharedInvitation: auth, Device: spec("carol", devC)})
			if !errors.Is(err, ErrInvitationInvalid) {
				t.Fatalf("a stale shared invitation registered an account: %v", err)
			}
			for _, store := range []*Store{s, open(t, dir, newClock(), nil)} {
				if store.Exists("carol") || len(store.Devices("carol")) != 0 {
					t.Fatal("a refused registration left account or device data")
				}
			}
		})
	}
}

func TestRegistrationRequiresAnInvitationAndUsesItsKind(t *testing.T) {
	s, _, _ := withAccounts(t)
	rec, err := s.HashPassword("NewAccountPass123!")
	if err != nil {
		t.Fatal(err)
	}
	r := RegisterSpec{Login: "carol", PasswordRecord: rec, Kind: KindMember, Device: spec("carol", devC)}
	if _, err := s.RegisterAccount(r); !errors.Is(err, ErrInvitationInvalid) {
		t.Fatalf("registration without an invitation: %v", err)
	}
	code, _, err := s.CreateInvitation(KindGuest, "")
	if err != nil {
		t.Fatal(err)
	}
	r.InvitationCode = code
	if _, err := s.RegisterAccount(r); err != nil {
		t.Fatal(err)
	}
	if s.KindOf("carol") != KindGuest {
		t.Fatal("a guest invitation registered a member account")
	}
}
