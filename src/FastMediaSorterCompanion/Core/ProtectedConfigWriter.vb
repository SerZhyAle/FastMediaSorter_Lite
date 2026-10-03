Option Strict On

Imports System.IO
Imports System.Security.AccessControl
Imports System.Security.Principal
Imports System.Text

''' <summary>How an attempt to publish a secret-bearing file ended.</summary>
Friend Enum ConfigWriteOutcome
    ''' <summary>Written, owner-only, verified. The only outcome that may say "saved".</summary>
    Saved = 0
    ''' <summary>The file system cannot enforce an owner-only descriptor (FAT/exFAT, some shares):
    ''' nothing was written. The user is told to pick an ACL-capable destination.</summary>
    RefusedNoAcls = 1
    ''' <summary>Others can replace what is published there (a folder they may delete from, or a
    ''' redirected path): nothing was written.</summary>
    RefusedUnsafeTarget = 2
    ''' <summary>Anything else - locked target, no space, a failed verification. The previous target is
    ''' untouched; only a file this call created may have been removed.</summary>
    Failed = 3
End Enum

Friend NotInheritable Class ConfigWriteRefusedException
    Inherits IOException

    Friend ReadOnly Property Outcome As ConfigWriteOutcome

    Friend Sub New(outcome As ConfigWriteOutcome)
        MyBase.New("The configuration file was not written: " & outcome.ToString())
        Me.Outcome = outcome
    End Sub
End Class

