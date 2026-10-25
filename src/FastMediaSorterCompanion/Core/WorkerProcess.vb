Option Strict On

Imports System.IO
Imports System.Threading
Imports System.Windows.Forms

''' <summary>
''' Locates, spawns and stops the bundled Android-share sidecar. The worker exe
''' is a sibling payload resolved relative to the running app exe:
''' &lt;app dir&gt;\companion\fms-share-worker.exe in every distribution channel.
''' Never touches the worker's data dir (host key is TOFU-pinned by phones).
''' Spawn pattern mirrors Translate\OllamaManager.vb (hidden, no window).
''' </summary>
Public Module WorkerProcess

    ''' <summary>Payload exe file name.</summary>
    Public Const ExeName As String = "fms-share-worker.exe"

    ''' <summary>Process name for Process.GetProcessesByName (no extension).</summary>
    Public Const ProcessName As String = "fms-share-worker"

    ''' <summary>Tests point the module at a scratch folder that holds a stand-in worker exe, so a window built for the SFTP
    ''' component can be opened without the real payload. Nothing else sets it.</summary>
    Friend ExeDirectoryOverride As String

    ''' <summary>
    ''' Absolute path of the sibling worker exe, or "" if it cannot be resolved.
    ''' Resolved from Application.ExecutablePath only (Invariant 2).
    ''' </summary>
    Public Function WorkerExePath() As String
        If Not String.IsNullOrEmpty(ExeDirectoryOverride) Then Return Path.Combine(ExeDirectoryOverride, ExeName)
        Try
            Dim baseDir As String = Path.GetDirectoryName(Application.ExecutablePath)
            If String.IsNullOrEmpty(baseDir) Then Return ""
            Return Path.Combine(baseDir, "companion", ExeName)
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>True when the worker exe is present next to the app.</summary>
    Public Function IsAvailable() As Boolean
        Dim p As String = WorkerExePath()
        Return p.Length > 0 AndAlso File.Exists(p)
    End Function

    ''' <summary>
    ''' Ensures the worker is listening: connects if it already runs, otherwise
    ''' spawns it hidden and polls the pipe up to totalWaitMs. Returns the worker
    ''' status (from GetStatus), or Nothing if it could not be reached.
    ''' </summary>
    ''' <summary>
    ''' How long after a failed bring-up a repeat call is answered from memory instead of
    ''' launching another process. Deliberately SHORT: every current caller is an explicit
    ''' user action (open the window, share a folder, resume from the tray), and making a
    ''' user wait out a long backoff after they read the error and clicked again would be
    ''' worse than the waste it prevents. This only swallows the burst - a double click, a
    ''' window reopened at once, or a future caller that ends up on a timer.
    ''' </summary>
    Private Const Spawn_Backoff_Ms As Integer = 5000

    Private spawn_Failed_At As DateTime = DateTime.MinValue

    ''' <summary>
    ''' How long a Server edition console waits for the service worker to answer the
    ''' pipe. Longer than the interactive spawn wait on purpose: the SCM may still be
    ''' bringing a delayed-automatic service up (or restarting it after a recovery
    ''' action) and the console has no way to hurry it along - waiting is the entire
    ''' repertoire. Failing early here would show "unavailable" on a service that is
    ''' seconds from Running.
    ''' </summary>
    Private Const Service_Connect_Wait_Ms As Integer = 15000

    Public Function EnsureRunning(Optional totalWaitMs As Integer = 5000) As WorkerResponse
        Return EnsureRunning(totalWaitMs, WorkerIpc.PipeName, AddressOf SpawnWorker)
    End Function

    ''' <summary>
    ''' The bring-up against an explicit pipe, with the launch passed in - so a test can drive
    ''' a private pipe and observe that nothing is launched (the frozen pipe is a machine-wide
    ''' singleton; see WorkerIpc.Send). <paramref name="startWorker"/> returns False when the
    ''' launch itself failed.
    '''
    ''' ANY answer means a worker is running - including a version refusal (WORKER-IPC
    ''' section 6 rule 3), an <c>ok=false</c>, and every <see cref="WorkerExchangeProblem"/>
    ''' (an unreadable answer, a pipe this account may not open, a pipe held by a process we
    ''' do not trust). Whoever holds the frozen pipe, a second worker would only lose the race
    ''' for it. The refusal used to be swallowed as "nobody answered", and this function then
    ''' went on to spawn.
    ''' </summary>
    Friend Function EnsureRunning(totalWaitMs As Integer, pipeName As String, startWorker As Func(Of Boolean)) As WorkerResponse
        ' Fast path: a worker is already listening.
        Dim status As WorkerResponse = TryGetStatus(400, pipeName)
        If status IsNot Nothing Then Return status

        ' The service suppresses the foreground worker. Two conditions, because they
        ' fail differently:
        '   * the registration is ours -> this is a Server edition install and the
        '     console is a console, full stop;
        '   * the service is merely RUNNING (or starting) -> whatever registered it,
        '     it already owns the frozen pipe, the listen port and the host key, so a
        '     second worker would only lose the race and report sharing as broken.
        ' Either way: connect, never spawn (spec §1.4, §3.6 - the two hosts are
        ' interchangeable, never concurrent).
        If ServerFeatures.IsSystemServiceHost() OrElse ServiceControl.IsServiceServing() Then
            Return WaitForServiceWorker(pipeName)
        End If

        If Not IsAvailable() Then Return Nothing

        If spawn_Failed_At <> DateTime.MinValue AndAlso
           (DateTime.UtcNow - spawn_Failed_At).TotalMilliseconds < Spawn_Backoff_Ms Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " companion: spawn backoff - not retrying yet")
            Return Nothing
        End If

        If Not startWorker() Then
            spawn_Failed_At = DateTime.UtcNow
            Return Nothing
        End If

        Dim sw As Stopwatch = Stopwatch.StartNew()
        Do While sw.ElapsedMilliseconds < totalWaitMs
            Thread.Sleep(200)
            status = TryGetStatus(400, pipeName)
            If status IsNot Nothing Then
                spawn_Failed_At = DateTime.MinValue
                Return status
            End If
        Loop

        ' It started but never answered the pipe - just as bad as a failed launch, and the
        ' same backoff applies.
        spawn_Failed_At = DateTime.UtcNow
        Return Nothing
    End Function

    ''' <summary>
    ''' The command line of a worker this host starts (WORKER-LAUNCH 1.0 section 1): the state
    ''' directory the host itself resolved (<see cref="WorkerStateDir.Resolve"/> - the same
    ''' value the Hosting page reports and the viewer reads), passed explicitly as
    ''' <c>--datadir</c> so the worker's directory is what the host says it is and stays so for the
    ''' worker's life. It is the directory the worker would pick on its own (the order is the
    ''' worker's), so nothing moves for an ordinary install; with no resolvable directory no
    ''' argument is passed and the worker resolves for itself. One argument per item: a path with
    ''' spaces stays a single value.
    ''' </summary>
    Friend Function SpawnArguments(stateDir As String) As List(Of String)
        Dim args As New List(Of String)()
        If Not String.IsNullOrEmpty(stateDir) Then
            args.Add("--datadir")
            args.Add(stateDir)
        End If
        Return args
    End Function

    Friend Function SpawnStartInfo(exePath As String, stateDir As String) As ProcessStartInfo
        Dim psi As New ProcessStartInfo(exePath) With {
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .WindowStyle = ProcessWindowStyle.Hidden}
        For Each arg As String In SpawnArguments(stateDir)
            psi.ArgumentList.Add(arg)
        Next
        Return psi
    End Function

    ''' <summary>Launches the bundled worker hidden. False when the launch itself failed.
    ''' Only ever reached when nothing answered the pipe (see <see cref="EnsureRunning"/>), so the
    ''' <c>--datadir</c> it passes belongs to the one worker that is about to own the pipe.</summary>
    Private Function SpawnWorker() As Boolean
        Try
            ' Using: we never wait on the object itself (readiness is decided by the pipe
            ' poll), and a discarded Process holds its handle until finalization.
            Using spawned As Process = Process.Start(SpawnStartInfo(WorkerExePath(), WorkerStateDir.Resolve()))
            End Using
            StateLog.EventOccurred("worker-spawned")
            Return True
        Catch ex As Exception
            AppFileLogger.LogException("Worker: spawn failed", ex)
            Return False
        End Try
    End Function

    ''' <summary>Polls the control pipe while the service comes up. Returns the status
    ''' once the service worker answers, or Nothing when it never does - which in
    ''' Server mode means the service is stopped, broken or not ours, and the UI must
    ''' offer the administrative controls rather than a "start sharing" that cannot
    ''' work.</summary>
    Private Function WaitForServiceWorker(pipeName As String) As WorkerResponse
        Dim sw As Stopwatch = Stopwatch.StartNew()
        Do
            Dim status As WorkerResponse = TryGetStatus(1000, pipeName)
            If status IsNot Nothing Then Return status
            If sw.ElapsedMilliseconds >= Service_Connect_Wait_Ms Then Return Nothing
            Thread.Sleep(500)
        Loop
    End Function

    ''' <summary>GetStatus with transport failures swallowed (returns Nothing). A version
    ''' refusal is NOT a transport failure and comes back as a refused response
    ''' (<see cref="WorkerResponse.IsRefused"/>).</summary>
    Public Function TryGetStatus(Optional connectTimeoutMs As Integer = 1000) As WorkerResponse
        Return TryGetStatus(connectTimeoutMs, WorkerIpc.PipeName)
    End Function

    Friend Function TryGetStatus(connectTimeoutMs As Integer, pipeName As String) As WorkerResponse
        Return Exchange(New WorkerRequest With {.type = "GetStatus"}, connectTimeoutMs, pipeName)
    End Function

    ''' <summary>
    ''' The most recent version refusal (WORKER-IPC section 6 rule 3), or Nothing once any
    ''' later exchange was understood or nobody answered at all. The pipe is a machine-wide
    ''' singleton, so this is a fact about THE worker, not about one call - which is what lets
    ''' a screen that only holds a Nothing status still say why.
    ''' </summary>
    Public ReadOnly Property LastRefusal As WorkerVersionRefusedException
        Get
            Return last_Refusal
        End Get
    End Property

    Private last_Refusal As WorkerVersionRefusedException

    ''' <summary>The problem of the most recent exchange (<see cref="WorkerExchangeProblem"/>),
    ''' None once one was understood, refused or met silence. Machine-wide like
    ''' <see cref="LastRefusal"/>, and for the same reason: a screen holding only a Nothing
    ''' status can still say why.</summary>
    Public ReadOnly Property LastProblem As WorkerExchangeProblem
        Get
            Return last_Problem
        End Get
    End Property

    Private last_Problem As WorkerExchangeProblem = WorkerExchangeProblem.None

    ''' <summary>
    ''' One exchange, with the outcomes kept apart: a response (read <c>Ok</c> on it), a
    ''' refused response (<see cref="WorkerResponse.IsRefused"/> - a worker answered, with a
    ''' newer protocol), a response carrying a problem (<see cref="WorkerResponse.IsProblem"/>
    ''' - something holds the pipe: an answer we cannot read, an account we are not, a process
    ''' that is not our worker), or Nothing (transport failure: nobody answered in time).
    ''' Never throws. Every product exchange goes through here, so <see cref="LastRefusal"/>
    ''' and <see cref="LastProblem"/> are always current.
    ''' </summary>
    Friend Function Exchange(request As WorkerRequest, timeoutMs As Integer, pipeName As String) As WorkerResponse
        Try
            Dim resp As WorkerResponse = WorkerIpc.Send(request, timeoutMs, pipeName)
            If String.Equals(pipeName, WorkerIpc.PipeName, StringComparison.Ordinal) Then
                StateLog.CommandCompleted(request.type, resp.Ok)
                If resp.Ok Then StateLog.Observe(resp.Status)
            End If
            last_Refusal = Nothing
            last_Problem = WorkerExchangeProblem.None
            Return resp
        Catch refusal As WorkerVersionRefusedException
            last_Refusal = refusal
            last_Problem = WorkerExchangeProblem.None
            Return WorkerResponse.Refused(refusal)
        Catch ex As Exception
            last_Refusal = Nothing
            Dim problem As WorkerExchangeProblem = ClassifyFailure(ex)
            last_Problem = problem
            If problem = WorkerExchangeProblem.None Then Return Nothing
            AppFileLogger.LogException("Worker: " & problem.ToString(), ex)
            Return WorkerResponse.WithProblem(problem)
        End Try
    End Function

    Private ReadOnly StateLog As New ShareStateLog(AddressOf AppFileLogger.WriteLine)

    ''' <summary>
    ''' Which failures mean "something holds the pipe" rather than "nobody answered". An
    ''' access denial on connect is the pipe DACL of a worker that belongs to another
    ''' account (the owner plus the enrolled management SIDs): a second account's console
    ''' used to read it as "not running", spawn a worker that could only lose the pipe, or
    ''' wait out the service and then say "the service is running" with no hint that this
    ''' account is simply not enrolled (ticket 054 H-26).
    ''' </summary>
    Friend Function ClassifyFailure(ex As Exception) As WorkerExchangeProblem
        Dim typed As WorkerExchangeException = TryCast(ex, WorkerExchangeException)
        If typed IsNot Nothing Then Return typed.Problem
        If TypeOf ex Is UnauthorizedAccessException Then Return WorkerExchangeProblem.NotAuthorized
        Return WorkerExchangeProblem.None
    End Function

    ' StopWorker - the "hard stop" that sent StopServer to whoever held the pipe and then
    ' killed every fms-share-worker process by NAME, in any session this account could open -
    ' was removed (ticket 054 H-24): it had no caller, and its shape was the danger. Stopping
    ' sharing is StopServer over IPC; stopping the SERVICE is ServiceControl.Manage.

End Module
