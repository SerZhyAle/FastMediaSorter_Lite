#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Spec 227 T-4, Q227-1: the one-time LAN receive address behind "Show a code for the phone". The request
''' reader and the receive loop are shared functions of <c>Resource_Phone_Code_Form</c>, so they run here on a
''' loopback listener with no window. Pinned: a POST body is read whole (it arrives with the head, or
''' trickles), the token is matched as a parameter and not as a substring, a client that says nothing never
''' holds up the phone, and the token is single use.
''' </summary>
Public Class PhoneCodeReceiveTests

    Private Const Token As String = "0123456789abcdef0123456789abcdef"

    ''' <summary>An FMSSFTP2 code has raw ampersands and percent escapes of its own.</summary>
    Private Const Code As String = "FMSSFTP2:h=192.168.7.43&p=2022&u=phone&pw=p%40ss&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"

    Private Const Landing As String = "<html><body>landing</body></html>"

    Private Shared Function Wire(method As String, target As String, Optional body As String = Nothing) As Byte()
        Dim bodyBytes As Byte() = If(body Is Nothing, Array.Empty(Of Byte)(), Encoding.UTF8.GetBytes(body))
        Dim head As String = method & " " & target & " HTTP/1.1" & vbCrLf & "Host: 192.168.7.43" & vbCrLf
        If body IsNot Nothing Then
            head &= "Content-Type: text/plain" & vbCrLf & "Content-Length: " & bodyBytes.Length.ToString(Globalization.CultureInfo.InvariantCulture) & vbCrLf
        End If
        head &= vbCrLf
        Return Encoding.UTF8.GetBytes(head).Concat(bodyBytes).ToArray()
    End Function

    ''' <summary>A stream that hands over one byte per read, the worst a slow link can do.</summary>
    Private NotInheritable Class TrickleStream
        Inherits MemoryStream

        Public Sub New(data As Byte())
            MyBase.New(data)
        End Sub

        Public Overrides Function ReadAsync(buffer As Byte(), offset As Integer, count As Integer, cancellationToken As CancellationToken) As Task(Of Integer)
            Return MyBase.ReadAsync(buffer, offset, Math.Min(count, 1), cancellationToken)
        End Function
    End Class

    Private Shared Async Function ReadWholeAsync(stream As Stream) As Task(Of String)
        Return Await Resource_Phone_Code_Form.ReadRequestAsync(stream, CancellationToken.None)
    End Function

    ' ---------------------------------------------------------------- the request reader --

    <Fact>
    Public Async Function A_post_body_that_arrives_with_the_head_is_kept_whole() As Task
        Using stream As New MemoryStream(Wire("POST", "/fms-intake?token=" & Token, Code))
            Dim request As String = Await ReadWholeAsync(stream)
            Assert.Equal(Code, Resource_Phone_Code_Form.ExtractIntakePayload(request, Token))
        End Using
    End Function

    <Fact>
    Public Async Function A_body_longer_than_the_head_buffer_is_read_to_its_content_length() As Task
        Dim document As String = New String("x"c, 20000)
        Using stream As New MemoryStream(Wire("POST", "/fms-intake?token=" & Token, document))
            Dim request As String = Await ReadWholeAsync(stream)
            Assert.Equal(document, Resource_Phone_Code_Form.ExtractIntakePayload(request, Token))
        End Using
    End Function

    <Fact>
    Public Async Function A_body_that_trickles_in_byte_by_byte_is_kept_whole() As Task
        Using stream As New TrickleStream(Wire("POST", "/fms-intake?token=" & Token, Code))
            Dim request As String = Await ReadWholeAsync(stream)
            Assert.Equal(Code, Resource_Phone_Code_Form.ExtractIntakePayload(request, Token))
        End Using
    End Function

    <Fact>
    Public Async Function A_body_over_the_cap_is_bounded_not_buffered_whole() As Task
        Dim huge As String = New String("y"c, Resource_Phone_Code_Form.MaxBodyBytes + 5000)
        Using stream As New MemoryStream(Wire("POST", "/fms-intake?token=" & Token, huge))
            Dim request As String = Await ReadWholeAsync(stream)
            Dim payload As String = Resource_Phone_Code_Form.ExtractIntakePayload(request, Token)
            Assert.Equal(Resource_Phone_Code_Form.MaxBodyBytes, payload.Length)
        End Using
    End Function

    <Fact>
    Public Async Function A_get_with_the_code_in_the_query_is_unescaped_once() As Task
        Dim target As String = "/fms-intake?token=" & Token & "&code=" & Uri.EscapeDataString(Code)
        Using stream As New MemoryStream(Wire("GET", target))
            Dim request As String = Await ReadWholeAsync(stream)
            Assert.Equal(Code, Resource_Phone_Code_Form.ExtractIntakePayload(request, Token))
        End Using
    End Function

    <Fact>
    Public Sub A_raw_ampersand_code_is_taken_to_the_end_of_the_query()
        Const raw As String = "FMSSFTP2:h=192.168.7.43&p=2022&u=phone&pw=pw"
        Dim request As String = "GET /fms-intake?token=" & Token & "&code=" & raw & " HTTP/1.1" & vbCrLf & vbCrLf
        Assert.Equal(raw, Resource_Phone_Code_Form.ExtractIntakePayload(request, Token))
    End Sub

    <Fact>
    Public Sub The_token_is_matched_as_a_parameter_not_as_a_substring()
        Dim body As String = Code
        ' A longer token that merely starts with the right one.
        Assert.Null(Resource_Phone_Code_Form.ExtractIntakePayload(
            "POST /fms-intake?token=" & Token & "ff HTTP/1.1" & vbCrLf & vbCrLf & body, Token))
        ' A wrong token.
        Assert.Null(Resource_Phone_Code_Form.ExtractIntakePayload(
            "POST /fms-intake?token=" & New String("0"c, 32) & " HTTP/1.1" & vbCrLf & vbCrLf & body, Token))
        ' No token at all.
        Assert.Null(Resource_Phone_Code_Form.ExtractIntakePayload(
            "POST /fms-intake HTTP/1.1" & vbCrLf & vbCrLf & body, Token))
        ' The right token only inside the code's own text.
        Assert.Null(Resource_Phone_Code_Form.ExtractIntakePayload(
            "GET /fms-intake?token=nope&code=x&token=" & Token & " HTTP/1.1" & vbCrLf & vbCrLf, Token))
        ' The right token in a header is not a token.
        Assert.Null(Resource_Phone_Code_Form.ExtractIntakePayload(
            "POST /fms-intake HTTP/1.1" & vbCrLf & "X-Token: token=" & Token & vbCrLf & vbCrLf & body, Token))
    End Sub

    ' ---------------------------------------------------------------- the receive loop ---

    Private NotInheritable Class Rig
        Implements IDisposable

        Private ReadOnly _listener As TcpListener
        Private ReadOnly _cts As New CancellationTokenSource()
        Private ReadOnly _received As New List(Of String)()
        Private ReadOnly _running As Task

        Friend ReadOnly Port As Integer
        Friend ReadOnly First As New TaskCompletionSource(Of String)(TaskCreationOptions.RunContinuationsAsynchronously)

        Friend Sub New(Optional deadline As TimeSpan = Nothing)
            _listener = New TcpListener(IPAddress.Loopback, 0)
            _listener.Start()
            Port = DirectCast(_listener.LocalEndpoint, IPEndPoint).Port
            Dim handler As Action(Of String) = AddressOf OnCode
            Dim cancellation As CancellationToken = _cts.Token
            Dim listener As TcpListener = _listener
            _running = Task.Run(Function() Resource_Phone_Code_Form.ReceiveAsync(listener, PhoneCodeReceiveTests.Token, Landing, handler, cancellation, deadline))
        End Sub

        Private Sub OnCode(code As String)
            SyncLock _received
                _received.Add(code)
            End SyncLock
            First.TrySetResult(code)
        End Sub

        Friend ReadOnly Property ReceivedCount As Integer
            Get
                SyncLock _received
                    Return _received.Count
                End SyncLock
            End Get
        End Property

        ''' <summary>Cancel and stop, the way the window does; True when the loop ended in time.</summary>
        Friend Function Stop_() As Boolean
            _cts.Cancel()
            _listener.Stop()
            Try
                Return _running.Wait(TimeSpan.FromSeconds(5))
            Catch ex As AggregateException
                Return True
            End Try
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Stop_()
            _cts.Dispose()
        End Sub
    End Class

    ''' <summary>One complete exchange: connect, send, read the answer to the server's close.</summary>
    Private Shared Async Function ExchangeAsync(port As Integer, wire As Byte()) As Task(Of String)
        Using client As New TcpClient()
            Await client.ConnectAsync(IPAddress.Loopback, port)
            Dim stream As NetworkStream = client.GetStream()
            Await stream.WriteAsync(wire, 0, wire.Length)
            Using reader As New StreamReader(stream, Encoding.UTF8)
                Return Await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10))
            End Using
        End Using
    End Function

    ''' <summary>The server has closed this connection: a read answers end-of-stream (or a reset) in time.</summary>
    Private Shared Async Function IsClosedByServerAsync(client As TcpClient, within As TimeSpan) As Task(Of Boolean)
        Dim buffer(0) As Byte
        Try
            Dim count As Integer = Await client.GetStream().ReadAsync(buffer, 0, 1).WaitAsync(within)
            Return count = 0
        Catch ex As IOException
            Return True
        Catch ex As TimeoutException
            Return False
        End Try
    End Function

    <Fact>
    Public Async Function The_phone_gets_ok_and_the_code_arrives_whole() As Task
        Using rig As New Rig()
            Dim answer As String = Await ExchangeAsync(rig.Port, Wire("POST", "/fms-intake?token=" & Token, Code))
            Assert.StartsWith("HTTP/1.1 200", answer)
            Assert.EndsWith("OK", answer)
            Assert.Same(rig.First.Task, Await Task.WhenAny(rig.First.Task, Task.Delay(5000)))
            Assert.Equal(Code, rig.First.Task.Result)
        End Using
    End Function

    <Fact>
    Public Async Function A_client_that_connects_and_says_nothing_does_not_hold_up_the_phone() As Task
        ' A long deadline: were the loop serial, the phone would wait behind this connection.
        Using rig As New Rig(TimeSpan.FromSeconds(30))
            Using idle As New TcpClient()
                Await idle.ConnectAsync(IPAddress.Loopback, rig.Port)
                Dim answer As String = Await ExchangeAsync(rig.Port, Wire("POST", "/fms-intake?token=" & Token, Code))
                Assert.StartsWith("HTTP/1.1 200", answer)
                Assert.Same(rig.First.Task, Await Task.WhenAny(rig.First.Task, Task.Delay(5000)))
                Assert.Equal(Code, rig.First.Task.Result)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_client_that_says_nothing_is_dropped_at_the_deadline() As Task
        Using rig As New Rig(TimeSpan.FromMilliseconds(400))
            Using idle As New TcpClient()
                Await idle.ConnectAsync(IPAddress.Loopback, rig.Port)
                Dim stopwatch As Stopwatch = Stopwatch.StartNew()
                Assert.True(Await IsClosedByServerAsync(idle, TimeSpan.FromSeconds(5)), "the idle connection was never closed")
                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(4), "the idle connection outlived its deadline")
            End Using
            Assert.Equal(0, rig.ReceivedCount)
        End Using
    End Function

    <Fact>
    Public Async Function The_token_is_single_use() As Task
        Using rig As New Rig()
            Dim first As String = Await ExchangeAsync(rig.Port, Wire("POST", "/fms-intake?token=" & Token, Code))
            Assert.EndsWith("OK", first)
            Assert.Same(rig.First.Task, Await Task.WhenAny(rig.First.Task, Task.Delay(5000)))

            Dim second As String = Await ExchangeAsync(rig.Port, Wire("POST", "/fms-intake?token=" & Token, "FMSSFTP2:h=10.0.0.9&p=22&u=x&pw=y"))
            Assert.Contains("landing", second)
            Assert.Equal(1, rig.ReceivedCount)
            Assert.Equal(Code, rig.First.Task.Result)
        End Using
    End Function

    <Fact>
    Public Async Function A_wrong_token_or_a_browser_visit_gets_the_landing_page_and_the_window_keeps_waiting() As Task
        Using rig As New Rig()
            Dim wrong As String = Await ExchangeAsync(rig.Port, Wire("POST", "/fms-intake?token=" & New String("1"c, 32), Code))
            Assert.StartsWith("HTTP/1.1 200", wrong)
            Assert.Contains("landing", wrong)

            Dim browser As String = Await ExchangeAsync(rig.Port, Wire("GET", "/fms-intake?token=" & Token))
            Assert.Contains("landing", browser)
            Assert.Equal(0, rig.ReceivedCount)

            ' Still open for the phone.
            Dim phone As String = Await ExchangeAsync(rig.Port, Wire("POST", "/fms-intake?token=" & Token, Code))
            Assert.EndsWith("OK", phone)
            Assert.Same(rig.First.Task, Await Task.WhenAny(rig.First.Task, Task.Delay(5000)))
        End Using
    End Function

    <Fact>
    Public Async Function Idle_connections_over_the_cap_are_released_and_the_phone_still_gets_through() As Task
        Using rig As New Rig(TimeSpan.FromMilliseconds(400))
            Dim idle As New List(Of TcpClient)()
            Try
                For i As Integer = 1 To Resource_Phone_Code_Form.MaxConcurrentClients + 4
                    Dim client As New TcpClient()
                    idle.Add(client)
                    Await client.ConnectAsync(IPAddress.Loopback, rig.Port)
                Next
                ' Every one of them - served-and-timed-out or refused over the cap - ends closed.
                For Each client As TcpClient In idle
                    Assert.True(Await IsClosedByServerAsync(client, TimeSpan.FromSeconds(5)), "an idle connection stayed open")
                Next

                Dim answer As String = Await ExchangeAsync(rig.Port, Wire("POST", "/fms-intake?token=" & Token, Code))
                Assert.EndsWith("OK", answer)
                Assert.Same(rig.First.Task, Await Task.WhenAny(rig.First.Task, Task.Delay(5000)))
            Finally
                For Each client As TcpClient In idle
                    client.Dispose()
                Next
            End Try
        End Using
    End Function

    <Fact>
    Public Sub Stopping_the_listener_ends_the_loop()
        Using rig As New Rig()
            Assert.True(rig.Stop_(), "the receive loop did not end when the window closed")
        End Using
    End Sub

    ' ---------------------------------------------------------------- the decoder bound ---

    <Fact>
    Public Sub A_small_image_that_declares_a_huge_canvas_is_refused_from_its_header()
        ' WEBP: GDI+ cannot read it, so the Magick.NET fallback is the path under test.
        Dim width As Integer = 4100
        Dim height As Integer = 4000
        Assert.True(CLng(width) * height > QrCodeDecoder.MaxPixelCount)
        Dim bytes As Byte()
        Using canvas As New ImageMagick.MagickImage(ImageMagick.MagickColors.Transparent, CUInt(width), CUInt(height))
            Using ms As New MemoryStream()
                canvas.Write(ms, ImageMagick.MagickFormat.WebP)
                bytes = ms.ToArray()
            End Using
        End Using
        Assert.True(bytes.Length < QrCodeDecoder.MaxFileBytes, "the fixture must stay under the file bound")

        Assert.True(QrCodeDecoder.ExceedsPixelBound(bytes))
        Dim result As QrDecodeResult = QrCodeDecoder.DecodeBytes(bytes)
        Assert.False(result.Succeeded)
        Assert.Equal(QrDecodeOutcome.TooLarge, result.Outcome)
    End Sub

    <Fact>
    Public Sub An_ordinary_or_unreadable_header_is_not_over_the_bound()
        Assert.False(QrCodeDecoder.ExceedsPixelBound(New Byte() {1, 2, 3, 4, 5}))
        Assert.False(QrCodeDecoder.ExceedsPixelBound(Array.Empty(Of Byte)()))
    End Sub

End Class
#End If
