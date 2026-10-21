Option Strict On

Imports System.IO
Imports System.Linq
Imports System.IO.Pipes
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Win32.SafeHandles

''' <summary>
''' Named-pipe JSON control client for the bundled Android-share sidecar
''' (companion\fms-share-worker.exe). One request -> one response per
''' connection; the worker closes the pipe after replying. Transport only -
''' the worker owns the SFTP server, keys, mDNS, port mapping and QR/config
''' rendering. Legacy wire schema 1; DISK-SHARE disk requests use schema 2.
''' </summary>
Public NotInheritable Class WorkerIpc

    ''' <summary>Pipe name (server side is \\.\pipe\fms-companion).</summary>
    Public Const PipeName As String = "fms-companion"

    ''' <summary>Legacy IPC schema (WORKER-IPC section 3 rule 1). RequestSchema chooses
    ''' schema 2 only for the six DISK-SHARE request types.
    ''' It rides on every request, and a response carrying a HIGHER one is refused cleanly by
    ''' <see cref="EnsureUnderstood"/> - WORKER-IPC section 6 rule 3.</summary>
    Public Const SchemaVersion As Integer = 1

    ' System.Text.Json replaces net48's JavaScriptSerializer (spec §6.2). Two
    ' behaviours must be preserved to keep the frozen wire contract intact:
    '  - PropertyNameCaseInsensitive: the response DTOs are PascalCase but the
    '    worker emits camelCase - JavaScriptSerializer bound them case-insensitively.
    '  - WhenWritingNull: WorkerRequest.folders is only sent for SetSharedFolders;
    '    omit it (rather than emit "folders":null) on every other request.
    ' Request field names are already the exact on-the-wire names, so the default
    ' naming policy (verbatim CLR names, no camelCasing) emits them correctly.
    Private Shared ReadOnly JsonOpts As New JsonSerializerOptions With {
        .PropertyNameCaseInsensitive = True,
        .DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    }

    Private Sub New()
    End Sub

    ''' <summary>
    ''' Sends one request and returns the worker's response. Throws on transport
    ''' failure (no worker listening / timeout / broken pipe); callers
    ''' that want a soft failure should wrap in Try or use WorkerProcess helpers.
    '''
    ''' <paramref name="timeoutMs"/> bounds the WHOLE exchange, not just the connect. It used
    ''' to bound only the connect, while the write and the "read until the worker closes its
    ''' end" loop were unbounded blocking calls - so a worker that accepted the pipe and then
    ''' never answered (deadlocked, AV-suspended, stuck on disk I/O) blocked the call for ever.
    ''' Callers pass it through Task.Run, so each such call pinned a thread-pool thread and a
    ''' pipe handle permanently, and the status pollers added another every 10-30 s for as long
    ''' as Companion sat in the tray. Every awaiting UI flow simply never resumed.
    ''' </summary>
    Public Shared Function Send(request As WorkerRequest, Optional timeoutMs As Integer = 5000) As WorkerResponse
        Return Send(request, timeoutMs, PipeName)
    End Function

    ''' <summary>
    ''' The exchange against an explicitly named pipe. Product code never passes anything but
    ''' <see cref="PipeName"/> - the parameter exists so the timeout tests can drive a pipe of
    ''' their own. They must: the frozen pipe is a machine-wide singleton, so on any machine
    ''' where sharing is actually running - a Companion in the tray, or the Server edition's
    ''' FastMediaSorterCompanionSFTP service - "nothing is listening" and "the server accepts
    ''' and then says nothing" are both unreachable states, the real worker answers, and the
    ''' tests fail for a reason that has nothing to do with the code under test.
    ''' </summary>
    Public Shared Function Send(request As WorkerRequest, timeoutMs As Integer, pipeName As String) As WorkerResponse
        If request Is Nothing Then Throw New ArgumentNullException(NameOf(request))
        If String.IsNullOrEmpty(pipeName) Then Throw New ArgumentNullException(NameOf(pipeName))
        request.schemaVersion = RequestSchema(request)

        Using deadline As New CancellationTokenSource(Math.Max(250, timeoutMs))
            Try
                Return SendAsync(request, timeoutMs, pipeName, deadline.Token).GetAwaiter().GetResult()
            Catch ex As OperationCanceledException
                ' Cancellation here only ever means our own deadline.
                Throw New TimeoutException("Companion worker did not answer within " & timeoutMs.ToString() & " ms.", ex)
            End Try
        End Using
    End Function

    ''' <summary>
    ''' The exact bytes this host puts on the pipe for <paramref name="request"/> - the ONE
    ''' serializer, stamping <c>schemaVersion</c> first. Friend so the conformance tests pin
    ''' what the product emits rather than what a mirrored copy of its options would emit
    ''' (WORKER-IPC section 7: the host half of the recorded exchange).
    ''' </summary>
    Friend Shared Function SerializeRequest(request As WorkerRequest) As Byte()
        If request Is Nothing Then Throw New ArgumentNullException(NameOf(request))
        request.schemaVersion = RequestSchema(request)
        Return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, JsonOpts))
    End Function

    ''' <summary>
    ''' Reads one response exactly as <see cref="Send"/> does: case-insensitive binding, then
    ''' the version check. Throws <see cref="WorkerVersionRefusedException"/> for a response
    ''' from a newer worker - never returns a partially understood one.
    ''' </summary>
    Friend Shared Function ParseResponse(text As String, Optional understoodSchema As Integer = SchemaVersion) As WorkerResponse
        ' Deserialize is case-insensitive (JsonOpts), so PascalCase DTO names
        ' bind to the worker's camelCase JSON fields.
        Dim response As WorkerResponse
        Try
            response = JsonSerializer.Deserialize(Of WorkerResponse)(text, JsonOpts)
        Catch ex As JsonException
            ' A worker answered - with something this client cannot bind: a field whose type
            ' changed in a newer worker, a truncated write. The version is read on its own
            ' first, so a newer worker is still refused by NAME; anything else is "an answer
            ' we could not read", never "nobody answered" (ticket 054 H-22) - that reading sent
            ' EnsureRunning off to spawn a second worker that could only lose the pipe.
            Dim version As Integer = TryReadSchemaVersion(text)
            If version > understoodSchema Then Throw New WorkerVersionRefusedException(version, understoodSchema)
            Throw New WorkerExchangeException(WorkerExchangeProblem.Malformed, "Unreadable worker answer.", ex)
        End Try
        ' A literal "null" body deserializes to Nothing, which every caller read as silence.
        If response Is Nothing Then Throw New WorkerExchangeException(WorkerExchangeProblem.Malformed, "Empty worker answer.")
        EnsureUnderstood(response, understoodSchema)
        If understoodSchema = 2 AndAlso response.Ok AndAlso response.SchemaVersion <> 2 Then
            Throw New WorkerExchangeException(WorkerExchangeProblem.Malformed, "A disk response must use schema 2.")
        End If
        If understoodSchema = 3 AndAlso response.Ok AndAlso response.SchemaVersion <> 3 Then
            Throw New WorkerExchangeException(WorkerExchangeProblem.Malformed, "A resource PIN response must use schema 3.")
        End If
        If response.Disks IsNot Nothing AndAlso response.Disks.Count > 1024 Then Throw New WorkerExchangeException(WorkerExchangeProblem.Malformed, "Too many disk records.")
        If response.Status?.Roots IsNot Nothing AndAlso response.Status.Roots.Count > 1024 Then Throw New WorkerExchangeException(WorkerExchangeProblem.Malformed, "Too many roots.")
        Return response
    End Function

    Friend Shared Function RequestSchema(request As WorkerRequest) As Integer
        Select Case request.type
            Case "InvalidatePinSessions"
                Return 3
            Case "SetSharedFolders"
                If request.PinConfiguration OrElse (request.folders IsNot Nothing AndAlso request.folders.Any(Function(r) Not String.IsNullOrEmpty(r.accessPin))) Then Return 3
                Return SchemaVersion
            Case "disk-share", "disk-open", "disk-close", "disk-autostart", "disk-status", "disk-list"
                Return 2
            Case Else
                Return SchemaVersion
        End Select
    End Function

    ''' <summary>The top-level <c>schemaVersion</c> of an answer that did not bind as a whole,
    ''' or 0 when even that cannot be read.</summary>
    Private Shared Function TryReadSchemaVersion(text As String) As Integer
        Try
            Using doc As JsonDocument = JsonDocument.Parse(text)
                If doc.RootElement.ValueKind <> JsonValueKind.Object Then Return 0
                For Each prop As JsonProperty In doc.RootElement.EnumerateObject()
                    Dim v As Integer
                    If String.Equals(prop.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase) AndAlso
                       prop.Value.ValueKind = JsonValueKind.Number AndAlso prop.Value.TryGetInt32(v) Then Return v
                Next
            End Using
        Catch
        End Try
        Return 0
    End Function

    ' --- who is on the other end (ticket 054 H-10) ----------------------------------

    ''' <summary>
    ''' Test seam: a verifier for a PRIVATE pipe name, replacing <see cref="IsTrustedServer"/>
    ''' for that name only - so a test can drive the refusal without affecting any other
    ''' test running in parallel. Never set for the product's pipe.
    ''' </summary>
    Friend Shared ReadOnly ServerVerifierOverrides As New System.Collections.Concurrent.ConcurrentDictionary(Of String, Func(Of Integer, Boolean))(StringComparer.Ordinal)

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

    ''' <summary>
    ''' The pipe name is machine-wide, and before the worker creates it (logon, after a crash)
    ''' ANY local account can - with a DACL of its choosing. The client then sent that process
    ''' its folder list and put its username, password, fingerprint and port into the QR, so a
    ''' phone uploaded into someone else's server. The server is trusted when it is the
    ''' process the SCM runs our service in (only an administrator can register that name), or
    ''' when it runs as THIS account - which is every worker this user could have started, and
    ''' a boundary nothing else offers anyway (the same account can replace the app itself).
    ''' </summary>
    Friend Shared Function IsTrustedServer(pid As Integer) As Boolean
        If pid <= 0 Then Return False
        Dim servicePid As Integer = ServiceControl.ServiceProcessId()
        If servicePid > 0 AndAlso servicePid = pid Then Return True
        Dim owner As String = ProcessOwnerSid(pid)
        Return owner.Length > 0 AndAlso String.Equals(owner, ServiceControl.CurrentUserSid(), StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>The account a process runs as, or "" when it cannot be read - another
    ''' account's process usually cannot, which is exactly the answer "not ours".</summary>
    Friend Shared Function ProcessOwnerSid(pid As Integer) As String
        Dim proc As IntPtr = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
        If proc = IntPtr.Zero Then Return ""
        Try
            Dim token As IntPtr = IntPtr.Zero
            If Not OpenProcessToken(proc, TOKEN_QUERY, token) Then Return ""
            Try
                Using id As New WindowsIdentity(token)
                    Return If(id.User?.Value, "")
                End Using
            Finally
                CloseHandle(token)
            End Try
        Catch
            Return ""
        Finally
            CloseHandle(proc)
        End Try
    End Function

    ''' <summary>Refuses the exchange BEFORE a byte of the request is written when the
    ''' server is not one we trust.</summary>
    Private Shared Sub VerifyServer(pipe As NamedPipeClientStream, pipeName As String)
        Dim pid As UInteger
        Dim known As Boolean = GetNamedPipeServerProcessId(pipe.SafePipeHandle, pid)
        Dim verifier As Func(Of Integer, Boolean) = Nothing
        If Not ServerVerifierOverrides.TryGetValue(pipeName, verifier) Then verifier = AddressOf IsTrustedServer
        If known AndAlso verifier(CInt(pid)) Then Return
        Throw New WorkerExchangeException(WorkerExchangeProblem.NotOurWorker, "The control pipe is held by an untrusted process.")
    End Sub

    ''' <summary>The exchange itself, fully asynchronous so the deadline can actually
    ''' interrupt it - a synchronous pipe Read cannot be cancelled.</summary>
    Private Shared Async Function SendAsync(request As WorkerRequest,
                                            timeoutMs As Integer,
                                            pipeName As String,
                                            ct As CancellationToken) As Task(Of WorkerResponse)
        Using pipe As New NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous)
            Await pipe.ConnectAsync(timeoutMs, ct).ConfigureAwait(False)
            VerifyServer(pipe, pipeName)
            If Not String.IsNullOrEmpty(request.password) AndAlso pipeName = WorkerIpc.PipeName Then VerifyCredentialServer(pipe)

            Dim payload As Byte() = SerializeRequest(request)
            Try
                Await pipe.WriteAsync(payload, 0, payload.Length, ct).ConfigureAwait(False)
                Await pipe.FlushAsync(ct).ConfigureAwait(False)
            Finally
                Array.Clear(payload)
            End Try

            ' No length prefix - read until the worker closes its end.
            Dim respText As String
            Using ms As New MemoryStream()
                Dim buffer(8191) As Byte
                Do
                    Dim n As Integer = Await pipe.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(False)
                    If n <= 0 Then Exit Do
                    If RequestSchema(request) = 2 AndAlso ms.Length + n > 256 * 1024 Then Throw New WorkerExchangeException(WorkerExchangeProblem.Malformed, "Disk response exceeds 256 KiB.")
                    ms.Write(buffer, 0, n)
                Loop
                respText = Encoding.UTF8.GetString(ms.ToArray())
            End Using

            If String.IsNullOrEmpty(respText) Then
                Throw New IOException("Companion worker closed the pipe without a response.")
            End If

            Return ParseResponse(respText, RequestSchema(request))
        End Using
    End Function

    ''' <summary>
    ''' WORKER-IPC section 6 rule 3: a <c>schemaVersion</c> above the reader's own is refused
    ''' cleanly, by BOTH ends, with a message that names the two halves - never a partial
    ''' application of the response and never a guess. The server has held this since it was
    ''' written; this is the host half, and without it a newer worker was read as if it were
    ''' this one.
    '''
    ''' The case is reachable inside a single release here, which is why it is not theoretical:
    ''' the worker is a vendored binary built from a separate repository, so a newer one can be
    ''' dropped in beside an unchanged client.
    '''
    ''' Refused UPWARDS only, deliberately. The rule the server is stricter under - refuse any
    ''' version you do not know, including 0 (section 6 rule 4) - is stated for the server for a
    ''' reason the host does not share: an absent <c>schemaVersion</c> on a REQUEST means a
    ''' caller that is not a client of this contract at all, while an absent one on a RESPONSE
    ''' would mean a worker older than the field, and refusing that would break the very
    ''' upgrade path the field exists to protect. Section 3 rule 4's "an unknown field is
    ''' ignored" points the same way: older is tolerated, newer is refused.
    '''
    ''' The words "update the app" belong to the end that faces the user, which is this one -
    ''' the server's own message names the two halves and stops there.
    '''
    ''' Thrown as its OWN type, never as a transport failure. It used to be a plain
    ''' NotSupportedException on the theory that every caller shows a thrown message - in fact
    ''' every caller swallowed it as "the worker is unreachable", so a newer worker read as
    ''' "could not reach the companion" and EnsureRunning went on to spawn a second one.
    ''' <see cref="WorkerProcess.Exchange"/> is where it is turned into a typed result.
    ''' </summary>
    Friend Shared Sub EnsureUnderstood(response As WorkerResponse, Optional understoodSchema As Integer = SchemaVersion)
        If response Is Nothing Then Return
        If response.SchemaVersion <= understoodSchema Then Return
        Throw New WorkerVersionRefusedException(response.SchemaVersion, understoodSchema)
    End Sub

    Private Shared Sub VerifyCredentialServer(pipe As NamedPipeClientStream)
        Dim pid As UInteger
        If GetNamedPipeServerProcessId(pipe.SafePipeHandle, pid) Then
            Try
                Dim handle As IntPtr = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, CInt(pid))
                If handle = IntPtr.Zero Then Throw New IOException("Cannot query the worker image.")
                Try
                    Dim pathBuffer As New StringBuilder(32768)
                    Dim length As Integer = pathBuffer.Capacity
                    If Not QueryFullProcessImageName(handle, 0, pathBuffer, length) Then Throw New IOException("Cannot read the worker image.")
                    Dim path As String = pathBuffer.ToString()
                    If String.Equals(IO.Path.GetFileName(path), "fms-share-worker.exe", StringComparison.OrdinalIgnoreCase) Then
                        If ServiceControl.ServiceProcessId() <> CInt(pid) OrElse
                           path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) & "\", StringComparison.OrdinalIgnoreCase) OrElse
                           path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) & "\", StringComparison.OrdinalIgnoreCase) Then Return
                    End If
                Finally
                    CloseHandle(handle)
                End Try
            Catch
            End Try
        End If
        Throw New WorkerExchangeException(WorkerExchangeProblem.NotOurWorker, "Credential receiver identity could not be verified.")
    End Sub

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function QueryFullProcessImageName(process As IntPtr, flags As UInteger, path As StringBuilder, ByRef size As Integer) As Boolean
    End Function

