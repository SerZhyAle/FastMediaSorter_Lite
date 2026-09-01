Option Strict On

Imports System.Threading.Tasks

''' <summary>
''' High-level, UI-agnostic driver for the Android-share sidecar. Wraps the raw
''' WorkerProcess/WorkerIpc calls into the flows both the Share settings tab and
''' the "Share this folder" wizard need: bring a folder set up and serving, wait
''' for the worker's async reachability probe to finish, and stop serving. All
''' work runs off the UI thread (Task.Run around the blocking pipe I/O), so
''' callers just Await. The worker is a process-wide singleton (one named pipe),
''' so the tab and the wizard always drive the same server.
'''
''' NOTE: the QR + .fmscfg are built on the LITE side by ShareConfigBuilder from
''' the returned Status (not via the worker's ExportConfig), so the "internet
''' access" toggle can advertise the external path even for a manual port
''' forward. This controller only needs the reachability data in Status.
''' </summary>
Public Module ShareController

    ''' <summary>The token the worker embeds in <see cref="WorkerStatus.LastStartError"/>
    ''' when a bind was refused with an access denial rather than "already in use" - the
    ''' Hyper-V / WSL / Docker excluded-range case. Matched literally and case-sensitively;
    ''' it is a marker precisely because the OS error text around it is localized. Must stay
    ''' identical to service.ExcludedRangeMarker in the worker repo.</summary>
    Public Const ExcludedRangeMarker As String = " [excluded-range]"

    ''' <summary>Outcome of a ShareFoldersAsync call.</summary>
    Public NotInheritable Class ShareResult
        ''' <summary>The worker exe was found next to the app.</summary>
        Public Property Available As Boolean
        ''' <summary>The worker answered the control pipe.</summary>
        Public Property Reachable As Boolean
        ''' <summary>Server running AND the async reachability probe has finished
        ''' (Status.Reachability populated), so LAN + external fields are final.</summary>
        Public Property Served As Boolean
        ''' <summary>Latest status snapshot (may be Nothing if unreachable).</summary>
        Public Property Status As WorkerStatus
    End Class

    ''' <summary>
    ''' Ensures the worker runs, replaces its shared-folder list, starts the SFTP
    ''' server, then polls until the worker's reachability probe finishes (LAN +
    ''' UPnP/echo). Never throws - transport failures come back as
    ''' Available/Reachable = False.
    ''' </summary>
    Public Async Function ShareFoldersAsync(folders As List(Of ShareFolder),
                                            Optional pollSeconds As Integer = 15) As Task(Of ShareResult)
        Dim r As New ShareResult()
        If Not WorkerProcess.IsAvailable() Then Return r
        r.Available = True

        Dim ensured As WorkerResponse = Await Task.Run(Function() WorkerProcess.EnsureRunning(6000))
        If ensured Is Nothing Then Return r
        r.Reachable = True
        r.Status = ensured.Status
        MarkShareEverStarted()

        ' Push the network policy BEFORE the server starts so the first reachability
        ' pass already honors LAN-only (no UPnP) and the connection cap.
        Await PushNetworkPolicyAsync()
        Await SendAsync(New WorkerRequest With {.type = "SetSharedFolders", .folders = folders}, 5000)
        Await SendAsync(New WorkerRequest With {.type = "StartServer"}, 6000)

        For attempt As Integer = 0 To Math.Max(0, pollSeconds)
            Dim cur As WorkerResponse = Await Task.Run(Function() WorkerProcess.TryGetStatus(2000))
            If cur IsNot Nothing AndAlso cur.Status IsNot Nothing Then r.Status = cur.Status
            ' Reachability is nil until the worker's async probe (mDNS + UPnP/NAT-PMP
            ' + public-IP echo) finishes; once non-nil, every field is final.
            If r.Status IsNot Nothing AndAlso r.Status.Running AndAlso r.Status.Reachability IsNot Nothing Then
                r.Served = True
                Return r
            End If
            Await Task.Delay(1000)
        Next
        Return r
    End Function

    ''' <summary>Ensures the worker process is up (spawns it if needed) and returns
    ''' its status, or Nothing if it could not be reached / is not installed.</summary>
    Public Async Function EnsureRunningAsync() As Task(Of WorkerStatus)
        If Not WorkerProcess.IsAvailable() Then Return Nothing
        Dim resp As WorkerResponse = Await Task.Run(Function() WorkerProcess.EnsureRunning(6000))
        Return If(resp IsNot Nothing, resp.Status, Nothing)
    End Function

    ''' <summary>
    ''' Ensures the worker is up AND reconciles what it ENFORCES with what LITE
    ''' ADVERTISES. The worker autostarts its SFTP server from its own persisted
    ''' shares.json (per-root readOnly frozen at the last SetSharedFolders push),
    ''' while the .fmscfg readOnly is recomputed live from ShareRootParams. A bare
    ''' relaunch never re-pushes the list, so a folder the phone is told is writable
    ''' (readOnly:false) can still be served read-only - the phone shows Move/Delete
    ''' but the SFTP rm is denied. Here, after the worker is up, we recompute each
    ''' root's readOnly from ShareRootParams (the same IsWritable() the export uses)
    ''' and, only when a root drifted, re-push the corrected list so the SFTP server
    ''' matches the contract. Never throws; a transport failure just leaves it as-is.
    ''' Returns the (possibly refreshed) status, or Nothing if unreachable.
    ''' </summary>
    Public Async Function EnsureRunningReconciledAsync() As Task(Of WorkerStatus)
        If Not WorkerProcess.IsAvailable() Then Return Nothing
        Dim resp As WorkerResponse = Await Task.Run(Function() WorkerProcess.EnsureRunning(6000))
        If resp Is Nothing Then Return Nothing
        ' A resume/reconcile must re-assert the network policy too, so LAN-only and
        ' the connection cap survive an autostart that brought the server up from
        ' shares.json before any UI ran.
        Await PushNetworkPolicyAsync()
        Dim status As WorkerStatus = resp.Status
        If status Is Nothing OrElse status.Roots Is Nothing OrElse status.Roots.Count = 0 Then Return status

        Dim corrected As New List(Of ShareFolder)()
        Dim drift As Boolean = False
        For Each r As ShareFolder In status.Roots
            Dim host As String = If(r.hostPath, "")
            Dim desiredReadOnly As Boolean = Not ShareRootParamsStore.GetFor(host).IsWritable()
            If desiredReadOnly <> r.readOnly Then drift = True
            corrected.Add(New ShareFolder With {.name = r.name, .hostPath = host, .readOnly = desiredReadOnly})
        Next
        If Not drift Then Return status

        ' SetSharedFolders replaces the list (hot-restarts the server if it was
        ' already running); StartServer then covers the case where autostart had not
        ' brought it up. Both best-effort.
        Await SendAsync(New WorkerRequest With {.type = "SetSharedFolders", .folders = corrected}, 5000)
        Await SendAsync(New WorkerRequest With {.type = "StartServer"}, 6000)

        Dim after As WorkerResponse = Await Task.Run(Function() WorkerProcess.TryGetStatus(2000))
        Return If(after IsNot Nothing AndAlso after.Status IsNot Nothing, after.Status, status)
    End Function

    ''' <summary>Fetches the current worker status, or Nothing if unreachable.</summary>
    Public Async Function GetStatusAsync() As Task(Of WorkerStatus)
        If Not WorkerProcess.IsAvailable() Then Return Nothing
        Dim resp As WorkerResponse = Await Task.Run(Function() WorkerProcess.TryGetStatus(2000))
        Return If(resp IsNot Nothing, resp.Status, Nothing)
    End Function

    ''' <summary>Stops the SFTP server (leaves the worker process running).</summary>
    Public Async Function StopServerAsync() As Task
        Await SendAsync(New WorkerRequest With {.type = "StopServer"}, 4000)
    End Function

    ''' <summary>
    ''' Pushes the network policy (max simultaneous connections + LAN-only switch +
    ''' the pinned listen port) from ShareSettings to the worker via SetNetworkPolicy.
    ''' The port rides here rather than on a request type of its own because this is
    ''' already the server-wide-knobs channel, is already pushed before StartServer and
    ''' on the resume path, and already restarts a running server when a knob changes -
    ''' which a port change needs anyway. This is the ENFORCING
    ''' side of "LAN only": it stops the worker opening any UPnP/NAT-PMP hole and
    ''' advertising a WAN path, not just stripping the exported config. Best-effort -
    ''' an older worker soft-fails on the unknown request type and keeps its
    ''' persisted/default policy. Call whenever these settings change while a share is
    ''' live, and it is already folded into the share-start and resume flows.
    ''' </summary>
    Public Async Function PushNetworkPolicyAsync() As Task
        Dim s As New ShareSettings()
        s.Load()
        Await SendAsync(New WorkerRequest With {
            .type = "SetNetworkPolicy",
            .maxConnections = ShareSettings.ClampConnections(s.MaxConnections),
            .lanOnly = s.LanOnlyExport,
            .port = ShareSettings.ClampPort(s.ListenPort)
        }, 5000)
    End Function

    ''' <summary>
    ''' The port this PC serves on, whether or not the server happens to be up. Answered in
    ''' the order that is actually true: what the worker itself is bound to, then what it
    ''' will bind next (it reports that while down - the number is in every QR already
    ''' handed out), then the number recorded on this side. 0 = no port anywhere yet, which
    ''' only happens before the first ever start.
    ''' </summary>
    Public Function EffectivePort(status As WorkerStatus) As Integer
        If status IsNot Nothing Then
            If status.Running AndAlso status.ListenPort > 0 Then Return status.ListenPort
            If status.DesiredPort > 0 Then Return status.DesiredPort
        End If
        Try
            Dim s As New ShareSettings()
            s.Load()
            Return ShareSettings.ClampPort(s.ListenPort)
        Catch
            Return ShareSettings.UnsetPort
        End Try
    End Function

    ''' <summary>
    ''' The honest verdict on the port, or "" when there is nothing to say. Two ways it can
    ''' fail to be the port, and neither may be silent: the worker refused to bind it and
    ''' stayed down (busy / inside a Hyper-V excluded range), or an older worker dropped the
    ''' unknown request field and is serving on a different number - which PortSupported
    ''' identifies rather than leaves us guessing. Called from the status -> UI path, so it
    ''' must never throw.
    ''' </summary>
    Public Function PortWarning(status As WorkerStatus) As String
        Dim want As Integer
        Try
            Dim s As New ShareSettings()
            s.Load()
            want = ShareSettings.ClampPort(s.ListenPort)
        Catch
            want = ShareSettings.UnsetPort
        End Try
        Return PortWarning(status, want)
    End Function

    ''' <summary>The verdict itself, with the port recorded on this side passed in - the
    ''' whole decision without a registry read, which is what makes it testable.</summary>
    Public Function PortWarning(status As WorkerStatus, want As Integer) As String
        If status Is Nothing Then Return ""
        If Not status.Running Then
            ' Only a FAILED start is ours to explain - a share the user simply turned off
            ' is not a port problem.
            If String.IsNullOrEmpty(status.LastStartError) Then Return ""
            ' The number comes from the worker when it has one: a start can fail on a port
            ' this side never recorded (the OS-assigned first one), and "some port is
            ' busy" would be a useless sentence.
            Dim busy As Integer = If(status.DesiredPort > 0, status.DesiredPort, want)
            If busy = ShareSettings.UnsetPort Then Return ""
            Dim text As String = ShareText.PortBusyText(busy)
            ' The worker marks an access denial (as opposed to a plain "in use") with a
            ' stable ASCII token, because the OS message itself is localized. That case
            ' needs the second line - nothing is listening and the port still cannot be
            ' bound, which is unguessable without naming Hyper-V/WSL/Docker.
            If status.LastStartError.IndexOf(ExcludedRangeMarker, StringComparison.Ordinal) >= 0 Then
                text &= Environment.NewLine & ShareText.PortExcludedRangeHint()
            End If
            Return text
        End If
        ' Serving on a number nobody asked for. Only reachable against a worker too old to
        ' understand the request field, which is what the message says.
        If want = ShareSettings.UnsetPort OrElse status.ListenPort = want Then Return ""
        Return ShareText.PortMismatchText(want, status.ListenPort)
    End Function

    ''' <summary>Resets the worker's local usage counters (stats.json) and returns the
    ''' fresh status, or Nothing if unreachable. Best-effort - an older worker replies
    ''' with a benign "unsupported request type" (soft failure), so nothing breaks.</summary>
    Public Async Function ResetStatsAsync() As Task(Of WorkerStatus)
        Dim resp As WorkerResponse = Await SendAsync(New WorkerRequest With {.type = "ResetStats"}, 4000)
        Return If(resp IsNot Nothing, resp.Status, Nothing)
    End Function

    ''' <summary>Persists the "the user has shared at least once" hint the first
    ''' time a share actually reaches the worker with folders to serve. Read back by
    ''' Companion's resume-on-launch (the tray host's ResumeShareIfEnabled analogue)
    ''' so sharing resumes automatically - including on a silent autostart into the
    ''' tray - instead of staying off until the window is opened again.</summary>
    Private Sub MarkShareEverStarted()
        Dim s As New ShareSettings()
        s.Load()
        If s.WorkerEverStarted Then Return
        s.WorkerEverStarted = True
        s.Save()
    End Sub

    Private Async Function SendAsync(req As WorkerRequest, timeoutMs As Integer) As Task(Of WorkerResponse)
        Return Await Task.Run(Function()
                                  Try
                                      Return WorkerIpc.Send(req, timeoutMs)
                                  Catch
                                      Return CType(Nothing, WorkerResponse)
                                  End Try
                              End Function)
    End Function

End Module
