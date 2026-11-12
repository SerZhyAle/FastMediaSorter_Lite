Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading

' The executor of scheduled file operations (ticket 222, sections 4 and 6).
'
' ORDER OF A RUN - and the order is the safety story:
'   1. validate the definition (the same function the editor's save runs);
'   2. open the source, and check it can be read;
'   3. scan, then filter - the WHOLE affected list is resolved before anything is acted on;
'   4. the target pre-flight: reachable and writable, refusing the run whole, before anything is touched;
'   5. plan the names (skip / overwrite / keep both, structure preserved);
'   6. act, one file at a time - a failed file is counted and the run goes on;
'   7. for an archive: read it back and prove it before an original is touched.
'
' A run is UNATTENDED: nothing here asks a question. What cannot be fixed by a retry stops the whole
' run as a WALL (an unreachable folder, a resource whose password is not stored); everything about one
' file is a counted failure.
'
' Everything the executor learns about the machine - the clock, the classifier, the volume facts, the
' bin - comes through <see cref="ScheduledServices"/>, so a test drives it on a scratch folder with a
' fixed clock and a stub bin.

''' <summary>The result of resolving one side of an operation.</summary>
Friend NotInheritable Class ScheduledSideOpen
    Friend ReadOnly Side As IScheduledSide
    Friend ReadOnly Problem As SideProblem

    Friend Sub New(side As IScheduledSide, problem As SideProblem)
        Me.Side = side
        Me.Problem = problem
    End Sub

    Friend Shared Function Opened(side As IScheduledSide) As ScheduledSideOpen
        Return New ScheduledSideOpen(side, SideProblem.Ok)
    End Function

    Friend Shared Function Failed(problem As SideProblem) As ScheduledSideOpen
        Return New ScheduledSideOpen(Nothing, problem)
    End Function
End Class

''' <summary>Everything the executor reads from the machine, injectable.</summary>
Friend NotInheritable Class ScheduledServices

    Friend Property Clock As Func(Of DateTime) = Function() DateTime.Now
    ''' <summary>The local time zone of the wall clock (ticket 231, 3.7).</summary>
    Friend Property TimeZone As Func(Of TimeZoneInfo) = Function() TimeZoneInfo.Local
    Friend Property KindOf As Func(Of String, MediaKind) = Function(name As String) MediaKindClassifier.KindOf(name)
    ''' <summary>The folders no operation may touch. Nothing means none (tests).</summary>
    Friend Property Protection As ProtectedFolders
    ''' <summary>Resolves a location to a side, or says why it cannot be used. The product's default knows
    ''' paths; the resource side is added where the registry is linked.</summary>
    Friend Property OpenSide As Func(Of ScheduledLocation, ScheduledSideOpen) = AddressOf OpenDefaultSide
    ''' <summary>The facts of a registered resource, from the viewer's registry (read, never written).</summary>
    Friend Property ResourceFacts As Func(Of String, ScheduledResourceFacts) = Function(id As String) ScheduledResourceRegistry.Lookup(id)
    ''' <summary>Whether the user is signed in to Google in the Exchange settings (ticket 231, 3.2).</summary>
    Friend Property IsDriveSignedIn As Func(Of Boolean) = AddressOf ScheduledDriveRegistry.IsSignedIn
    ''' <summary>Called when a run ends in Failed or Wall on an operation with <see cref="ScheduledOperation.ShowBalloonOnError"/> (ticket 231, 3.5).</summary>
    Friend Property NotifyError As Action(Of ScheduledOperation, ScheduledRunResult)
    ''' <summary>Which transfer moves the bytes between two sides: plain System.IO between two local folders, the
    ''' viewer's checked executor when either end is a resource.</summary>
    Friend Property TransferFor As Func(Of IScheduledSide, IScheduledSide, IScheduledTransfer) =
        Function(source As IScheduledSide, destination As IScheduledSide) As IScheduledTransfer
            Return If(source.IsLocalPath AndAlso destination.IsLocalPath, LocalTransferInstance, ProviderTransferInstance)
        End Function

    Private Shared ReadOnly LocalTransferInstance As IScheduledTransfer = New LocalTransfer()
    Private Shared ReadOnly ProviderTransferInstance As IScheduledTransfer = New ProviderTransfer()
    ''' <summary>The viewer's global "delete to the Recycle Bin" setting (D3), read at EVERY run.</summary>
    Friend Property DeleteToRecycleBin As Func(Of Boolean) = Function() ScheduledSettings.DeleteToRecycleBin
    Friend Property VolumeFacts As Func(Of String, DeleteVolumeFacts) = Function(path As String) DeleteVolumeProbe.FactsFor(path)
    ''' <summary>Hands the file to the shell and answers True when it was deleted for good instead.</summary>
    Friend Property SendToBin As Func(Of String, Boolean) = Function(path As String) RecycleBinIo.SendToBinChecked(path, DateTime.UtcNow)
    Friend Property DeletePermanently As Action(Of String) = Sub(path As String) System.IO.File.Delete(path)
    Friend Property ItemJournal As Action(Of ItemJournalEntry)
    ''' <summary>Called before each file is acted on. A test hook (a source that changes under the run).</summary>
    Friend Property BeforeItem As Action(Of String)
    ''' <summary>Called with the archive's path after it is written and before it is read back. A test hook.</summary>
    Friend Property AfterArchiveWritten As Action(Of String)
    ''' <summary>Fill of free space (ticket L0299): where FileDO is, what the volume holds, and the launch itself.</summary>
    Friend Property LocateFileDo As Func(Of String) = AddressOf FileDoLocator.Find
    Friend Property FillVolume As Func(Of String, FillVolumeInfo) = AddressOf FillVolumeInfo.Read
    Friend Property LaunchFill As Func(Of FillLaunch, CancellationToken, FillLaunchOutcome) = AddressOf FillProcess.Run
    ''' <summary>Backup (ticket L0298): the stored password of a sealed backup, "" when there is none on this PC.</summary>
    Friend Property BackupPassword As Func(Of String, String) =
        Function(id As String) New ScheduledBackupCredentialStore(ScheduledStore.DefaultFolder()).Read(id)
    ''' <summary>Whether the FileDO found can run a numbered generation set. False until the generation and stem options are
    ''' published by FileDO (L0298 blocker B-1b); the argument builder of step P4.1 is written against that spelling.</summary>
    Friend Property FileDoHasBackupGenerations As Func(Of String, Boolean) = Function(exe As String) False

    Friend Shared Function ForProduct(journal As ScheduledJournal) As ScheduledServices
        Dim services As New ScheduledServices With {.Protection = ProtectedFolders.ForThisMachine()}
        If journal IsNot Nothing Then services.ItemJournal = Sub(entry As ItemJournalEntry) journal.AppendItem(entry)
        Return services
    End Function

    Private Shared Function OpenDefaultSide(location As ScheduledLocation) As ScheduledSideOpen
        If location.Kind = ScheduledLocationKind.Drive Then
            Return ScheduledDriveRegistry.OpenSide(location)
        End If
        If location.Kind = ScheduledLocationKind.Resource Then
            Return ScheduledResourceSides.Open(location, location.Recursive, CancellationToken.None)
        End If
        Try
            Return ScheduledSideOpen.Opened(LocalSide.ForLocation(location))
        Catch ex As ArgumentException
            Return ScheduledSideOpen.Failed(New SideProblem(SideProblemKind.Unreachable, location.Path))
        End Try
    End Function

End Class

Friend NotInheritable Class ScheduledRunResult
    Friend Property Outcome As ScheduledRunOutcome = ScheduledRunOutcome.None
    ''' <summary>Files acted on.</summary>
    Friend Property Files As Integer
    Friend Property Failed As Integer
    Friend Property Skipped As Integer
    Friend Property Bytes As Long
    ''' <summary>The first failed file's error, as the machine wrote it.</summary>
    Friend Property FirstError As String = ""
    ''' <summary>For a refusal: "&lt;issue code&gt;" and its detail; for a wall: its reason.</summary>
    Friend Property Detail As String = ""
    Friend Property Issues As New List(Of ScheduledIssue)()
    ''' <summary>The halt the engine applies to the operation: a wall, or a removed resource.</summary>
    Friend Property Halt As ScheduledHalt = ScheduledHalt.None
    ''' <summary>Delete: "recycle", "permanent:&lt;reason&gt;" or "mixed" - recorded on the log line (D3).</summary>
    Friend Property Verdict As String = ""
    Friend Property ArchivePath As String = ""
    Friend Property StartedAt As DateTime
    Friend Property Duration As TimeSpan
    Friend Property SourceDescription As String = ""
    Friend Property DestinationDescription As String = ""
    ''' <summary>The mark the next run through Google Drive starts from, set only by a clean run; "" when there is none.</summary>
    Friend Property DriveMark As String = ""
End Class

''' <summary>One file of a plan.</summary>
Friend NotInheritable Class PlannedItem
    Friend Property Entry As SideEntry
    ''' <summary>Where it goes inside the destination (copy and move).</summary>
    Friend Property TargetRelative As String = ""
    Friend Property Skip As Boolean
    ''' <summary>Why it is skipped - "exists" - or "" when it is not.</summary>
    Friend Property SkipReason As String = ""
    Friend Property Overwrite As Boolean
    ''' <summary>Keep-both chose a new name.</summary>
    Friend Property Renamed As Boolean
End Class

''' <summary>The "what would run now" listing of the editor (D6): read-only, capped, never a gate.</summary>
Friend NotInheritable Class ScheduledPreview
    Friend Property Problem As SideProblem = SideProblem.Ok
    Friend Property Issues As New List(Of ScheduledIssue)()
    ''' <summary>Files that would be acted on, and in total.</summary>
    Friend Property WouldAct As Integer
    Friend Property WouldSkip As Integer
    Friend Property Bytes As Long
    Friend Property Lines As New List(Of String)()
    Friend Property Truncated As Boolean
    ''' <summary>The source is Google Drive: what would run is known only to the run itself (asking would take files off Drive).</summary>
    Friend Property DriveSource As Boolean
