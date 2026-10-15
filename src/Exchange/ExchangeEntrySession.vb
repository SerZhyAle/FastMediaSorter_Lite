#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 187 T-3 and rules 5, 7, 8. The viewer's OWN session with one server for one entry - the road that replaces the Share
' Manager's agent for the consumer (Q187-1: one road, never "the agent's when it runs").
'
'   - It answers the three questions the "My devices" feed and the sync ask (state, directory, access - the shape of the viewer's
'     IExchangeAgentApi, which a thin adapter in src\Resources puts over this class), from the entry's own control stream, so the view and the
'     sync are unchanged above it. It lives in the shared library so the Share Manager's tests drive it against the same stub server.
'   - The control stream is open only while a LEASE is held - the directory view is open, or an attached resource is being opened
'     (rule 8) - and `bye` is said when the last lease ends. Nothing keeps a connection to a server for a feature nobody opened.
'   - The token comes from the entry's own DPAPI store; with none (a revoked device, a removed token) the state is `SignInAgain`
'     and nothing connects. A `device-revoked` answer discards the token and leaves the entry (the user signs in again with the
'     server and login already known).
'   - The directory is the account's full list with THIS entry's device removed (its own records are hidden from it, 9.1); another
'     program's records on the same machine are another device's, which is how this PC's shared folders appear next to the others.
'
' It never logs a server, a login, a fingerprint, a token or an `access` - only names of states and the log form of an id.

