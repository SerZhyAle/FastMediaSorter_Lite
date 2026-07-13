Option Strict On

Imports System.Net.Sockets
Imports System.Text
Imports System.Threading.Tasks

''' <summary>
''' Lightweight reachability probe for the Share SFTP endpoint, driving the small
''' "Тест" / "Test" buttons next to the LAN and internet addresses on the Share
''' settings tab. Opens a TCP connection to host:port and reads the server's SSH
''' identification banner ("SSH-2.0-...") to confirm it is really our SFTP/SSH
''' server answering, not just any process holding the port open.
'''
''' Honesty note on the internet test: it runs from this same PC, so a router
''' without NAT loopback (hairpinning) can refuse the public address even when the
''' port forward is correct for real outside clients. A failed internet probe is
''' therefore reported by the caller as "could not confirm from here - test from
''' the phone on mobile data", not a hard "closed". The LAN probe has no such
''' caveat: a green result there is authoritative.
''' </summary>
Public Module SftpProbe

    ''' <summary>Outcome of a single probe.</summary>
    Public Enum ProbeResult
        ''' <summary>TCP connected AND an SSH banner was read - definitely our server.</summary>
        SshOk
        ''' <summary>TCP connected but no SSH banner arrived - port is open, but the
        ''' peer did not identify as SSH (something else, or it timed out reading).</summary>
        PortOpen
        ''' <summary>Connection actively refused / host unreachable.</summary>
        Refused
        ''' <summary>No answer within the timeout.</summary>
        Timeout
        ''' <summary>Empty host or an out-of-range port - nothing to probe.</summary>
        BadInput
    End Enum

    ''' <summary>
    ''' TCP-connects to <paramref name="host"/>:<paramref name="port"/> and tries to
    ''' read the SSH banner. Never throws - every failure maps to a ProbeResult.
    ''' Runs fully async so the UI thread stays responsive while it waits.
    ''' </summary>
    Public Async Function ProbeAsync(host As String, port As Integer,
                                     Optional timeoutMs As Integer = 4000) As Task(Of ProbeResult)
        If String.IsNullOrWhiteSpace(host) OrElse port <= 0 OrElse port > 65535 Then Return ProbeResult.BadInput

        Dim client As TcpClient = Nothing
        Try
            client = New TcpClient()
            Dim connectTask As Task = client.ConnectAsync(host.Trim(), port)
            Dim done As Task = Await Task.WhenAny(connectTask, Task.Delay(timeoutMs))
            If done IsNot connectTask Then
                ObserveFault(connectTask) ' abandoned connect will fault when we Close() below
                Return ProbeResult.Timeout
            End If
            Await connectTask ' surface a refused/no-route SocketException to the Catch
            If Not client.Connected Then Return ProbeResult.Refused

            ' Read the SSH identification string. An SSH server sends it immediately
            ' on connect (RFC 4253 §4.2), so a short read confirms it is our server.
            Dim readMs As Integer = Math.Max(600, timeoutMs \ 2)
            Try
                Dim ns As NetworkStream = client.GetStream()
                Dim buf(255) As Byte
                Dim readTask As Task(Of Integer) = ns.ReadAsync(buf, 0, buf.Length)
                Dim rdone As Task = Await Task.WhenAny(readTask, Task.Delay(readMs))
                If rdone Is readTask Then
                    Dim n As Integer = Await readTask
                    If n > 0 Then
                        Dim banner As String = Encoding.ASCII.GetString(buf, 0, n)
                        If banner.IndexOf("SSH-", StringComparison.OrdinalIgnoreCase) >= 0 Then Return ProbeResult.SshOk
                    End If
                Else
                    ObserveFault(readTask)
                End If
            Catch
            End Try

            ' Connected, but no SSH banner seen - the port is at least open.
            Return ProbeResult.PortOpen
        Catch
            Return ProbeResult.Refused
        Finally
            Try
                If client IsNot Nothing Then client.Close()
            Catch
            End Try
        End Try
    End Function

    ''' <summary>Marks an abandoned task's eventual exception as observed so it never
    ''' surfaces as an unobserved-task-exception.</summary>
    Private Sub ObserveFault(t As Task)
        If t Is Nothing Then Return
        t.ContinueWith(Sub(x)
                           ' Touch .Exception so the fault counts as observed.
                           If x.Exception IsNot Nothing Then Return
                       End Sub, TaskContinuationOptions.OnlyOnFaulted Or TaskContinuationOptions.ExecuteSynchronously)
    End Sub

End Module
