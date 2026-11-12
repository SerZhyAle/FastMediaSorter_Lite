Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 269 P3.2 - the device agent asks, for the viewer, the PC that lists a folder on Google Drive for a listing or files
' (DEVICE-EXCHANGE section 24, items CO, CQ, CR). The viewer speaks to the agent over its local pipe (DriveStart / DriveJob /
' DriveCancel, ExchangeAgentPipe); the agent sends the REQUEST, waits for the REPLYs and saves what they carry. Three parts:
'
'   - IDriveRequestPort: the five queue operations a requester needs, so the job logic runs against a fake in a test.
'     DriveQueueRequestPort is the production one over ExchangeDriveQueue.
'   - DriveRequestConsumer.RunAsync: one request from send to answer.
'   - DriveRequestJobs: the agent's running and finished jobs, bounded, long-polled by the viewer.
'
' Rules held here (cited, not restated):
'   - The requester waits at the active interval (10 s) and gives up after 120 s without a new reply, deleting its own REQUEST
'     (item CQ, ExchangeDriveQueue.AwaitRepliesAsync). A job the viewer cancels withdraws its REQUEST too.
'   - Every file arrives whole (size and SHA-256) or nothing of it is kept; each REPLY is deleted once read (item CR).
'   - Files go only into a folder under the viewer's remote cache, which the viewer names and the agent checks: nothing is ever
'     written anywhere else on the viewer's say-so.
'   - A REPLY names files by base name (item CN; PROPOSAL-2026-10-30-drive-reply-relative-paths is open), so a fetch whose paths
'     share a base name cannot be told apart and is refused before anything is sent.
'   - Log lines name kinds and counts, never a path, a file name or a request body.

