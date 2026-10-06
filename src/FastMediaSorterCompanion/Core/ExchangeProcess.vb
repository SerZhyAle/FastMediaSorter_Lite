Option Strict On

Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Locates, runs and stops the bundled exchange server (ticket 142, 133 section 5): the console program
''' <c>exchange\fms-exchange-server.exe</c> beside the Share Manager.
'''
''' It differs from <see cref="WorkerProcess"/> on purpose. The worker is a background sidecar that
''' outlives the Share Manager; the exchange server is a console program started for the user, on a click,
''' and stopped on request:
'''   * nothing starts it but <see cref="Start"/> (no autostart, 133 section 3);
'''   * it is stopped by a STOP REQUEST - the binary's own <c>stop</c> subcommand signals the named event
'''     the running console instance waits on, so registrations close cleanly and the server logs its stop
'''     line - and only after 5 s without an exit is it killed;
'''   * the Share Manager stops a server it started itself when it exits, unless
'''     <see cref="ExchangeSettings.KeepRunningOnExit"/> is on (<see cref="StopStartedOnExit"/>);
'''   * the password goes to the binary over STDIN and never appears on a command line, in the registry or
'''     in any log; a generated one is read once from the binary's stdout and handed to the caller, which
'''     shows it once and drops it.
'''
''' The binary owns the password policy, its generation and its hashing, and the config file; this module
''' only drives it. Everything the UI needs to know about the server's state comes from <c>inspect</c>
''' (JSON on stdout), the process list and, for the service, <see cref="ExchangeService"/> - the server
''' keeps no session list and exposes none (133 section 5.3).
''' </summary>
Friend Module ExchangeProcess

    ''' <summary>Payload exe file name.</summary>
    Friend Const ExeName As String = "fms-exchange-server.exe"

    ''' <summary>Process name for Process.GetProcessesByName (no extension).</summary>
    Friend Const ProcessName As String = "fms-exchange-server"

    ''' <summary>The folder beside the Share Manager that holds the exe and its two helpers.</summary>
    Friend Const FolderName As String = "exchange"

    ''' <summary>How long a start waits for an immediate refusal (no password, bad config, port taken, the
    ''' service is running) before it reports "started". The server answers all of those in well under a
    ''' second; a healthy one is still running when the wait ends.</summary>
    Friend Const StartupGraceMs As Integer = 1500

    ''' <summary>How long a stop request may take before the process is killed (ticket 142: 5 s).</summary>
    Friend Const StopWaitMs As Integer = 5000

    ''' <summary>A one-shot subcommand (inspect, set-password) is quick; this bounds a hung one.</summary>
    Friend Const CommandTimeoutMs As Integer = 20000

    ' The binary's exit codes (cmd/fms-exchange-server/main.go).
    Friend Const ExitOk As Integer = 0
    Friend Const ExitFailure As Integer = 1
    Friend Const ExitUsage As Integer = 2
    Friend Const ExitPolicy As Integer = 3
    Friend Const ExitConfig As Integer = 4
    Friend Const ExitBesideService As Integer = 5

    ''' <summary>The data-directory override the server itself honours (config.EnvDataDir).</summary>
    Friend Const DataDirEnvironmentName As String = "FMS_EXCHANGE_DATA_DIR"

    ' --- test seams: nothing here is reachable by a user --------------------------------

    ''' <summary>Tests point the module at a scratch folder holding a copy of the binary. Nothing else sets it.</summary>
    Friend ExeDirectoryOverride As String

    ''' <summary>Tests (and only tests) run the real binary against a scratch data directory.</summary>
    Friend DataDirOverride As String

    ''' <summary>One run of the binary: the exit code and what it wrote.</summary>
    Friend Structure RunResult
        Public Started As Boolean
        Public TimedOut As Boolean
        Public ExitCode As Integer
        Public StdOut As String
        Public StdErr As String
    End Structure

    ''' <summary>Tests replace the process launch with a function over (arguments, stdin text). Nothing
    ''' else sets it; the default launches the real binary.</summary>
    Friend RunOverride As Func(Of String(), String, RunResult)

    ' --- location -----------------------------------------------------------------------

    ''' <summary>&lt;app dir&gt;\exchange, or "" when it cannot be resolved.</summary>
    Friend Function ExeDirectory() As String
        If Not String.IsNullOrEmpty(ExeDirectoryOverride) Then Return ExeDirectoryOverride
        Try
            Dim baseDir As String = Path.GetDirectoryName(Application.ExecutablePath)
            If String.IsNullOrEmpty(baseDir) Then Return ""
            Return Path.Combine(baseDir, FolderName)
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>Absolute path of the exe, or "" if it cannot be resolved.</summary>
    Friend Function ExePath() As String
        Dim dir As String = ExeDirectory()
        If dir.Length = 0 Then Return ""
        Return Path.Combine(dir, ExeName)
    End Function

    ''' <summary>True when the component is installed. Everything about the exchange server in the
    ''' Share Manager - the settings group, the tray item - exists only while this is true: without the
    ''' component the program is exactly what it was.</summary>
    Friend Function IsAvailable() As Boolean
        Dim p As String = ExePath()
        Return p.Length > 0 AndAlso File.Exists(p)
    End Function

    ' --- what the binary reports ---------------------------------------------------------

    ''' <summary>The parsed <c>inspect</c> report. <see cref="Answered"/> False means the binary could not be
    ''' asked or printed no JSON - never "no password"; callers keep the two apart.</summary>
    Friend NotInheritable Class InspectReport
        Public Property Answered As Boolean
        Public Property ExitCode As Integer
        Public Property DataDir As String = ""
        Public Property ConfigPresent As Boolean
        Public Property PasswordSet As Boolean
        Public Property PasswordHashValid As Boolean
        Public Property CertPresent As Boolean
        Public Property Fingerprint As String = ""
        Public Property Listen As String = ""
        Public Property PortRange As String = ""
        Public Property ErrorText As String = ""

        ''' <summary>The listen port from the endpoint ("" host or not), or 0 when it has none.</summary>
        Public ReadOnly Property Port As Integer
            Get
                Return ExchangeProcess.PortOfListen(Listen)
            End Get
        End Property

        ''' <summary>A password is set and the record parses: the server can start.</summary>
        Public ReadOnly Property Configured As Boolean
            Get
                Return PasswordSet AndAlso PasswordHashValid
            End Get
        End Property

        ''' <summary>&lt;DataDir&gt;\exchange.json - the file the binary reads and writes.</summary>
        Public ReadOnly Property ConfigPath As String
            Get
                Return If(DataDir.Length = 0, "", Path.Combine(DataDir, "exchange.json"))
            End Get
        End Property
    End Class

    ''' <summary>Parses the JSON <c>inspect</c> prints, even when it exits non-zero (it reports a problem
    ''' in its <c>error</c> member and still prints). Unknown members are ignored, a missing one is the
    ''' zero value, and text that is not a JSON object is "not answered".</summary>
    Friend Function ParseInspect(json As String, exitCode As Integer) As InspectReport
        Dim report As New InspectReport With {.ExitCode = exitCode}
        If String.IsNullOrWhiteSpace(json) Then Return report
        Try
            Using doc As JsonDocument = JsonDocument.Parse(json)
                Dim root As JsonElement = doc.RootElement
                If root.ValueKind <> JsonValueKind.Object Then Return report
                report.DataDir = ReadString(root, "dataDir")
                report.ConfigPresent = ReadBool(root, "configPresent")
                report.PasswordSet = ReadBool(root, "passwordSet")
                report.PasswordHashValid = ReadBool(root, "passwordHashValid")
                report.CertPresent = ReadBool(root, "certPresent")
                report.Fingerprint = ReadString(root, "fingerprint")
                report.Listen = ReadString(root, "listen")
                report.PortRange = ReadString(root, "portRange")
                report.ErrorText = ReadString(root, "error")
                report.Answered = True
            End Using
        Catch ex As JsonException
            report.Answered = False
        End Try
        Return report
    End Function

    Private Function ReadString(root As JsonElement, name As String) As String
        Dim v As JsonElement = Nothing
        If root.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String Then Return If(v.GetString(), "")
        Return ""
    End Function

    Private Function ReadBool(root As JsonElement, name As String) As Boolean
        Dim v As JsonElement = Nothing
        Return root.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.True
    End Function

    ''' <summary>The port of a listen endpoint (":22022", "127.0.0.1:22022", "[::1]:22022"), or 0.</summary>
    Friend Function PortOfListen(listen As String) As Integer
        If String.IsNullOrEmpty(listen) Then Return 0
        Dim colon As Integer = listen.LastIndexOf(":"c)
        If colon < 0 Then Return 0
        Dim n As Integer
        If Integer.TryParse(listen.Substring(colon + 1), Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, n) AndAlso n >= 0 AndAlso n <= 65535 Then Return n
        Return 0
    End Function

    ''' <summary>Asks the binary what it holds (read-only: creates nothing).</summary>
    Friend Function Inspect() As InspectReport
        If Not IsAvailable() Then Return New InspectReport()
        Dim r As RunResult = Run({"inspect"}, Nothing, CommandTimeoutMs)
        If Not r.Started OrElse r.TimedOut Then Return New InspectReport With {.ExitCode = r.ExitCode}
        Return ParseInspect(r.StdOut, r.ExitCode)
    End Function


    ' --- the listen port --------------------------------------------------------------------

    Friend Enum ListenOutcome
        Saved
        ''' <summary>Exit 2: the binary refused the address (it validates with the serve rules).</summary>
        BadAddress
        ''' <summary>Exit 4: the config cannot be read or written.</summary>
        ConfigProblem
        Failed
        Unavailable
    End Enum

    ''' <summary>The endpoint with its port replaced and its host kept: ":22022" -> ":2222",
    ''' "127.0.0.1:22022" -> "127.0.0.1:2222", "[::1]:22022" -> "[::1]:2222". An endpoint with no colon
    ''' (or an empty one) becomes ":port".</summary>
    Friend Function WithPort(listen As String, port As Integer) As String
        Dim p As String = port.ToString(Globalization.CultureInfo.InvariantCulture)
        If String.IsNullOrEmpty(listen) Then Return ":" & p
        Dim colon As Integer = listen.LastIndexOf(":"c)
        If colon < 0 Then Return ":" & p
        Return listen.Substring(0, colon + 1) & p
    End Function

    ''' <summary>Writes the listen endpoint THROUGH the binary (<c>set-listen</c>): it validates with the serve
    ''' rules, stores only <c>listen</c> and keeps every other member. This program never edits
    ''' exchange.json itself. The change reaches a running server only after a restart.</summary>
    Friend Function SetListen(listen As String) As ListenOutcome
        If Not IsAvailable() Then Return ListenOutcome.Unavailable
        Dim r As RunResult = Run({"set-listen", If(listen, "")}, Nothing, CommandTimeoutMs)
        Log("set-listen", r)
        If Not r.Started OrElse r.TimedOut Then Return ListenOutcome.Failed
        Select Case r.ExitCode
            Case ExitOk : Return ListenOutcome.Saved
            Case ExitUsage : Return ListenOutcome.BadAddress
            Case ExitConfig : Return ListenOutcome.ConfigProblem
            Case Else : Return ListenOutcome.Failed
        End Select
    End Function

    ' --- passwords -----------------------------------------------------------------------

    Friend Enum PasswordOutcome
        Saved
        ''' <summary>The binary refused the password (exit 3): the policy floor.</summary>
        PolicyRefused
        ''' <summary>The config could not be read or written (exit 4).</summary>
        ConfigProblem
        Failed
        Unavailable
    End Enum

    ''' <summary>The outcome of a set / generate, with the generated plaintext when there is one. The plaintext
    ''' is the ONLY place it lives; the caller shows it once and lets go of it.</summary>
    Friend NotInheritable Class PasswordResult
        Public Property Outcome As PasswordOutcome
        Public Property ExitCode As Integer
        ''' <summary>Set only by <see cref="GeneratePassword"/> on success.</summary>
        Public Property Plaintext As String = ""
    End Class

    ''' <summary>Stores the Argon2id record of <paramref name="plaintext"/> in the config (the binary hashes and
    ''' writes; this only feeds stdin). The password never reaches a command line, a log or the registry.</summary>
    Friend Function SetPassword(plaintext As String) As PasswordResult
        If Not IsAvailable() Then Return New PasswordResult With {.Outcome = PasswordOutcome.Unavailable}
        Dim r As RunResult = Run({"set-password"}, If(plaintext, ""), CommandTimeoutMs)
        Log("set-password", r)
        Return New PasswordResult With {.Outcome = OutcomeOf(r), .ExitCode = r.ExitCode}
    End Function

    ''' <summary>Makes the binary generate a password, store its hash and print the plaintext ONCE. The text is
    ''' returned to the caller and logged nowhere. A reply that is not one well-formed password is a failure,
    ''' never something to show.</summary>
    Friend Function GeneratePassword() As PasswordResult
        If Not IsAvailable() Then Return New PasswordResult With {.Outcome = PasswordOutcome.Unavailable}
        Dim r As RunResult = Run({"set-password", "--generate"}, Nothing, CommandTimeoutMs)
        Log("set-password --generate", r)
        Dim result As New PasswordResult With {.Outcome = OutcomeOf(r), .ExitCode = r.ExitCode}
        If result.Outcome <> PasswordOutcome.Saved Then Return result
        Dim text As String = If(r.StdOut, "").Trim()
        If text.Length = 0 OrElse text.IndexOfAny({ChrW(10), ChrW(13)}) >= 0 OrElse Not ExchangePasswordPolicy.IsAccepted(text) Then
            result.Outcome = PasswordOutcome.Failed
            Return result
        End If
        result.Plaintext = text
        Return result
    End Function

    Private Function OutcomeOf(r As RunResult) As PasswordOutcome
        If Not r.Started OrElse r.TimedOut Then Return PasswordOutcome.Failed
        Select Case r.ExitCode
            Case ExitOk : Return PasswordOutcome.Saved
            Case ExitPolicy : Return PasswordOutcome.PolicyRefused
            Case ExitConfig : Return PasswordOutcome.ConfigProblem
            Case Else : Return PasswordOutcome.Failed
        End Select
    End Function

    ' --- start / stop ---------------------------------------------------------------------

    Friend Enum StartOutcome
        Started
        AlreadyRunning
        ''' <summary>Exit 3: no password, or a record that does not parse.</summary>
        NoPassword
        ''' <summary>Exit 4: the config cannot be read or is not valid.</summary>
        ConfigProblem
        ''' <summary>Exit 5: the service runs - the service wins (133 section 6.1).</summary>
        BesideService
        ''' <summary>Any other early exit - typically the listen port is taken.</summary>
        Failed
        Unavailable
    End Enum

    Private ReadOnly Gate As New Object()

    ''' <summary>The server THIS Share Manager session started and has not seen exit. Servers it merely
    ''' found already running are never stopped on exit.</summary>
    Private _started As Process

    ''' <summary>Raised on a thread-pool thread when the server this session started exits by itself or is
    ''' stopped; the group refreshes on it. Never raised for a server it did not start.</summary>
    Friend Event ConsoleExited As EventHandler

    ''' <summary>Starts <c>serve</c> hidden. Waits <see cref="StartupGraceMs"/> for an immediate refusal and maps
    ''' the exit code; a server still running after that is "started". Never asks for elevation.</summary>
    Friend Function Start() As StartOutcome
        If Not IsAvailable() Then Return StartOutcome.Unavailable
        If FindRunning().Length > 0 Then Return StartOutcome.AlreadyRunning

        Dim psi As New ProcessStartInfo(ExePath()) With {
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .WindowStyle = ProcessWindowStyle.Hidden,
            .WorkingDirectory = ExeDirectory()}
        psi.ArgumentList.Add("serve")
        ApplyEnvironment(psi)

        Dim p As Process
        Try
            p = Process.Start(psi)
        Catch ex As Exception
            AppFileLogger.LogException("Exchange server: start", ex)
            Return StartOutcome.Failed
        End Try
        If p Is Nothing Then Return StartOutcome.Failed

        If p.WaitForExit(StartupGraceMs) Then
            Dim code As Integer = p.ExitCode
            p.Dispose()
            AppFileLogger.WriteLine("Exchange server: serve exited at once with code " & code.ToString(Globalization.CultureInfo.InvariantCulture))
            Select Case code
                Case ExitPolicy : Return StartOutcome.NoPassword
                Case ExitConfig : Return StartOutcome.ConfigProblem
                Case ExitBesideService : Return StartOutcome.BesideService
                Case Else : Return StartOutcome.Failed
            End Select
        End If

        SyncLock Gate
            _started = p
        End SyncLock
        p.EnableRaisingEvents = True
        AddHandler p.Exited, AddressOf OnStartedProcessExited
        AppFileLogger.WriteLine("Exchange server: started, pid " & p.Id.ToString(Globalization.CultureInfo.InvariantCulture))
        Return StartOutcome.Started
    End Function

    Private Sub OnStartedProcessExited(sender As Object, e As EventArgs)
        SyncLock Gate
            If _started IsNot Nothing AndAlso Object.ReferenceEquals(_started, sender) Then
                Try : _started.Dispose() : Catch : End Try
                _started = Nothing
            End If
        End SyncLock
        RaiseEvent ConsoleExited(Nothing, EventArgs.Empty)
    End Sub

    Friend Enum StopOutcome
        NotRunning
        ''' <summary>The stop request was honoured within the wait.</summary>
        Stopped
        ''' <summary>No exit within 5 s (or the binary does not know the stop command): killed.</summary>
        Killed
        Failed
    End Enum

    ''' <summary>The running console server (this session's or one found by exe path), or an empty array.
    ''' A process whose path cannot be read (another account's, the service's) is not ours.</summary>
    Friend Function FindRunning() As Process()
        Dim mine As String = ExePath()
        If mine.Length = 0 Then Return Array.Empty(Of Process)()
        Dim wanted As String
        Try
            wanted = Path.GetFullPath(mine)
        Catch
            Return Array.Empty(Of Process)()
        End Try
        Dim found As New List(Of Process)()
        Try
            For Each p As Process In Process.GetProcessesByName(ProcessName)
                Dim keep As Boolean = False
                Try
                    Dim modulePath As String = p.MainModule?.FileName
                    keep = Not String.IsNullOrEmpty(modulePath) AndAlso String.Equals(Path.GetFullPath(modulePath), wanted, StringComparison.OrdinalIgnoreCase)
                Catch ex As Win32Exception
                Catch ex As InvalidOperationException
                End Try
                If keep Then found.Add(p) Else p.Dispose()
            Next
        Catch ex As Exception
            AppFileLogger.LogException("Exchange server: looking for a running instance", ex)
        End Try
        Return found.ToArray()
    End Function

    ''' <summary>The pid of the console server, or 0 when none runs. Cheap enough for the tray menu.</summary>
    Friend Function RunningPid() As Integer
        Dim all As Process() = FindRunning()
        Try
            Return If(all.Length > 0, all(0).Id, 0)
        Finally
            For Each p As Process In all
                p.Dispose()
            Next
        End Try
    End Function

    ''' <summary>Asks the running console server to stop by its own <c>stop</c> command (a named event, so a
    ''' stop always closes registrations cleanly) and waits <see cref="StopWaitMs"/>; kills the process
    ''' only when it did not go, or when the binary has no such command.</summary>
    Friend Function StopServer() As StopOutcome
        Dim targets As Process() = FindRunning()
        If targets.Length = 0 Then Return StopOutcome.NotRunning
        Try
            Dim r As RunResult = Run({"stop"}, Nothing, CommandTimeoutMs)
            Log("stop", r)
            ' Exit 2 = a binary that does not know the command: nothing was signalled, so waiting would
            ' only delay the kill. Exit 1 = "nothing running" (it exited in the meantime, or it is not a
            ' console instance of this config): wait, the check below tells.
            Dim signalled As Boolean = r.Started AndAlso Not r.TimedOut AndAlso r.ExitCode <> ExitUsage
            Dim gone As Boolean = False
            If signalled Then gone = WaitAllExited(targets, StopWaitMs)
            If gone Then Return StopOutcome.Stopped
            For Each p As Process In targets
                Try
                    If Not p.HasExited Then p.Kill(entireProcessTree:=True)
                Catch ex As Exception
                    AppFileLogger.LogException("Exchange server: killing after the stop request did not end it", ex)
                End Try
            Next
            If WaitAllExited(targets, 3000) Then
                AppFileLogger.WriteLine("Exchange server: killed (no exit within " & (StopWaitMs \ 1000).ToString(Globalization.CultureInfo.InvariantCulture) & " s of the stop request)")
                Return StopOutcome.Killed
            End If
            Return StopOutcome.Failed
        Finally
            For Each p As Process In targets
                p.Dispose()
            Next
        End Try
    End Function

    Private Function WaitAllExited(targets As Process(), timeoutMs As Integer) As Boolean
        Dim deadline As DateTime = DateTime.UtcNow.AddMilliseconds(timeoutMs)
        For Each p As Process In targets
            Dim left As Integer = CInt(Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds))
            Try
                If Not p.HasExited AndAlso Not p.WaitForExit(left) Then Return False
            Catch
                ' A process that cannot be waited on is gone as far as this call is concerned.
            End Try
        Next
        Return True
    End Function

    ''' <summary>Called when the Share Manager exits: stops the server THIS session started, unless the user
    ''' asked to keep it running. A server found already running is never touched.</summary>
    Friend Function StopStartedOnExit(keepRunning As Boolean) As StopOutcome
        Dim mine As Process
        SyncLock Gate
            mine = _started
        End SyncLock
        If mine Is Nothing Then Return StopOutcome.NotRunning
        Try
            If mine.HasExited Then Return StopOutcome.NotRunning
        Catch
            Return StopOutcome.NotRunning
        End Try
        If keepRunning Then Return StopOutcome.NotRunning
        Return StopServer()
    End Function

    ' --- plumbing -------------------------------------------------------------------------


    Private Sub ApplyEnvironment(psi As ProcessStartInfo)
        If Not String.IsNullOrEmpty(DataDirOverride) Then psi.Environment(DataDirEnvironmentName) = DataDirOverride
    End Sub

    ''' <summary>One run of the binary with bounded waits. Reads stdout and stderr concurrently (a full pipe
    ''' would otherwise block the child), writes stdin as UTF-8 WITHOUT a byte-order mark - the Go side
    ''' trims only newlines, so a BOM would become the first character of the password.</summary>
    Friend Function Run(args As String(), stdin As String, timeoutMs As Integer) As RunResult
        If RunOverride IsNot Nothing Then Return RunOverride(args, stdin)

        Dim psi As New ProcessStartInfo(ExePath()) With {
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .RedirectStandardInput = True,
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8,
            .StandardInputEncoding = New UTF8Encoding(False),
            .WorkingDirectory = ExeDirectory()}
        For Each a As String In args
            psi.ArgumentList.Add(a)
        Next
        ApplyEnvironment(psi)

        Dim result As New RunResult
        Dim p As Process
        Try
            p = Process.Start(psi)
        Catch ex As Exception
            AppFileLogger.LogException("Exchange server: running " & args(0), ex)
            Return result
        End Try
        If p Is Nothing Then Return result
        result.Started = True
        Using p
            Dim outTask As Task(Of String) = p.StandardOutput.ReadToEndAsync()
            Dim errTask As Task(Of String) = p.StandardError.ReadToEndAsync()
            Try
                If stdin IsNot Nothing Then p.StandardInput.Write(stdin & vbLf)
                p.StandardInput.Close()
            Catch ex As IOException
                ' The child exited before reading (a refusal it decided from the arguments alone).
            End Try
            If Not p.WaitForExit(timeoutMs) Then
                result.TimedOut = True
                Try : p.Kill(entireProcessTree:=True) : Catch : End Try
                Return result
            End If
            p.WaitForExit()
            result.ExitCode = p.ExitCode
            result.StdOut = outTask.GetAwaiter().GetResult()
            result.StdErr = errTask.GetAwaiter().GetResult()
        End Using
        Return result
    End Function

    ''' <summary>One line for companion.log: the verb and the exit code. The command line, stdin and both
    ''' output streams are NEVER logged - stdout of <c>set-password --generate</c> is the plaintext.</summary>
    Private Sub Log(verb As String, r As RunResult)
        AppFileLogger.WriteLine("Exchange server: " & verb & " - " &
            If(Not r.Started, "could not run", If(r.TimedOut, "timed out", "exit " & r.ExitCode.ToString(Globalization.CultureInfo.InvariantCulture))))
    End Sub

End Module
