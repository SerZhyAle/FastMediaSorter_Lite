Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 269 P3.1 - the Share Manager answers the REQUEST packets addressed to this PC for a folder it lists on Drive
' (DEVICE-EXCHANGE section 24, items CO, CR, CS). Two halves:
'
'   - DriveRequestPlanner decides the answer from the request and the folder on disk: a refusal with its reason, a listing,
'     or the files split into replies. It reads the disk and nothing else (no Drive, no clock of its own).
'   - DriveRequestProducer carries it out on the packet queue after each poll of the Drive service: the replies, the last one
'     `final`, then the REQUEST deleted (item CR).
'
' Rules held here (cited, not restated):
'   - A folder is served only while this PC lists its record on Drive (item CO): the lookup is the publisher's standing set,
'     so a folder that is no longer shared, or a channel that was signed out, answers `unavailable`.
'   - A PIN-locked root answers `pin-required` and never sends a file (item CO, 9.10).
'   - A path leaves the root neither by its form (ExchangeDriveRequests.IsRelativePath) nor through a link: every component
'     below the root that is a reparse point is `not-permitted`, and a listing or a changes walk never enters one.
'   - A single file above `packetBytes` makes the answer `too-large`; a larger answer is split (item CS). A reply whose upload
'     is refused for space becomes a `drive-full` refusal (item CS). The quota itself is read by the queue before any upload.
'   - No reply after a reply failed: the requester gives up after 120 s and deletes its REQUEST (item CQ). A request whose
'     first reply could not even start for a passing reason (offline, a rate limit that outlasted the back-off) is tried again
'     at the next poll; everything else is answered once per session and the REQUEST expires on its own (900 s).
'   - Log lines name kinds and counts, never a path, a file name or a request body.

''' <summary>A folder this PC lists on Drive, as the producer serves it.</summary>
Friend NotInheritable Class DriveServedRoot

    Public ReadOnly ResourceId As String
    Public ReadOnly HostPath As String
    Public ReadOnly Locked As Boolean

    Public Sub New(resourceId As String, hostPath As String, locked As Boolean)
        Me.ResourceId = If(resourceId, "")
        Me.HostPath = If(hostPath, "")
        Me.Locked = locked
    End Sub

    Public Overrides Function ToString() As String
        Return "DriveServedRoot"
    End Function
End Class

Friend Enum DriveAnswerKind
    ''' <summary>An action this version does not know: no reply, the request is left to expire.</summary>
    Skip
    Refused
    ''' <summary>`list`: one reply carrying `listing.json`.</summary>
    Listing
    ''' <summary>`fetch` or `changes`: one or more replies carrying files.</summary>
    Files
End Enum

''' <summary>What the planner decided.</summary>
Friend NotInheritable Class DriveRequestAnswer

    Public ReadOnly Kind As DriveAnswerKind
    Public ReadOnly Reason As String
    Public ReadOnly AsOf As DateTimeOffset
    Public ReadOnly ListingBody As Byte()
    ''' <summary>The files of each reply, in order; the last reply is the final one.</summary>
    Public ReadOnly Replies As IReadOnlyList(Of IReadOnlyList(Of ExchangeDriveAnswerFile))
    ''' <summary>The resource root on disk, for the full paths of <see cref="Replies"/>.</summary>
    Public ReadOnly RootPath As String

    Private Sub New(kind As DriveAnswerKind, reason As String, asOf As DateTimeOffset, listingBody As Byte(),
                    replies As IReadOnlyList(Of IReadOnlyList(Of ExchangeDriveAnswerFile)), rootPath As String)
        Me.Kind = kind
        Me.Reason = If(reason, "")
        Me.AsOf = asOf
        Me.ListingBody = listingBody
        Me.Replies = If(replies, Array.Empty(Of IReadOnlyList(Of ExchangeDriveAnswerFile))())
        Me.RootPath = If(rootPath, "")
    End Sub

    Public Shared ReadOnly Skipped As New DriveRequestAnswer(DriveAnswerKind.Skip, "", DateTimeOffset.MinValue, Nothing, Nothing, "")

    Public Shared Function Refuse(reason As String) As DriveRequestAnswer
        Return New DriveRequestAnswer(DriveAnswerKind.Refused, reason, DateTimeOffset.MinValue, Nothing, Nothing, "")
    End Function

    Public Shared Function Listed(asOf As DateTimeOffset, body As Byte()) As DriveRequestAnswer
        Return New DriveRequestAnswer(DriveAnswerKind.Listing, "", asOf, body, Nothing, "")
    End Function

    Public Shared Function WithFiles(asOf As DateTimeOffset, replies As IReadOnlyList(Of IReadOnlyList(Of ExchangeDriveAnswerFile)), rootPath As String) As DriveRequestAnswer
        Return New DriveRequestAnswer(DriveAnswerKind.Files, "", asOf, Nothing, replies, rootPath)
    End Function

    ''' <summary>The full path on disk of one planned file.</summary>
    Public Function FullPathOf(file As ExchangeDriveAnswerFile) As String
        Return DriveRequestPlanner.Combine(RootPath, file.RelativePath)
    End Function

    Public Overrides Function ToString() As String
        Return "DriveRequestAnswer(" & Kind.ToString() & ")"
    End Function
