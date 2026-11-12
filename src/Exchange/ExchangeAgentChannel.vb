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
    ''' <summary>Ticket 269 P2.4: the agent serves a Google Drive listing in its directory (a Drive sign-in is present), so the directory may be read
    ''' with no exchange account (<see cref="Configured"/> False). Additive on the wire: written only when True, and an absent member reads False,
    ''' so an older agent and a newer viewer (or the reverse) keep working.</summary>
    Public ReadOnly Drive As Boolean

    Public Sub New(configured As Boolean, state As ExchangeAgentState, features As IEnumerable(Of String), deviceId As String, deviceName As String, lastReason As String,
                   Optional drive As Boolean = False)
        Me.Drive = drive
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
        If Drive Then o("drive") = True
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
                Dim drive As Boolean = r.TryGetProperty("drive", el) AndAlso el.ValueKind = JsonValueKind.True
                info = New ExchangeAgentStateInfo(configured, state, feats, deviceId, deviceName, lastReason, drive)
                Return True
            End Using
        Catch ex As JsonException
            Return False
        End Try
    End Function

End Class

''' <summary>Where a Drive job stands (ticket 269 P3.2).</summary>
Friend Enum ExchangeAgentDriveJobState
    ''' <summary>The REQUEST is on Drive and the device has not answered in full yet.</summary>
    Waiting = 0
    ''' <summary>The answer arrived: a listing, or every requested file saved whole.</summary>
    Done = 1
    ''' <summary>Refused, not answered, or not readable: <see cref="ExchangeAgentDriveJob.Error"/> says which.</summary>
    Failed = 2
End Enum

''' <summary>One fetched file: the path the viewer asked for and the file the agent saved for it.</summary>
Friend NotInheritable Class ExchangeAgentDriveFile
    Public ReadOnly Path As String
    Public ReadOnly SavedPath As String
    Public Sub New(path As String, savedPath As String)
        Me.Path = If(path, "")
        Me.SavedPath = If(savedPath, "")
    End Sub
End Class

