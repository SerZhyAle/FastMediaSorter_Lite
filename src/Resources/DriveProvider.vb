#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 269 P3.3 - a folder of another device of the account, reached through Google Drive (DEVICE-EXCHANGE item CO): the third source
' a listing can come from, after a live connection and a port forward, and the slowest by far.
'
'   - Read-only and fetched on demand: a listing carries names, sizes and dates and no thumbnails (item CO), and nothing is fetched ahead
'     of the person, because every fetch is a packet through their quota (item CS).
'   - The viewer never talks to Drive. It asks the Share Manager's agent over the local pipe, which keeps the sign-in; a request is a job
'     the agent runs (the pipe caps one handler at 40 s, an answer can take 120 s), followed here by long polls (item CQ).
'   - One folder: subfolders are listed by the producer and not walked here, since each level is one more round trip of its own. Entering
'     one takes an address of that folder.

''' <summary>The four calls of the agent's pipe the provider needs, so the facts run it against a fake.</summary>
Friend Interface IDriveJobChannel
    Function ListAsync(resourceId As String, path As String, ct As CancellationToken) As Task(Of ExchangeAgentDriveJobResult)
    Function FetchAsync(resourceId As String, paths As IEnumerable(Of String), directory As String, ct As CancellationToken) As Task(Of ExchangeAgentDriveJobResult)
    Function JobAsync(jobId As String, waitMs As Integer, ct As CancellationToken) As Task(Of ExchangeAgentDriveJobResult)
    Function CancelAsync(jobId As String, ct As CancellationToken) As Task(Of ExchangeAgentReply)
End Interface

''' <summary>The channel of the running program: the pipe of the Share Manager's agent.</summary>
Friend NotInheritable Class ExchangeAgentDriveChannel
    Implements IDriveJobChannel

    Private ReadOnly _client As ExchangeAgentClient

    Friend Sub New(Optional client As ExchangeAgentClient = Nothing)
        _client = If(client, New ExchangeAgentClient())
    End Sub

    Public Function ListAsync(resourceId As String, path As String, ct As CancellationToken) As Task(Of ExchangeAgentDriveJobResult) Implements IDriveJobChannel.ListAsync
        Return _client.DriveListAsync(resourceId, path, ct)
    End Function

    Public Function FetchAsync(resourceId As String, paths As IEnumerable(Of String), directory As String, ct As CancellationToken) As Task(Of ExchangeAgentDriveJobResult) Implements IDriveJobChannel.FetchAsync
        Return _client.DriveFetchAsync(resourceId, paths, directory, ct)
    End Function

    Public Function JobAsync(jobId As String, waitMs As Integer, ct As CancellationToken) As Task(Of ExchangeAgentDriveJobResult) Implements IDriveJobChannel.JobAsync
        Return _client.DriveJobAsync(jobId, waitMs, ct)
    End Function

    Public Function CancelAsync(jobId As String, ct As CancellationToken) As Task(Of ExchangeAgentReply) Implements IDriveJobChannel.CancelAsync
        Return _client.DriveCancelAsync(jobId, ct)
    End Function

End Class

''' <summary>How a refusal of the Drive channel travels as a <see cref="ResourceFailure"/>: the machine token in Detail, under a prefix of
''' its own, so <c>ResourceMessages.ForFailure</c> tells it from a server's and says the sentence of the channel. Pure.</summary>
Friend Module DriveFailures

    Private Const DetailPrefix As String = "gd:"
    ''' <summary>The agent's pipe did not answer: the Share Manager is not running.</summary>
    Friend Const ReasonAgentAbsent As String = "agent-absent"

    Friend Function FromReason(reason As String) As ResourceFailure
        Dim token As String = If(String.IsNullOrEmpty(reason), ExchangeDriveRequests.ReasonUnavailable, reason)
        Select Case token
            Case ExchangeAgentPipe.ErrorNoAnswer
                Return New ResourceFailure(ResourceFailureKind.Timeout, Nothing, DetailPrefix & token)
            Case ExchangeDriveRequests.ReasonNotPermitted, ExchangeDriveRequests.ReasonPinRequired, ExchangeDriveRequests.ReasonTooLarge
                Return New ResourceFailure(ResourceFailureKind.Unsupported, Nothing, DetailPrefix & token)
            Case Else
                Return New ResourceFailure(ResourceFailureKind.Transport, Nothing, DetailPrefix & token)
        End Select
    End Function

    ''' <summary>The machine token of a failure of this channel, or "" for any other failure.</summary>
    Friend Function ReasonOf(failure As ResourceFailure) As String
        If failure Is Nothing OrElse Not failure.Detail.StartsWith(DetailPrefix, StringComparison.Ordinal) Then Return ""
        Return failure.Detail.Substring(DetailPrefix.Length)
    End Function

