Option Strict On

Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 269 P3.2 - the agent's half of the viewer's Drive requests: DriveStart, DriveJob, DriveCancel on the local pipe
' (ExchangeAgentPipe). The request logic is DriveRequestConsumer; this file finds the device behind a folder listed on Drive,
' checks where a fetch may save, and keeps the jobs.

Partial Friend NotInheritable Class ExchangeDeviceAgent

    ''' <summary>The job registry, created on the first request; Nothing once the agent is disposed.</summary>
    Private Function DriveJobs() As DriveRequestJobs
        SyncLock _gate
            If _disposed Then Return Nothing
            If _driveJobs Is Nothing Then _driveJobs = New DriveRequestJobs()
            Return _driveJobs
        End SyncLock
    End Function

    ''' <summary>DriveStart: `action` (`list` or `fetch`), `resourceId`, `path` or `paths`, and for a fetch the `directory` under the
    ''' viewer's remote cache. Answers the waiting job at once; refuses a folder that is not listed on Drive (`unknown-resource`), no
    ''' Drive sign-in (`drive-offline`), a request out of form or a directory outside the cache (`bad-request`), too many jobs (`busy`).</summary>
    Private Function StartDriveJob(request As JsonElement) As ExchangeLocalResult
        If request.ValueKind <> JsonValueKind.Object Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorBadRequest)
        Dim parsed As ExchangeDriveRequest = Nothing
        If Not ExchangeDriveRequests.TryParse(request.GetRawText(), parsed) Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorBadRequest)
        If DriveRequestConsumer.Refusal(parsed).Length > 0 Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorBadRequest)

        ' only a folder another PC lists on Drive is asked for this way: the record names the device that answers
        Dim listing As ExchangeDirectorySnapshot = DriveListing()
        Dim resource As ExchangeResourceRecord = Nothing
        For Each r As ExchangeResourceRecord In listing.Resources
            If String.Equals(r.ResourceId, parsed.ResourceId, StringComparison.Ordinal) Then resource = r
        Next
        If resource Is Nothing OrElse Not ExchangeIds.IsValidId(resource.DeviceId) Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorUnknownResource)
        Dim deviceName As String = ""
        Dim lastRecordAt As String = ""
        For Each d As ExchangeDeviceRecord In listing.Devices
            If String.Equals(d.DeviceId, resource.DeviceId, StringComparison.Ordinal) Then
                deviceName = d.DeviceName
                lastRecordAt = d.LastSeen
            End If
        Next

        Dim directory As String = ""
        If parsed.Action = ExchangeDriveRequestAction.Fetch Then
            directory = ReadString(request, "directory")
            Dim root As Func(Of String) = _ports.RemoteCacheRoot
            If Not DriveRequestConsumer.IsCacheFolder(directory, If(root Is Nothing, "", root.Invoke())) Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorBadRequest)
        End If

        Dim portSource As Func(Of IDriveRequestPort) = _ports.DriveRequests
        Dim port As IDriveRequestPort = If(portSource Is Nothing OrElse Not _ports.HasDriveSignIn(), Nothing, portSource.Invoke())
        If port Is Nothing Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorDriveOffline)
        Dim jobs As DriveRequestJobs = DriveJobs()
        If jobs Is Nothing Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorNotConnected)

        Dim deviceId As String = resource.DeviceId
        Dim id As String = jobs.Start(deviceName, Function(token As CancellationToken) DriveRequestConsumer.RunAsync(port, parsed, deviceId, deviceName, directory, token))
        If id.Length = 0 Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorBusy)
        _ports.Log.Invoke("Exchange agent: a Google Drive " & ExchangeDriveRequests.ActionText(parsed.Action) & " request was sent")
        Return ExchangeLocalResult.Success(ExchangeAgentDriveJob.ToJson(id, ExchangeAgentDriveJobState.Waiting, "", deviceName, Nothing, Nothing, lastRecordAt))
    End Function

    ''' <summary>DriveJob: `jobId`, `waitMs` (up to 30 s). A state longer than the pipe's reply bound (a listing of a very large folder)
    ''' is answered `too-large`.</summary>
    Private Async Function ReadDriveJobAsync(request As JsonElement, ct As CancellationToken) As Task(Of ExchangeLocalResult)
        Dim jobs As DriveRequestJobs = DriveJobs()
        If jobs Is Nothing Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorUnknownJob)
        Dim waitMs As Integer = CInt(Math.Min(Math.Max(ReadLong(request, "waitMs", 0), 0), DriveRequestJobs.MaxWaitMs))
        Dim state As String = Await jobs.WaitAsync(ReadString(request, "jobId"), waitMs, ct).ConfigureAwait(False)
        If state Is Nothing Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorUnknownJob)
        ' the reply line also carries `ok` and `error`: keep a margin under the client's bound
        If Encoding.UTF8.GetByteCount(state) > ExchangeAgentPipe.MaxReplyBytes - 1024 Then Return ExchangeLocalResult.Failure(ExchangeDriveRequests.ReasonTooLarge)
        Return ExchangeLocalResult.Success(state)
    End Function

    ''' <summary>DriveCancel: `jobId`. The job's REQUEST is withdrawn from Drive in the background.</summary>
    Private Function CancelDriveJob(request As JsonElement) As ExchangeLocalResult
        Dim jobs As DriveRequestJobs = DriveJobs()
        If jobs Is Nothing OrElse Not jobs.Cancel(ReadString(request, "jobId")) Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorUnknownJob)
        Return ExchangeLocalResult.Success("null")
    End Function

End Class