End Class

Friend Enum DrivePathResolution
    Found
    ''' <summary>The path does not exist, or is not the kind of entry the request needs.</summary>
    Missing
    ''' <summary>The path leaves the root (a link on the way).</summary>
    NotPermitted
End Enum

Friend NotInheritable Class DriveRequestPlanner

    Private Sub New()
    End Sub

    ''' <summary>The answer to <paramref name="request"/> for the folder <paramref name="root"/> (Nothing: not served).</summary>
    ''' <param name="now">The producer's clock: the `asOf` of the answer, taken BEFORE the folder is read, so a file changed while
    ''' it is read comes again in the next `changes` instead of never.</param>
    Public Shared Function Plan(request As ExchangeDriveRequest, root As DriveServedRoot, now As DateTimeOffset, packetBytes As Long) As DriveRequestAnswer
        If request Is Nothing OrElse request.Action = ExchangeDriveRequestAction.Unknown Then Return DriveRequestAnswer.Skipped
        If root Is Nothing OrElse root.HostPath.Length = 0 Then Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonUnavailable)
        If root.Locked Then Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonPinRequired)

        Dim rootPath As String
        Try
            rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.HostPath))
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException OrElse TypeOf ex Is PathTooLongException
            Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonUnavailable)
        End Try
        If Not Directory.Exists(rootPath) Then Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonUnavailable)
        Dim limit As Long = If(packetBytes > 0, packetBytes, ExchangeDrivePackets.DefaultPacketBytes)

        Select Case request.Action
            Case ExchangeDriveRequestAction.List
                Return PlanList(request, rootPath, now, limit)
            Case ExchangeDriveRequestAction.Fetch
                Return PlanFetch(request, rootPath, now, limit)
            Case Else
                Return PlanChanges(request, rootPath, now, limit)
        End Select
    End Function

    Private Shared Function PlanList(request As ExchangeDriveRequest, rootPath As String, now As DateTimeOffset, limit As Long) As DriveRequestAnswer
        If Not ExchangeDriveRequests.IsRelativePath(request.Path) Then Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonNotPermitted)
        Dim folder As String = ""
        Select Case Resolve(rootPath, request.Path, True, folder)
            Case DrivePathResolution.NotPermitted : Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonNotPermitted)
            Case DrivePathResolution.Missing : Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonUnavailable)
        End Select

        Dim entries As New List(Of ExchangeDriveListingEntry)
        Try
            For Each info As FileSystemInfo In New DirectoryInfo(folder).EnumerateFileSystemInfos()
                If (info.Attributes And FileAttributes.ReparsePoint) <> 0 Then Continue For
                If Not ExchangeDriveRequests.IsRelativePath(info.Name) Then Continue For
                Dim isDir As Boolean = (info.Attributes And FileAttributes.Directory) <> 0
                Dim size As Long = If(isDir, 0, DirectCast(info, FileInfo).Length)
                entries.Add(New ExchangeDriveListingEntry(info.Name, isDir, size, New DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)))
            Next
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is System.Security.SecurityException
            Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonUnavailable)
        End Try
        entries.Sort(Function(a As ExchangeDriveListingEntry, b As ExchangeDriveListingEntry) String.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase))

        Dim body As Byte() = ExchangeDriveRequests.BuildListing(now, entries)
        If body.LongLength > Math.Min(limit, CLng(ExchangeDriveRequests.MaxListingBytes)) Then Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonTooLarge)
        Return DriveRequestAnswer.Listed(now, body)
    End Function

    Private Shared Function PlanFetch(request As ExchangeDriveRequest, rootPath As String, now As DateTimeOffset, limit As Long) As DriveRequestAnswer
        ' an empty set or more than 100 paths is outside what a request may ask (item CO)
        If request.Paths.Count = 0 OrElse request.Paths.Count > ExchangeDriveRequests.MaxFetchPaths Then Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonNotPermitted)
        Dim files As New List(Of ExchangeDriveAnswerFile)
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each relative As String In request.Paths
            If relative.Length = 0 OrElse Not ExchangeDriveRequests.IsRelativePath(relative) Then Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonNotPermitted)
            Dim full As String = ""
            Select Case Resolve(rootPath, relative, False, full)
                Case DrivePathResolution.NotPermitted : Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonNotPermitted)
                Case DrivePathResolution.Missing : Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonUnavailable)
            End Select
            Dim file As New ExchangeDriveAnswerFile(relative, 0)
            ' a file whose base name a packet cannot carry (`manifest.json`, item CN) cannot be sent
            If Not ExchangeDrivePackets.IsValidPayloadName(file.Name) Then Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonUnavailable)
            If Not seen.Add(relative) Then Continue For
            Try
                files.Add(New ExchangeDriveAnswerFile(relative, New FileInfo(full).Length))
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonUnavailable)
            End Try
        Next
        Return Split(files, now, rootPath, limit)
    End Function

    Private Shared Function PlanChanges(request As ExchangeDriveRequest, rootPath As String, now As DateTimeOffset, limit As Long) As DriveRequestAnswer
        If Not ExchangeDriveRequests.IsRelativePath(request.Path) Then Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonNotPermitted)
        Dim folder As String = ""
        Select Case Resolve(rootPath, request.Path, True, folder)
            Case DrivePathResolution.NotPermitted : Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonNotPermitted)
            Case DrivePathResolution.Missing : Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonUnavailable)
        End Select

        Dim files As New List(Of ExchangeDriveAnswerFile)
        Dim pending As New Stack(Of KeyValuePair(Of String, String))
        pending.Push(New KeyValuePair(Of String, String)(folder, request.Path))
        Try
            While pending.Count > 0
                Dim current As KeyValuePair(Of String, String) = pending.Pop()
                For Each info As FileSystemInfo In New DirectoryInfo(current.Key).EnumerateFileSystemInfos()
                    If (info.Attributes And FileAttributes.ReparsePoint) <> 0 Then Continue For
                    If Not ExchangeDriveRequests.IsRelativePath(info.Name) Then Continue For
                    Dim relative As String = ExchangeDriveRequests.Join(current.Value, info.Name)
                    If (info.Attributes And FileAttributes.Directory) <> 0 Then
                        pending.Push(New KeyValuePair(Of String, String)(info.FullName, relative))
                        Continue For
                    End If
                    If Not ExchangeDrivePackets.IsValidPayloadName(info.Name) Then Continue For
                    If request.HasModifiedAfter AndAlso info.LastWriteTimeUtc <= request.ModifiedAfter.UtcDateTime Then Continue For
                    files.Add(New ExchangeDriveAnswerFile(relative, DirectCast(info, FileInfo).Length))
                Next
            End While
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is System.Security.SecurityException
            Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonUnavailable)
        End Try
        files.Sort(Function(a As ExchangeDriveAnswerFile, b As ExchangeDriveAnswerFile) String.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase))
        Return Split(files, now, rootPath, limit)
    End Function

    Private Shared Function Split(files As List(Of ExchangeDriveAnswerFile), now As DateTimeOffset, rootPath As String, limit As Long) As DriveRequestAnswer
        Dim replies As List(Of List(Of ExchangeDriveAnswerFile)) = ExchangeDriveRequests.SplitReplies(files, limit)
        If replies Is Nothing Then Return DriveRequestAnswer.Refuse(ExchangeDriveRequests.ReasonTooLarge)
        Return DriveRequestAnswer.WithFiles(now, replies.Select(Function(r) CType(r, IReadOnlyList(Of ExchangeDriveAnswerFile))).ToList(), rootPath)
    End Function

    ''' <summary>Walks <paramref name="relative"/> down from the root, one component at a time: a component that is a reparse
    ''' point (a link, a junction, a mount) is <see cref="DrivePathResolution.NotPermitted"/>; one that is missing, or the last one
    ''' not being a folder (<paramref name="wantFolder"/>) or a file, is <see cref="DrivePathResolution.Missing"/>. The result is
    ''' checked once more to lie under the root.</summary>
    Public Shared Function Resolve(rootPath As String, relative As String, wantFolder As Boolean, ByRef full As String) As DrivePathResolution
        full = ""
        If Not ExchangeDriveRequests.IsRelativePath(relative) Then Return DrivePathResolution.NotPermitted
        Dim segments As String() = ExchangeDriveRequests.Segments(relative)
        Dim current As String = rootPath
        For index As Integer = 0 To segments.Length - 1
            current = Path.Combine(current, segments(index))
            Dim attributes As FileAttributes
            Try
                attributes = File.GetAttributes(current)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException
                Return DrivePathResolution.Missing
            End Try
            If (attributes And FileAttributes.ReparsePoint) <> 0 Then Return DrivePathResolution.NotPermitted
            Dim isFolder As Boolean = (attributes And FileAttributes.Directory) <> 0
            Dim last As Boolean = (index = segments.Length - 1)
            If Not last AndAlso Not isFolder Then Return DrivePathResolution.Missing
            If last AndAlso isFolder <> wantFolder Then Return DrivePathResolution.Missing
        Next
        If segments.Length = 0 AndAlso Not wantFolder Then Return DrivePathResolution.Missing

        Dim resolved As String
        Try
            resolved = Path.GetFullPath(current)
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException OrElse TypeOf ex Is PathTooLongException
            Return DrivePathResolution.NotPermitted
        End Try
        If Not IsUnder(rootPath, resolved) Then Return DrivePathResolution.NotPermitted
        full = resolved
        Return DrivePathResolution.Found
    End Function

    Private Shared Function IsUnder(rootPath As String, candidate As String) As Boolean
        If String.Equals(candidate, rootPath, StringComparison.OrdinalIgnoreCase) Then Return True
        ' a drive root ("D:\") already ends in the separator
        Dim prefix As String = If(Path.EndsInDirectorySeparator(rootPath), rootPath, rootPath & Path.DirectorySeparatorChar)
        Return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>The full path of a relative path the planner already resolved.</summary>
    Public Shared Function Combine(rootPath As String, relative As String) As String
        Dim result As String = rootPath
        For Each segment As String In ExchangeDriveRequests.Segments(relative)
            result = Path.Combine(result, segment)
        Next
        Return result
    End Function

End Class

''' <summary>Answers the REQUESTs addressed to this PC after each poll of the Drive service. One pass at a time; a poll that
''' arrives while a pass runs is picked up by the next one (the service polls again within its interval).</summary>
Friend NotInheritable Class DriveRequestProducer
    Implements IDisposable

    Private ReadOnly _queue As ExchangeDriveQueue
    Private ReadOnly _findRoot As Func(Of String, DriveServedRoot)
    Private ReadOnly _utcNow As Func(Of DateTimeOffset)
    Private ReadOnly _packetBytes As Long
    Private ReadOnly _log As Action(Of String)
    Private ReadOnly _gate As New Object()
    ' request ids this session answered or decided to leave alone; touched under _gate
    Private ReadOnly _answered As New HashSet(Of String)(StringComparer.Ordinal)
    Private ReadOnly _cts As New CancellationTokenSource()
    Private _running As Task = Task.CompletedTask
    Private _busy As Boolean
    Private _stopped As Boolean

    ''' <param name="queue">The packet queue of the Drive service.</param>
    ''' <param name="findRoot">The folder this PC lists on Drive under a resource id, or Nothing (the publisher's standing set).</param>
    Public Sub New(queue As ExchangeDriveQueue, findRoot As Func(Of String, DriveServedRoot), Optional utcNow As Func(Of DateTimeOffset) = Nothing,
                   Optional packetBytes As Long = ExchangeDrivePackets.DefaultPacketBytes, Optional log As Action(Of String) = Nothing)
        If queue Is Nothing Then Throw New ArgumentNullException(NameOf(queue))
        If findRoot Is Nothing Then Throw New ArgumentNullException(NameOf(findRoot))
        _queue = queue
        _findRoot = findRoot
        _utcNow = If(utcNow, Function() DateTimeOffset.UtcNow)
        _packetBytes = If(packetBytes > 0, packetBytes, ExchangeDrivePackets.DefaultPacketBytes)
        _log = If(log, Sub(line As String) AppFileLogger.WriteLine(line))
    End Sub

    ''' <summary>The service's Updated event: starts a pass over the poll unless one runs. Returns at once.</summary>
    Public Sub OnUpdated(update As ExchangeDriveUpdate)
        If update Is Nothing OrElse update.Poll Is Nothing Then Return
        If _queue.RequestsForThisDevice(update.Poll).Count = 0 Then Return
        SyncLock _gate
            If _stopped OrElse _busy Then Return
            _busy = True
            Dim token As CancellationToken = _cts.Token
            _running = Task.Run(Async Function() As Task
                                    Try
                                        Await AnswerAsync(update.Poll, token).ConfigureAwait(False)
                                    Catch ex As OperationCanceledException
                                    Catch ex As Exception When Not (TypeOf ex Is OutOfMemoryException OrElse TypeOf ex Is StackOverflowException OrElse TypeOf ex Is AccessViolationException)
                                        _log("Drive channel: answering a request failed (" & ex.GetType().Name & ")")
                                    Finally
                                        SyncLock _gate
                                            _busy = False
                                        End SyncLock
                                    End Try
                                End Function)
        End SyncLock
    End Sub

    ''' <summary>Answers every REQUEST of <paramref name="poll"/> addressed to this PC that this session has not answered. Returns
    ''' how many were answered. Public for the tests; <see cref="OnUpdated"/> is the only other caller.</summary>
    Public Async Function AnswerAsync(poll As ExchangeDriveQueuePoll, ct As CancellationToken) As Task(Of Integer)
        Dim answered As Integer = 0
        For Each packet As ExchangeDrivePacket In _queue.RequestsForThisDevice(poll)
            ct.ThrowIfCancellationRequested()
            Dim skip As Boolean
            SyncLock _gate
                skip = _stopped OrElse _answered.Contains(packet.PacketId)
            End SyncLock
            If skip Then Continue For
            If Await AnswerOneAsync(packet, ct).ConfigureAwait(False) Then answered += 1
        Next
        Return answered
    End Function

    ''' <summary>True when the request was answered (its last reply written); False when it was left alone or will be tried again.</summary>
    Private Async Function AnswerOneAsync(packet As ExchangeDrivePacket, ct As CancellationToken) As Task(Of Boolean)
        Dim request As ExchangeDriveRequest = Nothing
        If Not ExchangeDriveRequests.TryParse(packet.Manifest.RequestJson, request) OrElse Not ExchangeIds.IsValidId(packet.Manifest.SenderDeviceId) Then
            ' no request this version can read, or no requester to address: left to expire
            Remember(packet.PacketId)
            Return False
        End If

        Dim answer As DriveRequestAnswer = DriveRequestPlanner.Plan(request, _findRoot(request.ResourceId), _utcNow(), _packetBytes)
        If answer.Kind = DriveAnswerKind.Skip Then
            Remember(packet.PacketId)
            Return False
        End If

        ' the reply that ends the request when an ok reply could not be written; "" while every reply landed
        Dim refusal As String = If(answer.Kind = DriveAnswerKind.Refused, answer.Reason, "")
        Dim sentAny As Boolean = False
        Dim failure As ExchangeDriveException = Nothing
        Try
            Select Case answer.Kind
                Case DriveAnswerKind.Listing
                    Dim listing As ExchangeDriveSendSource = ExchangeDriveSendSource.FromBytes(ExchangeDriveRequests.ListingFileName, ExchangeDriveRequests.ListingMimeType, answer.ListingBody)
                    Dim result As ExchangeDriveSendResult = Await _queue.SendReplyAsync(packet, ExchangeDriveRequests.OutcomeOk, "", ExchangeDriveRequests.FormatInstant(answer.AsOf), True,
                                                                                        New ExchangeDriveSendSource() {listing}, ct).ConfigureAwait(False)
                    If Not result.Succeeded Then refusal = ReasonOf(result.Outcome)
                Case DriveAnswerKind.Files
                    Dim asOf As String = ExchangeDriveRequests.FormatInstant(answer.AsOf)
                    For index As Integer = 0 To answer.Replies.Count - 1
                        Dim sources As New List(Of ExchangeDriveSendSource)
                        For Each file As ExchangeDriveAnswerFile In answer.Replies(index)
                            sources.Add(ExchangeDriveSendSource.FromFile(answer.FullPathOf(file), file.Name))
                        Next
                        Dim result As ExchangeDriveSendResult = Nothing
                        Dim vanished As Boolean = False
                        Try
                            result = Await _queue.SendReplyAsync(packet, ExchangeDriveRequests.OutcomeOk, "", asOf, index = answer.Replies.Count - 1, sources, ct).ConfigureAwait(False)
                        Catch ex As IOException When Not TypeOf ex Is ExchangeDriveException
                            ' a file went away or was locked between the plan and the upload
                            vanished = True
                        End Try
                        If vanished Then
                            refusal = ExchangeDriveRequests.ReasonUnavailable
                            Exit For
                        End If
                        If Not result.Succeeded Then
                            refusal = ReasonOf(result.Outcome)
                            Exit For
                        End If
                        sentAny = True
                    Next
            End Select
        Catch ex As ExchangeDriveException When ex.Failure <> ExchangeDriveFailure.SignedOut
            failure = ex
        End Try

        If failure IsNot Nothing Then
            If failure.Failure <> ExchangeDriveFailure.DriveFull Then
                _log("Drive channel: a request could not be answered (" & failure.Failure.ToString() & ")")
                Dim passing As Boolean = failure.Failure = ExchangeDriveFailure.Offline OrElse failure.Failure = ExchangeDriveFailure.Unavailable
                If sentAny OrElse Not passing Then Remember(packet.PacketId)
                Return False
            End If
            ' an upload refused for space (item CS): the requester is told so
            refusal = ExchangeDriveRequests.ReasonDriveFull
        End If

        If refusal.Length > 0 Then
            Dim refusalFailed As ExchangeDriveException = Nothing
            Try
                Await RefuseAsync(packet, refusal, ct).ConfigureAwait(False)
            Catch ex As ExchangeDriveException When ex.Failure <> ExchangeDriveFailure.SignedOut
                refusalFailed = ex
            End Try
            If refusalFailed IsNot Nothing Then
                _log("Drive channel: a request could not be answered (" & refusalFailed.Failure.ToString() & ")")
                Remember(packet.PacketId)
                Return False
            End If
        End If

        Remember(packet.PacketId)
        Try
            Await _queue.DeletePacketAsync(packet, ct).ConfigureAwait(False)
        Catch ex As ExchangeDriveException When ex.Failure <> ExchangeDriveFailure.SignedOut
            ' answered in full; the REQUEST expires on its own (item CR)
        End Try
        Return True
    End Function

    Private Async Function RefuseAsync(packet As ExchangeDrivePacket, reason As String, ct As CancellationToken) As Task
        Await _queue.SendReplyAsync(packet, ExchangeDriveRequests.OutcomeRefused, reason, "", True, Nothing, ct).ConfigureAwait(False)
    End Function

    Private Shared Function ReasonOf(outcome As ExchangeDriveSendOutcome) As String
        Select Case outcome
            Case ExchangeDriveSendOutcome.DriveFull : Return ExchangeDriveRequests.ReasonDriveFull
            Case ExchangeDriveSendOutcome.TooLarge : Return ExchangeDriveRequests.ReasonTooLarge
            Case Else : Return ExchangeDriveRequests.ReasonUnavailable
        End Select
    End Function

    Private Sub Remember(packetId As String)
        SyncLock _gate
            _answered.Add(packetId)
        End SyncLock
    End Sub

    ''' <summary>Ends the producer and waits for a pass in flight (sign-out deletes this PC's records right after).</summary>
    Public Async Function StopAsync() As Task
        Dim running As Task
        SyncLock _gate
            _stopped = True
            running = _running
        End SyncLock
        Try
            _cts.Cancel()
        Catch ex As ObjectDisposedException
        End Try
        Try
            Await Task.WhenAny(running, Task.Delay(5000)).ConfigureAwait(False)
        Catch ex As OperationCanceledException
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            StopAsync().GetAwaiter().GetResult()
        Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is ObjectDisposedException
        End Try
        _cts.Dispose()
    End Sub

End Class
