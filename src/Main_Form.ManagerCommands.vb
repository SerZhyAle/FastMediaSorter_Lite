#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Windows.Forms

Friend NotInheritable Class ManagerLocationChangedEventArgs
    Inherits EventArgs
    Friend Sub New(requestId As String, entryId As String, generation As Integer, location As ManagerLocation,
                   Optional includeDescendants As Boolean = False)
        Me.RequestId = requestId
        Me.EntryId = entryId
        Me.Generation = generation
        Me.Location = location
        Me.IncludeDescendants = includeDescendants
    End Sub
    Friend ReadOnly Property RequestId As String
    Friend ReadOnly Property EntryId As String
    Friend ReadOnly Property Generation As Integer
    Friend ReadOnly Property Location As ManagerLocation
    Friend ReadOnly Property IncludeDescendants As Boolean
End Class

Partial Public Class Main_Form
    Friend Event ManagerLocationChanged As EventHandler(Of ManagerLocationChangedEventArgs)

    Friend Async Function StartManagerCommandAsync(owner As IWin32Window, request As ManagerCommandRequest,
                                                  Optional archiveOptions As ManagerArchiveOptions = Nothing) As Task(Of Boolean)
        Dim started As Boolean
        If BatchBusy() Then
            request.Dispose()
            Return False
        End If
        batch_Starting = True
        Try
            If EnsureFileOpQueue().IsClosed Then Return False
            If archiveOptions IsNot Nothing Then
                manager_Archive_Preparing = request
                SayProgress(Localization.T("Подготовка архива"))
                ShowManagerArchiveProgress(0, 0)
                Dim progress As New Progress(Of KeyValuePair(Of Integer, Long))(
                    Sub(value) ShowManagerArchiveProgress(value.Key, value.Value))
                Dim job As ManagerArchiveJob = Nothing
                Dim ready As New TaskCompletionSource(Of Exception)(TaskCreationOptions.RunContinuationsAsynchronously)
                Dim prepare As New BatchOp With {.Kind = FileOpKind.Copy, .Source = request.Origin.DirectoryPath}
                prepare.ManagerOperation = New ManagerOperation(request, prepare, Nothing, "") With {
                    .PrepareWork = Sub(scratch) job = ManagerArchiveJob.Prepare(request, archiveOptions, scratch,
                        Sub(count, bytes) DirectCast(progress, IProgress(Of KeyValuePair(Of Integer, Long))).Report(New KeyValuePair(Of Integer, Long)(count, bytes))),
                    .PrepareCompleted = Sub(failure) ready.TrySetResult(failure)}
                If Not EnqueueBatchOp(prepare) Then Return False
                Dim preparationFailure As Exception = Await ready.Task
                If preparationFailure IsNot Nothing Then Throw preparationFailure
                request = job.Request
                manager_Archive_Preparing = Nothing
            End If
            Dim tree As ManagerTreePlan = Await Task.Run(Function() ManagerTreePlan.Capture(request))
            Dim probe As Func(Of ManagerEndpointLease, String, StatResult) = Function(lease, path) lease.Directory.StatEntry(path)
            Dim decide As Func(Of ResourceEntry, DeleteDecision) =
                Function(entry)
                    Dim size As Long = If(entry.IsDirectory,
                        tree.Nodes.Where(Function(n) ManagerPathRules.Contains(request.Source.Place.Kind, entry.FullPath, n.Entry.FullPath) AndAlso Not n.Entry.IsDirectory).Sum(Function(n) Math.Max(0L, n.Entry.Length)), entry.Length)
                    Dim facts As DeleteVolumeFacts = If(request.Source.Capabilities.HasRecycleBin, DeleteVolumeProbe.FactsFor(entry.FullPath), Nothing)
                    Return DeletePolicy.DecideForProvider(request.Source.Capabilities, facts, size, request.UseRecycleBin,
                                                         request.Kind = ManagerCommandKind.DeletePermanently)
                End Function
            Dim plan As BatchPlan = Await Task.Run(Function() ManagerCommandPlanner.Build(request, tree, probe, decide))
            If plan.NeedsCollisionAnswer.Count > 0 Then
                Dim answer As CollisionAnswer? = AskBatchCollision(owner, plan.NeedsCollisionAnswer.Count, plan.Count)
                If Not answer.HasValue Then Return False
                plan = Await Task.Run(Function() ManagerCommandPlanner.Build(request, tree, probe, decide, answer))
            End If
            If request.ArchiveJob IsNot Nothing Then
                plan = request.ArchiveJob.AddCleanup(plan,
                    Function(lease, entry) DeletePolicy.DecideForProvider(lease.Capabilities,
                        If(lease.Capabilities.HasRecycleBin, DeleteVolumeProbe.FactsFor(entry.FullPath), Nothing),
                        If(entry.IsDirectory, 0, entry.Length), request.ArchiveJob.UseRecycleBin, False))
            End If
            Dim permanent As Integer = plan.Operations.Where(Function(o) o.Kind = FileOpKind.Delete OrElse o.Kind = FileOpKind.RemoveEmptyDirectory).Count()
            Dim transfer As ManagerTransferPreflight = Await Task.Run(Function()
                Dim facts As New ManagerTransferPreflight(plan.Operations, request.Source.Capabilities, request.Destination?.Capabilities,
                    request.Destination Is Nothing OrElse request.Source.Endpoint("").IsSameResourceAs(request.Destination.Endpoint("")))
                facts.CheckScratch(ResourceRouting.TransferScratchDir)
                Return facts
            End Function)
            If transfer.NeedsWarning Then
                If ViewerBox.Show(owner,
                    Localization.TF("Будет передано {0} через медленный или тарифицируемый ресурс. Между разными серверами файл сначала скачивается, затем загружается; проверка также расходует трафик. Продолжить?", transfer.BytesText()),
                    Localization.T("Ресурсы"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return False
            End If
            Dim manifestBytes As String = If(tree.Nodes.Any(Function(n) Not n.Entry.IsDirectory AndAlso n.Entry.Length < 0),
                Localization.T("Объём передачи неизвестен"), tree.TotalBytes.ToString(Globalization.CultureInfo.CurrentCulture))
            Dim manifest As String = Localization.TF("Файлов: {0}; папок: {1}; байт: {2}", tree.FileCount, tree.FolderCount, manifestBytes)
            If transfer.RelaysThroughPc Then manifest &= Environment.NewLine & ManagerTransferPreflight.RelayText()
            If request.ArchiveJob IsNot Nothing AndAlso request.ArchiveJob.DeleteOriginals Then manifest = request.ArchiveJob.ManifestText
            If request.Kind = ManagerCommandKind.Delete OrElse request.Kind = ManagerCommandKind.DeletePermanently OrElse
               (request.ArchiveJob IsNot Nothing AndAlso request.ArchiveJob.DeleteOriginals) Then
                manifest &= Environment.NewLine & Localization.TF("Безвозвратных операций: {0}; отмена для них недоступна", permanent)
            End If
            If plan.NeedsDeleteConfirmation Then
                If ViewerBox.Show(owner, manifest, Localization.T("Удалить"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return False
            ElseIf tree.FolderCount > 0 AndAlso request.Kind <> ManagerCommandKind.Delete AndAlso request.Kind <> ManagerCommandKind.DeletePermanently Then
                If ViewerBox.Show(owner, manifest, Localization.T("Файловый менеджер"), MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) <> DialogResult.OK Then Return False
            End If
            ' A prompt is an arbitrary delay: recapture the complete tree and all source versions.
            Await Task.Run(Sub() RevalidateManagerManifest(request, tree))
            If IsDisposed OrElse EnsureFileOpQueue().IsClosed Then Return False
            Dim context As ResourceOperationContext = Await Task.Run(Function() New ResourceOperationContext With {
                .SourceCapabilities = request.Source.Capabilities, .DestinationCapabilities = request.Destination?.Capabilities})
            If ResourceBlocksFileOperations(ManagerCommandPlanner.Verb(request.Kind), context) Then Return False
            Dim phaseProgress As New Progress(Of TransferPhase)(Sub(state)
                If batch_Run?.ManagerRequest Is request Then SayProgress(ManagerTransferPhaseText(state, transfer.RelaysThroughPc))
            End Sub)
            For Each operation In plan.Operations
                operation.ManagerOperation.TransferProgress = phaseProgress
            Next
            Dim run As New BatchRunState With {.ManagerRequest = request, .ManagerTree = tree, .Entry = New BatchUndoEntry With {.IsManager = True}, .ListGeneration = file_List_Generation}
            ReleaseManagerMedia(run, plan.Operations)
            batch_Run = run
            SayProgress(manifest)
            started = EnsureBatchRunner().Start(BatchRunKind.Manager, plan.Operations, plan.Skipped, 0)
            If Not started Then batch_Run = Nothing
            Return started
        Catch ex As Exception
            Notify(FailureText.Describe(ex), NotifyLevel.Warning)
            AppFileLogger.LogException("Manager command", ex)
            Return False
        Finally
            manager_Archive_Preparing = Nothing
            If archiveOptions IsNot Nothing Then FinishManagerArchiveProgress()
            batch_Starting = False
            If Not started Then request.Dispose()
        End Try
    End Function

    Private Shared Sub RevalidateManagerManifest(request As ManagerCommandRequest, original As ManagerTreePlan)
        If Not ManagerCommandPlanner.Allows(request.Kind, request.Source.Capabilities, request.Entries, request.Origin, request.Destination?.Capabilities) Then
            Throw ManagerCommandPlanner.Failure(request.Origin.DirectoryPath, ManagerFailure.Permission)
        End If
        Dim fresh As ManagerTreePlan = ManagerTreePlan.Capture(request)
        original.RevalidateVersions(request, fresh)
    End Sub

    Private Shared Function ManagerTransferPhaseText(state As TransferPhase, relay As Boolean) As String
        Dim text As String
        Select Case state
            Case TransferPhase.DownloadSource : text = Localization.T("Чтение исходного файла")
            Case TransferPhase.UploadStage : text = Localization.T("Загрузка временного файла")
            Case TransferPhase.ReadBack : text = Localization.T("Проверка загруженного файла")
            Case TransferPhase.Publish : text = Localization.T("Публикация проверенного файла")
            Case TransferPhase.DeleteSource : text = Localization.T("Проверка удаления источника")
            Case Else : text = Localization.T("Файловый менеджер")
        End Select
        Return text & If(relay, Environment.NewLine & ManagerTransferPreflight.RelayText(), "")
    End Function

    Private Sub StartManagerUndo(entry As BatchUndoEntry)
        Dim plan As BatchUndoPlan = BatchUndoPlanner.Build(entry, Nothing)
        Dim run As New BatchRunState With {.IsUndo = True, .ManagerUndo = entry, .ListGeneration = file_List_Generation}
        ReleaseManagerMedia(run, plan.Inverse)
        batch_Run = run
        If Not EnsureBatchRunner().Start(BatchRunKind.Undo, plan.Inverse, Nothing, plan.NotReturnable.Count) Then
            batch_Run = Nothing
            entry.Dispose()
        End If
    End Sub

    Private Function ManagerDiskPath(lease As ManagerEndpointLease, path As String) As String
        If lease.Place.IsRemotePosix Then
            Dim session As ResourceSession = ActiveResourceSession()
            If session Is Nothing OrElse Not String.Equals(session.Resource.Id, lease.Place.ResourceId, StringComparison.OrdinalIgnoreCase) Then Return ""
            Dim entry As ResourceEntry = session.Entries.FirstOrDefault(Function(e) String.Equals(e.FullPath, path, StringComparison.Ordinal))
            Return If(entry Is Nothing, "", entry.LocalPath)
        End If
        Return path
    End Function

    Private Sub ReleaseManagerMedia(run As BatchRunState, ops As IReadOnlyList(Of BatchOp))
        If String.IsNullOrEmpty(Current_File_Name) Then Return
        For Each op As BatchOp In ops
            Dim payload As ManagerOperation = op.ManagerOperation
            If payload Is Nothing Then Continue For
            Dim targetLease As ManagerEndpointLease = Nothing
            Dim targetPath As String = ""
            Dim targetIsDirectory As Boolean
            If payload.Inverse IsNot Nothing Then
                targetLease = payload.Inverse.DestinationLease
                targetPath = payload.Inverse.TargetPath
                targetIsDirectory = payload.Inverse.IsDirectory
            ElseIf op.ReplaceExisting AndAlso op.Destination.Length > 0 Then
                targetLease = If(payload.Request.Destination, payload.Request.Source)
                targetPath = op.Destination
                targetIsDirectory = (payload.Expected?.IsDirectory).GetValueOrDefault()
            End If
            If targetLease IsNot Nothing Then
                Dim targetDisk As String = ManagerDiskPath(targetLease, targetPath)
                If targetDisk.Length > 0 AndAlso (String.Equals(targetDisk, Current_File_Name, StringComparison.OrdinalIgnoreCase) OrElse
                   (targetIsDirectory AndAlso ManagerPathRules.Contains(ResourceKind.Local, targetDisk, Current_File_Name))) Then
                    ReleaseActiveMedia()
                    run.MediaReleased = True
                    Return
                End If
            End If
            If op.Kind = FileOpKind.Copy OrElse op.Kind = FileOpKind.CreateDirectory Then Continue For
            If payload.Request?.Source.Place.IsRemotePosix AndAlso payload.Expected IsNot Nothing AndAlso payload.Expected.IsDirectory Then
                Dim active As ResourceSession = ActiveResourceSession()
                Dim current As ResourceEntry = active?.EntryAt(Current_File_Name)
                If active IsNot Nothing AndAlso current IsNot Nothing AndAlso
                   String.Equals(active.Resource.Id, payload.Request.Source.Place.ResourceId, StringComparison.OrdinalIgnoreCase) AndAlso
                   ManagerPathRules.Contains(ResourceKind.Sftp, payload.Op.Source, current.FullPath) Then
                    ReleaseActiveMedia()
                    run.MediaReleased = True
                    Return
                End If
            End If
            Dim disk As String = If(payload.Request Is Nothing, op.Source, ManagerDiskPath(payload.Request.Source, op.Source))
            If String.Equals(disk, Current_File_Name, StringComparison.OrdinalIgnoreCase) OrElse
               (payload.Expected IsNot Nothing AndAlso payload.Expected.IsDirectory AndAlso disk.Length > 0 AndAlso
                ManagerPathRules.Contains(ResourceKind.Local, disk, Current_File_Name)) Then
                ReleaseActiveMedia()
                run.MediaReleased = True
                Return
            End If
        Next
    End Sub

    Private Sub ManagerItemFinished(run As BatchRunState, result As BatchItemResult)
        If run Is Nothing Then Return
        Dim op As BatchOp = result.Op
        Dim payload As ManagerOperation = op.ManagerOperation
        If result.State = BatchItemState.Done Then
            If run.IsUndo Then ReconcileManagerUndo(run, payload.Inverse)
            If Not run.IsUndo AndAlso (op.Kind <> FileOpKind.CreateDirectory OrElse op.CreatedDestinationFolder) Then
                run.Entry.Items.Add(New BatchUndoItem With {.Kind = op.Kind, .Source = op.Source, .Destination = op.Destination,
                    .ManagerUndo = payload.UndoData, .PermanentDeleteReason = op.PermanentDeleteReason})
            End If
            ' Remove only actual logical sources from the viewer, never a same-name row in another folder.
            If payload.Request IsNot Nothing AndAlso op.Kind <> FileOpKind.Copy AndAlso op.Kind <> FileOpKind.CreateDirectory Then
                Dim disk As String = ManagerDiskPath(payload.Request.Source, op.Source)
                If payload.Request.Source.Place.IsRemotePosix AndAlso payload.Expected.IsDirectory Then
                    Dim active As ResourceSession = ActiveResourceSession()
                    If active IsNot Nothing AndAlso String.Equals(active.Resource.Id, payload.Request.Source.Place.ResourceId, StringComparison.OrdinalIgnoreCase) Then
                        For Each entry As ResourceEntry In active.Entries.Where(Function(e) ManagerPathRules.Contains(ResourceKind.Sftp, op.Source, e.FullPath)).ToList()
                            If String.Equals(Current_File_Name, entry.LocalPath, StringComparison.OrdinalIgnoreCase) Then run.Redisplay = True
                            RemoveCurrentFileFromList(entry.LocalPath)
                            active.Invalidate(entry.LocalPath)
                            run.ListChanged = True
                        Next
                    End If
                End If
                If disk.Length > 0 AndAlso Not IsArchiveMode() Then
                    Dim affected As List(Of String) = SnapshotFileList().Rows.Select(Function(r) r.Path).Where(
                        Function(p) String.Equals(p, disk, StringComparison.OrdinalIgnoreCase) OrElse
                            (payload.Expected.IsDirectory AndAlso ManagerPathRules.Contains(ResourceKind.Local, disk, p))).ToList()
                    For Each path As String In affected
                        If String.Equals(Current_File_Name, path, StringComparison.OrdinalIgnoreCase) Then run.Redisplay = True
                        RemoveCurrentFileFromList(path)
                        run.ListChanged = True
                        ActiveResourceSession()?.Invalidate(path)
                    Next
                End If
            End If
        End If
        If payload.Request IsNot Nothing AndAlso op.Destination.Length > 0 AndAlso
           (result.State = BatchItemState.Done OrElse (payload.Outcome?.DestinationCreated).GetValueOrDefault()) Then
            Dim targetLease As ManagerEndpointLease = If(payload.Request.Destination, payload.Request.Source)
            Dim active As ResourceSession = ActiveResourceSession()
            If targetLease.Place.IsRemotePosix AndAlso active IsNot Nothing AndAlso
               String.Equals(active.Resource.Id, targetLease.Place.ResourceId, StringComparison.OrdinalIgnoreCase) AndAlso
               ManagerPathRules.Contains(ResourceKind.Sftp, active.CurrentDirectory, op.Destination) Then
                For Each entry As ResourceEntry In active.Entries.Where(Function(e) String.Equals(e.FullPath, op.Destination, StringComparison.Ordinal)).ToList()
                    active.Invalidate(entry.LocalPath)
                Next
                run.ManagerRefreshResource = True
            End If
        End If
        ' Failed transfers can have published a checked copy: both places still need invalidation.
        RaiseEvent ManagerLocationChanged(Me, New ManagerLocationChangedEventArgs(payload.RequestId, payload.EntryId, payload.Generation, payload.Origin))
        If payload.Request IsNot Nothing Then
            InvalidateManagerPath(payload, payload.Request.Source, op.Source, (payload.Expected?.IsDirectory).GetValueOrDefault())
            If op.Destination.Length > 0 Then
                InvalidateManagerPath(payload, If(payload.Request.Destination, payload.Request.Source), op.Destination, (payload.Expected?.IsDirectory).GetValueOrDefault() OrElse op.Kind = FileOpKind.CreateDirectory)
            End If
        ElseIf payload.Inverse IsNot Nothing Then
            InvalidateManagerPath(payload, payload.Inverse.SourceLease, payload.Inverse.SourcePath, payload.Inverse.IsDirectory)
            InvalidateManagerPath(payload, payload.Inverse.DestinationLease, payload.Inverse.TargetPath, payload.Inverse.IsDirectory)
        End If
        Dim destination As ManagerLocation = If(payload.Request?.Destination IsNot Nothing,
            New ManagerLocation(payload.Request.Destination.Place, payload.Request.DestinationPath), payload.Inverse?.DestinationLocation)
        If destination IsNot Nothing Then RaiseEvent ManagerLocationChanged(Me, New ManagerLocationChangedEventArgs(payload.RequestId, payload.EntryId, payload.Generation, destination))
    End Sub

    Private Sub ReconcileManagerUndo(run As BatchRunState, data As ManagerUndoData)
        If data Is Nothing OrElse IsArchiveMode() Then Return
        If data.OriginalKind = FileOpKind.Copy OrElse data.OriginalKind = FileOpKind.Move OrElse
           data.OriginalKind = FileOpKind.Rename OrElse data.OriginalKind = FileOpKind.CreateDirectory Then
            Dim target As String = ManagerDiskPath(data.DestinationLease, data.TargetPath)
            For Each path As String In SnapshotFileList().Rows.Select(Function(r) r.Path).Where(
                Function(p) target.Length > 0 AndAlso (String.Equals(p, target, StringComparison.OrdinalIgnoreCase) OrElse
                    (data.IsDirectory AndAlso ManagerPathRules.Contains(ResourceKind.Local, target, p)))).ToList()
                If String.Equals(Current_File_Name, path, StringComparison.OrdinalIgnoreCase) Then run.Redisplay = True
                RemoveCurrentFileFromList(path)
                ActiveResourceSession()?.Invalidate(path)
                run.ListChanged = True
            Next
        End If
        If data.OriginalKind = FileOpKind.Move OrElse data.OriginalKind = FileOpKind.Rename OrElse data.OriginalKind = FileOpKind.RecycleDelete Then
            If data.SourceLease.Place.IsRemotePosix Then
                Dim active As ResourceSession = ActiveResourceSession()
                If active IsNot Nothing AndAlso String.Equals(active.Resource.Id, data.SourceLease.Place.ResourceId, StringComparison.OrdinalIgnoreCase) AndAlso
                   (ManagerPathRules.AreEqual(ResourceKind.Sftp, active.CurrentDirectory, data.SourceLease.Directory.ParentOf(data.SourcePath)) OrElse
                    (data.IsDirectory AndAlso ManagerPathRules.Contains(ResourceKind.Sftp, data.SourcePath, active.CurrentDirectory))) Then
                    run.ManagerRefreshResource = True
                End If
            ElseIf Not data.IsDirectory AndAlso IsListOfFolder(FolderOfFile(data.SourcePath)) Then
                InsertFileIntoList(data.SourcePath, current_File_Index)
                run.Redisplay = True
                run.ListChanged = True
            End If
        End If
    End Sub

    Private Sub InvalidateManagerPath(payload As ManagerOperation, lease As ManagerEndpointLease, path As String, isDirectory As Boolean)
        Dim parent As String = lease.Directory.ParentOf(path)
        If parent IsNot Nothing Then
            RaiseEvent ManagerLocationChanged(Me, New ManagerLocationChangedEventArgs(payload.RequestId, payload.EntryId,
                payload.Generation, New ManagerLocation(lease.Place, parent)))
        End If
        If isDirectory Then
            RaiseEvent ManagerLocationChanged(Me, New ManagerLocationChangedEventArgs(payload.RequestId, payload.EntryId,
                payload.Generation, New ManagerLocation(lease.Place, path), includeDescendants:=True))
        End If
    End Sub

    Private Shared Sub FinishManagerRun(run As BatchRunState, report As BatchReport)
        If Not run.IsUndo Then report.NonReversibleCount = run.Entry.Items.Where(Function(i) i.ManagerUndo Is Nothing).Count()
        If run.ManagerUndo IsNot Nothing Then
            report.NonReversibleCount = report.NotReturnableCount
            report.NotReturnableCount = 0
        End If
        If run.ManagerTree IsNot Nothing Then
            report.ManifestText = Localization.TF("Файлов: {0}; папок: {1}; байт: {2}", run.ManagerTree.FileCount, run.ManagerTree.FolderCount, run.ManagerTree.TotalBytes)
        End If
        If run.ManagerRequest?.ArchiveJob IsNot Nothing AndAlso run.ManagerRequest.ArchiveJob.DeleteOriginals Then report.ManifestText = run.ManagerRequest.ArchiveJob.ManifestText
        run.ManagerRequest?.Dispose()
        run.ManagerUndo?.Dispose()
    End Sub
End Class
#End If
