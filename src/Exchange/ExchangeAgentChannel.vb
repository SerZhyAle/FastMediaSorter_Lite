#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.IO.Pipes
Imports System.Runtime.InteropServices
Imports System.Security.Cryptography
Imports System.Security.Principal
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Nodes
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Win32.SafeHandles

' Ticket 180 T-3 (section 6): the local channel from the viewer to the device agent in the Share Manager. This file is
' the CLIENT half and the shared vocabulary; the server half is Companion\Core\ExchangeDeviceLocalChannel.vb.
'
' It is this product's PRIVATE surface, not a catalog contract: a named pipe `fms-device-agent-<hash of the user SID>`
' with a DACL for the current user only, ONE JSON request per connection (a line), answered `{ok, error, data}` (a line),
' bounded and cancellable like WorkerIpc. It carries no password. The viewer never opens a control stream and never
' sends `list`: it reads the snapshot here and opens DATA streams itself with the stored token (ExchangeDataStreams).
'
' The one place a secret crosses to another program of the same user is GetAccess (`access` of one listed resource).
' The client therefore refuses to talk to a pipe whose server process is not run by this same user (the pipe name is
' machine-wide, and before the agent creates it any local account could).

''' <summary>The state of the device agent, as the agent reports it and the viewer reads it.</summary>
Friend Enum ExchangeAgentState
    ''' <summary>No account is configured: the agent is as absent as before the feature.</summary>
    NotConfigured = 0
    ''' <summary>Opening the control stream.</summary>
    Connecting = 1
    ''' <summary>The control stream is up and the directory is mirrored.</summary>
    Online = 2
    ''' <summary>Waiting out a backoff after a lost stream or a retryable refusal.</summary>
    Reconnecting = 3
    ''' <summary>`bad-credentials`, `device-revoked`, `signed-out` or no token: the user must sign in again. Never retried.
    ''' After `signed-out` the entry, its `deviceId` and its attachments stay (item AT).</summary>
    SignInAgain = 4
    ''' <summary>The certificate is not the pinned one. Never retried, never re-pinned.</summary>
    PinMismatch = 5
    ''' <summary>`version-unsupported`: the program and the server speak different versions of the exchange (item CZ asks for no
    ''' update: release 46 has no minimum program version, item CK). Never retried.</summary>
    UpdateNeeded = 6
    ''' <summary>A defect of this program (`tls-required` on a TLS stream): logged, not retried.</summary>
    Defect = 7
    ''' <summary>Stopped on purpose (leaving the account, closing).</summary>
    Stopped = 8
End Enum

