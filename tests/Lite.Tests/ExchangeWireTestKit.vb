#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Security
Imports System.Net.Sockets
Imports System.Security.Authentication
Imports System.Security.Cryptography
Imports System.Security.Cryptography.X509Certificates
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit
#If COMPANION_TESTS Then
Imports FastMediaSorterCompanion
#End If

' Ticket 180: the shared kit of the exchange client tests - a TLS stub server on the loopback address only, a self-signed
' certificate made per run, a scripted connection with the library's own frame reader and writer, and a few stream helpers.
' Compiled into Lite.Tests (the library) and linked into Companion.Tests (the agent), so both speak to the same stub.
'
' Nothing here leaves the machine: every socket is bound to 127.0.0.1, and no test needs a server of anyone's.

Friend Module ExchangeWireTestKit

    ''' <summary>The repository root (the folder that holds src and exchange-server), found from the test's own folder upwards.</summary>
    Friend Function RepoRoot() As String
        Dim dir As String = AppContext.BaseDirectory
        While Not String.IsNullOrEmpty(dir)
            If Directory.Exists(Path.Combine(dir, "src")) AndAlso Directory.Exists(Path.Combine(dir, "exchange-server")) Then Return dir
            dir = Path.GetDirectoryName(dir)
        End While
        Return ""
    End Function

    ''' <summary>An RSA self-signed certificate valid for 127.0.0.1 and localhost, with a key SChannel can use.</summary>
    Friend Function NewSelfSignedCertificate() As X509Certificate2
        Using rsa As RSA = RSA.Create(2048)
            Dim req As New CertificateRequest("CN=exchange-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            Dim san As New SubjectAlternativeNameBuilder()
            san.AddIpAddress(IPAddress.Loopback)
            san.AddDnsName("localhost")
            req.CertificateExtensions.Add(san.Build())
            req.CertificateExtensions.Add(New X509EnhancedKeyUsageExtension(New OidCollection From {New Oid("1.3.6.1.5.5.7.3.1")}, False))
            Using cert As X509Certificate2 = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2))
                Return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), Nothing)
            End Using
        End Using
    End Function

    Friend Function FingerprintOf(cert As X509Certificate2) As String
        Return ExchangeTrust.FingerprintOf(cert.RawData)
    End Function

    ''' <summary>A connected pair of loopback streams (what a tunnel and a service would be).</summary>
    Friend Async Function LoopbackPairAsync() As Task(Of Tuple(Of Stream, Stream))
        Dim listener As New TcpListener(IPAddress.Loopback, 0)
        listener.Start()
        Try
            Dim port As Integer = DirectCast(listener.LocalEndpoint, IPEndPoint).Port
            Dim client As New TcpClient()
            Dim accept As Task(Of TcpClient) = listener.AcceptTcpClientAsync()
            Await client.ConnectAsync(IPAddress.Loopback, port)
            Dim server As TcpClient = Await accept
            Return Tuple.Create(Of Stream, Stream)(client.GetStream(), server.GetStream())
        Finally
            listener.Stop()
        End Try
    End Function

    Friend Function Cts(Optional seconds As Integer = 20) As CancellationTokenSource
        Return New CancellationTokenSource(TimeSpan.FromSeconds(seconds))
    End Function

    ''' <summary>The id of a letter: 22 characters that decode to 16 bytes, for readable fixtures.</summary>
    Friend Function IdOf(letter As Char) As String
        Return New String(letter, 21) & "A"
    End Function

    Friend Function TokenOf(letter As Char) As String
        Return New String(letter, 42) & "A"
    End Function

End Module

''' <summary>One accepted connection of the stub: the raw stream and a frame reader and writer over it.</summary>
Friend NotInheritable Class ExchangeStubConnection

    Public ReadOnly Index As Integer
    Public ReadOnly Stream As Stream
    Public ReadOnly Reader As ExchangeFrameReader
    Public ReadOnly Writer As ExchangeFrameWriter
    Public ReadOnly Token As CancellationToken

    Public Sub New(index As Integer, stream As Stream, token As CancellationToken)
        Me.Index = index
        Me.Stream = stream
        Me.Token = token
        Reader = New ExchangeFrameReader(stream)
        Writer = New ExchangeFrameWriter(stream)
    End Sub

    ''' <summary>The stream authenticated: both limits widen.</summary>
    Public Sub Authenticated()
        Reader.MarkAuthenticated()
        Writer.MarkAuthenticated()
    End Sub

    Public Function ReadAsync() As Task(Of ExchangeEnvelope)
        Return Reader.ReadAsync(Token)
    End Function

    Public Function WriteAsync(env As ExchangeEnvelope) As Task
        Return Writer.WriteAsync(env, Token)
    End Function

    Public Function Refuse(reason As String) As Task
        Return Writer.WriteAsync(New ExchangeEnvelope("refused").SetText("reason", reason), Token)
    End Function

    Public Function Welcome(Optional keepaliveSeconds As Integer = 30, Optional features As String() = Nothing) As ExchangeEnvelope
        Return New ExchangeEnvelope("welcome").SetText("account", "alice").SetText("publicEndpoint", "203.0.113.5:22022").
            SetNumber("keepaliveSeconds", keepaliveSeconds).SetTextList("features", If(features, New String() {"directory", "tunnel", "pairing"})).
            SetText("serverVersion", "26.10.7.1200")
    End Function

End Class

