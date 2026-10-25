Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks

' Specification 203 section 5.11, item 2 of 5.9: what this PC writes into the Drive appDataFolder while it is signed in - its own device
' record and one resource record per shared folder (no `access`, see DriveShareRecords) - kept current as the worker's folders change,
' and taken back when a folder goes away.
'
'   - The records are handed to ExchangeDriveDirectory.PublishAsync, which keeps them alive (a rewrite every ttl/3, item C) and refuses
'     anything that is not this device's own. This class decides only WHAT is standing.
'   - It never talks to Google by itself and never reads a secret: the worker's status is read the way the exchange publisher reads it
'     (a read, no spawn), the ids come from the same DPAPI store, and a failure is a kind, never a body or an exception text.
'   - StopAsync ends the loop and waits for a pass in flight: sign-out (criterion 4) deletes this PC's records right after, and a write
'     landing behind that deletion would put a record back that the person just asked to remove.

Friend Interface IDriveChannelPublisher
    Inherits IDisposable

    Sub Start()

    ''' <summary>Ends the loop and returns once no write of this publisher is in flight; later calls are no-ops.</summary>
    Function StopAsync() As Task

    ''' <summary>Look at the folders now instead of at the next interval.</summary>
    Sub Nudge()

End Interface

''' <summary>Everything the publisher needs from outside, so a test hands in fakes and counts the calls.</summary>
Friend NotInheritable Class DriveChannelPublisherOptions

    Public Property GetStatus As Func(Of Task(Of WorkerStatus)) = Function() ShareController.GetStatusAsync()
    Public Property Store As ExchangeProducerStore = ExchangeProducerStore.CreateDefault()
    Public Property RoleFacts As IExchangeRoleFacts = New ExchangeProductionRoleFacts()
    Public Property ProductVersion As String = ExchangeProductVersion.Current()
    Public Property Platform As Func(Of String) = Function() ExchangeDeviceDescription.PlatformText()
    ''' <summary>True when a remote exchange-server account is stored: then the server publisher owns the ids file and this class only reads it.</summary>
    Public Property HasServerAccount As Func(Of Boolean) = Function() ExchangeAccountStore.CreateDefault().Load() IsNot Nothing
    Public Property HasPin As Func(Of String, Boolean) = Function(hostPath) Not String.IsNullOrEmpty(ShareRootParamsStore.GetFor(hostPath)?.AccessPin)
    Public Property NewId As Func(Of String) = AddressOf ExchangeIds.NewId
    Public Property NowUtc As Func(Of DateTime) = Function() DateTime.UtcNow
    Public Property Log As Action(Of String) = Sub(line) AppFileLogger.WriteLine(line)
    ''' <summary>How often the folders are looked at when nothing nudged the loop.</summary>
    Public Property IntervalMs As Integer = 15000
    ''' <summary>How long the worker's status may take.</summary>
    Public Property StatusTimeoutMs As Integer = 4000
    ''' <summary>How long StopAsync waits for a pass in flight (the pass is cancelled first, so this is a ceiling, not a delay).</summary>
    Public Property StopWaitMs As Integer = 5000

End Class

Friend NotInheritable Class DriveChannelPublisher
    Implements IDriveChannelPublisher

    Private ReadOnly _service As ExchangeDriveService
    Private ReadOnly _deviceId As String
    Private ReadOnly _deviceName As String
    Private ReadOnly _options As DriveChannelPublisherOptions
    Private ReadOnly _gate As New Object()
    ' resource id -> signature of the record standing on Drive; touched by the loop only
    Private ReadOnly _standing As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Private _deviceJson As String = ""
    Private _cts As CancellationTokenSource
    Private _loop As Task
    Private _nudge As TaskCompletionSource(Of Boolean)
    Private _nudged As Boolean
    Private _stopped As Boolean
    Private _lastNote As String = ""

    Public Sub New(service As ExchangeDriveService, deviceId As String, deviceName As String, options As DriveChannelPublisherOptions)
        If service Is Nothing Then Throw New ArgumentNullException(NameOf(service))
        If Not ExchangeIds.IsValidId(deviceId) Then Throw New ArgumentException("the device id is not in the id form", NameOf(deviceId))
        _service = service
        _deviceId = deviceId
        _deviceName = If(deviceName, "")
        _options = If(options, New DriveChannelPublisherOptions())
    End Sub

    ''' <summary>The production wiring: the real worker, the real ids file.</summary>
    Public Shared Function CreateFor(service As ExchangeDriveService, deviceId As String, deviceName As String) As IDriveChannelPublisher
        Return New DriveChannelPublisher(service, deviceId, deviceName, New DriveChannelPublisherOptions())
    End Function

    ''' <summary>How many folders this PC lists on Drive right now (read by a test and by the page).</summary>
    Public ReadOnly Property ListedFolders As Integer
        Get
            SyncLock _gate
                Return _standing.Count
            End SyncLock
        End Get
    End Property

    Public Sub Start() Implements IDriveChannelPublisher.Start
        SyncLock _gate
            If _stopped OrElse _loop IsNot Nothing Then Return
            _cts = New CancellationTokenSource()
            Dim token As CancellationToken = _cts.Token
            _loop = Task.Run(Function() RunAsync(token))
        End SyncLock
    End Sub

    Public Sub Nudge() Implements IDriveChannelPublisher.Nudge
        Dim waiting As TaskCompletionSource(Of Boolean)
        SyncLock _gate
            If _stopped Then Return
            _nudged = True
            waiting = _nudge
        End SyncLock
        If waiting IsNot Nothing Then waiting.TrySetResult(True)
    End Sub

    Public Async Function StopAsync() As Task Implements IDriveChannelPublisher.StopAsync
        Dim source As CancellationTokenSource
        Dim running As Task
        SyncLock _gate
            _stopped = True
            source = _cts
            running = _loop
        End SyncLock
        If source Is Nothing Then Return
        Try
            source.Cancel()
        Catch ex As ObjectDisposedException
        End Try
        If running IsNot Nothing Then
            Try
                Await Task.WhenAny(running, Task.Delay(_options.StopWaitMs)).ConfigureAwait(False)
            Catch ex As Exception When TypeOf ex Is OperationCanceledException
            End Try
        End If
        SyncLock _gate
            _standing.Clear()
        End SyncLock
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            StopAsync().GetAwaiter().GetResult()
        Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is ObjectDisposedException
        End Try
        Dim source As CancellationTokenSource
        SyncLock _gate
            source = _cts
            _cts = Nothing
        End SyncLock
        If source IsNot Nothing Then source.Dispose()
    End Sub

    ' ------------------------------------------------------------------------------------------ the loop

    Private Async Function RunAsync(ct As CancellationToken) As Task
        While Not ct.IsCancellationRequested
            Try
                Await PassAsync(ct).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                Return
            Catch ex As ExchangeDriveException
                ' Drive could not be reached or refused: the standing set is unchanged (a record is marked written only after its write
                ' succeeded), so the next pass tries the same again. The service's own loop owns the back-off and the signed-out verdict.
                Note("Drive channel: the listing of this PC's folders failed (" & ex.Failure.ToString() & ")")
                If ex.Failure = ExchangeDriveFailure.SignedOut Then Return
            Catch ex As Exception When Not (TypeOf ex Is OutOfMemoryException OrElse TypeOf ex Is StackOverflowException OrElse TypeOf ex Is AccessViolationException)
                Note("Drive channel: the listing of this PC's folders failed (" & ex.GetType().Name & ")")
            End Try
            Try
                Await WaitAsync(ct).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                Return
            End Try
        End While
    End Function

    Private Async Function WaitAsync(ct As CancellationToken) As Task
        Dim tcs As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        SyncLock _gate
            If _nudged Then
                _nudged = False
                Return
            End If
            _nudge = tcs
        End SyncLock
        Try
            Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                Dim delay As Task = Task.Delay(Math.Max(100, _options.IntervalMs), linked.Token)
                Using linked.Token.Register(Sub() tcs.TrySetCanceled())
                    Dim winner As Task = Await Task.WhenAny(tcs.Task, delay).ConfigureAwait(False)
                    linked.Cancel()
                    If winner Is tcs.Task Then Await tcs.Task.ConfigureAwait(False)
                End Using
            End Using
        Finally
            SyncLock _gate
                _nudge = Nothing
                _nudged = False
            End SyncLock
        End Try
    End Function

    ''' <summary>One look at the world, then Drive made to match: the device record, the folders that are new or changed, and the folders that
    ''' are gone. Public for the tests; the loop is the only other caller.</summary>
    Public Async Function PassAsync(ct As CancellationToken) As Task
        ' Stopped is the state before the first round answered, so only SignedOut (no sign-in, or Google no longer honours it) holds back
        If _service.State = ExchangeDriveServiceState.SignedOut Then Return
        Dim records As ExchangeDriveDirectory = _service.Records

        ' the device record: always while signed in, even with no folder (a device list shows the PC)
        Dim deviceJson As String = DriveShareRecords.BuildDevice(_deviceId, _deviceName, _options.ProductVersion, _options.Platform.Invoke(),
                                                                 ExchangeRoles.Compute(_options.RoleFacts))
        If deviceJson.Length > 0 AndAlso Not String.Equals(deviceJson, _deviceJson, StringComparison.Ordinal) Then
            ct.ThrowIfCancellationRequested()
            Await records.PublishAsync(ExchangeDriveRecordKind.Device, _deviceId, deviceJson, 0, ct).ConfigureAwait(False)
            _deviceJson = deviceJson
        End If

        Dim status As WorkerStatus = Await ReadStatusAsync(ct).ConfigureAwait(False)
        ct.ThrowIfCancellationRequested()
        Dim mayCreate As Boolean = Not _options.HasServerAccount.Invoke()
        Dim state As ExchangeProducerState = _options.Store.Load(_deviceId)
        Dim plan As DriveSharePlan = DriveShareRecords.Plan(status, state, _deviceId, mayCreate, _options.NewId, _options.HasPin, _options.NowUtc.Invoke())
        If plan.StateChanged AndAlso Not _options.Store.Save(_deviceId, plan.State) Then
            ' an id that cannot be kept would be a different resource after the next start: nothing new is listed this pass
            Note("Drive channel: the folder ids could not be stored under DPAPI, no new folder is listed")
            plan = New DriveSharePlan(plan.Resources.Where(Function(r) state.Entries.Values.Any(Function(e) String.Equals(e.ResourceId, r.ResourceId, StringComparison.Ordinal))).ToList(),
                                      state, False, plan.WaitingForIds)
        End If

        ' what stands on Drive and is no longer wanted goes first
        Dim wanted As New HashSet(Of String)(plan.Resources.Select(Function(r) r.ResourceId), StringComparer.Ordinal)
        Dim gone As List(Of String)
        SyncLock _gate
            gone = _standing.Keys.Where(Function(id) Not wanted.Contains(id)).ToList()
        End SyncLock
        For Each id As String In gone
            ct.ThrowIfCancellationRequested()
            Await records.WithdrawAsync(ExchangeDriveRecordKind.Resource, id, ct).ConfigureAwait(False)
            SyncLock _gate
                _standing.Remove(id)
            End SyncLock
        Next

        For Each item As DrivePlannedResource In plan.Resources
            Dim same As Boolean
            SyncLock _gate
                Dim signature As String = Nothing
                same = _standing.TryGetValue(item.ResourceId, signature) AndAlso String.Equals(signature, item.Signature, StringComparison.Ordinal)
            End SyncLock
            If same Then Continue For
            ct.ThrowIfCancellationRequested()
            Await records.PublishAsync(ExchangeDriveRecordKind.Resource, item.ResourceId, item.Json, 0, ct).ConfigureAwait(False)
            SyncLock _gate
                _standing(item.ResourceId) = item.Signature
            End SyncLock
        Next
    End Function

    Private Async Function ReadStatusAsync(ct As CancellationToken) As Task(Of WorkerStatus)
        Try
            Using cts As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                cts.CancelAfter(_options.StatusTimeoutMs)
                Dim read As Task(Of WorkerStatus) = _options.GetStatus.Invoke()
                Dim winner As Task = Await Task.WhenAny(read, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(False)
                If winner Is read Then Return Await read.ConfigureAwait(False)
            End Using
        Catch ex As OperationCanceledException When Not ct.IsCancellationRequested
        Catch ex As Exception When Not (TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is OutOfMemoryException)
            Note("Drive channel: the worker status could not be read (" & ex.GetType().Name & ")")
        End Try
        Return Nothing
    End Function

    ''' <summary>Says a line once per distinct note, so a standing failure does not fill the log every interval.</summary>
    Private Sub Note(line As String)
        If String.Equals(line, _lastNote, StringComparison.Ordinal) Then Return
        _lastNote = line
        _options.Log.Invoke(line)
    End Sub

End Class
