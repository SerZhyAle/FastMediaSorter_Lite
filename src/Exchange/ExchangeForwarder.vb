#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Net
Imports System.Net.Sockets
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 180 T-1. The loopback forwarder of DEVICE-EXCHANGE 7.7: "a local port whose every accepted connection
' becomes one `connect`". A player (or an SFTP client) that cannot speak the exchange wire is pointed at
' 127.0.0.1:<Port>; each connection it makes opens its own data stream through the opener, and the two are
' bridged byte for byte.
'
' The rule the tests hold: ONE opener call per accepted connection - never zero (a lost connect), never two (a
' retry that would burn a tunnel), and a refused or failed open closes that one connection and nothing else.
' It listens on the loopback address only; it is created when something attaches a tunnel and never at start.
'
' Ticket 234 E1b (item DM): a forwarder given a `tunnelPin` is the consumer end of a SEALED broadcast tunnel. The player still
' speaks plain HTTP to 127.0.0.1; the forwarder runs the pinned TLS client over the tunnel connection (ExchangeSealedTunnel) and
' pumps the player's bytes through it. A leaf other than the pinned one ends that one connection (state `sealed-failed`) before
' a single byte of the player's request is written. Without a pin the forwarder is what it always was.

Friend NotInheritable Class ExchangeForwarder
    Implements IDisposable

    ''' <summary>Past this many open bridges a new connection is closed at once (a runaway client cannot exhaust the process).</summary>
    Public Const MaxBridges As Integer = 64

    Private ReadOnly _opener As IExchangeDataOpener
    Private ReadOnly _tunnelPin As String
    Private ReadOnly _cts As New CancellationTokenSource()
    Private _listener As TcpListener
    Private _accept As Task
    Private _connectsStarted As Integer
    Private _active As Integer
    Private _lastOutcome As Integer
    Private _lastRefusal As Integer
    Private _lastSealed As Integer = -1
    Private _disposed As Boolean

    ''' <param name="tunnelPin">The `SHA256:` pin of the producer's tunnel leaf (item DM) for a sealed broadcast; Nothing or "" for a
    ''' tunnel without inner TLS (an SFTP tunnel, item DI, and every clear tunnel of today). A pin that is not of the `SHA256:` form
    ''' is not "no pin": every connection then fails closed.</param>
    Public Sub New(opener As IExchangeDataOpener, Optional tunnelPin As String = Nothing)
        If opener Is Nothing Then Throw New ArgumentNullException(NameOf(opener))
        _opener = opener
        _tunnelPin = If(tunnelPin, "")
    End Sub

    ''' <summary>True when the connections run the pinned inner TLS of item DM.</summary>
    Public ReadOnly Property IsSealed As Boolean
        Get
            Return _tunnelPin.Length > 0
        End Get
    End Property

    ''' <summary>The outcome of the most recent pinned TLS attempt; Nothing while there was none (a clear forwarder, or no connection yet).</summary>
    Public ReadOnly Property LastSealedOutcome As ExchangeSealedTunnelOutcome?
        Get
            Dim v As Integer = Volatile.Read(_lastSealed)
            If v < 0 Then Return Nothing
            Return CType(v, ExchangeSealedTunnelOutcome)
        End Get
    End Property

    ''' <summary>`sealed-failed` when the most recent pinned attempt ended in that state, "" otherwise.</summary>
    Public ReadOnly Property LastSealedState As String
        Get
            Dim v As Integer = Volatile.Read(_lastSealed)
            If v < 0 Then Return ""
            Select Case CType(v, ExchangeSealedTunnelOutcome)
                Case ExchangeSealedTunnelOutcome.PinMismatch, ExchangeSealedTunnelOutcome.NoPin, ExchangeSealedTunnelOutcome.TlsFailed
                    Return ConnectionDiagnosisMessages.SealedFailed.Code
                Case Else
                    Return ""
            End Select
        End Get
    End Property

    ''' <summary>The loopback port, once started.</summary>
    Public ReadOnly Property Port As Integer
        Get
            Dim l As TcpListener = _listener
            If l Is Nothing Then Return 0
            Return DirectCast(l.LocalEndpoint, IPEndPoint).Port
        End Get
    End Property

    ''' <summary>How many `connect`s were started: one per accepted connection.</summary>
    Public ReadOnly Property ConnectsStarted As Integer
        Get
            Return Volatile.Read(_connectsStarted)
        End Get
    End Property

    ''' <summary>Bridges open right now.</summary>
    Public ReadOnly Property ActiveBridges As Integer
        Get
            Return Volatile.Read(_active)
        End Get
    End Property

    ''' <summary>The outcome of the most recent opener call.</summary>
    Public ReadOnly Property LastOutcome As ExchangeOpenOutcome
        Get
            Return CType(Volatile.Read(_lastOutcome), ExchangeOpenOutcome)
        End Get
    End Property

    ''' <summary>The refusal code of the most recent refused opener call.</summary>
    Public ReadOnly Property LastRefusal As ExchangeRefusal
        Get
            Return CType(Volatile.Read(_lastRefusal), ExchangeRefusal)
        End Get
    End Property

    ''' <summary>Binds 127.0.0.1 on a port the system chooses and starts accepting.</summary>
    Public Sub Start()
        If _listener IsNot Nothing Then Throw New InvalidOperationException("already started")
        Dim l As New TcpListener(IPAddress.Loopback, 0)
        l.Start()
        _listener = l
        _accept = Task.Run(Function() AcceptLoopAsync(l, _cts.Token))
    End Sub

    Private Async Function AcceptLoopAsync(l As TcpListener, ct As CancellationToken) As Task
        While Not ct.IsCancellationRequested
            Dim client As TcpClient
            Try
                client = Await l.AcceptTcpClientAsync(ct).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                Return
            Catch ex As ObjectDisposedException
                Return
            Catch ex As SocketException
                If ct.IsCancellationRequested Then Return
                Continue While
            End Try
            Dim t As Task = Task.Run(Function() BridgeAsync(client, ct))
        End While
    End Function

    Private Async Function BridgeAsync(client As TcpClient, ct As CancellationToken) As Task
        Dim opened As ExchangeOpenResult = Nothing
        Dim sealedWire As ExchangeSealedTunnelClientResult = Nothing
        Try
            If Volatile.Read(_active) >= MaxBridges Then Return
            Interlocked.Increment(_active)
            Try
                Interlocked.Increment(_connectsStarted)
                client.NoDelay = True
                opened = Await _opener.OpenAsync(ct).ConfigureAwait(False)
                If opened IsNot Nothing Then
                    Volatile.Write(_lastOutcome, CInt(opened.Outcome))
                    Volatile.Write(_lastRefusal, CInt(opened.Refusal))
                End If
                If opened Is Nothing OrElse Not opened.IsConnected Then Return

                Dim wire As IO.Stream = opened.Stream
                If _tunnelPin.Length > 0 Then
                    ' Item DM: the TLS client runs first and nothing of the player's request is read or written until the
                    ' producer has shown the pinned leaf. A failure has already closed the tunnel connection.
                    sealedWire = Await ExchangeSealedTunnel.ClientStreamAsync(opened.Stream, _tunnelPin, ct).ConfigureAwait(False)
                    Volatile.Write(_lastSealed, CInt(sealedWire.Outcome))
                    If Not sealedWire.IsOk Then Return
                    wire = sealedWire.Stream
                End If

                Using bridgeCts As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                    Dim local As NetworkStream = client.GetStream()
                    Dim up As Task = CopyAsync(local, wire, bridgeCts.Token)
                    Dim down As Task = CopyAsync(wire, local, bridgeCts.Token)
                    Await Task.WhenAny(up, down).ConfigureAwait(False)
                    bridgeCts.Cancel()
                End Using
            Finally
                Interlocked.Decrement(_active)
            End Try
        Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is SocketException OrElse TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is ObjectDisposedException
            ' A bridge that breaks closes itself; nothing else is affected.
        Finally
            Try : sealedWire?.Dispose() : Catch : End Try
            Try : opened?.Dispose() : Catch : End Try
            Try : client.Dispose() : Catch : End Try
        End Try
    End Function

    Private Shared Async Function CopyAsync(source As IO.Stream, target As IO.Stream, ct As CancellationToken) As Task
        Dim buffer(16 * 1024 - 1) As Byte
        Try
            While Not ct.IsCancellationRequested
                Dim n As Integer = Await source.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(False)
                If n = 0 Then Return
                Await target.WriteAsync(buffer, 0, n, ct).ConfigureAwait(False)
                Await target.FlushAsync(ct).ConfigureAwait(False)
            End While
        Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is SocketException OrElse TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is ObjectDisposedException
            ' the other direction's close
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        If _disposed Then Return
        _disposed = True
        Try : _cts.Cancel() : Catch : End Try
        Try : _listener?.Stop() : Catch : End Try
        Try : _cts.Dispose() : Catch : End Try
    End Sub

End Class
#End If
