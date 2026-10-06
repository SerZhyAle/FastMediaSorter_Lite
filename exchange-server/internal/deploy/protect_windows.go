package deploy

import (
	"fmt"

	"golang.org/x/sys/windows"
)

// ProtectDir gives dir a protected DACL: the current user, SYSTEM and Administrators have full
// control, and nobody else - not even inherited rights. It is applied to the directory that holds
// the TLS private key before the key is written, so the key is born inside it and inherits it.
func ProtectDir(dir string) error {
	tok := windows.GetCurrentProcessToken()
	user, err := tok.GetTokenUser()
	if err != nil {
		return fmt.Errorf("cannot read the current user: %w", err)
	}
	sddl := fmt.Sprintf("D:PAI(A;OICI;FA;;;%s)(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)", user.User.Sid.String())
	sd, err := windows.SecurityDescriptorFromString(sddl)
	if err != nil {
		return err
	}
	dacl, _, err := sd.DACL()
	if err != nil {
		return err
	}
	return windows.SetNamedSecurityInfo(dir, windows.SE_FILE_OBJECT,
		windows.DACL_SECURITY_INFORMATION|windows.PROTECTED_DACL_SECURITY_INFORMATION,
		nil, nil, dacl, nil)
}

// DirSDDL reads a directory's security descriptor as text (for the test that proves the protection).
func DirSDDL(dir string) (string, error) {
	sd, err := windows.GetNamedSecurityInfo(dir, windows.SE_FILE_OBJECT, windows.DACL_SECURITY_INFORMATION)
	if err != nil {
		return "", err
	}
	return sd.String(), nil
}
