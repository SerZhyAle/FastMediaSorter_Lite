#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>What a reachability probe of an SFTP address found.</summary>
Friend Enum ProbeVerdict
    ''' <summary>TCP connected and the peer introduced itself as SSH - our kind of server answered.</summary>
    SshOk = 0
    ''' <summary>Something accepted the connection but did not say SSH within the wait.</summary>
    PortOpen = 1
    ''' <summary>Refused or unreachable: nothing listens there, or the route is gone.</summary>
    Refused = 2
    ''' <summary>No answer inside the budget.</summary>
    Timeout = 3
    ''' <summary>An empty host or a port outside 1..65535: nothing to ask.</summary>
    BadInput = 4
End Enum

''' <summary>
''' The manager's "Test" (007 §3.2): a TCP connection and the SSH identification line (RFC 4253 §4.2), no
''' login and no host key. It answers one question - did a server of our kind answer at this address - and
''' deliberately nothing about credentials, so pressing it can never count against a lockout. Never throws;
''' every failure is a verdict.
''' </summary>
Friend Module ResourceProbe

    Friend Async Function ProbeSftpAsync(host As String, port As Integer, ct As CancellationToken,
                                         Optional timeoutMs As Integer = 4000) As Task(Of ProbeVerdict)
        If String.IsNullOrWhiteSpace(host) OrElse port < 1 OrElse port > 65535 Then Return ProbeVerdict.BadInput
        Using client As New TcpClient()
            Try
                Dim connect As Task = client.ConnectAsync(host.Trim(), port)
                Dim finished As Task = Await Task.WhenAny(connect, Task.Delay(timeoutMs, ct)).ConfigureAwait(False)
                If ct.IsCancellationRequested Then Return ProbeVerdict.Timeout
                If finished IsNot connect Then
                    ObserveLater(connect)
                    Return ProbeVerdict.Timeout
                End If
                Await connect.ConfigureAwait(False)
                If Not client.Connected Then Return ProbeVerdict.Refused

                ' An SSH server speaks first, so a short read is all the evidence there is to want.
                Dim buffer(255) As Byte
                Dim read As Task(Of Integer) = client.GetStream().ReadAsync(buffer, 0, buffer.Length, ct)
                Dim readDone As Task = Await Task.WhenAny(read, Task.Delay(Math.Max(600, timeoutMs \ 2), ct)).ConfigureAwait(False)
                If readDone IsNot read Then
                    ObserveLater(read)
                    Return ProbeVerdict.PortOpen
                End If
                Dim count As Integer = Await read.ConfigureAwait(False)
                If count > 0 AndAlso Encoding.ASCII.GetString(buffer, 0, count).StartsWith("SSH-", StringComparison.Ordinal) Then
                    Return ProbeVerdict.SshOk
                End If
                Return ProbeVerdict.PortOpen
            Catch ex As OperationCanceledException
                Return ProbeVerdict.Timeout
            Catch ex As SocketException
                Return If(ex.SocketErrorCode = SocketError.TimedOut, ProbeVerdict.Timeout, ProbeVerdict.Refused)
            Catch ex As Exception
                Return ProbeVerdict.Refused
            End Try
        End Using
    End Function

    ''' <summary>An abandoned connect or read faults when the socket is closed under it; observing it keeps
    ''' that out of the unobserved-exception path.</summary>
    Private Sub ObserveLater(task As Task)
        task.ContinueWith(Sub(t As Task)
                              Dim ignored As AggregateException = t.Exception
                          End Sub, TaskScheduler.Default)
    End Sub

    Friend Function VerdictSentence(verdict As ProbeVerdict) As String
        Select Case verdict
            Case ProbeVerdict.SshOk
                Return Localization.T("Сервер отвечает")
            Case ProbeVerdict.PortOpen
                Return Localization.T("Порт открыт, но это не SSH-сервер")
            Case ProbeVerdict.Refused
                Return Localization.T("Сервер недоступен: подключение отклонено")
            Case ProbeVerdict.Timeout
                Return Localization.T("Сервер не ответил вовремя")
            Case Else
                Return Localization.T("Проверять нечего: не указан адрес сервера")
        End Select
    End Function

End Module
#End If
