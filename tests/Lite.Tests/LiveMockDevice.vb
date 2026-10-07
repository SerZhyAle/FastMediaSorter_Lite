#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.IO
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

' A mock broadcasting device for the live-broadcast tests (spec 023 section 10.2). A raw TcpListener, so
' the bytes on the wire are EXACTLY what the LIVE-BROADCAST contract section 2 shows - the phone's
' HTTP/1.1 chunked body, the watch's HTTP/1.0 raw body until the socket closes, the watch's 503 at the
' fifth listener - and a HttpListener's own idea of a response never stands between a test and the claim.
' It counts what the listener under test does to it: connections accepted, how many are open at once, and
' the request head of each.

''' <summary>One accepted connection, handed to the device's script.</summary>
Friend NotInheritable Class LiveMockConnection

    Public ReadOnly Property Client As TcpClient
    Public ReadOnly Property Stream As NetworkStream
    Public ReadOnly Property RequestHead As String
    Public ReadOnly Property Sequence As Integer

    Public Sub New(client As TcpClient, requestHead As String, sequence As Integer)
        Me.Client = client
        Me.Stream = client.GetStream()
        Me.RequestHead = requestHead
        Me.Sequence = sequence
    End Sub

    Public Sub WriteAscii(text As String)
        Dim bytes As Byte() = Encoding.ASCII.GetBytes(text)
        Stream.Write(bytes, 0, bytes.Length)
    End Sub

    Public Sub Write(bytes As Byte())
        Stream.Write(bytes, 0, bytes.Length)
    End Sub

    ''' <summary>Closes with a RST instead of a FIN, the way a phone that left the Wi-Fi looks.</summary>
    Public Sub Reset()
        Client.LingerState = New LingerOption(True, 0)
        Client.Close()
    End Sub

End Class

Friend NotInheritable Class LiveMockDevice
    Implements IDisposable

    Private ReadOnly listener As TcpListener
    Private ReadOnly script As Func(Of LiveMockConnection, CancellationToken, Task)
    Private ReadOnly cts As New CancellationTokenSource()
    Private ReadOnly acceptLoop As Task
    Private accepted As Integer
    Private open As Integer
    Private maxOpen As Integer

    ''' <summary>The request head (request line and headers) of every connection, in order of arrival.</summary>
    Public ReadOnly Requests As New ConcurrentQueue(Of String)()

    ''' <summary>Anything a test wants ordered against the device's own events ("accept#2", ...).</summary>
    Public ReadOnly Events As ConcurrentQueue(Of String)

    Public Sub New(script As Func(Of LiveMockConnection, CancellationToken, Task), Optional events As ConcurrentQueue(Of String) = Nothing,
                   Optional bindAny As Boolean = False)
        Me.script = script
        Me.Events = If(events, New ConcurrentQueue(Of String)())
        ' Loopback for every unit test; all interfaces only for the hand-run that lets the real viewer connect.
        listener = New TcpListener(If(bindAny, IPAddress.Any, IPAddress.Loopback), 0)
        listener.Start()
        acceptLoop = Task.Run(AddressOf AcceptLoopAsync)
    End Sub

    Public ReadOnly Property Port As Integer
        Get
            Return CType(listener.LocalEndpoint, IPEndPoint).Port
        End Get
    End Property

    Public Function Url(Optional path As String = "/live-audio.aac") As String
        Return "http://127.0.0.1:" & Port.ToString(Globalization.CultureInfo.InvariantCulture) & path
    End Function

    ''' <summary>Connections accepted so far.</summary>
    Public ReadOnly Property AcceptedCount As Integer
        Get
            Return Volatile.Read(accepted)
        End Get
    End Property

    ''' <summary>The most connections that were open at the same moment.</summary>
    Public ReadOnly Property MaxConcurrent As Integer
        Get
            Return Volatile.Read(maxOpen)
        End Get
    End Property

    Private Async Function AcceptLoopAsync() As Task
        While Not cts.IsCancellationRequested
            Dim client As TcpClient
            Try
                client = Await listener.AcceptTcpClientAsync(cts.Token).ConfigureAwait(False)
            Catch
                Return
            End Try
            Dim handled As Task = Task.Run(Function() HandleAsync(client))
        End While
    End Function

    Private Async Function HandleAsync(client As TcpClient) As Task
        Dim sequence As Integer = Interlocked.Increment(accepted)
        Dim nowOpen As Integer = Interlocked.Increment(open)
        Dim seen As Integer
        Do
            seen = Volatile.Read(maxOpen)
            If nowOpen <= seen Then Exit Do
        Loop While Interlocked.CompareExchange(maxOpen, nowOpen, seen) <> seen

        Try
            Events.Enqueue("accept#" & sequence.ToString(Globalization.CultureInfo.InvariantCulture))
            Dim head As String = Await ReadHeadAsync(client.GetStream()).ConfigureAwait(False)
            Requests.Enqueue(head)
            Dim connection As New LiveMockConnection(client, head, sequence)
            Await script(connection, cts.Token).ConfigureAwait(False)
        Catch
            ' The listener under test hangs up whenever it likes; that is not a failure of the device.
        Finally
            Try
                client.Close()
            Catch
            End Try
            Interlocked.Decrement(open)
            Events.Enqueue("closed#" & sequence.ToString(Globalization.CultureInfo.InvariantCulture))
        End Try
    End Function

    Private Shared Async Function ReadHeadAsync(stream As NetworkStream) As Task(Of String)
        Dim builder As New StringBuilder()
        Dim one(0) As Byte
        While builder.Length < 8192
            Dim n As Integer = Await stream.ReadAsync(one, 0, 1).ConfigureAwait(False)
            If n <= 0 Then Exit While
            builder.Append(ChrW(one(0)))
            If builder.Length >= 4 AndAlso builder.ToString(builder.Length - 4, 4) = vbCrLf & vbCrLf Then Exit While
        End While
        Return builder.ToString()
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        cts.Cancel()
        Try
            listener.Stop()
        Catch
        End Try
        Try
            acceptLoop.Wait(1000)
        Catch
        End Try
        cts.Dispose()
    End Sub