Friend NotInheritable Class ExchangeAgentStateInfo

    Public ReadOnly Configured As Boolean
    Public ReadOnly State As ExchangeAgentState
    Public ReadOnly Features As IReadOnlyList(Of String)
    Public ReadOnly DeviceId As String
    Public ReadOnly DeviceName As String
    ''' <summary>The last refusal of the server, as its wire reason ("" when none).</summary>
    Public ReadOnly LastReason As String

    Public Sub New(configured As Boolean, state As ExchangeAgentState, features As IEnumerable(Of String), deviceId As String, deviceName As String, lastReason As String)
        Me.Configured = configured
        Me.State = state
        Me.Features = New List(Of String)(If(features, Array.Empty(Of String)()))
        Me.DeviceId = If(deviceId, "")
        Me.DeviceName = If(deviceName, "")
        Me.LastReason = If(lastReason, "")
    End Sub

    Public ReadOnly Property IsOnline As Boolean
        Get
            Return State = ExchangeAgentState.Online
        End Get
    End Property

    Public Function ToJson() As String
        Dim o As New JsonObject()
        o("configured") = Configured
        o("state") = State.ToString()
        Dim f As New JsonArray()
        For Each s As String In Features
            f.Add(JsonValue.Create(s))
        Next
        o("features") = f
        o("deviceId") = DeviceId
        o("deviceName") = DeviceName
        o("lastReason") = LastReason
        Return o.ToJsonString()
    End Function

    Public Shared Function TryParse(json As String, ByRef info As ExchangeAgentStateInfo) As Boolean
        info = Nothing
        Try
            Using doc As JsonDocument = JsonDocument.Parse(json)
                Dim r As JsonElement = doc.RootElement
                If r.ValueKind <> JsonValueKind.Object Then Return False
                Dim el As JsonElement = Nothing
                Dim configured As Boolean = r.TryGetProperty("configured", el) AndAlso el.ValueKind = JsonValueKind.True
                Dim state As ExchangeAgentState = ExchangeAgentState.NotConfigured
                If r.TryGetProperty("state", el) AndAlso el.ValueKind = JsonValueKind.String Then
                    Dim parsed As ExchangeAgentState
                    If [Enum].TryParse(el.GetString(), False, parsed) Then state = parsed
                End If
                Dim feats As New List(Of String)
                If r.TryGetProperty("features", el) AndAlso el.ValueKind = JsonValueKind.Array Then
                    For Each item As JsonElement In el.EnumerateArray()
                        If item.ValueKind = JsonValueKind.String Then feats.Add(item.GetString())
                    Next
                End If
                Dim deviceId As String = If(r.TryGetProperty("deviceId", el) AndAlso el.ValueKind = JsonValueKind.String, el.GetString(), "")
                Dim deviceName As String = If(r.TryGetProperty("deviceName", el) AndAlso el.ValueKind = JsonValueKind.String, el.GetString(), "")
                Dim lastReason As String = If(r.TryGetProperty("lastReason", el) AndAlso el.ValueKind = JsonValueKind.String, el.GetString(), "")
                info = New ExchangeAgentStateInfo(configured, state, feats, deviceId, deviceName, lastReason)
                Return True
            End Using
        Catch ex As JsonException
            Return False
        End Try
    End Function

End Class

''' <summary>The pipe's name and the request vocabulary, shared by both ends.</summary>
Friend NotInheritable Class ExchangeAgentPipe
    Public Const ReceiverJson As String = "{""modes"":[""AUDIO_ONLY"",""VIDEO_AUDIO"",""VIDEO_ONLY""],""transports"":[""HTTP"",""RELAY"",""TUNNEL""]}"

    Private Sub New()
    End Sub

    Public Const NamePrefix As String = "fms-device-agent-"

    ' --- the requests (section 6) ---
    Public Const RequestGetState As String = "GetState"
    Public Const RequestGetDirectory As String = "GetDirectory"
    Public Const RequestGetAccess As String = "GetAccess"
    ''' <summary>Ticket 201: answer the pending cast offer once, by broadcast id.</summary>
    Public Const RequestCastAnswer As String = "CastAnswer"
    ''' <summary>Reserved agent request. The viewer persists its cast preference in ModernViewerPreferences.</summary>
    Public Const RequestCastPolicy As String = "CastPolicy"
    Public Const RequestGetCastOffer As String = "GetCastOffer"
    Public Const RequestGetBroadcast As String = "GetBroadcast"
    Public Const RequestGetLiveDirectory As String = "GetLiveDirectory"

    ' --- the machine errors of a refused request ---
    Public Const ErrorNotConfigured As String = "not-configured"
    Public Const ErrorNotConnected As String = "not-connected"
    Public Const ErrorUnknownResource As String = "unknown-resource"
    Public Const ErrorNoAccess As String = "no-access"
    Public Const ErrorUnsupported As String = "unsupported"
    Public Const ErrorBadRequest As String = "bad-request"
    ' Spec 234 T-2 (items DI, DJ, DP, DR): the four states of the sealed scheme, spelled as the codes of ConnectionDiagnosisMessages so
    ' the token IS the code. An old peer sees an unknown token and treats it as any other refusal.
    Public Const ErrorE2eMissing As String = "e2e-missing"
    Public Const ErrorKeyChanged As String = "key-changed"
    Public Const ErrorServerNoE2e As String = "server-no-e2e"
    Public Const ErrorSealedFailed As String = "sealed-failed"

    ''' <summary>The sealed state a refusal token names, or Nothing for any other token.</summary>
    Public Shared Function SealedMessageOf(errorToken As String) As ConnectionDiagnosisMessage
        Select Case errorToken
            Case ErrorE2eMissing
                Return ConnectionDiagnosisMessages.E2eMissing
            Case ErrorKeyChanged
                Return ConnectionDiagnosisMessages.KeyChanged
            Case ErrorServerNoE2e
                Return ConnectionDiagnosisMessages.ServerNoE2e
            Case ErrorSealedFailed
                Return ConnectionDiagnosisMessages.SealedFailed
            Case Else
                Return Nothing
        End Select
    End Function

    ''' <summary>The longest request line the server reads.</summary>
    Public Const MaxRequestBytes As Integer = 4096
    ''' <summary>The longest reply line the client reads.</summary>
    Public Const MaxReplyBytes As Integer = 1024 * 1024

    ''' <summary>The pipe of a user: the prefix and the first 16 hex characters of the SHA-256 of the SID.</summary>
    Public Shared Function NameForSid(sid As String) As String
        Dim h As Byte() = SHA256.HashData(Encoding.UTF8.GetBytes(If(sid, "")))
        Return NamePrefix & Convert.ToHexString(h, 0, 8).ToLowerInvariant()
    End Function

    Public Shared Function CurrentUserSid() As String
        Using id As WindowsIdentity = WindowsIdentity.GetCurrent()
            Return If(id.User?.Value, "")
        End Using
    End Function

    ''' <summary>The pipe of the current Windows user.</summary>
    Public Shared Function PipeName() As String
        Return NameForSid(CurrentUserSid())
    End Function

    Public Shared Function BuildRequest(type As String, Optional fill As Action(Of JsonObject) = Nothing) As String
        Dim o As New JsonObject()
        o("type") = type
        If fill IsNot Nothing Then fill(o)
        Return o.ToJsonString()
    End Function

    ''' <summary>One reply line.</summary>
    Public Shared Function BuildReply(ok As Boolean, [error] As String, dataJson As String) As String
        Dim o As New JsonObject()
        o("ok") = ok
        o("error") = If([error], "")
        If Not String.IsNullOrEmpty(dataJson) Then o("data") = JsonNode.Parse(dataJson)
        Return o.ToJsonString()
    End Function

    ''' <summary>Reads one '\n'-terminated line of UTF-8 of at most <paramref name="maxBytes"/>; Nothing at a clean end. A longer line is an error.</summary>
    Public Shared Async Function ReadLineAsync(stream As Stream, maxBytes As Integer, ct As CancellationToken) As Task(Of String)
        ' One request or one reply per connection, so anything read past the newline is nothing and may be dropped.
        Dim ms As New MemoryStream()
        Dim buffer(4095) As Byte
        While True
            Dim n As Integer = Await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(False)
            If n = 0 Then
                If ms.Length = 0 Then Return Nothing
                Exit While
            End If
            Dim newline As Integer = Array.IndexOf(buffer, CByte(10), 0, n)
            Dim take As Integer = If(newline >= 0, newline, n)
            If ms.Length + take > maxBytes Then Throw New InvalidDataException("line too long")
            ms.Write(buffer, 0, take)
            If newline >= 0 Then Exit While
        End While
        Return Encoding.UTF8.GetString(ms.ToArray())
    End Function

    Public Shared Async Function WriteLineAsync(stream As Stream, line As String, ct As CancellationToken) As Task
        Dim bytes As Byte() = Encoding.UTF8.GetBytes(line & vbLf)
        Await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(False)
        Await stream.FlushAsync(ct).ConfigureAwait(False)
    End Function

End Class

Friend Enum ExchangeAgentReplyKind
    ''' <summary>The agent answered `ok`.</summary>
    Ok = 0
    ''' <summary>Nobody listens on the pipe: the Share Manager (or its agent) is not running.</summary>
    NotRunning = 1
    ''' <summary>The agent answered with an error; <see cref="ExchangeAgentReply.Error"/> is its machine token.</summary>
    Refused = 2
    ''' <summary>No answer inside the deadline.</summary>
    TimedOut = 3
    ''' <summary>The pipe is held by a process that is not this user's: nothing was sent.</summary>
    Untrusted = 4
    ''' <summary>An answer that is not one JSON object, or too long.</summary>
    Malformed = 5
    Cancelled = 6
End Enum

Friend NotInheritable Class ExchangeAgentReply

    Public ReadOnly Kind As ExchangeAgentReplyKind
    Public ReadOnly [Error] As String
    ''' <summary>The `data` member as JSON ("" when none).</summary>
    Public ReadOnly DataJson As String

    Public Sub New(kind As ExchangeAgentReplyKind, [error] As String, dataJson As String)
        Me.Kind = kind
        Me.Error = If([error], "")
        Me.DataJson = If(dataJson, "")
    End Sub

    Public ReadOnly Property IsOk As Boolean
        Get
            Return Kind = ExchangeAgentReplyKind.Ok
        End Get
    End Property

    ''' <summary>Never the data: it may hold an `access` descriptor.</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeAgentReply " & Kind.ToString()
    End Function

End Class

Friend NotInheritable Class ExchangeAgentStateResult
    Public ReadOnly Reply As ExchangeAgentReply
    Public ReadOnly Info As ExchangeAgentStateInfo
    Public Sub New(reply As ExchangeAgentReply, info As ExchangeAgentStateInfo)
        Me.Reply = reply
        Me.Info = info
    End Sub
End Class

Friend NotInheritable Class ExchangeAgentDirectoryResult
    Public ReadOnly Reply As ExchangeAgentReply
    Public ReadOnly Snapshot As ExchangeDirectorySnapshot
    Public Sub New(reply As ExchangeAgentReply, snapshot As ExchangeDirectorySnapshot)
        Me.Reply = reply
        Me.Snapshot = snapshot
    End Sub
End Class

Friend NotInheritable Class ExchangeAgentAccessResult
    Public ReadOnly Reply As ExchangeAgentReply
    ''' <summary>The `access` of the resource as JSON; "" unless the reply is ok. A secret: never logged.</summary>
    Public ReadOnly AccessJson As String
    Public Sub New(reply As ExchangeAgentReply, accessJson As String)
        Me.Reply = reply
        Me.AccessJson = If(accessJson, "")
    End Sub

    ''' <summary>The sealed state of a refused request (`e2e-missing`, `key-changed`, `server-no-e2e`, `sealed-failed`), or Nothing
    ''' when the reply is ok or refused for another reason. Its text is the one of ConnectionDiagnosis, whole.</summary>
    Public ReadOnly Property SealedState As ConnectionDiagnosisMessage
        Get
            If Reply Is Nothing OrElse Reply.Kind <> ExchangeAgentReplyKind.Refused Then Return Nothing
            Return ExchangeAgentPipe.SealedMessageOf(Reply.Error)
        End Get
    End Property
