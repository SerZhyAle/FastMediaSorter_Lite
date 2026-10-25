#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Threading

''' <summary>A typed payload of the shared queue. Only this execution layer calls provider mutations.</summary>
Friend NotInheritable Class ManagerOperation
    Friend Sub New(request As ManagerCommandRequest, op As BatchOp, expected As ResourceEntry, entryId As String)
        Me.Request = request
        Me.Op = op
        Me.Expected = expected
        Me.EntryId = entryId
        Me.RequestId = request.Id
        Me.Origin = request.Origin
        Me.Generation = request.Generation
    End Sub

    Private Sub New(op As BatchOp, inverse As ManagerUndoData)
        Me.Op = op
        Me.Inverse = inverse
        Me.EntryId = inverse.EntryId
        Me.RequestId = inverse.RequestId
        Me.Origin = inverse.Origin
        Me.Generation = inverse.Generation
    End Sub

    Friend ReadOnly Property Request As ManagerCommandRequest
    Friend ReadOnly Property Op As BatchOp
    Friend ReadOnly Property Expected As ResourceEntry
    Friend ReadOnly Property EntryId As String
    Friend ReadOnly Property RequestId As String
    Friend ReadOnly Property Origin As ManagerLocation
    Friend ReadOnly Property Generation As Integer
    Friend ReadOnly Property Dependencies As New List(Of ManagerOperation)()
    Friend Property HasSkippedChildren As Boolean
    Friend ReadOnly Property Inverse As ManagerUndoData
    Friend Property Completed As Boolean
    Friend Property Outcome As TransferOutcome
    Friend Property UndoData As ManagerUndoData
    Friend Property BeforeExecute As Action
    Friend Property AfterExecute As Action
    Friend Property ReportPath As String
    Friend Property PrepareWork As Action(Of String)
    Friend Property PrepareCompleted As Action(Of Exception)
    Friend Property TransferProgress As IProgress(Of TransferPhase)

    Friend Shared Function ForUndo(data As ManagerUndoData) As BatchOp
        Dim op As New BatchOp With {.Kind = FileOpKind.MoveUndo, .Source = data.TargetPath, .Destination = data.SourcePath}
        op.ManagerOperation = New ManagerOperation(op, data)
        Return op
    End Function

    Friend Sub Execute(scratch As String)
        If PrepareWork IsNot Nothing Then
            Request.Token.ThrowIfCancellationRequested()
            PrepareWork.Invoke(scratch)
            Completed = True
            Return
        End If
        If Inverse IsNot Nothing Then
            Inverse.Execute(scratch)
            Completed = True
            Return
        End If
        Dim ct As CancellationToken = Request.Token
        ct.ThrowIfCancellationRequested()
        BeforeExecute?.Invoke()
        If HasSkippedChildren Then Throw ManagerCommandPlanner.Failure(Op.Source, ManagerFailure.Dependency)
        For Each dependency As ManagerOperation In Dependencies
            If Not dependency.Completed Then Throw ManagerCommandPlanner.Failure(Op.Source, ManagerFailure.Dependency)
        Next
        Dim source As ManagerEndpointLease = Request.Source
        Dim destination As ManagerEndpointLease = If(Request.Destination, source)
        Dim context As New ResourceOperationContext With {.SourceCapabilities = source.Capabilities, .DestinationCapabilities = Request.Destination?.Capabilities}
        If Not ResourceRefusalPolicy.IsAllowed(context, ManagerCommandPlanner.Verb(Request.Kind)) Then
            Throw ManagerCommandPlanner.Failure(Op.Source, ManagerFailure.Permission)
        End If
        ManagerTreePlan.ValidatePath(source, Op.Source, allowRoot:=Expected Is Nothing)
        If Expected IsNot Nothing Then
            Dim current As ResourceEntry = ManagerTreePlan.RequireEntry(source, Op.Source)
            If current.IsLink OrElse current.IsDirectory <> Expected.IsDirectory OrElse
               (Op.Kind <> FileOpKind.RemoveEmptyDirectory AndAlso Not current.SameVersionAs(Expected)) Then
                Throw ManagerCommandPlanner.Failure(Op.Source, ManagerFailure.SourceChanged)
            End If
        End If
        If Op.Destination.Length > 0 Then
            ManagerTreePlan.ValidatePath(destination, Op.Destination, allowRoot:=False, allowMissing:=True)
        End If
        ct.ThrowIfCancellationRequested()
        Select Case Op.Kind
            Case FileOpKind.Copy, FileOpKind.Move
                Dim plan As New TransferPlan(If(Op.Kind = FileOpKind.Copy, TransferKind.Copy, TransferKind.Move),
                    source.Endpoint(Op.Source), destination.Endpoint(Op.Destination),
                    If(Op.ReplaceExisting, ConflictPolicy.Replace, ConflictPolicy.Fail), scratch, Expected, allowServerSideMove:=False)
                plan.PhaseProgress = TransferProgress
                plan.ValidateMutation = Sub()
                    Request.Token.ThrowIfCancellationRequested()
                    If Not ResourceRefusalPolicy.IsAllowed(New ResourceOperationContext With {
                        .SourceCapabilities = source.Capabilities, .DestinationCapabilities = destination.Capabilities},
                        ManagerCommandPlanner.Verb(Request.Kind)) Then Throw ManagerCommandPlanner.Failure(Op.Source, ManagerFailure.Permission)
                End Sub
                ' A directory move is always decomposed; file transfers retain the shared verified executor.
                Outcome = TransferExecutor.Execute(plan, Nothing, ct)
                If Not Outcome.Succeeded Then Throw New TransferOutcomeException(Outcome, ResourceRouting.DescribeOutcome(Outcome))
                If TransferUndo.Record(Outcome) IsNot Nothing AndAlso
                   (Op.Kind = FileOpKind.Move OrElse destination.Capabilities.SupportsStableSourceDelete) Then
                    UndoData = New ManagerUndoData(Me, source, destination, Outcome.PublishedEntry, TransferUndo.Record(Outcome))
                End If
            Case FileOpKind.CreateDirectory
                If Not ResourceRefusalPolicy.IsAllowed(destination.Capabilities, ResourceVerb.CreateFolder) Then
                    Throw ManagerCommandPlanner.Failure(Op.Destination, ManagerFailure.Permission)
                End If
                Dim before As StatResult = destination.Directory.StatEntry(Op.Destination)
                If before.Outcome = StatOutcome.Found Then
                    If Not before.Entry.IsDirectory OrElse before.Entry.IsLink OrElse Expected Is Nothing OrElse Not Op.ReplaceExisting Then
                        Throw ManagerCommandPlanner.Failure(Op.Destination, ManagerFailure.Conflict)
                    End If
                ElseIf before.Outcome = StatOutcome.Missing Then
                    Dim parent As ResourceEntry = ManagerTreePlan.RequireEntry(destination, destination.Directory.ParentOf(Op.Destination))
                    If Not parent.IsDirectory Then Throw ManagerCommandPlanner.Failure(Op.Destination, ManagerFailure.Conflict)
                    destination.Provider.CreateDirectory(Op.Destination)
                    Dim created As ResourceEntry = ManagerTreePlan.RequireEntry(destination, Op.Destination)
                    If Not created.IsDirectory OrElse created.IsLink Then Throw ManagerCommandPlanner.Failure(Op.Destination, ManagerFailure.Conflict)
                    Op.CreatedDestinationFolder = True
                    UndoData = New ManagerUndoData(Me, source, destination, created, Nothing)
                Else
                    Throw ManagerCommandPlanner.Failure(Op.Destination, ManagerFailure.MissingOrDenied)
                End If
            Case FileOpKind.Rename
                Dim version As ResourceEntry
                If Not Expected.IsDirectory AndAlso Op.ReplaceExisting AndAlso Not destination.Directory.PathsEqual(Op.Source, Op.Destination) Then
                    Dim renamePlan As New TransferPlan(TransferKind.Move, source.Endpoint(Op.Source), destination.Endpoint(Op.Destination), ConflictPolicy.Replace, scratch, Expected) With {
                        .ValidateMutation = Sub()
                            ct.ThrowIfCancellationRequested()
                            If Not ResourceRefusalPolicy.IsAllowed(source.Capabilities, ResourceVerb.Rename) OrElse Not destination.Capabilities.CanWrite Then
                                Throw ManagerCommandPlanner.Failure(Op.Source, ManagerFailure.Permission)
                            End If
                        End Sub, .PhaseProgress = TransferProgress}
                    Outcome = TransferExecutor.Execute(renamePlan, Nothing, ct)
                    If Not Outcome.Succeeded Then Throw New TransferOutcomeException(Outcome, ResourceRouting.DescribeOutcome(Outcome))
                    version = Outcome.PublishedEntry
                Else
                    RenameChecked(source, Op.Source, Op.Destination, Expected, ct)
                    version = ManagerTreePlan.RequireEntry(source, Op.Destination)
                End If
                UndoData = New ManagerUndoData(Me, source, destination, version, If(Outcome Is Nothing, Nothing, TransferUndo.Record(Outcome)))
            Case FileOpKind.Delete
                ResourceDeleteTarget.ExecuteProvider(source.Provider, Op.Source)
            Case FileOpKind.RecycleDelete
                If Not source.Capabilities.HasRecycleBin OrElse source.Place.IsRemotePosix Then Throw ManagerCommandPlanner.Failure(Op.Source, ManagerFailure.Permission)
                Op.DeletedAtUtc = DateTime.UtcNow
                Op.RecycleFellThrough = RecycleBinIo.SendToBinChecked(Op.Source, Op.DeletedAtUtc)
                If source.Directory.StatEntry(Op.Source).Outcome <> StatOutcome.Missing Then Throw ManagerCommandPlanner.Failure(Op.Source, ManagerFailure.MissingOrDenied)
                If Not Op.RecycleFellThrough Then UndoData = New ManagerUndoData(Me, source, source, Expected, Nothing)
                If Op.RecycleFellThrough Then
                    Op.Kind = FileOpKind.Delete
                    Op.PermanentDeleteReason = PermanentReason.VolumeUnknown
                End If
            Case FileOpKind.RemoveEmptyDirectory
                Dim listing As DirectoryListing = source.Directory.ListChildren(Op.Source, ct)
                If listing.Outcome <> DirectoryOutcome.Listed OrElse listing.IsTruncated Then Throw ManagerCommandPlanner.Failure(Op.Source, ManagerFailure.IncompleteTree)
                If listing.Entries.Count > 0 Then Throw ManagerCommandPlanner.Failure(Op.Source, ManagerFailure.NotEmpty)
                ct.ThrowIfCancellationRequested()
                Dim removal As DirectoryRemoval = source.Directory.RemoveEmptyDirectory(source.Place.RootPath, Op.Source)
                If removal <> DirectoryRemoval.Removed Then Throw ManagerCommandPlanner.Failure(Op.Source, ManagerFailure.NotEmpty)
                If Request.Kind = ManagerCommandKind.Move Then UndoData = New ManagerUndoData(Me, source, source, Expected, Nothing)
            Case Else
                Throw New InvalidOperationException("Unknown manager queue payload.")
        End Select
        If Request.ArchiveJob IsNot Nothing Then
            ' Stages expire after this run, so their copies cannot promise ordinary source-based undo.
            UndoData?.Dispose()
            UndoData = Nothing
        End If
        AfterExecute?.Invoke()
        Completed = True
    End Sub

    Friend Shared Sub RenameChecked(lease As ManagerEndpointLease, source As String, target As String, version As ResourceEntry, ct As CancellationToken)
        Dim caseOnly As Boolean = lease.Directory.PathsEqual(source, target) AndAlso Not String.Equals(source, target, StringComparison.Ordinal)
        If Not caseOnly Then
            If lease.Directory.StatEntry(target).Outcome <> StatOutcome.Missing Then Throw ManagerCommandPlanner.Failure(target, ManagerFailure.Conflict)
            lease.Provider.Rename(source, target)
        Else
            Dim intermediate As String = ManagerPathRules.Combine(lease.Place.Kind, lease.Directory.ParentOf(source), "." & Guid.NewGuid().ToString("N") & ".fms-rename")
            If lease.Directory.StatEntry(intermediate).Outcome <> StatOutcome.Missing Then Throw ManagerCommandPlanner.Failure(intermediate, ManagerFailure.Conflict)
            lease.Provider.Rename(source, intermediate)
            Try
                ct.ThrowIfCancellationRequested()
                If lease.Directory.StatEntry(target).Outcome <> StatOutcome.Missing Then Throw ManagerCommandPlanner.Failure(target, ManagerFailure.Conflict)
                lease.Provider.Rename(intermediate, target)
            Catch
                Dim staged As StatResult = lease.Directory.StatEntry(intermediate)
                Dim published As StatResult = lease.Directory.StatEntry(target)
                If published.Outcome = StatOutcome.Found AndAlso staged.Outcome = StatOutcome.Missing AndAlso
                   String.Equals(published.Entry.FullPath, target, StringComparison.Ordinal) AndAlso published.Entry.SameVersionAs(version) Then Return
                If staged.Outcome = StatOutcome.Found AndAlso staged.Entry.SameVersionAs(version) AndAlso published.Outcome = StatOutcome.Missing Then
                    lease.Provider.Rename(intermediate, source)
                End If
                Throw
            End Try
        End If
        Dim after As StatResult = lease.Directory.StatEntry(target)
        If after.Outcome <> StatOutcome.Found OrElse Not after.Entry.SameVersionAs(version) Then Throw ManagerCommandPlanner.Failure(target, ManagerFailure.SourceChanged)
        If Not caseOnly AndAlso lease.Directory.StatEntry(source).Outcome <> StatOutcome.Missing Then Throw ManagerCommandPlanner.Failure(source, ManagerFailure.SourceChanged)
    End Sub
End Class

''' <summary>Conditional inverse retained by the existing UndoStack, with its own counted leases.</summary>
Friend NotInheritable Class ManagerUndoData
    Implements IDisposable
    Private ReadOnly source As ManagerEndpointLease
    Private ReadOnly destination As ManagerEndpointLease
    Private ReadOnly version As ResourceEntry
    Private ReadOnly record As UndoRecord
    Private ReadOnly kind As FileOpKind
    Private ReadOnly deletedAt As DateTime

    Friend Sub New(operation As ManagerOperation, source As ManagerEndpointLease, destination As ManagerEndpointLease,
                   version As ResourceEntry, record As UndoRecord)
        Me.source = source.Fork()
        Me.destination = destination.Fork()
        Me.version = version
        Me.record = record
        Me.kind = operation.Op.Kind
        Me.deletedAt = operation.Op.DeletedAtUtc
        Me.SourcePath = operation.Op.Source
        Me.TargetPath = If(kind = FileOpKind.RecycleDelete OrElse kind = FileOpKind.RemoveEmptyDirectory, SourcePath, operation.Op.Destination)
        Me.EntryId = operation.EntryId
        Me.RequestId = operation.RequestId
        Me.Origin = operation.Origin
        Me.Generation = operation.Generation
    End Sub
    Friend ReadOnly Property SourcePath As String
    Friend ReadOnly Property TargetPath As String
    Friend ReadOnly Property EntryId As String
    Friend ReadOnly Property RequestId As String
    Friend ReadOnly Property Origin As ManagerLocation
    Friend ReadOnly Property Generation As Integer
    Friend ReadOnly Property SourceLease As ManagerEndpointLease
        Get
            Return source
        End Get
    End Property
    Friend ReadOnly Property DestinationLease As ManagerEndpointLease
        Get
            Return destination
        End Get
    End Property
    Friend ReadOnly Property OriginalKind As FileOpKind
        Get
            Return kind
        End Get
    End Property
    Friend ReadOnly Property IsDirectory As Boolean
        Get
            Return version IsNot Nothing AndAlso version.IsDirectory
        End Get
    End Property
    Friend ReadOnly Property DestinationLocation As ManagerLocation
        Get
            Return New ManagerLocation(destination.Place, destination.Directory.ParentOf(TargetPath))
        End Get
    End Property

    Friend Sub Execute(scratch As String)
        If kind = FileOpKind.RemoveEmptyDirectory Then
            If Not ResourceRefusalPolicy.IsAllowed(source.Capabilities, ResourceVerb.CreateFolder) Then Throw ManagerCommandPlanner.Failure(SourcePath, ManagerFailure.Permission)
            ManagerTreePlan.ValidatePath(source, SourcePath, allowRoot:=False, allowMissing:=True)
            If source.Directory.StatEntry(SourcePath).Outcome <> StatOutcome.Missing Then Throw ManagerCommandPlanner.Failure(SourcePath, ManagerFailure.Conflict)
            source.Provider.CreateDirectory(SourcePath)
            Return
        End If
        If kind = FileOpKind.RecycleDelete Then
            If Not source.Capabilities.CanWrite Then Throw ManagerCommandPlanner.Failure(SourcePath, ManagerFailure.Permission)
            ManagerTreePlan.ValidatePath(source, source.Directory.ParentOf(SourcePath), allowRoot:=True)
            If source.Directory.StatEntry(SourcePath).Outcome <> StatOutcome.Missing Then Throw ManagerCommandPlanner.Failure(SourcePath, ManagerFailure.Conflict)
            If RecycleBinIo.TryRestore(SourcePath, deletedAt, SourcePath) <> BinRestoreResult.Restored Then Throw ManagerCommandPlanner.Failure(SourcePath, ManagerFailure.MissingOrDenied)
            Return
        End If
        ManagerTreePlan.ValidatePath(destination, TargetPath, allowRoot:=False)
        Dim current As ResourceEntry = ManagerTreePlan.RequireEntry(destination, TargetPath)
        If current.IsLink OrElse current.IsDirectory <> version.IsDirectory OrElse Not current.SameVersionAs(version) Then Throw ManagerCommandPlanner.Failure(TargetPath, ManagerFailure.SourceChanged)
        If record IsNot Nothing Then
            If Not destination.Capabilities.CanDelete OrElse (record.IsMoveInverse AndAlso Not source.Capabilities.CanWrite) Then Throw ManagerCommandPlanner.Failure(TargetPath, ManagerFailure.Permission)
            If record.IsMoveInverse Then ManagerTreePlan.ValidatePath(source, SourcePath, allowRoot:=False, allowMissing:=True)
            If Not record.IsMoveInverse Then
                Dim original As ResourceEntry = ManagerTreePlan.RequireEntry(source, SourcePath)
                If Not original.SameVersionAs(record.Outcome.SourceVersion) Then Throw ManagerCommandPlanner.Failure(SourcePath, ManagerFailure.SourceChanged)
            End If
            Dim result As TransferOutcome = TransferUndo.Undo(record, scratch)
            If Not result.Succeeded Then Throw New TransferOutcomeException(result, ResourceRouting.DescribeOutcome(result))
        ElseIf kind = FileOpKind.Rename Then
            If Not source.Capabilities.CanRename Then Throw ManagerCommandPlanner.Failure(TargetPath, ManagerFailure.Permission)
            ManagerOperation.RenameChecked(source, TargetPath, SourcePath, version, CancellationToken.None)
        ElseIf kind = FileOpKind.CreateDirectory Then
            If Not destination.Capabilities.CanDelete Then Throw ManagerCommandPlanner.Failure(TargetPath, ManagerFailure.Permission)
            Dim children As DirectoryListing = destination.Directory.ListChildren(TargetPath, CancellationToken.None)
            If children.Outcome <> DirectoryOutcome.Listed OrElse children.IsTruncated OrElse children.Entries.Count <> 0 Then Throw ManagerCommandPlanner.Failure(TargetPath, ManagerFailure.NotEmpty)
            If destination.Directory.RemoveEmptyDirectory(destination.Place.RootPath, TargetPath) <> DirectoryRemoval.Removed Then Throw ManagerCommandPlanner.Failure(TargetPath, ManagerFailure.NotEmpty)
        Else
            Throw ManagerCommandPlanner.Failure(TargetPath, ManagerFailure.Permission)
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        source.Dispose()
        destination.Dispose()
    End Sub
End Class
#End If
