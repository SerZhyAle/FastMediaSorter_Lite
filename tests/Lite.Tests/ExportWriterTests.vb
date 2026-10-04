#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Security.AccessControl
Imports System.Security.Principal
Imports System.Text
Imports Xunit

''' <summary>
''' Ticket 118 Ф-3 / FMSCFG rule 5: the viewer's protected publication of the exported .fmscfg,
''' proven against a fake <see cref="IExportFileOps"/> - the Companion writer's failure matrix,
''' reproduced for the ported writer: saved; redirected target; no-ACL volume;
''' others-can-replace; temp creation failure; a verification mismatch keeps the recovery copy
''' and never deletes the target by name; rename failure; and a rename that replaced the target
''' leaves the new file's own descriptor in force. Every failure leaves the previous target
''' exactly as it was.
''' </summary>
Public Class ExportWriterTests

    Private Const Json As String = "{""schemaVersion"":1,""resourceName"":""t""}"
    Private Const Target As String = "C:\export\NAS.fmscfg"

    ''' <summary>An in-memory file system. A created file keeps an open stream while the writer
    ''' works on it; every read takes the stream's LIVE bytes - the writer may still hold the
    ''' stream open when it asks, and a MemoryStream answers with what is written so far.</summary>
    Private NotInheritable Class FakeFiles
        Implements IExportFileOps

        Friend Files As New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)
        Friend Acls As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
        Friend OpenStreams As New Dictionary(Of String, MemoryStream)(StringComparer.OrdinalIgnoreCase)
        Friend PreExisting As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Friend Redirected As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Friend SupportsAcls As Boolean = True
        ''' <summary>True when the volume accepts the create call but the descriptor does not hold
        ''' (a silently dropped ACL on every newly created file).</summary>
        Friend NewFilesUnprotected As Boolean = False
        Friend OthersCanReplace As Boolean = False
        Friend FailCreate As Boolean = False
        Friend FailMove As Boolean = False
        Friend MoveCount As Integer = 0
        ''' <summary>Paths whose byte verification will not match what was written to them - applied
        ''' to reads AFTER the publication attempt, so the recovery copy is written from honest bytes.</summary>
        Friend CorruptOnRead As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        Public Function CreateProtected(path As String, ownerSid As SecurityIdentifier) As Stream Implements IExportFileOps.CreateProtected
            If FailCreate Then Throw New IOException("create refused")
            If Redirected.Contains(path) Then Throw New IOException("redirected")
            If Not OpenStreams.ContainsKey(path) Then OpenStreams(path) = New MemoryStream()
            Acls(path) = SupportsAcls
            Return OpenStreams(path)
        End Function

        Public Sub Move(source As String, destination As String, overwrite As Boolean) Implements IExportFileOps.Move
            MoveCount += 1
            If FailMove Then
                MoveCount -= 1
                Throw New IOException("move refused")
            End If
            If Files.ContainsKey(destination) AndAlso Not overwrite AndAlso Not OpenStreams.ContainsKey(destination) Then
                Throw New IOException("destination exists")
            End If
            Files(destination) = LiveBytes(source)
            Acls(destination) = If(Acls.ContainsKey(source), Acls(source), False)
            Files.Remove(source)
            Acls.Remove(source)
            OpenStreams.Remove(source)
        End Sub

        Public Sub Delete(path As String) Implements IExportFileOps.Delete
            Files.Remove(path)
            Acls.Remove(path)
            OpenStreams.Remove(path)
        End Sub

        Public Function Exists(path As String) As Boolean Implements IExportFileOps.Exists
            Return Files.ContainsKey(path) OrElse OpenStreams.ContainsKey(path)
        End Function

        Public Function IsOwnerOnly(path As String, ownerSid As SecurityIdentifier) As Boolean Implements IExportFileOps.IsOwnerOnly
            If Not Files.ContainsKey(path) AndAlso Not OpenStreams.ContainsKey(path) Then Return False
            If NewFilesUnprotected AndAlso Not PreExisting.Contains(path) Then Return False
            Return If(Acls.ContainsKey(path), Acls(path), False)
        End Function

        Public Function SupportsOwnerOnly(directory As String) As Boolean Implements IExportFileOps.SupportsOwnerOnly
            Return SupportsAcls
        End Function

        Public Function OthersCanReplaceIn(directory As String, ownerSid As SecurityIdentifier) As Boolean Implements IExportFileOps.OthersCanReplaceIn
            Return OthersCanReplace
        End Function

        Public Function IsRedirected(path As String) As Boolean Implements IExportFileOps.IsRedirected
            Return Redirected.Contains(path)
        End Function

        Public Function ReadAllBytes(path As String) As Byte() Implements IExportFileOps.ReadAllBytes
            If Not Exists(path) Then Throw New FileNotFoundException(path)
            If MoveCount > 0 AndAlso CorruptOnRead.Contains(path) Then Return Encoding.UTF8.GetBytes("corrupted")
            Return LiveBytes(path)
        End Function

        Public Function Identity(path As String) As String Implements IExportFileOps.Identity
            If Not Exists(path) Then Return Nothing
            Dim bytes As Byte() = LiveBytes(path)
            Return bytes.Length.ToString() & ":" & BitConverter.ToString(System.Security.Cryptography.MD5.Create().ComputeHash(bytes))
        End Function

        Friend Sub PutExisting(path As String, bytes As Byte(), ownerOnly As Boolean)
            Files(path) = bytes
            Acls(path) = ownerOnly
            PreExisting.Add(path)
        End Sub

        Private Function LiveBytes(path As String) As Byte()
            Dim stream As MemoryStream = Nothing
            If OpenStreams.TryGetValue(path, stream) Then Return stream.ToArray()
            Return Files(path)
        End Function

        Friend Function RecoveryPaths() As IEnumerable(Of String)
            Return Files.Keys.Concat(OpenStreams.Keys).Where(Function(k) k.EndsWith(".recovery.tmp", StringComparison.Ordinal))
        End Function

        Friend Function TempPaths() As IEnumerable(Of String)
            Return Files.Keys.Concat(OpenStreams.Keys).
                Where(Function(k) k.EndsWith(".tmp", StringComparison.Ordinal) AndAlso Not k.EndsWith(".recovery.tmp", StringComparison.Ordinal))
        End Function
    End Class

    Private Shared Function Utf8(text As String) As Byte()
        Return Encoding.UTF8.GetBytes(text)
    End Function

    <Fact>
    Public Sub Saved_WhenEverythingHolds()
        Dim files As New FakeFiles()
        Assert.Equal(ExportWriteOutcome.Saved, ExportWriter.Publish(Target, Json, files))
        Assert.Equal(Utf8(Json), files.ReadAllBytes(Target))
        Assert.True(files.Acls(Target), "the published file keeps the owner-only descriptor of the temp it was renamed from")
        ' No temp and no recovery copy is left behind.
        Assert.Empty(files.RecoveryPaths())
        Assert.Empty(files.TempPaths())
        Assert.Equal(1, files.MoveCount)
    End Sub

    <Fact>
    Public Sub Refused_WhenTheTargetIsRedirected()
        Dim files As New FakeFiles()
        files.Redirected.Add(Target)
        Assert.Equal(ExportWriteOutcome.RefusedUnsafeTarget, ExportWriter.Publish(Target, Json, files))
        Assert.Equal(0, files.MoveCount)
    End Sub

    <Fact>
    Public Sub Refused_WhenTheVolumeCannotHoldAnOwnerOnlyDescriptor()
        Dim files As New FakeFiles() With {.SupportsAcls = False}
        Assert.Equal(ExportWriteOutcome.RefusedNoAcls, ExportWriter.Publish(Target, Json, files))
        Assert.False(files.Files.ContainsKey(Target), "nothing was written")
    End Sub

    <Fact>
    Public Sub Refused_WhenOthersCanReplaceInTheFolder()
        Dim files As New FakeFiles() With {.OthersCanReplace = True}
        Assert.Equal(ExportWriteOutcome.RefusedUnsafeTarget, ExportWriter.Publish(Target, Json, files))
        Assert.False(files.Files.ContainsKey(Target))
    End Sub

    <Fact>
    Public Sub TempCreationFailure_LeavesThePreviousTargetUntouched()
        Dim files As New FakeFiles()
        files.PutExisting(Target, Utf8("previous"), True)
        files.FailCreate = True

        Assert.Equal(ExportWriteOutcome.Failed, ExportWriter.Publish(Target, Json, files))
        Assert.Equal(Utf8("previous"), files.ReadAllBytes(Target))
        Assert.Equal(0, files.MoveCount)
        ' Everything this call created is gone again; only the target remains.
        Assert.Empty(files.RecoveryPaths())
        Assert.Empty(files.TempPaths())
    End Sub

    <Fact>
    Public Sub VerificationMismatch_KeepsTheRecoveryCopy_AndNeverDeletesTheTarget()
        Dim files As New FakeFiles()
        files.PutExisting(Target, Utf8("previous"), True)
        files.CorruptOnRead.Add(Target) ' after the rename, the bytes read back wrong

        Assert.Equal(ExportWriteOutcome.Failed, ExportWriter.Publish(Target, Json, files))
        ' The target still exists - it is never deleted by name after publication.
        Assert.Equal(Utf8("corrupted"), files.ReadAllBytes(Target))
        ' A protected recovery sibling remains, carrying the previous generation's bytes.
        Assert.Equal(1, files.RecoveryPaths().Count())
        Assert.Equal(Utf8("previous"), files.ReadAllBytes(files.RecoveryPaths().Single()))
    End Sub

    <Fact>
    Public Sub RenameFailure_LeavesThePreviousTargetAndCleansTheTemp()
        Dim files As New FakeFiles()
        files.PutExisting(Target, Utf8("previous"), True)
        files.FailMove = True

        Assert.Equal(ExportWriteOutcome.Failed, ExportWriter.Publish(Target, Json, files))
        Assert.Equal(Utf8("previous"), files.ReadAllBytes(Target))
        ' The temp this call created is gone again; the protected recovery copy is RETAINED -
        ' a throwing rename may have changed the destination, so the evidence stays.
        Assert.Empty(files.TempPaths())
        Assert.Equal(1, files.RecoveryPaths().Count())
        Assert.Equal(Utf8("previous"), files.ReadAllBytes(files.RecoveryPaths().Single()))
    End Sub

    <Fact>
    Public Sub PublishedByRename_SoTheNewFileKeepsItsOwnDescriptor()
        ' The whole reason for publishing by rename: an in-place overwrite could keep the OLD
        ' target's permissive descriptor. The fake records each file's own ACL, so the published
        ' file must carry the temp's owner-only one even beside a permissive previous target.
        Dim files As New FakeFiles()
        files.PutExisting(Target, Utf8("previous"), False)
        Assert.Equal(ExportWriteOutcome.Saved, ExportWriter.Publish(Target, Json, files))
        Assert.True(files.Acls(Target))
        Assert.Equal(Utf8(Json), files.ReadAllBytes(Target))
    End Sub

    <Fact>
    Public Sub AnAclThatSilentlyDropsIsCaughtBeforePublication()
        ' The descriptor check happens on what was CREATED, before publication: a volume that
        ' silently drops the ACL fails the publish instead of handing over the key.
        Dim files As New FakeFiles() With {.NewFilesUnprotected = True}
        Assert.Equal(ExportWriteOutcome.Failed, ExportWriter.Publish(Target, Json, files))
        Assert.Equal(0, files.MoveCount)
    End Sub

End Class
#End If
