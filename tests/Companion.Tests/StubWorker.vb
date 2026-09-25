Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Pipes
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' A worker that answers on a pipe of its OWN, for tests that need to see what the host does
''' with a particular answer. Never the product's frozen <c>fms-companion</c>: that pipe is a
''' machine-wide singleton, so on a machine where sharing runs the real worker would answer
''' instead (see WorkerIpcTimeoutTests).
'''
''' Speaks the transport of WORKER-IPC section 2: one request per connection, the answer
''' written back, then the server closes. <paramref name="answer"/> maps the request's
''' <c>type</c> to the exact response text.
''' </summary>
Friend NotInheritable Class StubWorker
    Implements IDisposable

    Public ReadOnly Property PipeName As String

    Private ReadOnly _answer As Func(Of String, String)
    Private ReadOnly _cts As New CancellationTokenSource()
    Private ReadOnly _loop As Task
    Private ReadOnly _seen As New List(Of String)()
    Private ReadOnly _requests As New List(Of String)()

    Public Sub New(answer As Func(Of String, String))
        _answer = answer
        PipeName = "fms-companion-test-stub-" & Guid.NewGuid().ToString("N")
        _loop = Task.Run(Function() ServeAsync(_cts.Token))
    End Sub

    ''' <summary>The request types received so far, in order.</summary>
    Public Function Seen() As List(Of String)
        SyncLock _seen
            Return New List(Of String)(_seen)
        End SyncLock
    End Function

    ''' <summary>The raw request texts received so far, in the same order as <see cref="Seen"/>.</summary>
    Public Function SeenRequests() As List(Of String)
        SyncLock _seen
            Return New List(Of String)(_requests)
        End SyncLock
    End Function

    Private Async Function ServeAsync(ct As CancellationToken) As Task
        Do While Not ct.IsCancellationRequested
            Try
                Using server As New NamedPipeServerStream(PipeName, PipeDirection.InOut,
                                                          NamedPipeServerStream.MaxAllowedServerInstances,
                                                          PipeTransmissionMode.Byte, PipeOptions.Asynchronous)
                    Await server.WaitForConnectionAsync(ct).ConfigureAwait(False)
                    Dim request As String = Await ReadOneRequestAsync(server, ct).ConfigureAwait(False)
                    Dim type As String = ""
                    Using doc As JsonDocument = JsonDocument.Parse(request)
                        Dim el As JsonElement
                        If doc.RootElement.TryGetProperty("type", el) Then type = el.GetString()
                    End Using
                    SyncLock _seen
                        _seen.Add(type)
                        _requests.Add(request)
                    End SyncLock
                    Dim reply As Byte() = Encoding.UTF8.GetBytes(_answer(type))
                    Await server.WriteAsync(reply, 0, reply.Length, ct).ConfigureAwait(False)
                    Await server.FlushAsync(ct).ConfigureAwait(False)
                    ' Closing the handle (not Disconnect, which discards unread data) is the
                    ' "server closes after replying" the client reads to.
                    Try
                        server.WaitForPipeDrain()
                    Catch
                    End Try
                End Using
            Catch When ct.IsCancellationRequested
                Return
            Catch
                ' A client that gave up mid-exchange; take the next connection.
            End Try
        Loop
    End Function

    ''' <summary>The client sends no length and does not close its end, so a request is
    ''' complete when the bytes so far parse as one JSON object.</summary>
    Private Shared Async Function ReadOneRequestAsync(pipe As Stream, ct As CancellationToken) As Task(Of String)
        Dim buffer(8191) As Byte
        Using ms As New MemoryStream()
            Do
                Dim n As Integer = Await pipe.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(False)
                If n <= 0 Then Exit Do
                ms.Write(buffer, 0, n)
                Dim text As String = Encoding.UTF8.GetString(ms.ToArray())
                Try
                    Using JsonDocument.Parse(text)
                    End Using
                    Return text
                Catch ex As JsonException
                    ' Not all of it yet.
                End Try
            Loop
            Return Encoding.UTF8.GetString(ms.ToArray())
        End Using
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _cts.Cancel()
        Try
            _loop.Wait(TimeSpan.FromSeconds(5))
        Catch
        End Try
        _cts.Dispose()
    End Sub

End Class
