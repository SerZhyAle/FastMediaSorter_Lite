#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.AccessControl
Imports System.Security.Principal
Imports System.Text

''' <summary>How an attempt to publish the exported <c>.fmscfg</c> - a KEY (FMSCFG rule 6) - ended.
''' The same four outcomes the Share Manager's <c>ProtectedConfigWriter</c> reports, kept shape-for-shape
''' (ticket 118 Ф-3, R4) so the two writers can be compared line by line.</summary>
Friend Enum ExportWriteOutcome
    ''' <summary>Written, owner-only, verified. The only outcome that may say "saved".</summary>
    Saved = 0
    ''' <summary>The file system cannot enforce an owner-only descriptor (FAT/exFAT, some shares):
    ''' nothing was written.</summary>
    RefusedNoAcls = 1
    ''' <summary>Others can replace what is published there (a folder they may delete from, or a
    ''' redirected path): nothing was written.</summary>
    RefusedUnsafeTarget = 2
    ''' <summary>Anything else - locked target, no space, a failed verification. The previous target
    ''' is unchanged before publication; uncertain publication retains protected recovery evidence.</summary>
    Failed = 3
End Enum

''' <summary>The file operations the writer performs - injectable so each failure of the publication
''' can be produced on demand without a broken disk (the Companion writer's own seam).</summary>
Friend Interface IExportFileOps
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
    ''' <summary>Identity/version of an existing file, or Nothing for an absent target.</summary>
    Function Identity(path As String) As String
End Interface

''' <summary>
''' Publishes the exported <c>.fmscfg</c> from the viewer (ticket 118 Ф-3, FMSCFG rule 5): the
''' password is inside. This is the Share Manager's <c>ProtectedConfigWriter.vb</c> ported, not
''' linked - the Companion project boundary stays untouched by a viewer ticket - and the port is
''' kept line-by-line comparable with the original so a future change there has a fair chance of
''' being recognised here:
'''
'''  1. The destination is judged first: a file system that cannot hold an owner-only descriptor, or a
'''     folder others can delete from or a redirected path, is REFUSED - a user-chosen folder is not
'''     consent to expose a credential.
'''  2. A sibling temp file is created exclusively and ALREADY owner-only, before any secret byte is
'''     written; its descriptor is verified, the bytes are written and flushed.
'''  3. The temp is renamed over the target. A rename keeps the temp's own descriptor; an in-place
'''     overwrite could keep the OLD target's, which is how a permissive existing file would have
'''     received the secret. The result is verified (descriptor AND bytes).
'''  4. Verify identity before replacement and retain an owner-only recovery copy until verification.
'''     A failure never deletes the target by name; another writer may already own it.
'''     "Saved" is reported only after identity, descriptor and bytes are verified.
''' </summary>
Friend Module ExportWriter

    Private ReadOnly Utf8 As New UTF8Encoding(False)

    Friend Function Publish(path As String, json As String, Optional ops As IExportFileOps = Nothing) As ExportWriteOutcome
        Dim files As IExportFileOps = If(ops, RealExportFileOps.Instance)
        If String.IsNullOrWhiteSpace(path) OrElse json Is Nothing Then Return ExportWriteOutcome.Failed

        Dim fullPath As String
        Dim directory As String
        Dim owner As SecurityIdentifier
        Try
            fullPath = System.IO.Path.GetFullPath(path)
            directory = System.IO.Path.GetDirectoryName(fullPath)
            Using identity As WindowsIdentity = WindowsIdentity.GetCurrent()
                owner = identity.User
            End Using
        Catch
            Return ExportWriteOutcome.Failed
        End Try
        If String.IsNullOrEmpty(directory) OrElse owner Is Nothing Then Return ExportWriteOutcome.Failed

        Try
            If files.IsRedirected(directory) OrElse files.IsRedirected(fullPath) Then Return ExportWriteOutcome.RefusedUnsafeTarget
            If Not files.SupportsOwnerOnly(directory) Then Return ExportWriteOutcome.RefusedNoAcls
            If files.OthersCanReplaceIn(directory, owner) Then Return ExportWriteOutcome.RefusedUnsafeTarget
        Catch
            Return ExportWriteOutcome.Failed
        End Try

        Dim bytes As Byte() = Utf8.GetBytes(json)
        Dim temp As String = System.IO.Path.Combine(directory, "." & System.IO.Path.GetFileName(fullPath) & "." & Guid.NewGuid().ToString("N") & ".tmp")
        Dim created As Boolean = False
        Dim recovery As String = temp & ".recovery.tmp"
        Dim recoveryCreated As Boolean = False
        Dim published As Boolean = False
        Try
            Dim originalIdentity As String = files.Identity(fullPath)
            Dim previous As Byte() = If(originalIdentity Is Nothing, bytes, files.ReadAllBytes(fullPath))
            ' Keep a protected rollback copy until final verification. A failed or uncertain
            ' publication must never destroy the previous generation or an unrelated target.
            Using stream As Stream = files.CreateProtected(recovery, owner)
                recoveryCreated = True
                If Not files.IsOwnerOnly(recovery, owner) Then Throw New IOException("Recovery protection failed.")
                stream.Write(previous, 0, previous.Length)
                stream.Flush()
                If TypeOf stream Is FileStream Then DirectCast(stream, FileStream).Flush(True)
            End Using
            If Not files.IsOwnerOnly(recovery, owner) OrElse Not BytesEqual(files.ReadAllBytes(recovery), previous) Then
                Throw New IOException("Recovery verification failed.")
            End If
            Using stream As Stream = files.CreateProtected(temp, owner)
                created = True
                If Not files.IsOwnerOnly(temp, owner) Then Throw New IOException("Temporary protection failed.")
                stream.Write(bytes, 0, bytes.Length)
                stream.Flush()
                If TypeOf stream Is FileStream Then DirectCast(stream, FileStream).Flush(True)
            End Using

            ' The descriptor is checked on what was actually created, BEFORE it is published.
            If Not files.IsOwnerOnly(temp, owner) Then
                Throw New IOException("The temporary file is not owner-only.")
            End If

            Dim newIdentity As String = files.Identity(temp)
            If files.IsRedirected(directory) OrElse files.IsRedirected(fullPath) OrElse
               files.OthersCanReplaceIn(directory, owner) OrElse
               Not String.Equals(originalIdentity, files.Identity(fullPath), StringComparison.Ordinal) Then
                Throw New IOException("The destination changed before publication.")
            End If
            published = True ' A throwing move may still have changed the destination.
            files.Move(temp, fullPath, overwrite:=originalIdentity IsNot Nothing)
            created = False ' the temp name no longer exists; what is at the target is the new file

            If Not String.Equals(newIdentity, files.Identity(fullPath), StringComparison.Ordinal) OrElse
               Not files.IsOwnerOnly(fullPath, owner) OrElse Not BytesEqual(files.ReadAllBytes(fullPath), bytes) Then
                ' Ownership is uncertain. Never delete the target by name: it may already
                ' belong to another writer. Retain the protected recovery evidence.
                Return ExportWriteOutcome.Failed
            End If
            Cleanup(files, recovery, recoveryCreated)
            Return ExportWriteOutcome.Saved
        Catch ex As PlatformNotSupportedException
            Cleanup(files, temp, created)
            If Not published Then Cleanup(files, recovery, recoveryCreated)
            Return ExportWriteOutcome.RefusedNoAcls
        Catch
            Cleanup(files, temp, created)
            If Not published Then Cleanup(files, recovery, recoveryCreated)
            Return ExportWriteOutcome.Failed
        End Try
    End Function

    Private Sub Cleanup(files As IExportFileOps, temp As String, created As Boolean)
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

Friend NotInheritable Class RealExportFileOps
    Implements IExportFileOps

    Friend Shared ReadOnly Instance As New RealExportFileOps()

    Public Function CreateProtected(path As String, ownerSid As SecurityIdentifier) As Stream Implements IExportFileOps.CreateProtected
        Dim security As New FileSecurity()
        security.SetAccessRuleProtection(True, False) ' protected; inherited rules NOT copied
        security.AddAccessRule(New FileSystemAccessRule(ownerSid, FileSystemRights.FullControl, AccessControlType.Allow))
        ' The descriptor is part of the CREATE call: there is no moment at which the file exists with the
        ' folder's inherited one.
        Return FileSystemAclExtensions.Create(New FileInfo(path), FileMode.CreateNew,
                                              FileSystemRights.WriteData Or FileSystemRights.ReadData Or FileSystemRights.ReadAttributes Or FileSystemRights.Synchronize,
                                              FileShare.None, 4096, FileOptions.None, security)
    End Function

    Public Sub Move(source As String, destination As String, overwrite As Boolean) Implements IExportFileOps.Move
        File.Move(source, destination, overwrite)
    End Sub

    Public Sub Delete(path As String) Implements IExportFileOps.Delete
        File.Delete(path)
    End Sub

    Public Function Exists(path As String) As Boolean Implements IExportFileOps.Exists
        Return File.Exists(path)
    End Function

    Public Function IsOwnerOnly(path As String, ownerSid As SecurityIdentifier) As Boolean Implements IExportFileOps.IsOwnerOnly
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

    Public Function SupportsOwnerOnly(directory As String) As Boolean Implements IExportFileOps.SupportsOwnerOnly
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

    Public Function OthersCanReplaceIn(directory As String, ownerSid As SecurityIdentifier) As Boolean Implements IExportFileOps.OthersCanReplaceIn
        Dim current As String = Path.GetFullPath(directory)
        Do While Not String.IsNullOrEmpty(current)
            Dim di As New DirectoryInfo(current)
            Dim security As DirectorySecurity = di.GetAccessControl(AccessControlSections.Access)
            If OthersCanReplaceDirectory(security, ownerSid) Then Return True
            If security.AreAccessRulesProtected Then Exit Do
            current = Path.GetDirectoryName(current)
        Loop
        Return False
    End Function

    Private Function OthersCanReplaceDirectory(security As DirectorySecurity, ownerSid As SecurityIdentifier) As Boolean
        Dim trusted As New List(Of SecurityIdentifier) From {
            ownerSid,
            New SecurityIdentifier(WellKnownSidType.LocalSystemSid, Nothing),
            New SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, Nothing),
            New SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, Nothing)}
        Const Replaces As FileSystemRights = FileSystemRights.DeleteSubdirectoriesAndFiles Or FileSystemRights.Delete Or
                                             FileSystemRights.ChangePermissions Or FileSystemRights.TakeOwnership
        For Each rule As FileSystemAccessRule In security.GetAccessRules(True, True, GetType(SecurityIdentifier))
            If rule.AccessControlType <> AccessControlType.Allow Then Continue For
            If (rule.PropagationFlags And PropagationFlags.InheritOnly) <> 0 Then Continue For
            If (rule.FileSystemRights And Replaces) = 0 Then Continue For
            Dim sid As SecurityIdentifier = DirectCast(rule.IdentityReference, SecurityIdentifier)
            If trusted.Contains(sid) Then Continue For
            ' TrustedInstaller owns system folders; a user file is never published there anyway.
            If sid.Value.StartsWith("S-1-5-80-", StringComparison.Ordinal) Then Continue For
            Return True
        Next
        Return False
    End Function

    Public Function IsRedirected(path As String) As Boolean Implements IExportFileOps.IsRedirected
        Dim current As String = System.IO.Path.GetFullPath(path)
        Do While Not String.IsNullOrEmpty(current)
            If File.Exists(current) OrElse Directory.Exists(current) Then
                If (File.GetAttributes(current) And FileAttributes.ReparsePoint) <> 0 Then Return True
            End If
            current = System.IO.Path.GetDirectoryName(current)
        Loop
        Return False
    End Function

    Public Function ReadAllBytes(path As String) As Byte() Implements IExportFileOps.ReadAllBytes
        Return File.ReadAllBytes(path)
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Private Structure FileIdentityInfo
        Public Attributes As UInteger
        Public Created As Runtime.InteropServices.ComTypes.FILETIME
        Public Accessed As Runtime.InteropServices.ComTypes.FILETIME
        Public Written As Runtime.InteropServices.ComTypes.FILETIME
        Public Volume As UInteger
        Public SizeHigh As UInteger
        Public SizeLow As UInteger
        Public Links As UInteger
        Public IndexHigh As UInteger
        Public IndexLow As UInteger
    End Structure

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function GetFileInformationByHandle(handle As Microsoft.Win32.SafeHandles.SafeFileHandle,
                                                       ByRef info As FileIdentityInfo) As Boolean
    End Function

    Public Function Identity(path As String) As String Implements IExportFileOps.Identity
        Try
            Using handle As Microsoft.Win32.SafeHandles.SafeFileHandle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                Dim info As New FileIdentityInfo()
                If Not GetFileInformationByHandle(handle, info) Then Throw New IOException("Cannot examine destination identity.")
                Return String.Join(":", info.Volume, info.IndexHigh, info.IndexLow, info.SizeHigh, info.SizeLow,
                                   info.Written.dwHighDateTime, info.Written.dwLowDateTime)
            End Using
        Catch ex As FileNotFoundException
            Return Nothing
        End Try
    End Function
End Class
#End If
