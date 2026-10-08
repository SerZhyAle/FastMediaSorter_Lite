#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Ticket 180 T-1: the loopback forwarder of DEVICE-EXCHANGE 7.7. The rule: ONE `connect` per accepted connection - never zero, never
''' two - and a failed open closes that connection and nothing else. The opener is a fake, so no server is involved.
''' </summary>
Public Class ExchangeForwarderTests

    ''' <summary>An opener that counts its calls and answers each with the next scripted behaviour.</summary>
    Private NotInheritable Class CountingOpener
        Implements IExchangeDataOpener

        Public Calls As Integer
        Public ReadOnly Services As New List(Of Stream)
        Public Behaviour As Func(Of Integer, Task(Of ExchangeOpenResult))

        Public Async Function OpenAsync(ct As CancellationToken) As Task(Of ExchangeOpenResult) Implements IExchangeDataOpener.OpenAsync
            Dim n As Integer = Interlocked.Increment(Calls)
            Return Await Behaviour(n)
        End Function
    End Class

    ''' <summary>A connected result whose service end is handed back for the test to serve (an echo).</summary>
    Private Shared Async Function ConnectedToEchoAsync(opener As CountingOpener) As Task(Of ExchangeOpenResult)
        Dim pair As Tuple(Of Stream, Stream) = Await ExchangeWireTestKit.LoopbackPairAsync()
        SyncLock opener.Services
            opener.Services.Add(pair.Item2)
        End SyncLock
        Dim t As Task = Task.Run(Async Function()
                                     Dim buf(1023) As Byte
                                     Try
                                         While True
                                             Dim n As Integer = Await pair.Item2.ReadAsync(buf, 0, buf.Length)
                                             If n = 0 Then Exit While
                                             Await pair.Item2.WriteAsync(buf, 0, n)
                                             Await pair.Item2.FlushAsync()
                                         End While
                                     Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ObjectDisposedException
                                     End Try
                                 End Function)
        Return New ExchangeOpenResult(ExchangeOpenOutcome.Connected, pair.Item1, Nothing, Nothing, Nothing, ExchangeRefusal.Unknown, "")
    End Function

    Private Shared Async Function ClientRoundTripAsync(port As Integer, text As String) As Task(Of String)
        Using client As New TcpClient()
            Await client.ConnectAsync(IPAddress.Loopback, port)
            Dim s As NetworkStream = client.GetStream()
            Dim data As Byte() = Encoding.ASCII.GetBytes(text)
            Await s.WriteAsync(data, 0, data.Length)
            Await s.FlushAsync()
            Dim buf(255) As Byte
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts(10)
                Dim n As Integer = Await s.ReadAsync(buf, 0, buf.Length, cts.Token)
                Return Encoding.ASCII.GetString(buf, 0, n)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Every_accepted_connection_becomes_exactly_one_connect_and_the_bytes_are_bridged_both_ways() As Task
        Dim opener As New CountingOpener()
        opener.Behaviour = Function(n) ConnectedToEchoAsync(opener)
        Using fwd As New ExchangeForwarder(opener)
            fwd.Start()
            Assert.True(fwd.Port > 0)
            Assert.Equal(0, opener.Calls)   ' nothing until somebody connects

            Dim a As String = Await ClientRoundTripAsync(fwd.Port, "first")
            Dim b As String = Await ClientRoundTripAsync(fwd.Port, "second")
            Dim c As String = Await ClientRoundTripAsync(fwd.Port, "third")
            Assert.Equal("first", a)
            Assert.Equal("second", b)
            Assert.Equal("third", c)
            Assert.Equal(3, opener.Calls)
            Assert.Equal(3, fwd.ConnectsStarted)
        End Using
    End Function

    <Fact>
    Public Async Function Several_simultaneous_connections_each_get_their_own_connect() As Task
        Dim opener As New CountingOpener()
        opener.Behaviour = Function(n) ConnectedToEchoAsync(opener)
        Using fwd As New ExchangeForwarder(opener)
            fwd.Start()
            Dim tasks As New List(Of Task(Of String))
            For i As Integer = 0 To 7
                tasks.Add(ClientRoundTripAsync(fwd.Port, "msg" & i.ToString()))
            Next
            Dim results As String() = Await Task.WhenAll(tasks)
            For i As Integer = 0 To 7
                Assert.Equal("msg" & i.ToString(), results(i))
            Next
            Assert.Equal(8, opener.Calls)
        End Using
    End Function

    <Fact>
    Public Async Function A_refused_open_closes_that_one_connection_and_the_next_still_works() As Task
        Dim opener As New CountingOpener()
        opener.Behaviour = Function(n)
                               If n = 1 Then Return Task.FromResult(ExchangeOpenResult.Failed(ExchangeOpenOutcome.Refused, ExchangeRefusal.Unavailable))
                               Return ConnectedToEchoAsync(opener)
                           End Function
        Using fwd As New ExchangeForwarder(opener)
            fwd.Start()
            ' the first connection is closed by the forwarder: a read ends at once with no data
            Using client As New TcpClient()
                Await client.ConnectAsync(IPAddress.Loopback, fwd.Port)
                Dim buf(15) As Byte
                Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts(10)
                    Dim n As Integer
                    Try
                        n = Await client.GetStream().ReadAsync(buf, 0, buf.Length, cts.Token)
                    Catch ex As IOException
                        n = 0
                    End Try
                    Assert.Equal(0, n)
                End Using
            End Using
            Assert.Equal("again", Await ClientRoundTripAsync(fwd.Port, "again"))
            Assert.Equal(2, opener.Calls)   ' one per connection, the failed one was not retried
        End Using
    End Function

    <Fact>
    Public Async Function An_opener_that_throws_does_not_take_the_forwarder_down() As Task
        Dim opener As New CountingOpener()
        opener.Behaviour = Function(n)
                               If n = 1 Then Throw New IOException("boom")
                               Return ConnectedToEchoAsync(opener)
                           End Function
        Using fwd As New ExchangeForwarder(opener)
            fwd.Start()
            Using client As New TcpClient()
                Await client.ConnectAsync(IPAddress.Loopback, fwd.Port)
                Dim buf(15) As Byte
                Dim n As Integer
                Try
                    n = Await client.GetStream().ReadAsync(buf, 0, buf.Length)
                Catch ex As IOException
                    n = 0
                End Try
                Assert.Equal(0, n)
            End Using
            Assert.Equal("ok", Await ClientRoundTripAsync(fwd.Port, "ok"))
        End Using
    End Function

    <Fact>
    Public Sub It_listens_on_the_loopback_address_only()
        Dim opener As New CountingOpener()
        opener.Behaviour = Function(n) Task.FromResult(ExchangeOpenResult.Failed(ExchangeOpenOutcome.Network))
        Using fwd As New ExchangeForwarder(opener)
            fwd.Start()
            Dim endpoints As IPEndPoint() = Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
            Dim mine As IPEndPoint = Array.Find(endpoints, Function(e) e.Port = fwd.Port)
            Assert.NotNull(mine)
            Assert.True(IPAddress.IsLoopback(mine.Address), mine.Address.ToString())
        End Using
    End Sub

    <Fact>
    Public Sub It_is_not_started_by_construction()
        Dim opener As New CountingOpener()
        opener.Behaviour = Function(n) Task.FromResult(ExchangeOpenResult.Failed(ExchangeOpenOutcome.Network))
        Using fwd As New ExchangeForwarder(opener)
            Assert.Equal(0, fwd.Port)
            Assert.Equal(0, fwd.ConnectsStarted)
        End Using
    End Sub

End Class
#End If