End Class

''' <summary>
''' WORKER-IPC section 6 rule 3 on the host: the worker answered, with a protocol version above
''' the one this client speaks, so nothing in the answer was applied. Distinct from every
''' transport failure on purpose - a worker that answered is RUNNING, and the two conclusions a
''' caller draws from "nobody answered" (show "cannot reach", start a worker) are both wrong
''' here. The message is the registered, localized one the user reads.
''' </summary>
Public NotInheritable Class WorkerVersionRefusedException
    Inherits Exception

    ''' <summary>The <c>schemaVersion</c> the worker answered with.</summary>
    Public ReadOnly Property WorkerVersion As Integer
    ''' <summary>The <c>schemaVersion</c> this client speaks.</summary>
    Public ReadOnly Property ClientVersion As Integer

    Public Sub New(workerVersion As Integer, clientVersion As Integer)
        MyBase.New(Text(workerVersion, clientVersion))
        Me.WorkerVersion = workerVersion
        Me.ClientVersion = clientVersion
    End Sub

    ''' <summary>The sentence the user reads, in the active UI language.</summary>
    Public Shared Function Text(workerVersion As Integer, clientVersion As Integer) As String
        Return Localization.TF("Служба общего доступа отвечает по версии протокола {0}, а это приложение понимает {1}. Обновите приложение.",
                               workerVersion, clientVersion)
    End Function

    ''' <summary>The Server edition's variant: the worker there is the Windows service, which
    ''' updates with its own package, so the sentence names the service rather than leaving
    ''' "update the app" to be read as this console alone.</summary>
    Public Shared Function ServiceText(workerVersion As Integer, clientVersion As Integer) As String
        Return Localization.TF("Служба Windows {0} отвечает по версии протокола {1}, а это приложение понимает {2}. Обновите приложение и серверную редакцию до одной версии.",
                               ServiceControl.ServiceName, workerVersion, clientVersion)
    End Function

End Class

''' <summary>
''' What went wrong with an exchange that was neither a refusal nor silence. Each one means
''' SOMETHING holds the control pipe - so, like a refusal, none of them may be read as
''' "nobody answered", which is what starts a second worker.
''' </summary>
Public Enum WorkerExchangeProblem
    None
    ''' <summary>A worker answered in a shape this client cannot read (ticket 054 H-22).</summary>
    Malformed
    ''' <summary>The control pipe exists, and this account is not allowed to open it - another
    ''' account manages sharing on this PC (ticket 054 H-26).</summary>
    NotAuthorized
    ''' <summary>The control pipe is held by a process that is neither our service nor this
    ''' account's own (ticket 054 H-10). Nothing was sent to it.</summary>
    NotOurWorker
End Enum

''' <summary>An exchange that ended in a <see cref="WorkerExchangeProblem"/>. Its Message is
''' for the log only; the user reads ShareController.ProblemMessage.</summary>
Public NotInheritable Class WorkerExchangeException
    Inherits Exception

    Public ReadOnly Property Problem As WorkerExchangeProblem

    Public Sub New(problem As WorkerExchangeProblem, message As String, Optional inner As Exception = Nothing)
        MyBase.New(message, inner)
        Me.Problem = problem
    End Sub
End Class

' --- request DTOs (property names are the on-the-wire JSON field names) --------

''' <summary>Request envelope. type is one of the WORKER-IPC section 4 message names.</summary>
Public Class WorkerRequest
    <JsonIgnore>
    Public Property PinConfiguration As Boolean
    Public Property owner As String
    Public Property action As String
    Public Property containerPath As String
    Public Property rootName As String
    Public Property [readOnly] As Boolean?
    Public Property password As String
    Public Property enable As Boolean?
    Public Property consent As Boolean?
    Public Property force As Boolean?
    Public Property drainTimeout As Integer?
    <JsonPropertyOrder(-2)>
    Public Property schemaVersion As Integer = WorkerIpc.SchemaVersion
    ' "type" and "readOnly" collide with VB keywords; bracket-escape keeps the
    ' emitted JSON field name (the worker matches it case-insensitively anyway).
    <JsonPropertyOrder(-1)>
    Public Property [type] As String = ""
    ''' <summary>Only sent for SetSharedFolders; replaces the whole list.</summary>
    Public Property folders As List(Of ShareFolder) = Nothing
    ''' <summary>SetNetworkPolicy payload. Nullable so an unset knob is OMITTED from
    ''' the JSON (JsonOpts WhenWritingNull) rather than sent as an explicit 0/false -
    ''' the worker leaves an absent field unchanged. maxConnections is clamped to
    ''' [1, 99999] by the worker; lanOnly enforces the LAN-only switch (no UPnP, no
    ''' WAN path). Additive - an older worker replies <c>ok=false</c> ("unsupported
    ''' request type"), which is NOT benign when LAN only is on: ShareController reads it
    ''' and says so (<see cref="ShareController.LanOnlyNotEnforced"/>).</summary>
    Public Property maxConnections As Integer?
    Public Property lanOnly As Boolean?
    ''' <summary>SetNetworkPolicy: move the SFTP listen port to this number. The worker binds
    ''' exactly it or does not serve at all - a port that quietly became another one is the
    ''' precise failure this exists to prevent. The port is a setting, not a mode, so there
    ''' is nothing to switch back to. An unset port is OMITTED, never sent as 0 (WORKER-IPC
    ''' rule 4) - the worker happens to read 0 as unchanged too, but absent is what the
    ''' contract says, so absent is what this host sends. Additive on an
    ''' EXISTING request type, which an older worker silently drops rather than rejecting -
    ''' hence <see cref="WorkerStatus.PortSupported"/>, which turns that into a
    ''' diagnosis.</summary>
    Public Property port As Integer?
    ''' <summary>SetBroadcastPolicy: enable or disable the HTTP broadcast gateway.</summary>
    Public Property broadcastEnabled As Boolean?
    ''' <summary>SetBroadcastPolicy: optional port for the HTTP broadcast gateway.</summary>
    Public Property broadcastPort As Integer?
    ''' <summary>CreateBroadcast: "file" or "folder".</summary>
    Public Property kind As String
    ''' <summary>CreateBroadcast: host path of file or folder.</summary>
    Public Property hostPath As String
    ''' <summary>CreateBroadcast: include subfolders for folder broadcasts.</summary>
    Public Property recursive As Boolean?
    ''' <summary>CreateBroadcast: suggested channel title.</summary>
    Public Property title As String
    ''' <summary>CreateBroadcast: lifetime in seconds (0 = until revoked).</summary>
    Public Property ttlSeconds As Long?
    ''' <summary>RefreshBroadcast / RevokeBroadcast: ID of target broadcast.</summary>
    Public Property id As String

    ' There is NO exchange member on this request (ticket 170 section 5: SetExchange, registerHost, registerPort, certFingerprint, reissue
    ' retired). The worker is not told about an exchange server, and no exchange secret can be carried to it: the device agent publishes
    ' what the worker serves and splices tunnels to its loopback listener (ExchangePublisher), which is a structural property that
    ' WorkerRequestHasNoExchangeMemberTests holds by reflection.
End Class

''' <summary>A shared root. Used both in requests (folders) and status (roots).</summary>
Public Class ShareFolder
    Public Property accessPin As String
    <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingDefault)>
    Public Property pinProtected As Boolean
    Public Property owner As String
    <JsonIgnore>
    Public ReadOnly Property IsFmsOwned As Boolean
        Get
            Return String.IsNullOrEmpty(owner) OrElse owner = "fms"
        End Get
    End Property
    Public Property name As String = ""
    Public Property hostPath As String = ""
    ''' <summary>Default False (writable) mirrors the product default
    ''' (ShareRootParams.IsReadOnly = False - a freshly shared folder accepts the
    ''' phone's Move/copy out of the box). Every send path sets this explicitly from
    ''' ShareRootParams.IsWritable(); the default only guards against a future caller
    ''' that forgets. It used to default True, which silently served folders
    ''' read-only while the .fmscfg advertised them writable.</summary>
    Public Property [readOnly] As Boolean = False
End Class

''' <summary>A broadcast record served by the HTTP gateway in the worker.</summary>
Public Class WorkerBroadcast
    Private _itemCount As Integer
    Public Property Id As String = ""
    Public Property Token As String = ""
    Public Property Kind As String = ""
    Public Property MediaKind As String = ""
    Public Property HostPath As String = ""
    Public Property Title As String = ""
    Public Property Recursive As Boolean
    Public Property Items As List(Of WorkerBroadcastItem)
    Public Property ItemCount As Integer
        Get
            ' The first broadcast worker serialized items but omitted its computed count.
            Return If(_itemCount > 0, _itemCount, If(Items Is Nothing, 0, Items.Count))
        End Get
        Set(value As Integer)
            _itemCount = value
        End Set
    End Property
    Public Property DurationSeconds As Long
    Public Property CreatedAt As String = ""
    Public Property ExpiresAt As String = ""
    Public Property Hits As Long
    Public Property BytesServed As Long
    ''' <summary>The worker's irreversible expiry flag (WORKER-IPC 0.16: snapshot media kind and
    ''' irreversible expiry). Absent on a worker that predates it = False, which is also the
    ''' honest reading: no claim of expiry was made.</summary>
    Public Property Expired As Boolean
End Class

Public Class WorkerBroadcastItem
    Public Property Name As String = ""
    Public Property HostPath As String = ""
    Public Property Size As Long
End Class

' --- response DTOs (WORKER-IPC section 3 envelope + section 5 status) ---------

Public Class WorkerResponse
    Public Property SchemaVersion As Integer
    ''' <summary>The verdict, and the only one (WORKER-IPC section 3 rule 2): a transport that
    ''' succeeded says nothing about whether the request did. An older worker answers a
    ''' request type it does not know with <c>ok=false</c> - which, for SetNetworkPolicy, is
    ''' the difference between LAN only being enforced and not.</summary>
    Public Property Ok As Boolean
    ''' <summary>Present only on failure (omitempty on the wire).</summary>
    Public Property [Error] As String
    Public Property Status As WorkerStatus
    Public Property Export As WorkerExport
    Public Property Broadcast As WorkerBroadcast
    Public Property Broadcasts As List(Of WorkerBroadcast)
    Public Property Disk As SharedDisk
    Public Property Disks As List(Of SharedDisk)
    Public Property OutcomeClass As Integer

    ''' <summary>Set only on a response the HOST built: the worker answered with a newer
    ''' protocol version, so nothing it said was read (<see cref="Ok"/> is False and
    ''' <see cref="Status"/> is Nothing). Never on the wire.</summary>
    <JsonIgnore>
    Public Property Refusal As WorkerVersionRefusedException

    ''' <summary>True when this is the host's record of a version refusal.</summary>
    <JsonIgnore>
    Public ReadOnly Property IsRefused As Boolean
        Get
            Return Refusal IsNot Nothing
        End Get
    End Property

    ''' <summary>The typed result of a refused exchange: a worker answered, and what it
    ''' answered was not applied.</summary>
    Public Shared Function Refused(refusal As WorkerVersionRefusedException) As WorkerResponse
        If refusal Is Nothing Then Throw New ArgumentNullException(NameOf(refusal))
        Return New WorkerResponse With {.SchemaVersion = refusal.WorkerVersion, .Ok = False, .Refusal = refusal}
    End Function

    ''' <summary>Set only on a response the HOST built: something holds the control pipe, and
    ''' the exchange could not be completed with it (<see cref="WorkerExchangeProblem"/>).
    ''' <see cref="Ok"/> is False and <see cref="Status"/> is Nothing. Never on the wire.</summary>
    <JsonIgnore>
    Public Property Problem As WorkerExchangeProblem = WorkerExchangeProblem.None

    <JsonIgnore>
    Public ReadOnly Property IsProblem As Boolean
        Get
            Return Problem <> WorkerExchangeProblem.None
        End Get
    End Property

    ''' <summary>The typed result of an exchange that ended in <paramref name="problem"/>.</summary>
    Public Shared Function WithProblem(problem As WorkerExchangeProblem) As WorkerResponse
        Return New WorkerResponse With {.Ok = False, .Problem = problem}
    End Function
End Class

Public Class WorkerStatus
    Public Property ResourcePinSupported As Boolean
    Public Property Mode As String
    Public Property Capabilities As Dictionary(Of String, Boolean)
    Public Property DiskSharingError As String
    Public Property Running As Boolean
    Public Property ListenPort As Integer
    Public Property Fingerprint As String
    Public Property Username As String
    Public Property Password As String
    Public Property Roots As List(Of ShareFolder)
    ''' <summary>Always present (may be empty). Last server-side error text.</summary>
    Public Property LastError As String
    ''' <summary>The port this worker serves on, reported even while the server is DOWN -
    ''' which is exactly when it is needed, since that number is in every QR already handed
    ''' out and the window must still be able to show it. 0 only before the first ever
    ''' start.</summary>
    Public Property DesiredPort As Integer
    ''' <summary>This worker understands the <c>port</c> request field. Additive: an older
    ''' worker omits it, so False after we asked for a port positively identifies one - the
    ''' difference between "update the app" and a mystery number on screen.</summary>
    Public Property PortSupported As Boolean
    ''' <summary>Non-empty when the LAST attempt to start the SFTP server failed; cleared by
    ''' a successful start. The boot-restore path has no requester to answer, so without this
    ''' a refused pinned port would read as a plain "not running". A value that is not a JSON
    ''' string is read as absent (<see cref="LastStartErrorWire"/>): a type drift of a known
    ''' field never poisons the whole answer into "unreadable" (VERSIONING section 4).</summary>
    <JsonIgnore>
    Public Property LastStartError As String

    ''' <summary>The wire member <c>lastStartError</c>, bound as a raw JSON value so that ANY type
    ''' binds: a string is the text, everything else (and null) leaves <see cref="LastStartError"/>
    ''' Nothing, exactly as if the member were absent. Before this, a number or an object here made
    ''' System.Text.Json throw, which <see cref="WorkerIpc.ParseResponse"/> reports as an unreadable
    ''' answer for the WHOLE response - status, roots and ports lost with it. (A custom converter
    ''' would be the usual tool; VB cannot consume Utf8JsonReader, a ref struct.) Not for callers:
    ''' read <see cref="LastStartError"/>.</summary>
    <JsonPropertyName("lastStartError")>
    <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingDefault)>
    Public Property LastStartErrorWire As JsonElement
        Get
            Return Nothing
        End Get
        Set(value As JsonElement)
            LastStartError = If(value.ValueKind = JsonValueKind.String, value.GetString(), Nothing)
        End Set
    End Property

    Private _lastStartErrorKind As String = StartErrorKinds.None
    Private _lastStartErrorKindRaw As String = ""
    Private _hasLastStartErrorKind As Boolean

    ''' <summary>The structured reading of <see cref="LastStartError"/> (WORKER-IPC 0.17, the
    ''' consolidation amendment item 1): one of <see cref="StartErrorKinds"/> - empty (no start
    ''' error), <c>in-use</c> (an occupied address), <c>excluded-range</c> (an access denial in a
    ''' reserved port range) or <c>other</c>. An unknown token, and a value that is not a JSON
    ''' string, read as <c>other</c>; the token as received stays in
    ''' <see cref="LastStartErrorKindRaw"/>. Setting it - from the wire or in code - marks the
    ''' field as SENT (<see cref="HasLastStartErrorKind"/>); a JSON null is the same as absent.
    ''' A worker that predates the field never sets it, and the host then falls back to the
    ''' literal <see cref="ShareController.ExcludedRangeMarker"/> token and the free text.</summary>
    <JsonIgnore>
    Public Property LastStartErrorKind As String
        Get
            Return _lastStartErrorKind
        End Get
        Set(value As String)
            If value Is Nothing Then
                _lastStartErrorKind = StartErrorKinds.None
                _lastStartErrorKindRaw = ""
                _hasLastStartErrorKind = False
                Return
            End If
            _hasLastStartErrorKind = True
            _lastStartErrorKindRaw = value
            _lastStartErrorKind = StartErrorKinds.Normalize(value)
        End Set
    End Property

    ''' <summary>The wire member <c>lastStartErrorKind</c>, bound as a raw JSON value so that ANY
    ''' type binds (see <see cref="LastStartErrorWire"/>): a string is the token, JSON null is the
    ''' same as absent, and any other value becomes the first 64 characters of its JSON text - which
    ''' <see cref="StartErrorKinds.Normalize"/> then reads as <c>other</c>. Not for callers: read
    ''' <see cref="LastStartErrorKind"/>.</summary>
    <JsonPropertyName("lastStartErrorKind")>
    <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingDefault)>
    Public Property LastStartErrorKindWire As JsonElement
        Get
            Return Nothing
        End Get
        Set(value As JsonElement)
            Select Case value.ValueKind
                Case JsonValueKind.Undefined, JsonValueKind.Null
                    LastStartErrorKind = Nothing
                Case JsonValueKind.String
                    LastStartErrorKind = value.GetString()
                Case Else
                    Dim raw As String = value.GetRawText()
                    LastStartErrorKind = If(raw.Length > 64, raw.Substring(0, 64), raw)
            End Select
        End Set
    End Property

    ''' <summary>The kind token exactly as the worker sent it ("" when absent); for a value that
    ''' is not a JSON string, the first 64 characters of its JSON text. Kept for the log.</summary>
    <JsonIgnore>
    Public ReadOnly Property LastStartErrorKindRaw As String
        Get
            Return _lastStartErrorKindRaw
        End Get
    End Property

    ''' <summary>True when the worker sent <c>lastStartErrorKind</c> (any non-null value). When
    ''' False the host reads the start error from its text, as it did before the field existed.</summary>
    <JsonIgnore>
    Public ReadOnly Property HasLastStartErrorKind As Boolean
        Get
            Return _hasLastStartErrorKind
        End Get
    End Property
    Public Property Reachability As WorkerReachability
    ''' <summary>Local usage counters (SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md §3.2).
    ''' Additive field - absent from an older worker, in which case it stays Nothing and is
    ''' treated as "no data yet", never an error (same rule as <see cref="Reachability"/>).</summary>
    Public Property Stats As WorkerStats
    ''' <summary>Whether the HTTP broadcast gateway is enabled.</summary>
    Public Property BroadcastEnabled As Boolean
    ''' <summary>Port the HTTP broadcast gateway is listening on.</summary>
    Public Property BroadcastPort As Integer
    ''' <summary>Number of active broadcasts.</summary>
    Public Property BroadcastCount As Integer
    ''' <summary>This worker serves all five broadcast verbs of 196 section 3.4 (spec 159 section 3.1), so none
    ''' of them is answered with "unsupported request type". Additive and top level, the exact shape of
    ''' <see cref="PortSupported"/>: a worker that predates the verbs omits it, so absent is False. The host never infers support
    ''' from a refusal's wording.</summary>
    Public Property BroadcastSupported As Boolean
    Public Property BroadcastReachability As WorkerReachability
    ' No `exchange` / `exchangeSupported` member (ticket 170 section 5): the worker is not told about an exchange server, so its status
    ' has nothing to say about one. A worker that still reports such a member has it ignored (an unknown member is never an error).
End Class

''' <summary>The vocabulary of <c>status.lastStartErrorKind</c> (WORKER-IPC 0.17, consolidation
''' amendment item 1). Matched literally and case-sensitively, like the
''' <see cref="ShareController.ExcludedRangeMarker"/> token it refines.</summary>
Public NotInheritable Class StartErrorKinds
    ''' <summary>No start error.</summary>
    Public Const None As String = ""
    ''' <summary>The listen address is occupied.</summary>
    Public Const InUse As String = "in-use"
    ''' <summary>An access denial in a reserved range (the Hyper-V, WSL or Docker excluded
    ''' port-range case).</summary>
    Public Const ExcludedRange As String = "excluded-range"
    ''' <summary>Any other start error - and what every unknown token reads as.</summary>
    Public Const Other As String = "other"

    Private Sub New()
    End Sub

    ''' <summary>The documented default for an unknown value (VERSIONING section 4 rule 4):
    ''' <c>other</c>. The four known tokens map to themselves.</summary>
    Public Shared Function Normalize(token As String) As String
        Select Case token
            Case None, InUse, ExcludedRange, Other
                Return token
            Case Else
                Return Other
        End Select
    End Function
End Class

''' <summary>Local-only SFTP usage counters kept by the worker in <c>stats.json</c> and
''' surfaced via GetStatus (SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md §3.1). NOT telemetry -
''' these never leave the PC (invariant 1). Timestamps are UTC ISO8601 strings ("" = never).</summary>
Public Class WorkerStats
    ''' <summary>SFTP connections over all time (persists across worker restarts).</summary>
    Public Property TotalConnections As Long
    ''' <summary>SFTP connections since the current worker start (resets on restart).</summary>
    Public Property ConnectionsSinceStart As Long
    ''' <summary>UTC ISO8601 of the first-ever connection; "" if never.</summary>
    Public Property FirstConnectionAt As String
    ''' <summary>UTC ISO8601 of the most recent connection; "" if never.</summary>
    Public Property LastConnectionAt As String
    ''' <summary>Best-effort client address of the last connection; "" when unavailable.</summary>
    Public Property LastConnectionAddress As String
    ''' <summary>File-read operations served over all time (a file open for read; not listings).</summary>
    Public Property FilesServedTotal As Long
    ''' <summary>File-read operations served since the current worker start.</summary>
    Public Property FilesServedSinceStart As Long
End Class

Public Class WorkerReachability
    Public Property LanAddress As String
    Public Property LanMdnsActive As Boolean
    Public Property PortMapMethod As String
    Public Property ExternalHost As String
    Public Property ExternalPort As Integer
    Public Property IsCgnat As Boolean
    ''' <summary>False when IsCgnat=False could not be independently cross-checked
    ''' against the worker's IP-echo view (the echo query itself failed) - only
    ''' means "the router's request was accepted", not a confirmed guarantee that
    ''' the port is reachable from outside. See the worker's ExternalAddress.</summary>
    Public Property ExternalVerified As Boolean
    Public Property ManualForwardHint As String
    ''' <summary>A globally-routable IPv6 of this PC, dialed on ListenPort (the
    ''' server binds dual-stack). Bypasses NAT/CGNAT, so it is often the only way a
    ''' remote phone on cellular reaches a home PC. "" when none.</summary>
    Public Property Ipv6Address As String
    ''' <summary>ExternalPortChecked/Open carry the worker's honest external
    ''' connect-back result for the port-forward path: an outside vantage actually
    ''' tried ExternalHost:ExternalPort. Checked=False = the probe could not run
    ''' (treat as unconfirmed, NOT dead); Checked=True &amp; Open=False = the
    ''' forwarded port is genuinely unreachable from the internet.</summary>
    Public Property ExternalPortChecked As Boolean
    Public Property ExternalPortOpen As Boolean
End Class

Public Class WorkerExport
    ''' <summary>The exact .fmscfg JSON (opaque to the viewer).</summary>
    Public Property ConfigJson As String
    ''' <summary>Rendered QR as base64 PNG - decode straight into a PictureBox.</summary>
    Public Property QrPngBase64 As String
    Public Property HasInternetPath As Boolean
    Public Property ManualForwardHint As String
    Public Property FileExtension As String
End Class