''' <summary>A TLS server on 127.0.0.1 that hands each accepted connection to a script.</summary>
Friend NotInheritable Class ExchangeStubTlsServer
    Implements IDisposable

    Private ReadOnly _listener As TcpListener
    Private ReadOnly _cts As New CancellationTokenSource()
    Private ReadOnly _handler As Func(Of ExchangeStubConnection, Task)
    Private _accepted As Integer
    Private _handshakeFailures As Integer
    Private _certificate As X509Certificate2
    Private ReadOnly _loop As Task

    Public Sub New(handler As Func(Of ExchangeStubConnection, Task), Optional certificate As X509Certificate2 = Nothing)
        _handler = handler
        _certificate = If(certificate, ExchangeWireTestKit.NewSelfSignedCertificate())
        _listener = New TcpListener(IPAddress.Loopback, 0)
        _listener.Start()
        _loop = Task.Run(AddressOf AcceptLoopAsync)
    End Sub

    Public ReadOnly Property Port As Integer
        Get
            Return DirectCast(_listener.LocalEndpoint, IPEndPoint).Port
        End Get
    End Property

    ''' <summary>"127.0.0.1:port", the form an account entry holds.</summary>
    Public ReadOnly Property Address As String
        Get
            Return "127.0.0.1:" & Port.ToString(Globalization.CultureInfo.InvariantCulture)
        End Get
    End Property

    ''' <summary>The certificate served to the NEXT connection (a test replaces it to model a changed certificate).</summary>
    Public Property Certificate As X509Certificate2
        Get
            Return _certificate
        End Get
        Set(value As X509Certificate2)
            _certificate = value
        End Set
    End Property

    Public ReadOnly Property Fingerprint As String
        Get
            Return ExchangeWireTestKit.FingerprintOf(_certificate)
        End Get
    End Property

    ''' <summary>TCP connections whose TLS handshake succeeded.</summary>
    Public ReadOnly Property Accepted As Integer
        Get
            Return Volatile.Read(_accepted)
        End Get
    End Property

    ''' <summary>Handshakes that failed (a client that refused the certificate).</summary>
    Public ReadOnly Property HandshakeFailures As Integer
        Get
            Return Volatile.Read(_handshakeFailures)
        End Get
    End Property

    Private Async Function AcceptLoopAsync() As Task
        While Not _cts.IsCancellationRequested
            Dim client As TcpClient
            Try
                client = Await _listener.AcceptTcpClientAsync(_cts.Token)
            Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is ObjectDisposedException OrElse TypeOf ex Is SocketException
                Return
            End Try
            Dim t As Task = Task.Run(Function() ServeAsync(client))
        End While
    End Function

    Private Async Function ServeAsync(client As TcpClient) As Task
        Using client
            Dim ssl As New SslStream(client.GetStream(), False)
            Try
                Dim options As New SslServerAuthenticationOptions With {
                    .ServerCertificate = _certificate,
                    .EnabledSslProtocols = SslProtocols.Tls12 Or SslProtocols.Tls13,
                    .ClientCertificateRequired = False}
                Await ssl.AuthenticateAsServerAsync(options, _cts.Token)
            Catch ex As Exception When TypeOf ex Is AuthenticationException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is OperationCanceledException
                Interlocked.Increment(_handshakeFailures)
                ssl.Dispose()
                Return
            End Try
            Dim index As Integer = Interlocked.Increment(_accepted)
            Using ssl
                Try
                    Await _handler(New ExchangeStubConnection(index, ssl, _cts.Token))
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is ExchangeWireException OrElse TypeOf ex Is ObjectDisposedException OrElse TypeOf ex Is EndOfStreamException
                    ' a script that ends by its peer leaving is a normal end
                End Try
            End Using
        End Using
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try : _cts.Cancel() : Catch : End Try
        Try : _listener.Stop() : Catch : End Try
        Try : _loop.Wait(1000) : Catch : End Try
        Try : _cts.Dispose() : Catch : End Try
    End Sub

End Class

''' <summary>A plain TCP listener that only records the first bytes it is sent: what a client that must never speak plain would
''' reveal if it did.</summary>
Friend NotInheritable Class ExchangePlainSink
    Implements IDisposable

    Private ReadOnly _listener As TcpListener
    Private ReadOnly _cts As New CancellationTokenSource()
    Private ReadOnly _received As New List(Of Byte)
    Private ReadOnly _gate As New Object()
    Private ReadOnly _loop As Task
    Private _connections As Integer

    Public Sub New()
        _listener = New TcpListener(IPAddress.Loopback, 0)
        _listener.Start()
        _loop = Task.Run(AddressOf RunAsync)
    End Sub

    Public ReadOnly Property Port As Integer
        Get
            Return DirectCast(_listener.LocalEndpoint, IPEndPoint).Port
        End Get
    End Property

    Public ReadOnly Property Connections As Integer
        Get
            Return Volatile.Read(_connections)
        End Get
    End Property

    Public Function Received() As Byte()
        SyncLock _gate
            Return _received.ToArray()
        End SyncLock
    End Function

    Private Async Function RunAsync() As Task
        While Not _cts.IsCancellationRequested
            Dim client As TcpClient
            Try
                client = Await _listener.AcceptTcpClientAsync(_cts.Token)
            Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is ObjectDisposedException OrElse TypeOf ex Is SocketException
                Return
            End Try
            Interlocked.Increment(_connections)
            Dim t As Task = Task.Run(Function() DrainAsync(client))
        End While
    End Function

    Private Async Function DrainAsync(client As TcpClient) As Task
        Using client
            Dim buf(255) As Byte
            Try
                Dim s As NetworkStream = client.GetStream()
                Dim n As Integer = Await s.ReadAsync(buf, 0, buf.Length, _cts.Token)
                SyncLock _gate
                    For i As Integer = 0 To n - 1
                        _received.Add(buf(i))
                    Next
                End SyncLock
                ' answer with plain bytes that are no TLS, then leave
                Await s.WriteAsync(New Byte() {0, 0, 0, 2, 123, 125}, 0, 6, _cts.Token)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is SocketException
            End Try
        End Using
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try : _cts.Cancel() : Catch : End Try
        Try : _listener.Stop() : Catch : End Try
        Try : _loop.Wait(1000) : Catch : End Try
        Try : _cts.Dispose() : Catch : End Try
    End Sub

End Class

''' <summary>A stream that records the buffers handed to Write, so a test can look at them AFTER the call.</summary>
Friend NotInheritable Class ExchangeRecordingStream
    Inherits Stream

    Public ReadOnly Buffers As New List(Of Byte())
    Public ReadOnly Copies As New List(Of Byte())

    Public Overrides ReadOnly Property CanRead As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overrides ReadOnly Property CanSeek As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overrides ReadOnly Property CanWrite As Boolean
        Get
            Return True
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

    Public Overrides Function Read(buffer() As Byte, offset As Integer, count As Integer) As Integer
        Throw New NotSupportedException()
    End Function

    Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
        Throw New NotSupportedException()
    End Function

    Public Overrides Sub SetLength(value As Long)
        Throw New NotSupportedException()
    End Sub

    Public Overrides Sub Write(buffer() As Byte, offset As Integer, count As Integer)
        Buffers.Add(buffer)
        Dim copy(count - 1) As Byte
        Array.Copy(buffer, offset, copy, 0, count)
        Copies.Add(copy)
    End Sub

    Public Overrides Function WriteAsync(buffer() As Byte, offset As Integer, count As Integer, cancellationToken As CancellationToken) As Task
        Write(buffer, offset, count)
        Return Task.CompletedTask
    End Function

End Class

' ------------------------------------------------------------------------------------------------------------------
' Ticket L0278 T-3, slice A: the replay harness of the remote administration client (spec 7.2). Three parts:
'   - the transcript reader: streams, steps and directions of the local frames-v2.json, and the hash pin of that file;
'   - LockStepStream: a duplex stream built from the steps of ONE transcript stream (or from synthetic steps). A client->server
'     step is an EXPECTATION (the bytes the client writes must equal it), a server->client step is SUPPLIED to the client's
'     reads in order, a `closed` step is the end of the stream. A divergence is reported, never waited on;
'   - ScriptedTransport: an IExchangeTransport that hands out an ExchangeTlsConnection built over such a stream (T-0 item 8:
'     the public constructor takes any Stream), so a session is driven through the very type it uses in production.
' ExchangeStubTlsServer stays for the loopback-TLS cases of 7.4 only.
' ------------------------------------------------------------------------------------------------------------------

''' <summary>What one step of a transcript stream asks of a replay.</summary>
Friend Enum ExchangeStepKind
    ''' <summary>client->server: the client must write exactly these bytes (a lock-step EXPECTATION).</summary>
    ClientFrame = 1
    ''' <summary>server->client: handed to the client's reads, in order.</summary>
    ServerFrame = 2
    ''' <summary>The server side ends: a read returns 0 bytes, a clean end of stream.</summary>
    ServerClose = 3
    ''' <summary>Synthetic (the corpus has none): a read throws an IOException over a SocketException ConnectionReset.</summary>
    ServerReset = 4
    ''' <summary>The HTTP upgrade that opens a stream of the WEBSOCKET copy: not a frame, never replayed (7.2).</summary>
    Http = 5
End Enum

''' <summary>The lock-step stream found a divergence from its script (or a test bug that would otherwise hang).</summary>
Friend NotInheritable Class ExchangeLockStepException
    Inherits Exception

    Public ReadOnly Property StreamName As String
    ''' <summary>1-based number of the step the script was at.</summary>
    Public ReadOnly Property StepNumber As Integer

    Public Sub New(message As String, streamName As String, stepNumber As Integer)
        MyBase.New(message)
        Me.StreamName = streamName
        Me.StepNumber = stepNumber
    End Sub
End Class

