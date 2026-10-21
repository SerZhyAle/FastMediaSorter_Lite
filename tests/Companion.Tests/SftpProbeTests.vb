Option Strict On

Imports System.Collections.Concurrent
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 244, SHARE-SESSION 1.3 (iii): the Share Manager's address test reads an end of stream BEFORE the SSH banner as an
''' undetermined transport ("the server closed the connection before it answered"), never as "not an SSH server" - a server at its
''' connection limit does exactly this. A peer that really answers with something else (an HTTP reply) is still "not SSH". Every case runs
''' against a real loopback listener, so the verdict is the one a person gets.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class SftpProbeTests

    ''' <summary>A loopback listener whose behaviour on an accepted connection is the delegate the test hands in.</summary>
    Private NotInheritable Class ScriptedListener
        Implements IDisposable

        Private ReadOnly _listener As TcpListener
        Private ReadOnly _serve As Func(Of TcpClient, Task)
        Private ReadOnly _stop As New CancellationTokenSource()
        Private ReadOnly _held As New ConcurrentBag(Of TcpClient)()
        Private ReadOnly _acceptLoop As Task

        Friend ReadOnly Port As Integer

        Friend Sub New(serve As Func(Of TcpClient, Task))
            _serve = serve
            _listener = New TcpListener(IPAddress.Loopback, 0)
            _listener.Start()
            Port = DirectCast(_listener.LocalEndpoint, IPEndPoint).Port
            _acceptLoop = Task.Run(AddressOf AcceptLoop)
        End Sub

        Private Async Function AcceptLoop() As Task
            Try
                While Not _stop.IsCancellationRequested
                    Dim client As TcpClient = Await _listener.AcceptTcpClientAsync(_stop.Token)
                    _held.Add(client)
                    Dim ignored As Task = _serve(client)
                End While
            Catch ex As Exception
                ' the listener was stopped under the loop: nothing left to accept
            End Try
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            _stop.Cancel()
            Try
                _listener.Stop()
            Catch ex As Exception
            End Try
            For Each client As TcpClient In _held
                Try
                    client.Close()
                Catch ex As Exception
                End Try
            Next
        End Sub
    End Class

    Private Shared Function Probe(port As Integer, Optional timeoutMs As Integer = 3000) As SftpProbe.ProbeResult
        Return SftpProbe.ProbeAsync("127.0.0.1", port, timeoutMs).GetAwaiter().GetResult()
    End Function

    Private Shared Function Pending() As Task
        Return Task.CompletedTask
    End Function

    ' ---------------------------------------------------------------- the verdicts

    <Fact>
    Public Sub A_peer_that_accepts_and_closes_before_any_byte_is_closed_before_the_banner_and_not_a_non_ssh_server()
        Using server As New ScriptedListener(Function(c As TcpClient) As Task
                                                 c.Close()
                                                 Return Pending()
                                             End Function)
            Dim verdict As SftpProbe.ProbeResult = Probe(server.Port)
            Assert.Equal(SftpProbe.ProbeResult.ClosedBeforeBanner, verdict)
            ' the whole point of 1.3 (iii): this must not read as the answer for "something else holds the port"
            Assert.NotEqual(SftpProbe.ProbeResult.PortOpen, verdict)
        End Using
    End Sub

    <Fact>
    Public Sub A_peer_that_resets_the_connection_before_any_byte_is_also_closed_before_the_banner()
        Using server As New ScriptedListener(Function(c As TcpClient) As Task
                                                 c.LingerState = New LingerOption(True, 0)   ' close sends a reset, not a FIN
                                                 c.Close()
                                                 Return Pending()
                                             End Function)
            Assert.Equal(SftpProbe.ProbeResult.ClosedBeforeBanner, Probe(server.Port))
        End Using
    End Sub

    <Fact>
    Public Sub A_peer_that_answers_with_an_http_reply_is_still_not_ssh()
        Using server As New ScriptedListener(Async Function(c As TcpClient) As Task
                                                 Dim reply As Byte() = Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request" & vbCrLf & vbCrLf)
                                                 Await c.GetStream().WriteAsync(reply, 0, reply.Length)
                                                 c.Close()
                                             End Function)
            Assert.Equal(SftpProbe.ProbeResult.PortOpen, Probe(server.Port))
        End Using
    End Sub

    <Fact>
    Public Sub A_peer_that_sends_an_ssh_banner_is_ssh_ok()
        Using server As New ScriptedListener(Async Function(c As TcpClient) As Task
                                                 Dim banner As Byte() = Encoding.ASCII.GetBytes("SSH-2.0-test" & vbCrLf)
                                                 Await c.GetStream().WriteAsync(banner, 0, banner.Length)
                                             End Function)
            Assert.Equal(SftpProbe.ProbeResult.SshOk, Probe(server.Port))
        End Using
    End Sub

    <Fact>
    Public Sub A_peer_that_accepts_and_stays_silent_is_an_open_port_and_not_closed_before_the_banner()
        Using server As New ScriptedListener(Function(c As TcpClient) As Task
                                                 Return Pending()   ' held open by the fixture, never speaks
                                             End Function)
            Assert.Equal(SftpProbe.ProbeResult.PortOpen, Probe(server.Port, 1000))
        End Using
    End Sub

    <Fact>
    Public Sub A_port_nobody_listens_on_is_refused()
        Dim port As Integer
        Using probeListener As New ScriptedListener(Function(c As TcpClient) As Task
                                                        Return Pending()
                                                    End Function)
            port = probeListener.Port
        End Using
        Assert.Equal(SftpProbe.ProbeResult.Refused, Probe(port))
    End Sub

    <Theory>
    <InlineData("", 22)>
    <InlineData("  ", 22)>
    <InlineData("127.0.0.1", 0)>
    <InlineData("127.0.0.1", 70000)>
    Public Sub An_empty_host_or_a_port_out_of_range_is_bad_input(host As String, port As Integer)
        Assert.Equal(SftpProbe.ProbeResult.BadInput, SftpProbe.ProbeAsync(host, port, 500).GetAwaiter().GetResult())
    End Sub

    <Fact>
    Public Sub The_new_value_is_appended_after_the_earlier_ones_and_equals_the_viewers_value()
        Assert.Equal(0, CInt(SftpProbe.ProbeResult.SshOk))
        Assert.Equal(1, CInt(SftpProbe.ProbeResult.PortOpen))
        Assert.Equal(2, CInt(SftpProbe.ProbeResult.Refused))
        Assert.Equal(3, CInt(SftpProbe.ProbeResult.Timeout))
        Assert.Equal(4, CInt(SftpProbe.ProbeResult.BadInput))
        Assert.Equal(5, CInt(SftpProbe.ProbeResult.ClosedBeforeBanner))
    End Sub

    ' ---------------------------------------------------------------- the words

    Private Const ClosedBeforeBannerEnglish As String = "The server closed the connection before it answered. It may be at its connection limit; try again in a moment."

    Private Shared Sub InLanguage(code As String, body As Action)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            body()
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub Closed_before_the_banner_has_its_own_sentence_that_never_calls_the_peer_not_ssh()
        InLanguage("en", Sub()
                             Dim line As String = ShareText.AccessTestResultLine(SftpProbe.ProbeResult.ClosedBeforeBanner, "pc:22")
                             Assert.Equal(ClosedBeforeBannerEnglish, line)
                             Assert.NotEqual(ShareText.AccessTestResultLine(SftpProbe.ProbeResult.PortOpen, "pc:22"), line)
                             Assert.NotEqual(ShareText.AccessTestResultLine(SftpProbe.ProbeResult.Refused, "pc:22"), line)
                             Assert.NotEqual(ShareText.AccessTestResultLine(CType(99, SftpProbe.ProbeResult), "pc:22"), line)
                             ' the detail is empty rather than the "address not detected yet" text of the catch-all
                             Assert.Equal("", ShareText.AccessTestDetail(SftpProbe.ProbeResult.ClosedBeforeBanner))
                         End Sub)
    End Sub

    <Fact>
    Public Sub Closed_before_the_banner_is_a_distinct_sentence_in_every_one_of_the_13_languages()
        Dim russian As String = "Сервер закрыл соединение, не успев ответить. Возможно, достигнут предел подключений; повторите через минуту."
        For Each code As String In Localization.Codes
            Dim languageCode As String = code
            InLanguage(languageCode, Sub()
                                         Dim line As String = ShareText.AccessTestResultLine(SftpProbe.ProbeResult.ClosedBeforeBanner, "pc:22")
                                         Assert.False(String.IsNullOrWhiteSpace(line), languageCode)
                                         Assert.NotEqual(ShareText.AccessTestResultLine(SftpProbe.ProbeResult.PortOpen, "pc:22"), line)
                                         ' a row that is missing falls back to the Russian source: only Russian itself may read that
                                         If languageCode <> "ru" Then Assert.NotEqual(russian, line)
                                     End Sub)
        Next
    End Sub

End Class