''' <summary>A sent REQUEST: its packet id, or the machine token of why it was not sent.</summary>
Friend NotInheritable Class DriveRequestSent
    Public ReadOnly PacketId As String
    Public ReadOnly [Error] As String
    Public Sub New(packetId As String, [error] As String)
        Me.PacketId = If(packetId, "")
        Me.Error = If([error], "")
    End Sub
    Public ReadOnly Property Sent As Boolean
        Get
            Return PacketId.Length > 0 AndAlso [Error].Length = 0
        End Get
    End Property
End Class

''' <summary>One REPLY as the requester reads it. <see cref="Handle"/> is the port's own packet object.</summary>
Friend NotInheritable Class DriveReplyView
    Public ReadOnly PacketId As String
    Public ReadOnly Outcome As String
    Public ReadOnly Reason As String
    Public ReadOnly Final As Boolean
    Public ReadOnly Handle As Object
    ''' <summary>The producer's own instant an ok reply to `changes` was read at (item CO); "" when the reply carries none.</summary>
    Public ReadOnly AsOf As String
    Public Sub New(packetId As String, outcome As String, reason As String, final As Boolean, handle As Object, Optional asOf As String = "")
        Me.PacketId = If(packetId, "")
        Me.Outcome = If(outcome, "")
        Me.Reason = If(reason, "")
        Me.Final = final
        Me.Handle = handle
        Me.AsOf = If(asOf, "")
    End Sub
    Public Overrides Function ToString() As String
        Return "DriveReplyView"
    End Function
End Class

''' <summary>The queue as a requester uses it.</summary>
Friend Interface IDriveRequestPort
    Function SendRequestAsync(requestJson As String, targetDeviceId As String, targetDeviceName As String, ct As CancellationToken) As Task(Of DriveRequestSent)
    ''' <summary>Replies not in <paramref name="seen"/>; empty after 120 s of silence (the REQUEST is then deleted).</summary>
    Function AwaitRepliesAsync(requestPacketId As String, seen As ISet(Of String), ct As CancellationToken) As Task(Of IReadOnlyList(Of DriveReplyView))
    ''' <summary>The reply's `listing.json`, whole and checked, or Nothing.</summary>
    Function ReadListingAsync(reply As DriveReplyView, ct As CancellationToken) As Task(Of Byte())
    ''' <summary>Saves the reply's files under <paramref name="directory"/> and deletes the reply; Nothing when a file did not arrive whole.</summary>
    Function ReceiveAsync(reply As DriveReplyView, directory As String, ct As CancellationToken) As Task(Of IReadOnlyList(Of String))
    Function DeleteAsync(reply As DriveReplyView, ct As CancellationToken) As Task
    Function WithdrawAsync(requestPacketId As String, ct As CancellationToken) As Task
End Interface

''' <summary>The production port: the Drive service's packet queue.</summary>
Friend NotInheritable Class DriveQueueRequestPort
    Implements IDriveRequestPort

    Private ReadOnly _queue As ExchangeDriveQueue

    Public Sub New(queue As ExchangeDriveQueue)
        If queue Is Nothing Then Throw New ArgumentNullException(NameOf(queue))
        _queue = queue
    End Sub

    Public Async Function SendRequestAsync(requestJson As String, targetDeviceId As String, targetDeviceName As String, ct As CancellationToken) As Task(Of DriveRequestSent) Implements IDriveRequestPort.SendRequestAsync
        Dim sent As ExchangeDriveSendResult = Await _queue.SendRequestAsync(requestJson, targetDeviceId, targetDeviceName, ct).ConfigureAwait(False)
        If sent.Succeeded Then Return New DriveRequestSent(sent.PacketId, "")
        Return New DriveRequestSent("", If(sent.Outcome = ExchangeDriveSendOutcome.DriveFull, ExchangeDriveRequests.ReasonDriveFull, ExchangeAgentPipe.ErrorBadRequest))
    End Function

    Public Async Function AwaitRepliesAsync(requestPacketId As String, seen As ISet(Of String), ct As CancellationToken) As Task(Of IReadOnlyList(Of DriveReplyView)) Implements IDriveRequestPort.AwaitRepliesAsync
        Dim result As New List(Of DriveReplyView)
        For Each packet As ExchangeDrivePacket In Await _queue.AwaitRepliesAsync(requestPacketId, seen, ct).ConfigureAwait(False)
            Dim m As ExchangeDrivePacketManifest = packet.Manifest
            result.Add(New DriveReplyView(packet.PacketId, m.Outcome, m.Reason, m.Final, packet, m.AsOf))
        Next
        Return result
    End Function

    Public Function ReadListingAsync(reply As DriveReplyView, ct As CancellationToken) As Task(Of Byte()) Implements IDriveRequestPort.ReadListingAsync
        Return _queue.ReadPayloadAsync(PacketOf(reply), ExchangeDriveRequests.ListingFileName, ExchangeDriveRequests.MaxListingBytes, ct)
    End Function

    Public Async Function ReceiveAsync(reply As DriveReplyView, directory As String, ct As CancellationToken) As Task(Of IReadOnlyList(Of String)) Implements IDriveRequestPort.ReceiveAsync
        Dim received As ExchangeDriveReceiveResult = Await _queue.ReceiveReplyAsync(PacketOf(reply), directory, ct).ConfigureAwait(False)
        Return If(received.Succeeded, received.SavedPaths, Nothing)
    End Function

    Public Function DeleteAsync(reply As DriveReplyView, ct As CancellationToken) As Task Implements IDriveRequestPort.DeleteAsync
        Return _queue.DeletePacketAsync(PacketOf(reply), ct)
    End Function

    Public Function WithdrawAsync(requestPacketId As String, ct As CancellationToken) As Task Implements IDriveRequestPort.WithdrawAsync
        Return _queue.WithdrawRequestAsync(requestPacketId, ct)
    End Function

    Private Shared Function PacketOf(reply As DriveReplyView) As ExchangeDrivePacket
        Dim packet As ExchangeDrivePacket = TryCast(reply?.Handle, ExchangeDrivePacket)
        If packet Is Nothing Then Throw New ArgumentException("not a reply of this queue", NameOf(reply))
        Return packet
    End Function
End Class