''' <summary>The file operations the writer performs - injectable so each failure of the publication
''' can be produced on demand without a broken disk.</summary>
Friend Interface IConfigFileOps
    ''' <summary>Creates <paramref name="path"/> exclusively with an owner-only descriptor from the
    ''' first moment it exists, and returns the open stream.</summary>
    Function CreateProtected(path As String, ownerSid As SecurityIdentifier) As Stream
    Sub Move(source As String, destination As String, overwrite As Boolean)
    Sub Delete(path As String)
    Function Exists(path As String) As Boolean
    ''' <summary>True when the file carries a protected descriptor naming ONLY the owner.</summary>
    Function IsOwnerOnly(path As String, ownerSid As SecurityIdentifier) As Boolean
    ''' <summary>True when the file system can enforce descriptors at all.</summary>
    Function SupportsOwnerOnly(directory As String) As Boolean
    ''' <summary>True when someone other than the owner and the system may delete from / replace in the folder.</summary>
    Function OthersCanReplaceIn(directory As String, ownerSid As SecurityIdentifier) As Boolean
    ''' <summary>True for a junction or symbolic link at <paramref name="path"/>.</summary>
    Function IsRedirected(path As String) As Boolean
    ''' <summary>The bytes of the file, for the final hash check.</summary>
    Function ReadAllBytes(path As String) As Byte()
End Interface

''' <summary>
''' Publishes an exported .fmscfg - a KEY (FMSCFG rule 6): the password and any PIN are inside. 108 H03:
'''
'''  1. The destination is judged first: a file system that cannot hold an owner-only descriptor, or a
'''     folder others can delete from or a redirected path, is REFUSED - a user-chosen folder is not
'''     consent to expose a credential.
'''  2. A sibling temp file is created exclusively and ALREADY owner-only, before any secret byte is
'''     written; its descriptor is verified, the bytes are written and flushed.
'''  3. The temp is renamed over the target. A rename keeps the temp's own descriptor; an in-place
'''     overwrite (or File.Replace) would keep the OLD target's, which is how a permissive existing
'''     file would have received the secret. The result is verified (descriptor AND bytes).
'''  4. Any failure before the rename leaves the previous target exactly as it was; only this call's own
'''     temp is removed. "Saved" is reported only after the verification.
''' </summary>
Friend Module ProtectedConfigWriter

    Private ReadOnly Utf8 As New UTF8Encoding(False)

    Friend Function Publish(path As String, json As String, Optional ops As IConfigFileOps = Nothing) As ConfigWriteOutcome
        Dim files As IConfigFileOps = If(ops, RealConfigFileOps.Instance)
        If String.IsNullOrWhiteSpace(path) OrElse json Is Nothing Then Return ConfigWriteOutcome.Failed

        Dim fullPath As String
        Dim directory As String
        Dim owner As SecurityIdentifier
        Try
            fullPath = System.IO.Path.GetFullPath(path)
            directory = System.IO.Path.GetDirectoryName(fullPath)
            owner = WindowsIdentity.GetCurrent().User
        Catch
            Return ConfigWriteOutcome.Failed
        End Try
        If String.IsNullOrEmpty(directory) OrElse owner Is Nothing Then Return ConfigWriteOutcome.Failed

        Try
            If files.IsRedirected(directory) OrElse files.IsRedirected(fullPath) Then Return ConfigWriteOutcome.RefusedUnsafeTarget
            If Not files.SupportsOwnerOnly(directory) Then Return ConfigWriteOutcome.RefusedNoAcls
            If files.OthersCanReplaceIn(directory, owner) Then Return ConfigWriteOutcome.RefusedUnsafeTarget
        Catch
            Return ConfigWriteOutcome.Failed
        End Try

        Dim bytes As Byte() = Utf8.GetBytes(json)
        Dim temp As String = System.IO.Path.Combine(directory, "." & System.IO.Path.GetFileName(fullPath) & "." & Guid.NewGuid().ToString("N") & ".tmp")
        Dim created As Boolean = False
        Try
            Using stream As Stream = files.CreateProtected(temp, owner)
                created = True
                stream.Write(bytes, 0, bytes.Length)
                stream.Flush()
                If TypeOf stream Is FileStream Then DirectCast(stream, FileStream).Flush(True)
            End Using

            ' The descriptor is checked on what was actually created, BEFORE it is published.
            If Not files.IsOwnerOnly(temp, owner) Then
                Throw New IOException("The temporary file is not owner-only.")
            End If

            files.Move(temp, fullPath, overwrite:=True)
            created = False ' the temp name no longer exists; what is at the target is the new file

            If Not files.IsOwnerOnly(fullPath, owner) OrElse Not BytesEqual(files.ReadAllBytes(fullPath), bytes) Then
                ' Published but not as intended: it is ours, and it holds a secret - take it back.
                Try : files.Delete(fullPath) : Catch : End Try
                Return ConfigWriteOutcome.Failed
            End If
            Return ConfigWriteOutcome.Saved
        Catch ex As PlatformNotSupportedException
            Cleanup(files, temp, created)
            Return ConfigWriteOutcome.RefusedNoAcls
        Catch
            Cleanup(files, temp, created)
            Return ConfigWriteOutcome.Failed
        End Try
    End Function

    ''' <summary>
    ''' Creates a folder only this user (and the system) can touch, protected from the first moment.
    ''' The e-mail hand-off uses it for its throw-away copy: a shared TEMP is not a safe place for a
    ''' key by itself, so the writer is pointed at a folder that is.
    ''' </summary>
    Friend Function CreatePrivateDirectory(path As String) As Boolean
        Try
            Dim owner As SecurityIdentifier = WindowsIdentity.GetCurrent().User
            If owner Is Nothing Then Return False
            Dim security As New DirectorySecurity()
            security.SetAccessRuleProtection(True, False)
            Dim inherit As InheritanceFlags = InheritanceFlags.ContainerInherit Or InheritanceFlags.ObjectInherit
            security.AddAccessRule(New FileSystemAccessRule(owner, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow))
            security.AddAccessRule(New FileSystemAccessRule(New SecurityIdentifier(WellKnownSidType.LocalSystemSid, Nothing),
                                                            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow))
            FileSystemAclExtensions.Create(New DirectoryInfo(path), security)
            Return True
        Catch
            Return False
        End Try
    End Function
    Private Sub Cleanup(files As IConfigFileOps, temp As String, created As Boolean)
        ' Only the temp THIS call created - never the target, never anything it did not make.
        If Not created Then Return
        Try
            If files.Exists(temp) Then files.Delete(temp)
        Catch
        End Try
    End Sub

    Private Function BytesEqual(a As Byte(), b As Byte()) As Boolean
        If a Is Nothing OrElse b Is Nothing OrElse a.Length <> b.Length Then Return False
        For i As Integer = 0 To a.Length - 1
            If a(i) <> b(i) Then Return False
        Next
        Return True
    End Function

End Module

