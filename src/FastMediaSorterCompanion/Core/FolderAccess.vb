Option Strict On

Imports System.Diagnostics
Imports System.IO
Imports System.Security.AccessControl
Imports System.Security.Principal
Imports System.Threading

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

        Return Verdict(path, RequiredRights(writable)) = AccessVerdict.Missing
    End Function

    ''' <summary>What the DACL of one folder says about the service account.</summary>
    Public Enum AccessVerdict
        ''' <summary>The rights are there.</summary>
        Granted
        ''' <summary>The rights are absent, and adding an ACE would supply them. This is
        ''' the only verdict that justifies asking for elevation.</summary>
        Missing
        ''' <summary>An explicit Deny blocks the rights. The folder is just as invisible
        ''' as <see cref="Missing"/>, but a grant would NOT fix it - a Deny outranks
        ''' every Allow - so it must be reported and never turned into a UAC prompt.</summary>
        Denied
        ''' <summary>Cannot tell: the DACL could not be read, the path vanished
        ''' mid-check, the provider has no ACLs. Never guess a problem into existence -
        ''' treated as fine by every caller.</summary>
        Unknown
    End Enum

    ''' <summary>
    ''' Evaluates one folder's DACL for the set of SIDs the service account carries.
    ''' An approximation of the kernel's decision, deliberately erring towards "fine":
    ''' it may ask for a grant that was not strictly needed, which is harmless, but it
    ''' never claims access that is not there.
    ''' </summary>
    Public Function Verdict(path As String, needed As FileSystemRights) As AccessVerdict
        Try
            Dim rules As AuthorizationRuleCollection =
                New DirectoryInfo(path).GetAccessControl(AccessControlSections.Access).
                    GetAccessRules(True, True, GetType(SecurityIdentifier))

            Dim allowed As FileSystemRights = 0
            For Each rule As FileSystemAccessRule In rules
                Dim sid As String = CType(rule.IdentityReference, SecurityIdentifier).Value
                If Array.IndexOf(Service_Sids, sid) < 0 Then Continue For
                If rule.AccessControlType = AccessControlType.Deny AndAlso
                   (rule.FileSystemRights And needed) <> 0 Then Return AccessVerdict.Denied
                If rule.AccessControlType = AccessControlType.Allow Then
                    allowed = allowed Or rule.FileSystemRights
                End If
            Next
            Return If((allowed And needed) = needed, AccessVerdict.Granted, AccessVerdict.Missing)
        Catch
            Return AccessVerdict.Unknown
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

    ''' <summary>Default ceiling for a subtree scan. A share can be a whole disk, and
    ''' this runs where a person is waiting - so the scan is bounded and says so rather
    ''' than finishing at any cost. The viewer draws the same line at 2 s for a
    ''' recipient-folder probe (ticket 011); a tree walk is given a little more.</summary>
    Public ReadOnly Default_Scan_Budget As TimeSpan = TimeSpan.FromSeconds(4)

    ''' <summary>Second ceiling, on directories rather than time: a very fast disk would
    ''' otherwise let one scan enumerate millions of folders inside the time budget and
    ''' return a list nobody can read.</summary>
    Public Const Max_Scanned_Directories As Integer = 20000

    ''' <summary>
    ''' What a subtree walk found. <see cref="SubtreeScan.Completed"/> is the honest
    ''' half: when the budget ran out the answer covers only the folders actually
    ''' scanned, and a caller must say "nothing found SO FAR" rather than "all fine".
    ''' </summary>
    Public NotInheritable Class SubtreeScan
        Public ReadOnly Property Blocked As New List(Of String)()
        Public Property Scanned As Integer
        Public Property Completed As Boolean = True
        ''' <summary>Blocked folders an ACE could fix, i.e. excluding explicit Deny.
        ''' Only these are worth sending to the elevated helper.</summary>
        Public ReadOnly Property Grantable As New List(Of String)()
    End Class

    ''' <summary>
    ''' Walks a shared folder and collects the subdirectories the serving account cannot
    ''' read. This is the check that was missing: the grant, the row warning and the
    ''' add-folder prompt all looked at the ROOT only, and a root can be perfectly
    ''' readable while a folder deep inside it is not - which is exactly how a phone came
    ''' to receive an empty listing for a folder holding 758 files, with nothing wrong
    ''' anywhere the PC could see.
    '''
    ''' Two verdicts, because the serving identity differs by host mode and each check is
    ''' only valid for one of them:
    '''   - always: a real UnauthorizedAccessException while enumerating. In User mode
    '''     THIS process is the one that serves, so its own refusal is authoritative.
    '''   - Server mode only: the DACL evaluated for LOCAL SERVICE. Needed because this
    '''     process runs as the user, who typically CAN read a folder the service cannot,
    '''     so the walk itself would never notice.
    '''
    ''' Bounded by time and by directory count, and reports whether it finished.
    ''' Reparse points are never descended into - a junction can point back up the tree
    ''' (an endless walk) or out of the share entirely (a scan of somewhere never shared).
    ''' </summary>
    ''' <param name="forServiceAccount">Which identity to judge against: True for
    ''' LOCAL SERVICE (read the DACL), False for this process (trust its own refusals).
    ''' Left unset it follows the machine's host mode, which is what callers want and
    ''' what makes the result untestable - so tests state it outright.</param>
    Public Function ScanSubtree(hostPath As String,
                                writable As Boolean,
                                Optional budget As TimeSpan = Nothing,
                                Optional cancel As CancellationToken = Nothing,
                                Optional forServiceAccount As Boolean? = Nothing) As SubtreeScan
        Dim result As New SubtreeScan()
        If String.IsNullOrWhiteSpace(hostPath) Then Return result
        Dim root As String = hostPath.Trim()
        ' A UNC root cannot be fixed by an ACL here (LOCAL SERVICE authenticates
        ' anonymously on the network), so scanning one would only produce findings
        ' nothing in this app can act on.
        If root.StartsWith("\\", StringComparison.Ordinal) Then Return result
        If Not Directory.Exists(root) Then Return result

        Dim limit As TimeSpan = If(budget = TimeSpan.Zero, Default_Scan_Budget, budget)
        Dim needed As FileSystemRights = RequiredRights(writable)
        Dim checkAcl As Boolean = If(forServiceAccount.HasValue, forServiceAccount.Value,
                                     ServerFeatures.HostMode() = ServerFeatures.ServerHostMode.SystemService)
        Dim clock As Stopwatch = Stopwatch.StartNew()

        Dim pending As New Stack(Of String)()
        pending.Push(root)
        While pending.Count > 0
            If clock.Elapsed > limit OrElse result.Scanned >= Max_Scanned_Directories OrElse
               cancel.IsCancellationRequested Then
                result.Completed = False
                Exit While
            End If

            Dim current As String = pending.Pop()
            result.Scanned += 1

            ' The root is judged like every other folder. It has its own root-level check
            ' elsewhere (for the add-folder grant prompt), but a caller asking "can this
            ' tree be served" needs one answer covering the whole tree - including the
            ' case where nothing in it can be served at all.
            If checkAcl Then
                Select Case Verdict(current, needed)
                    Case AccessVerdict.Missing
                        result.Blocked.Add(current)
                        result.Grantable.Add(current)
                        Continue While   ' unreachable for the service, and so is all below it
                    Case AccessVerdict.Denied
                        result.Blocked.Add(current)
                        Continue While
                End Select
            End If

            Dim children As String()
            Try
                children = Directory.GetDirectories(current)
            Catch ex As UnauthorizedAccessException
                ' Only meaningful when this process IS the serving identity; in Server
                ' mode the DACL check above already spoke for LOCAL SERVICE, and a folder
                ' this user cannot open says nothing about what the service can.
                If Not checkAcl Then
                    result.Blocked.Add(current)
                    result.Grantable.Add(current)
                End If
                Continue While
            Catch
                ' Vanished mid-walk, path too long, a drive that went away: not an access
                ' problem, and inventing one would send the user chasing a folder that is
                ' fine.
                Continue While
            End Try

            For Each child As String In children
                Try
                    If (New DirectoryInfo(child).Attributes And FileAttributes.ReparsePoint) <> 0 Then Continue For
                Catch
                    Continue For
                End Try
                pending.Push(child)
            Next
        End While

        Return result
    End Function

    ''' <summary>
    ''' Scans every shared folder under ONE shared budget, so the check costs the same
    ''' whether the user shares one folder or ten. Roots are walked in the given order,
    ''' and a scan cut short is reported as such rather than as a clean result.
    ''' </summary>
    Public Function ScanRoots(roots As IEnumerable(Of ShareFolder),
                              Optional budget As TimeSpan = Nothing,
                              Optional cancel As CancellationToken = Nothing,
                              Optional forServiceAccount As Boolean? = Nothing) As SubtreeScan
        Dim total As New SubtreeScan()
        If roots Is Nothing Then Return total
        Dim limit As TimeSpan = If(budget = TimeSpan.Zero, Default_Scan_Budget, budget)
        Dim clock As Stopwatch = Stopwatch.StartNew()

        For Each r As ShareFolder In roots
            If r Is Nothing OrElse String.IsNullOrWhiteSpace(r.hostPath) Then Continue For
            Dim left As TimeSpan = limit - clock.Elapsed
            If left <= TimeSpan.Zero Then
                total.Completed = False
                Exit For
            End If
            Dim one As SubtreeScan = ScanSubtree(r.hostPath, Not r.readOnly, left, cancel, forServiceAccount)
            total.Blocked.AddRange(one.Blocked)
            total.Grantable.AddRange(one.Grantable)
            total.Scanned += one.Scanned
            If Not one.Completed Then total.Completed = False
        Next
        Return total
    End Function

End Module
