Option Strict On

Imports System.IO
Imports System.Security.AccessControl
Imports System.Security.Principal

''' <summary>Test folders whose ACL is owner-only, because the product refuses to publish a key into a
''' folder others can delete from - and a developer's TEMP often is one (sandbox groups, shared profiles).</summary>
Friend Module PrivateTestFolders

    Friend Function Create(Optional prefix As String = "fms-priv-") As String
        ' Shared TEMP can permit renaming an ancestor even when the leaf is protected.
        Dim dir As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), prefix & Guid.NewGuid().ToString("N"))
        Dim security As New DirectorySecurity()
        security.SetAccessRuleProtection(True, False)
        Dim inherit As InheritanceFlags = InheritanceFlags.ContainerInherit Or InheritanceFlags.ObjectInherit
        security.AddAccessRule(New FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow))
        security.AddAccessRule(New FileSystemAccessRule(New SecurityIdentifier(WellKnownSidType.LocalSystemSid, Nothing), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow))
        FileSystemAclExtensions.Create(New DirectoryInfo(dir), security)
        Return dir
    End Function

End Module