''' <summary>One step of a transcript stream, as the file has it (or a synthetic one made by the factories below).</summary>
Friend NotInheritable Class ExchangeTranscriptStep

    Public ReadOnly StreamName As String
    ''' <summary>1-based number among the steps of its stream in the file; 0 for a synthetic step.</summary>
    Public ReadOnly Number As Integer
    Public ReadOnly Kind As ExchangeStepKind
    ''' <summary>As written in the file: "client->server", "server->client" or "client->server / server->client" (http).</summary>
    Public ReadOnly Direction As String
    ''' <summary>The body of the frame as JSON text; "" when the step carries none.</summary>
    Public ReadOnly Json As String
    ''' <summary>The whole frame (4-byte big-endian length and the body) in lower-case hex; "" when the step carries none.</summary>
    Public ReadOnly Hex As String
    Public ReadOnly Note As String
    ''' <summary>The whole frame (or the raw bytes of a synthetic raw step); never modified.</summary>
    Public ReadOnly Bytes As Byte()

    Public Sub New(streamName As String, number As Integer, kind As ExchangeStepKind, direction As String, json As String, bytes As Byte(), note As String)
        Me.StreamName = If(streamName, "")
        Me.Number = number
        Me.Kind = kind
        Me.Direction = If(direction, "")
        Me.Json = If(json, "")
        Me.Bytes = If(bytes, Array.Empty(Of Byte)())
        Me.Hex = Convert.ToHexString(Me.Bytes).ToLowerInvariant()
        Me.Note = If(note, "")
    End Sub

    ''' <summary>The frame of a JSON body: the 4-byte big-endian length and the UTF-8 bytes (what the corpus' hex is).</summary>
    Public Shared Function FrameOf(json As String) As Byte()
        Dim body As Byte() = Encoding.UTF8.GetBytes(json)
        Dim frame(3 + body.Length) As Byte
        frame(0) = CByte((body.Length >> 24) And &HFF)
        frame(1) = CByte((body.Length >> 16) And &HFF)
        frame(2) = CByte((body.Length >> 8) And &HFF)
        frame(3) = CByte(body.Length And &HFF)
        Buffer.BlockCopy(body, 0, frame, 4, body.Length)
        Return frame
    End Function

    ''' <summary>A synthetic expectation: the client must write the frame of this JSON.</summary>
    Public Shared Function Expect(json As String, Optional streamName As String = "scripted") As ExchangeTranscriptStep
        Return New ExchangeTranscriptStep(streamName, 0, ExchangeStepKind.ClientFrame, "client->server", json, FrameOf(json), "")
    End Function

    ''' <summary>A synthetic server frame: handed to the client's reads.</summary>
    Public Shared Function Supply(json As String, Optional streamName As String = "scripted") As ExchangeTranscriptStep
        Return New ExchangeTranscriptStep(streamName, 0, ExchangeStepKind.ServerFrame, "server->client", json, FrameOf(json), "")
    End Function

    ''' <summary>Synthetic server bytes that are no well-formed frame (an oversize length, a cut frame, garbage).</summary>
    Public Shared Function SupplyRaw(bytes As Byte(), Optional note As String = "", Optional streamName As String = "scripted") As ExchangeTranscriptStep
        If bytes Is Nothing OrElse bytes.Length = 0 Then Throw New ArgumentException("a raw step carries at least one byte", NameOf(bytes))
        Return New ExchangeTranscriptStep(streamName, 0, ExchangeStepKind.ServerFrame, "server->client", "", bytes, note)
    End Function

    ''' <summary>The server side ends here: a clean end of stream.</summary>
    Public Shared Function Closed(Optional streamName As String = "scripted") As ExchangeTranscriptStep
        Return New ExchangeTranscriptStep(streamName, 0, ExchangeStepKind.ServerClose, "server->client", "", Nothing, "closed")
    End Function

    ''' <summary>The connection is reset here: a read throws an IOException with a SocketException inside.</summary>
    Public Shared Function Reset(Optional streamName As String = "scripted") As ExchangeTranscriptStep
        Return New ExchangeTranscriptStep(streamName, 0, ExchangeStepKind.ServerReset, "server->client", "", Nothing, "reset")
    End Function

    ''' <summary>The `type` of the frame's JSON body; "" for a step without one.</summary>
    Public Function FrameType() As String
        If Json.Length = 0 Then Return ""
        Try
            Using doc As JsonDocument = JsonDocument.Parse(Json)
                Dim t As JsonElement = Nothing
                If doc.RootElement.ValueKind = JsonValueKind.Object AndAlso doc.RootElement.TryGetProperty("type", t) AndAlso t.ValueKind = JsonValueKind.String Then Return t.GetString()
            End Using
        Catch ex As JsonException
        End Try
        Return ""
    End Function

    ''' <summary>One line for a failure message.</summary>
    Public Function Summary() As String
        Select Case Kind
            Case ExchangeStepKind.ClientFrame
                Return "client->server " & Json
            Case ExchangeStepKind.ServerFrame
                Return "server->client " & If(Json.Length > 0, Json, "raw hex " & Hex)
            Case ExchangeStepKind.ServerClose
                Return "the server closes the stream"
            Case ExchangeStepKind.ServerReset
                Return "the connection is reset"
            Case Else
                Return "http opening of a websocket stream"
        End Select
    End Function

End Class

