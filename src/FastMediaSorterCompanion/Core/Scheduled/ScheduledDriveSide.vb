Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading

' A scheduled operation through Google Drive (ticket 269 P4, ticket 231 rule 3, DEVICE-EXCHANGE items CO, CN, CR, CS).
'
' As a DESTINATION the side sends FILES packets, targeted at one of the person's devices or at none. A run counts a file as gone when
' the manifest of its packet landed (item CO); the device takes it later, and that is the channel's own business.
'
' As a SOURCE the side fills a temporary folder of this PC, and from then on the executor works as with any folder: filter, plan,
' copy, verify. Two forms:
'   - with DriveResourceId: a `changes` request to the device that lists the resource. `modifiedAfter` is the `asOf` of the previous
'     clean run, never this PC's clock, and it moves only when the whole run was clean (item CO).
'   - without: the FILES packets other devices sent to this PC. A packet leaves Drive only after every file of it reached the
'     destination (231 rule 3): a run that fails half way loses nothing, it takes the packet again.
'
' A run is unattended and asks nothing. Drive that does not answer, or a device that does not answer in the 120 s of item CQ, skips the
' run (231 rule 4); no room on Drive, a lost sign-in or a refusal by the device stops it as a wall.

''' <summary>A destination that takes whole packets rather than one file at a time.</summary>
Friend Interface IScheduledPacketTarget
    Function AcceptsName(name As String) As Boolean
    ReadOnly Property MaxPacketFiles As Integer
    ReadOnly Property MaxPacketBytes As Long
    ''' <summary>The packet landed, or: <see cref="ScheduledSideException"/> when the destination itself is lost, and
    ''' <see cref="ScheduledTransferException"/> when only this packet cannot go.</summary>
    Sub SendPacket(files As IReadOnlyList(Of ScheduledPacketFile), ct As CancellationToken)
End Interface

Friend NotInheritable Class ScheduledPacketFile
    Friend ReadOnly Name As String
    Friend ReadOnly Path As String

    Friend Sub New(name As String, path As String)
        Me.Name = name
        Me.Path = path
    End Sub
End Class

''' <summary>A source that has to know how the run it serves began and how it ended.</summary>
Friend Interface IScheduledRunAwareSource
    ''' <summary>Before the source is listed.</summary>
    Sub RunStarting(op As ScheduledOperation)
    ''' <summary>The action is over, however it ended. <paramref name="completed"/> is False after a wall or a cancellation. Never throws.</summary>
    Sub RunEnded(result As ScheduledRunResult, completed As Boolean)
End Interface

