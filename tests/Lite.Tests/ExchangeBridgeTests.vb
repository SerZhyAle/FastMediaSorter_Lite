#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Net.Sockets
Imports System.Security.Cryptography
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Ticket 170 rule 6: the byte pump of a tunnel. Two loopback socket pairs stand for "the consumer's side of the exchange" and "the worker's
''' side": bytes go both ways exactly, a half-close of one side reaches the other while the opposite direction keeps flowing, and a bridge
''' that moves nothing ends at its idle timeout. Every socket is on 127.0.0.1.
''' </summary>
Public Class ExchangeBridgeTests

    Private NotInheritable Class Rig
        Implements IDisposable

        Public ReadOnly PeerLeft As Stream
        Public ReadOnly BridgeLeft As Stream
        Public ReadOnly PeerRight As Stream
        Public ReadOnly BridgeRight As Stream

        Private Sub New(a As Tuple(Of Stream, Stream), b As Tuple(Of Stream, Stream))
            PeerLeft = a.Item1
            BridgeLeft = a.Item2
            PeerRight = b.Item1
            BridgeRight = b.Item2
        End Sub

        Public Shared Async Function CreateAsync() As Task(Of Rig)
            Dim a As Tuple(Of Stream, Stream) = Await ExchangeWireTestKit.LoopbackPairAsync()
            Dim b As Tuple(Of Stream, Stream) = Await ExchangeWireTestKit.LoopbackPairAsync()
            Return New Rig(a, b)
        End Function

        Public Sub ShutdownSend(peer As Stream)
            DirectCast(peer, NetworkStream).Socket.Shutdown(SocketShutdown.Send)
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            For Each s As Stream In {PeerLeft, BridgeLeft, PeerRight, BridgeRight}
                Try : s.Dispose() : Catch : End Try
            Next
        End Sub
    End Class

    Private Shared Async Function ReadAllAsync(s As Stream, count As Integer) As Task(Of Byte())
        Dim buf(count - 1) As Byte
        Dim got As Integer = 0
        Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(20))
            While got < count
                Dim n As Integer = Await s.ReadAsync(buf, got, count - got, cts.Token)
                If n = 0 Then Throw New EndOfStreamException()
                got += n
            End While
        End Using
        Return buf
    End Function

    <Fact>
    Public Async Function Bytes_go_both_ways_exactly() As Task
        Using rig As Rig = Await Rig.CreateAsync()
            Dim bridge As Task(Of ExchangeBridgeResult) = ExchangeBridge.RunAsync(rig.BridgeLeft, rig.BridgeRight, Nothing, CancellationToken.None)
            Dim up(200 * 1024 - 1) As Byte
            Dim down(150 * 1024 - 1) As Byte
            RandomNumberGenerator.Fill(up)
            RandomNumberGenerator.Fill(down)

            Dim readUp As Task(Of Byte()) = ReadAllAsync(rig.PeerRight, up.Length)
            Dim readDown As Task(Of Byte()) = ReadAllAsync(rig.PeerLeft, down.Length)
            Await rig.PeerLeft.WriteAsync(up, 0, up.Length)
            Await rig.PeerRight.WriteAsync(down, 0, down.Length)
            Assert.Equal(up, Await readUp)
            Assert.Equal(down, Await readDown)

            rig.PeerLeft.Dispose()
            rig.PeerRight.Dispose()
            Dim result As ExchangeBridgeResult = Await bridge
            Assert.Equal(up.Length, result.LeftToRight)
            Assert.Equal(down.Length, result.RightToLeft)
        End Using
    End Function

    <Fact>
    Public Async Function A_half_close_reaches_the_other_side_while_the_opposite_direction_keeps_flowing() As Task
        Using rig As Rig = Await Rig.CreateAsync()
            Dim bridge As Task(Of ExchangeBridgeResult) = ExchangeBridge.RunAsync(rig.BridgeLeft, rig.BridgeRight, Nothing, CancellationToken.None)

            ' the left peer sends a request and says "no more from me"
            Dim request As Byte() = {1, 2, 3, 4, 5}
            Await rig.PeerLeft.WriteAsync(request, 0, request.Length)
            Await rig.PeerLeft.FlushAsync()
            rig.ShutdownSend(rig.PeerLeft)

            Assert.Equal(request, Await ReadAllAsync(rig.PeerRight, request.Length))
            ' ... the right peer SEES the end of the stream (the half-close was passed on)
            Dim one(0) As Byte
            Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(10))
                Assert.Equal(0, Await rig.PeerRight.ReadAsync(one, 0, 1, cts.Token))
            End Using

            ' ... and can still answer: the opposite direction is open
            Dim answer As Byte() = {9, 8, 7}
            Await rig.PeerRight.WriteAsync(answer, 0, answer.Length)
            Await rig.PeerRight.FlushAsync()
            Assert.Equal(answer, Await ReadAllAsync(rig.PeerLeft, answer.Length))

            rig.ShutdownSend(rig.PeerRight)
            Dim result As ExchangeBridgeResult = Await bridge
            Assert.Equal(ExchangeBridgeEnd.Completed, result.Ended)
            Assert.Equal(request.Length, result.LeftToRight)
            Assert.Equal(answer.Length, result.RightToLeft)
        End Using
    End Function

    <Fact>
    Public Async Function A_bridge_that_moves_nothing_ends_at_its_idle_timeout() As Task
        Using rig As Rig = Await Rig.CreateAsync()
            Dim started As Long = Environment.TickCount64
            Dim result As ExchangeBridgeResult = Await ExchangeBridge.RunAsync(rig.BridgeLeft, rig.BridgeRight,
                New ExchangeBridgeOptions With {.IdleTimeout = TimeSpan.FromMilliseconds(300)}, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15))
            Assert.Equal(ExchangeBridgeEnd.Idle, result.Ended)
            Assert.InRange(Environment.TickCount64 - started, 250, 8000)
        End Using
    End Function

    <Fact>
    Public Async Function Traffic_keeps_a_bridge_alive_past_the_idle_timeout() As Task
        Using rig As Rig = Await Rig.CreateAsync()
            Dim bridge As Task(Of ExchangeBridgeResult) = ExchangeBridge.RunAsync(rig.BridgeLeft, rig.BridgeRight,
                New ExchangeBridgeOptions With {.IdleTimeout = TimeSpan.FromMilliseconds(1500)}, CancellationToken.None) ' a timer stall of 1 s on a loaded test machine must not idle the bridge out
            Dim one As Byte() = {42}
            For i As Integer = 1 To 5 ' 5 x 500 ms of traffic is 2.5 s, past the 1.5 s idle timeout
                Await Task.Delay(500)
                Await rig.PeerLeft.WriteAsync(one, 0, 1)
                Await rig.PeerLeft.FlushAsync()
                Assert.Equal(one, Await ReadAllAsync(rig.PeerRight, 1))
            Next
            Assert.False(bridge.IsCompleted)
            rig.PeerLeft.Dispose()
            rig.PeerRight.Dispose()
            Dim result As ExchangeBridgeResult = Await bridge.WaitAsync(TimeSpan.FromSeconds(15))
            Assert.NotEqual(ExchangeBridgeEnd.Idle, result.Ended)
        End Using
    End Function

    <Fact>
    Public Async Function The_caller_can_cancel_a_bridge() As Task
        Using rig As Rig = Await Rig.CreateAsync()
            Using cts As New CancellationTokenSource()
                Dim bridge As Task(Of ExchangeBridgeResult) = ExchangeBridge.RunAsync(rig.BridgeLeft, rig.BridgeRight, Nothing, cts.Token)
                Await Task.Delay(100)
                cts.Cancel()
                Dim result As ExchangeBridgeResult = Await bridge.WaitAsync(TimeSpan.FromSeconds(15))
                Assert.Equal(ExchangeBridgeEnd.Cancelled, result.Ended)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_closed_side_ends_the_bridge_without_hanging() As Task
        Using rig As Rig = Await Rig.CreateAsync()
            Dim bridge As Task(Of ExchangeBridgeResult) = ExchangeBridge.RunAsync(rig.BridgeLeft, rig.BridgeRight, Nothing, CancellationToken.None)
            rig.PeerLeft.Dispose()
            rig.PeerRight.Dispose()
            Dim result As ExchangeBridgeResult = Await bridge.WaitAsync(TimeSpan.FromSeconds(15))
            Assert.True(result.Ended = ExchangeBridgeEnd.Completed OrElse result.Ended = ExchangeBridgeEnd.Faulted)
        End Using
    End Function

    <Fact>
    Public Sub The_buffer_is_bounded_and_the_options_default_to_ten_minutes()
        Assert.Equal(TimeSpan.FromMinutes(10), ExchangeBridgeOptions.DefaultIdleTimeout)
        Assert.Equal(16 * 1024, New ExchangeBridgeOptions().BufferSize)
    End Sub

End Class
#End If