''' <summary>One stream of a transcript: its steps in file order.</summary>
Friend NotInheritable Class ExchangeTranscriptStream

    Public ReadOnly Name As String
    Public ReadOnly Transport As String
    Public ReadOnly Purpose As String
    Public ReadOnly Steps As IReadOnlyList(Of ExchangeTranscriptStep)

    Public Sub New(name As String, transport As String, purpose As String, steps As IEnumerable(Of ExchangeTranscriptStep))
        Me.Name = name
        Me.Transport = If(transport, "")
        Me.Purpose = If(purpose, "")
        Me.Steps = New List(Of ExchangeTranscriptStep)(steps).AsReadOnly()
    End Sub

    ''' <summary>The steps a lock-step replay plays: the `http` openings of the websocket copy are left out.</summary>
    Public Function Replayable() As List(Of ExchangeTranscriptStep)
        Return Steps.Where(Function(s) s.Kind <> ExchangeStepKind.Http).ToList()
    End Function

    ''' <summary>The client->server steps in order.</summary>
    Public Function ClientSteps() As List(Of ExchangeTranscriptStep)
        Return Steps.Where(Function(s) s.Kind = ExchangeStepKind.ClientFrame).ToList()
    End Function

    ''' <summary>The directions as one letter per step: C client frame, S server frame, X the server closes, R reset, H http.</summary>
    Public Function Shape() As String
        Dim sb As New StringBuilder()
        For Each s As ExchangeTranscriptStep In Steps
            Select Case s.Kind
                Case ExchangeStepKind.ClientFrame
                    sb.Append("C"c)
                Case ExchangeStepKind.ServerFrame
                    sb.Append("S"c)
                Case ExchangeStepKind.ServerClose
                    sb.Append("X"c)
                Case ExchangeStepKind.ServerReset
                    sb.Append("R"c)
                Case Else
                    sb.Append("H"c)
            End Select
        Next
        Return sb.ToString()
    End Function

    ''' <summary>A fresh lock-step stream over the replayable steps of this stream.</summary>
    Public Function LockStep() As LockStepStream
        Return New LockStepStream(Replayable())
    End Function

End Class

''' <summary>One named transcript of the file (`admin-native`, `admin-websocket`, ..).</summary>
Friend NotInheritable Class ExchangeTranscript

    Public ReadOnly Name As String
    Public ReadOnly Description As String
    Public ReadOnly Streams As IReadOnlyList(Of ExchangeTranscriptStream)

    Public Sub New(name As String, description As String, streams As IEnumerable(Of ExchangeTranscriptStream))
        Me.Name = name
        Me.Description = If(description, "")
        Me.Streams = New List(Of ExchangeTranscriptStream)(streams).AsReadOnly()
    End Sub

    Public Function StreamNames() As List(Of String)
        Return Streams.Select(Function(s) s.Name).ToList()
    End Function

    ''' <summary>The stream by name; a name the transcript does not have is a failure that lists the names it has.</summary>
    Public Function StreamNamed(name As String) As ExchangeTranscriptStream
        For Each s As ExchangeTranscriptStream In Streams
            If String.Equals(s.Name, name, StringComparison.Ordinal) Then Return s
        Next
        Throw New KeyNotFoundException("transcript '" & Me.Name & "' has no stream '" & name & "'; it has: " & String.Join(", ", StreamNames()))
    End Function

End Class

''' <summary>The local frames-v2.json: pinned by its SHA-256 (7.2) and read into transcripts, streams and steps.</summary>
Friend NotInheritable Class ExchangeTranscriptFile

    ''' <summary>The SHA-256 (lower-case hex) of the whole file, the corpus adopted in catalog 1.11 as fmsw-245-completion.</summary>
    Public Const PinnedSha256 As String = "5cf90a6f6b1990155dfb346c581b97eb4f690531530f4e7a832219a1156c194b"
    Public Const PinnedLength As Integer = 524510

    Private ReadOnly _transcripts As New List(Of ExchangeTranscript)

    Private Sub New()
    End Sub

    Public ReadOnly Property Transcripts As IReadOnlyList(Of ExchangeTranscript)
        Get
            Return _transcripts
        End Get
    End Property

    ''' <summary>Nothing when the bytes are the pinned file; otherwise the failure message (naming both corpora).</summary>
    Public Shared Function PinFailure(bytes As Byte()) As String
        Dim actual As String = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
        If String.Equals(actual, PinnedSha256, StringComparison.Ordinal) AndAlso bytes.Length = PinnedLength Then Return Nothing
        Return "frames-v2.json is not the corpus adopted in catalog 1.11 as fmsw-245-completion (SHA-256 " & PinnedSha256 & ", " &
               PinnedLength.ToString(Globalization.CultureInfo.InvariantCulture) & " bytes): this file is SHA-256 " & actual & ", " &
               bytes.Length.ToString(Globalization.CultureInfo.InvariantCulture) & " bytes. The earlier fmsw-245 corpus is 0C57C52B.., 488112 bytes. " &
               "A change to the corpus is noticed here and never absorbed: take it to the catalog first, then move the pin (L0278 7.2)."
    End Function

    ''' <summary>Reads the local file (the one copied next to the tests); with <paramref name="requirePin"/> a file that is not the
    ''' pinned one fails the test before any step is read.</summary>
    Public Shared Function Load(Optional requirePin As Boolean = True) As ExchangeTranscriptFile
        Dim bytes As Byte() = File.ReadAllBytes(ExchangeVectorCorpus.TranscriptPath())
        If requirePin Then
            Dim problem As String = PinFailure(bytes)
            If problem IsNot Nothing Then Assert.Fail(problem)
        End If
        Return Parse(bytes)
    End Function

    ''' <summary>The `admin-native` transcript of the pinned file: the eight streams of 7.2.</summary>
    Public Shared Function AdminNative() As ExchangeTranscript
        Return Load().Transcript("admin-native")
    End Function

    ''' <summary>The `admin-websocket` copy: the same bodies, other server-generated values, and an `http` opening per stream.</summary>
    Public Shared Function AdminWebsocket() As ExchangeTranscript
        Return Load().Transcript("admin-websocket")
    End Function

    Public Function Transcript(name As String) As ExchangeTranscript
        For Each t As ExchangeTranscript In _transcripts
            If String.Equals(t.Name, name, StringComparison.Ordinal) Then Return t
        Next
        Throw New KeyNotFoundException("frames-v2.json has no transcript '" & name & "'; it has: " & String.Join(", ", _transcripts.Select(Function(t) t.Name)))
    End Function

    Public Shared Function Parse(bytes As Byte()) As ExchangeTranscriptFile
        Dim text As String = Encoding.UTF8.GetString(bytes).TrimStart(ChrW(&HFEFF))
        Dim result As New ExchangeTranscriptFile()
        Using doc As JsonDocument = JsonDocument.Parse(text)
            For Each tEl As JsonElement In doc.RootElement.GetProperty("transcripts").EnumerateArray()
                ' Only the administration transcripts are replayed through this reader. The data-stream transcripts of the same file carry
                ' steps it has no replay for (`chunk`, a `put` with no body) and belong to the tests of those streams.
                If Not TextOf(tEl, "name").StartsWith("admin-", StringComparison.Ordinal) Then Continue For
                result._transcripts.Add(ReadTranscript(tEl))
            Next
        End Using
        Return result
    End Function

    Private Shared Function TextOf(el As JsonElement, name As String) As String
        Dim v As JsonElement = Nothing
        If el.ValueKind = JsonValueKind.Object AndAlso el.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String Then Return v.GetString()
        Return ""
    End Function

    Private Shared Function ReadTranscript(tEl As JsonElement) As ExchangeTranscript
        Dim name As String = TextOf(tEl, "name")
        Dim stepsByStream As New Dictionary(Of String, List(Of ExchangeTranscriptStep))(StringComparer.Ordinal)
        Dim order As New List(Of String)
        For Each s As JsonElement In tEl.GetProperty("steps").EnumerateArray()
            Dim streamName As String = TextOf(s, "stream")
            Dim list As List(Of ExchangeTranscriptStep) = Nothing
            If Not stepsByStream.TryGetValue(streamName, list) Then
                list = New List(Of ExchangeTranscriptStep)
                stepsByStream(streamName) = list
                order.Add(streamName)
            End If
            list.Add(ReadStep(s, streamName, list.Count + 1))
        Next
        Dim streams As New List(Of ExchangeTranscriptStream)
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        Dim meta As JsonElement = Nothing
        If tEl.TryGetProperty("streams", meta) AndAlso meta.ValueKind = JsonValueKind.Array Then
            For Each m As JsonElement In meta.EnumerateArray()
                Dim id As String = TextOf(m, "id")
                Dim list As List(Of ExchangeTranscriptStep) = Nothing
                If Not stepsByStream.TryGetValue(id, list) Then list = New List(Of ExchangeTranscriptStep)
                streams.Add(New ExchangeTranscriptStream(id, TextOf(m, "transport"), TextOf(m, "purpose"), list))
                seen.Add(id)
            Next
        End If
        For Each id As String In order
            If seen.Add(id) Then streams.Add(New ExchangeTranscriptStream(id, "", "", stepsByStream(id)))
        Next
        Return New ExchangeTranscript(name, TextOf(tEl, "description"), streams)
    End Function

    Private Shared Function ReadStep(s As JsonElement, streamName As String, number As Integer) As ExchangeTranscriptStep
        Dim direction As String = TextOf(s, "direction")
        Dim note As String = TextOf(s, "note")
        Dim http As JsonElement = Nothing
        Dim closed As JsonElement = Nothing
        If s.TryGetProperty("http", http) Then
            Return New ExchangeTranscriptStep(streamName, number, ExchangeStepKind.Http, direction, "", Nothing, note)
        End If
        If s.TryGetProperty("closed", closed) AndAlso closed.ValueKind = JsonValueKind.True Then
            If direction <> "server->client" Then
                Throw New InvalidDataException("stream '" & streamName & "' step " & number.ToString(Globalization.CultureInfo.InvariantCulture) & ": a closed step that is not the server's (" & direction & ") has no replay yet")
            End If
            Return New ExchangeTranscriptStep(streamName, number, ExchangeStepKind.ServerClose, direction, "", Nothing, note)
        End If
        Dim json As String = TextOf(s, "json")
        Dim hex As String = TextOf(s, "hex")
        If json.Length = 0 OrElse hex.Length = 0 Then
            Throw New InvalidDataException("stream '" & streamName & "' step " & number.ToString(Globalization.CultureInfo.InvariantCulture) & " has neither a frame, a close nor an http opening")
        End If
        Dim kind As ExchangeStepKind
        Select Case direction
            Case "client->server"
                kind = ExchangeStepKind.ClientFrame
            Case "server->client"
                kind = ExchangeStepKind.ServerFrame
            Case Else
                Throw New InvalidDataException("stream '" & streamName & "' step " & number.ToString(Globalization.CultureInfo.InvariantCulture) & ": unknown direction '" & direction & "'")
        End Select
        Return New ExchangeTranscriptStep(streamName, number, kind, direction, json, Convert.FromHexString(hex), note)
    End Function