End Module

''' <summary>A folder listed by another device on Google Drive, as a provider. Thread-safe the way the session uses it: one listing at a
''' time, a fetch at a time, the status line reading the two waiting facts from the UI thread.</summary>
Friend NotInheritable Class DriveProvider
    Implements IFileSystemProvider

    ''' <summary>The long poll of one DriveJob call; the pipe answers sooner the moment the job ends.</summary>
    Friend Const PollWaitMs As Integer = 25000
    ''' <summary>A poll answered at once (an agent's error, a fake) is never repeated faster than this.</summary>
    Friend Const MinPollIntervalMs As Integer = 500
    ''' <summary>The pipe may miss a beat while the agent is busy; this many in a row mean it is gone.</summary>
    Friend Const MaxPipeMisses As Integer = 3
    ''' <summary>A flip through files leaves the job just cancelled still counted: `busy` is retried this many times, a second apart.</summary>
    Friend Const MaxBusyRetries As Integer = 5

    Private Shared ReadOnly Caps As New ProviderCapabilities(
        canWrite:=False, canDelete:=False, canRename:=False, canCreateFolder:=False, needsMaterialisation:=True,
        costHint:=ProviderCostHint.Slow, fetchOnDemandOnly:=True)

    Private ReadOnly _channel As IDriveJobChannel
    Private ReadOnly _resourceId As String
    Private ReadOnly _gate As New Object()
    Private ReadOnly _known As New Dictionary(Of String, ResourceEntry)(StringComparer.Ordinal)
    Private ReadOnly _stagingRoot As String
    Private _deviceName As String
    Private _lastRecordAt As String = ""
    Private _lastFailure As ResourceFailure
    Private _startedFolder As String
    Private _startedJob As String = ""

    ''' <param name="stagingRoot">Where a fetch stages its file: the remote cache unless a test names a scratch root.</param>
    Friend Sub New(channel As IDriveJobChannel, resourceId As String, Optional deviceName As String = "", Optional stagingRoot As String = Nothing)
        _channel = channel
        _resourceId = If(resourceId, "")
        _deviceName = If(deviceName, "")
        _stagingRoot = If(String.IsNullOrEmpty(stagingRoot), RemoteCachePaths.CacheRoot(), stagingRoot)
    End Sub

    Public ReadOnly Property Kind As ResourceKind Implements IFileSystemProvider.Kind
        Get
            Return ResourceKind.Drive
        End Get
    End Property

    Public ReadOnly Property Capabilities As ProviderCapabilities Implements IFileSystemProvider.Capabilities
        Get
            Return Caps
        End Get
    End Property

    ''' <summary>The device being waited for, as the agent names it - what item CQ's waiting line says.</summary>
    Friend ReadOnly Property DeviceName As String
        Get
            SyncLock _gate
                Return _deviceName
            End SyncLock
        End Get
    End Property

    ''' <summary>When that device last wrote its record, or Nothing when not known.</summary>
    Friend ReadOnly Property LastRecordAt As DateTimeOffset?
        Get
            Dim text As String
            SyncLock _gate
                text = _lastRecordAt
            End SyncLock
            Dim parsed As DateTimeOffset
            If DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, parsed) Then Return parsed
            Return Nothing
        End Get
    End Property

    ''' <summary>Why the last fetch failed, or Nothing: the session reports a failed fetch as an outcome and logs the exception, and the
    ''' status line has to say which refusal it was.</summary>
    Friend ReadOnly Property LastFailure As ResourceFailure
        Get
            SyncLock _gate
                Return _lastFailure
            End SyncLock
        End Get
    End Property

    ''' <summary>The last road of item BB: asks for the folder's listing now. False when that is refused at once (Share Manager not running,
    ''' no sign-in, the folder not listed on Drive), which leaves the caller's own failure as the one to report; True means the request is
    ''' on its way and the next <see cref="ListEntries"/> of this folder follows it instead of sending another.</summary>
    Friend Function TryStartListing(dirPath As String, ct As CancellationToken) As Boolean
        Dim folder As String = ToRelative(dirPath)
        Try
            Dim first As ExchangeAgentDriveJobResult = StartJob(Function(token As CancellationToken) _channel.ListAsync(_resourceId, folder, token), ct)
            SyncLock _gate
                _startedFolder = folder
                _startedJob = first.Job.JobId
            End SyncLock
            Return True
        Catch ex As ResourceFailureException
            Return False
        Catch ex As OperationCanceledException
            Return False
        End Try
    End Function

    ''' <summary>Files and folders of one folder. A refusal is a failure with its reason: unlike a local scan, "empty" and "the device did
    ''' not answer" must not read alike here.</summary>
    Public Function ListEntries(dirPath As String, ct As CancellationToken) As IReadOnlyList(Of ResourceEntry) Implements IFileSystemProvider.ListEntries
        Dim folder As String = ToRelative(dirPath)
        Dim started As String
        SyncLock _gate
            started = If(String.Equals(_startedFolder, folder, StringComparison.Ordinal), _startedJob, "")
            _startedFolder = Nothing
            _startedJob = ""
        End SyncLock
        Dim job As ExchangeAgentDriveJob = If(started.Length > 0,
                                              FollowJob(started, ct),
                                              RunJob(Function(token As CancellationToken) _channel.ListAsync(_resourceId, folder, token), ct))
        If job.Listing Is Nothing Then Throw New ResourceFailureException(DriveFailures.FromReason(ExchangeAgentPipe.ErrorCorrupt))
        Dim entries As New List(Of ResourceEntry)(job.Listing.Entries.Count)
        SyncLock _gate
            _known.Clear()
            For Each item As ExchangeDriveListingEntry In job.Listing.Entries
                ' one name, one level: a producer's reply never gets to steer a path on this machine
                If item.Name.Length = 0 OrElse Not ExchangeDriveRequests.IsRelativePath(item.Name) OrElse ExchangeDriveRequests.Segments(item.Name).Length <> 1 Then Continue For
                Dim entry As New ResourceEntry With {
                    .FullPath = "/" & ExchangeDriveRequests.Join(folder, item.Name),
                    .Name = item.Name,
                    .Length = item.Size,
                    .LastWriteUtc = item.ModifiedAt.UtcDateTime,
                    .IsDirectory = item.IsDirectory}
                _known(entry.FullPath) = entry
                entries.Add(entry)
            Next
        End SyncLock
        Return entries
    End Function

    ''' <summary>From the last listing: asking the other device about one file costs a packet and a minute, and nothing here needs a fresher answer.</summary>
    Public Function TryStat(path As String, ByRef entry As ResourceEntry) As Boolean Implements IFileSystemProvider.TryStat
        entry = Nothing
        If String.IsNullOrEmpty(path) Then Return False
        SyncLock _gate
            Return _known.TryGetValue(path, entry)
        End SyncLock
    End Function

    ''' <summary>Fetches one file into <paramref name="localPath"/>. The agent saves under the file's base name into a folder that must already
    ''' exist in the remote cache - and a transfer's target may lie anywhere - so each call stages in a session folder of its own under the
    ''' cache root, gone when the call ends and swept like any other if the program dies first.</summary>
    Public Sub Download(remotePath As String, localPath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Download
        SyncLock _gate
            _lastFailure = Nothing
        End SyncLock
        Dim folder As String = ""
        Try
            Dim relative As String = ToRelative(remotePath)
            folder = RemoteCacheStore.CreateSessionUnder(_stagingRoot)
            Dim job As ExchangeAgentDriveJob = RunJob(Function(token As CancellationToken) _channel.FetchAsync(_resourceId, {relative}, folder, token), ct)
            Dim saved As String = ""
            For Each item As ExchangeAgentDriveFile In job.Files
                If String.Equals(item.Path, relative, StringComparison.Ordinal) Then saved = item.SavedPath
            Next
            If saved.Length = 0 OrElse Not IsIn(folder, saved) OrElse Not File.Exists(saved) Then
                Throw New ResourceFailureException(DriveFailures.FromReason(ExchangeDriveRequests.ReasonUnavailable))
            End If
            File.Move(saved, localPath, True)
            progress?.Report(New FileInfo(localPath).Length)
        Catch ex As ResourceFailureException
            SyncLock _gate
                _lastFailure = ex.Failure
            End SyncLock
            Throw
        Finally
            RemoteCacheStore.DeleteSession(folder)
        End Try
    End Sub

    Public Sub Upload(localPath As String, remotePath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Upload
        Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.Unsupported))
    End Sub

    Public Sub DeleteFile(remotePath As String) Implements IFileSystemProvider.DeleteFile
        Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.Unsupported))
    End Sub

    Public Sub Rename(remotePath As String, newRemotePath As String) Implements IFileSystemProvider.Rename
        Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.Unsupported))
    End Sub

    Public Sub CreateDirectory(remotePath As String) Implements IFileSystemProvider.CreateDirectory
        Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.Unsupported))
    End Sub

    ''' <summary>"/sub/name" of this provider to the relative path of the wire; "" is the folder's root. A path out of form never reaches
    ''' the agent: the producer's own refusal of it would cost a round trip.</summary>
    Friend Shared Function ToRelative(path As String) As String
        Dim relative As String = If(path, "").Replace("\"c, "/"c).Trim("/"c)
        If Not ExchangeDriveRequests.IsRelativePath(relative) Then Throw New ResourceFailureException(DriveFailures.FromReason(ExchangeDriveRequests.ReasonNotPermitted))
        Return relative
    End Function

    Private Shared Function IsIn(folder As String, candidate As String) As Boolean
        Dim root As String = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
        Return Path.GetFullPath(candidate).StartsWith(root, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>Starts a job and follows it to its end. Blocking, off the UI thread (the session calls every verb from the pool). A cancel,
    ''' and a failure while following, gives the job up so its REQUEST leaves Drive.</summary>
    Private Function RunJob(start As Func(Of CancellationToken, Task(Of ExchangeAgentDriveJobResult)), ct As CancellationToken) As ExchangeAgentDriveJob
        Return FollowJob(StartJob(start, ct).Job.JobId, ct)
    End Function

    Private Function FollowJob(startedJob As String, ct As CancellationToken) As ExchangeAgentDriveJob
        Dim jobId As String = startedJob
        Dim misses As Integer = 0
        Dim watch As New Diagnostics.Stopwatch()
        Try
            Do
                ct.ThrowIfCancellationRequested()
                watch.Restart()
                Dim state As ExchangeAgentDriveJobResult = _channel.JobAsync(jobId, PollWaitMs, ct).GetAwaiter().GetResult()
                If state.Reply.Kind = ExchangeAgentReplyKind.Cancelled Then Throw New OperationCanceledException(ct)
                If state.Reply.IsOk Then
                    misses = 0
                    Note(state.Job)
                    If state.Job.State = ExchangeAgentDriveJobState.Done Then
                        jobId = ""
                        Return state.Job
                    End If
                    If state.Job.State = ExchangeAgentDriveJobState.Failed Then
                        jobId = ""
                        Throw New ResourceFailureException(DriveFailures.FromReason(state.Job.Error))
                    End If
                ElseIf state.Reply.Kind = ExchangeAgentReplyKind.Refused Then
                    ' the agent no longer has the job (it restarted, or a job was answered once): nothing is left to cancel
                    jobId = ""
                    Throw New ResourceFailureException(DriveFailures.FromReason(state.Reply.Error))
                Else
                    misses += 1
                    If misses >= MaxPipeMisses Then Throw New ResourceFailureException(DriveFailures.FromReason(ReasonOfReply(state.Reply)))
                End If
                If watch.ElapsedMilliseconds < MinPollIntervalMs Then
                    ct.WaitHandle.WaitOne(CInt(MinPollIntervalMs - watch.ElapsedMilliseconds))
                End If
            Loop
        Finally
            If jobId.Length > 0 Then GiveUp(jobId)
        End Try
    End Function

    Private Function StartJob(start As Func(Of CancellationToken, Task(Of ExchangeAgentDriveJobResult)), ct As CancellationToken) As ExchangeAgentDriveJobResult
        Dim attempt As Integer = 0
        Do
            ct.ThrowIfCancellationRequested()
            Dim result As ExchangeAgentDriveJobResult = start(ct).GetAwaiter().GetResult()
            If result.Reply.Kind = ExchangeAgentReplyKind.Cancelled Then Throw New OperationCanceledException(ct)
            If result.Reply.IsOk AndAlso result.Job IsNot Nothing Then
                Note(result.Job)
                Return result
            End If
            attempt += 1
            If Not String.Equals(result.Reply.Error, ExchangeAgentPipe.ErrorBusy, StringComparison.Ordinal) OrElse attempt > MaxBusyRetries Then
                Throw New ResourceFailureException(DriveFailures.FromReason(ReasonOfReply(result.Reply)))
            End If
            ct.WaitHandle.WaitOne(1000)
        Loop
    End Function

    ''' <summary>A refusal says its own token; a pipe that did not answer means the Share Manager is not there.</summary>
    Private Shared Function ReasonOfReply(reply As ExchangeAgentReply) As String
        Return If(reply.Kind = ExchangeAgentReplyKind.Refused, reply.Error, ReasonAgentAbsent)
    End Function

    Private Sub GiveUp(jobId As String)
        Try
            _channel.CancelAsync(jobId, CancellationToken.None).GetAwaiter().GetResult()
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is TimeoutException
            ' the agent's own REQUEST expires by itself (item CR): nothing else depends on this answer
            AppFileLogger.WriteLine("Resource: a Google Drive request was not withdrawn - " & ex.GetType().Name)
        End Try
    End Sub

    Private Sub Note(job As ExchangeAgentDriveJob)
        If job Is Nothing Then Return
        SyncLock _gate
            If job.DeviceName.Length > 0 Then _deviceName = job.DeviceName
            If job.LastRecordAt.Length > 0 Then _lastRecordAt = job.LastRecordAt
        End SyncLock
    End Sub

End Class
#End If