''' <summary>The state of a Drive job as the agent reports it (ticket 269 P3.2): this product's private pipe, not a catalog shape. The
''' listing travels as the `listing.json` of the reply, verbatim in form, so one reader serves both.</summary>
Friend NotInheritable Class ExchangeAgentDriveJob

    Public ReadOnly JobId As String
    Public ReadOnly State As ExchangeAgentDriveJobState
    ''' <summary>The machine token of a failed job ("" otherwise).</summary>
    Public ReadOnly [Error] As String
    ''' <summary>The device the job waits for, as its record names it - for "waiting for .. via Google Drive".</summary>
    Public ReadOnly DeviceName As String
    ''' <summary>The listing of a done `list` job, or Nothing.</summary>
    Public ReadOnly Listing As ExchangeDriveListingReply
    ''' <summary>The saved files of a done `fetch` job, in the order asked.</summary>
    Public ReadOnly Files As IReadOnlyList(Of ExchangeAgentDriveFile)
    ''' <summary>When that device last wrote its record on Drive, as the record says it ("" when not known). The waiting line of
    ''' item CQ shows it; only the answer that starts a job carries it, because it does not move while one waits.</summary>
    Public ReadOnly LastRecordAt As String

    Public Sub New(jobId As String, state As ExchangeAgentDriveJobState, [error] As String, deviceName As String, listing As ExchangeDriveListingReply,
                   files As IReadOnlyList(Of ExchangeAgentDriveFile), Optional lastRecordAt As String = "")
        Me.LastRecordAt = If(lastRecordAt, "")
        Me.JobId = If(jobId, "")
        Me.State = state
        Me.Error = If([error], "")
        Me.DeviceName = If(deviceName, "")
        Me.Listing = listing
        Me.Files = If(files, Array.Empty(Of ExchangeAgentDriveFile)())
    End Sub

    ''' <summary>The JSON of the state. <paramref name="listingBody"/> is the reply's `listing.json` as received (Nothing when none).</summary>
    Public Shared Function ToJson(jobId As String, state As ExchangeAgentDriveJobState, [error] As String, deviceName As String, listingBody As Byte(),
                                  files As IEnumerable(Of ExchangeAgentDriveFile), Optional lastRecordAt As String = "") As String
        Dim o As New JsonObject()
        o("jobId") = If(jobId, "")
        o("state") = state.ToString()
        If Not String.IsNullOrEmpty([error]) Then o("error") = [error]
        o("deviceName") = If(deviceName, "")
        If Not String.IsNullOrEmpty(lastRecordAt) Then o("lastRecordAt") = lastRecordAt
        If listingBody IsNot Nothing Then o("listing") = JsonNode.Parse(Encoding.UTF8.GetString(listingBody))
        If files IsNot Nothing Then
            Dim list As New JsonArray()
            For Each f As ExchangeAgentDriveFile In files
                Dim item As New JsonObject()
                item("path") = f.Path
                item("saved") = f.SavedPath
                list.Add(item)
            Next
            o("files") = list
        End If
        Return o.ToJsonString()
    End Function

    Public Shared Function TryParse(json As String, ByRef job As ExchangeAgentDriveJob) As Boolean
        job = Nothing
        If String.IsNullOrEmpty(json) Then Return False
        Try
            Using doc As JsonDocument = JsonDocument.Parse(json)
                Dim r As JsonElement = doc.RootElement
                If r.ValueKind <> JsonValueKind.Object Then Return False
                Dim el As JsonElement = Nothing
                Dim id As String = If(r.TryGetProperty("jobId", el) AndAlso el.ValueKind = JsonValueKind.String, el.GetString(), "")
                If String.IsNullOrEmpty(id) Then Return False
                Dim state As ExchangeAgentDriveJobState = ExchangeAgentDriveJobState.Waiting
                If r.TryGetProperty("state", el) AndAlso el.ValueKind = JsonValueKind.String Then
                    Dim parsed As ExchangeAgentDriveJobState
                    If Not [Enum].TryParse(el.GetString(), False, parsed) Then Return False
                    state = parsed
                End If
                Dim err As String = If(r.TryGetProperty("error", el) AndAlso el.ValueKind = JsonValueKind.String, el.GetString(), "")
                Dim deviceName As String = If(r.TryGetProperty("deviceName", el) AndAlso el.ValueKind = JsonValueKind.String, el.GetString(), "")
                Dim lastRecordAt As String = If(r.TryGetProperty("lastRecordAt", el) AndAlso el.ValueKind = JsonValueKind.String, el.GetString(), "")
                Dim listing As ExchangeDriveListingReply = Nothing
                If r.TryGetProperty("listing", el) AndAlso el.ValueKind = JsonValueKind.Object Then
                    If Not ExchangeDriveRequests.TryParseListing(Encoding.UTF8.GetBytes(el.GetRawText()), listing) Then Return False
                End If
                Dim files As New List(Of ExchangeAgentDriveFile)
                If r.TryGetProperty("files", el) AndAlso el.ValueKind = JsonValueKind.Array Then
                    For Each item As JsonElement In el.EnumerateArray()
                        If item.ValueKind <> JsonValueKind.Object Then Continue For
                        Dim p As JsonElement = Nothing
                        Dim s As JsonElement = Nothing
                        If item.TryGetProperty("path", p) AndAlso p.ValueKind = JsonValueKind.String AndAlso
                           item.TryGetProperty("saved", s) AndAlso s.ValueKind = JsonValueKind.String Then files.Add(New ExchangeAgentDriveFile(p.GetString(), s.GetString()))
                    Next
                End If
                job = New ExchangeAgentDriveJob(id, state, err, deviceName, listing, files, lastRecordAt)
                Return True
            End Using
        Catch ex As JsonException
            Return False
        End Try
    End Function

    ''' <summary>Never the paths: they name the person's files.</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeAgentDriveJob " & State.ToString()
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
    Public Const RequestGetBroadcastSeal As String = "GetBroadcastSeal"
    ''' <summary>Ticket 201: answer the pending cast offer once, by broadcast id.</summary>
    Public Const RequestCastAnswer As String = "CastAnswer"
    ''' <summary>Reserved agent request. The viewer persists its cast preference in ModernViewerPreferences.</summary>
    Public Const RequestCastPolicy As String = "CastPolicy"
    Public Const RequestGetCastOffer As String = "GetCastOffer"
    Public Const RequestGetBroadcast As String = "GetBroadcast"
    Public Const RequestGetLiveDirectory As String = "GetLiveDirectory"
    ''' <summary>Ticket 269 P3.2: ask the device that lists a folder on Google Drive for a listing or files (`list` / `fetch`, item CO).
    ''' Answered at once with a job id; the answer arrives through <see cref="RequestDriveJob"/>, because a Drive device may take up to
    ''' two minutes and one pipe connection is held for 40 s at most.</summary>
    Public Const RequestDriveStart As String = "DriveStart"
    ''' <summary>Long-polls one Drive job (`waitMs` up to 30 s) for its state; a finished job is forgotten once it was read.</summary>
    Public Const RequestDriveJob As String = "DriveJob"
    ''' <summary>Gives a Drive job up: its REQUEST is deleted from Drive and nothing more is saved.</summary>
    Public Const RequestDriveCancel As String = "DriveCancel"

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
    ' Ticket 269 P3.2: a Drive job's own failures. The producer's refusals (`unavailable`, `not-permitted`, `pin-required`, `too-large`,
    ' `drive-full`, ExchangeDriveRequests) pass through as they are.
    ''' <summary>The device did not answer via Google Drive within two minutes (item CQ); its REQUEST was deleted.</summary>
    Public Const ErrorNoAnswer As String = "no-answer"
    ''' <summary>A file of the answer did not arrive whole (size or SHA-256), or the answer was not readable: nothing partial was kept.</summary>
    Public Const ErrorCorrupt As String = "corrupt"
    ''' <summary>The job id is not (or no longer) known to the agent.</summary>
    Public Const ErrorUnknownJob As String = "unknown-job"
    ''' <summary>Too many Drive jobs run at once.</summary>
    Public Const ErrorBusy As String = "busy"
    ''' <summary>The Google Drive channel is not signed in, or Drive cannot be reached right now.</summary>
    Public Const ErrorDriveOffline As String = "drive-offline"

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

    ' Local pipe tokens are diagnostics, never UI copy. Unknown tokens use the canonical refusal sentence.
    Public Shared Function ErrorMessage(errorToken As String) As ConnectionDiagnosisMessage
        If errorToken = ErrorNotConnected OrElse errorToken = ErrorNotConfigured OrElse errorToken = ErrorUnknownResource Then
            Return ConnectionDiagnosisMessages.Unavailable
        End If
        Return ConnectionDiagnosisMessages.OfCode(errorToken)
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

