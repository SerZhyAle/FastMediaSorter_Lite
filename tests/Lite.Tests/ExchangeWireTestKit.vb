#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Net
Imports System.Net.Security
Imports System.Net.Sockets
Imports System.Security.Authentication
Imports System.Security.Cryptography
Imports System.Security.Cryptography.X509Certificates
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
#End If