End Class

''' <summary>
''' A duplex stream built from the steps of one transcript stream. A client->server step is an EXPECTATION: the bytes the client
''' writes must equal the step's frame, however many Write calls they arrive in. A server->client step is SUPPLIED to the
''' client's reads, in order, as many bytes as a read asks for. A `closed` step ends the server side (reads return 0), a reset
''' step throws an IOException. After the last step the server is silent: a read waits for its token.
''' A divergence - wrong bytes, a write when the script has the server speaking or closing, a write after the end - is never
''' waited on: the write throws <see cref="ExchangeLockStepException"/> naming the step number, the expected and the actual
''' JSON, every pending and later read throws the same exception, and <see cref="Mismatch"/> / <see cref="Verify"/> report it
''' even when the code under test swallowed the exception. A read that would wait for ever (the script wants the client to write
''' first and nobody does; a silent server and no token) fails after <see cref="StallTimeout"/> of REAL time - only in a
''' broken test, never in a green one.
''' </summary>
Friend NotInheritable Class LockStepStream
    Inherits Stream

    Private ReadOnly _steps As List(Of ExchangeTranscriptStep)
    Private ReadOnly _gate As New Object()
    Private ReadOnly _pending As New List(Of Byte)
    Private ReadOnly _writes As New List(Of Byte())
    Private ReadOnly _frames As New List(Of String)
    Private _index As Integer
    Private _offset As Integer
    Private _fault As ExchangeLockStepException
    Private _clientClosed As Boolean
    Private _signal As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)

    Public Sub New(steps As IEnumerable(Of ExchangeTranscriptStep))
        If steps Is Nothing Then Throw New ArgumentNullException(NameOf(steps))
        _steps = New List(Of ExchangeTranscriptStep)(steps)
        For Each s As ExchangeTranscriptStep In _steps
            If s.Kind = ExchangeStepKind.Http Then
                Throw New ArgumentException("an http opening of the websocket copy is not replayed; use ExchangeTranscriptStream.Replayable()", NameOf(steps))
            End If
        Next
    End Sub

    Public Sub New(ParamArray steps As ExchangeTranscriptStep())
        Me.New(CType(steps, IEnumerable(Of ExchangeTranscriptStep)))
    End Sub

    ''' <summary>Real time a read may wait with nothing to wait for before it is called a hang in the test.</summary>
    Public Property StallTimeout As TimeSpan = TimeSpan.FromSeconds(10)

    Public ReadOnly Property Name As String
        Get
            Return If(_steps.Count > 0, _steps(0).StreamName, "scripted")
        End Get
    End Property

    Public ReadOnly Property StepCount As Integer
        Get
            Return _steps.Count
        End Get
    End Property

    ''' <summary>How many steps are played (0-based index of the step the script is at).</summary>
    Public ReadOnly Property Played As Integer
        Get
            SyncLock _gate
                Return _index
            End SyncLock
        End Get
    End Property

    ''' <summary>The client disposed the stream (what the session does when it ends or is cancelled).</summary>
    Public ReadOnly Property ClientClosed As Boolean
        Get
            SyncLock _gate
                Return _clientClosed
            End SyncLock
        End Get
    End Property

    ''' <summary>The first divergence, as the exception message has it; Nothing while the client kept to the script.</summary>
    Public ReadOnly Property Mismatch As String
        Get
            SyncLock _gate
                Return If(_fault Is Nothing, Nothing, _fault.Message)
            End SyncLock
        End Get
    End Property

    ''' <summary>Every Write call the client made, as copies, in order.</summary>
    Public Function Writes() As List(Of Byte())
        SyncLock _gate
            Return New List(Of Byte())(_writes)
        End SyncLock
    End Function

    ''' <summary>The JSON bodies of the client frames that matched an expectation, in order.</summary>
    Public Function WrittenFrames() As List(Of String)
        SyncLock _gate
            Return New List(Of String)(_frames)
        End SyncLock
    End Function

    ''' <summary>Steps not played yet; a trailing close or reset is not counted (a client need not read the end of the stream).</summary>
    Public Function Unconsumed() As List(Of ExchangeTranscriptStep)
        SyncLock _gate
            Dim list As New List(Of ExchangeTranscriptStep)
            For i As Integer = _index To _steps.Count - 1
                If _steps(i).Kind = ExchangeStepKind.ServerClose OrElse _steps(i).Kind = ExchangeStepKind.ServerReset Then Continue For
                list.Add(_steps(i))
            Next
            Return list
        End SyncLock
    End Function

    ''' <summary>Fails the test (Xunit) with the divergence when there was one - this first, because it is the cause of any other
    ''' symptom - or, with <paramref name="requireConsumed"/>, when part of the script was never played.</summary>
    Public Sub Verify(Optional requireConsumed As Boolean = True)
        Dim problem As String = Nothing
        SyncLock _gate
            If _fault IsNot Nothing Then
                problem = _fault.Message
            ElseIf requireConsumed Then
                Dim left As List(Of ExchangeTranscriptStep) = Unconsumed()
                If left.Count > 0 Then
                    problem = "lock-step stream '" & Name & "': the script was not played to its end - " & left.Count.ToString(Globalization.CultureInfo.InvariantCulture) &
                              " step(s) left, the next is step " & (_index + 1).ToString(Globalization.CultureInfo.InvariantCulture) & ": " & left(0).Summary()
                ElseIf _pending.Count > 0 Then
                    problem = "lock-step stream '" & Name & "': " & _pending.Count.ToString(Globalization.CultureInfo.InvariantCulture) & " byte(s) of a client frame were left unmatched"
                End If
            End If
        End SyncLock
        If problem IsNot Nothing Then Assert.Fail(problem)
    End Sub

    ' ---------------------------------------------------------------- the Stream surface

    Public Overrides ReadOnly Property CanRead As Boolean
        Get
            Return True
        End Get
    End Property

    Public Overrides ReadOnly Property CanSeek As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overrides ReadOnly Property CanWrite As Boolean
        Get
            Return True
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

    Public Overrides Function FlushAsync(cancellationToken As CancellationToken) As Task
        Return Task.CompletedTask
    End Function

    Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
        Throw New NotSupportedException()
    End Function

    Public Overrides Sub SetLength(value As Long)
        Throw New NotSupportedException()
    End Sub

    Public Overrides Function Read(buffer() As Byte, offset As Integer, count As Integer) As Integer
        Return ReadCoreAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult()
    End Function

    Public Overrides Function ReadAsync(buffer() As Byte, offset As Integer, count As Integer, cancellationToken As CancellationToken) As Task(Of Integer)
        Return ReadCoreAsync(buffer, offset, count, cancellationToken)
    End Function

    Public Overrides Sub Write(buffer() As Byte, offset As Integer, count As Integer)
        SyncLock _gate
            WriteLocked(buffer, offset, count)
        End SyncLock
    End Sub

    Public Overrides Function WriteAsync(buffer() As Byte, offset As Integer, count As Integer, cancellationToken As CancellationToken) As Task
        If cancellationToken.IsCancellationRequested Then Return Task.FromCanceled(cancellationToken)
        Try
            Write(buffer, offset, count)
        Catch ex As Exception
            Return Task.FromException(ex)
        End Try
        Return Task.CompletedTask
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            SyncLock _gate
                _clientClosed = True
                Signal()
            End SyncLock
        End If
        MyBase.Dispose(disposing)
    End Sub

    ' ---------------------------------------------------------------- the script

    Private Sub Signal()
        Dim old As TaskCompletionSource(Of Boolean) = _signal
        _signal = New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        old.TrySetResult(True)
    End Sub

    Private Function Fail(message As String) As ExchangeLockStepException
        If _fault Is Nothing Then _fault = New ExchangeLockStepException(message, Name, _index + 1)
        Signal()
        Return _fault
    End Function

    Private Function Here() As String
        Return "'" & Name & "' at step " & (_index + 1).ToString(Globalization.CultureInfo.InvariantCulture) & " of " & _steps.Count.ToString(Globalization.CultureInfo.InvariantCulture)
    End Function

    ''' <summary>The bytes of a client write as text: the JSON body when they hold a frame, hex otherwise.</summary>
    Private Shared Function Describe(bytes As Byte()) As String
        Dim text As String
        If bytes.Length >= 4 Then
            Dim n As Long = (CLng(bytes(0)) << 24) Or (CLng(bytes(1)) << 16) Or (CLng(bytes(2)) << 8) Or CLng(bytes(3))
            Dim available As Integer = bytes.Length - 4
            Dim take As Integer = CInt(Math.Min(n, CLng(available)))
            text = Encoding.UTF8.GetString(bytes, 4, take)
            If n > available Then text &= "  (cut: the header says " & n.ToString(Globalization.CultureInfo.InvariantCulture) & " bytes, " & available.ToString(Globalization.CultureInfo.InvariantCulture) & " written)"
            If n < available Then text &= "  (+" & (available - CInt(n)).ToString(Globalization.CultureInfo.InvariantCulture) & " more byte(s) after the frame)"
            If text.Contains(ChrW(&HFFFD)) Then text &= "  hex " & Convert.ToHexString(bytes)
        Else
            text = "hex " & Convert.ToHexString(bytes)
        End If
        If text.Length > 1500 Then text = text.Substring(0, 1500) & ".."
        Return text
    End Function

    Private Sub WriteLocked(buffer As Byte(), offset As Integer, count As Integer)
        If _fault IsNot Nothing Then Throw _fault
        If _clientClosed Then Throw New ObjectDisposedException(NameOf(LockStepStream), "the client closed the scripted stream")
        If count <= 0 Then Return
        Dim copy(count - 1) As Byte
        System.Buffer.BlockCopy(buffer, offset, copy, 0, count)
        _writes.Add(copy)
        _pending.AddRange(copy)
        While _pending.Count > 0
            If _index >= _steps.Count Then
                Throw Fail("lock-step mismatch on " & Here() & ": the client wrote after the end of the script:" & vbLf & Describe(_pending.ToArray()))
            End If
            Dim current As ExchangeTranscriptStep = _steps(_index)
            If current.Kind <> ExchangeStepKind.ClientFrame Then
                Throw Fail("lock-step mismatch on " & Here() & ": the client wrote when the script has this instead: " & current.Summary() & vbLf & "the client wrote:" & vbLf & Describe(_pending.ToArray()))
            End If
            Dim expected As Byte() = current.Bytes
            Dim common As Integer = Math.Min(_pending.Count, expected.Length)
            For i As Integer = 0 To common - 1
                If _pending(i) <> expected(i) Then
                    Throw Fail("lock-step mismatch on " & Here() & " (client->server)" & vbLf & "expected: " & current.Json & vbLf & "actual:   " & Describe(_pending.ToArray()))
                End If
            Next
            If _pending.Count < expected.Length Then Return    ' the first part of a frame: the rest may come in another Write
            _frames.Add(current.Json)
            _pending.RemoveRange(0, expected.Length)
            _index += 1
            Signal()
        End While
    End Sub

    ''' <summary>The bytes of a read, 0 for the end of the stream, or -1 when the read has to wait.</summary>
    Private Function TryReadLocked(buffer As Byte(), offset As Integer, count As Integer) As Integer
        While True
            If _fault IsNot Nothing Then Throw _fault
            If _clientClosed Then Throw New ObjectDisposedException(NameOf(LockStepStream), "the client closed the scripted stream")
            If _index >= _steps.Count Then Return -1
            Dim current As ExchangeTranscriptStep = _steps(_index)
            Select Case current.Kind
                Case ExchangeStepKind.ServerFrame
                    Dim left As Integer = current.Bytes.Length - _offset
                    If left <= 0 Then
                        _index += 1
                        _offset = 0
                        Signal()
                        Continue While
                    End If
                    Dim n As Integer = Math.Min(count, left)
                    System.Buffer.BlockCopy(current.Bytes, _offset, buffer, offset, n)
                    _offset += n
                    If _offset >= current.Bytes.Length Then
                        _index += 1
                        _offset = 0
                        Signal()
                    End If
                    Return n
                Case ExchangeStepKind.ServerClose
                    Return 0
                Case ExchangeStepKind.ServerReset
                    Throw New IOException("scripted connection reset", New SocketException(CInt(SocketError.ConnectionReset)))
                Case Else
                    Return -1    ' a client expectation: the script waits for the client to write first
            End Select
        End While
        Return -1
    End Function

    Private Function StalledMessage() As String
        If _index >= _steps.Count Then
            Return "lock-step stall on " & Here() & ": the script has ended, the server side is silent and the read has no token that ends it"
        End If
        Return "lock-step stall on " & Here() & ": a read is waiting for the server, but the script wants the client to write first: " & _steps(_index).Summary()
    End Function

    Private Async Function ReadCoreAsync(buffer As Byte(), offset As Integer, count As Integer, ct As CancellationToken) As Task(Of Integer)
        If buffer Is Nothing Then Throw New ArgumentNullException(NameOf(buffer))
        If count <= 0 Then Return 0
        Using stall As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
            stall.CancelAfter(StallTimeout)
            While True
                ct.ThrowIfCancellationRequested()
                Dim waitOn As Task
                SyncLock _gate
                    Dim n As Integer = TryReadLocked(buffer, offset, count)
                    If n >= 0 Then Return n
                    waitOn = _signal.Task
                End SyncLock
                Try
                    Await waitOn.WaitAsync(stall.Token).ConfigureAwait(False)
                Catch ex As OperationCanceledException When ct.IsCancellationRequested
                    ct.ThrowIfCancellationRequested()
                Catch ex As OperationCanceledException
                    SyncLock _gate
                        Throw Fail(StalledMessage())
                    End SyncLock
                End Try
            End While
        End Using
        Return 0
    End Function

End Class

''' <summary>
''' An <see cref="IExchangeTransport"/> that hands out, one per connect, an <see cref="ExchangeTlsConnection"/> built over a
''' <see cref="LockStepStream"/> with the public constructor - the session under test gets the very type it gets in production.
''' A connect with nothing queued is a failure of the test (an unscripted second connection), recorded so
''' <see cref="Verify"/> reports it even when the session swallowed the exception.
''' </summary>
Friend NotInheritable Class ScriptedTransport
    Implements IExchangeTransport

    ''' <summary>One call of ConnectAsync, as the session made it.</summary>
    Public NotInheritable Class ConnectCall
        Public ReadOnly Host As String
        Public ReadOnly Port As Integer
        Public ReadOnly Pin As String

        Public Sub New(host As String, port As Integer, pin As String)
            Me.Host = host
            Me.Port = port
            Me.Pin = pin
        End Sub
    End Class

    Private NotInheritable Class Entry
        Public Stream As LockStepStream
        Public Failure As ExchangeConnectFailure
        Public Hang As Boolean
        Public Fingerprint As String
        Public Verdict As ExchangeTrustVerdict
        Public KeyPin As String
    End Class

    ''' <summary>A well-formed "SHA256:" fingerprint every scripted connection reports unless the test says another.</summary>
    Public Shared ReadOnly DefaultFingerprint As String = ExchangeTrust.FingerprintOf(Encoding.ASCII.GetBytes("scripted-transport-leaf"))

    Private ReadOnly _gate As New Object()
    Private ReadOnly _queue As New Queue(Of Entry)
    Private ReadOnly _streams As New List(Of LockStepStream)
    Private ReadOnly _calls As New List(Of ConnectCall)
    Private _problem As String

    ''' <summary>Real time a scripted hanging connect waits for its token before it is called a hang in the test.</summary>
    Public Property StallTimeout As TimeSpan = TimeSpan.FromSeconds(10)

    ''' <summary>The next connect returns a connection over this stream.</summary>
    Public Function Enqueue(stream As LockStepStream, Optional fingerprint As String = Nothing,
                            Optional verdict As ExchangeTrustVerdict = ExchangeTrustVerdict.PlatformValid, Optional keyPin As String = "") As ScriptedTransport
        If stream Is Nothing Then Throw New ArgumentNullException(NameOf(stream))
        SyncLock _gate
            _queue.Enqueue(New Entry With {.Stream = stream, .Fingerprint = If(fingerprint, DefaultFingerprint), .Verdict = verdict, .KeyPin = If(keyPin, "")})
            _streams.Add(stream)
        End SyncLock
        Return Me
    End Function

    ''' <summary>The next connect throws <see cref="ExchangeConnectException"/> with this failure (nothing is opened).</summary>
    Public Function EnqueueFailure(failure As ExchangeConnectFailure, Optional fingerprint As String = "") As ScriptedTransport
        SyncLock _gate
            _queue.Enqueue(New Entry With {.Failure = failure, .Fingerprint = If(fingerprint, "")})
        End SyncLock
        Return Me
    End Function

    ''' <summary>The next connect never completes until its token is cancelled (the 15 s connect bound, a cancel while connecting).</summary>
    Public Function EnqueueHang() As ScriptedTransport
        SyncLock _gate
            _queue.Enqueue(New Entry With {.Hang = True})
        End SyncLock
        Return Me
    End Function

    Public ReadOnly Property ConnectCount As Integer
        Get
            SyncLock _gate
                Return _calls.Count
            End SyncLock
        End Get
    End Property

    Public Function Calls() As List(Of ConnectCall)
        SyncLock _gate
            Return New List(Of ConnectCall)(_calls)
        End SyncLock
    End Function

    ''' <summary>The streams queued so far, in the order they were queued.</summary>
    Public Function Streams() As List(Of LockStepStream)
        SyncLock _gate
            Return New List(Of LockStepStream)(_streams)
        End SyncLock
    End Function

    Public Function ConnectAsync(host As String, port As Integer, pin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection) Implements IExchangeTransport.ConnectAsync
        If ct.IsCancellationRequested Then Return Task.FromCanceled(Of ExchangeTlsConnection)(ct)
        Dim item As Entry = Nothing
        SyncLock _gate
            _calls.Add(New ConnectCall(host, port, pin))
            If _queue.Count = 0 Then
                If _problem Is Nothing Then _problem = "scripted transport: connect number " & _calls.Count.ToString(Globalization.CultureInfo.InvariantCulture) & " has no scripted stream - the session opened a connection the test did not expect"
                Return Task.FromException(Of ExchangeTlsConnection)(New ExchangeLockStepException(_problem, "transport", _calls.Count))
            End If
            item = _queue.Dequeue()
        End SyncLock
        If item.Hang Then Return HangAsync(ct)
        If item.Stream Is Nothing Then
            Return Task.FromException(Of ExchangeTlsConnection)(New ExchangeConnectException(item.Failure, item.Fingerprint, "scripted connect failure " & item.Failure.ToString()))
        End If
        Return Task.FromResult(New ExchangeTlsConnection(item.Stream, item.Fingerprint, item.Verdict, item.KeyPin))
    End Function

    Private Async Function HangAsync(ct As CancellationToken) As Task(Of ExchangeTlsConnection)
        Await Task.Delay(StallTimeout, ct).ConfigureAwait(False)
        Throw New ExchangeLockStepException("scripted transport: a hanging connect was not cancelled within " & StallTimeout.TotalSeconds.ToString("0.###", Globalization.CultureInfo.InvariantCulture) & " s", "transport", 0)
    End Function

    ''' <summary>Fails the test when a connect had nothing scripted, when any stream diverged from its script and, with
    ''' <paramref name="requireConsumed"/>, when a queued entry was never connected to or a script was not played to its end.</summary>
    Public Sub Verify(Optional requireConsumed As Boolean = True)
        Dim streams As List(Of LockStepStream)
        Dim problem As String
        Dim unused As Integer
        SyncLock _gate
            streams = New List(Of LockStepStream)(_streams)
            problem = _problem
            unused = _queue.Count
        End SyncLock
        For Each s As LockStepStream In streams
            s.Verify(requireConsumed)
        Next
        If problem IsNot Nothing Then Assert.Fail(problem)
        If requireConsumed AndAlso unused > 0 Then
            Assert.Fail("scripted transport: " & unused.ToString(Globalization.CultureInfo.InvariantCulture) & " scripted connection(s) were never connected to")
        End If
    End Sub

