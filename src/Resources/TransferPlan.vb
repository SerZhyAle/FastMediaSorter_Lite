#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Security.Cryptography
Imports System.Threading

''' <summary>Copy leaves the source alone; Move removes it only once the copy is proven. A move
''' inside one resource may be a rename; a copy inside one resource never is (100 §14.2).</summary>
Friend Enum TransferKind
    Copy = 0
    Move = 1
End Enum

Friend Enum ConflictPolicy
    ''' <summary>An existing destination is a refusal; nothing is touched.</summary>
    Fail = 0
    ''' <summary>The existing destination is replaced, keeping an owned backup until undo expires.</summary>
    Replace = 1
End Enum

''' <summary>The last DURABLE step a transfer reached (100 §15.6 item 1). A failure retains the
''' last state, so a caller - and a reconciliation - knows exactly what exists on each side.</summary>
Friend Enum TransferState
    Planned = 0
    SourceCaptured = 1
    StageWritten = 2
    ContentVerified = 3
    Published = 4
    SourceDeleteConfirmed = 5
    Completed = 6
End Enum

Friend Enum TransferResult
    ''' <summary>Everything the plan asked for is done (a copy is published; a move also removed the source).</summary>
    Completed = 0
    ''' <summary>The copy is published and verified, the SOURCE is still there: a move that did not
    ''' complete, reported as exactly that, with both copies identified.</summary>
    CopyOnly = 1
    ''' <summary>Refused before anything was written.</summary>
    Refused = 2
    Failed = 3
    Cancelled = 4
    ''' <summary>A write or delete was in flight when the link died and reconciliation could not tell
    ''' which side won. Never replayed blindly; never entered in the undo log.</summary>
    Indeterminate = 5
End Enum

Friend Enum TransferReason
    None = 0
    SourceMissing = 1
    SourceUnreadable = 2
    DestinationExists = 3
    ''' <summary>The destination could not be probed (permission or link): not assumed free.</summary>
    DestinationUnknown = 4
    NotWritable = 5
    NotDeletable = 6
    SamePath = 7
    StageFailed = 8
    VerificationFailed = 9
    PublishFailed = 10
    ''' <summary>The source is not the version that was copied.</summary>
    SourceChanged = 11
    SourceDeleteFailed = 12
    ''' <summary>The source provider offers neither a stable nor a checked source delete (a read-only or
    ''' PIN-locked root), or an undo of a copy needs a stable delete it lacks: copy only.</summary>
    SourceDeleteUnsupported = 13
    Cancelled = 14
    Uncertain = 15
    ''' <summary>Undo: the object this operation made has since changed or gone.</summary>
    DestinationChanged = 16
    ''' <summary>Undo: the original slot is occupied.</summary>
    SourceSlotOccupied = 17
    NothingToUndo = 18
End Enum

''' <summary>One side of a transfer: which provider, which path in ITS vocabulary, and the
''' identity of the resource behind it (never a name or an address alone).</summary>
Friend NotInheritable Class TransferEndpoint
    Friend Sub New(provider As IFileSystemProvider, path As String, resourceId As String)
        If provider Is Nothing Then Throw New ArgumentNullException(NameOf(provider))
        Me.Provider = provider
        Me.Path = If(path, "")
        Me.ResourceId = If(resourceId, "")
    End Sub
    Friend ReadOnly Property Provider As IFileSystemProvider
    Friend ReadOnly Property Path As String
    Friend ReadOnly Property ResourceId As String

    Friend Function IsSameResourceAs(other As TransferEndpoint) As Boolean
        If other Is Nothing Then Return False
        Return ReferenceEquals(Provider, other.Provider) OrElse
               (ResourceId.Length > 0 AndAlso String.Equals(ResourceId, other.ResourceId, StringComparison.Ordinal))
    End Function
End Class

