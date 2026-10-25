#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Threading
Imports System.Threading.Tasks

' Spec 234 T-3, DEVICE-EXCHANGE section 27 item DK (and the order of item N under item DL): keeps the directory record of ONE sealed
' broadcast current. The session (ExchangeSealedBroadcastSession) knows the keys and the box; this class knows WHEN the record goes
' out and in which order:
'   - the first record is made here and sent by the caller as `broadcast-start`;
'   - whenever the recipient set changes (debounced), or a replacement upload is asked for, `broadcast-update` carries the record with
'     the new box, and only when it is sent the session releases the replacement upload (record FIRST, upload after);
'   - before a `cast` (7.8) to a device the current box does not address, the record is updated first.
' The transport is the caller's: `sendUpdate` returns True when the envelope went out, `remember` keeps the record the agent repeats
' as `broadcast-start` after a reconnect. Nothing here logs; a box, a key and a record never reach a message or an exception text.

Friend NotInheritable Class ExchangeSealedBroadcastPublisher
    Implements IDisposable

    ''' <summary>How long a burst of directory changes is let to settle before the record is updated.</summary>
    Public Const DefaultDebounceMs As Integer = 400

    Private ReadOnly _session As ExchangeSealedBroadcastSession
    Private ReadOnly _buildRecord As Func(Of String, String)
    Private ReadOnly _remember As Action(Of String)
    Private ReadOnly _sendUpdate As Func(Of String, Task(Of Boolean))
    Private ReadOnly _debounceMs As Integer
    Private ReadOnly _gate As New SemaphoreSlim(1, 1)
    Private ReadOnly _cts As New CancellationTokenSource()
    Private _ticket As Integer
    Private _disposed As Boolean

    ''' <param name="buildRecord">The directory record (JSON) for a box (JSON, or Nothing while there is none).</param>
    ''' <param name="remember">Keeps the newest record: the one repeated as `broadcast-start` after a reconnect.</param>
    ''' <param name="sendUpdate">Sends `broadcast-update` with the record; True when it went out.</param>
    Public Sub New(session As ExchangeSealedBroadcastSession, buildRecord As Func(Of String, String), remember As Action(Of String),
                   sendUpdate As Func(Of String, Task(Of Boolean)), debounceMs As Integer)
        If session Is Nothing Then Throw New ArgumentNullException(NameOf(session))
        If buildRecord Is Nothing Then Throw New ArgumentNullException(NameOf(buildRecord))
        If sendUpdate Is Nothing Then Throw New ArgumentNullException(NameOf(sendUpdate))
        _session = session
        _buildRecord = buildRecord
        _remember = remember
        _sendUpdate = sendUpdate
        _debounceMs = Math.Max(0, debounceMs)
    End Sub

    Public ReadOnly Property Session As ExchangeSealedBroadcastSession
        Get
            Return _session
        End Get
    End Property

    ''' <summary>The record for `broadcast-start`: the recipients are read now and the box built when there are any. The caller sends
    ''' it and then calls <see cref="ExchangeSealedBroadcastSession.MarkPublished"/>.</summary>
    Public Function PrepareRecord() As String
        _session.Refresh()
        Dim record As String = _buildRecord.Invoke(_session.BoxJson)
        _remember?.Invoke(record)
        Return record
    End Function

    ''' <summary>The recipient set may have changed: updates the record once the burst has settled. The task ends when this
    ''' notification was served or superseded by a later one; it never throws.</summary>
    Public Async Function NotifyRecipientsChangedAsync() As Task
        If _disposed Then Return
        Dim ticket As Integer = Interlocked.Increment(_ticket)
        Try
            If _debounceMs > 0 Then Await Task.Delay(_debounceMs, _cts.Token).ConfigureAwait(False)
            If ticket <> Volatile.Read(_ticket) Then Return
            Await PublishAsync().ConfigureAwait(False)
        Catch
            ' a cancelled wait (the broadcast ended) or a connection that went away: the next change or the next welcome repeats it
        End Try
    End Function

    ''' <summary>Reads the recipients again and sends `broadcast-update` when the box is new or a replacement upload waits for the
    ''' record. True when a record went out. The replacement upload is released only after the send (item N).</summary>
    Public Async Function PublishAsync(Optional beforePublish As Func(Of Task) = Nothing, Optional force As Boolean = False) As Task(Of Boolean)
        Try
            Await _gate.WaitAsync(_cts.Token).ConfigureAwait(False)
        Catch ex As OperationCanceledException
            Return False
        Catch ex As ObjectDisposedException
            Return False
        End Try
        Try
            If _disposed Then Return False
            ' Guest boxes must be sent before a concurrent own-device update can release the new ring.
            If beforePublish IsNot Nothing Then Await beforePublish.Invoke().ConfigureAwait(False)
            Dim changed As Boolean = _session.Refresh()
            If Not changed AndAlso Not force Then Return False
            Dim record As String = _buildRecord.Invoke(_session.BoxJson)
            _remember?.Invoke(record)
            Dim sent As Boolean = Await _sendUpdate.Invoke(record).ConfigureAwait(False)
            If sent Then _session.MarkPublished()
            Return sent
        Finally
            _gate.Release()
        End Try
    End Function

    ''' <summary>Item DK / 7.8: a cast to a device the current box does not address updates the record first. The cast itself is the
    ''' caller's, and goes out after this either way (a device the box cannot address is told so by its own state).</summary>
    Public Async Function BeforeCastAsync(receiverDeviceId As String) As Task
        If _disposed OrElse _session.Addresses(receiverDeviceId) Then Return
        Try
            Await PublishAsync().ConfigureAwait(False)
        Catch
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        If _disposed Then Return
        _disposed = True
        Try
            _cts.Cancel()
        Catch
        End Try
        _cts.Dispose()
    End Sub

    ''' <summary>The type name; never a box, a key or a record.</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeSealedBroadcastPublisher"
    End Function

End Class
#End If