End Class

''' <summary>
''' The handshake seam of the remote administration session (L0278), scripted: each pinned connect and each probe takes the next
''' answer of its own queue, and the calls are recorded in order. A call with nothing queued is a failure of the test, recorded so
''' <see cref="Verify"/> reports it even when the session swallowed the exception.
''' </summary>
Friend NotInheritable Class ScriptedAdminHandshake
    Implements IExchangeAdminHandshake

    Private NotInheritable Class Answer
        Public Stream As LockStepStream
        Public Failure As ExchangeConnectFailure
        Public Hang As Boolean
        Public Fingerprint As String
        Public Verdict As ExchangeTrustVerdict
        Public KeyPin As String
        Public PlatformValid As Boolean
    End Class

    Private ReadOnly _gate As New Object()
    Private ReadOnly _connects As New Queue(Of Answer)
    Private ReadOnly _probes As New Queue(Of Answer)
    Private ReadOnly _streams As New List(Of LockStepStream)
    Private ReadOnly _calls As New List(Of String)
    Private _problem As String

    ''' <summary>Real time a scripted hang waits for its token before it is called a hang in the test.</summary>
    Public Property StallTimeout As TimeSpan = TimeSpan.FromSeconds(10)

    ''' <summary>The next pinned connect returns a connection over this stream.</summary>
    Public Function EnqueueConnect(stream As LockStepStream, Optional fingerprint As String = Nothing,
                                   Optional verdict As ExchangeTrustVerdict = ExchangeTrustVerdict.PlatformValid, Optional keyPin As String = "") As ScriptedAdminHandshake
        If stream Is Nothing Then Throw New ArgumentNullException(NameOf(stream))
        SyncLock _gate
            _connects.Enqueue(New Answer With {.Stream = stream, .Fingerprint = If(fingerprint, ScriptedTransport.DefaultFingerprint), .Verdict = verdict, .KeyPin = If(keyPin, "")})
            _streams.Add(stream)
        End SyncLock
        Return Me
    End Function

    ''' <summary>The next pinned connect throws <see cref="ExchangeConnectException"/> with this failure.</summary>
    Public Function EnqueueConnectFailure(failure As ExchangeConnectFailure, Optional fingerprint As String = "") As ScriptedAdminHandshake
        SyncLock _gate
            _connects.Enqueue(New Answer With {.Failure = failure, .Fingerprint = If(fingerprint, "")})
        End SyncLock
        Return Me
    End Function

    ''' <summary>The next pinned connect never completes until its token is cancelled.</summary>
    Public Function EnqueueConnectHang() As ScriptedAdminHandshake
        SyncLock _gate
            _connects.Enqueue(New Answer With {.Hang = True})
        End SyncLock
        Return Me
    End Function

    ''' <summary>The next probe reports this certificate.</summary>
    Public Function EnqueueProbe(platformValid As Boolean, Optional fingerprint As String = Nothing, Optional keyPin As String = "") As ScriptedAdminHandshake
        SyncLock _gate
            _probes.Enqueue(New Answer With {.PlatformValid = platformValid, .Fingerprint = If(fingerprint, ScriptedTransport.DefaultFingerprint), .KeyPin = If(keyPin, "")})
        End SyncLock
        Return Me
    End Function

    ''' <summary>The next probe throws <see cref="ExchangeConnectException"/> with this failure.</summary>
    Public Function EnqueueProbeFailure(failure As ExchangeConnectFailure, Optional fingerprint As String = "") As ScriptedAdminHandshake
        SyncLock _gate
            _probes.Enqueue(New Answer With {.Failure = failure, .Fingerprint = If(fingerprint, "")})
        End SyncLock
        Return Me
    End Function

    ''' <summary>The next probe never completes until its token is cancelled.</summary>
    Public Function EnqueueProbeHang() As ScriptedAdminHandshake
        SyncLock _gate
            _probes.Enqueue(New Answer With {.Hang = True})
        End SyncLock
        Return Me
    End Function

    ''' <summary>Every call in order: "connect leafPin keyPin" (an empty pin as "-") or "probe".</summary>
    Public Function Calls() As List(Of String)
        SyncLock _gate
            Return New List(Of String)(_calls)
        End SyncLock
    End Function

    ''' <summary>The streams queued so far, in order.</summary>
    Public Function Streams() As List(Of LockStepStream)
        SyncLock _gate
            Return New List(Of LockStepStream)(_streams)
        End SyncLock
    End Function

    Private Function NextAnswer(queue As Queue(Of Answer), what As String) As Answer
        SyncLock _gate
            If queue.Count > 0 Then Return queue.Dequeue()
            If _problem Is Nothing Then _problem = "scripted handshake: " & what & " number " & _calls.Count.ToString(Globalization.CultureInfo.InvariantCulture) & " has no scripted answer"
            Return Nothing
        End SyncLock
    End Function

    Public Function ConnectPinnedAsync(host As String, port As Integer, leafPin As String, keyPin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection) Implements IExchangeAdminHandshake.ConnectPinnedAsync
        If ct.IsCancellationRequested Then Return Task.FromCanceled(Of ExchangeTlsConnection)(ct)
        SyncLock _gate
            _calls.Add("connect " & If(String.IsNullOrEmpty(leafPin), "-", leafPin) & " " & If(String.IsNullOrEmpty(keyPin), "-", keyPin))
        End SyncLock
        Dim item As Answer = NextAnswer(_connects, "connect")
        If item Is Nothing Then Return Task.FromException(Of ExchangeTlsConnection)(New ExchangeLockStepException(_problem, "handshake", 0))
        If item.Hang Then Return HangAsync(Of ExchangeTlsConnection)(ct)
        If item.Stream Is Nothing Then
            Return Task.FromException(Of ExchangeTlsConnection)(New ExchangeConnectException(item.Failure, item.Fingerprint, "scripted connect failure " & item.Failure.ToString()))
        End If
        Return Task.FromResult(New ExchangeTlsConnection(item.Stream, item.Fingerprint, item.Verdict, item.KeyPin))
    End Function

    Public Function ProbeAsync(host As String, port As Integer, ct As CancellationToken) As Task(Of ExchangeCertificateInfo) Implements IExchangeAdminHandshake.ProbeAsync
        If ct.IsCancellationRequested Then Return Task.FromCanceled(Of ExchangeCertificateInfo)(ct)
        SyncLock _gate
            _calls.Add("probe")
        End SyncLock
        Dim item As Answer = NextAnswer(_probes, "probe")
        If item Is Nothing Then Return Task.FromException(Of ExchangeCertificateInfo)(New ExchangeLockStepException(_problem, "handshake", 0))
        If item.Hang Then Return HangAsync(Of ExchangeCertificateInfo)(ct)
        If item.Fingerprint Is Nothing OrElse (item.Fingerprint.Length = 0 AndAlso Not item.PlatformValid) Then
            Return Task.FromException(Of ExchangeCertificateInfo)(New ExchangeConnectException(item.Failure, "", "scripted probe failure " & item.Failure.ToString()))
        End If
        Return Task.FromResult(New ExchangeCertificateInfo(item.Fingerprint, item.PlatformValid, item.KeyPin))
    End Function

    Private Async Function HangAsync(Of T)(ct As CancellationToken) As Task(Of T)
        Await Task.Delay(StallTimeout, ct).ConfigureAwait(False)
        Throw New ExchangeLockStepException("scripted handshake: a hanging call was not cancelled within " & StallTimeout.TotalSeconds.ToString("0.###", Globalization.CultureInfo.InvariantCulture) & " s", "handshake", 0)
    End Function

    ''' <summary>Fails the test when a call had nothing scripted, when a stream diverged from its script and, with
    ''' <paramref name="requireConsumed"/>, when a queued answer was never used or a script was not played to its end.</summary>
    Public Sub Verify(Optional requireConsumed As Boolean = True)
        Dim streams As List(Of LockStepStream)
        Dim problem As String
        Dim unused As Integer
        SyncLock _gate
            streams = New List(Of LockStepStream)(_streams)
            problem = _problem
            unused = _connects.Count + _probes.Count
        End SyncLock
        For Each s As LockStepStream In streams
            s.Verify(requireConsumed)
        Next
        If problem IsNot Nothing Then Assert.Fail(problem)
        If requireConsumed AndAlso unused > 0 Then
            Assert.Fail("scripted handshake: " & unused.ToString(Globalization.CultureInfo.InvariantCulture) & " scripted answer(s) were never used")
        End If
    End Sub

