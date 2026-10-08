#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 180 T-1. The 4-byte length framing of DEVICE-EXCHANGE 7.1 with BOTH limits of item T (16384 until the
' stream is authenticated, 262144 after `welcome` or `enrolled`, in both directions), and the relay chunk
' framing of 7.6 with the bound of item U. The reader takes exactly the bytes of one frame and nothing more:
' after `connected` or `upload-accepted` the stream is not envelopes any more and the next byte belongs to
' the tunnelled service or to the relay.

''' <summary>Reads frames under the limit in force for the stream's state.</summary>
Friend NotInheritable Class ExchangeFrameReader

    Private ReadOnly _stream As Stream
    Private _limit As Integer

    Public Sub New(stream As Stream)
        _stream = stream
        _limit = ExchangeWire.MaxFramePreAuth
    End Sub

    ''' <summary>The limit in force: <see cref="ExchangeWire.MaxFramePreAuth"/> until <see cref="MarkAuthenticated"/>.</summary>
    Public ReadOnly Property Limit As Integer
        Get
            Return _limit
        End Get
    End Property

    ''' <summary>The stream authenticated (`welcome` or `enrolled` was read): the larger limit applies from the next frame.</summary>
    Public Sub MarkAuthenticated()
        _limit = ExchangeWire.MaxFrameAuthenticated
    End Sub

    ''' <summary>The JSON body of the next frame. A zero length, or one above the limit in force, is
    ''' <see cref="ExchangeWireError.FrameSize"/> and the caller must close the stream; the oversize body is
    ''' never read, so nothing is buffered on the peer's say-so.</summary>
    Public Async Function ReadBodyAsync(ct As CancellationToken) As Task(Of Byte())
        Dim header(3) As Byte
        Await ReadExactAsync(header, 4, ct, True).ConfigureAwait(False)
        Dim n As Long = (CLng(header(0)) << 24) Or (CLng(header(1)) << 16) Or (CLng(header(2)) << 8) Or CLng(header(3))
        If n = 0 OrElse n > _limit Then Throw New ExchangeWireException(ExchangeWireError.FrameSize, "frame length out of range")
        Dim body(CInt(n) - 1) As Byte
        Await ReadExactAsync(body, CInt(n), ct, False).ConfigureAwait(False)
        Return body
    End Function

    ''' <summary>The next envelope (an unknown type comes back with IsKnown False).</summary>
    Public Async Function ReadAsync(ct As CancellationToken) As Task(Of ExchangeEnvelope)
        Dim body As Byte() = Await ReadBodyAsync(ct).ConfigureAwait(False)
        Return ExchangeEnvelopeCodec.Decode(body)
    End Function

    Private Async Function ReadExactAsync(buffer As Byte(), count As Integer, ct As CancellationToken, cleanEofAllowed As Boolean) As Task
        Dim got As Integer = 0
        While got < count
            Dim n As Integer = Await _stream.ReadAsync(buffer, got, count - got, ct).ConfigureAwait(False)
            If n = 0 Then
                ' EOF before the first byte of a frame is the peer closing (an EndOfStreamException the caller treats as
                ' a clean end); EOF inside a frame is a cut stream.
                If got = 0 AndAlso cleanEofAllowed Then Throw New EndOfStreamException("the stream was closed")
                Throw New ExchangeWireException(ExchangeWireError.Truncated, "the stream ended inside a frame")
            End If
            got += n
        End While
    End Function

End Class

''' <summary>Writes frames under the limit in force, one at a time (a control stream is written from the keepalive
''' timer and from calls at once). The buffer of a frame is overwritten once sent - an `enroll` holds a password.</summary>
Friend NotInheritable Class ExchangeFrameWriter

    Private ReadOnly _stream As Stream
    Private ReadOnly _gate As New SemaphoreSlim(1, 1)
    Private _limit As Integer

    Public Sub New(stream As Stream)
        _stream = stream
        _limit = ExchangeWire.MaxFramePreAuth
    End Sub

    Public ReadOnly Property Limit As Integer
        Get
            Return _limit
        End Get
    End Property

    Public Sub MarkAuthenticated()
        _limit = ExchangeWire.MaxFrameAuthenticated
    End Sub

    ''' <summary>The whole frame of an envelope: the 4-byte big-endian length and the body. Refuses a body above the
    ''' limit in force (the client bounds what it SENDS by the same limit as what it reads).</summary>
    Public Shared Function BuildFrame(env As ExchangeEnvelope, limit As Integer) As Byte()
        Dim writer As ExchangeJsonWriter = ExchangeEnvelopeCodec.EncodeToWriter(env)
        Try
            Dim n As Integer = writer.Length
            If n = 0 OrElse n > limit Then Throw New ExchangeWireException(ExchangeWireError.FrameSize, "frame above the limit in force")
            Dim frame(3 + n) As Byte
            frame(0) = CByte((n >> 24) And &HFF)
            frame(1) = CByte((n >> 16) And &HFF)
            frame(2) = CByte((n >> 8) And &HFF)
            frame(3) = CByte(n And &HFF)
            Dim body As Byte() = writer.ToArray()
            Buffer.BlockCopy(body, 0, frame, 4, n)
            Array.Clear(body, 0, body.Length)
            Return frame
        Finally
            writer.Clear()
        End Try
    End Function

    Public Async Function WriteAsync(env As ExchangeEnvelope, ct As CancellationToken) As Task
        Dim frame As Byte() = BuildFrame(env, _limit)
        Try
            Await _gate.WaitAsync(ct).ConfigureAwait(False)
            Try
                Await _stream.WriteAsync(frame, 0, frame.Length, ct).ConfigureAwait(False)
                Await _stream.FlushAsync(ct).ConfigureAwait(False)
            Finally
                _gate.Release()
            End Try
        Finally
            Array.Clear(frame, 0, frame.Length)
        End Try
    End Function

End Class

''' <summary>The relay chunk framing of 7.6: a 4-byte big-endian length that counts one flag byte plus the payload,
''' the flag byte, the payload. The length field is at most 1 MiB (item U); zero ends the broadcast.</summary>
Friend NotInheritable Class ExchangeChunks

    Private Sub New()
    End Sub

    ''' <summary>A listener may start at this chunk.</summary>
    Public Const FlagJoinable As Byte = &H1
    ''' <summary>A header chunk, kept and replayed to every new listener.</summary>
    Public Const FlagHeader As Byte = &H2

    ''' <summary>The bytes of one chunk. The payload is at most MaxChunkLengthField - 1 bytes; only the flags 1 and 2 may be set.</summary>
    Public Shared Function Build(flags As Byte, payload As Byte()) As Byte()
        If (flags And &HFC) <> 0 Then Throw New ExchangeWireException(ExchangeWireError.BadMessage, "reserved chunk flag bits")
        Dim n As Integer = 1 + If(payload Is Nothing, 0, payload.Length)
        If n > ExchangeWire.MaxChunkLengthField Then Throw New ExchangeWireException(ExchangeWireError.FrameSize, "chunk above 1 MiB on the length field")
        Dim r(3 + n) As Byte
        r(0) = CByte((n >> 24) And &HFF)
        r(1) = CByte((n >> 16) And &HFF)
        r(2) = CByte((n >> 8) And &HFF)
        r(3) = CByte(n And &HFF)
        r(4) = flags
        If payload IsNot Nothing AndAlso payload.Length > 0 Then Buffer.BlockCopy(payload, 0, r, 5, payload.Length)
        Return r
    End Function

    ''' <summary>The end of a broadcast: a length of zero.</summary>
    Public Shared Function EndMarker() As Byte()
        Return New Byte() {0, 0, 0, 0}
    End Function

    ''' <summary>Reads one chunk: Nothing for the clean end (a zero length). A length above 1 MiB, or a reserved flag
    ''' bit, is a <see cref="ExchangeWireError.FrameSize"/> / Malformed error.</summary>
    Public Shared Async Function ReadAsync(stream As Stream, ct As CancellationToken) As Task(Of ExchangeChunk)
        Dim header(3) As Byte
        Await ReadExactAsync(stream, header, 4, ct).ConfigureAwait(False)
        Dim n As Long = (CLng(header(0)) << 24) Or (CLng(header(1)) << 16) Or (CLng(header(2)) << 8) Or CLng(header(3))
        If n = 0 Then Return Nothing
        If n > ExchangeWire.MaxChunkLengthField Then Throw New ExchangeWireException(ExchangeWireError.FrameSize, "chunk above 1 MiB on the length field")
        Dim body(CInt(n) - 1) As Byte
        Await ReadExactAsync(stream, body, CInt(n), ct).ConfigureAwait(False)
        If (body(0) And &HFC) <> 0 Then Throw New ExchangeWireException(ExchangeWireError.Malformed, "reserved chunk flag bits")
        Dim payload(CInt(n) - 2) As Byte
        If payload.Length > 0 Then Buffer.BlockCopy(body, 1, payload, 0, payload.Length)
        Return New ExchangeChunk(body(0), payload)
    End Function

    Private Shared Async Function ReadExactAsync(stream As Stream, buffer As Byte(), count As Integer, ct As CancellationToken) As Task
        Dim got As Integer = 0
        While got < count
            Dim n As Integer = Await stream.ReadAsync(buffer, got, count - got, ct).ConfigureAwait(False)
            If n = 0 Then Throw New ExchangeWireException(ExchangeWireError.Truncated, "the stream ended inside a chunk")
            got += n
        End While
    End Function

End Class

Friend NotInheritable Class ExchangeChunk
    Public ReadOnly Flags As Byte
    Public ReadOnly Payload As Byte()

    Public Sub New(flags As Byte, payload As Byte())
        Me.Flags = flags
        Me.Payload = payload
    End Sub

    Public ReadOnly Property IsJoinable As Boolean
        Get
            Return (Flags And ExchangeChunks.FlagJoinable) <> 0
        End Get
    End Property

    Public ReadOnly Property IsHeader As Boolean
        Get
            Return (Flags And ExchangeChunks.FlagHeader) <> 0
        End Get
    End Property
End Class
#End If
