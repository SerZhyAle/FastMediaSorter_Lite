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
        Dim resolved As Resolution = Resolve(op, result.StartedAt, forRun:=True, ct)
        result.SourceDescription = op.Source.Describe()
        result.DestinationDescription = If(op.NeedsDestination, op.Destination.Describe(), "")
        If resolved.Stopped Then
            Adopt(result, resolved.StopResult)
            Return
        End If

        Try
            Select Case op.Action
                Case ScheduledAction.Copy, ScheduledAction.Move
                    ExecuteTransfers(op, resolved, result, ct)
                Case ScheduledAction.Delete
                    ExecuteDeletes(op, resolved, result, ct)
                Case ScheduledAction.Archive
                    ExecuteArchive(op, resolved, result, ct)
            End Select
        Finally
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
        Dim issues As List(Of ScheduledIssue) = ScheduledValidation.Validate(op, services.Protection, services.ResourceFacts)
        If op.Halt = ScheduledHalt.InvalidDefinition Then
            Return Stopping(ScheduledRunOutcome.Refused, "invalid definition: " & op.HaltDetail)
        End If
        If issues.Count > 0 Then
            If forRun AndAlso issues(0).Code = ScheduledIssueCode.ResourcePinLocked Then
                Return Stopping(ScheduledRunOutcome.Wall, "pin-locked|" & issues(0).Detail, ScheduledHalt.Wall)
            End If
            Dim refused As Resolution = Stopping(ScheduledRunOutcome.Refused, "issue-" & issues(0).Code.ToString() & "|" & issues(0).Detail)
            refused.StopResult.Issues = issues
            Return refused
        End If

        ' 2. The source.
        Dim sourceOpen As ScheduledSideOpen = services.OpenSide.Invoke(op.Source)
        If sourceOpen.Problem.IsProblem Then Return FromProblem(sourceOpen.Problem, "source")
        Dim source As IScheduledSide = sourceOpen.Side
        opened.Add(source)
        Dim readable As SideProblem = source.CheckReadable()
        If readable.IsProblem Then Return FromProblem(readable, "source")

        ' 3. Scan, then filter - the whole list, before anything is acted on.
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

    Private Shared Function FromProblem(problem As SideProblem, which As String) As Resolution
        Select Case problem.Kind
            Case SideProblemKind.ResourceRemoved
                ' Not a wall: the operation is halted until the user re-points it (4.3).
                Return Stopping(ScheduledRunOutcome.Refused, "resource-removed|" & problem.Detail, ScheduledHalt.ResourceRemoved)
            Case SideProblemKind.Offline
                ' An offline target or network skips the run without halting the operation (ticket 231, 3.5).
                Return Stopping(ScheduledRunOutcome.Skipped, which & "-offline|" & problem.Detail, ScheduledHalt.None)
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
                            Journal(op, If(op.Action = ScheduledAction.Move, "move", "drive-pickup"),
                                    resolved.Source.Combine(item.Entry.RelativePath),
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
                Journal(op, "archive-delete", path, written.ArchivePath, verdict)
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