''' <summary>An operation, fully decided before the first byte moves. Stage and backup names carry
''' the operation GUID - never a reusable suffix - so recovery can recognise exactly its own
''' leftovers and can never delete a file it did not create.</summary>
Friend NotInheritable Class TransferPlan
    Friend Sub New(kind As TransferKind, source As TransferEndpoint, destination As TransferEndpoint,
                   conflict As ConflictPolicy, scratchDir As String)
        If source Is Nothing Then Throw New ArgumentNullException(NameOf(source))
        If destination Is Nothing Then Throw New ArgumentNullException(NameOf(destination))
        Me.Kind = kind
        Me.Source = source
        Me.Destination = destination
        Me.Conflict = conflict
        Me.ScratchDir = If(scratchDir, "")
        Me.OperationId = Guid.NewGuid().ToString("N")
    End Sub

    Friend ReadOnly Property OperationId As String
    Friend ReadOnly Property Kind As TransferKind
    Friend ReadOnly Property Source As TransferEndpoint
    Friend ReadOnly Property Destination As TransferEndpoint
    Friend ReadOnly Property Conflict As ConflictPolicy
    ''' <summary>A local folder for the bounded read-back copies. Deleted by the executor.</summary>
    Friend ReadOnly Property ScratchDir As String

    Private Function Owned(suffix As String) As String
        Dim name As String = Destination.Path.Substring(Math.Max(Destination.Path.LastIndexOfAny({"/"c, "\"c}) + 1, 0))
        Return ResourceTransfer.Combine(ResourceTransfer.ParentOf(Destination.Path), "." & name & "." & OperationId & suffix)
    End Function

    ''' <summary>Where the new bytes are written before they are published.</summary>
    Friend ReadOnly Property StagePath As String
        Get
            Return Owned(".fms-stage")
        End Get
    End Property

    ''' <summary>Where a replaced destination is kept until its undo window ends.</summary>
    Friend ReadOnly Property BackupPath As String
        Get
            Return Owned(".fms-backup")
        End Get
    End Property

    Friend ReadOnly Property IsSameResource As Boolean
        Get
            Return Source.IsSameResourceAs(Destination)
        End Get
    End Property
End Class

Friend NotInheritable Class TransferOutcome
    Friend Sub New(plan As TransferPlan, result As TransferResult, reason As TransferReason, lastState As TransferState,
                   Optional sourceVersion As ResourceEntry = Nothing, Optional published As ResourceEntry = Nothing,
                   Optional backupPath As String = "", Optional bytesReported As Long = 0,
                   Optional replacedExisting As Boolean = False)
        Me.Plan = plan
        Me.Result = result
        Me.Reason = reason
        Me.LastState = lastState
        Me.SourceVersion = sourceVersion
        Me.PublishedEntry = published
        Me.BackupPath = If(backupPath, "")
        Me.BytesReported = bytesReported
        Me.ReplacedExisting = replacedExisting
    End Sub

    Friend ReadOnly Property Plan As TransferPlan
    Friend ReadOnly Property Result As TransferResult
    Friend ReadOnly Property Reason As TransferReason
    Friend ReadOnly Property LastState As TransferState
    ''' <summary>The source as it was when it was captured.</summary>
    Friend ReadOnly Property SourceVersion As ResourceEntry
    ''' <summary>The destination object as the server reported it right after publication.</summary>
    Friend ReadOnly Property PublishedEntry As ResourceEntry
    ''' <summary>A replaced destination kept under its owned backup name; "" when there is none.</summary>
    Friend ReadOnly Property BackupPath As String
    ''' <summary>Verification bytes included: they consume transfer capacity too.</summary>
    Friend ReadOnly Property BytesReported As Long
    ''' <summary>The destination already held a file that this operation replaced.</summary>
    Friend ReadOnly Property ReplacedExisting As Boolean

    Friend ReadOnly Property DestinationCreated As Boolean
        Get
            Return LastState >= TransferState.Published
        End Get
    End Property

    Friend ReadOnly Property Succeeded As Boolean
        Get
            Return Result = TransferResult.Completed
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return Result.ToString() & "/" & Reason.ToString() & "@" & LastState.ToString()
    End Function
End Class

''' <summary>
''' Runs a <see cref="TransferPlan"/>: capture -> stage -> verify by content -> publish -> (move
''' only) confirmed source delete. Rules that are the point of this class:
''' <list type="bullet">
''' <item>Size equality is not verification: the staged copy is read back and its SHA-256 compared
''' with the source's. The interface has no checksum seam, so the read-back is a bounded local
''' scratch file; the bytes count in the progress.</item>
''' <item>A path is "free" only on a definite Missing. A permission error or a dead link is never
''' read as absence.</item>
''' <item>Nothing is deleted before the new content is published and proven, the old destination
''' is renamed to an owned backup (never deleted ahead of an uncertain rename), and an uncertain
''' step is reconciled by stat - never replayed.</item>
''' <item>A source on a provider with neither <see cref="ProviderCapabilities.SupportsStableSourceDelete"/>
''' nor <see cref="ProviderCapabilities.SupportsCheckedSourceDelete"/> is never deleted: the outcome
''' is CopyOnly. Ordinary SFTP has the checked form only (re-stat, remove, confirm absence); no
''' atomic-delete guarantee is invented for it (ticket 221).</item>
''' </list>
''' </summary>
Friend Module TransferExecutor

    Private NotInheritable Class Cumulative
        Implements IProgress(Of Long)
        Private ReadOnly target As IProgress(Of Long)
        Private done As Long
        Private phaseBase As Long
        Friend Sub New(target As IProgress(Of Long))
            Me.target = target
        End Sub
        Friend ReadOnly Property Total As Long
            Get
                Return done
            End Get
        End Property
        ''' <summary>The next phase starts counting from here; counts never go backwards.</summary>
        Friend Sub NextPhase()
            phaseBase = done
        End Sub
        Public Sub Report(value As Long) Implements IProgress(Of Long).Report
            done = Math.Max(done, phaseBase + value)
            target?.Report(done)
        End Sub
    End Class

    Private NotInheritable Class Run
        Friend ReadOnly Plan As TransferPlan
        Friend ReadOnly Progress As Cumulative
        Friend State As TransferState = TransferState.Planned
        Friend Source As ResourceEntry
        Friend Published As ResourceEntry
        Friend Backup As String = ""
        Friend StageCreated As Boolean
        Friend Replaced As Boolean
        Friend Sub New(plan As TransferPlan, progress As IProgress(Of Long))
            Me.Plan = plan
            Me.Progress = New Cumulative(progress)
        End Sub
        Friend Function Outcome(result As TransferResult, reason As TransferReason) As TransferOutcome
            Return New TransferOutcome(Plan, result, reason, State, Source, Published, Backup, Progress.Total, Replaced)
        End Function
    End Class

    Friend Function Execute(plan As TransferPlan, Optional progress As IProgress(Of Long) = Nothing,
                            Optional ct As CancellationToken = Nothing) As TransferOutcome
        Dim run As New Run(plan, progress)
        Try
            Return Perform(run, ct)
        Catch ex As OperationCanceledException
            RemoveStage(run)
            Return run.Outcome(TransferResult.Cancelled, TransferReason.Cancelled)
        Catch ex As Exception
            AppFileLogger.WriteLine("Transfer: failed in state " & run.State.ToString() & " - " & ResourceFailure.FromException(ex).ToString())
            RemoveStage(run)
            Return run.Outcome(TransferResult.Failed, If(run.State < TransferState.StageWritten, TransferReason.StageFailed, TransferReason.PublishFailed))
        Finally
            CleanScratch(plan)
        End Try
    End Function

    ' ------------------------------------------------------------------- the steps ----

    Private Function Perform(run As Run, ct As CancellationToken) As TransferOutcome
        Dim plan As TransferPlan = run.Plan
        Dim source As TransferEndpoint = plan.Source
        Dim destination As TransferEndpoint = plan.Destination

        If source.IsSameResourceAs(destination) AndAlso String.Equals(source.Path, destination.Path, StringComparison.OrdinalIgnoreCase) Then
            Return run.Outcome(TransferResult.Refused, TransferReason.SamePath)
        End If
        If Not destination.Provider.Capabilities.CanWrite Then Return run.Outcome(TransferResult.Refused, TransferReason.NotWritable)
        If plan.Kind = TransferKind.Move AndAlso Not source.Provider.Capabilities.CanDelete Then
            Return run.Outcome(TransferResult.Refused, TransferReason.NotDeletable)
        End If
        ct.ThrowIfCancellationRequested()

        ' 1. Capture the source as it is now.
        Dim captured As StatResult = ResourceProviderCalls.Stat(source.Provider, source.Path)
        If captured.Outcome <> StatOutcome.Found Then
            Return run.Outcome(TransferResult.Failed, If(captured.IsDefinitelyMissing, TransferReason.SourceMissing, TransferReason.SourceUnreadable))
        End If
        run.Source = captured.Entry
        run.State = TransferState.SourceCaptured

        ' 2. The destination: only a definite Missing is "free".
        Dim existing As StatResult = ResourceProviderCalls.Stat(destination.Provider, destination.Path)
        Dim replacing As Boolean
        Select Case existing.Outcome
            Case StatOutcome.Missing
                replacing = False
            Case StatOutcome.Found
                If source.IsSameResourceAs(destination) AndAlso String.Equals(existing.Entry.FullPath, run.Source.FullPath, StringComparison.Ordinal) Then
                    Return run.Outcome(TransferResult.Refused, TransferReason.SamePath) ' the same file under another spelling
                End If
                If plan.Conflict <> ConflictPolicy.Replace Then Return run.Outcome(TransferResult.Refused, TransferReason.DestinationExists)
                replacing = True
            Case Else
                Return run.Outcome(TransferResult.Failed, TransferReason.DestinationUnknown)
        End Select
        run.Replaced = replacing
        ct.ThrowIfCancellationRequested()

        ' 3. A move inside one resource is one validated rename, when the server has one.
        If plan.Kind = TransferKind.Move AndAlso plan.IsSameResource AndAlso source.Provider.Capabilities.SupportsServerSideRename Then
            Return RenameWithin(run, replacing, ct)
        End If

        ' 4. Stage, then prove the staged bytes are the source's bytes.
        Dim expectedHash As Byte() = Nothing
        Dim scratch As String = ScratchFile(plan, ".src")
        run.Progress.NextPhase()
        source.Provider.Download(run.Source.FullPath, scratch, run.Progress, ct)
        Dim arrived As Long = New FileInfo(scratch).Length
        If run.Source.Length >= 0 AndAlso arrived <> run.Source.Length Then
            Return run.Outcome(TransferResult.Failed, TransferReason.StageFailed)
        End If
        expectedHash = HashFile(scratch)
        ct.ThrowIfCancellationRequested()

        run.Progress.NextPhase()
        run.StageCreated = True
        destination.Provider.Upload(scratch, plan.StagePath, run.Progress, ct)
        run.State = TransferState.StageWritten
        ct.ThrowIfCancellationRequested()

        Dim readBack As String = ScratchFile(plan, ".back")
        run.Progress.NextPhase()
        destination.Provider.Download(plan.StagePath, readBack, run.Progress, ct)
        If New FileInfo(readBack).Length <> arrived OrElse Not CryptographicOperations.FixedTimeEquals(expectedHash, HashFile(readBack)) Then
            RemoveStage(run)
            Return run.Outcome(TransferResult.Failed, TransferReason.VerificationFailed)
        End If
        run.State = TransferState.ContentVerified
        ct.ThrowIfCancellationRequested()

        ' 5. Publish: a rename of the stage into place, around an owned backup when replacing.
        Dim published As TransferOutcome = Publish(run, replacing, arrived)
        If published IsNot Nothing Then Return published

        ' 6. A move deletes its source only if that is provably safe.
        If plan.Kind = TransferKind.Copy Then
            run.State = TransferState.Completed
            Return run.Outcome(TransferResult.Completed, TransferReason.None)
        End If
        Return DeleteSource(run, ct)
    End Function

    Private Function Publish(run As Run, replacing As Boolean, expectedLength As Long) As TransferOutcome
        Dim plan As TransferPlan = run.Plan
        Dim provider As IFileSystemProvider = plan.Destination.Provider
        Dim dest As String = plan.Destination.Path

        If replacing AndAlso Not provider.Capabilities.SupportsAtomicOverwriteRename Then
            ' Never DeleteFile(existing) ahead of an uncertain rename: the old file moves aside, intact.
            Try
                provider.Rename(dest, plan.BackupPath)
                run.Backup = plan.BackupPath
            Catch ex As Exception
                Return AfterBackupRenameThrew(run, ex)
            End Try
        End If

        Try
            provider.Rename(plan.StagePath, dest)
            run.StageCreated = False
        Catch ex As Exception
            Return Reconcile(run, expectedLength, ex, backupMoved:=run.Backup.Length > 0)
        End Try

        Dim landed As StatResult = ResourceProviderCalls.Stat(provider, dest)
        If landed.Outcome = StatOutcome.Found AndAlso (landed.Entry.Length < 0 OrElse landed.Entry.Length = expectedLength) Then
            run.Published = landed.Entry
            run.State = TransferState.Published
            Return Nothing
        End If
        If landed.Outcome = StatOutcome.Found OrElse landed.Outcome = StatOutcome.Missing Then
            RestoreBackup(run)
            Return run.Outcome(TransferResult.Failed, TransferReason.PublishFailed)
        End If
        Return run.Outcome(TransferResult.Indeterminate, TransferReason.Uncertain)
    End Function

    ''' <summary>After a rename threw: ask the server what is true instead of replaying. The only
    ''' files touched are this operation's own stage and backup.</summary>
    Private Function Reconcile(run As Run, expectedLength As Long, cause As Exception, backupMoved As Boolean) As TransferOutcome
        If TypeOf cause Is OperationCanceledException Then
            RestoreBackup(run)
            RemoveStage(run)
            Return run.Outcome(TransferResult.Cancelled, TransferReason.Cancelled)
        End If
        Dim provider As IFileSystemProvider = run.Plan.Destination.Provider
        Dim dest As StatResult = ResourceProviderCalls.Stat(provider, run.Plan.Destination.Path)
        Dim stage As StatResult = ResourceProviderCalls.Stat(provider, run.Plan.StagePath)

        If dest.Outcome = StatOutcome.Failed OrElse stage.Outcome = StatOutcome.Failed Then
            Return run.Outcome(TransferResult.Indeterminate, TransferReason.Uncertain)
        End If
        If dest.Outcome = StatOutcome.Found AndAlso stage.Outcome = StatOutcome.Missing AndAlso
           (dest.Entry.Length < 0 OrElse dest.Entry.Length = expectedLength) AndAlso
           (Not backupMoved OrElse run.Backup.Length > 0) Then
            ' The rename did happen; only its answer was lost.
            run.StageCreated = False
            run.Published = dest.Entry
            run.State = TransferState.Published
            Return Nothing
        End If
        RestoreBackup(run)
        RemoveStage(run)
        Return run.Outcome(TransferResult.Failed, TransferReason.PublishFailed)
    End Function

    ''' <summary>The old destination was being moved aside and the call threw. Only the server knows
    ''' whether it went: if the file is now under the backup name it is put back; if the server cannot
    ''' be asked, the outcome is Indeterminate - the old file is never deleted to make room.</summary>
    Private Function AfterBackupRenameThrew(run As Run, cause As Exception) As TransferOutcome
        If TypeOf cause Is OperationCanceledException Then
            RemoveStage(run)
            Return run.Outcome(TransferResult.Cancelled, TransferReason.Cancelled)
        End If
        Dim provider As IFileSystemProvider = run.Plan.Destination.Provider
        Dim dest As StatResult = ResourceProviderCalls.Stat(provider, run.Plan.Destination.Path)
        Dim backup As StatResult = ResourceProviderCalls.Stat(provider, run.Plan.BackupPath)
        If dest.Outcome = StatOutcome.Failed OrElse backup.Outcome = StatOutcome.Failed Then
            Return run.Outcome(TransferResult.Indeterminate, TransferReason.Uncertain)
        End If
        If dest.Outcome = StatOutcome.Missing AndAlso backup.Outcome = StatOutcome.Found Then
            run.Backup = run.Plan.BackupPath
            RestoreBackup(run)
        End If
        RemoveStage(run)
        Return run.Outcome(TransferResult.Failed, TransferReason.PublishFailed)
    End Function

    Private Sub RestoreBackup(run As Run)
        If run.Backup.Length = 0 Then Return
        Try
            Dim dest As StatResult = ResourceProviderCalls.Stat(run.Plan.Destination.Provider, run.Plan.Destination.Path)
            If dest.Outcome = StatOutcome.Missing Then
                run.Plan.Destination.Provider.Rename(run.Backup, run.Plan.Destination.Path)
                run.Backup = ""
            End If
        Catch ex As Exception
            AppFileLogger.WriteLine("Transfer: the replaced file could not be put back from " & run.Backup & " - " & ex.GetType().Name)
        End Try
    End Sub

    Private Function RenameWithin(run As Run, replacing As Boolean, ct As CancellationToken) As TransferOutcome
        Dim plan As TransferPlan = run.Plan
        Dim provider As IFileSystemProvider = plan.Source.Provider
        Dim length As Long = run.Source.Length

        If replacing AndAlso Not provider.Capabilities.SupportsAtomicOverwriteRename Then
            Try
                provider.Rename(plan.Destination.Path, plan.BackupPath)
                run.Backup = plan.BackupPath
            Catch ex As Exception
                Return AfterBackupRenameThrew(run, ex)
            End Try
        End If
        Try
            provider.Rename(run.Source.FullPath, plan.Destination.Path)
        Catch ex As Exception
            Dim dest As StatResult = ResourceProviderCalls.Stat(provider, plan.Destination.Path)
            Dim src As StatResult = ResourceProviderCalls.Stat(provider, run.Source.FullPath)
            If dest.Outcome = StatOutcome.Failed OrElse src.Outcome = StatOutcome.Failed Then
                Return run.Outcome(TransferResult.Indeterminate, TransferReason.Uncertain)
            End If
            If Not (dest.Outcome = StatOutcome.Found AndAlso src.Outcome = StatOutcome.Missing) Then
                RestoreBackup(run)
                Return run.Outcome(If(TypeOf ex Is OperationCanceledException, TransferResult.Cancelled, TransferResult.Failed),
                                   If(TypeOf ex Is OperationCanceledException, TransferReason.Cancelled, TransferReason.PublishFailed))
            End If
            ' The rename did happen; fall through to the checks.
        End Try

        Dim landed As StatResult = ResourceProviderCalls.Stat(provider, plan.Destination.Path)
        Dim gone As StatResult = ResourceProviderCalls.Stat(provider, run.Source.FullPath)
        If landed.Outcome = StatOutcome.Failed OrElse gone.Outcome = StatOutcome.Failed Then
            Return run.Outcome(TransferResult.Indeterminate, TransferReason.Uncertain)
        End If
        If landed.Outcome <> StatOutcome.Found OrElse (length >= 0 AndAlso landed.Entry.Length >= 0 AndAlso landed.Entry.Length <> length) Then
            RestoreBackup(run)
            Return run.Outcome(TransferResult.Failed, TransferReason.PublishFailed)
        End If
        run.Published = landed.Entry
        run.State = TransferState.Published
        If gone.Outcome = StatOutcome.Missing Then
            run.State = TransferState.SourceDeleteConfirmed
            run.State = TransferState.Completed
            Return run.Outcome(TransferResult.Completed, TransferReason.None)
        End If
        Return run.Outcome(TransferResult.CopyOnly, TransferReason.SourceDeleteFailed)
    End Function

    Private Function DeleteSource(run As Run, ct As CancellationToken) As TransferOutcome
        Dim plan As TransferPlan = run.Plan
        Dim provider As IFileSystemProvider = plan.Source.Provider
        If Not provider.Capabilities.SupportsStableSourceDelete AndAlso Not provider.Capabilities.SupportsCheckedSourceDelete Then
            Return run.Outcome(TransferResult.CopyOnly, TransferReason.SourceDeleteUnsupported)
        End If
        If ct.IsCancellationRequested Then Return run.Outcome(TransferResult.CopyOnly, TransferReason.Cancelled)

        Dim again As StatResult = ResourceProviderCalls.Stat(provider, run.Source.FullPath)
        Select Case again.Outcome
            Case StatOutcome.Found
                If Not again.Entry.SameVersionAs(run.Source) Then Return run.Outcome(TransferResult.CopyOnly, TransferReason.SourceChanged)
            Case StatOutcome.Missing
                ' Already gone (another actor): the destination is now the only copy; nothing to delete.
                run.State = TransferState.SourceDeleteConfirmed
                run.State = TransferState.Completed
                Return run.Outcome(TransferResult.Completed, TransferReason.None)
            Case Else
                Return run.Outcome(TransferResult.CopyOnly, TransferReason.SourceDeleteFailed)
        End Select

        If ct.IsCancellationRequested Then Return run.Outcome(TransferResult.CopyOnly, TransferReason.Cancelled)
        Try
            provider.DeleteFile(run.Source.FullPath)
        Catch ex As Exception
            ' The reply may have been lost after removal. Reconcile without retrying remove.
        End Try
        Dim after As StatResult = ResourceProviderCalls.Stat(provider, run.Source.FullPath)
        If after.Outcome <> StatOutcome.Found AndAlso after.Outcome <> StatOutcome.Missing Then Return run.Outcome(TransferResult.Indeterminate, TransferReason.Uncertain)
        If after.Outcome <> StatOutcome.Missing Then Return run.Outcome(TransferResult.CopyOnly, TransferReason.SourceDeleteFailed)
        run.State = TransferState.SourceDeleteConfirmed
        run.State = TransferState.Completed
        Return run.Outcome(TransferResult.Completed, TransferReason.None)
    End Function

    ' --------------------------------------------------------------------- helpers ----

    Private Sub RemoveStage(run As Run)
        If Not run.StageCreated Then Return
        Try
            run.Plan.Destination.Provider.DeleteFile(run.Plan.StagePath)
            run.StageCreated = False
        Catch ex As Exception
            ' Its own, GUID-named leftover; harmless and recognisable, never anything else's.
            AppFileLogger.WriteLine("Transfer: a staged copy could not be removed - " & ex.GetType().Name)
        End Try
    End Sub

    Private Function ScratchFile(plan As TransferPlan, suffix As String) As String
        Directory.CreateDirectory(plan.ScratchDir)
        Return Path.Combine(plan.ScratchDir, plan.OperationId & suffix)
    End Function

    Private Sub CleanScratch(plan As TransferPlan)
        For Each suffix As String In {".src", ".back"}
            Try
                Dim file As String = Path.Combine(plan.ScratchDir, plan.OperationId & suffix)
                If plan.ScratchDir.Length > 0 AndAlso System.IO.File.Exists(file) Then System.IO.File.Delete(file)
            Catch ex As Exception
            End Try
        Next
    End Sub

    Private Function HashFile(path As String) As Byte()
        Using stream As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920)
            Using sha As SHA256 = SHA256.Create()
                Return sha.ComputeHash(stream)
            End Using
        End Using
    End Function

    ''' <summary>The backup of a replaced destination is no longer needed (the undo window is
    ''' over). Only the operation's own backup name is ever removed.</summary>
    Friend Function ReleaseBackup(outcome As TransferOutcome) As Boolean
        If outcome Is Nothing OrElse outcome.BackupPath.Length = 0 Then Return True
        Try
            outcome.Plan.Destination.Provider.DeleteFile(outcome.BackupPath)
            Return True
        Catch ex As Exception
            Return False
        End Try
    End Function

End Module

''' <summary>The exception form of a <see cref="TransferOutcome"/>, for the FileManager verb
''' contract whose callers speak in exceptions. It carries the outcome whole, so the
''' completion path can say exactly what DID happen - a completed copy beside a failed move,
''' an uncertain last step - instead of one undifferentiated failure. A refusal of an taken
''' name carries ERROR_FILE_EXISTS, so the existing collision wording keeps applying.</summary>
Friend NotInheritable Class TransferOutcomeException
    Inherits IOException

    Private Const HResultFileExists As Integer = &H80070050

    Friend Sub New(outcome As TransferOutcome, message As String)
        MyBase.New(If(message, "The transfer did not complete."),
                   If(outcome IsNot Nothing AndAlso outcome.Reason = TransferReason.DestinationExists, HResultFileExists, 0))
        Me.Outcome = outcome
    End Sub

    Friend ReadOnly Property Outcome As TransferOutcome
End Class

''' <summary>What an undo needs to know, recorded only for a DURABLE outcome (100 §15.6 item 5).</summary>
Friend NotInheritable Class UndoRecord
    Friend Sub New(outcome As TransferOutcome)
        Me.Outcome = outcome
    End Sub
    Friend ReadOnly Property Outcome As TransferOutcome
    Friend ReadOnly Property IsMoveInverse As Boolean
        Get
            Return Outcome.Plan.Kind = TransferKind.Move AndAlso Outcome.Result = TransferResult.Completed
        End Get
    End Property
End Class

Friend Module TransferUndo

    ''' <summary>
    ''' An undo entry for an outcome that is durable and recoverable, else Nothing: nothing for a
    ''' failure, a refusal, a cancel or an Indeterminate result, and nothing for a replace whose old
    ''' file was not kept (an atomic overwrite) - recovery of an overwritten file requires its backup.
    ''' </summary>
    Friend Function Record(outcome As TransferOutcome) As UndoRecord
        If outcome Is Nothing OrElse Not outcome.DestinationCreated Then Return Nothing
        If outcome.Result <> TransferResult.Completed AndAlso outcome.Result <> TransferResult.CopyOnly Then Return Nothing
        If outcome.ReplacedExisting AndAlso outcome.BackupPath.Length = 0 Then Return Nothing
        If outcome.PublishedEntry Is Nothing Then Return Nothing
        Return New UndoRecord(outcome)
    End Function

    ''' <summary>Puts things back, if - and only if - what this operation made is still exactly as it
    ''' left it. A move inverse also needs the original slot to be free.</summary>
    Friend Function Undo(record As UndoRecord, scratchDir As String, Optional ct As CancellationToken = Nothing) As TransferOutcome
        If record Is Nothing Then Throw New ArgumentNullException(NameOf(record))
        Dim done As TransferOutcome = record.Outcome
        Dim plan As TransferPlan = done.Plan
        Dim destProvider As IFileSystemProvider = plan.Destination.Provider

        Dim current As StatResult = ResourceProviderCalls.Stat(destProvider, plan.Destination.Path)
        If current.Outcome <> StatOutcome.Found OrElse Not current.Entry.SameVersionAs(done.PublishedEntry) Then
            Return New TransferOutcome(plan, TransferResult.Refused, TransferReason.DestinationChanged, done.LastState)
        End If

        If record.IsMoveInverse Then
            Dim slot As StatResult = ResourceProviderCalls.Stat(plan.Source.Provider, plan.Source.Path)
            If slot.Outcome <> StatOutcome.Missing Then
                Return New TransferOutcome(plan, TransferResult.Refused, TransferReason.SourceSlotOccupied, done.LastState)
            End If
            Dim back As New TransferPlan(TransferKind.Move, plan.Destination, plan.Source, ConflictPolicy.Fail, scratchDir)
            Dim moved As TransferOutcome = TransferExecutor.Execute(back, Nothing, ct)
            If moved.Succeeded Then RestoreOverwritten(done)
            Return moved
        End If

        ' A copy's inverse removes only the unchanged object it created - and only where a delete
        ' can be conditioned on that, which arbitrary SFTP cannot.
        If Not destProvider.Capabilities.CanDelete Then Return New TransferOutcome(plan, TransferResult.Refused, TransferReason.NotDeletable, done.LastState)
        If Not destProvider.Capabilities.SupportsStableSourceDelete Then Return New TransferOutcome(plan, TransferResult.Refused, TransferReason.SourceDeleteUnsupported, done.LastState)
        Try
            destProvider.DeleteFile(plan.Destination.Path)
        Catch ex As Exception
            Dim after As StatResult = ResourceProviderCalls.Stat(destProvider, plan.Destination.Path)
            If after.Outcome = StatOutcome.Failed Then Return New TransferOutcome(plan, TransferResult.Indeterminate, TransferReason.Uncertain, done.LastState)
            If after.Outcome <> StatOutcome.Missing Then Return New TransferOutcome(plan, TransferResult.Failed, TransferReason.SourceDeleteFailed, done.LastState)
        End Try
        RestoreOverwritten(done)
        Return New TransferOutcome(plan, TransferResult.Completed, TransferReason.None, TransferState.Completed)
    End Function

    Private Sub RestoreOverwritten(done As TransferOutcome)
        If done.BackupPath.Length = 0 Then Return
        Try
            done.Plan.Destination.Provider.Rename(done.BackupPath, done.Plan.Destination.Path)
        Catch ex As Exception
            AppFileLogger.WriteLine("Undo: the replaced file is still under " & done.BackupPath & " - " & ex.GetType().Name)
        End Try
    End Sub

End Module

''' <summary>A batch: per-item results with bytes, new work stops at the first cancel, and the
''' item in flight is allowed to finish its own cleanup (it owns its stage and backup names).</summary>
Friend Module TransferBatch

    Friend Function Run(plans As IEnumerable(Of TransferPlan), Optional progress As IProgress(Of Long) = Nothing,
                        Optional ct As CancellationToken = Nothing) As List(Of TransferOutcome)
        Dim results As New List(Of TransferOutcome)()
        Dim total As Long
        For Each plan As TransferPlan In plans
            If ct.IsCancellationRequested Then Exit For
            Dim before As Long = total
            Dim item As TransferOutcome = TransferExecutor.Execute(plan,
                If(progress Is Nothing, Nothing, New Progress(Of Long)(Sub(v) progress.Report(before + v))), ct)
            total += item.BytesReported
            results.Add(item)
        Next
        Return results
    End Function

End Module
#End If
