#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Net.Security
Imports System.Net.Sockets
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 170 rule 6. The byte pump of a tunnel: two streams, bytes both ways, bounded buffers, half-close honoured, an idle
' timeout. The producer's agent splices the data stream it attached to the exchange with a loopback connection to the share
' worker's SFTP listener; SSH stays end to end through it (the credentials, the host-key pin and the folder confinement are the
' direct path's), so this class interprets no byte.
'
'   - one buffer of 16 KiB per direction, so a slow reader slows the writer by TCP backpressure and memory stays bounded;
'   - when one side stops sending (a read returns 0) the other side is told so by a half-close - Shutdown(Send) on a socket,
'     close_notify on a TLS stream - and the opposite direction keeps flowing until it ends too;
'   - when neither direction moved a byte for <see cref="ExchangeBridgeOptions.IdleTimeout"/> the bridge ends;
'   - nothing here logs, prints or keeps a byte.

Friend NotInheritable Class ExchangeBridgeOptions

    ''' <summary>10 minutes (DEVICE-EXCHANGE 14.5 item L, the server's own bridge number).</summary>
    Public Shared ReadOnly DefaultIdleTimeout As TimeSpan = TimeSpan.FromMinutes(10)

    Public Property IdleTimeout As TimeSpan = DefaultIdleTimeout
    Public Property BufferSize As Integer = 16 * 1024

End Class

Friend Enum ExchangeBridgeEnd
    ''' <summary>Both directions ended by their peers (each a clean half-close or a close).</summary>
    Completed = 0
    ''' <summary>No byte moved either way for the idle timeout.</summary>
    Idle = 1
    ''' <summary>The caller's token ended it.</summary>
    Cancelled = 2
    ''' <summary>A stream failed (reset, closed under it).</summary>
    Faulted = 3
End Enum

Friend NotInheritable Class ExchangeBridgeResult
    Public ReadOnly Ended As ExchangeBridgeEnd
    ''' <summary>Bytes copied from the left stream to the right one.</summary>
    Public ReadOnly LeftToRight As Long
    ''' <summary>Bytes copied from the right stream to the left one.</summary>
    Public ReadOnly RightToLeft As Long

    Public Sub New(ended As ExchangeBridgeEnd, leftToRight As Long, rightToLeft As Long)
        Me.Ended = ended
        Me.LeftToRight = leftToRight
        Me.RightToLeft = rightToLeft
    End Sub
End Class

Friend NotInheritable Class ExchangeBridge

    Private Sub New()
    End Sub

    ''' <summary>Pumps until both directions have ended, the idle timeout passes, or the token is cancelled. Neither stream is
    ''' disposed here: the caller owns both.</summary>
    Public Shared Async Function RunAsync(left As Stream, right As Stream, options As ExchangeBridgeOptions, ct As CancellationToken) As Task(Of ExchangeBridgeResult)
        Dim opt As ExchangeBridgeOptions = If(options, New ExchangeBridgeOptions())
        Dim state As New PumpState()
        Using idleCts As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
            Dim watcher As Task = WatchIdleAsync(state, opt.IdleTimeout, idleCts)
            Dim up As Task = PumpAsync(left, right, opt.BufferSize, state, True, idleCts.Token)
            Dim down As Task = PumpAsync(right, left, opt.BufferSize, state, False, idleCts.Token)
            Try
                Await Task.WhenAll(up, down).ConfigureAwait(False)
            Catch
                ' a pump records its own failure in the state
            End Try
            idleCts.Cancel()
            Try
                Await watcher.ConfigureAwait(False)
            Catch
            End Try
        End Using
        Dim ended As ExchangeBridgeEnd = ExchangeBridgeEnd.Completed
        If state.Faulted Then
            ended = ExchangeBridgeEnd.Faulted
        ElseIf state.TimedOut Then
            ended = ExchangeBridgeEnd.Idle
        ElseIf ct.IsCancellationRequested Then
            ended = ExchangeBridgeEnd.Cancelled
        End If
        Return New ExchangeBridgeResult(ended, state.UpBytes, state.DownBytes)
    End Function

    Private NotInheritable Class PumpState
        Public LastActivity As Long = Environment.TickCount64
        Public UpBytes As Long
        Public DownBytes As Long
        Public Faulted As Boolean
        Public TimedOut As Boolean
    End Class

    Private Shared Async Function WatchIdleAsync(state As PumpState, idle As TimeSpan, cts As CancellationTokenSource) As Task
        If idle <= TimeSpan.Zero Then Return
        Dim tick As Integer = CInt(Math.Max(10, Math.Min(1000, idle.TotalMilliseconds / 4)))
        Try
            While Not cts.IsCancellationRequested
                Await Task.Delay(tick, cts.Token).ConfigureAwait(False)
                If Environment.TickCount64 - Volatile.Read(state.LastActivity) >= CLng(idle.TotalMilliseconds) Then
                    state.TimedOut = True
                    cts.Cancel()
                    Return
                End If
            End While
        Catch ex As OperationCanceledException
        End Try
    End Function

    Private Shared Async Function PumpAsync(source As Stream, target As Stream, bufferSize As Integer, state As PumpState, isUp As Boolean, ct As CancellationToken) As Task
        Dim buffer(Math.Max(256, bufferSize) - 1) As Byte
        Try
            While Not ct.IsCancellationRequested
                Dim n As Integer = Await source.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(False)
                If n = 0 Then
                    ' the source stopped sending: tell the target, and leave the opposite direction running
                    Await HalfCloseAsync(target).ConfigureAwait(False)
                    Return
                End If
                Await target.WriteAsync(buffer, 0, n, ct).ConfigureAwait(False)
                Await target.FlushAsync(ct).ConfigureAwait(False)
                Volatile.Write(state.LastActivity, Environment.TickCount64)
                If isUp Then Interlocked.Add(state.UpBytes, n) Else Interlocked.Add(state.DownBytes, n)
            End While
        Catch ex As OperationCanceledException
            ' idle, or the caller: the state says which
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ObjectDisposedException OrElse TypeOf ex Is SocketException OrElse TypeOf ex Is InvalidOperationException
            If Not ct.IsCancellationRequested Then state.Faulted = True
        End Try
    End Function

    ''' <summary>A half-close of a stream's sending side: Shutdown(Send) on a socket, close_notify on TLS. A stream that has no such
    ''' thing is left alone (the other direction closing it later is what ends the conversation).</summary>
    Public Shared Async Function HalfCloseAsync(stream As Stream) As Task
        Try
            Dim ssl As SslStream = TryCast(stream, SslStream)
            If ssl IsNot Nothing Then
                Await ssl.ShutdownAsync().ConfigureAwait(False)
                Return
            End If
            Dim net As NetworkStream = TryCast(stream, NetworkStream)
            If net IsNot Nothing Then
                net.Socket.Shutdown(SocketShutdown.Send)
                Return
            End If
            Dim half As IExchangeHalfClose = TryCast(stream, IExchangeHalfClose)
            If half IsNot Nothing Then half.ShutdownSend()
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ObjectDisposedException OrElse TypeOf ex Is SocketException OrElse TypeOf ex Is InvalidOperationException
            ' the other side is already gone: nothing to tell
        End Try
    End Function

End Class

''' <summary>A stream that can say "no more from me" without closing (a test stream; the real ones are sockets and TLS).</summary>
Friend Interface IExchangeHalfClose
    Sub ShutdownSend()
End Interface
#End If
