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
