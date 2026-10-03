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
''' NOTE: the QR + .fmscfg are built on the viewer side by ShareConfigBuilder from
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
        ''' <summary>Non-empty when the worker ANSWERED and said no - a version refusal, an
        ''' <c>ok=false</c> on SetSharedFolders/StartServer, or a start that left the server
        ''' down with a <c>lastError</c>. What the user reads instead of the generic
        ''' "started, address not confirmed", which is a different problem.</summary>
        Public Property ErrorText As String = ""
    End Class

    ''' <summary>
    ''' Ensures the worker runs, replaces its shared-folder list, starts the SFTP
    ''' server, then polls until the worker's reachability probe finishes (LAN +
    ''' UPnP/echo). Never throws - transport failures come back as
    ''' Available/Reachable = False, and a worker that refused comes back with
    ''' <see cref="ShareResult.ErrorText"/>.
    ''' </summary>
    Public Async Function ShareFoldersAsync(folders As List(Of ShareFolder),
                                            Optional pollSeconds As Integer = 15) As Task(Of ShareResult)
        Dim r As New ShareResult()
        If Not WorkerProcess.IsAvailable() Then Return r
        r.Available = True

        Dim ensured As WorkerResponse = Await Task.Run(Function() WorkerProcess.EnsureRunning(6000))
        If ensured Is Nothing Then Return r
        r.Reachable = True
        If ensured.IsRefused OrElse ensured.IsProblem Then
            ' Something holds the pipe and did not give a status we can use - its own sentence,
            ' never "could not reach".
            r.ErrorText = If(ensured.IsRefused, RefusalMessage(ensured.Refusal), ProblemMessage(ensured.Problem))
            Return r
        End If
        r.Status = ensured.Status
        MarkShareEverStarted()

        Await ServeAsync(r, folders, pollSeconds, WorkerIpc.PipeName)
        Return r
    End Function

    ''' <summary>
    ''' The serving half of <see cref="ShareFoldersAsync"/> against an explicit pipe (tests
    ''' drive a private one). Each step's <c>ok</c> is READ (WORKER-IPC section 3 rule 2): a
    ''' refused folder list is not followed by a StartServer that would serve the previous
    ''' one, and a refused start is reported with the worker's own words.
    ''' </summary>
    Friend Async Function ServeAsync(r As ShareResult, folders As List(Of ShareFolder),
                                     pollSeconds As Integer, pipeName As String) As Task
        ' Push the network policy BEFORE the server starts so the first reachability
        ' pass already honors LAN-only (no UPnP) and the connection cap.
        Await PushNetworkPolicyAsync(pipeName)

        Dim setResp As WorkerResponse = Await SendAsync(New WorkerRequest With {.type = "SetSharedFolders", .folders = FmsFoldersOnly(folders)}, 5000, pipeName)
        Dim refused As String = RefusedText(setResp)
        If refused <> "" Then
            r.ErrorText = refused
            Return
        End If

        Dim startResp As WorkerResponse = Await SendAsync(New WorkerRequest With {.type = "StartServer"}, 6000, pipeName)
        refused = RefusedText(startResp)
        If refused <> "" Then
            r.ErrorText = refused
            If startResp.Status IsNot Nothing Then r.Status = startResp.Status
            Return
        End If

        For attempt As Integer = 0 To Math.Max(0, pollSeconds)
            Dim cur As WorkerResponse = Await Task.Run(Function() WorkerProcess.TryGetStatus(2000, pipeName))
            If cur IsNot Nothing AndAlso cur.Ok AndAlso cur.Status IsNot Nothing Then r.Status = cur.Status
            ' Reachability is nil until the worker's async probe (mDNS + UPnP/NAT-PMP
            ' + public-IP echo) finishes; once non-nil, every field is final.
            If r.Status IsNot Nothing AndAlso r.Status.Running AndAlso r.Status.Reachability IsNot Nothing Then
                r.Served = True
                Return
            End If
            Await Task.Delay(1000)
        Next

        ' StartServer with zero folders (or a failed bind) answers ok=true and records the
        ' reason in lastError instead (WORKER-IPC section 4) - a server that is DOWN with a
        ' reason is not "started, address not confirmed". A running server's lastError is
        ' history, not a verdict on this start, and is left alone.
        If r.Status IsNot Nothing AndAlso Not r.Status.Running AndAlso Not String.IsNullOrEmpty(r.Status.LastError) Then
            r.ErrorText = WorkerSaidNoText(r.Status.LastError)
        End If
    End Function

    ''' <summary>"" when <paramref name="resp"/> is a success or nobody answered (a transport
    ''' failure has no verdict to report); otherwise the sentence for the user: the refusal
    ''' message, or the worker's own <c>error</c>.</summary>
    Friend Function RefusedText(resp As WorkerResponse) As String
        If resp Is Nothing OrElse resp.Ok Then Return ""
        If resp.IsRefused Then Return RefusalMessage(resp.Refusal)
        If resp.IsProblem Then Return ProblemMessage(resp.Problem)
        Dim why As String = resp.Error
        If String.IsNullOrEmpty(why) AndAlso resp.Status IsNot Nothing Then why = resp.Status.LastError
        Return WorkerSaidNoText(why)
    End Function

    Private Function WorkerSaidNoText(why As String) As String
        Return Localization.TF("Служба общего доступа отказала: {0}", If(String.IsNullOrEmpty(why), "?", why))
    End Function

    ''' <summary>
    ''' The sentence for a version refusal (WORKER-IPC section 6 rule 3) - "" when there is
    ''' none. Without an argument it answers for the most recent exchange with THE worker
    ''' (<see cref="WorkerProcess.LastRefusal"/>), which is what a screen holding only a
    ''' Nothing status needs. In Server mode it names the service, which updates with its own
    ''' package.
    ''' </summary>
    Public Function RefusalMessage(Optional refusal As WorkerVersionRefusedException = Nothing) As String
        If refusal Is Nothing Then refusal = WorkerProcess.LastRefusal
        ' The other ways a pipe can be held and still give no status - they are the same
        ' kind of fact about THE worker, and every screen that asks for this sentence needs
        ' them too (ticket 054 H-10, H-22, H-26).
        If refusal Is Nothing Then Return ProblemMessage(WorkerProcess.LastProblem)
        Dim serviceHost As Boolean
        Try
            serviceHost = ServerFeatures.IsSystemServiceHost()
        Catch
            serviceHost = False
        End Try
        Return RefusalMessage(refusal, serviceHost)
    End Function

    ''' <summary>The sentence for an exchange that ended in <paramref name="problem"/>, "" for
    ''' None. Each names what holds the pipe instead of "could not reach the companion".</summary>
    Public Function ProblemMessage(problem As WorkerExchangeProblem) As String
        Select Case problem
            Case WorkerExchangeProblem.Malformed
                Return Localization.T("Служба общего доступа ответила в непонятном формате. Обновите приложение и службу до одной версии.")
            Case WorkerExchangeProblem.NotAuthorized
                Return Localization.T("Этой учётной записи не разрешено управлять общим доступом на этом компьютере - им управляет другая учётная запись.")
            Case WorkerExchangeProblem.NotOurWorker
                Return Localization.T("Канал управления общим доступом занят посторонней программой. Ей ничего не передано; раздача не запущена. Перезагрузите компьютер или завершите эту программу.")
            Case Else
                Return ""
        End Select
    End Function

    ''' <summary>The decision without the host-mode probe, which is what makes it testable.</summary>
    Friend Function RefusalMessage(refusal As WorkerVersionRefusedException, serviceHost As Boolean) As String
        If refusal Is Nothing Then Return ""
        If serviceHost Then Return WorkerVersionRefusedException.ServiceText(refusal.WorkerVersion, refusal.ClientVersion)
        Return refusal.Message
    End Function

    ''' <summary>The status of an understood, successful answer - Nothing otherwise. A
    ''' refused or <c>ok=false</c> response is never read for its fields.</summary>
    Private Function StatusOf(resp As WorkerResponse) As WorkerStatus
        If resp Is Nothing OrElse Not resp.Ok Then Return Nothing
        Return resp.Status
    End Function

    ''' <summary>
    ''' Ensures the worker is up AND reconciles what it ENFORCES with what the viewer
    ''' ADVERTISES. The worker autostarts its SFTP server from its own persisted
    ''' shares.json (per-root readOnly frozen at the last SetSharedFolders push),
    ''' while the .fmscfg readOnly is recomputed live from ShareRootParams. A bare
    ''' relaunch never re-pushes the list, so a folder the phone is told is writable
    ''' (readOnly:false) can still be served read-only - the phone shows Move/Delete
    ''' but the SFTP rm is denied. Here, after the worker is up, we recompute each
    ''' root's readOnly from ShareRootParams (the same IsWritable() the export uses)
    ''' and, only when a root drifted, re-push the corrected list so the SFTP server
    ''' matches the contract - in the TIGHTENING direction only (<see cref="ReconciledReadOnly"/>);
    ''' the export clamps to the server for the other one (ShareConfigBuilder.ClampToServer).
    ''' Never throws; a transport failure just leaves it as-is.
    ''' Returns the (possibly refreshed) status, or Nothing if unreachable.
    ''' </summary>
    Public Async Function EnsureRunningReconciledAsync() As Task(Of WorkerStatus)
        If Not WorkerProcess.IsAvailable() Then Return Nothing
        Dim resp As WorkerResponse = Await Task.Run(Function() WorkerProcess.EnsureRunning(6000))
        ' A refusal leaves nothing to reconcile with - nothing it said was read, and nothing
        ' more is sent to it. The caller reads RefusalMessage() for the sentence.
        Dim status As WorkerStatus = StatusOf(resp)
        If status Is Nothing Then Return Nothing
        ' A resume/reconcile must re-assert the network policy too, so LAN-only and
        ' the connection cap survive an autostart that brought the server up from
        ' shares.json before any UI ran.
        Await PushNetworkPolicyAsync()
        If status Is Nothing OrElse status.Roots Is Nothing OrElse status.Roots.Count = 0 Then Return status

        Dim corrected As New List(Of ShareFolder)()
        Dim drift As Boolean = False
        For Each r As ShareFolder In status.Roots
            If Not r.IsFmsOwned Then Continue For
            Dim host As String = If(r.hostPath, "")
            Dim desiredReadOnly As Boolean = ReconciledReadOnly(r.readOnly, ShareRootParamsStore.GetFor(host))
            If desiredReadOnly <> r.readOnly Then drift = True
            corrected.Add(New ShareFolder With {.name = r.name, .hostPath = host, .readOnly = desiredReadOnly})
        Next
        If Not drift Then Return status

        ' SetSharedFolders replaces the list (hot-restarts the server if it was
        ' already running); StartServer then covers the case where autostart had not
        ' brought it up - and only when the list was actually taken, since starting on
        ' a refused list would serve the old one.
        Dim setResp As WorkerResponse = Await SendAsync(New WorkerRequest With {.type = "SetSharedFolders", .folders = corrected}, 5000)
        If setResp IsNot Nothing AndAlso setResp.Ok Then
            Await SendAsync(New WorkerRequest With {.type = "StartServer"}, 6000)
        End If

        Dim after As WorkerStatus = StatusOf(Await Task.Run(Function() WorkerProcess.TryGetStatus(2000)))
        Return If(after, status)
    End Function

    ''' <summary>
    ''' What the resume reconcile pushes for one root: the worker's read-only, TIGHTENED by
    ''' a local hard read-only - never loosened (ticket 054 H-18). The local params are this
    ''' account's registry, and they can be missing or unreadable - a second enrolled account
    ''' on a Server machine, a reset profile, a blob that failed to parse - and "missing"
    ''' reads as the writable default. Loosening from that silently re-served every read-only
    ''' folder as WRITABLE on the machine-wide server. A folder made writable on purpose is
    ''' pushed by the action that made it so, not by a resume.
    ''' </summary>
    Friend Function ReconciledReadOnly(workerReadOnly As Boolean, local As ShareRootParams) As Boolean
        If workerReadOnly Then Return True
        Return local IsNot Nothing AndAlso Not local.IsWritable()
    End Function

    ''' <summary>Fetches the current worker status, or Nothing if unreachable or refused
    ''' (<see cref="RefusalMessage"/> tells the two apart).</summary>
    Public Async Function GetStatusAsync() As Task(Of WorkerStatus)
        If Not WorkerProcess.IsAvailable() Then Return Nothing
        Return StatusOf(Await Task.Run(Function() WorkerProcess.TryGetStatus(2000)))
    End Function

    ''' <summary>Stops the SFTP server (leaves the worker process running). A session stop:
    ''' the worker's persisted folder list is left as it is, so the next worker start serves
    ''' it again. Removing the last folder is not this - see <see cref="UnshareAllAsync"/>.</summary>
    Public Async Function StopServerAsync() As Task
        Await SendAsync(New WorkerRequest With {.type = "StopServer"}, 4000)
    End Function

    ''' <summary>
    ''' The folder list became EMPTY - the last folder was removed or unticked. The worker
    ''' persists its list (shares.json) and serves it again on every start (logon autostart,
    ''' a service restart, a crash recovery), so a bare StopServer here used to leave the
    ''' folder shared from the next start on, while the user had just confirmed "phones will
    ''' stop seeing it" (ticket 046 H-01). SetSharedFolders applies the persisted selection
    ''' and its server lifecycle atomically on the worker; other owners are retained.
    ''' Never throws; <see cref="ShareResult.ErrorText"/> carries a refusal.
    ''' </summary>
    Public Async Function UnshareAllAsync() As Task(Of ShareResult)
        Dim r As New ShareResult()
        If Not WorkerProcess.IsAvailable() Then Return r
        r.Available = True
        ' Brought up if needed: a worker that is not running now still starts from its
        ' persisted list later, so "nobody to tell" is not "nothing to do".
        Dim ensured As WorkerResponse = Await Task.Run(Function() WorkerProcess.EnsureRunning(6000))
        If ensured Is Nothing Then Return r
        r.Reachable = True
        If ensured.IsRefused OrElse ensured.IsProblem Then
            ' Something holds the pipe and did not give a status we can use - its own sentence,
            ' never "could not reach".
            r.ErrorText = If(ensured.IsRefused, RefusalMessage(ensured.Refusal), ProblemMessage(ensured.Problem))
            Return r
        End If
        r.Status = ensured.Status
        Await UnshareAsync(r, WorkerIpc.PipeName)
        Return r
    End Function

    ''' <summary>The exchanges of <see cref="UnshareAllAsync"/> against an explicit pipe
    ''' (tests drive a private one): persist the empty FMS list, then read live status.
    ''' A separate global StopServer would race a concurrent FileDO registration, so the
    ''' worker's SetSharedFolders lifecycle owns whether the server remains needed.</summary>
    Friend Async Function UnshareAsync(r As ShareResult, pipeName As String) As Task
        Dim setResp As WorkerResponse = Await SendAsync(New WorkerRequest With {.type = "SetSharedFolders", .folders = New List(Of ShareFolder)()}, 5000, pipeName)
        r.ErrorText = RefusedText(setResp)
        Dim cur As WorkerResponse = Await Task.Run(Function() WorkerProcess.TryGetStatus(2000, pipeName))
        If cur IsNot Nothing AndAlso cur.Ok AndAlso cur.Status IsNot Nothing Then r.Status = cur.Status
    End Function

    Friend Function FmsFoldersOnly(folders As IEnumerable(Of ShareFolder)) As List(Of ShareFolder)
        Dim result As New List(Of ShareFolder)()
        If folders Is Nothing Then Return result
        For Each folder As ShareFolder In folders
            If folder IsNot Nothing AndAlso folder.IsFmsOwned Then result.Add(folder)
        Next
        Return result
    End Function

    ''' <summary>
    ''' The last SetNetworkPolicy the worker ANSWERED was refused while LAN only was on - so
    ''' the running worker is not enforcing it: it may still open a UPnP/NAT-PMP hole and
    ''' advertise a WAN path. The privacy-relevant half of WORKER-IPC section 3 rule 2; an
    ''' older worker answers the request type it does not know with <c>ok=false</c>, and that
    ''' used to read exactly like success. A push nobody answered leaves it as it was.
    ''' </summary>
    Public ReadOnly Property LanOnlyNotEnforced As Boolean
        Get
            Return lan_Only_Not_Enforced
        End Get
    End Property

    Private lan_Only_Not_Enforced As Boolean

    ''' <summary>The persistent warning for <see cref="LanOnlyNotEnforced"/>, or "".</summary>
    Public Function PolicyWarning() As String
        If Not lan_Only_Not_Enforced Then Return ""
        Return Localization.T("Работающая служба общего доступа старше этого приложения и не может обеспечить режим «только локальная сеть». Обновите приложение или выключите раздачу.")
    End Function

    ''' <summary>
    ''' Pushes the network policy (max simultaneous connections + LAN-only switch +
    ''' the pinned listen port) from ShareSettings to the worker via SetNetworkPolicy.
    ''' The port rides here rather than on a request type of its own because this is
    ''' already the server-wide-knobs channel, is already pushed before StartServer and
    ''' on the resume path, and already restarts a running server when a knob changes -
    ''' which a port change needs anyway. This is the ENFORCING
    ''' side of "LAN only": it stops the worker opening any UPnP/NAT-PMP hole and
    ''' advertising a WAN path, not just stripping the exported config. An older worker
    ''' answers the unknown request type with <c>ok=false</c> and keeps its persisted/default
    ''' policy - which is read here and, with LAN only on, turned into
    ''' <see cref="PolicyWarning"/>. Call whenever these settings change while a share is
    ''' live, and it is already folded into the share-start and resume flows. Returns the
    ''' worker's answer (Nothing when nobody answered).
    ''' </summary>
    Public Function PushNetworkPolicyAsync() As Task(Of WorkerResponse)
        Return PushNetworkPolicyAsync(WorkerIpc.PipeName)
    End Function

    Friend Async Function PushNetworkPolicyAsync(pipeName As String) As Task(Of WorkerResponse)
        Dim s As New ShareSettings()
        s.Load()
        ' LanOnly - the SERVER-WIDE enforcement - and never the export-shaping
        ' LanOnlyExport: the two were one flag until ticket 054 H-06 split them, and the
        ' per-code checkbox used to reach the worker here, silently re-mapping (or
        ' un-mapping) the router hole for the whole PC at the next resume.
        Dim req As WorkerRequest = NetworkPolicyRequest(s.MaxConnections, s.LanOnly, s.ListenPort)
        Dim resp As WorkerResponse = Await SendAsync(req, 5000, pipeName)
        lan_Only_Not_Enforced = LanOnlyNotEnforcedAfter(resp, s.LanOnly, lan_Only_Not_Enforced)
        Return resp
    End Function

    ''' <summary>The SetNetworkPolicy request for the recorded settings. The port is OMITTED
    ''' when none is recorded here (WORKER-IPC rule 4: absent is unchanged, never 0).</summary>
    Friend Function NetworkPolicyRequest(maxConnections As Integer, lanOnly As Boolean, listenPort As Integer) As WorkerRequest
        Dim port As Integer = ShareSettings.ClampPort(listenPort)
        Return New WorkerRequest With {
            .type = "SetNetworkPolicy",
            .maxConnections = ShareSettings.ClampConnections(maxConnections),
            .lanOnly = lanOnly,
            .port = If(port = ShareSettings.UnsetPort, CType(Nothing, Integer?), port)
        }
    End Function

    ''' <summary>The verdict on one policy push: a worker that answered and did not accept it
    ''' leaves LAN only unenforced; an accepted push, or LAN only off, clears the warning; a
    ''' push nobody answered teaches nothing and keeps <paramref name="previous"/>.</summary>
    Friend Function LanOnlyNotEnforcedAfter(resp As WorkerResponse, lanOnly As Boolean, previous As Boolean) As Boolean
        If resp Is Nothing Then Return previous
        Return lanOnly AndAlso Not resp.Ok
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
            ' The worker records EVERY failed start as "port N unavailable: ..", a refused
            ' folder list included. Only a refused bind is a port problem: "choose another
            ' port and change it in the router" is the wrong advice for anything else, and
            ' following it invalidates every code already handed out (ticket 054 H-03).
            If Not IsBindFailure(status.LastStartError) Then Return WorkerSaidNoText(WithoutPortPrefix(status.LastStartError))
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
        If want = ShareSettings.UnsetPort OrElse status.ListenPort = want Then Return ""
        ' Serving on a number nobody asked for. The capability field decides what that
        ' means (WORKER-IPC section 6 rule 1), never the mismatch alone: a worker that
        ' understands the port field and still serves on the old one has simply not rebound
        ' yet - the setting just changed and the restart is pending - which is not a stale
        ' worker and needs no "update the app".
        If status.PortSupported Then Return ""
        Return ShareText.PortMismatchText(want, status.ListenPort)
    End Function

    ''' <summary>A start error that is a refused BIND: Go's socket error names the "bind" call
    ''' (".. listen tcp :2222: bind: .."), and the excluded-range marker is the worker's own.
    ''' Both are ASCII tokens outside the localized OS text. A structured error kind is asked of
    ''' the worker's owner (ticket 054 section 6); until then this is the one reading.</summary>
    Friend Function IsBindFailure(lastStartError As String) As Boolean
        If String.IsNullOrEmpty(lastStartError) Then Return False
        Return lastStartError.IndexOf("bind:", StringComparison.Ordinal) >= 0 OrElse
               lastStartError.IndexOf(ExcludedRangeMarker, StringComparison.Ordinal) >= 0
    End Function

    ''' <summary>The worker's reason without its misleading "port N unavailable: " lead-in.</summary>
    Friend Function WithoutPortPrefix(lastStartError As String) As String
        Dim m As System.Text.RegularExpressions.Match =
            System.Text.RegularExpressions.Regex.Match(If(lastStartError, ""), "^port \d+ unavailable[^:]*:\s*")
        Return If(m.Success, lastStartError.Substring(m.Length), If(lastStartError, ""))
    End Function

    ''' <summary>Resets the worker's local usage counters (stats.json). Returns the worker's
    ''' answer (Nothing when nobody answered); only an answer with <c>ok</c> reset anything -
    ''' an older worker answers the unknown type with <c>ok=false</c> and a status whose
    ''' counters are the ones it still has.</summary>
    Public Function ResetStatsAsync() As Task(Of WorkerResponse)
        Return ResetStatsAsync(WorkerIpc.PipeName)
    End Function

    Friend Function ResetStatsAsync(pipeName As String) As Task(Of WorkerResponse)
        Return SendAsync(New WorkerRequest With {.type = "ResetStats"}, 4000, pipeName)
    End Function

    ''' <summary>"" after a reset the worker accepted; otherwise the sentence the reset button
    ''' shows. A reset nobody answered did not happen either.</summary>
    Public Function ResetFailureText(resp As WorkerResponse) As String
        If resp IsNot Nothing AndAlso resp.Ok Then Return ""
        If resp IsNot Nothing AndAlso resp.IsRefused Then Return RefusalMessage(resp.Refusal)
        If resp IsNot Nothing AndAlso resp.IsProblem Then Return ProblemMessage(resp.Problem)
        Dim why As String = If(resp IsNot Nothing, resp.Error, Nothing)
        If String.IsNullOrEmpty(why) Then Return Localization.T("Не удалось сбросить счётчики.")
        Return Localization.TF("Не удалось сбросить счётчики: {0}", why)
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

    ''' <summary>One exchange off the UI thread. Transport failures come back as Nothing; a
    ''' version refusal comes back as a refused response, never as an exception into UI
    ''' code (<see cref="WorkerProcess.Exchange"/>). The caller reads <c>Ok</c>.</summary>
    Private Function SendAsync(req As WorkerRequest, timeoutMs As Integer) As Task(Of WorkerResponse)
        Return SendAsync(req, timeoutMs, WorkerIpc.PipeName)
    End Function

    Private Function SendAsync(req As WorkerRequest, timeoutMs As Integer, pipeName As String) As Task(Of WorkerResponse)
        Return Task.Run(Function() WorkerProcess.Exchange(req, timeoutMs, pipeName))
    End Function

End Module
