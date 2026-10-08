Option Strict On

Imports System.IO
Imports System.IO.Pipes
Imports System.Security.AccessControl
Imports System.Security.Principal
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 180 T-3 (section 6): the SERVER half of the local channel. The viewer's end is ExchangeAgentClient in src\Exchange.
'
' A named pipe `fms-device-agent-<user SID hash>` whose DACL names the current user only (and denies the network SID, so no
' SMB client can reach it), created with FirstPipeInstance so another process that got there first makes creation FAIL
' instead of the agent serving beside it. One JSON request line per connection, one reply line, then the pipe is closed:
' bounded (a request line is at most 4 KiB, a read gives up after 5 s) and cancellable. It carries no password. The agent
' decides everything; this class is a transport and parses nothing but the request's `type`.
'
' Created only when an account is configured and the agent runs (the dormant rule): no account, no pipe.

Friend NotInheritable Class ExchangeDeviceLocalChannel
    Implements IDisposable

    Private Const MaxInstances As Integer = 8
    Private Const ReadTimeoutMs As Integer = 5000
    Private Const HandlerTimeoutMs As Integer = 40000

    Private ReadOnly _handler As IExchangeAgentHandler
    Private ReadOnly _pipeName As String
    Private ReadOnly _cts As New CancellationTokenSource()
    Private _first As NamedPipeServerStream
    Private _loop As Task
    Private _disposed As Boolean

    Public Sub New(handler As IExchangeAgentHandler, pipeName As String)
        If handler Is Nothing Then Throw New ArgumentNullException(NameOf(handler))
        If String.IsNullOrEmpty(pipeName) Then Throw New ArgumentException("pipeName")
        _handler = handler
        _pipeName = pipeName
    End Sub

    ''' <summary>The production start: the current user's pipe. Throws <see cref="IOException"/> when the name is already taken.</summary>
    Public Shared Function StartDefault(handler As IExchangeAgentHandler) As IDisposable
        Dim ch As New ExchangeDeviceLocalChannel(handler, ExchangeAgentPipe.PipeName())
        ch.Start()
        Return ch
    End Function

    Public ReadOnly Property PipeName As String
        Get
            Return _pipeName
        End Get
    End Property

    ''' <summary>The DACL: this user, full control; the network SID denied.</summary>
    Friend Shared Function BuildSecurity() As PipeSecurity
        Dim security As New PipeSecurity()
        Using id As WindowsIdentity = WindowsIdentity.GetCurrent()
            security.AddAccessRule(New PipeAccessRule(id.User, PipeAccessRights.FullControl, AccessControlType.Allow))
        End Using
        security.AddAccessRule(New PipeAccessRule(New SecurityIdentifier(WellKnownSidType.NetworkSid, Nothing), PipeAccessRights.FullControl, AccessControlType.Deny))
        Return security
    End Function

    Private Function CreateInstance(first As Boolean) As NamedPipeServerStream
        Dim options As PipeOptions = PipeOptions.Asynchronous
        If first Then options = options Or PipeOptions.FirstPipeInstance
        Return NamedPipeServerStreamAcl.Create(_pipeName, PipeDirection.InOut, MaxInstances, PipeTransmissionMode.Byte, options, 0, 0, BuildSecurity())
    End Function

    Public Sub Start()
        If _first IsNot Nothing Then Throw New InvalidOperationException("already started")
        Try
            _first = CreateInstance(True)
        Catch ex As UnauthorizedAccessException
            ' Windows answers "access denied" when the name already has a first instance owned by someone else: the same fact
            Throw New IOException("The pipe name is already in use.", ex)
        End Try
        _loop = Task.Run(Function() AcceptLoopAsync(_cts.Token))
    End Sub

    Private Async Function AcceptLoopAsync(ct As CancellationToken) As Task
        Dim server As NamedPipeServerStream = _first
        _first = server
        While Not ct.IsCancellationRequested
            Dim failed As Boolean = False
            Try
                Await server.WaitForConnectionAsync(ct).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                server.Dispose()
                Return
            Catch ex As IOException
                failed = True
            End Try
            If failed Then
                ' a client that dropped before the handshake finished: take a fresh instance and carry on
                server.Dispose()
                Await Task.Delay(50, CancellationToken.None).ConfigureAwait(False)
                Try
                    server = CreateInstance(False)
                Catch ex As IOException
                    Return
                End Try
                Continue While
            End If
            Dim connected As NamedPipeServerStream = server
            Dim t As Task = Task.Run(Function() ServeAsync(connected, ct))
            Try
                server = CreateInstance(False)
            Catch ex As IOException
                Return   ' the instance limit or the name is gone: no more connections can be accepted
            End Try
        End While
        server.Dispose()
    End Function

    Private Async Function ServeAsync(pipe As NamedPipeServerStream, ct As CancellationToken) As Task
        Using pipe
            Using handlerCts As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                handlerCts.CancelAfter(HandlerTimeoutMs)
                Try
                    Dim line As String
                    Using readCts As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(handlerCts.Token)
                        readCts.CancelAfter(ReadTimeoutMs)
                        line = Await ExchangeAgentPipe.ReadLineAsync(pipe, ExchangeAgentPipe.MaxRequestBytes, readCts.Token).ConfigureAwait(False)
                    End Using
                    Dim reply As ExchangeLocalResult = Await DispatchAsync(line, handlerCts.Token).ConfigureAwait(False)
                    Await ExchangeAgentPipe.WriteLineAsync(pipe, ExchangeAgentPipe.BuildReply(reply.Ok, reply.Error, reply.DataJson), handlerCts.Token).ConfigureAwait(False)
                    pipe.WaitForPipeDrain()
                Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is InvalidDataException OrElse TypeOf ex Is ObjectDisposedException
                    ' a client that left, spoke too long or too slowly: nothing more is owed
                End Try
            End Using
        End Using
    End Function

    Private Async Function DispatchAsync(line As String, ct As CancellationToken) As Task(Of ExchangeLocalResult)
        If String.IsNullOrWhiteSpace(line) Then Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorBadRequest)
        Try
            Using doc As JsonDocument = JsonDocument.Parse(line)
                Dim root As JsonElement = doc.RootElement
                Dim typeEl As JsonElement = Nothing
                If root.ValueKind <> JsonValueKind.Object OrElse Not root.TryGetProperty("type", typeEl) OrElse typeEl.ValueKind <> JsonValueKind.String Then
                    Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorBadRequest)
                End If
                Return Await _handler.HandleAsync(typeEl.GetString(), root, ct).ConfigureAwait(False)
            End Using
        Catch ex As JsonException
            Return ExchangeLocalResult.Failure(ExchangeAgentPipe.ErrorBadRequest)
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        If _disposed Then Return
        _disposed = True
        Try : _cts.Cancel() : Catch : End Try
        Try : _first?.Dispose() : Catch : End Try
        Try
            If _loop IsNot Nothing Then _loop.Wait(1000)
        Catch
        End Try
        Try : _cts.Dispose() : Catch : End Try
    End Sub

End Class