''' <summary>Ticket 269 P3.2: a Drive job's reply; <see cref="Job"/> is set when the reply is ok.</summary>
Friend NotInheritable Class ExchangeAgentDriveJobResult
    Public ReadOnly Reply As ExchangeAgentReply
    Public ReadOnly Job As ExchangeAgentDriveJob
    Public Sub New(reply As ExchangeAgentReply, job As ExchangeAgentDriveJob)
        Me.Reply = reply
        Me.Job = job
    End Sub
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

    ''' <summary>Ticket 269 P3.2: asks the PC that lists <paramref name="resourceId"/> on Google Drive for the listing of
    ''' <paramref name="path"/> ("" is the root). The answer is a waiting job; follow it with <see cref="DriveJobAsync"/>.</summary>
    Public Function DriveListAsync(resourceId As String, path As String, ct As CancellationToken) As Task(Of ExchangeAgentDriveJobResult)
        Return DriveStartAsync(Sub(o As JsonObject)
                                   o("action") = ExchangeDriveRequests.ActionList
                                   o("resourceId") = resourceId
                                   o("path") = If(path, "")
                               End Sub, ct)
    End Function

    ''' <summary>Ticket 269 P3.2: asks for files of a folder listed on Google Drive, saved by the agent into <paramref name="directory"/> -
    ''' an existing folder under the viewer's remote cache, refused anywhere else. Paths with one base name are refused (a reply names
    ''' files by base name only), and so is a request longer than the pipe's request bound: ask a file or a few at a time.</summary>
    Public Function DriveFetchAsync(resourceId As String, paths As IEnumerable(Of String), directory As String, ct As CancellationToken) As Task(Of ExchangeAgentDriveJobResult)
        Return DriveStartAsync(Sub(o As JsonObject)
                                   o("action") = ExchangeDriveRequests.ActionFetch
                                   o("resourceId") = resourceId
                                   Dim list As New JsonArray()
                                   For Each p As String In If(paths, Array.Empty(Of String)())
                                       list.Add(p)
                                   Next
                                   o("paths") = list
                                   o("directory") = If(directory, "")
                               End Sub, ct)
    End Function

    Private Async Function DriveStartAsync(fill As Action(Of JsonObject), ct As CancellationToken) As Task(Of ExchangeAgentDriveJobResult)
        Dim req As String = ExchangeAgentPipe.BuildRequest(ExchangeAgentPipe.RequestDriveStart, fill)
        If Encoding.UTF8.GetByteCount(req) >= ExchangeAgentPipe.MaxRequestBytes Then
            Return New ExchangeAgentDriveJobResult(New ExchangeAgentReply(ExchangeAgentReplyKind.Refused, ExchangeAgentPipe.ErrorBadRequest, ""), Nothing)
        End If
        Return ParseJob(Await SendAsync(req, 4000, ct).ConfigureAwait(False))
    End Function

    ''' <summary>The state of a Drive job, after waiting up to <paramref name="waitMs"/> (at most 30 s) for it to finish. A finished
    ''' job is answered once; asking again is `unknown-job`.</summary>
    Public Async Function DriveJobAsync(jobId As String, waitMs As Integer, ct As CancellationToken) As Task(Of ExchangeAgentDriveJobResult)
        Dim wait As Integer = Math.Min(Math.Max(waitMs, 0), 30000)
        Dim req As String = ExchangeAgentPipe.BuildRequest(ExchangeAgentPipe.RequestDriveJob,
            Sub(o As JsonObject)
                o("jobId") = If(jobId, "")
                o("waitMs") = wait
            End Sub)
        Return ParseJob(Await SendAsync(req, wait + 5000, ct).ConfigureAwait(False))
    End Function

    ''' <summary>Gives a Drive job up (the viewer left the folder); the agent withdraws its REQUEST.</summary>
    Public Function DriveCancelAsync(jobId As String, ct As CancellationToken) As Task(Of ExchangeAgentReply)
        Dim req As String = ExchangeAgentPipe.BuildRequest(ExchangeAgentPipe.RequestDriveCancel, Sub(o As JsonObject) o("jobId") = If(jobId, ""))
        Return SendAsync(req, 3000, ct)
    End Function

    Private Shared Function ParseJob(reply As ExchangeAgentReply) As ExchangeAgentDriveJobResult
        Dim job As ExchangeAgentDriveJob = Nothing
        If reply.IsOk AndAlso Not ExchangeAgentDriveJob.TryParse(reply.DataJson, job) Then
            Return New ExchangeAgentDriveJobResult(New ExchangeAgentReply(ExchangeAgentReplyKind.Malformed, "", ""), Nothing)
        End If
        Return New ExchangeAgentDriveJobResult(reply, job)
    End Function

End Class
#End If
