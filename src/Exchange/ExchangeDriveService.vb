#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Net.Http
Imports System.Threading

' Specification 203 - the Drive channel as one object a host (the Share Manager) owns. It ties the pieces together and adds
' the one thing none of them has: a clock.
'
'   session    the sign-in, the access token and the sign-out (ExchangeDriveSession)
'   records    the directory of devices, shares and broadcasts (ExchangeDriveDirectory, section 3.2)
'   packets    the packet queue (ExchangeDriveQueue, section 3.3)
'   poller     item CQ: Drive pushes nothing to an installed program, so a loop lists the appDataFolder ONCE per interval
'              and hands that one listing to both the directory and the queue - 10 s while a screen waits for something,
'              60 s otherwise - and backs off 1 s doubling to 5 min on a rate limit or a server error.
'
' Dormant by construction (204 section 2): until the person has signed in nothing here touches the network, the disk or a
' timer - Start() on a signed-out PC only reports SignedOut. A refresh token that Google stops honouring ends the loop the
' same way (CT) and the host shows the sign-in again; nothing is deleted then but the dead token.
'
' This file produces no user text. The host turns a state and a failure kind into words.

''' <summary>Where the channel stands, for the host's status line.</summary>
Friend Enum ExchangeDriveServiceState
    ''' <summary>Not started (or stopped).</summary>
    Stopped
    ''' <summary>No sign-in, or Google no longer honours the stored token: the person signs in (CT).</summary>
    SignedOut
    ''' <summary>The last round reached Drive.</summary>
    Running
    ''' <summary>The last round failed in a way that may pass (network, rate limit, server error): trying again, backing off.</summary>
    Unavailable
End Enum

''' <summary>What one polling round learned: the directory and the queue, read from the same listing.</summary>
Friend NotInheritable Class ExchangeDriveUpdate

    Public ReadOnly Snapshot As ExchangeDriveDirectoryResult
    Public ReadOnly Poll As ExchangeDriveQueuePoll

    Public Sub New(snapshot As ExchangeDriveDirectoryResult, poll As ExchangeDriveQueuePoll)
        Me.Snapshot = snapshot
        Me.Poll = poll
    End Sub
End Class

