Option Strict On

Imports System.IO
Imports System.IO.Pipes
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Named-pipe JSON control client for the bundled Android-share sidecar
''' (companion\fms-share-worker.exe). One request -> one response per
''' connection; the worker closes the pipe after replying. Transport only -
''' the worker owns the SFTP server, keys, mDNS, port mapping and QR/config
''' rendering. Protocol: SPECIFICATION_ANDROID_FOLDER_SHARE.md Appendix A (v1).
''' </summary>
Public NotInheritable Class WorkerIpc

    ''' <summary>Pipe name (server side is \\.\pipe\fms-companion).</summary>
    Public Const PipeName As String = "fms-companion"

    ''' <summary>IPC schema version this client speaks. Mismatch = "update the app".</summary>
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
        request.schemaVersion = SchemaVersion

        Using deadline As New CancellationTokenSource(Math.Max(250, timeoutMs))
            Try
                Return SendAsync(request, timeoutMs, pipeName, deadline.Token).GetAwaiter().GetResult()
            Catch ex As OperationCanceledException
                ' Cancellation here only ever means our own deadline.
                Throw New TimeoutException("Companion worker did not answer within " & timeoutMs.ToString() & " ms.", ex)
            End Try
        End Using
    End Function

    ''' <summary>The exchange itself, fully asynchronous so the deadline can actually
    ''' interrupt it - a synchronous pipe Read cannot be cancelled.</summary>
    Private Shared Async Function SendAsync(request As WorkerRequest,
                                            timeoutMs As Integer,
                                            pipeName As String,
                                            ct As CancellationToken) As Task(Of WorkerResponse)
        Using pipe As New NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous)
            Await pipe.ConnectAsync(timeoutMs, ct).ConfigureAwait(False)

            Dim payload As Byte() = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, JsonOpts))
            Await pipe.WriteAsync(payload, 0, payload.Length, ct).ConfigureAwait(False)
            Await pipe.FlushAsync(ct).ConfigureAwait(False)

            ' No length prefix - read until the worker closes its end.
            Dim respText As String
            Using ms As New MemoryStream()
                Dim buffer(8191) As Byte
                Do
                    Dim n As Integer = Await pipe.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(False)
                    If n <= 0 Then Exit Do
                    ms.Write(buffer, 0, n)
                Loop
                respText = Encoding.UTF8.GetString(ms.ToArray())
            End Using

            If String.IsNullOrEmpty(respText) Then
                Throw New IOException("Companion worker closed the pipe without a response.")
            End If

            ' Deserialize is case-insensitive (JsonOpts), so PascalCase DTO names
            ' bind to the worker's camelCase JSON fields.
            Return JsonSerializer.Deserialize(Of WorkerResponse)(respText, JsonOpts)
        End Using
    End Function

End Class

' --- request DTOs (property names are the on-the-wire JSON field names) --------

''' <summary>Request envelope. type is one of the Appendix A message names.</summary>
Public Class WorkerRequest
    Public Property schemaVersion As Integer = WorkerIpc.SchemaVersion
    ' "type" and "readOnly" collide with VB keywords; bracket-escape keeps the
    ' emitted JSON field name (the worker matches it case-insensitively anyway).
    Public Property [type] As String = ""
    ''' <summary>Only sent for SetSharedFolders; replaces the whole list.</summary>
    Public Property folders As List(Of ShareFolder) = Nothing
    ''' <summary>SetNetworkPolicy payload. Nullable so an unset knob is OMITTED from
    ''' the JSON (JsonOpts WhenWritingNull) rather than sent as an explicit 0/false -
    ''' the worker leaves an absent field unchanged. maxConnections is clamped to
    ''' [1, 99999] by the worker; lanOnly enforces the LAN-only switch (no UPnP, no
    ''' WAN path). Additive - an older worker replies with a benign
    ''' "unsupported request type", so nothing breaks.</summary>
    Public Property maxConnections As Integer?
    Public Property lanOnly As Boolean?
    ''' <summary>SetNetworkPolicy: move the SFTP listen port to this number. The worker binds
    ''' exactly it or does not serve at all - a port that quietly became another one is the
    ''' precise failure this exists to prevent. The port is a setting, not a mode, so there
    ''' is nothing to switch back to: 0 and "omitted" both mean unchanged. Additive on an
    ''' EXISTING request type, which an older worker silently drops rather than rejecting -
    ''' hence <see cref="WorkerStatus.PortSupported"/>, which turns that into a
    ''' diagnosis.</summary>
    Public Property port As Integer?
End Class

''' <summary>A shared root. Used both in requests (folders) and status (roots).</summary>
Public Class ShareFolder
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

' --- response DTOs (Appendix A.2) ---------------------------------------------

Public Class WorkerResponse
    Public Property SchemaVersion As Integer
    Public Property Ok As Boolean
    ''' <summary>Present only on failure (omitempty on the wire).</summary>
    Public Property [Error] As String
    Public Property Status As WorkerStatus
    Public Property Export As WorkerExport
End Class

Public Class WorkerStatus
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
    ''' a refused pinned port would read as a plain "not running".</summary>
    Public Property LastStartError As String
    Public Property Reachability As WorkerReachability
    ''' <summary>Local usage counters (SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md §3.2).
    ''' Additive field - absent from an older worker, in which case it stays Nothing and is
    ''' treated as "no data yet", never an error (same rule as <see cref="Reachability"/>).</summary>
    Public Property Stats As WorkerStats
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
    ''' <summary>The exact .fmscfg JSON (opaque to LITE).</summary>
    Public Property ConfigJson As String
    ''' <summary>Rendered QR as base64 PNG - decode straight into a PictureBox.</summary>
    Public Property QrPngBase64 As String
    Public Property HasInternetPath As Boolean
    Public Property ManualForwardHint As String
    Public Property FileExtension As String
End Class