Friend NotInheritable Class DriveScheduledSide
    Implements IScheduledSide, IScheduledPacketTarget, IScheduledRunAwareSource

    Private Shared ReadOnly QuotaBound As TimeSpan = TimeSpan.FromSeconds(60)
    Private Shared ReadOnly ConfirmBound As TimeSpan = TimeSpan.FromSeconds(30)

    Private NotInheritable Class TakenPacket
        Friend ReadOnly Packet As ExchangeDrivePacket
        Friend ReadOnly Paths As IReadOnlyList(Of String)

        Friend Sub New(packet As ExchangeDrivePacket, paths As IReadOnlyList(Of String))
            Me.Packet = packet
            Me.Paths = paths
        End Sub
    End Class

    Private ReadOnly _location As ScheduledLocation
    Private ReadOnly _queue As ExchangeDriveQueue
    Private ReadOnly _client As ExchangeDriveClient
    Private ReadOnly _port As IDriveRequestPort
    Private ReadOnly _afterSend As Action
    Private ReadOnly _taken As New List(Of TakenPacket)()
    Private _stage As String = ""
    Private _previousAsOf As String = ""
    Private _asOf As String = ""
    Private _incomplete As Integer
    Private _incompleteName As String = ""

    Friend Sub New(location As ScheduledLocation, queue As ExchangeDriveQueue, client As ExchangeDriveClient, port As IDriveRequestPort, afterSend As Action)
        If location Is Nothing Then Throw New ArgumentNullException(NameOf(location))
        If queue Is Nothing Then Throw New ArgumentNullException(NameOf(queue))
        If client Is Nothing Then Throw New ArgumentNullException(NameOf(client))
        _location = location
        _queue = queue
        _client = client
        _port = port
        _afterSend = afterSend
    End Sub

    ' --- the side ------------------------------------------------------------------------------------

    ''' <summary>False on purpose: what stages here is a temporary copy, so the delete policy and its Recycle Bin never see it.</summary>
    Public ReadOnly Property IsLocalPath As Boolean Implements IScheduledSide.IsLocalPath
        Get
            Return False
        End Get
    End Property

    Public ReadOnly Property Root As String Implements IScheduledSide.Root
        Get
            Return If(_location.ResourceName.Length > 0, _location.ResourceName, ScheduledLocation.DriveLabel)
        End Get
    End Property

    Public Function Describe() As String Implements IScheduledSide.Describe
        Return _location.Describe()
    End Function

    ''' <summary>Asked before anything is scanned, and from the wall's own silent check: no request is made for it.</summary>
    Public Function CheckReadable() As SideProblem Implements IScheduledSide.CheckReadable
        Return SideProblem.Ok
    End Function

    ''' <summary>The room on the person's Drive, in a bounded time.</summary>
    Public Function CheckWritable() As SideProblem Implements IScheduledSide.CheckWritable
        Using bound As New CancellationTokenSource(QuotaBound)
            Try
                Dim quota As ExchangeDriveQuota = _client.GetStorageQuotaAsync(bound.Token).GetAwaiter().GetResult()
                Dim free As Long
                If Not ExchangeDrivePackets.FitsQuota(quota.LimitBytes, quota.UsageBytes, 1, free) Then
                    Return New SideProblem(SideProblemKind.DriveFull, Describe())
                End If
                Return SideProblem.Ok
            Catch ex As ExchangeDriveException
                Return New SideProblem(ProblemOf(ex), Describe())
            Catch ex As OperationCanceledException
                Return New SideProblem(SideProblemKind.DriveUnavailable, Describe())
            End Try
        End Using
    End Function

    ''' <summary>A staged file once the source has been listed; on a destination the name its packet gives it.</summary>
    Public Function Combine(relative As String) As String Implements IScheduledSide.Combine
        Return If(_stage.Length > 0, Path.Combine(_stage, relative), relative)
    End Function

    ''' <summary>Nothing is ever overwritten on Drive: a packet is a new folder, and the receiver saves under a free name (item CN).</summary>
    Public Function Exists(relative As String) As Boolean Implements IScheduledSide.Exists
        Return False
    End Function

    Public Function List(recursive As Boolean, ct As CancellationToken) As List(Of SideEntry) Implements IScheduledSide.List
        _stage = Path.Combine(Path.GetTempPath(), "fms-scheduled-drive-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(_stage)
        Try
            If _location.DriveResourceId.Length > 0 Then
                AskTheDevice(ct)
            Else
                PickUp(ct)
            End If
        Catch ex As ExchangeDriveException
            Throw New ScheduledSideException(New SideProblem(ProblemOf(ex), Describe()))
        End Try
        Return StagedEntries()
    End Function

    Private Sub AskTheDevice(ct As CancellationToken)
        Dim request As New ExchangeDriveRequest() With {.Action = ExchangeDriveRequestAction.Changes, .ResourceId = _location.DriveResourceId}
        Dim previous As DateTimeOffset
        If _previousAsOf.Length > 0 AndAlso ExchangeDriveRequests.TryParseInstant(_previousAsOf, previous) Then
            request.ModifiedAfter = previous
            request.HasModifiedAfter = True
        End If
        Dim outcome As DriveChangesOutcome = DriveRequestConsumer.RunChangesAsync(_port, request, _location.ResourceId, _location.DriveDeviceName, _stage, ct).GetAwaiter().GetResult()
        If Not outcome.Succeeded Then Throw New ScheduledSideException(New SideProblem(ProblemOfAnswer(outcome.Error), Describe()))
        _asOf = outcome.AsOf
    End Sub

    Private Sub PickUp(ct As CancellationToken)
        Dim listing As ExchangeDriveListing = _client.ListAllAsync(ct).GetAwaiter().GetResult()
        Dim poll As ExchangeDriveQueuePoll = _queue.PollAsync(listing, ct).GetAwaiter().GetResult()
        For Each packet As ExchangeDrivePacket In _queue.IncomingFiles(poll)
            ct.ThrowIfCancellationRequested()
            Dim received As ExchangeDriveReceiveResult = _queue.DownloadFilesAsync(packet, _stage, ct).GetAwaiter().GetResult()
            If received.Succeeded Then
                _taken.Add(New TakenPacket(packet, received.SavedPaths))
                Continue For
            End If
            ' Not whole: nothing of it goes on, and it stays on Drive for the next run.
            For Each saved As String In received.SavedPaths
                TryDeleteFile(saved)
            Next
            _incomplete += 1
            If _incompleteName.Length = 0 Then _incompleteName = received.FailedName
        Next
    End Sub

    Private Function StagedEntries() As List(Of SideEntry)
        Dim result As New List(Of SideEntry)()
        For Each info As FileInfo In New DirectoryInfo(_stage).EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            If info.Name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) AndAlso info.Name.StartsWith(".fmsgd-", StringComparison.OrdinalIgnoreCase) Then Continue For
            result.Add(New SideEntry With {.RelativePath = info.Name, .Name = info.Name, .Length = info.Length, .LastWriteUtc = info.LastWriteTimeUtc})
        Next
        Return result
    End Function

    ''' <summary>The permanent removal of a staged copy, when it is still the copy that was listed.</summary>
    Friend Function DeleteStaged(entry As SideEntry) As String
        Dim staged As String = Combine(entry.RelativePath)
        If Not LocalTransfer.SameAsListed(staged, entry) Then Throw New SourceChangedException(staged)
        File.Delete(staged)
        Return "permanent:VolumeUnknown"
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        If _stage.Length = 0 Then Return
        Try
            Directory.Delete(_stage, True)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            AppFileLogger.LogException("Scheduled operations: removing the Drive stage", ex)
        End Try
    End Sub

    ' --- the destination -----------------------------------------------------------------------------

    Public Function AcceptsName(name As String) As Boolean Implements IScheduledPacketTarget.AcceptsName
        Return ExchangeDrivePackets.IsValidPayloadName(name)
    End Function

    Public ReadOnly Property MaxPacketFiles As Integer Implements IScheduledPacketTarget.MaxPacketFiles
        Get
            Return ExchangeDrivePackets.MaxFilesPerPacket
        End Get
    End Property

    Public ReadOnly Property MaxPacketBytes As Long Implements IScheduledPacketTarget.MaxPacketBytes
        Get
            Return _queue.PacketBytes
        End Get
    End Property

    Public Sub SendPacket(files As IReadOnlyList(Of ScheduledPacketFile), ct As CancellationToken) Implements IScheduledPacketTarget.SendPacket
        Dim sources As New List(Of ExchangeDriveSendSource)(files.Count)
        For Each item As ScheduledPacketFile In files
            sources.Add(ExchangeDriveSendSource.FromFile(item.Path, item.Name, ExchangeDriveSendSource.MimeTypeOf(item.Name)))
        Next
        Dim sent As ExchangeDriveSendResult
        Try
            sent = _queue.SendFilesAsync(sources, _location.ResourceId, _location.DriveDeviceName, ct).GetAwaiter().GetResult()
        Catch ex As ExchangeDriveException
            Throw New ScheduledSideException(New SideProblem(ProblemOf(ex), Describe()))
        End Try
        Select Case sent.Outcome
            Case ExchangeDriveSendOutcome.Sent
                _afterSend?.Invoke()
            Case ExchangeDriveSendOutcome.DriveFull
                Throw New ScheduledSideException(New SideProblem(SideProblemKind.DriveFull, Describe()))
            Case ExchangeDriveSendOutcome.TooLarge
                Throw New ScheduledTransferException("too-large")
            Case Else
                Throw New ScheduledTransferException("invalid")
        End Select
    End Sub

    ' --- how the run went ----------------------------------------------------------------------------

    Public Sub RunStarting(op As ScheduledOperation) Implements IScheduledRunAwareSource.RunStarting
        _previousAsOf = ""
        Dim mark As String = If(op.Record?.DriveMark, "")
        Dim bar As Integer = mark.LastIndexOf("|"c)
        If bar > 0 AndAlso String.Equals(mark.Substring(0, bar), MarkKey(), StringComparison.Ordinal) Then _previousAsOf = mark.Substring(bar + 1)
    End Sub

    Public Sub RunEnded(result As ScheduledRunResult, completed As Boolean) Implements IScheduledRunAwareSource.RunEnded
        ConfirmWhatLeft()
        If _incomplete > 0 Then
            result.Failed += _incomplete
            If result.FirstError.Length = 0 Then result.FirstError = ScheduledFailure.Coded("mismatch", _incompleteName)
        End If
        If completed AndAlso result.Failed = 0 AndAlso result.Outcome = ScheduledRunOutcome.None AndAlso _asOf.Length > 0 Then
            result.DriveMark = MarkKey() & "|" & _asOf
        End If
    End Sub

    ''' <summary>A packet leaves Drive once every file of it left the stage for the destination; a cancelled run confirms too, so what
    ''' already reached the destination does not come again. A file still staged - filtered out, or its name taken - keeps its packet.</summary>
    Private Sub ConfirmWhatLeft()
        For Each taken As TakenPacket In _taken
            If taken.Paths.Any(Function(p As String) File.Exists(p)) Then Continue For
            Using bound As New CancellationTokenSource(ConfirmBound)
                Try
                    _queue.ConfirmTakenAsync(taken.Packet, bound.Token).GetAwaiter().GetResult()
                Catch ex As Exception When TypeOf ex Is ExchangeDriveException OrElse TypeOf ex Is OperationCanceledException
                    ' Handled here, so it is not taken twice; on Drive it stays until it expires (item CR).
                End Try
            End Using
        Next
    End Sub

    ''' <summary>The source the mark was read from: a re-pointed operation starts over.</summary>
    Private Function MarkKey() As String
        Return _location.ResourceId & "/" & _location.DriveResourceId & "/" & _location.Path
    End Function

    Private Shared Function ProblemOf(ex As ExchangeDriveException) As SideProblemKind
        Select Case ex.Failure
            Case ExchangeDriveFailure.SignedOut : Return SideProblemKind.SignedOut
            Case ExchangeDriveFailure.DriveFull : Return SideProblemKind.DriveFull
            Case Else : Return SideProblemKind.DriveUnavailable
        End Select
    End Function

    Private Shared Function ProblemOfAnswer(token As String) As SideProblemKind
        Select Case token
            Case ExchangeAgentPipe.ErrorNoAnswer : Return SideProblemKind.Offline
            Case ExchangeAgentPipe.ErrorDriveOffline, ExchangeAgentPipe.ErrorCorrupt : Return SideProblemKind.DriveUnavailable
            Case ExchangeDriveRequests.ReasonDriveFull : Return SideProblemKind.DriveFull
            Case ExchangeDriveRequests.ReasonPinRequired : Return SideProblemKind.PinLocked
            Case Else : Return SideProblemKind.Unreachable
        End Select
    End Function

    Private Shared Sub TryDeleteFile(path As String)
        Try
            File.Delete(path)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            AppFileLogger.LogException("Scheduled operations: removing a staged file", ex)
        End Try
    End Sub

End Class

''' <summary>One row of the Drive picker of the operation editor.</summary>
Friend NotInheritable Class ScheduledDriveChoice
    ''' <summary>"" when no one device is meant.</summary>
    Friend ReadOnly DeviceId As String
    Friend ReadOnly DeviceName As String
    ''' <summary>"" when no resource of that device is meant.</summary>
    Friend ReadOnly ResourceId As String
    ''' <summary>What the row and the operation show; "" for the row that names no device.</summary>
    Friend ReadOnly Label As String

    Friend Sub New(deviceId As String, deviceName As String, resourceId As String, label As String)
        Me.DeviceId = If(deviceId, "")
        Me.DeviceName = If(deviceName, "")
        Me.ResourceId = If(resourceId, "")
        Me.Label = If(label, "")
    End Sub

    ''' <summary>The rows a source offers: first what other devices send here, then every resource they list. A protected resource is
    ''' not offered: a run asks nothing, and its PIN could not be given.</summary>
    Friend Shared Function ForSource(directory As ExchangeDirectorySnapshot) As List(Of ScheduledDriveChoice)
        Dim result As New List(Of ScheduledDriveChoice) From {New ScheduledDriveChoice("", "", "", "")}
        For Each resource As ExchangeResourceRecord In directory.Resources
            If resource.IsProtected Then Continue For
            Dim device As ExchangeDeviceRecord = directory.Devices.FirstOrDefault(Function(d As ExchangeDeviceRecord) String.Equals(d.DeviceId, resource.DeviceId, StringComparison.Ordinal))
            If device Is Nothing Then Continue For
            result.Add(New ScheduledDriveChoice(device.DeviceId, device.DeviceName, resource.ResourceId, device.DeviceName & ": " & resource.Name))
        Next
        Return result
    End Function

    ''' <summary>The rows a destination offers: first any own device, then each device listed.</summary>
    Friend Shared Function ForDestination(directory As ExchangeDirectorySnapshot) As List(Of ScheduledDriveChoice)
        Dim result As New List(Of ScheduledDriveChoice) From {New ScheduledDriveChoice("", "", "", "")}
        For Each device As ExchangeDeviceRecord In directory.Devices
            result.Add(New ScheduledDriveChoice(device.DeviceId, device.DeviceName, "", device.DeviceName))
        Next
        Return result
    End Function
End Class