''' <summary>How a sign-out ended (criterion 4). The token file is always gone; these say what Google and the folder confirmed.</summary>
Friend NotInheritable Class ExchangeDriveSignOutResult

    ''' <summary>This device's records are deleted from the appDataFolder. False when Drive could not be reached: they then
    ''' stop counting by themselves within minutes (item C) and any other device sweeps them.</summary>
    Public ReadOnly RecordsDeleted As Boolean
    ''' <summary>How many records were deleted.</summary>
    Public ReadOnly DeletedCount As Integer
    ''' <summary>Google confirmed the revocation of the refresh token. False: the person finishes it at myaccount.google.com.</summary>
    Public ReadOnly RevocationConfirmed As Boolean

    Public Sub New(recordsDeleted As Boolean, deletedCount As Integer, revocationConfirmed As Boolean)
        Me.RecordsDeleted = recordsDeleted
        Me.DeletedCount = deletedCount
        Me.RevocationConfirmed = revocationConfirmed
    End Sub
End Class

''' <summary>The Drive channel of one PC: session, directory, queue and the poller that serves both.</summary>
Friend NotInheritable Class ExchangeDriveService
    Implements IDisposable

    Private ReadOnly _session As ExchangeDriveSession
    Private ReadOnly _client As ExchangeDriveClient
    Private ReadOnly _records As ExchangeDriveDirectory
    Private ReadOnly _packets As ExchangeDriveQueue
    Private ReadOnly _delay As Func(Of TimeSpan, CancellationToken, Task)
    Private ReadOnly _random As Func(Of Double)
    Private ReadOnly _backoff As New ExchangeDrivePolling.Backoff()
    Private ReadOnly _lock As New Object()

    Private _loopTask As Task
    Private _stopSource As CancellationTokenSource
    Private _waitSource As CancellationTokenSource
    Private _active As Boolean
    Private _state As ExchangeDriveServiceState = ExchangeDriveServiceState.Stopped
    Private _failure As ExchangeDriveFailure = ExchangeDriveFailure.None
    Private _latest As ExchangeDriveUpdate

    ''' <summary>Raised on a pool thread after a round that reached Drive. The host marshals to its UI thread.</summary>
    Public Event Updated As EventHandler(Of ExchangeDriveUpdate)
    ''' <summary>Raised on a pool thread when <see cref="State"/> changes.</summary>
    Public Event StateChanged As EventHandler

    Public Sub New(session As ExchangeDriveSession, client As ExchangeDriveClient, records As ExchangeDriveDirectory, packets As ExchangeDriveQueue,
                   Optional delay As Func(Of TimeSpan, CancellationToken, Task) = Nothing, Optional random As Func(Of Double) = Nothing)
        If session Is Nothing Then Throw New ArgumentNullException(NameOf(session))
        If client Is Nothing Then Throw New ArgumentNullException(NameOf(client))
        If records Is Nothing Then Throw New ArgumentNullException(NameOf(records))
        If packets Is Nothing Then Throw New ArgumentNullException(NameOf(packets))
        _session = session
        _client = client
        _records = records
        _packets = packets
        _delay = If(delay, Function(span As TimeSpan, token As CancellationToken) Task.Delay(span, token))
        Dim generator As New Random()
        _random = If(random, Function() generator.NextDouble())
    End Sub

    ''' <summary>The whole channel for this PC, on the real HTTP stack, the stored session and the real clocks.</summary>
    Public Shared Function Create(http As HttpClient, store As ExchangeDriveTokenStore, config As ExchangeDriveClientConfig,
                                  ownDeviceId As String, ownDeviceName As String) As ExchangeDriveService
        Dim session As New ExchangeDriveSession(http, store, config)
        Dim client As New ExchangeDriveClient(http, session)
        Return New ExchangeDriveService(session, client, New ExchangeDriveDirectory(client, ownDeviceId),
                                        New ExchangeDriveQueue(client, ownDeviceId, ownDeviceName, ExchangeDriveFileHandledStore.CreateDefault()))
    End Function

    Public ReadOnly Property Session As ExchangeDriveSession
        Get
            Return _session
        End Get
    End Property

    Public ReadOnly Property Client As ExchangeDriveClient
        Get
            Return _client
        End Get
    End Property

    ''' <summary>The directory of devices, shares and broadcasts (publish, withdraw; reads come with <see cref="Updated"/>).</summary>
    Public ReadOnly Property Records As ExchangeDriveDirectory
        Get
            Return _records
        End Get
    End Property

    ''' <summary>The packet queue (send, receive, requests and replies).</summary>
    Public ReadOnly Property Packets As ExchangeDriveQueue
        Get
            Return _packets
        End Get
    End Property

    Public ReadOnly Property State As ExchangeDriveServiceState
        Get
            SyncLock _lock
                Return _state
            End SyncLock
        End Get
    End Property

    ''' <summary>The kind of the last failed round; None after a good one.</summary>
    Public ReadOnly Property LastFailure As ExchangeDriveFailure
        Get
            SyncLock _lock
                Return _failure
            End SyncLock
        End Get
    End Property

    ''' <summary>What the last good round learned, or Nothing.</summary>
    Public ReadOnly Property Latest As ExchangeDriveUpdate
        Get
            SyncLock _lock
                Return _latest
            End SyncLock
        End Get
    End Property

    ' ------------------------------------------------------------------------------------------ starting and stopping

    ''' <summary>Starts the poller. On a PC that is not signed in it only reports SignedOut: no timer, no request. A second
    ''' call while it runs does nothing.</summary>
    Public Sub Start()
        SyncLock _lock
            If _loopTask IsNot Nothing AndAlso Not _loopTask.IsCompleted Then Return
        End SyncLock
        If Not _session.IsSignedIn() Then
            SetState(ExchangeDriveServiceState.SignedOut, ExchangeDriveFailure.SignedOut)
            Return
        End If
        Dim source As New CancellationTokenSource()
        SyncLock _lock
            _stopSource = source
            _loopTask = Task.Run(Function() RunAsync(source.Token))
        End SyncLock
    End Sub

    ''' <summary>Stops the poller and waits for the round in progress to end. The sign-in stays.</summary>
    Public Async Function StopAsync() As Task
        Dim source As CancellationTokenSource
        Dim running As Task
        SyncLock _lock
            source = _stopSource
            running = _loopTask
            _stopSource = Nothing
        End SyncLock
        If source IsNot Nothing Then source.Cancel()
        If running IsNot Nothing Then
            Try
                Await running.ConfigureAwait(False)
            Catch ex As OperationCanceledException
                ' the round in progress was cut short: that is the stop
            End Try
        End If
        If source IsNot Nothing Then source.Dispose()
        SetState(ExchangeDriveServiceState.Stopped, ExchangeDriveFailure.None)
    End Function

    ''' <summary>True while a screen waits for something (a reply, a send, the queue screen): the poller then runs at
    ''' 10 s instead of 60 s, and the wait in progress is cut short so the change takes effect at once.</summary>
    Public Sub SetActive(active As Boolean)
        SyncLock _lock
            If _active = active Then Return
            _active = active
        End SyncLock
        PollSoon()
    End Sub

    ''' <summary>Cuts the wait before the next round short ("refresh now", or right after the host published something).</summary>
    Public Sub PollSoon()
        Dim waiting As CancellationTokenSource
        SyncLock _lock
            waiting = _waitSource
        End SyncLock
        Try
            If waiting IsNot Nothing Then waiting.Cancel()
        Catch ex As ObjectDisposedException
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------ the rounds

    ''' <summary>One round: rewrite the own records that are a third of their lifetime old, list the folder once, read the
    ''' directory and the queue from that one listing. Throws <see cref="ExchangeDriveException"/>; state is not touched
    ''' (the loop does that). Exposed for the host's "refresh now" and for tests.</summary>
    Public Async Function PollOnceAsync(cancellation As CancellationToken) As Task(Of ExchangeDriveUpdate)
        Await _records.RefreshStandingAsync(cancellation).ConfigureAwait(False)
        Dim listing As ExchangeDriveListing = Await _client.ListAllAsync(cancellation).ConfigureAwait(False)
        Dim snapshot As ExchangeDriveDirectoryResult = Await _records.ReadFromAsync(listing, cancellation).ConfigureAwait(False)
        Dim poll As ExchangeDriveQueuePoll = Await _packets.PollAsync(listing, cancellation).ConfigureAwait(False)
        Return New ExchangeDriveUpdate(snapshot, poll)
    End Function

    Private Async Function RunAsync(cancellation As CancellationToken) As Task
        ' A crash can leave own records behind that nobody rewrites; they are already unreadable (item C) - this removes them.
        Dim sweepFailed As Boolean = False
        Try
            Await _records.DeleteOwnExpiredAsync(cancellation).ConfigureAwait(False)
        Catch ex As ExchangeDriveException
            sweepFailed = ex.Failure = ExchangeDriveFailure.SignedOut
        End Try
        If sweepFailed Then
            SetState(ExchangeDriveServiceState.SignedOut, ExchangeDriveFailure.SignedOut)
            Return
        End If

        Do While Not cancellation.IsCancellationRequested
            Dim wait As TimeSpan
            Dim update As ExchangeDriveUpdate = Nothing
            Dim failure As ExchangeDriveFailure = ExchangeDriveFailure.None
            Try
                update = Await PollOnceAsync(cancellation).ConfigureAwait(False)
            Catch ex As ExchangeDriveException
                failure = ex.Failure
            Catch ex As OperationCanceledException When cancellation.IsCancellationRequested
                Return
            End Try

            If update IsNot Nothing Then
                _backoff.Reset()
                SyncLock _lock
                    _latest = update
                End SyncLock
                SetState(ExchangeDriveServiceState.Running, ExchangeDriveFailure.None)
                Try
                    RaiseEvent Updated(Me, update)
                Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ObjectDisposedException
                    ' a host window that closed under the event: the loop is not the host's to stop
                End Try
                Dim active As Boolean
                SyncLock _lock
                    active = _active
                End SyncLock
                wait = TimeSpan.FromSeconds(ExchangeDrivePolling.IntervalSeconds(active))
            ElseIf failure = ExchangeDriveFailure.SignedOut Then
                ' the token is gone or no longer honoured (CT): the loop ends, the host offers the sign-in
                SetState(ExchangeDriveServiceState.SignedOut, ExchangeDriveFailure.SignedOut)
                Return
            ElseIf failure = ExchangeDriveFailure.DailyLimit OrElse failure = ExchangeDriveFailure.DriveFull Then
                ' a refusal that does not improve by asking again (item CS): the long idle interval, no back-off ladder
                SetState(ExchangeDriveServiceState.Unavailable, failure)
                wait = TimeSpan.FromSeconds(ExchangeDrivePolling.IdleSeconds)
            Else
                SetState(ExchangeDriveServiceState.Unavailable, failure)
                wait = _backoff.NextDelay(_random())
            End If

            Await WaitAsync(wait, cancellation).ConfigureAwait(False)
        Loop
    End Function

    Private Async Function WaitAsync(span As TimeSpan, cancellation As CancellationToken) As Task
        Using woken As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellation)
            SyncLock _lock
                _waitSource = woken
            End SyncLock
            Try
                Await _delay(span, woken.Token).ConfigureAwait(False)
            Catch ex As OperationCanceledException When Not cancellation.IsCancellationRequested
                ' woken early by PollSoon / SetActive: the next round starts now
            Finally
                SyncLock _lock
                    _waitSource = Nothing
                End SyncLock
            End Try
        End Using
    End Function

    Private Sub SetState(state As ExchangeDriveServiceState, failure As ExchangeDriveFailure)
        Dim changed As Boolean
        SyncLock _lock
            changed = _state <> state
            _state = state
            _failure = failure
        End SyncLock
        If changed Then RaiseEvent StateChanged(Me, EventArgs.Empty)
    End Sub

    ' ------------------------------------------------------------------------------------------ sign out

    ''' <summary>Criterion 4: stops the poller, deletes this device's own records while the token still works, revokes the
    ''' token at Google and removes the stored file. The order matters - the records need a live token. The file is removed
    ''' even when Drive or Google cannot be reached (the person asked to leave); the result says what was confirmed.
    ''' Packets in the queue are not touched here: a targeted one is taken or expires, and the person can delete the rest on the queue screen.</summary>
    Public Async Function SignOutAsync(cancellation As CancellationToken) As Task(Of ExchangeDriveSignOutResult)
        Await StopAsync().ConfigureAwait(False)

        Dim deleted As Integer = 0
        Dim recordsDeleted As Boolean = False
        If _session.IsSignedIn() Then
            Try
                deleted = Await _records.DeleteAllOwnAsync(cancellation).ConfigureAwait(False)
                recordsDeleted = True
            Catch ex As ExchangeDriveException
                recordsDeleted = False
            End Try
        Else
            recordsDeleted = True
        End If

        Dim revoked As Boolean = Await _session.SignOutAsync(cancellation).ConfigureAwait(False)
        SyncLock _lock
            _latest = Nothing
        End SyncLock
        SetState(ExchangeDriveServiceState.SignedOut, ExchangeDriveFailure.SignedOut)
        Return New ExchangeDriveSignOutResult(recordsDeleted, deleted, revoked)
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Dim source As CancellationTokenSource
        SyncLock _lock
            source = _stopSource
            _stopSource = Nothing
        End SyncLock
        Try
            If source IsNot Nothing Then source.Cancel()
        Catch ex As ObjectDisposedException
        End Try
    End Sub

End Class
#End If