''' <summary>The answer of one request, as a job reports it.</summary>
Friend NotInheritable Class DriveRequestOutcome
    Public ReadOnly [Error] As String
    Public ReadOnly ListingBody As Byte()
    Public ReadOnly Files As IReadOnlyList(Of ExchangeAgentDriveFile)

    Private Sub New([error] As String, listingBody As Byte(), files As IReadOnlyList(Of ExchangeAgentDriveFile))
        Me.Error = If([error], "")
        Me.ListingBody = listingBody
        Me.Files = files
    End Sub

    Public Shared Function Failed([error] As String) As DriveRequestOutcome
        Return New DriveRequestOutcome([error], Nothing, Nothing)
    End Function

    Public Shared Function Listed(body As Byte()) As DriveRequestOutcome
        Return New DriveRequestOutcome("", body, Nothing)
    End Function

    Public Shared Function Fetched(files As IReadOnlyList(Of ExchangeAgentDriveFile)) As DriveRequestOutcome
        Return New DriveRequestOutcome("", Nothing, files)
    End Function

    Public ReadOnly Property Succeeded As Boolean
        Get
            Return [Error].Length = 0
        End Get
    End Property
End Class

''' <summary>The answer of one `changes` request (ticket 269 P4, item CO): the files of every reply, saved whole, and the `asOf` the next
''' request sends as `modifiedAfter`.</summary>
Friend NotInheritable Class DriveChangesOutcome
    Public ReadOnly [Error] As String
    Public ReadOnly Paths As IReadOnlyList(Of String)
    Public ReadOnly AsOf As String

    Public Sub New([error] As String, paths As IReadOnlyList(Of String), asOf As String)
        Me.Error = If([error], "")
        Me.Paths = If(paths, Array.Empty(Of String)())
        Me.AsOf = If(asOf, "")
    End Sub

    Public ReadOnly Property Succeeded As Boolean
        Get
            Return [Error].Length = 0
        End Get
    End Property
End Class

Friend NotInheritable Class DriveRequestConsumer

    Private Sub New()
    End Sub

    ''' <summary>One `changes` request from send to the last reply, for a scheduled operation (not on the agent's pipe). A run counts only
    ''' after the reply marked final arrived and every reply before it arrived whole. A Drive failure becomes `drive-offline` or
    ''' `drive-full`, a lost sign-in stays the exception it is; cancellation withdraws the REQUEST and is rethrown.</summary>
    Public Shared Async Function RunChangesAsync(port As IDriveRequestPort, request As ExchangeDriveRequest, targetDeviceId As String, targetDeviceName As String,
                                                 directory As String, ct As CancellationToken) As Task(Of DriveChangesOutcome)
        If port Is Nothing Then Throw New ArgumentNullException(NameOf(port))
        If request Is Nothing OrElse request.Action <> ExchangeDriveRequestAction.Changes Then Return New DriveChangesOutcome(ExchangeAgentPipe.ErrorBadRequest, Nothing, "")
        Dim body As String = ExchangeDriveRequests.Build(request)
        If body.Length = 0 Then Return New DriveChangesOutcome(ExchangeAgentPipe.ErrorBadRequest, Nothing, "")
        Dim sent As DriveRequestSent
        Try
            sent = Await port.SendRequestAsync(body, targetDeviceId, targetDeviceName, ct).ConfigureAwait(False)
        Catch ex As ExchangeDriveException When ex.Failure <> ExchangeDriveFailure.SignedOut
            Return New DriveChangesOutcome(TokenOf(ex), Nothing, "")
        End Try
        If Not sent.Sent Then Return New DriveChangesOutcome(If(sent.Error.Length > 0, sent.Error, ExchangeAgentPipe.ErrorBadRequest), Nothing, "")

        Dim cancelled As OperationCanceledException = Nothing
        Try
            Return Await AwaitChangesAsync(port, sent.PacketId, directory, ct).ConfigureAwait(False)
        Catch ex As OperationCanceledException
            cancelled = ex
        Catch ex As ExchangeDriveException When ex.Failure <> ExchangeDriveFailure.SignedOut
            Return New DriveChangesOutcome(TokenOf(ex), Nothing, "")
        End Try
        Await WithdrawQuietlyAsync(port, sent.PacketId).ConfigureAwait(False)
        Throw cancelled
    End Function

    Private Shared Async Function AwaitChangesAsync(port As IDriveRequestPort, requestId As String, directory As String, ct As CancellationToken) As Task(Of DriveChangesOutcome)
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        Dim saved As New List(Of String)
        Dim asOf As String = ""
        Do
            Dim replies As IReadOnlyList(Of DriveReplyView) = Await port.AwaitRepliesAsync(requestId, seen, ct).ConfigureAwait(False)
            ' 120 s of silence: the queue already deleted the REQUEST
            If replies.Count = 0 Then Return New DriveChangesOutcome(ExchangeAgentPipe.ErrorNoAnswer, Nothing, "")
            Dim finished As Boolean = False
            For Each reply As DriveReplyView In replies
                seen.Add(reply.PacketId)
                If Not String.Equals(reply.Outcome, ExchangeDriveRequests.OutcomeOk, StringComparison.Ordinal) Then
                    Await port.DeleteAsync(reply, ct).ConfigureAwait(False)
                    Return New DriveChangesOutcome(If(reply.Reason.Length > 0, reply.Reason, ExchangeDriveRequests.ReasonUnavailable), Nothing, "")
                End If
                Dim paths As IReadOnlyList(Of String) = Await port.ReceiveAsync(reply, directory, ct).ConfigureAwait(False)
                If paths Is Nothing Then
                    ' read and refused: the reply is not left on the person's Drive until it expires (item CR)
                    Await port.DeleteAsync(reply, ct).ConfigureAwait(False)
                    Return New DriveChangesOutcome(ExchangeAgentPipe.ErrorCorrupt, Nothing, "")
                End If
                saved.AddRange(paths)
                If reply.AsOf.Length > 0 Then asOf = reply.AsOf
                If reply.Final Then finished = True
            Next
            If finished Then Exit Do
        Loop
        Return New DriveChangesOutcome("", saved, asOf)
    End Function

    ''' <summary>The requester gave up: the REQUEST goes, so the device does not answer it for nothing (best effort, never on the cancelled token).</summary>
    Private Shared Async Function WithdrawQuietlyAsync(port As IDriveRequestPort, requestId As String) As Task
        Using bound As New CancellationTokenSource(TimeSpan.FromSeconds(30))
            Try
                Await port.WithdrawAsync(requestId, bound.Token).ConfigureAwait(False)
            Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is IOException
            End Try
        End Using
    End Function

    ''' <summary>Why a request the viewer handed in is not sent ("" when it may be): no action this version sends, a path out of form,
    ''' more than 100 paths, or two paths with one base name (a REPLY could not tell them apart).</summary>
    Public Shared Function Refusal(request As ExchangeDriveRequest) As String
        If request Is Nothing Then Return ExchangeAgentPipe.ErrorBadRequest
        Select Case request.Action
            Case ExchangeDriveRequestAction.List
            Case ExchangeDriveRequestAction.Fetch
                Dim names As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                For Each p As String In request.Paths
                    If Not names.Add(New ExchangeDriveAnswerFile(p, 0).Name) Then Return ExchangeAgentPipe.ErrorBadRequest
                Next
            Case Else
                Return ExchangeAgentPipe.ErrorBadRequest
        End Select
        Return If(ExchangeDriveRequests.Build(request).Length = 0, ExchangeAgentPipe.ErrorBadRequest, "")
    End Function

    ''' <summary>True when <paramref name="directory"/> is an existing folder strictly inside <paramref name="cacheRoot"/> - the only
    ''' place a fetch saves to. A folder reached through a link is refused like one outside.</summary>
    Public Shared Function IsCacheFolder(directory As String, cacheRoot As String) As Boolean
        If String.IsNullOrEmpty(directory) OrElse String.IsNullOrEmpty(cacheRoot) Then Return False
        Try
            If Not Path.IsPathFullyQualified(directory) Then Return False
            Dim candidate As String = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)
            Dim root As String = Path.GetFullPath(cacheRoot).TrimEnd(Path.DirectorySeparatorChar)
            If Not candidate.StartsWith(root & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) Then Return False
            If Not System.IO.Directory.Exists(candidate) Then Return False
            ' every folder from the root down to the target must be a plain folder
            Dim walk As String = candidate
            While walk.Length > root.Length
                If (File.GetAttributes(walk) And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint Then Return False
                walk = Path.GetDirectoryName(walk)
                If walk Is Nothing Then Return False
            End While
            Return True
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                   TypeOf ex Is NotSupportedException
            Return False
        End Try
    End Function

    ''' <summary>One request from send to answer. <paramref name="directory"/> is where a fetch saves (already checked by the caller).
    ''' A Drive failure becomes `drive-offline` or `drive-full`; cancellation withdraws the REQUEST and is rethrown.</summary>
    Public Shared Async Function RunAsync(port As IDriveRequestPort, request As ExchangeDriveRequest, targetDeviceId As String, targetDeviceName As String,
                                          directory As String, ct As CancellationToken) As Task(Of DriveRequestOutcome)
        If port Is Nothing Then Throw New ArgumentNullException(NameOf(port))
        Dim refused As String = Refusal(request)
        If refused.Length > 0 Then Return DriveRequestOutcome.Failed(refused)
        Dim sent As DriveRequestSent
        Try
            sent = Await port.SendRequestAsync(ExchangeDriveRequests.Build(request), targetDeviceId, targetDeviceName, ct).ConfigureAwait(False)
        Catch ex As ExchangeDriveException
            Return DriveRequestOutcome.Failed(TokenOf(ex))
        End Try
        If Not sent.Sent Then Return DriveRequestOutcome.Failed(If(sent.Error.Length > 0, sent.Error, ExchangeAgentPipe.ErrorBadRequest))

        Dim cancelled As OperationCanceledException = Nothing
        Try
            Return Await AwaitAnswerAsync(port, request, sent.PacketId, directory, ct).ConfigureAwait(False)
        Catch ex As OperationCanceledException
            cancelled = ex
        Catch ex As ExchangeDriveException
            Return DriveRequestOutcome.Failed(TokenOf(ex))
        End Try
        Await WithdrawQuietlyAsync(port, sent.PacketId).ConfigureAwait(False)
        Throw cancelled
    End Function

    Private Shared Async Function AwaitAnswerAsync(port As IDriveRequestPort, request As ExchangeDriveRequest, requestId As String, directory As String,
                                                   ct As CancellationToken) As Task(Of DriveRequestOutcome)
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        ' a fetch collects by base name: the requested path a saved file belongs to (Refusal guarantees the names are unique)
        Dim wanted As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        For Each p As String In request.Paths
            wanted(New ExchangeDriveAnswerFile(p, 0).Name) = p
        Next
        Dim saved As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Do
            Dim replies As IReadOnlyList(Of DriveReplyView) = Await port.AwaitRepliesAsync(requestId, seen, ct).ConfigureAwait(False)
            ' 120 s of silence: the queue already deleted the REQUEST
            If replies.Count = 0 Then Return DriveRequestOutcome.Failed(ExchangeAgentPipe.ErrorNoAnswer)
            Dim finished As Boolean = False
            For Each reply As DriveReplyView In replies
                seen.Add(reply.PacketId)
                If Not String.Equals(reply.Outcome, ExchangeDriveRequests.OutcomeOk, StringComparison.Ordinal) Then
                    Await port.DeleteAsync(reply, ct).ConfigureAwait(False)
                    Return DriveRequestOutcome.Failed(If(reply.Reason.Length > 0, reply.Reason, ExchangeDriveRequests.ReasonUnavailable))
                End If
                If request.Action = ExchangeDriveRequestAction.List Then
                    Dim body As Byte() = Await port.ReadListingAsync(reply, ct).ConfigureAwait(False)
                    Await port.DeleteAsync(reply, ct).ConfigureAwait(False)
                    Dim parsed As ExchangeDriveListingReply = Nothing
                    If body Is Nothing OrElse Not ExchangeDriveRequests.TryParseListing(body, parsed) Then Return DriveRequestOutcome.Failed(ExchangeAgentPipe.ErrorCorrupt)
                    Return DriveRequestOutcome.Listed(body)
                End If
                Dim paths As IReadOnlyList(Of String) = Await port.ReceiveAsync(reply, directory, ct).ConfigureAwait(False)
                If paths Is Nothing Then
                    ' read and refused: the reply is not left on the person's Drive until it expires (item CR)
                    Await port.DeleteAsync(reply, ct).ConfigureAwait(False)
                    Return DriveRequestOutcome.Failed(ExchangeAgentPipe.ErrorCorrupt)
                End If
                For Each path As String In paths
                    ' the queue places a file under a free name (" (2)" beside one already in the folder): map it back to what was asked
                    Dim key As String = MatchWanted(wanted, saved, System.IO.Path.GetFileName(path))
                    If key IsNot Nothing Then saved(key) = path
                Next
                If reply.Final Then finished = True
            Next
            If finished Then Exit Do
        Loop
        Dim files As New List(Of ExchangeAgentDriveFile)
        For Each p As String In request.Paths
            Dim name As String = New ExchangeDriveAnswerFile(p, 0).Name
            Dim savedPath As String = Nothing
            ' a requested file the final reply did not carry: the folder changed between the ask and the answer
            If Not saved.TryGetValue(name, savedPath) Then Return DriveRequestOutcome.Failed(ExchangeDriveRequests.ReasonUnavailable)
            files.Add(New ExchangeAgentDriveFile(p, savedPath))
        Next
        Return DriveRequestOutcome.Fetched(files)
    End Function

    ''' <summary>The requested base name a saved file stands for: the name itself, or the name the queue gave it to avoid an existing
    ''' file ("a (2).jpg" for "a.jpg"). Nothing for a file nobody asked for.</summary>
    Friend Shared Function MatchWanted(wanted As Dictionary(Of String, String), saved As Dictionary(Of String, String), savedName As String) As String
        If wanted.ContainsKey(savedName) AndAlso Not saved.ContainsKey(savedName) Then Return savedName
        Dim stem As String = System.IO.Path.GetFileNameWithoutExtension(savedName)
        Dim ext As String = System.IO.Path.GetExtension(savedName)
        Dim cut As Integer = stem.LastIndexOf(" (", StringComparison.Ordinal)
        If cut > 0 AndAlso stem.EndsWith(")", StringComparison.Ordinal) Then
            Dim original As String = stem.Substring(0, cut) & ext
            If wanted.ContainsKey(original) AndAlso Not saved.ContainsKey(original) Then Return original
        End If
        Return Nothing
    End Function

    Private Shared Function TokenOf(ex As ExchangeDriveException) As String
        Return If(ex.Failure = ExchangeDriveFailure.DriveFull, ExchangeDriveRequests.ReasonDriveFull, ExchangeAgentPipe.ErrorDriveOffline)
    End Function

End Class

''' <summary>The agent's Drive jobs (ticket 269 P3.2): at most <see cref="MaxRunning"/> at once; a finished job is kept until the viewer
''' reads it or <see cref="KeepFinishedMs"/> passed. Every job is cancelled when the agent stops.</summary>
Friend NotInheritable Class DriveRequestJobs
    Implements IDisposable

    Public Const MaxRunning As Integer = 8
    ''' <summary>Finished jobs nobody read are kept at most this many (the oldest go first): a listing may be megabytes.</summary>
    Public Const MaxFinished As Integer = 16
    Public Const KeepFinishedMs As Long = 5 * 60 * 1000
    Public Const MaxWaitMs As Integer = 30000

    Private NotInheritable Class Job
        Public Id As String
        Public DeviceName As String
        Public Cts As CancellationTokenSource
        Public Done As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Public Outcome As DriveRequestOutcome
        Public FinishedMs As Long
    End Class

    Private ReadOnly _gate As New Object()
    Private ReadOnly _jobs As New Dictionary(Of String, Job)(StringComparer.Ordinal)
    Private ReadOnly _monotonicMs As Func(Of Long)
    Private _disposed As Boolean

    Public Sub New(Optional monotonicMs As Func(Of Long) = Nothing)
        _monotonicMs = If(monotonicMs, Function() Environment.TickCount64)
    End Sub

    ''' <summary>Starts a job and returns its id, or "" when <see cref="MaxRunning"/> jobs already run (the caller answers `busy`).
    ''' <paramref name="run"/> is the request itself; its exceptions other than cancellation end the job as `drive-offline`.</summary>
    Public Function Start(deviceName As String, run As Func(Of CancellationToken, Task(Of DriveRequestOutcome))) As String
        If run Is Nothing Then Throw New ArgumentNullException(NameOf(run))
        Dim job As New Job() With {.Id = Guid.NewGuid().ToString("N"), .DeviceName = If(deviceName, ""), .Cts = New CancellationTokenSource()}
        SyncLock _gate
            If _disposed Then Return ""
            Sweep()
            Dim running As Integer = 0
            For Each j As Job In _jobs.Values
                If j.Outcome Is Nothing Then running += 1
            Next
            If running >= MaxRunning Then
                job.Cts.Dispose()
                Return ""
            End If
            _jobs(job.Id) = job
        End SyncLock
        Dim unused As Task = Task.Run(Function() RunJobAsync(job, run))
        Return job.Id
    End Function

    Private Async Function RunJobAsync(job As Job, run As Func(Of CancellationToken, Task(Of DriveRequestOutcome))) As Task
        Dim outcome As DriveRequestOutcome
        Try
            outcome = Await run(job.Cts.Token).ConfigureAwait(False)
            If outcome Is Nothing Then outcome = DriveRequestOutcome.Failed(ExchangeAgentPipe.ErrorDriveOffline)
        Catch ex As OperationCanceledException
            outcome = DriveRequestOutcome.Failed(ExchangeAgentPipe.ErrorUnknownJob)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is InvalidOperationException OrElse
                                   TypeOf ex Is System.Net.Http.HttpRequestException
            AppFileLogger.WriteLine("Exchange agent: a Google Drive request failed - " & ex.GetType().Name)
            outcome = DriveRequestOutcome.Failed(ExchangeAgentPipe.ErrorDriveOffline)
        End Try
        Dim forgotten As Boolean
        SyncLock _gate
            job.Outcome = outcome
            job.FinishedMs = _monotonicMs()
            forgotten = Not _jobs.ContainsKey(job.Id)
        End SyncLock
        job.Done.TrySetResult(True)
        ' a cancelled job is no longer listed and nobody will read it
        If forgotten Then job.Cts.Dispose()
    End Function

    ''' <summary>The job's state after waiting up to <paramref name="waitMs"/> (at most 30 s) for it to finish; Nothing for an unknown
    ''' job. A finished job is forgotten once its state was handed out.</summary>
    Public Async Function WaitAsync(jobId As String, waitMs As Integer, ct As CancellationToken) As Task(Of String)
        Dim job As Job = Nothing
        SyncLock _gate
            If jobId Is Nothing OrElse Not _jobs.TryGetValue(jobId, job) Then Return Nothing
        End SyncLock
        Dim wait As Integer = Math.Min(Math.Max(waitMs, 0), MaxWaitMs)
        If wait > 0 AndAlso Not job.Done.Task.IsCompleted Then
            Await Task.WhenAny(job.Done.Task, Task.Delay(wait, ct)).ConfigureAwait(False)
        End If
        Dim outcome As DriveRequestOutcome
        SyncLock _gate
            outcome = job.Outcome
            If outcome IsNot Nothing Then _jobs.Remove(job.Id)
        End SyncLock
        If outcome Is Nothing Then Return ExchangeAgentDriveJob.ToJson(job.Id, ExchangeAgentDriveJobState.Waiting, "", job.DeviceName, Nothing, Nothing)
        job.Cts.Dispose()
        If Not outcome.Succeeded Then Return ExchangeAgentDriveJob.ToJson(job.Id, ExchangeAgentDriveJobState.Failed, outcome.Error, job.DeviceName, Nothing, Nothing)
        Return ExchangeAgentDriveJob.ToJson(job.Id, ExchangeAgentDriveJobState.Done, "", job.DeviceName, outcome.ListingBody, outcome.Files)
    End Function

    ''' <summary>Cancels a job (its REQUEST is withdrawn) and forgets it. False for an unknown job.</summary>
    Public Function Cancel(jobId As String) As Boolean
        Dim job As Job = Nothing
        SyncLock _gate
            If jobId Is Nothing OrElse Not _jobs.TryGetValue(jobId, job) Then Return False
            _jobs.Remove(jobId)
        End SyncLock
        Try
            job.Cts.Cancel()
        Catch ex As ObjectDisposedException
        End Try
        Return True
    End Function

    ''' <summary>How many jobs the registry holds (a test seam).</summary>
    Friend ReadOnly Property Count As Integer
        Get
            SyncLock _gate
                Return _jobs.Count
            End SyncLock
        End Get
    End Property

    ' Called under _gate: drops finished jobs nobody read in time, and the oldest beyond MaxFinished.
    Private Sub Sweep()
        Dim now As Long = _monotonicMs()
        Dim stale As New List(Of String)
        Dim finished As New List(Of Job)
        For Each pair As KeyValuePair(Of String, Job) In _jobs
            If pair.Value.Outcome Is Nothing Then Continue For
            If now - pair.Value.FinishedMs >= KeepFinishedMs Then stale.Add(pair.Key) Else finished.Add(pair.Value)
        Next
        finished.Sort(Function(a As Job, b As Job) a.FinishedMs.CompareTo(b.FinishedMs))
        For index As Integer = 0 To finished.Count - MaxFinished
            stale.Add(finished(index).Id)
        Next
        For Each id As String In stale
            _jobs(id).Cts.Dispose()
            _jobs.Remove(id)
        Next
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dim all As List(Of Job)
        SyncLock _gate
            If _disposed Then Return
            _disposed = True
            all = New List(Of Job)(_jobs.Values)
            _jobs.Clear()
        End SyncLock
        For Each job As Job In all
            Try
                job.Cts.Cancel()
            Catch ex As ObjectDisposedException
            End Try
        Next
    End Sub

End Class