End Class

''' <summary>
''' The injected time of the remote administration session: <see cref="Delay"/> never waits on a clock. It records the span and stays
''' pending until the test calls <see cref="Fire"/> (the bound elapses) or the token is cancelled. <see cref="Now"/> is the injected
''' monotonic clock, moved by hand.
''' </summary>
Friend NotInheritable Class ManualAdminTime

    Private NotInheritable Class Pending
        Public Span As TimeSpan
        Public Done As TaskCompletionSource(Of Boolean)
    End Class

    Private ReadOnly _gate As New Object()
    Private ReadOnly _pending As New List(Of Pending)
    Private ReadOnly _spans As New List(Of TimeSpan)
    Private _now As Long

    ''' <summary>Monotonic milliseconds; pass as the session's clock: <c>AddressOf time.Now</c>.</summary>
    Public Function Now() As Long
        SyncLock _gate
            Return _now
        End SyncLock
    End Function

    Public Sub Advance(ms As Long)
        SyncLock _gate
            _now += ms
        End SyncLock
    End Sub

    ''' <summary>Pass as the session's delay: <c>AddressOf time.Delay</c>.</summary>
    Public Function Delay(span As TimeSpan, ct As CancellationToken) As Task
        Dim item As New Pending With {.Span = span, .Done = New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)}
        SyncLock _gate
            _spans.Add(span)
            _pending.Add(item)
        End SyncLock
        If ct.CanBeCanceled Then
            ct.Register(Sub()
                            SyncLock _gate
                                _pending.Remove(item)
                            End SyncLock
                            item.Done.TrySetCanceled(ct)
                        End Sub)
        End If
        Return item.Done.Task
    End Function

    ''' <summary>Every span asked for, in order (the bounds of 5.5).</summary>
    Public Function Spans() As List(Of TimeSpan)
        SyncLock _gate
            Return New List(Of TimeSpan)(_spans)
        End SyncLock
    End Function

    ''' <summary>The delays still pending with exactly this span.</summary>
    Public Function PendingCount(span As TimeSpan) As Integer
        SyncLock _gate
            Return _pending.Where(Function(p) p.Span = span).Count()
        End SyncLock
    End Function

    ''' <summary>Elapses every pending delay of this span and returns how many there were.</summary>
    Public Function Fire(span As TimeSpan) As Integer
        Dim hit As List(Of Pending)
        SyncLock _gate
            hit = _pending.Where(Function(p) p.Span = span).ToList()
            For Each p As Pending In hit
                _pending.Remove(p)
            Next
        End SyncLock
        For Each p As Pending In hit
            p.Done.TrySetResult(True)
        Next
        Return hit.Count
    End Function

End Class
#End If