Friend NotInheritable Class ExchangeEntrySession
    Implements IDisposable

    ''' <summary>Longest an access request waits for the first directory of a session opened just for it.</summary>
    Friend Const AccessOpenWaitMs As Integer = 12000

    Private ReadOnly _entry As ExchangeEntry
    Private ReadOnly _ports As ExchangeConsumerPorts
    Private ReadOnly _configure As Action(Of ExchangeControlOptions)
    Private ReadOnly _gate As New Object()
    Private _session As ExchangeControlSession
    Private _cts As CancellationTokenSource
    Private _run As Task
    Private _leases As Integer
    Private _idleState As ExchangeAgentState = ExchangeAgentState.Stopped
    Private _idleReason As String = ""
    Private _disposed As Boolean
    Private _waiter As TaskCompletionSource(Of Boolean)

    ''' <param name="configure">A test's way to shorten the keepalive, the backoff and the clock of the control session.</param>
    Friend Sub New(entry As ExchangeEntry, ports As ExchangeConsumerPorts, Optional configure As Action(Of ExchangeControlOptions) = Nothing)
        If entry Is Nothing Then Throw New ArgumentNullException(NameOf(entry))
        _entry = entry
        _ports = If(ports, New ExchangeConsumerPorts())
        _configure = configure
    End Sub

    Friend ReadOnly Property Entry As ExchangeEntry
        Get
            Return _entry
        End Get
    End Property

    ''' <summary>The `publicEndpoint` of the last `welcome` ("" while there is no stream): what the server says the clients' address is.
    ''' Ask 2 of the entries proposal: the caller compares it with the entry's own address and offers the in-place edit - never a silent switch.</summary>
    Friend ReadOnly Property PublicEndpoint As String
        Get
            Dim s As ExchangeControlSession = Current()
            Return If(s Is Nothing, "", s.PublicEndpoint)
        End Get
    End Property

    ''' <summary>A hold on the session: it opens with the first lease and closes (with `bye`) when the last one is disposed.</summary>
    Friend Function Lease() As IDisposable
        SyncLock _gate
            If _disposed Then Return New LeaseToken(Nothing)
            _leases += 1
            If _session Is Nothing Then StartLocked()
        End SyncLock
        Return New LeaseToken(Me)
    End Function

    Private NotInheritable Class LeaseToken
        Implements IDisposable

        Private _owner As ExchangeEntrySession

        Friend Sub New(owner As ExchangeEntrySession)
            _owner = owner
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Dim o As ExchangeEntrySession = Interlocked.Exchange(_owner, Nothing)
            If o IsNot Nothing Then o.ReleaseLease()
        End Sub
    End Class

    Private Sub ReleaseLease()
        Dim stopIt As Boolean
        SyncLock _gate
            If _leases > 0 Then _leases -= 1
            stopIt = _leases = 0 AndAlso _session IsNot Nothing
        End SyncLock
        If stopIt Then
            Dim t As Task = StopAsync()
        End If
    End Sub

    ' ---- the session ----------------------------------------------------------------------------------------

    Private Sub StartLocked()
        Dim address As ExchangeServerAddress = Nothing
        Dim token As String = ""
        If Not _entry.TryGetAddress(address) OrElse Not _ports.Entries.TokenStoreFor(_entry.Id).TryLoad(_entry.DeviceId, token) Then
            _idleState = ExchangeAgentState.SignInAgain
            _idleReason = "no-token"
            Return
        End If
        Dim options As New ExchangeControlOptions With {
            .Server = address, .Pin = _entry.Pin, .Credentials = New ExchangeDeviceCredentials(_entry.DeviceId, token),
            .ProductVersion = _ports.ProductVersion, .Transport = _ports.Entries.TransportFor(_entry, _ports.Transport), .Log = _ports.Log}
        If _configure IsNot Nothing Then _configure.Invoke(options)
        Dim session As New ExchangeControlSession(options, Nothing)
        AddHandler session.StateChanged, AddressOf OnChanged
        AddHandler session.DirectoryChanged, AddressOf OnChanged
        AddHandler session.DeviceRevoked, AddressOf OnRevoked
        _idleState = ExchangeAgentState.Connecting
        _idleReason = ""
        _session = session
        _cts = New CancellationTokenSource()
        Dim cts As CancellationTokenSource = _cts
        _run = Task.Run(Function() session.RunAsync(cts.Token))
    End Sub

    Private Async Function StopAsync() As Task
        Dim session As ExchangeControlSession
        Dim cts As CancellationTokenSource
        Dim run As Task
        SyncLock _gate
            If _leases > 0 Then Return
            session = _session
            cts = _cts
            run = _run
            _session = Nothing
            _cts = Nothing
            _run = Nothing
            _idleState = ExchangeAgentState.Stopped
            _idleReason = ""
        End SyncLock
        If session Is Nothing Then Return
        Try
            Await session.SayByeAsync().ConfigureAwait(False)
        Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is ObjectDisposedException
        End Try
        Try
            cts?.Cancel()
            If run IsNot Nothing Then Await Task.WhenAny(run, Task.Delay(2000)).ConfigureAwait(False)
        Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is ObjectDisposedException
        End Try
        RemoveHandler session.StateChanged, AddressOf OnChanged
        RemoveHandler session.DirectoryChanged, AddressOf OnChanged
        RemoveHandler session.DeviceRevoked, AddressOf OnRevoked
        Try : cts?.Dispose() : Catch ex As ObjectDisposedException : End Try
        WakeWaiters()
    End Function

    Private Sub OnRevoked(sender As Object, e As EventArgs)
        ' the server no longer knows this device: its token is useless; the entry stays so the view can say "sign in again"
        Try
            _ports.Entries.TokenStoreFor(_entry.Id).Delete()
        Catch ex As ArgumentException
        End Try
        _ports.Log.Invoke("Exchange: a device of the viewer was revoked - the token was discarded")
    End Sub

    Private Sub OnChanged(sender As Object, e As EventArgs)
        WakeWaiters()
    End Sub

    Private Sub WakeWaiters()
        Dim w As TaskCompletionSource(Of Boolean)
        SyncLock _gate
            w = _waiter
            _waiter = Nothing
        End SyncLock
        If w IsNot Nothing Then w.TrySetResult(True)
    End Sub

    Private Function RegisterWaiter() As TaskCompletionSource(Of Boolean)
        SyncLock _gate
            If _waiter Is Nothing Then _waiter = New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
            Return _waiter
        End SyncLock
    End Function

    Private Function Current() As ExchangeControlSession
        SyncLock _gate
            Return _session
        End SyncLock
    End Function

    ''' <summary>The session's state, except that a session whose loop has not yet moved off its initial `Stopped` (the task was only
    ''' just started) is `Connecting`: a question asked at that instant must wait, not conclude that nothing will arrive.</summary>
    Private Function StateOf(session As ExchangeControlSession) As ExchangeAgentState
        Dim state As ExchangeAgentState = session.State
        If state = ExchangeAgentState.Stopped Then
            SyncLock _gate
                If Object.ReferenceEquals(session, _session) AndAlso _run IsNot Nothing AndAlso Not _run.IsCompleted Then Return ExchangeAgentState.Connecting
            End SyncLock
        End If
        Return state
    End Function

    ' ---- the three questions --------------------------------------------------------------------------------

    Public Function GetStateAsync(ct As CancellationToken) As Task(Of ExchangeAgentStateResult)
        Dim session As ExchangeControlSession = Current()
        Dim state As ExchangeAgentState
        Dim reason As String
        Dim features As IReadOnlyList(Of String) = Array.Empty(Of String)()
        If session Is Nothing Then
            SyncLock _gate
                state = _idleState
                reason = _idleReason
            End SyncLock
        Else
            state = StateOf(session)
            reason = session.LastReason
            features = session.Features
        End If
        Dim info As New ExchangeAgentStateInfo(True, state, features, _entry.DeviceId, _entry.DeviceName, reason)
        Return Task.FromResult(New ExchangeAgentStateResult(New ExchangeAgentReply(ExchangeAgentReplyKind.Ok, "", ""), info))
    End Function

    Public Async Function GetDirectoryAsync(sinceRevision As Long, waitMs As Integer, ct As CancellationToken) As Task(Of ExchangeAgentDirectoryResult)
        Dim notReady As New ExchangeAgentDirectoryResult(New ExchangeAgentReply(ExchangeAgentReplyKind.NotRunning, "", ""), Nothing)
        Dim deadline As Long = Environment.TickCount64 + Math.Max(0, waitMs)
        While True
            ct.ThrowIfCancellationRequested()
            Dim session As ExchangeControlSession = Current()
            If session Is Nothing Then Return notReady
            Dim waiter As TaskCompletionSource(Of Boolean) = RegisterWaiter()
            If StateOf(session) = ExchangeAgentState.Online AndAlso session.Model.HasSnapshot Then
                If session.Model.Revision <> sinceRevision OrElse sinceRevision = 0 Then Return Answer(session)
            ElseIf StateOf(session) <> ExchangeAgentState.Online AndAlso StateOf(session) <> ExchangeAgentState.Connecting AndAlso StateOf(session) <> ExchangeAgentState.Reconnecting Then
                Return notReady     ' stopped for a reason (sign in again, a pin mismatch, update): nothing will arrive
            End If
            Dim remaining As Long = deadline - Environment.TickCount64
            If remaining <= 0 Then
                If StateOf(session) = ExchangeAgentState.Online AndAlso session.Model.HasSnapshot Then Return Answer(session)
                Return notReady
            End If
            Await Task.WhenAny(waiter.Task, Task.Delay(CInt(Math.Min(remaining, Integer.MaxValue)), ct)).ConfigureAwait(False)
        End While
        Return notReady
    End Function

    Private Function Answer(session As ExchangeControlSession) As ExchangeAgentDirectoryResult
        Dim snapshot As ExchangeDirectorySnapshot = session.Model.Snapshot().WithoutDevice(_entry.DeviceId)
        Return New ExchangeAgentDirectoryResult(New ExchangeAgentReply(ExchangeAgentReplyKind.Ok, "", ""), snapshot)
    End Function

    Public Async Function GetAccessAsync(resourceId As String, ct As CancellationToken) As Task(Of ExchangeAgentAccessResult)
        Dim notRunning As New ExchangeAgentAccessResult(New ExchangeAgentReply(ExchangeAgentReplyKind.NotRunning, "", ""), "")
        ' a session opened just for this: a lease for the length of the question (rule 8), the first directory awaited
        Using lease As IDisposable = Me.Lease()
            Dim started As Long = Environment.TickCount64
            While Environment.TickCount64 - started < AccessOpenWaitMs
                ct.ThrowIfCancellationRequested()
                Dim session As ExchangeControlSession = Current()
                If session Is Nothing Then Return notRunning
                Dim waiter As TaskCompletionSource(Of Boolean) = RegisterWaiter()
                If StateOf(session) = ExchangeAgentState.Online AndAlso session.Model.HasSnapshot Then
                    Dim json As String = session.Model.AccessOf(resourceId)
                    If json.Length > 0 Then Return New ExchangeAgentAccessResult(New ExchangeAgentReply(ExchangeAgentReplyKind.Ok, "", ""), json)
                    Dim listed As Boolean = session.Model.Snapshot().Resources.Any(Function(r) String.Equals(r.ResourceId, resourceId, StringComparison.Ordinal))
                    Return New ExchangeAgentAccessResult(New ExchangeAgentReply(ExchangeAgentReplyKind.Refused,
                        If(listed, ExchangeAgentPipe.ErrorNoAccess, ExchangeAgentPipe.ErrorUnknownResource), ""), "")
                ElseIf StateOf(session) <> ExchangeAgentState.Online AndAlso StateOf(session) <> ExchangeAgentState.Connecting AndAlso StateOf(session) <> ExchangeAgentState.Reconnecting Then
                    Return notRunning
                End If
                Await Task.WhenAny(waiter.Task, Task.Delay(500, ct)).ConfigureAwait(False)
            End While
        End Using
        Return notRunning
    End Function

    ''' <summary>226 rule 4: change the account's password over this entry's own control stream. On success the fresh key of the answer
    ''' replaces the stored one (224 rule 9: THIS device stays signed in), and the account's other devices are signed out - the
    ''' surface's sentence says so. The caller rebuilds the session afterwards, so a later reconnect speaks with the fresh key.
    ''' Nothing is deleted on a refusal, and the agent never retries it on its own (218: one outcome per failure class).</summary>
    Friend Async Function ChangePasswordAsync(oldPassword As String, newPassword As String, ct As CancellationToken) As Task(Of ExchangePasswordChangeResult)
        Dim session As ExchangeControlSession = Current()
        If session Is Nothing Then Return New ExchangePasswordChangeResult(ExchangePasswordChangeOutcome.NoStream, ExchangeRefusal.Unknown, "")
        Dim result As ExchangePasswordChangeResult = Await session.ChangePasswordAsync(oldPassword, newPassword, ct).ConfigureAwait(False)
        If result.Outcome = ExchangePasswordChangeOutcome.Changed Then
            If Not _ports.Entries.TokenStoreFor(_entry.Id).Save(_entry.DeviceId, result.FreshToken) Then
                _ports.Log.Invoke("Exchange: the account's key changed, but the new one could not be stored on this computer")
                Return New ExchangePasswordChangeResult(ExchangePasswordChangeOutcome.NoStream, ExchangeRefusal.Unknown, "")
            End If
            _ports.Log.Invoke("Exchange: this account's key was changed - the other devices sign in again")
        End If
        Return result
    End Function
    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock _gate
            _disposed = True
            _leases = 0
        End SyncLock
        Dim t As Task = StopAsync()
    End Sub

End Class

''' <summary>The viewer's sessions, one per entry, kept while the program runs and closed with it. Nothing is created for an entry
''' until something asks (a view opens, a resource opens): with no entry the hub is empty and nothing connects (204 dual mode).</summary>
Friend NotInheritable Class ExchangeEntryHub
    Implements IDisposable

    Private ReadOnly _ports As ExchangeConsumerPorts
    Private ReadOnly _gate As New Object()
    Private ReadOnly _sessions As New Dictionary(Of String, ExchangeEntrySession)(StringComparer.Ordinal)

    Friend Sub New(ports As ExchangeConsumerPorts)
        _ports = If(ports, New ExchangeConsumerPorts())
    End Sub

    Friend ReadOnly Property Ports As ExchangeConsumerPorts
        Get
            Return _ports
        End Get
    End Property

    ''' <summary>The session of this entry. A changed entry (another pin, address or name) replaces the old session, which closes.</summary>
    Friend Function SessionFor(entry As ExchangeEntry) As ExchangeEntrySession
        If entry Is Nothing Then Return Nothing
        Dim old As ExchangeEntrySession = Nothing
        SyncLock _gate
            Dim current As ExchangeEntrySession = Nothing
            If _sessions.TryGetValue(entry.Id, current) Then
                Dim e As ExchangeEntry = current.Entry
                If String.Equals(e.Server, entry.Server, StringComparison.Ordinal) AndAlso String.Equals(e.Pin, entry.Pin, StringComparison.Ordinal) AndAlso
                   String.Equals(e.DeviceId, entry.DeviceId, StringComparison.Ordinal) Then Return current
                old = current
            End If
            Dim fresh As New ExchangeEntrySession(entry, _ports)
            _sessions(entry.Id) = fresh
            If old IsNot Nothing Then old.Dispose()
            Return fresh
        End SyncLock
    End Function

    ''' <summary>The entry's session is closed and forgotten (the entry was removed or its token replaced).</summary>
    Friend Sub Forget(entryId As String)
        Dim old As ExchangeEntrySession = Nothing
        SyncLock _gate
            If _sessions.TryGetValue(entryId, old) Then _sessions.Remove(entryId)
        End SyncLock
        If old IsNot Nothing Then old.Dispose()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dim all As List(Of ExchangeEntrySession)
        SyncLock _gate
            all = _sessions.Values.ToList()
            _sessions.Clear()
        End SyncLock
        For Each s As ExchangeEntrySession In all
            s.Dispose()
        Next
    End Sub

End Class
#End If
