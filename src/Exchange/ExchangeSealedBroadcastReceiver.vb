#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks

' Spec 234 E2a, DEVICE-EXCHANGE section 27 items DK and DL, the RECEIVER side of the sealed relay. The body of the listen answer of a
' sealed broadcast (content type `application/x-fmsx-sealed`) is a sequence of records (ExchangeRelayRecords); this file turns that
' body into clear media, one authenticated record at a time, without ever holding more than one record of it.
'
'   - ExchangeSealedBroadcastReceiver is the listener's STATE across connections: the broadcast id, the group key (`relayKey`, or the
'     key of an outward link) and the epoch key the last key ring gave. It outlives one HTTP response, because the reconnect that
'     item DL asks for continues the same state.
'   - ExchangeSealedMediaStream is ONE response body read through: Read gives clear media, and when it returns 0 the Status says why.
'
' Rules this file keeps (item DL, item DP):
'   - A key ring opens an epoch (also a new one in the middle of the body). The media type the ring names is the true one.
'   - A media record of an epoch the listener does not hold (a body joined without its header chunk, or a missed ring) ends the
'     body with NeedReconnect ONCE: the caller reconnects and reads the current header chunk. Media that decrypts after the
'     reconnect proves it worked and gives the budget back. A second unknown epoch before that is SealedFailed.
'   - A record whose tag fails, a ring with no entry for the listener's key and framing that is no record are SealedFailed: the
'     stream returns 0 from then on, the source is not read again, and the receiver opens no further body. Nothing partial is ever
'     played, because a record's media is handed out only after its own tag verified.
'   - A body that ends inside a record is Truncated (a cut connection): the bytes of the partial record are dropped unread.
'   - A record of an unknown type is skipped.
'   - Nothing here logs. A key, a nonce and a media byte never reach a message, an exception or a ToString.

''' <summary>Why a sealed body stopped giving media.</summary>
Friend Enum ExchangeSealedReceiveStatus
    ''' <summary>More may come.</summary>
    Streaming = 0
    ''' <summary>The body ended at a record boundary.</summary>
    Ended = 1
    ''' <summary>The body ended inside a record: nothing of that record was played.</summary>
    Truncated = 2
    ''' <summary>A media record of an epoch this listener does not hold: reconnect once and read the current header chunk.</summary>
    NeedReconnect = 3
    ''' <summary>`sealed-failed` (item DR): stop. Nothing more is played from this body, and the receiver opens no other.</summary>
    SealedFailed = 4
End Enum

''' <summary>The reason behind SealedFailed. A reason, never a value.</summary>
Friend Enum ExchangeSealedReceiveFailure
    None = 0
    ''' <summary>A record too short for its type, or a ring that does not parse.</summary>
    MalformedRecord = 1
    ''' <summary>The ring has no entry for this listener's key: it was dropped from the broadcast.</summary>
    NoRingEntry = 2
    RingTagFailed = 3
    MediaTagFailed = 4
    ''' <summary>A `len` of 0 or above the chunk bound: the bytes are no longer records.</summary>
    CorruptFraming = 5
    ''' <summary>A second unknown epoch after the one reconnect it was given.</summary>
    UnknownEpoch = 6
End Enum

''' <summary>The listener of one sealed broadcast: the state that outlives a connection.</summary>
Friend NotInheritable Class ExchangeSealedBroadcastReceiver
    Implements IDisposable

    ''' <summary>The only `seal.v` this build plays (LIVE-BROADCAST item Q, DEVICE-EXCHANGE item DK).</summary>
    Public Const SupportedSealVersion As Integer = 1
    ''' <summary>How many reconnects an unknown epoch earns before it is `sealed-failed` (item DL: "reconnect once").</summary>
    Public Const ReconnectBudget As Integer = 1

    Private ReadOnly _gate As New Object()
    Private ReadOnly _broadcastId As String
    Private ReadOnly _listener As ExchangeRelayListener
    Private _reconnectsLeft As Integer = ReconnectBudget
    Private _reconnects As Integer
    Private _failed As Boolean
    Private _failure As ExchangeSealedReceiveFailure
    Private _active As ExchangeSealedMediaStream
    Private _disposed As Boolean

    ''' <param name="relayKey">The 32-byte group key: the box's `relayKey` for an own device, `seal.key` of an outward link. This
    ''' object keeps its own copy and wipes it on Dispose.</param>
    Public Sub New(broadcastId As String, relayKey As Byte())
        If String.IsNullOrEmpty(broadcastId) Then Throw New ArgumentException("broadcastId", NameOf(broadcastId))
        If relayKey Is Nothing OrElse relayKey.Length <> ExchangeRelayRecords.KeyLength Then Throw New ArgumentException("relayKey", NameOf(relayKey))
        _broadcastId = broadcastId
        _listener = New ExchangeRelayListener(broadcastId, relayKey)
    End Sub

    ''' <summary>The same from the key as text (base64url, 32 bytes), the form of `seal.key` and of the pipe answer. False for a
    ''' key that is no key; nothing is made then.</summary>
    Public Shared Function TryCreate(broadcastId As String, relayKeyText As String, ByRef receiver As ExchangeSealedBroadcastReceiver) As Boolean
        receiver = Nothing
        If String.IsNullOrEmpty(broadcastId) Then Return False
        Dim raw As Byte() = Nothing
        If Not ExchangeBase64Url.TryDecode(relayKeyText, raw) Then Return False
        Try
            If raw.Length <> ExchangeRelayRecords.KeyLength Then Return False
            receiver = New ExchangeSealedBroadcastReceiver(broadcastId, raw)
            Return True
        Finally
            Array.Clear(raw, 0, raw.Length)
        End Try
    End Function

    ''' <summary>True for a `seal.v` this build plays. An endpoint with another value is skipped, never played as clear bytes
    ''' (item DK, LIVE-BROADCAST item Q); 0 is "absent or unreadable".</summary>
    Public Shared Function IsSupportedSeal(version As Integer) As Boolean
        Return version = SupportedSealVersion
    End Function

    ''' <summary>True when a `Content-Type` header value names the sealed relay body (item DL), whatever its parameters or case.</summary>
    Public Shared Function IsSealedContentType(contentType As String) As Boolean
        If String.IsNullOrWhiteSpace(contentType) Then Return False
        Dim semicolon As Integer = contentType.IndexOf(";"c)
        Dim bare As String = If(semicolon >= 0, contentType.Substring(0, semicolon), contentType).Trim()
        Return String.Equals(bare, ExchangeRelayRecords.ContentType, StringComparison.OrdinalIgnoreCase)
    End Function

    Public ReadOnly Property BroadcastId As String
        Get
            Return _broadcastId
        End Get
    End Property

    ''' <summary>True once a key ring opened.</summary>
    Public ReadOnly Property HoldsEpoch As Boolean
        Get
            SyncLock _gate
                Return _listener.HoldsEpoch
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property Epoch As UInteger
        Get
            SyncLock _gate
                Return _listener.Epoch
            End SyncLock
        End Get
    End Property

    ''' <summary>The TRUE media type the last key ring named (`video/mp2t`, `audio/aac`); "" before a ring.</summary>
    Public ReadOnly Property MediaType As String
        Get
            SyncLock _gate
                Return _listener.MediaType
            End SyncLock
        End Get
    End Property

    ''' <summary>How many reconnects the receiver asked for so far.</summary>
    Public ReadOnly Property ReconnectsUsed As Integer
        Get
            SyncLock _gate
                Return _reconnects
            End SyncLock
        End Get
    End Property

    ''' <summary>True once a body ended in SealedFailed: this receiver opens no further body.</summary>
    Public ReadOnly Property IsFailed As Boolean
        Get
            SyncLock _gate
                Return _failed
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property Failure As ExchangeSealedReceiveFailure
        Get
            SyncLock _gate
                Return _failure
            End SyncLock
        End Get
    End Property

    ''' <summary>Reads one response body through. A body still open from before is superseded (it returns 0 from now on): a
    ''' reconnect replaces the connection it came from. On a failed receiver the stream is born stopped and reads nothing.</summary>
    ''' <param name="source">The response body; disposed with the stream unless <paramref name="leaveOpen"/>.</param>
    Public Function Open(source As Stream, Optional leaveOpen As Boolean = False) As ExchangeSealedMediaStream
        If source Is Nothing Then Throw New ArgumentNullException(NameOf(source))
        Dim previous As ExchangeSealedMediaStream
        Dim stream As ExchangeSealedMediaStream
        SyncLock _gate
            If _disposed Then Throw New ObjectDisposedException(NameOf(ExchangeSealedBroadcastReceiver))
            previous = _active
            stream = New ExchangeSealedMediaStream(Me, source, leaveOpen, _failed, _failure)
            _active = stream
        End SyncLock
        If previous IsNot Nothing Then previous.Supersede()
        Return stream
    End Function

    ''' <summary>One record of a body. Streaming with media (or without: a ring, a skipped type); NeedReconnect or SealedFailed
    ''' with Nothing. The only caller is the stream.</summary>
    Friend Function Accept(record As ExchangeRelayRecord, ByRef media As Byte()) As ExchangeSealedReceiveStatus
        media = Nothing
        SyncLock _gate
            If _failed OrElse _disposed Then Return ExchangeSealedReceiveStatus.SealedFailed
            Select Case _listener.Process(record, -1, media)
                Case ExchangeRelayOutcome.Media
                    _reconnectsLeft = ReconnectBudget       ' a reconnect that gave media worked: it is not spent against the next epoch
                    Return ExchangeSealedReceiveStatus.Streaming
                Case ExchangeRelayOutcome.KeyRing, ExchangeRelayOutcome.Skipped
                    Return ExchangeSealedReceiveStatus.Streaming
                Case ExchangeRelayOutcome.NeedReconnect
                    If _reconnectsLeft > 0 Then
                        _reconnectsLeft -= 1
                        _reconnects += 1
                        Return ExchangeSealedReceiveStatus.NeedReconnect
                    End If
                    FailLocked(ExchangeSealedReceiveFailure.UnknownEpoch)
                    Return ExchangeSealedReceiveStatus.SealedFailed
                Case Else
                    FailLocked(FailureOf(_listener.LastFailure))
                    Return ExchangeSealedReceiveStatus.SealedFailed
            End Select
        End SyncLock
    End Function

    ''' <summary>The bytes of a body are no longer records.</summary>
    Friend Sub FailFraming()
        SyncLock _gate
            If Not _failed Then FailLocked(ExchangeSealedReceiveFailure.CorruptFraming)
        End SyncLock
    End Sub

    Private Sub FailLocked(reason As ExchangeSealedReceiveFailure)
        _failed = True
        _failure = reason
        _listener.Dispose()         ' wipes the group key and the epoch key: nothing more can be opened
    End Sub

    Private Shared Function FailureOf(reason As ExchangeRelayFailure) As ExchangeSealedReceiveFailure
        Select Case reason
            Case ExchangeRelayFailure.NoRingEntry
                Return ExchangeSealedReceiveFailure.NoRingEntry
            Case ExchangeRelayFailure.RingTagFailed
                Return ExchangeSealedReceiveFailure.RingTagFailed
            Case ExchangeRelayFailure.MediaTagFailed
                Return ExchangeSealedReceiveFailure.MediaTagFailed
            Case Else
                Return ExchangeSealedReceiveFailure.MalformedRecord
        End Select
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Dim active As ExchangeSealedMediaStream
        SyncLock _gate
            If _disposed Then Return
            _disposed = True
            active = _active
            _active = Nothing
            _listener.Dispose()
        End SyncLock
        If active IsNot Nothing Then active.Supersede()
    End Sub

    ''' <summary>The type name; never a key.</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeSealedBroadcastReceiver"
    End Function

End Class

''' <summary>One sealed response body, read through as clear media. Read returns 0 when the body is over or stopped; look at
''' <see cref="Status"/> to know which. Memory is bounded by one record (the chunk bound of item U, 1 MiB) plus the clear media of
''' that record: the body is pulled only as fast as the caller reads.</summary>
Friend NotInheritable Class ExchangeSealedMediaStream
    Inherits Stream

    Private ReadOnly _owner As ExchangeSealedBroadcastReceiver
    Private ReadOnly _source As Stream
    Private ReadOnly _leaveOpen As Boolean
    Private ReadOnly _reader As New ExchangeRelayRecordReader()
    Private _pending As Byte()
    Private _pendingOffset As Integer
    Private _status As ExchangeSealedReceiveStatus = ExchangeSealedReceiveStatus.Streaming
    Private _failure As ExchangeSealedReceiveFailure
    Private _disposed As Boolean

    Friend Sub New(owner As ExchangeSealedBroadcastReceiver, source As Stream, leaveOpen As Boolean, alreadyFailed As Boolean, failure As ExchangeSealedReceiveFailure)
        _owner = owner
        _source = source
        _leaveOpen = leaveOpen
        If alreadyFailed Then
            _status = ExchangeSealedReceiveStatus.SealedFailed
            _failure = failure
        End If
    End Sub

    ''' <summary>Streaming while more may come; otherwise why the stream stopped. Set before the Read that returns 0.</summary>
    Public ReadOnly Property Status As ExchangeSealedReceiveStatus
        Get
            Return _status
        End Get
    End Property

    ''' <summary>The reason when <see cref="Status"/> is SealedFailed.</summary>
    Public ReadOnly Property Failure As ExchangeSealedReceiveFailure
        Get
            Return _failure
        End Get
    End Property

    ''' <summary>A newer body took over (a reconnect): this one gives nothing more.</summary>
    Friend Sub Supersede()
        If _status = ExchangeSealedReceiveStatus.Streaming Then _status = ExchangeSealedReceiveStatus.Ended
        DropPending()
    End Sub

    Public Overrides ReadOnly Property CanRead As Boolean
        Get
            Return Not _disposed
        End Get
    End Property

    Public Overrides ReadOnly Property CanSeek As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overrides ReadOnly Property CanWrite As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overrides ReadOnly Property Length As Long
        Get
            Throw New NotSupportedException()
        End Get
    End Property

    Public Overrides Property Position As Long
        Get
            Throw New NotSupportedException()
        End Get
        Set(value As Long)
            Throw New NotSupportedException()
        End Set
    End Property

    Public Overrides Sub Flush()
    End Sub

    Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
        Throw New NotSupportedException()
    End Function

    Public Overrides Sub SetLength(value As Long)
        Throw New NotSupportedException()
    End Sub

    Public Overrides Sub Write(buffer As Byte(), offset As Integer, count As Integer)
        Throw New NotSupportedException()
    End Sub

    Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
        Check(buffer, offset, count)
        If count = 0 Then Return 0
        Do
            Dim n As Integer = TakePending(buffer, offset, count)
            If n > 0 Then Return n
            If _status <> ExchangeSealedReceiveStatus.Streaming OrElse _disposed Then Return 0
            Advance(_reader.ReadNext(_source))
        Loop
    End Function

    Public Overrides Async Function ReadAsync(buffer As Byte(), offset As Integer, count As Integer, cancellationToken As CancellationToken) As Task(Of Integer)
        Check(buffer, offset, count)
        If count = 0 Then Return 0
        Do
            Dim n As Integer = TakePending(buffer, offset, count)
            If n > 0 Then Return n
            If _status <> ExchangeSealedReceiveStatus.Streaming OrElse _disposed Then Return 0
            Advance(Await _reader.ReadNextAsync(_source, cancellationToken).ConfigureAwait(False))
        Loop
    End Function

    Private Shared Sub Check(buffer As Byte(), offset As Integer, count As Integer)
        If buffer Is Nothing Then Throw New ArgumentNullException(NameOf(buffer))
        If offset < 0 OrElse count < 0 OrElse offset + count > buffer.Length Then Throw New ArgumentOutOfRangeException(NameOf(count))
    End Sub

    Private Function TakePending(buffer As Byte(), offset As Integer, count As Integer) As Integer
        If _pending Is Nothing Then Return 0
        Dim n As Integer = Math.Min(count, _pending.Length - _pendingOffset)
        System.Buffer.BlockCopy(_pending, _pendingOffset, buffer, offset, n)
        _pendingOffset += n
        If _pendingOffset >= _pending.Length Then DropPending()
        Return n
    End Function

    Private Sub DropPending()
        If _pending IsNot Nothing Then Array.Clear(_pending, 0, _pending.Length)
        _pending = Nothing
        _pendingOffset = 0
    End Sub

    Private Sub Advance(result As ExchangeRelayReadResult)
        Select Case result.Status
            Case ExchangeRelayReadStatus.Record
                Dim media As Byte() = Nothing
                Dim outcome As ExchangeSealedReceiveStatus = _owner.Accept(result.Record, media)
                If outcome = ExchangeSealedReceiveStatus.Streaming Then
                    If media IsNot Nothing AndAlso media.Length > 0 Then
                        _pending = media
                        _pendingOffset = 0
                    End If
                Else
                    _status = outcome
                    If outcome = ExchangeSealedReceiveStatus.SealedFailed Then _failure = _owner.Failure
                End If
            Case ExchangeRelayReadStatus.EndOfStream
                _status = ExchangeSealedReceiveStatus.Ended
            Case ExchangeRelayReadStatus.Truncated
                _status = ExchangeSealedReceiveStatus.Truncated
            Case Else
                _owner.FailFraming()
                _status = ExchangeSealedReceiveStatus.SealedFailed
                _failure = ExchangeSealedReceiveFailure.CorruptFraming
        End Select
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso Not _disposed Then
            _disposed = True
            DropPending()
            If Not _leaveOpen Then _source.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

    ''' <summary>The type name; never a byte of media.</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeSealedMediaStream " & _status.ToString()
    End Function

End Class
#End If