Friend NotInheritable Class RealConfigFileOps
    Implements IConfigFileOps

    Friend Shared ReadOnly Instance As New RealConfigFileOps()

    Public Function CreateProtected(path As String, ownerSid As SecurityIdentifier) As Stream Implements IConfigFileOps.CreateProtected
        Dim security As New FileSecurity()
        security.SetAccessRuleProtection(True, False) ' protected; inherited rules NOT copied
        security.AddAccessRule(New FileSystemAccessRule(ownerSid, FileSystemRights.FullControl, AccessControlType.Allow))
        ' The descriptor is part of the CREATE call: there is no moment at which the file exists with the
        ' folder's inherited one.
        Return FileSystemAclExtensions.Create(New FileInfo(path), FileMode.CreateNew,
                                              FileSystemRights.WriteData Or FileSystemRights.ReadData Or FileSystemRights.ReadAttributes Or FileSystemRights.Synchronize,
                                              FileShare.None, 4096, FileOptions.None, security)
    End Function

    Public Sub Move(source As String, destination As String, overwrite As Boolean) Implements IConfigFileOps.Move
        File.Move(source, destination, overwrite)
    End Sub

    Public Sub Delete(path As String) Implements IConfigFileOps.Delete
        File.Delete(path)
    End Sub

    Public Function Exists(path As String) As Boolean Implements IConfigFileOps.Exists
        Return File.Exists(path)
    End Function

    Public Function IsOwnerOnly(path As String, ownerSid As SecurityIdentifier) As Boolean Implements IConfigFileOps.IsOwnerOnly
        Dim security As FileSecurity = New FileInfo(path).GetAccessControl(AccessControlSections.Access)
        If Not security.AreAccessRulesProtected Then Return False
        Dim rules As AuthorizationRuleCollection = security.GetAccessRules(True, True, GetType(SecurityIdentifier))
        If rules.Count = 0 Then Return False
        For Each rule As FileSystemAccessRule In rules
            If rule.IsInherited Then Return False
            If Not rule.IdentityReference.Equals(ownerSid) Then Return False
            If rule.AccessControlType <> AccessControlType.Allow Then Return False
        Next
        Return True
    End Function

    Public Function SupportsOwnerOnly(directory As String) As Boolean Implements IConfigFileOps.SupportsOwnerOnly
        Try
            Dim root As String = Path.GetPathRoot(directory)
            If String.IsNullOrEmpty(root) Then Return False
            If root.StartsWith("\\", StringComparison.Ordinal) Then
                ' A share: the server's file system decides. The descriptor check on the created temp
                ' is the proof; a share that cannot hold one fails that check or the create call.
                Return True
            End If
            Dim format As String = New DriveInfo(root).DriveFormat
            Return String.Equals(format, "NTFS", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(format, "ReFS", StringComparison.OrdinalIgnoreCase)
        Catch
            Return False
        End Try
    End Function

    Public Function OthersCanReplaceIn(directory As String, ownerSid As SecurityIdentifier) As Boolean Implements IConfigFileOps.OthersCanReplaceIn
        Dim security As DirectorySecurity = New DirectoryInfo(directory).GetAccessControl(AccessControlSections.Access)
        Dim trusted As New List(Of SecurityIdentifier) From {
            ownerSid,
            New SecurityIdentifier(WellKnownSidType.LocalSystemSid, Nothing),
            New SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, Nothing),
            New SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, Nothing)}
        Const Replaces As FileSystemRights = FileSystemRights.DeleteSubdirectoriesAndFiles Or FileSystemRights.Delete Or
                                             FileSystemRights.ChangePermissions Or FileSystemRights.TakeOwnership
        For Each rule As FileSystemAccessRule In security.GetAccessRules(True, True, GetType(SecurityIdentifier))
            If rule.AccessControlType <> AccessControlType.Allow Then Continue For
            If (rule.FileSystemRights And Replaces) = 0 Then Continue For
            Dim sid As SecurityIdentifier = DirectCast(rule.IdentityReference, SecurityIdentifier)
            If trusted.Contains(sid) Then Continue For
            ' TrustedInstaller owns system folders; a user file is never published there anyway.
            If sid.Value.StartsWith("S-1-5-80-", StringComparison.Ordinal) Then Continue For
            Return True
        Next
        Return False
    End Function

    Public Function IsRedirected(path As String) As Boolean Implements IConfigFileOps.IsRedirected
        Try
            If Not File.Exists(path) AndAlso Not Directory.Exists(path) Then Return False
            Dim attributes As FileAttributes = File.GetAttributes(path)
            Return (attributes And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint
        Catch
            Return False
        End Try
    End Function

    Public Function ReadAllBytes(path As String) As Byte() Implements IConfigFileOps.ReadAllBytes
        Return File.ReadAllBytes(path)
    End Function
End Class
