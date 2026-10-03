#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports Xunit

''' <summary>
''' 100 §15.6 (T10): the transfer state machine against a fault-injecting provider - copy vs move,
''' same vs cross resource, conflicts, content verification by read-back, the backup/restore dance,
''' reconciliation of an uncertain step, the refusal to delete an SFTP source, cancel at every
''' stage and the conditional undo. Nothing here touches a network.
''' </summary>
Public Class TransferExecutorTests

    ' ------------------------------------------------------------------ the double ----

    Private NotInheritable Class SyncProgress(Of T)
        Implements IProgress(Of T)
        Private ReadOnly _action As Action(Of T)
        Public Sub New(action As Action(Of T))
            _action = action
        End Sub
        Public Sub Report(value As T) Implements IProgress(Of T).Report
            _action?.Invoke(value)
        End Sub
    End Class

    ''' <summary>The scratch provider plus the failures a real link produces. Rename/Delete
    ''' behaviours: 0 = normal, 1 = throw WITHOUT doing it, 2 = do it, then throw (the answer was lost).</summary>
    Private NotInheritable Class FaultProvider
        Implements IFileSystemProvider, ITypedStatProvider

        Friend ReadOnly Inner As ResourceTestProvider
        Private ReadOnly caps As ProviderCapabilities
        Friend RenameBehavior As Func(Of String, String, Integer) = Function(a, b) 0
        Friend DeleteBehavior As Func(Of String, Integer) = Function(p) 0
        ''' <summary>0 = normal, 1 = the link dies before a byte is read.</summary>
        Friend DownloadBehavior As Func(Of String, Integer) = Function(p) 0
        ''' <summary>0 = normal, 1 = the link dies before the stage is written.</summary>
        Friend UploadBehavior As Func(Of String, Integer) = Function(p) 0
        Friend StatFault As Func(Of String, StatResult)
        Friend UploadMutator As Func(Of Byte(), Byte())
        Friend AfterUpload As Action(Of String)
        Friend ReadOnly Renames As New List(Of String)()
        Friend ReadOnly Deletes As New List(Of String)()
        Friend ReadOnly Uploads As New List(Of String)()
        Friend ReadOnly Downloads As New List(Of String)()

        Friend Sub New(root As String, caps As ProviderCapabilities)
            Inner = New ResourceTestProvider(root, caps)
            Me.caps = caps
        End Sub

        Public ReadOnly Property Kind As ResourceKind Implements IFileSystemProvider.Kind
            Get
                Return ResourceKind.Sftp
            End Get
        End Property
        Public ReadOnly Property Capabilities As ProviderCapabilities Implements IFileSystemProvider.Capabilities
            Get
                Return caps
            End Get
        End Property
        Public Function ListEntries(dirPath As String, ct As CancellationToken) As IReadOnlyList(Of ResourceEntry) Implements IFileSystemProvider.ListEntries
            Return Inner.ListEntries(dirPath, ct)
        End Function
        Public Function TryStat(path As String, ByRef entry As ResourceEntry) As Boolean Implements IFileSystemProvider.TryStat
            Return Inner.TryStat(path, entry)
        End Function
        Public Function Stat(path As String) As StatResult Implements ITypedStatProvider.Stat
            If StatFault IsNot Nothing Then
                Dim forced As StatResult = StatFault(path)
                If forced IsNot Nothing Then Return forced
            End If
            Dim entry As ResourceEntry = Nothing
            Return If(Inner.TryStat(path, entry), StatResult.Found(entry), StatResult.Missing())
        End Function
        Public Sub Download(remotePath As String, localPath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Download
            Downloads.Add(remotePath)
            If DownloadBehavior(remotePath) = 1 Then Throw New IOException("link dropped before a byte was read")
            Inner.Download(remotePath, localPath, progress, ct)
        End Sub
        Public Sub Upload(localPath As String, remotePath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Upload
            Uploads.Add(remotePath)
            ct.ThrowIfCancellationRequested()
            If UploadBehavior(remotePath) = 1 Then Throw New IOException("link dropped before the stage was written")
            Dim bytes As Byte() = File.ReadAllBytes(localPath)
            If UploadMutator IsNot Nothing Then bytes = UploadMutator(bytes)
            Inner.PutFile(remotePath, bytes)
            progress?.Report(bytes.Length)
            AfterUpload?.Invoke(remotePath)
            ct.ThrowIfCancellationRequested()
        End Sub
        Public Sub DeleteFile(remotePath As String) Implements IFileSystemProvider.DeleteFile
            Deletes.Add(remotePath)
            Dim mode As Integer = DeleteBehavior(remotePath)
            If mode = 1 Then Throw New IOException("link dropped before delete")
            File.Delete(Inner.BackingPath(remotePath))
            If mode = 2 Then Throw New IOException("link dropped after delete")
        End Sub
        Public Sub Rename(remotePath As String, newRemotePath As String) Implements IFileSystemProvider.Rename
            Renames.Add(remotePath & " -> " & newRemotePath)
            Dim mode As Integer = RenameBehavior(remotePath, newRemotePath)
            If mode = 1 Then Throw New IOException("link dropped before rename")
            Dim target As String = Inner.BackingPath(newRemotePath)
            Directory.CreateDirectory(Path.GetDirectoryName(target))
            File.Move(Inner.BackingPath(remotePath), target, overwrite:=caps.SupportsAtomicOverwriteRename)
            If mode = 2 Then Throw New IOException("link dropped after rename")
        End Sub
        Public Sub CreateDirectory(remotePath As String) Implements IFileSystemProvider.CreateDirectory
            Inner.CreateDirectory(remotePath)
        End Sub

        Friend Function Exists(path As String) As Boolean
            Return File.Exists(Inner.BackingPath(path))
        End Function
        Friend Function Bytes(path As String) As Byte()
            Return File.ReadAllBytes(Inner.BackingPath(path))
        End Function
        Friend Sub Put(path As String, data As Byte())
            Inner.PutFile(path, data)
        End Sub
        Friend Function Leftovers(folder As String) As String()
            Dim dir As String = Inner.BackingPath(folder)
            Return If(Directory.Exists(dir), Directory.GetFiles(dir).Select(Function(f) Path.GetFileName(f)).Where(Function(n) n.StartsWith(".")).ToArray(), Array.Empty(Of String)())
        End Function
    End Class

    Private Shared ReadOnly Sftpish As New ProviderCapabilities(supportsServerSideRename:=True)
    Private Shared Function Caps(Optional write As Boolean = True, Optional delete As Boolean = True, Optional rename As Boolean = False,
                                 Optional atomic As Boolean = False, Optional stable As Boolean = False) As ProviderCapabilities
        Return New ProviderCapabilities(canWrite:=write, canDelete:=delete, supportsServerSideRename:=rename,
                                        supportsAtomicOverwriteRename:=atomic, supportsStableSourceDelete:=stable)
    End Function

    Private Shared Function Dir() As String
        Dim d As String = Path.Combine(Path.GetTempPath(), "fms-t10-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(d)
        Return d
    End Function

    Private Shared Function Data(length As Integer, Optional seed As Integer = 3) As Byte()
        Return Enumerable.Range(0, length).Select(Function(i) CByte((i * 31 + seed) Mod 251)).ToArray()
    End Function

    Private Shared Function Plan(kind As TransferKind, src As FaultProvider, srcPath As String, dst As FaultProvider, dstPath As String,
                                 Optional conflict As ConflictPolicy = ConflictPolicy.Fail, Optional sameResource As Boolean = False) As TransferPlan
        Return New TransferPlan(kind, New TransferEndpoint(src, srcPath, "A"), New TransferEndpoint(dst, dstPath, If(sameResource, "A", "B")), conflict, Dir())
    End Function

    Private Shared Sub Stamp(provider As FaultProvider, path As String, Optional offsetSeconds As Integer = 5)
        File.SetLastWriteTimeUtc(provider.Inner.BackingPath(path), DateTime.UtcNow.AddSeconds(offsetSeconds))
    End Sub

    ' ------------------------------------------------------------------- copy ----

    <Fact>
    Public Sub A_cross_resource_copy_stages_verifies_publishes_and_leaves_the_source_alone()
        Dim src As New FaultProvider(Dir(), Caps(stable:=False))
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(5000))
        Dim plan As TransferPlan = TransferExecutorTests.Plan(TransferKind.Copy, src, "/s/a.jpg", dst, "/in/a.jpg")
        Dim reported As New List(Of Long)()

        Dim outcome As TransferOutcome = TransferExecutor.Execute(plan, New SyncProgress(Of Long)(Sub(v) reported.Add(v)))

        Assert.Equal(TransferResult.Completed, outcome.Result)
        Assert.Equal(TransferState.Completed, outcome.LastState)
        Assert.Equal(Data(5000), dst.Bytes("/in/a.jpg"))
        Assert.True(src.Exists("/s/a.jpg"))
        Assert.Empty(dst.Leftovers("/in"))
        Assert.Contains(plan.OperationId, plan.StagePath)
        Assert.Contains(plan.OperationId, plan.BackupPath)
        ' Download + upload + the verification read-back: three passes, all counted.
        Assert.Equal(15000L, outcome.BytesReported)
        Assert.Equal(reported.OrderBy(Function(v) v).ToArray(), reported.ToArray()) ' never goes backwards
        Assert.Equal({"/in/." & "a.jpg." & plan.OperationId & ".fms-stage -> /in/a.jpg"}, dst.Renames.ToArray())
    End Sub

    <Fact>
    Public Sub Copying_out_of_a_read_only_source_is_allowed_and_a_move_from_it_is_refused()
        Dim src As New FaultProvider(Dir(), Caps(delete:=False))
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(100))
        Assert.Equal(TransferResult.Completed, TransferExecutor.Execute(Plan(TransferKind.Copy, src, "/s/a.jpg", dst, "/in/a.jpg")).Result)

        Dim moved = TransferExecutor.Execute(Plan(TransferKind.Move, src, "/s/a.jpg", dst, "/in/b.jpg"))
        Assert.Equal(TransferResult.Refused, moved.Result)
        Assert.Equal(TransferReason.NotDeletable, moved.Reason)
        Assert.False(dst.Exists("/in/b.jpg"))
    End Sub

    <Fact>
    Public Sub A_destination_that_cannot_be_written_is_refused_before_anything_is_read()
        Dim src As New FaultProvider(Dir(), Caps())
        Dim dst As New FaultProvider(Dir(), Caps(write:=False))
        src.Put("/s/a.jpg", Data(10))
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Copy, src, "/s/a.jpg", dst, "/in/a.jpg"))
        Assert.Equal(TransferReason.NotWritable, outcome.Reason)
        Assert.Empty(dst.Uploads)
    End Sub

    ' ------------------------------------------------------------------- move ----

    <Fact>
    Public Sub A_move_deletes_the_source_only_after_the_copy_is_proven_when_the_source_can_be_deleted_safely()
        Dim src As New FaultProvider(Dir(), Caps(stable:=True))
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(777))
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Move, src, "/s/a.jpg", dst, "/in/a.jpg"))
        Assert.Equal(TransferResult.Completed, outcome.Result)
        Assert.False(src.Exists("/s/a.jpg"))
        Assert.Equal(Data(777), dst.Bytes("/in/a.jpg"))
    End Sub

    <Fact>
    Public Sub A_move_out_of_a_source_without_a_stable_delete_is_a_copy_and_says_so()
        Dim src As New FaultProvider(Dir(), Caps(stable:=False)) ' arbitrary SFTP: no atomic-delete guarantee is invented
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(777))
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Move, src, "/s/a.jpg", dst, "/in/a.jpg"))
        Assert.Equal(TransferResult.CopyOnly, outcome.Result)
        Assert.Equal(TransferReason.SourceDeleteUnsupported, outcome.Reason)
        Assert.Equal(TransferState.Published, outcome.LastState)
        Assert.True(src.Exists("/s/a.jpg"))   ' both copies exist and are identified
        Assert.True(dst.Exists("/in/a.jpg"))
        Assert.Empty(src.Deletes)
    End Sub

    <Fact>
    Public Sub A_source_that_changed_after_it_was_captured_is_never_deleted()
        Dim src As New FaultProvider(Dir(), Caps(stable:=True))
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(300))
        dst.AfterUpload = Sub(p)
                              src.Put("/s/a.jpg", Data(300, seed:=9)) ' someone edits the original mid-transfer
                              Stamp(src, "/s/a.jpg", 30)
                          End Sub
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Move, src, "/s/a.jpg", dst, "/in/a.jpg"))
        Assert.Equal(TransferResult.CopyOnly, outcome.Result)
        Assert.Equal(TransferReason.SourceChanged, outcome.Reason)
        Assert.True(src.Exists("/s/a.jpg"))
        Assert.Empty(src.Deletes)
    End Sub

    <Fact>
    Public Sub A_move_inside_one_resource_is_a_server_side_rename_and_a_copy_inside_one_is_not()
        Dim p As New FaultProvider(Dir(), Caps(rename:=True, stable:=True))
        p.Put("/a/x.jpg", Data(64))
        Dim moved = TransferExecutor.Execute(Plan(TransferKind.Move, p, "/a/x.jpg", p, "/b/x.jpg", sameResource:=True))
        Assert.Equal(TransferResult.Completed, moved.Result)
        Assert.Equal(0, p.Inner.DownloadCalls) ' no bytes moved for a rename
        Assert.False(p.Exists("/a/x.jpg"))

        Dim copied = TransferExecutor.Execute(Plan(TransferKind.Copy, p, "/b/x.jpg", p, "/c/x.jpg", sameResource:=True))
        Assert.Equal(TransferResult.Completed, copied.Result)
        Assert.True(p.Exists("/b/x.jpg"))      ' a copy never takes the source away
        Assert.True(p.Inner.DownloadCalls >= 1) ' and is staged, not renamed
    End Sub

    <Fact>
    Public Sub A_rename_inside_a_resource_that_threw_after_doing_it_is_reconciled_not_replayed()
        Dim p As New FaultProvider(Dir(), Caps(rename:=True, stable:=True))
        p.Put("/a/x.jpg", Data(64))
        p.RenameBehavior = Function(a, b) 2
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Move, p, "/a/x.jpg", p, "/b/x.jpg", sameResource:=True))
        Assert.Equal(TransferResult.Completed, outcome.Result)
        Assert.Single(p.Renames) ' asked once; the lost answer was settled by looking, not by asking again
    End Sub

    ' ---------------------------------------------------------------- conflicts ----

    <Fact>
    Public Sub An_existing_destination_is_refused_by_default_and_nothing_is_touched()
        Dim src As New FaultProvider(Dir(), Caps())
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(10))
        dst.Put("/in/a.jpg", Data(10, seed:=77))
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Copy, src, "/s/a.jpg", dst, "/in/a.jpg"))
        Assert.Equal(TransferResult.Refused, outcome.Result)
        Assert.Equal(TransferReason.DestinationExists, outcome.Reason)
        Assert.Equal(Data(10, seed:=77), dst.Bytes("/in/a.jpg"))
        Assert.Empty(dst.Uploads)
    End Sub

    <Theory>
    <InlineData(2)>
    <InlineData(3)>
    Public Sub A_destination_that_cannot_be_probed_is_not_taken_for_free(outcomeCode As Integer)
        Dim src As New FaultProvider(Dir(), Caps())
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(10))
        dst.StatFault = Function(p) If(p = "/in/a.jpg", If(outcomeCode = 2, StatResult.Denied(), StatResult.Failed(New ResourceFailure(ResourceFailureKind.Timeout))), Nothing)
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Copy, src, "/s/a.jpg", dst, "/in/a.jpg", ConflictPolicy.Replace))
        Assert.Equal(TransferResult.Failed, outcome.Result)
        Assert.Equal(TransferReason.DestinationUnknown, outcome.Reason)
        Assert.Empty(dst.Uploads)
    End Sub

    <Fact>
    Public Sub A_missing_source_fails_by_name_and_an_unreadable_one_does_not_claim_to_be_missing()
        Dim src As New FaultProvider(Dir(), Caps())
        Dim dst As New FaultProvider(Dir(), Caps())
        Assert.Equal(TransferReason.SourceMissing, TransferExecutor.Execute(Plan(TransferKind.Copy, src, "/s/none.jpg", dst, "/in/a.jpg")).Reason)
        src.StatFault = Function(p) StatResult.Denied()
        Assert.Equal(TransferReason.SourceUnreadable, TransferExecutor.Execute(Plan(TransferKind.Copy, src, "/s/none.jpg", dst, "/in/a.jpg")).Reason)
    End Sub

    <Fact>
    Public Sub A_file_is_never_replaced_with_itself_even_under_another_spelling_of_its_name()
        Dim p As New FaultProvider(Dir(), Caps(rename:=True, stable:=True))
        p.Put("/a/photo.jpg", Data(20))
        Assert.Equal(TransferReason.SamePath, TransferExecutor.Execute(Plan(TransferKind.Move, p, "/a/photo.jpg", p, "/a/photo.jpg", ConflictPolicy.Replace, True)).Reason)
        Dim differentCase = TransferExecutor.Execute(Plan(TransferKind.Move, p, "/a/photo.jpg", p, "/a/PHOTO.JPG", ConflictPolicy.Replace, True))
        Assert.Equal(TransferResult.Refused, differentCase.Result) ' the server resolves PHOTO.JPG to the very same file
        Assert.Equal(Data(20), p.Bytes("/a/photo.jpg"))
    End Sub

    <Fact>
    Public Sub Replace_moves_the_old_file_to_an_owned_backup_then_publishes_and_keeps_the_backup_for_undo()
        Dim src As New FaultProvider(Dir(), Caps())
        Dim dst As New FaultProvider(Dir(), Caps(atomic:=False))
        src.Put("/s/a.jpg", Data(50))
        dst.Put("/in/a.jpg", Data(50, seed:=200))
        Dim plan As TransferPlan = TransferExecutorTests.Plan(TransferKind.Copy, src, "/s/a.jpg", dst, "/in/a.jpg", ConflictPolicy.Replace)
        Dim outcome = TransferExecutor.Execute(plan)

        Assert.Equal(TransferResult.Completed, outcome.Result)
        Assert.True(outcome.ReplacedExisting)
        Assert.Equal(plan.BackupPath, outcome.BackupPath)
        Assert.Equal(Data(50), dst.Bytes("/in/a.jpg"))
        Assert.Equal(Data(50, seed:=200), dst.Bytes(outcome.BackupPath))
        Assert.Empty(dst.Deletes) ' the old file was moved aside, never deleted
        Assert.NotNull(TransferUndo.Record(outcome))

        Assert.True(TransferExecutor.ReleaseBackup(outcome))
        Assert.False(dst.Exists(outcome.BackupPath))
        Assert.True(dst.Exists("/in/a.jpg"))
    End Sub

    <Fact>
    Public Sub An_atomic_overwrite_keeps_no_backup_and_so_offers_no_undo()
        Dim src As New FaultProvider(Dir(), Caps())
        Dim dst As New FaultProvider(Dir(), Caps(atomic:=True))
        src.Put("/s/a.jpg", Data(50))
        dst.Put("/in/a.jpg", Data(50, seed:=200))
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Copy, src, "/s/a.jpg", dst, "/in/a.jpg", ConflictPolicy.Replace))
        Assert.Equal(TransferResult.Completed, outcome.Result)
        Assert.Equal("", outcome.BackupPath)
        Assert.Equal(1, dst.Renames.Count) ' one rename: stage over target, nothing in between
        Assert.Null(TransferUndo.Record(outcome)) ' recovery of an overwritten file needs its backup
    End Sub

    <Fact>
    Public Sub When_publication_fails_after_the_old_file_moved_aside_it_is_put_back()
        Dim src As New FaultProvider(Dir(), Caps())
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(50))
        dst.Put("/in/a.jpg", Data(50, seed:=200))
        dst.RenameBehavior = Function(a, b) If(b = "/in/a.jpg" AndAlso a.EndsWith(".fms-stage"), 1, 0) ' stage -> target fails; target -> backup and the restore work
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Copy, src, "/s/a.jpg", dst, "/in/a.jpg", ConflictPolicy.Replace))
        Assert.Equal(TransferResult.Failed, outcome.Result)
        Assert.Equal(TransferReason.PublishFailed, outcome.Reason)
        Assert.Equal(Data(50, seed:=200), dst.Bytes("/in/a.jpg")) ' the user's file is back where it was
        Assert.Empty(dst.Leftovers("/in"))                          ' no stage, no backup left behind
        Assert.Null(TransferUndo.Record(outcome))
    End Sub

    <Fact>
    Public Sub When_the_old_file_was_moved_aside_but_the_answer_was_lost_it_is_still_put_back()
        Dim src As New FaultProvider(Dir(), Caps())
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(50))
        dst.Put("/in/a.jpg", Data(50, seed:=200))
        dst.RenameBehavior = Function(a, b) If(a = "/in/a.jpg", 2, 0) ' target -> backup happened, then the link died
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Copy, src, "/s/a.jpg", dst, "/in/a.jpg", ConflictPolicy.Replace))
        Assert.Equal(TransferResult.Failed, outcome.Result)
        Assert.Equal(Data(50, seed:=200), dst.Bytes("/in/a.jpg"))
        Assert.Empty(dst.Leftovers("/in"))
    End Sub

    ' ------------------------------------------------------------- verification ----

    <Fact>
    Public Sub A_truncated_upload_is_caught_and_the_stage_is_removed()
        Dim src As New FaultProvider(Dir(), Caps(stable:=True))
        Dim dst As New FaultProvider(Dir(), Caps()) With {.UploadMutator = Function(b) b.Take(b.Length \ 2).ToArray()}
        src.Put("/s/a.jpg", Data(1000))
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Move, src, "/s/a.jpg", dst, "/in/a.jpg"))
        Assert.Equal(TransferResult.Failed, outcome.Result)
        Assert.Equal(TransferReason.VerificationFailed, outcome.Reason)
        Assert.False(dst.Exists("/in/a.jpg"))
        Assert.Empty(dst.Leftovers("/in"))
        Assert.True(src.Exists("/s/a.jpg"))
    End Sub

    <Fact>
    Public Sub A_corrupted_upload_of_the_right_size_is_caught_because_size_alone_proves_nothing()
        Dim src As New FaultProvider(Dir(), Caps(stable:=True))
        Dim dst As New FaultProvider(Dir(), Caps()) With {.UploadMutator = Function(b)
                                                                                Dim copy As Byte() = CType(b.Clone(), Byte())
                                                                                copy(copy.Length \ 2) = CByte(copy(copy.Length \ 2) Xor &HFF)
                                                                                Return copy
                                                                            End Function}
        src.Put("/s/a.jpg", Data(1000))
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Move, src, "/s/a.jpg", dst, "/in/a.jpg"))
        Assert.Equal(TransferReason.VerificationFailed, outcome.Reason)
        Assert.False(dst.Exists("/in/a.jpg"))
        Assert.True(src.Exists("/s/a.jpg"))
        Assert.Empty(src.Deletes)
    End Sub

    ' --------------------------------------------------------- uncertain outcomes ----

    <Fact>
    Public Sub A_publish_rename_whose_answer_was_lost_is_reconciled_by_looking()
        Dim src As New FaultProvider(Dir(), Caps(stable:=True))
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(90))
        dst.RenameBehavior = Function(a, b) 2
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Move, src, "/s/a.jpg", dst, "/in/a.jpg"))
        Assert.Equal(TransferResult.Completed, outcome.Result)
        Assert.Single(dst.Renames)
        Assert.Equal(Data(90), dst.Bytes("/in/a.jpg"))
    End Sub

    <Fact>
    Public Sub When_the_server_cannot_even_be_asked_after_a_failed_rename_the_outcome_is_Indeterminate_and_never_undoable()
        Dim src As New FaultProvider(Dir(), Caps())
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(90))
        dst.RenameBehavior = Function(a, b) 1
        ' From the moment the rename is attempted the link is dead for every question.
        dst.StatFault = Function(p) If(dst.Renames.Count > 0, StatResult.Failed(New ResourceFailure(ResourceFailureKind.Transport)), Nothing)
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Copy, src, "/s/a.jpg", dst, "/in/a.jpg"))
        Assert.Equal(TransferResult.Indeterminate, outcome.Result)
        Assert.Equal(TransferReason.Uncertain, outcome.Reason)
        Assert.Null(TransferUndo.Record(outcome))
    End Sub

    <Fact>
    Public Sub A_source_delete_that_threw_is_settled_by_a_stat_gone_means_done_still_there_means_copy_only()
        Dim src As New FaultProvider(Dir(), Caps(stable:=True))
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(40))
        src.DeleteBehavior = Function(p) 2 ' deleted, answer lost
        Assert.Equal(TransferResult.Completed, TransferExecutor.Execute(Plan(TransferKind.Move, src, "/s/a.jpg", dst, "/in/a.jpg")).Result)

        Dim src2 As New FaultProvider(Dir(), Caps(stable:=True))
        src2.Put("/s/b.jpg", Data(40))
        src2.DeleteBehavior = Function(p) 1 ' not deleted, error
        Dim stuck = TransferExecutor.Execute(Plan(TransferKind.Move, src2, "/s/b.jpg", dst, "/in/b.jpg"))
        Assert.Equal(TransferResult.CopyOnly, stuck.Result)
        Assert.Equal(TransferReason.SourceDeleteFailed, stuck.Reason)
        Assert.True(src2.Exists("/s/b.jpg"))
        Assert.True(dst.Exists("/in/b.jpg")) ' a completed copy and a failed move, both identified

        Dim src3 As New FaultProvider(Dir(), Caps(stable:=True))
        src3.Put("/s/c.jpg", Data(40))
        src3.DeleteBehavior = Function(p) 1
        src3.StatFault = Function(p) If(src3.Deletes.Count > 0, StatResult.Failed(New ResourceFailure(ResourceFailureKind.Transport)), Nothing)
        Assert.Equal(TransferResult.Indeterminate, TransferExecutor.Execute(Plan(TransferKind.Move, src3, "/s/c.jpg", dst, "/in/c.jpg")).Result)
    End Sub

    ' ------------------------------------------------ fault at every transition ----

    <Fact>
    Public Sub A_source_read_that_dies_costs_the_destination_nothing()
        Dim src As New FaultProvider(Dir(), Caps(stable:=True))
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(120))
        src.DownloadBehavior = Function(p) If(p = "/s/a.jpg", 1, 0)
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Move, src, "/s/a.jpg", dst, "/in/a.jpg"))
        Assert.Equal(TransferResult.Failed, outcome.Result)
        Assert.Equal(TransferReason.StageFailed, outcome.Reason)
        Assert.Equal(TransferState.SourceCaptured, outcome.LastState)
        Assert.True(src.Exists("/s/a.jpg"))
        Assert.False(dst.Exists("/in/a.jpg"))
        Assert.Empty(dst.Uploads)
    End Sub

    <Fact>
    Public Sub An_upload_that_dies_before_the_stage_is_written_leaves_both_sides_alone()
        Dim src As New FaultProvider(Dir(), Caps(stable:=True))
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(120))
        dst.UploadBehavior = Function(p) If(p.EndsWith(".fms-stage"), 1, 0)
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Move, src, "/s/a.jpg", dst, "/in/a.jpg"))
        Assert.Equal(TransferResult.Failed, outcome.Result)
        Assert.Equal(TransferState.SourceCaptured, outcome.LastState)
        Assert.True(src.Exists("/s/a.jpg"))
        Assert.False(dst.Exists("/in/a.jpg"))
        Assert.Empty(dst.Leftovers("/in"))
    End Sub

    <Fact>
    Public Sub A_verification_read_back_that_dies_takes_its_own_stage_and_nothing_else()
        Dim src As New FaultProvider(Dir(), Caps(stable:=True))
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(120))
        dst.DownloadBehavior = Function(p) If(p.EndsWith(".fms-stage"), 1, 0)
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Move, src, "/s/a.jpg", dst, "/in/a.jpg"))
        Assert.Equal(TransferResult.Failed, outcome.Result)
        Assert.Equal(TransferState.StageWritten, outcome.LastState)
        Assert.True(src.Exists("/s/a.jpg"))
        Assert.False(dst.Exists("/in/a.jpg"))
        Assert.Empty(dst.Leftovers("/in")) ' the stage is this operation's own GUID-named file
    End Sub

    <Fact>
    Public Sub A_server_that_won_not_answer_after_the_publish_rename_reports_Indeterminate()
        Dim src As New FaultProvider(Dir(), Caps(stable:=True))
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(90))
        dst.StatFault = Function(p) If(dst.Renames.Count > 0, StatResult.Failed(New ResourceFailure(ResourceFailureKind.Transport)), Nothing)
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Move, src, "/s/a.jpg", dst, "/in/a.jpg"))
        Assert.Equal(TransferResult.Indeterminate, outcome.Result)
        Assert.True(dst.Exists("/in/a.jpg")) ' the rename DID land - reconciliation, not replay, says so
        Assert.True(src.Exists("/s/a.jpg"))  ' and a move that cannot be proven never deletes
        Assert.Null(TransferUndo.Record(outcome))
    End Sub

    <Fact>
    Public Sub A_replace_whose_publish_answer_was_lost_is_reconciled_and_keeps_the_backup_for_undo()
        Dim src As New FaultProvider(Dir(), Caps(stable:=True))
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(50))
        dst.Put("/in/a.jpg", Data(50, seed:=200))
        dst.RenameBehavior = Function(a, b) If(a.EndsWith(".fms-stage"), 2, 0) ' published; the answer was lost
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Copy, src, "/s/a.jpg", dst, "/in/a.jpg", ConflictPolicy.Replace))
        Assert.Equal(TransferResult.Completed, outcome.Result)
        Assert.True(outcome.ReplacedExisting)
        Assert.Equal(Data(50), dst.Bytes("/in/a.jpg"))                   ' the new bytes are live
        Assert.Equal(Data(50, seed:=200), dst.Bytes(outcome.BackupPath)) ' the old ones are kept for undo
        Assert.NotNull(TransferUndo.Record(outcome))
    End Sub

    ' ------------------------------------------------------------------- cancel ----

    <Fact>
    Public Sub Cancelling_during_the_upload_removes_the_stage_and_leaves_the_source()
        Dim src As New FaultProvider(Dir(), Caps(stable:=True))
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(500))
        Using cts As New CancellationTokenSource()
            dst.AfterUpload = Sub(p) cts.Cancel()
            Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Move, src, "/s/a.jpg", dst, "/in/a.jpg"), Nothing, cts.Token)
            Assert.Equal(TransferResult.Cancelled, outcome.Result)
            Assert.False(dst.Exists("/in/a.jpg"))
            Assert.Empty(dst.Leftovers("/in"))
            Assert.True(src.Exists("/s/a.jpg"))
            Assert.Null(TransferUndo.Record(outcome))
        End Using
    End Sub

    <Fact>
    Public Sub A_late_cancel_after_publication_keeps_the_source_and_reports_the_published_copy()
        Dim src As New FaultProvider(Dir(), Caps(stable:=True))
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(500))
        Using cts As New CancellationTokenSource()
            dst.RenameBehavior = Function(a, b)
                                     cts.Cancel() ' the user cancels just as the stage is published
                                     Return 0
                                 End Function
            Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Move, src, "/s/a.jpg", dst, "/in/a.jpg"), Nothing, cts.Token)
            Assert.Equal(TransferResult.Cancelled, outcome.Result)
            Assert.True(outcome.DestinationCreated)
            Assert.True(dst.Exists("/in/a.jpg"))
            Assert.True(src.Exists("/s/a.jpg"))
            Assert.Empty(src.Deletes)
        End Using
    End Sub

    ' --------------------------------------------------------------------- undo ----

    <Fact>
    Public Sub Undoing_a_move_puts_the_object_back_only_if_it_is_unchanged_and_the_slot_is_free()
        Dim src As New FaultProvider(Dir(), Caps(stable:=True))
        Dim dst As New FaultProvider(Dir(), Caps(stable:=True))
        src.Put("/s/a.jpg", Data(120))
        Dim done = TransferExecutor.Execute(Plan(TransferKind.Move, src, "/s/a.jpg", dst, "/in/a.jpg"))
        Dim record = TransferUndo.Record(done)
        Assert.NotNull(record)
        Assert.True(record.IsMoveInverse)

        Dim undone = TransferUndo.Undo(record, Dir())
        Assert.Equal(TransferResult.Completed, undone.Result)
        Assert.Equal(Data(120), src.Bytes("/s/a.jpg"))
        Assert.False(dst.Exists("/in/a.jpg"))
    End Sub

    <Fact>
    Public Sub An_undo_is_refused_when_the_moved_object_changed_or_the_original_slot_is_taken()
        Dim src As New FaultProvider(Dir(), Caps(stable:=True))
        Dim dst As New FaultProvider(Dir(), Caps(stable:=True))
        src.Put("/s/a.jpg", Data(120))
        Dim done = TransferExecutor.Execute(Plan(TransferKind.Move, src, "/s/a.jpg", dst, "/in/a.jpg"))
        Dim record = TransferUndo.Record(done)

        src.Put("/s/a.jpg", Data(5)) ' somebody put something else in the original slot
        Assert.Equal(TransferReason.SourceSlotOccupied, TransferUndo.Undo(record, Dir()).Reason)
        src.Inner.DeleteFile("/s/a.jpg")

        dst.Put("/in/a.jpg", Data(120, seed:=1)) ' and then the moved file itself was edited
        Stamp(dst, "/in/a.jpg", 60)
        Assert.Equal(TransferReason.DestinationChanged, TransferUndo.Undo(record, Dir()).Reason)
        Assert.True(dst.Exists("/in/a.jpg")) ' nothing was touched
    End Sub

    <Fact>
    Public Sub Undoing_a_copy_removes_only_the_unchanged_object_it_made_and_restores_what_it_replaced()
        Dim src As New FaultProvider(Dir(), Caps())
        Dim dst As New FaultProvider(Dir(), Caps(stable:=True))
        src.Put("/s/a.jpg", Data(60))
        dst.Put("/in/a.jpg", Data(60, seed:=200))
        Dim done = TransferExecutor.Execute(Plan(TransferKind.Copy, src, "/s/a.jpg", dst, "/in/a.jpg", ConflictPolicy.Replace))
        Dim record = TransferUndo.Record(done)
        Assert.NotNull(record)
        Assert.False(record.IsMoveInverse)

        Dim undone = TransferUndo.Undo(record, Dir())
        Assert.Equal(TransferResult.Completed, undone.Result)
        Assert.Equal(Data(60, seed:=200), dst.Bytes("/in/a.jpg")) ' the overwritten file is back
        Assert.True(src.Exists("/s/a.jpg"))
        Assert.Empty(dst.Leftovers("/in"))
    End Sub

    <Fact>
    Public Sub A_copy_cannot_be_undone_on_a_server_that_cannot_condition_the_delete()
        Dim src As New FaultProvider(Dir(), Caps())
        Dim dst As New FaultProvider(Dir(), Caps(stable:=False))
        src.Put("/s/a.jpg", Data(60))
        Dim record = TransferUndo.Record(TransferExecutor.Execute(Plan(TransferKind.Copy, src, "/s/a.jpg", dst, "/in/a.jpg")))
        Assert.NotNull(record)
        Dim refused = TransferUndo.Undo(record, Dir())
        Assert.Equal(TransferResult.Refused, refused.Result)
        Assert.True(dst.Exists("/in/a.jpg"))
    End Sub

    <Fact>
    Public Sub Only_durable_outcomes_enter_the_undo_log()
        Dim src As New FaultProvider(Dir(), Caps())
        Dim dst As New FaultProvider(Dir(), Caps())
        Assert.Null(TransferUndo.Record(TransferExecutor.Execute(Plan(TransferKind.Copy, src, "/none", dst, "/in/a"))))   ' failed
        src.Put("/s/a.jpg", Data(10))
        dst.Put("/in/a.jpg", Data(10, 5))
        Assert.Null(TransferUndo.Record(TransferExecutor.Execute(Plan(TransferKind.Copy, src, "/s/a.jpg", dst, "/in/a.jpg")))) ' refused
        Assert.Null(TransferUndo.Record(Nothing))
        ' A move that fell back to copy-only is durable: its inverse is "remove the copy".
        Dim copyOnly = TransferExecutor.Execute(Plan(TransferKind.Move, src, "/s/a.jpg", dst, "/in/b.jpg"))
        Assert.Equal(TransferResult.CopyOnly, copyOnly.Result)
        Assert.NotNull(TransferUndo.Record(copyOnly))
    End Sub

    ' --------------------------------------------------------------------- batch ----

    <Fact>
    Public Sub A_batch_reports_each_item_and_stops_starting_new_work_after_a_cancel()
        Dim src As New FaultProvider(Dir(), Caps())
        Dim dst As New FaultProvider(Dir(), Caps())
        For Each n As String In {"a", "b", "c"}
            src.Put("/s/" & n & ".jpg", Data(100))
        Next
        Dim plans As List(Of TransferPlan) = {"a", "b", "c"}.Select(Function(n) Plan(TransferKind.Copy, src, "/s/" & n & ".jpg", dst, "/in/" & n & ".jpg")).ToList()
        Dim reported As New List(Of Long)()
        Using cts As New CancellationTokenSource()
            dst.AfterUpload = Sub(p) If p.Contains("b.jpg") Then cts.Cancel()
            Dim results = TransferBatch.Run(plans, New SyncProgress(Of Long)(Sub(v) reported.Add(v)), cts.Token)
            Assert.Equal(2, results.Count)             ' "c" never started
            Assert.Equal(TransferResult.Completed, results(0).Result)
            Assert.Equal(TransferResult.Cancelled, results(1).Result)
            Assert.False(dst.Exists("/in/c.jpg"))
            Assert.Empty(dst.Leftovers("/in"))
        End Using
    End Sub

    <Fact>
    Public Sub Releasing_a_backup_removes_only_the_operations_own_backup_name()
        Dim src As New FaultProvider(Dir(), Caps())
        Dim dst As New FaultProvider(Dir(), Caps())
        src.Put("/s/a.jpg", Data(10))
        dst.Put("/in/a.jpg", Data(10, 5))
        dst.Put("/in/.a.jpg.someone-elses.fms-backup", Data(3))
        Dim outcome = TransferExecutor.Execute(Plan(TransferKind.Copy, src, "/s/a.jpg", dst, "/in/a.jpg", ConflictPolicy.Replace))
        Assert.True(TransferExecutor.ReleaseBackup(outcome))
        Assert.True(dst.Exists("/in/.a.jpg.someone-elses.fms-backup"))
        Assert.True(TransferExecutor.ReleaseBackup(Nothing)) ' nothing to release is not a failure
    End Sub

End Class
#End If