#Region "Wire shapes (LIVE-BROADCAST section 2)"

    ''' <summary>The ADTS AAC-LC mono 44.1 kHz frames the producers send: a valid 7-byte header and a zero payload.</summary>
    Public Shared Function AdtsFrame(Optional frameLength As Integer = 371) As Byte()
        Dim frame(frameLength - 1) As Byte
        frame(0) = &HFF
        frame(1) = &HF1                                   ' MPEG-4, layer 0, no CRC
        frame(2) = &H50                                   ' AAC-LC, 44.1 kHz, channel config high bit 0
        frame(3) = CByte(&H40 Or ((frameLength >> 11) And &H3))   ' mono, frame length high bits
        frame(4) = CByte((frameLength >> 3) And &HFF)
        frame(5) = CByte(((frameLength And &H7) << 5) Or &H1F)
        frame(6) = &HFC                                   ' one raw data block
        Return frame
    End Function

    ''' <summary>The phone, section 2.1: HTTP/1.1 200, audio/aac, chunked; the body ends cleanly after <paramref name="frames"/>.</summary>
    Public Shared Async Function PhoneChunked(connection As LiveMockConnection, frames As Integer, paceMs As Integer, token As CancellationToken, Optional endCleanly As Boolean = True) As Task
        connection.WriteAscii("HTTP/1.1 200 OK" & vbCrLf & "Content-Type: audio/aac" & vbCrLf & "Transfer-Encoding: chunked" & vbCrLf &
                              "Accept-Ranges: none" & vbCrLf & "icy-name: Test phone" & vbCrLf & "icy-genre: live" & vbCrLf &
                              "icy-pub: 0" & vbCrLf & "icy-br: 128" & vbCrLf & vbCrLf)
        Dim frame As Byte() = AdtsFrame()
        For i As Integer = 1 To frames
            token.ThrowIfCancellationRequested()
            connection.WriteAscii(frame.Length.ToString("x", Globalization.CultureInfo.InvariantCulture) & vbCrLf)
            connection.Write(frame)
            connection.WriteAscii(vbCrLf)
            If paceMs > 0 Then Await Task.Delay(paceMs, token).ConfigureAwait(False)
        Next
        If endCleanly Then connection.WriteAscii("0" & vbCrLf & vbCrLf)
    End Function

    ''' <summary>The watch, section 2.2: HTTP/1.0 200, audio/aac, raw bytes until the socket closes.</summary>
    Public Shared Async Function WatchRaw(connection As LiveMockConnection, frames As Integer, paceMs As Integer, token As CancellationToken) As Task
        connection.WriteAscii("HTTP/1.0 200 OK" & vbCrLf & "Content-Type: audio/aac" & vbCrLf & "Cache-Control: no-cache" & vbCrLf &
                              "Connection: close" & vbCrLf & vbCrLf)
        Dim frame As Byte() = AdtsFrame()
        For i As Integer = 1 To frames
            token.ThrowIfCancellationRequested()
            connection.Write(frame)
            If paceMs > 0 Then Await Task.Delay(paceMs, token).ConfigureAwait(False)
        Next
    End Function

    ''' <summary>
    ''' Keeps the connection open until the listener under test hangs up (a read returns 0 or throws), so the
    ''' device's own "closed#N" event marks the moment the listener let go.
    ''' </summary>
    Public Shared Async Function UntilPeerCloses(connection As LiveMockConnection, token As CancellationToken) As Task
        Dim one(0) As Byte
        Try
            While Not token.IsCancellationRequested
                Dim n As Integer = Await connection.Stream.ReadAsync(one, 0, 1, token).ConfigureAwait(False)
                If n <= 0 Then Return
            End While
        Catch
        End Try
    End Function

    ''' <summary>The watch's fifth listener, section 2.2: HTTP/1.0 503, then the socket closes.</summary>
    Public Shared Sub WatchBusy(connection As LiveMockConnection)
        connection.WriteAscii("HTTP/1.0 503 Service Unavailable" & vbCrLf & vbCrLf)
    End Sub

    ''' <summary>Any status line with no body.</summary>
    Public Shared Sub Status(connection As LiveMockConnection, code As Integer, reason As String, Optional extraHeader As String = "")
        connection.WriteAscii("HTTP/1.1 " & code.ToString(Globalization.CultureInfo.InvariantCulture) & " " & reason & vbCrLf &
                              extraHeader & "Content-Length: 0" & vbCrLf & "Connection: close" & vbCrLf & vbCrLf)
    End Sub

#End Region

End Class

''' <summary>A stand-in for the player: reads a session's stream to the end on its own thread.</summary>
Friend NotInheritable Class LiveMockPlayer

    ''' <summary>Completes when the stream returned 0 (end) or failed.</summary>
    Public ReadOnly Property Completion As Task

    Private bytes As Long

    Public Sub New(stream As Stream)
        _Completion = System.Threading.Tasks.Task.Run(
            Sub()
                Dim buffer(4095) As Byte
                Try
                    While True
                        Dim n As Integer = stream.Read(buffer, 0, buffer.Length)
                        If n <= 0 Then Exit While
                        Interlocked.Add(bytes, n)
                    End While
                Catch
                End Try
            End Sub)
    End Sub

    Public ReadOnly Property Total As Long
        Get
            Return Interlocked.Read(bytes)
        End Get
    End Property

End Class
#End If
