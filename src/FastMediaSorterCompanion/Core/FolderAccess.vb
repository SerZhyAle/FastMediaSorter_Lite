Option Strict On

Imports System.IO
Imports System.Security.AccessControl
Imports System.Security.Principal

''' <summary>
''' Does the account that actually serves a folder have the access the folder list
''' promises? In Server mode the worker runs as LOCAL SERVICE, not as the person who
''' picked the folder, so "I can see it in Explorer" says nothing about whether the
''' share will work - and the failure surfaces far away, on the phone, as a directory
''' that will not open or a delete that is refused.
'''
''' This is a read-only check, deliberately: it needs no elevation, so the app can run
''' it on every folder-list change and only raise a UAC prompt when something is
''' genuinely missing. Granting is the elevated helper's job (ServiceControl.GrantRoots).
'''
''' The check reads the DACL and evaluates it for the set of SIDs LOCAL SERVICE
''' carries, because a folder is very often reachable through Everyone or Users rather
''' than through an ACE naming the service account. That makes it an approximation of
''' what the kernel would decide - close enough in the direction that matters: it can
''' ask for a grant that was not strictly needed, and the grant is harmless, but it
''' does not claim access that is not there.
''' </summary>
Public Module FolderAccess

    ''' <summary>LOCAL SERVICE, and the well-known groups it is a member of. Everyone,
    ''' Authenticated Users, Users and Service all routinely carry the ACE that makes a
    ''' folder readable, so ignoring them would report "no access" for most of the disk.</summary>
    Private ReadOnly Service_Sids As String() = {
        "S-1-5-19",     ' NT AUTHORITY\LOCAL SERVICE - the service account itself
        "S-1-1-0",      ' Everyone
        "S-1-5-11",     ' Authenticated Users
        "S-1-5-32-545", ' BUILTIN\Users
        "S-1-5-6"       ' SERVICE
    }

    ''' <summary>What a root needs from the service account, given how it is shared.</summary>
    Private Function RequiredRights(writable As Boolean) As FileSystemRights
        Return If(writable,
                  FileSystemRights.Modify,
                  FileSystemRights.ReadAndExecute)
    End Function

    ''' <summary>
    ''' True when LOCAL SERVICE appears to lack the access this root needs. False for a
    ''' UNC path (an ACL here cannot fix it - the account authenticates anonymously on
    ''' the network, so asking for a grant would only produce a pointless UAC prompt),
    ''' for a missing folder, and whenever the DACL cannot be read - never guess a
    ''' problem into existence.
    ''' </summary>
    Public Function NeedsGrant(hostPath As String, writable As Boolean) As Boolean
        If String.IsNullOrWhiteSpace(hostPath) Then Return False
        Dim path As String = hostPath.Trim()
        If path.StartsWith("\\", StringComparison.Ordinal) Then Return False
        If Not Directory.Exists(path) Then Return False

        Try
            Dim needed As FileSystemRights = RequiredRights(writable)
            Dim rules As AuthorizationRuleCollection =
                New DirectoryInfo(path).GetAccessControl(AccessControlSections.Access).
                    GetAccessRules(True, True, GetType(SecurityIdentifier))

            Dim allowed As FileSystemRights = 0
            For Each rule As FileSystemAccessRule In rules
                Dim sid As String = CType(rule.IdentityReference, SecurityIdentifier).Value
                If Array.IndexOf(Service_Sids, sid) < 0 Then Continue For
                ' A Deny anywhere in the set wins outright, exactly as it does for the
                ' kernel - and it is not something a grant would fix, so do not ask.
                If rule.AccessControlType = AccessControlType.Deny AndAlso
                   (rule.FileSystemRights And needed) <> 0 Then Return False
                If rule.AccessControlType = AccessControlType.Allow Then
                    allowed = allowed Or rule.FileSystemRights
                End If
            Next
            Return (allowed And needed) <> needed
        Catch
            ' No permission to read the DACL, a path that vanished mid-check, a provider
            ' that does not support ACLs: all "cannot tell", and cannot-tell must not
            ' turn into a UAC prompt.
            Return False
        End Try
    End Function

    ''' <summary>The roots that need a grant, in the order given. Empty when the machine
    ''' is not in Server mode: in User mode the worker runs as the person who picked the
    ''' folders, so their own access is the only one that matters.</summary>
    Public Function RootsNeedingGrant(roots As IEnumerable(Of ShareFolder)) As List(Of ShareFolder)
        Dim needy As New List(Of ShareFolder)()
        If roots Is Nothing Then Return needy
        If ServerFeatures.HostMode() <> ServerFeatures.ServerHostMode.SystemService Then Return needy
        For Each r As ShareFolder In roots
            If r Is Nothing Then Continue For
            If NeedsGrant(r.hostPath, Not r.readOnly) Then needy.Add(r)
        Next
        Return needy
    End Function

End Module