End Class

Friend NotInheritable Class ScheduledExecutor

    ''' <summary>The preview's cap: a folder of a million files must not become a million list rows.</summary>
    Friend Const PreviewLineCap As Integer = 200

    Private ReadOnly services As ScheduledServices

    Friend Sub New(services As ScheduledServices)
        Me.services = services
    End Sub

    ' --- the run ----------------------------------------------------------------------------------

    Friend Function Run(operation As ScheduledOperation, ct As CancellationToken) As ScheduledRunResult
        Dim op As ScheduledOperation = operation.Clone()
        ScheduledValidation.Normalize(op)
        Dim watch As Stopwatch = Stopwatch.StartNew()
        Dim result As New ScheduledRunResult With {.StartedAt = services.Clock.Invoke()}
        Try
            RunCore(op, result, ct)
        Catch ex As OperationCanceledException
            result.Outcome = ScheduledRunOutcome.Cancelled
        Catch ex As Exception
            ' Nothing a run does may take the host down; the failure is the run's result.
            result.Outcome = ScheduledRunOutcome.Failed
            If result.FirstError.Length = 0 Then result.FirstError = ScheduledFailure.Coded(ScheduledFailure.CodeOf(ex), "")
            AppFileLogger.LogException("Scheduled operations: " & op.DisplayName(), ex)
        End Try
        result.Duration = watch.Elapsed
        Return result
    End Function

    Private Sub RunCore(op As ScheduledOperation, result As ScheduledRunResult, ct As CancellationToken)
        If op.Action = ScheduledAction.FillFreeSpace Then
            ' Selects no files and has no destination: the definition, then FileDO (ticket L0299).
            result.SourceDescription = op.Source.Describe()
            Dim refusal As Resolution = CheckDefinition(op, forRun:=True)
            If refusal IsNot Nothing Then
                Adopt(result, refusal.StopResult)
            Else
                ExecuteFill(op, result, ct)
            End If
            Return
        End If
        If op.Action = ScheduledAction.Backup Then
            ' The folder is one unit and FileDO does the whole job (ticket L0298, Q298-1).
            result.SourceDescription = op.Source.Describe()
            result.DestinationDescription = op.Destination.Describe()
            Dim refusal As Resolution = CheckDefinition(op, forRun:=True)
            If refusal IsNot Nothing Then
                Adopt(result, refusal.StopResult)
            Else
                ExecuteBackup(op, result)
            End If
            Return
        End If
        Dim resolved As Resolution = Resolve(op, result.StartedAt, forRun:=True, ct)
        result.SourceDescription = op.Source.Describe()
        result.DestinationDescription = If(op.NeedsDestination, op.Destination.Describe(), "")
        If resolved.Stopped Then
            Adopt(result, resolved.StopResult)
            Return
        End If

        Dim completed As Boolean = False
        Try
            Select Case op.Action
                Case ScheduledAction.Copy, ScheduledAction.Move
                    ExecuteTransfers(op, resolved, result, ct)
                Case ScheduledAction.Delete
                    ExecuteDeletes(op, resolved, result, ct)
                Case ScheduledAction.Archive
                    ExecuteArchive(op, resolved, result, ct)
            End Select
            completed = True
        Catch ex As ScheduledSideException
            ' The destination went away under a packet: the same skip or wall as if it had been gone from the start.
            Dim firstError As String = result.FirstError
            Adopt(result, FromProblem(ex.Problem, "destination").StopResult)
            result.FirstError = firstError
        Finally
            TryCast(resolved.Source, IScheduledRunAwareSource)?.RunEnded(result, completed)
            resolved.Source?.Dispose()
            resolved.Destination?.Dispose()
        End Try

        If result.Outcome = ScheduledRunOutcome.None Then
            result.Outcome = If(result.Failed > 0, ScheduledRunOutcome.Failed, ScheduledRunOutcome.Ok)
        End If
    End Sub

    Private Shared Sub Adopt(result As ScheduledRunResult, stopped As ScheduledRunResult)
        result.Outcome = stopped.Outcome
        result.Detail = stopped.Detail
        result.Halt = stopped.Halt
        result.Issues = stopped.Issues
        result.FirstError = stopped.FirstError
    End Sub

    ' --- resolve ----------------------------------------------------------------------------------

    Private NotInheritable Class Resolution
        Friend StopResult As ScheduledRunResult
        Friend Source As IScheduledSide
        Friend Destination As IScheduledSide
        Friend Items As List(Of PlannedItem)
        ''' <summary>A preview of a Drive source: valid, and nothing more can be said without taking files off Drive.</summary>
        Friend DriveSource As Boolean
        Friend ReadOnly Property Stopped As Boolean
            Get
                Return StopResult IsNot Nothing
            End Get
        End Property
    End Class

    Private Shared Function Stopping(outcome As ScheduledRunOutcome, detail As String, Optional halt As ScheduledHalt = ScheduledHalt.None) As Resolution
        Return New Resolution With {.StopResult = New ScheduledRunResult With {.Outcome = outcome, .Detail = detail, .Halt = halt}}
    End Function

    ''' <summary>Steps 1-5 of a run, shared with the preview. With <paramref name="forRun"/> False nothing
    ''' on disk is created or written: the destination is only looked at.</summary>
    Private Function Resolve(op As ScheduledOperation, now As DateTime, forRun As Boolean, ct As CancellationToken) As Resolution
        ' The sides opened on the way are released here when the resolution does not go on to a run: a connected
        ' resource is a session, and a refusal must not leave one open.
        Dim opened As New List(Of IScheduledSide)()
        Dim resolution As Resolution = Nothing
        Try
            resolution = ResolveCore(op, now, forRun, ct, opened)
            Return resolution
        Finally
            If resolution Is Nothing OrElse resolution.Stopped Then
                For Each side As IScheduledSide In opened
                    side.Dispose()
                Next
            End If
        End Try
    End Function

    Private Function ResolveCore(op As ScheduledOperation, now As DateTime, forRun As Boolean, ct As CancellationToken,
                                 opened As List(Of IScheduledSide)) As Resolution
        ' 1. The definition. A definition that is refused is refused before any side is touched.
        Dim refusal As Resolution = CheckDefinition(op, forRun)
        If refusal IsNot Nothing Then Return refusal

        If Not forRun AndAlso op.Source.Kind = ScheduledLocationKind.Drive Then Return New Resolution With {.DriveSource = True}

        ' 2. The source.
        Dim sourceOpen As ScheduledSideOpen = services.OpenSide.Invoke(op.Source)
        If sourceOpen.Problem.IsProblem Then Return FromProblem(sourceOpen.Problem, "source")
        Dim source As IScheduledSide = sourceOpen.Side
        opened.Add(source)
        Dim readable As SideProblem = source.CheckReadable()
        If readable.IsProblem Then Return FromProblem(readable, "source")

        ' 3. Scan, then filter - the whole list, before anything is acted on.
        TryCast(source, IScheduledRunAwareSource)?.RunStarting(op)
        Dim entries As List(Of SideEntry)
        Try
            entries = source.List(op.Source.Recursive, ct)
        Catch ex As OperationCanceledException
            Throw
        Catch ex As ScheduledSideException
            Return FromProblem(ex.Problem, "source")
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: scanning " & source.Describe(), ex)
            Return FromProblem(New SideProblem(SideProblemKind.Unreachable, source.Describe()), "source")
        End Try
        Dim matcher As New ConditionMatcher(op.Conditions, now, op.Record.LastSuccessAt, services.KindOf)
        Dim matched As List(Of SideEntry) = entries.Where(Function(e As SideEntry) matcher.Matches(e)).
            OrderBy(Function(e As SideEntry) e.RelativePath, StringComparer.OrdinalIgnoreCase).ToList()

        ' 4. The target pre-flight, always: an unplugged backup drive is worth saying even when nothing is new.
        Dim destination As IScheduledSide = Nothing
        If op.NeedsDestination Then
            Dim destinationOpen As ScheduledSideOpen = services.OpenSide.Invoke(op.Destination)
            If destinationOpen.Problem.IsProblem Then Return FromProblem(destinationOpen.Problem, "destination")
            destination = destinationOpen.Side
            opened.Add(destination)
            If forRun Then
                Dim writable As SideProblem = destination.CheckWritable()
                If writable.IsProblem Then Return FromProblem(writable, "destination")
            End If
        End If

        ' 5. The names.
        Dim items As List(Of PlannedItem) = Plan(op, matched, destination)
        Return New Resolution With {.Source = source, .Destination = destination, .Items = items}
    End Function

    ''' <summary>Step 1 of a run or a preview: the refusal of the definition, or Nothing when it may go on.</summary>
    Private Function CheckDefinition(op As ScheduledOperation, forRun As Boolean) As Resolution
        Dim issues As List(Of ScheduledIssue) = ScheduledValidation.Validate(op, services.Protection, services.ResourceFacts,
            Function(id As String) services.BackupPassword.Invoke(id).Length > 0)
        If op.Halt = ScheduledHalt.InvalidDefinition Then
            Return Stopping(ScheduledRunOutcome.Refused, "invalid definition: " & op.HaltDetail)
        End If
        If issues.Count = 0 Then Return Nothing
        If forRun AndAlso issues(0).Code = ScheduledIssueCode.BackupPasswordMissing Then
            ' A password this PC never had (an import) or can no longer read: the operation waits for it, it is not refused.
            Return Stopping(ScheduledRunOutcome.Wall, ShareSettingsTransfer.BackupNoPasswordCode, ScheduledHalt.Wall)
        End If
        If forRun AndAlso issues(0).Code = ScheduledIssueCode.ResourcePinLocked Then
            Return Stopping(ScheduledRunOutcome.Wall, "pin-locked|" & issues(0).Detail, ScheduledHalt.Wall)
        End If
        Dim refused As Resolution = Stopping(ScheduledRunOutcome.Refused, "issue-" & issues(0).Code.ToString() & "|" & issues(0).Detail)
        refused.StopResult.Issues = issues
        Return refused
    End Function

    Private Shared Function FromProblem(problem As SideProblem, which As String) As Resolution
        Select Case problem.Kind
            Case SideProblemKind.ResourceRemoved
                ' Not a wall: the operation is halted until the user re-points it (4.3).
                Return Stopping(ScheduledRunOutcome.Refused, "resource-removed|" & problem.Detail, ScheduledHalt.ResourceRemoved)
            Case SideProblemKind.Offline, SideProblemKind.DriveUnavailable
                ' An offline target or network skips the run without halting the operation (ticket 231, 3.5).
                Return Stopping(ScheduledRunOutcome.Skipped, which & "-" & ProblemCode(problem.Kind) & "|" & problem.Detail, ScheduledHalt.None)
            Case Else
                ' "source-unreachable|D:\Photos" - a code and the place; the words come at display time.
                Return Stopping(ScheduledRunOutcome.Wall, which & "-" & ProblemCode(problem.Kind) & "|" & problem.Detail, ScheduledHalt.Wall)
        End Select
    End Function

    Friend Shared Function ProblemCode(kind As SideProblemKind) As String
        Select Case kind
            Case SideProblemKind.Denied : Return "denied"
            Case SideProblemKind.NoCredential : Return "no-credential"
            Case SideProblemKind.KeyNotPinned : Return "key-not-pinned"
            Case SideProblemKind.DeviceRevoked : Return "device-revoked"
            Case SideProblemKind.ExchangeCertificateChanged : Return "exchange-certificate"
            Case SideProblemKind.SignedOut : Return "signed-out"
            Case SideProblemKind.PinLocked : Return "pin-locked"
            Case SideProblemKind.Offline : Return "offline"
            Case SideProblemKind.DriveFull : Return "drive-full"
            Case SideProblemKind.DriveUnavailable : Return "drive-unavailable"
            Case Else : Return "unreachable"
        End Select
    End Function

    Private Shared Function IsDriveSide(side As IScheduledSide, location As ScheduledLocation) As Boolean
        If location IsNot Nothing AndAlso location.Kind = ScheduledLocationKind.Drive Then Return True
        Dim providerSide As ProviderSide = TryCast(side, ProviderSide)
        Return providerSide IsNot Nothing AndAlso providerSide.IsDrive
    End Function

    ''' <summary>Plans the destination names. Copy and move keep the relative path (4.2) and settle a taken
    ''' name by the operation's conflict mode; delete and archive have no target names.</summary>
    Private Function Plan(op As ScheduledOperation, matched As List(Of SideEntry), destination As IScheduledSide) As List(Of PlannedItem)
        Dim items As New List(Of PlannedItem)(matched.Count)
        Dim claimed As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each entry As SideEntry In matched
            Dim item As New PlannedItem With {.Entry = entry, .TargetRelative = entry.RelativePath}
            If destination IsNot Nothing AndAlso (op.Action = ScheduledAction.Copy OrElse op.Action = ScheduledAction.Move) Then
                Dim separator As Char = If(destination.IsLocalPath, "\"c, "/"c)
                item.TargetRelative = entry.RelativePath.Replace("\"c, separator).Replace("/"c, separator)
                If destination.Exists(item.TargetRelative) OrElse claimed.Contains(item.TargetRelative) Then
                    Select Case op.Conflict
                        Case ScheduledConflict.Skip
                            item.Skip = True
                            item.SkipReason = "exists"
                        Case ScheduledConflict.Overwrite
                            item.Overwrite = True
                        Case ScheduledConflict.KeepBoth
                            Dim slash As Integer = item.TargetRelative.LastIndexOf(separator)
                            Dim folder As String = If(slash >= 0, item.TargetRelative.Substring(0, slash), "")
                            Dim fileName As String = If(slash >= 0, item.TargetRelative.Substring(slash + 1), item.TargetRelative)
                            Dim free As String = FreeNameRule.Find(folder, fileName,
                                Function(dir As String, name As String) If(dir.Length = 0, name, dir & separator & name),
                                Function(candidate As String) destination.Exists(candidate), claimed)
                            If free Is Nothing Then
                                item.Skip = True
                                item.SkipReason = "no free name"
                            Else
                                item.TargetRelative = free
                                item.Renamed = True
                            End If
                    End Select
                End If
                If Not item.Skip Then claimed.Add(item.TargetRelative)
            End If
            items.Add(item)
        Next
        Return items
    End Function

    ' --- copy and move ----------------------------------------------------------------------------

    Private Sub ExecuteTransfers(op As ScheduledOperation, resolved As Resolution, result As ScheduledRunResult, ct As CancellationToken)
        Dim packets As IScheduledPacketTarget = TryCast(resolved.Destination, IScheduledPacketTarget)
        If packets IsNot Nothing Then
            ExecutePacketTransfers(op, packets, resolved, result, ct)
            Return
        End If

        ' Ticket 231, 3.2: when "via Google Drive" is the source, the picked-up item is removed from the
        ' exchange channel on Drive once the destination copy is verified (Drive is a channel, not a second copy).
        Dim moving As Boolean = op.Action = ScheduledAction.Move OrElse IsDriveSide(resolved.Source, op.Source)
        Dim transfer As IScheduledTransfer = services.TransferFor.Invoke(resolved.Source, resolved.Destination)
        For Each item As PlannedItem In resolved.Items
            If item.Skip Then
                result.Skipped += 1
                Continue For
            End If
            ct.ThrowIfCancellationRequested()
            Try
                services.BeforeItem?.Invoke(item.Entry.RelativePath)
                If moving Then
                    Dim outcome As MoveOutcome = transfer.MoveFile(resolved.Source, item.Entry, resolved.Destination,
                                                                   item.TargetRelative, item.Overwrite, ct)
                    Select Case outcome
                        Case MoveOutcome.Moved
                            result.Files += 1
                            result.Bytes += item.Entry.Length
                            Journal(op, If(op.Action = ScheduledAction.Move AndAlso Not IsDriveSide(resolved.Source, op.Source), "move", "drive-pickup"),
                                    JournalPlace(resolved.Source, item.Entry.RelativePath),
                                    resolved.Destination.Combine(item.TargetRelative), "")
                        Case MoveOutcome.SourceChanged
                            ' Not an error: the file is still where it was and the next run sees it as it is.
                            result.Skipped += 1
                        Case MoveOutcome.CopiedSourceKept
                            Fail(result, item, "kept-both")
                    End Select
                Else
                    Dim written As Long = transfer.CopyFile(resolved.Source, item.Entry, resolved.Destination,
                                                            item.TargetRelative, item.Overwrite, ct)
                    result.Files += 1
                    result.Bytes += written
                End If
            Catch ex As OperationCanceledException
                Throw
            Catch ex As SourceChangedException
                result.Skipped += 1
            Catch ex As Exception
                AppFileLogger.LogException("Scheduled operations: " & item.Entry.RelativePath, ex)
                Dim lost As String = LostSide(ex, resolved)
                If lost.Length > 0 Then
                    HitWall(result, ScheduledFailure.CodeOf(ex) & "|" & lost)
                    Return
                End If
                Fail(result, item, ScheduledFailure.PerFileCode(ex))
            End Try
        Next
    End Sub

    ''' <summary>The destination takes whole packets (the Drive channel, item CO). A packet names its files by base name, so the source's
    ''' folders flatten and a repeated name starts the next packet; a packet holds at most what the target allows. A file of the person's
    ''' is touched only after the packet carrying it landed.</summary>
    Private Sub ExecutePacketTransfers(op As ScheduledOperation, target As IScheduledPacketTarget, resolved As Resolution, result As ScheduledRunResult, ct As CancellationToken)
        Dim batch As New List(Of PlannedItem)()
        Dim names As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim batchBytes As Long = 0
        For Each item As PlannedItem In resolved.Items
            If item.Skip Then
                result.Skipped += 1
                Continue For
            End If
            ct.ThrowIfCancellationRequested()
            services.BeforeItem?.Invoke(item.Entry.RelativePath)
            If Not target.AcceptsName(item.Entry.Name) Then
                Fail(result, item, "invalid")
                Continue For
            End If
            If item.Entry.Length > target.MaxPacketBytes Then
                Fail(result, item, "too-large")
                Continue For
            End If
            If batch.Count > 0 AndAlso (names.Contains(item.Entry.Name) OrElse batch.Count >= target.MaxPacketFiles OrElse
                                        batchBytes + item.Entry.Length > target.MaxPacketBytes) Then
                SendPacketBatch(op, target, batch, resolved, result, ct)
                batch.Clear()
                names.Clear()
                batchBytes = 0
            End If
            batch.Add(item)
            names.Add(item.Entry.Name)
            batchBytes += item.Entry.Length
        Next
        If batch.Count > 0 Then SendPacketBatch(op, target, batch, resolved, result, ct)
    End Sub

    Private Sub SendPacketBatch(op As ScheduledOperation, target As IScheduledPacketTarget, batch As List(Of PlannedItem), resolved As Resolution,
                                result As ScheduledRunResult, ct As CancellationToken)
        Dim source As IScheduledSide = resolved.Source
        Dim scratch As String = ""
        Try
            Dim going As New List(Of PlannedItem)(batch.Count)
            Dim files As New List(Of ScheduledPacketFile)(batch.Count)
            For Each item As PlannedItem In batch
                ct.ThrowIfCancellationRequested()
                Try
                    Dim path As String
                    If source.IsLocalPath Then
                        path = source.Combine(item.Entry.RelativePath)
                        If Not LocalTransfer.SameAsListed(path, item.Entry) Then
                            result.Skipped += 1
                            Continue For
                        End If
                        ' Opened now, so that a locked file is this file's failure and not the whole packet's.
                        Using probe As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete, 1)
                        End Using
                    Else
                        If scratch.Length = 0 Then
                            scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fms-scheduled-packet-" & Guid.NewGuid().ToString("N"))
                            Directory.CreateDirectory(scratch)
                        End If
                        Dim local As New LocalSide(scratch)
                        services.TransferFor.Invoke(source, local).CopyFile(source, item.Entry, local, item.Entry.Name, False, ct)
                        path = local.Combine(item.Entry.Name)
                    End If
                    files.Add(New ScheduledPacketFile(item.Entry.Name, path))
                    going.Add(item)
                Catch ex As OperationCanceledException
                    Throw
                Catch ex As SourceChangedException
                    result.Skipped += 1
                Catch ex As Exception
                    AppFileLogger.LogException("Scheduled operations: " & item.Entry.RelativePath, ex)
                    Fail(result, item, ScheduledFailure.PerFileCode(ex))
                End Try
            Next
            If files.Count = 0 Then Return

            Try
                target.SendPacket(files, ct)
            Catch ex As Exception When Not (TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is ScheduledSideException)
                AppFileLogger.LogException("Scheduled operations: a packet of " & files.Count.ToString(Globalization.CultureInfo.InvariantCulture) & " files", ex)
                For Each item As PlannedItem In going
                    Fail(result, item, ScheduledFailure.PerFileCode(ex))
                Next
                Return
            End Try

            For Each item As PlannedItem In going
                If op.Action = ScheduledAction.Move AndAlso Not RemoveSent(op, item, resolved, result, ct) Then Continue For
                result.Files += 1
                result.Bytes += item.Entry.Length
            Next
        Finally
            If scratch.Length > 0 Then
                Try
                    Directory.Delete(scratch, True)
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                    AppFileLogger.LogException("Scheduled operations: removing a packet's temporary folder", ex)
                End Try
            End If
        End Try
    End Sub

    ''' <summary>The packet landed, so the original of a move goes - when it is still the file that was sent. If not, both stay and the run says so.</summary>
    Private Function RemoveSent(op As ScheduledOperation, item As PlannedItem, resolved As Resolution, result As ScheduledRunResult, ct As CancellationToken) As Boolean
        Dim source As IScheduledSide = resolved.Source
        Dim place As String = source.Combine(item.Entry.RelativePath)
        Try
            Dim verdict As String
            If source.IsLocalPath Then
                If Not LocalTransfer.SameAsListed(place, item.Entry) Then Throw New SourceChangedException(place)
                verdict = DeleteOne(place, item.Entry.Length)
            Else
                verdict = services.TransferFor.Invoke(source, source).DeleteFile(source, item.Entry, ct)
            End If
            Journal(op, "move", place, resolved.Destination.Describe() & ": " & item.Entry.Name, verdict)
            Return True
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: " & item.Entry.RelativePath, ex)
            Fail(result, item, "kept-both")
            Return False
        End Try
    End Function

    ''' <summary>The journal's word for a file of a side: a Drive source's staged copy is a temporary path nobody can use.</summary>
    Private Shared Function JournalPlace(side As IScheduledSide, relative As String) As String
        If TypeOf side Is DriveScheduledSide Then Return side.Describe() & ": " & relative
        Return side.Combine(relative)
    End Function

    ''' <summary>
    ''' Is this failure a LOST side rather than one bad file? A disk that is full is the destination's, whatever
    ''' file met it. An error that merely looks like a broken link (an invalid name raises one too) is only believed
    ''' once the sides are asked again: a side that still answers means the error was about the file. Returns the
    ''' place that was lost, or "" when it was the file.
    ''' </summary>
    Private Shared Function LostSide(ex As Exception, resolved As Resolution) As String
        Dim code As String = ScheduledFailure.CodeOf(ex)
        If code = "disk-full" Then
            Return If(resolved.Destination IsNot Nothing, resolved.Destination.Describe(), resolved.Source.Describe())
        End If
        If code <> "transport" Then Return ""
        If resolved.Source.CheckReadable().IsProblem Then Return resolved.Source.Describe()
        If resolved.Destination IsNot Nothing AndAlso resolved.Destination.CheckWritable().IsProblem Then Return resolved.Destination.Describe()
        Return ""
    End Function

    Private Shared Sub HitWall(result As ScheduledRunResult, reason As String)
        result.Outcome = ScheduledRunOutcome.Wall
        result.Halt = ScheduledHalt.Wall
        result.Detail = reason
    End Sub

    Private Shared Sub Fail(result As ScheduledRunResult, item As PlannedItem, code As String)
        result.Failed += 1
        If result.FirstError.Length = 0 Then result.FirstError = ScheduledFailure.Coded(code, item.Entry.RelativePath)
    End Sub

    ' --- delete -----------------------------------------------------------------------------------

    ''' <summary>
    ''' What a deletion in this location does, by the viewer's own rule (D3): a bin where one exists and
    ''' the global setting is on, otherwise permanent. The editor shows it read-only; the run records
    ''' what it actually did on every log line.
    ''' </summary>
    Friend Function DeleteVerdictFor(location As ScheduledLocation, fileSizeBytes As Long) As DeleteDecision
        Dim binOn As Boolean = services.DeleteToRecycleBin.Invoke()
        If location.Kind = ScheduledLocationKind.Resource OrElse location.Kind = ScheduledLocationKind.Drive Then
            ' There is no SFTP bin and none is claimed (4.3).
            Return DeletePolicy.DecideForProvider(New ProviderCapabilities(hasRecycleBin:=False), Nothing, fileSizeBytes, binOn, False)
        End If
        Dim facts As DeleteVolumeFacts = services.VolumeFacts.Invoke(CompanionCommand.CanonicalFolder(location.Path))
        Return DeletePolicy.Decide(facts, fileSizeBytes, binOn, False)
    End Function

    Private Sub ExecuteDeletes(op As ScheduledOperation, resolved As Resolution, result As ScheduledRunResult, ct As CancellationToken)
        Dim tally As New VerdictTally()
        For Each item As PlannedItem In resolved.Items
            ct.ThrowIfCancellationRequested()
            Try
                services.BeforeItem?.Invoke(item.Entry.RelativePath)
                Dim path As String = resolved.Source.Combine(item.Entry.RelativePath)
                Dim verdict As String
                If resolved.Source.IsLocalPath Then
                    If Not LocalTransfer.SameAsListed(path, item.Entry) Then
                        ' It changed after the listing: not the file the operation selected. Left alone.
                        result.Skipped += 1
                        Continue For
                    End If
                    verdict = DeleteOne(path, item.Entry.Length)
                Else
                    ' A resource: no bin exists there; the viewer's checked, permanent removal (D3, 4.3).
                    verdict = services.TransferFor.Invoke(resolved.Source, resolved.Source).DeleteFile(resolved.Source, item.Entry, ct)
                End If
                tally.Add(verdict)
                result.Files += 1
                result.Bytes += item.Entry.Length
                Journal(op, "delete", path, "", verdict)
            Catch ex As OperationCanceledException
                Throw
            Catch ex As SourceChangedException
                result.Skipped += 1
            Catch ex As Exception
                AppFileLogger.LogException("Scheduled operations: " & item.Entry.RelativePath, ex)
                Dim lost As String = LostSide(ex, resolved)
                If lost.Length > 0 Then
                    HitWall(result, ScheduledFailure.CodeOf(ex) & "|" & lost)
                    Exit For
                End If
                Fail(result, item, ScheduledFailure.PerFileCode(ex))
            End Try
        Next
        result.Verdict = tally.Summary()
    End Sub

    ''' <summary>Deletes one file as the policy says and returns what really happened, as a verdict text.</summary>
    Private Function DeleteOne(path As String, length As Long) As String
        Dim facts As DeleteVolumeFacts = services.VolumeFacts.Invoke(path)
        Dim decision As DeleteDecision = DeletePolicy.Decide(facts, length, services.DeleteToRecycleBin.Invoke(), False)
        If decision.Outcome = DeleteOutcome.Recycle Then
            Dim hardDeleted As Boolean = services.SendToBin.Invoke(path)
            ' The shell deleted it for good although a bin was promised (a subst drive, a mounted folder):
            ' the record says what happened, not what was predicted.
            Return If(hardDeleted, "permanent:BinDidNotTakeIt", "recycle")
        End If
        services.DeletePermanently.Invoke(path)
        Return "permanent:" & decision.Reason.ToString()
    End Function

    ''' <summary>"recycle" when every file went to the bin, "permanent:&lt;reason&gt;" when every file went
    ''' the same way, "mixed" otherwise.</summary>
    Private NotInheritable Class VerdictTally
        Private ReadOnly seen As New HashSet(Of String)()
        Friend Sub Add(verdict As String)
            seen.Add(verdict)
        End Sub
        Friend Function Summary() As String
            If seen.Count = 0 Then Return ""
            If seen.Count = 1 Then Return seen.First()
            Return "mixed"
        End Function
    End Class

    ' --- archive ----------------------------------------------------------------------------------

    Private Sub ExecuteArchive(op As ScheduledOperation, resolved As Resolution, result As ScheduledRunResult, ct As CancellationToken)
        Dim entries As List(Of SideEntry) = resolved.Items.Select(Function(i As PlannedItem) i.Entry).ToList()
        If entries.Count = 0 Then Return

        Dim archiveName As String = ScheduledArchiveWriter.ArchiveFileName(resolved.Source.Root, result.StartedAt)
        Dim finalPath As String = resolved.Destination.Combine(archiveName)
        Dim remoteDest As Boolean = Not resolved.Destination.IsLocalPath
        Dim written As ArchiveWriteResult = ScheduledArchiveWriter.Write(resolved.Source, entries, finalPath, ct,
            resolved.Destination, If(remoteDest, services.AfterArchiveWritten, Nothing))
        result.ArchivePath = written.ArchivePath
        result.Files = written.Written.Count
        result.Bytes = written.Bytes
        For Each failure As KeyValuePair(Of SideEntry, String) In written.Failed
            result.Failed += 1
            If result.FirstError.Length = 0 Then result.FirstError = ScheduledFailure.Coded(failure.Value, failure.Key.RelativePath)
        Next

        If Not remoteDest Then services.AfterArchiveWritten?.Invoke(written.ArchivePath)
        Dim removeSource As Boolean = op.DeleteOriginalsAfterVerify OrElse IsDriveSide(resolved.Source, op.Source)
        If Not removeSource OrElse written.ArchivePath.Length = 0 Then Return

        ' D4: the archive is read back and proved BEFORE a single original is touched. A failed proof
        ' keeps every original and the archive, and reports. (For a remote destination, Write verified
        ' the local zip before uploading and ProviderTransfer verified the uploaded bytes.)
        If Not remoteDest Then
            Dim problem As String = ScheduledArchiveWriter.Verify(written.ArchivePath, written.Written, ct)
            If problem.Length > 0 Then
                result.Failed += 1
                If result.FirstError.Length = 0 Then result.FirstError = ScheduledFailure.Coded("archive-unverified", problem)
                Return
            End If
        End If

        Dim tally As New VerdictTally()
        For Each record As ArchiveEntryRecord In written.Written
            ct.ThrowIfCancellationRequested()
            If Not record.Stable Then Continue For
            Dim path As String = resolved.Source.Combine(record.Entry.RelativePath)
            Try
                Dim verdict As String
                If resolved.Source.IsLocalPath Then
                    ' The original must still be the file that was archived.
                    If Not LocalTransfer.SameAsListed(path, record.Entry) Then Continue For
                    verdict = DeleteOne(path, record.Entry.Length)
                Else
                    verdict = services.TransferFor.Invoke(resolved.Source, resolved.Source).DeleteFile(resolved.Source, record.Entry, ct)
                End If
                tally.Add(verdict)
                Journal(op, "archive-delete", JournalPlace(resolved.Source, record.Entry.RelativePath), written.ArchivePath, verdict)
            Catch ex As OperationCanceledException
                Throw
            Catch ex As SourceChangedException
                Continue For
            Catch ex As Exception
                AppFileLogger.LogException("Scheduled operations: " & record.Entry.RelativePath, ex)
                result.Failed += 1
                If result.FirstError.Length = 0 Then result.FirstError = ScheduledFailure.Coded(ScheduledFailure.CodeOf(ex), record.Entry.RelativePath)
            End Try
        Next
        result.Verdict = tally.Summary()
    End Sub

    ' --- fill free space (ticket L0299) -------------------------------------------------------------

    ''' <summary>
    ''' FileDO's `fill &lt;MB&gt; del` in a work folder this run creates and owns (Q299-4). Earlier work folders of this
    ''' operation are cleared first; one that cannot be cleared stops the operation as a wall, because its files still
    ''' hold the space. The verdict is the event file's (CLI-EVENT-STREAM rules 10, 11 and 15), never the exit code alone.
    ''' </summary>
    Private Sub ExecuteFill(op As ScheduledOperation, result As ScheduledRunResult, ct As CancellationToken)
        Dim place As String = CompanionCommand.CanonicalFolder(op.Source.Path)
        Dim volume As FillVolumeInfo = services.FillVolume.Invoke(place)
        If volume.IsNetwork Then
            HitWall(result, "fill-network-drive|" & place)
            Return
        End If
        If Not Directory.Exists(place) Then
            HitWall(result, "source-unreachable|" & place)
            Return
        End If
        Dim exe As String = services.LocateFileDo.Invoke()
        If exe.Length = 0 Then
            HitWall(result, "filedo-missing")
            Return
        End If
        Dim left As List(Of String) = FillWorkFolders.RecoverEarlier(place, op.Id)
        If left.Count > 0 Then
            HitWall(result, "fill-leftovers|" & left(0))
            Return
        End If
        ct.ThrowIfCancellationRequested()

        Dim workFolder As String
        Try
            workFolder = FillWorkFolders.Create(place, op.Id, result.StartedAt)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            AppFileLogger.LogException("Scheduled operations: fill work folder in " & place, ex)
            HitWall(result, If(TypeOf ex Is UnauthorizedAccessException, "source-denied|", "source-unreachable|") & place)
            Return
        End Try

        Dim eventsFile As String = Path.Combine(workFolder, FillWorkFolders.EventsName)
        Dim launch As New FillLaunch With {.Exe = exe, .StopFile = Path.Combine(workFolder, FillWorkFolders.StopName)}
        launch.Arguments = FillCommandLine.Arguments(workFolder, op.ChunkSizeMb, eventsFile, launch.StopFile)
        Dim outcome As FillLaunchOutcome = services.LaunchFill.Invoke(launch, ct)
        Dim events As FillEvents = FillEvents.Parse(ReadEvents(eventsFile))
        Dim judged As FillJudgement = events.Judge(outcome.ExitCode, outcome.StopRequested)
        Dim cleared As Boolean = FillWorkFolders.Clear(workFolder, op.Id)

        result.Files = CInt(Math.Min(Integer.MaxValue, events.DoneItems))
        result.Bytes = events.DoneBytes
        result.Verdict = FillCommandLine.Verdict(If(volume.Known, volume.FreeBytes, 0L), events.DoneBytes, outcome.ExitCode)
        Journal(op, "fill-free-space", workFolder, "", If(outcome.Started, judged.ToString(), "not-started"))

        If Not cleared Then
            ' The space is still held, and the next run must not stack a second fill on top of it.
            HitWall(result, "fill-leftovers|" & workFolder)
        ElseIf Not outcome.Started Then
            HitWall(result, "filedo-start|" & exe)
        ElseIf judged = FillJudgement.Passed Then
            result.Outcome = ScheduledRunOutcome.Ok
        ElseIf judged = FillJudgement.Stopped AndAlso outcome.StopRequested Then
            result.Outcome = ScheduledRunOutcome.Cancelled
        Else
            result.Outcome = ScheduledRunOutcome.Failed
            result.Failed = 1
            result.FirstError = ScheduledFailure.Coded(If(judged = FillJudgement.Failed, "fill-failed", "fill-not-proven"), place)
        End If
    End Sub

    Private Shared Function ReadEvents(eventsFile As String) As String
        Try
            Using stream As New FileStream(eventsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                Using reader As New StreamReader(stream, System.Text.Encoding.UTF8)
                    Return reader.ReadToEnd()
                End Using
            End Using
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            ' No event file is a run that said nothing: not proven (rule 10).
            Return ""
        End Try
    End Function

    ''' <summary>A walled fill comes back when FileDO is there, the place is a reachable local folder and every earlier
    ''' work folder of the operation can be cleared.</summary>
    Private Function FillPreflight(op As ScheduledOperation) As SideProblem
        Dim place As String = CompanionCommand.CanonicalFolder(op.Source.Path)
        If place.Length = 0 OrElse Not Directory.Exists(place) Then Return New SideProblem(SideProblemKind.Unreachable, op.Source.Describe())
        If services.FillVolume.Invoke(place).IsNetwork Then Return New SideProblem(SideProblemKind.Unreachable, place)
        If services.LocateFileDo.Invoke().Length = 0 Then Return New SideProblem(SideProblemKind.Unreachable, place)
        For Each folder As String In FillWorkFolders.OwnFolders(place, op.Id)
            If Not FillWorkFolders.IsClearable(folder, op.Id) Then Return New SideProblem(SideProblemKind.Unreachable, folder)
        Next
        Return SideProblem.Ok
    End Function

    ' --- backup (ticket L0298) -----------------------------------------------------------------------

    ''' <summary>
    ''' FileDO's `backup` of the source folder into the operation's own generation set. What is checked here holds for every
    ''' run: both folders reachable, FileDO present and able to number generations. The launch and the reading of its
    ''' `result` (generation, retiredGeneration; CLI-EVENT-STREAM 0.17 items D-F) are step P4.1, written against the option
    ''' spelling FileDO publishes; until then a FileDO without the generation set is a wall the operation waits at.
    ''' </summary>
    Private Sub ExecuteBackup(op As ScheduledOperation, result As ScheduledRunResult)
        Dim problem As SideProblem = BackupPreflight(op)
        If problem.IsProblem Then
            HitWall(result, BackupWallCode(op, problem))
            Return
        End If
        HitWall(result, "backup-filedo-unsupported")
    End Sub

    ''' <summary>The wall a backup's preflight problem stands for, in the order the preflight checks them.</summary>
    Private Function BackupWallCode(op As ScheduledOperation, problem As SideProblem) As String
        Dim exe As String = services.LocateFileDo.Invoke()
        If exe.Length = 0 Then Return "filedo-missing"
        If Not services.FileDoHasBackupGenerations.Invoke(exe) Then Return "backup-filedo-unsupported"
        If op.BackupSealed AndAlso services.BackupPassword.Invoke(op.Id).Length = 0 Then Return ShareSettingsTransfer.BackupNoPasswordCode
        Dim source As String = CompanionCommand.CanonicalFolder(op.Source.Path)
        If source.Length = 0 OrElse Not Directory.Exists(source) Then Return "source-unreachable|" & op.Source.Describe()
        Return "destination-unreachable|" & op.Destination.Describe()
    End Function

    ''' <summary>A walled backup comes back when FileDO can number generations, the password is stored and both folders are
    ''' there. Read-only and silent.</summary>
    Private Function BackupPreflight(op As ScheduledOperation) As SideProblem
        Dim exe As String = services.LocateFileDo.Invoke()
        If exe.Length = 0 OrElse Not services.FileDoHasBackupGenerations.Invoke(exe) Then
            Return New SideProblem(SideProblemKind.Unreachable, op.Source.Describe())
        End If
        If op.BackupSealed AndAlso services.BackupPassword.Invoke(op.Id).Length = 0 Then
            Return New SideProblem(SideProblemKind.Unreachable, op.Source.Describe())
        End If
        Dim source As String = CompanionCommand.CanonicalFolder(op.Source.Path)
        If source.Length = 0 OrElse Not Directory.Exists(source) Then Return New SideProblem(SideProblemKind.Unreachable, op.Source.Describe())
        Dim destination As String = CompanionCommand.CanonicalFolder(op.Destination.Path)
        If destination.Length = 0 OrElse Not Directory.Exists(destination) Then Return New SideProblem(SideProblemKind.Unreachable, op.Destination.Describe())
        Return SideProblem.Ok
    End Function

    Private Sub Journal(op As ScheduledOperation, verb As String, source As String, destination As String, verdict As String)
        If services.ItemJournal Is Nothing Then Return
        Try
            services.ItemJournal.Invoke(New ItemJournalEntry With {.Timestamp = services.Clock.Invoke(), .OperationId = op.Id,
                .Verb = verb, .Source = source, .Destination = destination, .Verdict = verdict})
        Catch
        End Try
    End Sub

    ' --- preview and pre-flight -------------------------------------------------------------------

    ''' <summary>"What would run now" (D6): the same resolution as a run, but read-only, capped, and never
    ''' a gate - a run needs no preview.</summary>
    Friend Function Preview(operation As ScheduledOperation, ct As CancellationToken) As ScheduledPreview
        Dim view As New ScheduledPreview()
        Dim op As ScheduledOperation = operation.Clone()
        ScheduledValidation.Normalize(op)
        If op.Action = ScheduledAction.FillFreeSpace OrElse op.Action = ScheduledAction.Backup Then
            ' Nothing is listed: a fill and a backup select no files, and the editor shows its own note instead.
            Dim refusal As Resolution = CheckDefinition(op, forRun:=False)
            If refusal IsNot Nothing Then view.Issues = refusal.StopResult.Issues
            Return view
        End If
        Dim resolved As Resolution
        Try
            resolved = Resolve(op, services.Clock.Invoke(), forRun:=False, ct)
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: preview", ex)
            view.Problem = New SideProblem(SideProblemKind.Unreachable, operation.Source.Describe())
            Return view
        End Try
        If resolved.DriveSource Then
            view.DriveSource = True
            Return view
        End If
        If resolved.Stopped Then
            view.Issues = resolved.StopResult.Issues
            If resolved.StopResult.Outcome = ScheduledRunOutcome.Wall OrElse resolved.StopResult.Halt <> ScheduledHalt.None Then
                view.Problem = New SideProblem(SideProblemKind.Unreachable, resolved.StopResult.Detail)
            End If
            Return view
        End If
        Try
            For Each item As PlannedItem In resolved.Items
                If item.Skip Then
                    view.WouldSkip += 1
                    Continue For
                End If
                view.WouldAct += 1
                view.Bytes += item.Entry.Length
                If view.Lines.Count < PreviewLineCap Then
                    view.Lines.Add(item.Entry.RelativePath & If(item.Renamed, "  ->  " & item.TargetRelative, ""))
                Else
                    view.Truncated = True
                End If
            Next
        Finally
            resolved.Source?.Dispose()
            resolved.Destination?.Dispose()
        End Try
        Return view
    End Function

    ''' <summary>Only the conditions that walled an operation: can the sides be used now? Silent and
    ''' read-only, so a walled operation can come back by itself when its cause is gone.</summary>
    Friend Function Preflight(operation As ScheduledOperation) As SideProblem
        Dim op As ScheduledOperation = operation.Clone()
        ScheduledValidation.Normalize(op)
        If op.Action = ScheduledAction.FillFreeSpace Then Return FillPreflight(op)
        If op.Action = ScheduledAction.Backup Then Return BackupPreflight(op)
        Dim sourceOpen As ScheduledSideOpen = services.OpenSide.Invoke(op.Source)
        If sourceOpen.Problem.IsProblem Then Return sourceOpen.Problem
        Using sourceSide As IScheduledSide = sourceOpen.Side
            Dim readable As SideProblem = sourceSide.CheckReadable()
            If readable.IsProblem Then Return readable
            If op.NeedsDestination Then
                Dim destinationOpen As ScheduledSideOpen = services.OpenSide.Invoke(op.Destination)
                If destinationOpen.Problem.IsProblem Then Return destinationOpen.Problem
                Using destinationSide As IScheduledSide = destinationOpen.Side
                    Return destinationSide.CheckWritable()
                End Using
            End If
            Return SideProblem.Ok
        End Using
    End Function

End Class