End Class

''' <summary>The viewer's end of the local channel.</summary>
Friend NotInheritable Class ExchangeAgentClient

    Private ReadOnly _pipeName As String
    Private ReadOnly _connectTimeoutMs As Integer

    Public Sub New(Optional pipeName As String = Nothing, Optional connectTimeoutMs As Integer = 600)
        _pipeName = If(String.IsNullOrEmpty(pipeName), ExchangeAgentPipe.PipeName(), pipeName)
        _connectTimeoutMs = connectTimeoutMs
    End Sub

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function GetNamedPipeServerProcessId(pipe As SafePipeHandle, ByRef serverProcessId As UInteger) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function OpenProcess(desiredAccess As UInteger, inheritHandle As Boolean, processId As Integer) As IntPtr
    End Function

    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Shared Function OpenProcessToken(process As IntPtr, desiredAccess As UInteger, ByRef token As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CloseHandle(handle As IntPtr) As Boolean
    End Function

    Private Const PROCESS_QUERY_LIMITED_INFORMATION As UInteger = &H1000UI
    Private Const TOKEN_QUERY As UInteger = &H8UI

    ''' <summary>True when the process serving the pipe runs as this same Windows user. Another account's process usually cannot
    ''' even be opened, which is exactly the answer "not ours".</summary>
    Private Shared Function ServerIsOurs(pipe As NamedPipeClientStream) As Boolean
        Dim pid As UInteger
        If Not GetNamedPipeServerProcessId(pipe.SafePipeHandle, pid) Then Return False
        Dim proc As IntPtr = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, CInt(pid))
        If proc = IntPtr.Zero Then Return False
        Try
            Dim token As IntPtr = IntPtr.Zero
            If Not OpenProcessToken(proc, TOKEN_QUERY, token) Then Return False
            Try
                Using id As New WindowsIdentity(token)
                    Return String.Equals(If(id.User?.Value, ""), ExchangeAgentPipe.CurrentUserSid(), StringComparison.OrdinalIgnoreCase)
                End Using
            Finally
                CloseHandle(token)
            End Try
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is UnauthorizedAccessException
            Return False
        Finally
            CloseHandle(proc)
        End Try
    End Function

    ''' <summary>One request, one reply, the whole exchange bounded by <paramref name="timeoutMs"/>.</summary>
    Public Async Function SendAsync(requestJson As String, timeoutMs As Integer, ct As CancellationToken) As Task(Of ExchangeAgentReply)
        Using deadline As New CancellationTokenSource(Math.Max(250, timeoutMs))
            Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token)
                Try
                    Using pipe As New NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous)
                        Try
                            Await pipe.ConnectAsync(_connectTimeoutMs, linked.Token).ConfigureAwait(False)
                        Catch ex As TimeoutException
                            Return New ExchangeAgentReply(ExchangeAgentReplyKind.NotRunning, "", "")
                        End Try
                        If Not ServerIsOurs(pipe) Then Return New ExchangeAgentReply(ExchangeAgentReplyKind.Untrusted, "", "")
                        Await ExchangeAgentPipe.WriteLineAsync(pipe, requestJson, linked.Token).ConfigureAwait(False)
                        Dim line As String = Await ExchangeAgentPipe.ReadLineAsync(pipe, ExchangeAgentPipe.MaxReplyBytes, linked.Token).ConfigureAwait(False)
                        If line Is Nothing Then Return New ExchangeAgentReply(ExchangeAgentReplyKind.Malformed, "", "")
                        Return ParseReply(line)
                    End Using
                Catch ex As OperationCanceledException
                    Return New ExchangeAgentReply(If(ct.IsCancellationRequested, ExchangeAgentReplyKind.Cancelled, ExchangeAgentReplyKind.TimedOut), "", "")
                Catch ex As InvalidDataException
                    Return New ExchangeAgentReply(ExchangeAgentReplyKind.Malformed, "", "")
                Catch ex As IOException
                    Return New ExchangeAgentReply(ExchangeAgentReplyKind.NotRunning, "", "")
                End Try
            End Using
        End Using
    End Function

    Friend Shared Function ParseReply(line As String) As ExchangeAgentReply
        Try
            Using doc As JsonDocument = JsonDocument.Parse(line)
                Dim r As JsonElement = doc.RootElement
                If r.ValueKind <> JsonValueKind.Object Then Return New ExchangeAgentReply(ExchangeAgentReplyKind.Malformed, "", "")
                Dim el As JsonElement = Nothing
                Dim ok As Boolean = r.TryGetProperty("ok", el) AndAlso el.ValueKind = JsonValueKind.True
                Dim err As String = If(r.TryGetProperty("error", el) AndAlso el.ValueKind = JsonValueKind.String, el.GetString(), "")
                Dim data As String = If(r.TryGetProperty("data", el), el.GetRawText(), "")
                Return New ExchangeAgentReply(If(ok, ExchangeAgentReplyKind.Ok, ExchangeAgentReplyKind.Refused), err, data)
            End Using
        Catch ex As JsonException
            Return New ExchangeAgentReply(ExchangeAgentReplyKind.Malformed, "", "")
        End Try
    End Function

    Public Async Function GetStateAsync(ct As CancellationToken) As Task(Of ExchangeAgentStateResult)
        Dim reply As ExchangeAgentReply = Await SendAsync(ExchangeAgentPipe.BuildRequest(ExchangeAgentPipe.RequestGetState), 3000, ct).ConfigureAwait(False)
        Dim info As ExchangeAgentStateInfo = Nothing
        If reply.IsOk AndAlso Not ExchangeAgentStateInfo.TryParse(reply.DataJson, info) Then
            reply = New ExchangeAgentReply(ExchangeAgentReplyKind.Malformed, "", "")
        End If
        Return New ExchangeAgentStateResult(reply, info)
    End Function

    ''' <summary>The snapshot without this device's own records and without any `access`. With <paramref name="waitMs"/> above 0 the
    ''' call long-polls up to that long for a revision newer than <paramref name="sinceRevision"/> (a pipe with one request per
    ''' connection cannot push).</summary>
    Public Async Function GetDirectoryAsync(sinceRevision As Long, waitMs As Integer, ct As CancellationToken, Optional liveBroadcasts As Boolean = False) As Task(Of ExchangeAgentDirectoryResult)
        Dim req As String = ExchangeAgentPipe.BuildRequest(If(liveBroadcasts, ExchangeAgentPipe.RequestGetLiveDirectory, ExchangeAgentPipe.RequestGetDirectory),
            Sub(o As JsonObject)
                o("sinceRevision") = sinceRevision
                o("waitMs") = waitMs
            End Sub)
        Dim reply As ExchangeAgentReply = Await SendAsync(req, waitMs + 5000, ct).ConfigureAwait(False)
        Dim snap As ExchangeDirectorySnapshot = Nothing
        If reply.IsOk Then
            Try
                snap = ExchangeDirectorySnapshot.FromLocalJson(reply.DataJson)
            Catch ex As JsonException
                reply = New ExchangeAgentReply(ExchangeAgentReplyKind.Malformed, "", "")
            End Try
        End If
        Return New ExchangeAgentDirectoryResult(reply, snap)
    End Function

    ''' <summary>`access` of one listed resource: the one place a secret crosses to another program of the same user.</summary>
    Public Async Function GetAccessAsync(resourceId As String, ct As CancellationToken) As Task(Of ExchangeAgentAccessResult)
        Dim req As String = ExchangeAgentPipe.BuildRequest(ExchangeAgentPipe.RequestGetAccess, Sub(o As JsonObject) o("resourceId") = resourceId)
        Dim reply As ExchangeAgentReply = Await SendAsync(req, 4000, ct).ConfigureAwait(False)
        If Not reply.IsOk Then Return New ExchangeAgentAccessResult(reply, "")
        Return New ExchangeAgentAccessResult(reply, reply.DataJson)
    End Function

End Class
#End If
