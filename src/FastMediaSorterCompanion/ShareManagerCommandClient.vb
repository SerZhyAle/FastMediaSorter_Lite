Option Strict On
Option Explicit On

Imports System.Diagnostics
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports FastMediaSorterCompanion.AppCommands

''' <summary>
''' The second process of APP-COMMANDS for the Share Manager (ticket L0316, rules 6 to 10): it decides its role from the
''' command line before the single-instance mutex is touched, answers <c>commands</c> and <c>version</c> itself, hands every
''' other verb to the running manager over WM_COPYDATA with the profile's own tag, waits for the reply file the request names,
''' prints the answer to the console it was started from and exits with its code. What the verb does with NO manager
''' running is the verb's cold start (the manifest's <c>coldStart</c>): start the manager, answer by itself, or refuse.
'''
''' A command line that is not a command (the empty line, <c>--tray</c>, a folder path, every spelling the program shipped
''' before the table) is NOT touched here: <see cref="TryRun"/> returns False and <see cref="Program"/> sorts it as it always did.
''' </summary>
Friend Module ShareManagerCommandClient

    ''' <summary>"FMSC" - the dwData of a command request. The legacy tag of the program's own wake (<see cref="CopyDataTransport.CompanionTag"/>)
    ''' is unchanged, so an older manager answers this one with 0: DefWindowProc's answer.</summary>
    Friend Const Tag As Integer = &H464D5343

    Friend ReadOnly Property TagPointer As IntPtr
        Get
            Return New IntPtr(Tag)
        End Get
    End Property

    Private Const PollMs As Integer = 20
    Private Const SMTO_ABORTIFHUNG As UInteger = &H2UI
    Private Const STD_OUTPUT_HANDLE As Integer = -11
    Private Const ATTACH_PARENT_PROCESS As Integer = -1

    Private Enum Delivery
        Delivered
        OlderCopy
        NoTarget
        NoAnswer
    End Enum

    <DllImport("user32.dll", EntryPoint:="FindWindowW", ExactSpelling:=True, CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Function FindWindowW(lpClassName As String, lpWindowName As String) As IntPtr
    End Function

    <DllImport("user32.dll", EntryPoint:="SendMessageTimeoutW", ExactSpelling:=True, CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Function SendMessageTimeoutW(hWnd As IntPtr, Msg As Integer, wParam As IntPtr,
                                         ByRef lParam As Program.COPYDATASTRUCT, fuFlags As UInteger,
                                         uTimeout As UInteger, ByRef lpdwResult As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Function GetWindowThreadProcessId(hwnd As IntPtr, ByRef pid As UInteger) As UInteger
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function AllowSetForegroundWindow(dwProcessId As Integer) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function AttachConsole(dwProcessId As Integer) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function GetStdHandle(nStdHandle As Integer) As IntPtr
    End Function

    ''' <summary>
    ''' True when the command line was a command and this process is done: <paramref name="exitCode"/> is the process exit code.
    ''' False when it is an ordinary launch (<paramref name="plan"/> is Nothing: sort the arguments as before) or a command whose
    ''' cold start is "start" and nothing runs (<paramref name="plan"/> says what to start: this process becomes the manager).
    ''' </summary>
    Friend Function TryRun(args As String(), ByRef exitCode As Integer, ByRef plan As LaunchPlan) As Boolean
        exitCode = CommandExit.Done
        plan = Nothing
        If Not ShareManagerCommandTable.IsCommandLine(args) Then Return False
        CompanionGlobals.LoadLanguage()

        Dim manifest As CommandManifest = ShareManagerCommandTable.Build()
        Dim parsed As ParsedCommand = Nothing
        Dim refusal As ParseRefusal = CommandLineParser.Parse(args, manifest, parsed)
        Dim requestId As String = Guid.NewGuid().ToString("D")
        If refusal IsNot Nothing Then
            Dim json As Boolean = Array.IndexOf(args, "--json") >= 0
            exitCode = Emit(CommandResponse.ForReason(requestId, refusal.Reason, ShareManagerCommandText.ForRefusal(refusal)), json, Nothing)
            Return True
        End If

        Dim wantsJson As Boolean = parsed.Flag("json")
        Dim resultPath As String = parsed.Value("result")
        Select Case parsed.Spec.Name
            Case "commands"
                AttachOutput()
                Console.Out.Write(If(wantsJson, manifest.ToJsonText(), manifest.ToReadableList()))
                Console.Out.Flush()
                exitCode = CommandExit.Done
                Return True
            Case "version"
                Dim data As New JsonObject()
                data.Add("version", System.Windows.Forms.Application.ProductVersion)
                data.Add("cliVersion", manifest.CliVersion)
                exitCode = Emit(CommandResponse.Done(requestId, System.Windows.Forms.Application.ProductVersion, data), wantsJson, resultPath)
                Return True
        End Select

        ' The folder or path a verb names, checked here by the very function the manager applies (a relative path against THIS
        ' process's directory): a misspelt folder is refused with exit 10 before anything is delivered or started.
        Dim badToken As String = Nothing
        Dim target As String = LaunchArguments.TargetOf(parsed, Environment.CurrentDirectory, badToken)
        If badToken IsNot Nothing OrElse Not ShareManagerScheduleCli.ValidateClientArguments(parsed, Environment.CurrentDirectory, badToken) Then
            exitCode = Emit(CommandResponse.ForReason(requestId, CommandReasons.BadArgument, ShareManagerCommandText.BadValue(badToken)), wantsJson, resultPath)
            Return True
        End If

        ' Rule 13, once here so that no cold start (a manager of this process, the clipboard on its own) gets round it; the manager
        ' decides again for what reaches it, because any program can send the tag.
        If Not CommandDispatch.IsAdmittedByRisk(parsed.Spec, False, AppCommandsSetting.IsAllowed()) Then
            exitCode = Emit(CommandResponse.ForReason(requestId, CommandReasons.SwitchOff,
                                                      ShareManagerCommandText.ForReason(CommandReasons.SwitchOff)), wantsJson, resultPath)
            Return True
        End If

        Dim request As New CommandRequest With {
            .Command = parsed.Spec.Name, .WorkingDir = Environment.CurrentDirectory, .Origin = CommandOrigin.Cli,
            .RequestId = requestId, .Wait = parsed.Spec.Wait, .CallerPid = Environment.ProcessId,
            .CallerElevated = SingleInstanceProtocol.IsProcessElevated(),
            .ReplyPath = CommandFiles.ReplyPathFor(ShareManagerCommandTable.ReplyFolder, requestId)}
        request.Args.AddRange(parsed.RawArgs)
        If resultPath IsNot Nothing Then request.ResultPath = SafeFullPath(resultPath)

        Select Case Deliver(request)
            Case Delivery.NoTarget
                Return ColdStart(parsed, request, target, wantsJson, resultPath, exitCode, plan)
            Case Delivery.OlderCopy
                exitCode = AnswerOlderCopy(parsed, requestId, target, wantsJson)
                Return True
            Case Delivery.NoAnswer
                exitCode = Emit(CommandResponse.ForReason(requestId, CommandReasons.Timeout,
                                                          ShareManagerCommandText.ForReason(CommandReasons.Timeout)), wantsJson, Nothing)
                Return True
        End Select

        Dim response As CommandResponse = WaitForReply(request.ReplyPath, parsed.Spec.CompletionMs(), requestId)
        If response Is Nothing Then
            response = CommandResponse.ForReason(requestId, CommandReasons.Timeout, ShareManagerCommandText.ForReason(CommandReasons.Timeout))
        End If
        exitCode = Emit(response, wantsJson, If(response.Data IsNot Nothing, request.ResultPath, Nothing))
        Return True
    End Function

    ''' <summary>Schedule verbs that mutate or run operations go through the running manager (single writer rule) and
    ''' return a structured answer; with none running, the client starts the manager silently and forwards the request.</summary>
    Private Function IsBackgroundScheduleVerb(name As String) As Boolean
        Return String.Equals(name, "schedule add", StringComparison.Ordinal) OrElse
               String.Equals(name, "schedule remove", StringComparison.Ordinal) OrElse
               String.Equals(name, "schedule run", StringComparison.Ordinal)
    End Function

    ''' <summary>What a verb does when no manager runs (rule 10, the manifest's <c>coldStart</c>). Returns True when the answer was
    ''' given here; False when this process must become the manager, with <paramref name="plan"/> set.</summary>
    Private Function ColdStart(parsed As ParsedCommand, request As CommandRequest, target As String, wantsJson As Boolean,
                               resultPath As String, ByRef exitCode As Integer, ByRef plan As LaunchPlan) As Boolean
        Select Case parsed.Spec.ColdStart
            Case "start"
                If IsBackgroundScheduleVerb(parsed.Spec.Name) Then
                    exitCode = StartSilentManagerAndDeliver(parsed, request, target, wantsJson)
                    Return True
                End If
                plan = LaunchArguments.FromCommand(parsed, target)
                Return False
            Case "oneshot"
                Select Case parsed.Spec.Name
                    Case "clipboard send", "clipboard get"
                        exitCode = RunClipboardAlone(parsed.Spec.Name = "clipboard send", request.RequestId, wantsJson, resultPath)
                    Case "schedule list"
                        ' Reads the operation files and answers: no tray, no window, no scheduler, nothing runs (ticket L0319).
                        exitCode = Emit(ShareManagerObserve.ScheduleListResponse(request.RequestId), wantsJson,
                                        If(resultPath IsNot Nothing, SafeFullPath(resultPath), Nothing))
                    Case "schedule history"
                        ' Reads the journal files and answers: no tray, no window, no scheduler, nothing runs (ticket L0345).
                        exitCode = Emit(ShareManagerScheduleCli.ScheduleHistoryResponse(request.RequestId, parsed), wantsJson,
                                        If(resultPath IsNot Nothing, SafeFullPath(resultPath), Nothing))
                    Case "exchange status"
                        ' No manager means no agent and no queue: whether an account is set up, read from disk, state stopped, nothing waiting.
                        exitCode = Emit(ShareManagerObserve.ExchangeStatusResponse(request.RequestId, 0), wantsJson,
                                        If(resultPath IsNot Nothing, SafeFullPath(resultPath), Nothing))
                    Case Else
                        ' quit with nothing running is done (rule 17).
                        exitCode = Emit(CommandResponse.Done(request.RequestId, ShareManagerCommandText.NotStarted()), wantsJson, resultPath)
                End Select
                Return True
            Case Else
                exitCode = Emit(CommandResponse.ForReason(request.RequestId, CommandReasons.NotRunning,
                                                          ShareManagerCommandText.ForReason(CommandReasons.NotRunning)), wantsJson, resultPath)
                Return True
        End Select
    End Function

    Private Function StartSilentManagerAndDeliver(parsed As ParsedCommand, request As CommandRequest, target As String,
                                                  wantsJson As Boolean) As Integer
        Dim exe As String = Environment.ProcessPath
        If String.IsNullOrEmpty(exe) Then
            Return Emit(CommandResponse.ForReason(request.RequestId, CommandReasons.OperationFailed, ShareManagerCommandText.Failed()), wantsJson, Nothing)
        End If
        Try
            Dim startInfo As New ProcessStartInfo(exe) With {
                .UseShellExecute = False,
                .WorkingDirectory = Environment.CurrentDirectory}
            startInfo.ArgumentList.Add(Program.TrayFlag)
            Process.Start(startInfo)
        Catch ex As Exception
            AppFileLogger.LogException("App command: starting Share Manager silently", ex)
            Return Emit(CommandResponse.ForReason(request.RequestId, CommandReasons.OperationFailed, ShareManagerCommandText.Failed()), wantsJson, Nothing)
        End Try

        Dim startupBoundMs As Integer = Math.Min(parsed.Spec.CompletionMs(), 15000)
        Dim watch As Stopwatch = Stopwatch.StartNew()
        While watch.ElapsedMilliseconds < startupBoundMs
            Select Case Deliver(request)
                Case Delivery.Delivered
                    Dim response As CommandResponse = WaitForReply(request.ReplyPath, parsed.Spec.CompletionMs(), request.RequestId)
                    If response Is Nothing Then
                        response = CommandResponse.ForReason(request.RequestId, CommandReasons.Timeout, ShareManagerCommandText.ForReason(CommandReasons.Timeout))
                    End If
                    Return Emit(response, wantsJson, If(response.Data IsNot Nothing, request.ResultPath, Nothing))
                Case Delivery.OlderCopy
                    Return AnswerOlderCopy(parsed, request.RequestId, target, wantsJson)
                Case Delivery.NoAnswer
                    Return Emit(CommandResponse.ForReason(request.RequestId, CommandReasons.Timeout,
                                                          ShareManagerCommandText.ForReason(CommandReasons.Timeout)), wantsJson, Nothing)
            End Select
            Thread.Sleep(50)
        End While
        Return Emit(CommandResponse.ForReason(request.RequestId, CommandReasons.Timeout,
                                              ShareManagerCommandText.ForReason(CommandReasons.Timeout)), wantsJson, Nothing)
    End Function

    ''' <summary>The clipboard verbs with no manager: the same one action as the desktop shortcut (a tray icon, never the manager,
    ''' the worker or a window), its outcome answered through the standard output and <c>--result</c>.</summary>
    Private Function RunClipboardAlone(isSend As Boolean, requestId As String, wantsJson As Boolean, resultPath As String) As Integer
        Dim outcome As ShareManagerClipboardOutcome = Nothing
        Program.RunClipboardOneShot(isSend, outcome)
        Dim response As CommandResponse = outcome.ToResponse(requestId, outcome.Text)
        Return Emit(response, wantsJson, If(resultPath IsNot Nothing, SafeFullPath(resultPath), Nothing))
    End Function

    ''' <summary>An older manager answered 0: it does not know the tag. Rule 2 keeps what it does understand working over the old
    ''' transport (the spelling the program shipped for the same verb); the answer still says it must be restarted (exit 2).</summary>
    Private Function AnswerOlderCopy(parsed As ParsedCommand, requestId As String, target As String, wantsJson As Boolean) As Integer
        Dim legacy As String = LaunchArguments.LegacyForwardOf(parsed, target)
        If legacy.Length > 0 Then
            Try
                Program.ForwardToRunningInstance(legacy)
            Catch ex As Exception
                AppFileLogger.LogException("App command: forward to an older copy", ex)
            End Try
        End If
        Return Emit(New CommandResponse With {.RequestId = requestId, .Status = CommandStatus.Failed,
                                              .ExitCode = CommandExit.NotProven, .Reason = CommandReasons.Schema,
                                              .Message = ShareManagerCommandText.OlderCopy()}, wantsJson, Nothing)
    End Function

    Private Function SafeFullPath(path As String) As String
        Try
            Return System.IO.Path.GetFullPath(path)
        Catch ex As Exception
            Return path
        End Try
    End Function

    ''' <summary>A manager holds the single-instance mutex. Access denied counts: an elevated manager's DACL excludes this token.</summary>
    Private Function ManagerMutexExists() As Boolean
        Try
            Dim existing As Mutex = Nothing
            If Mutex.TryOpenExisting(Program.MutexName, existing) Then
                existing.Dispose()
                Return True
            End If
            Return False
        Catch ex As UnauthorizedAccessException
            Return True
        Catch ex As Exception
            Return False
        End Try
    End Function

    ''' <summary>Rule 8: the delivery bound is one second for the whole attempt, every send inside it. No message window and no
    ''' mutex is "nothing runs" at once; a mutex without a window yet is a manager still starting, worth another try inside the bound.</summary>
    Private Function Deliver(request As CommandRequest) As Delivery
        Dim payload As Byte() = Nothing
        Dim refusal As PayloadRefusal
        If Not CopyDataTransport.TryEncode(request.ToLine(), payload, refusal) Then Return Delivery.NoAnswer
        Dim watch As Stopwatch = Stopwatch.StartNew()
        Dim sawWindow As Boolean = False
        Do
            Dim remaining As Integer = CommandBounds.DeliveryMs - CInt(watch.ElapsedMilliseconds)
            If remaining <= 0 Then Exit Do
            Dim hwnd As IntPtr = FindWindowW(Nothing, Program.MessageWindowTitle)
            If hwnd = IntPtr.Zero Then
                If Not sawWindow AndAlso Not ManagerMutexExists() Then Return Delivery.NoTarget
            Else
                sawWindow = True
                Try
                    Dim pid As UInteger = 0
                    GetWindowThreadProcessId(hwnd, pid)
                    If pid <> 0UI Then AllowSetForegroundWindow(CInt(pid))
                Catch ex As Exception
                    ' The window may then stay behind another one; the answer is unaffected.
                End Try
                Dim answer As Integer = SendOnce(hwnd, payload, remaining)
                If answer = 1 Then Return Delivery.Delivered
                If answer = 0 Then Return Delivery.OlderCopy
            End If
            Thread.Sleep(50)
        Loop
        If Not sawWindow AndAlso Not ManagerMutexExists() Then Return Delivery.NoTarget
        Return Delivery.NoAnswer
    End Function

    ''' <summary>1 = taken by a manager that knows the tag, 0 = answered by one that does not, -1 = no answer.</summary>
    Private Function SendOnce(hwnd As IntPtr, payload As Byte(), timeoutMs As Integer) As Integer
        Dim ptr As IntPtr = Marshal.AllocHGlobal(payload.Length + 1)
        Try
            Marshal.Copy(payload, 0, ptr, payload.Length)
            Marshal.WriteByte(ptr, payload.Length, 0)
            Dim cds As New Program.COPYDATASTRUCT With {.dwData = TagPointer, .cbData = payload.Length, .lpData = ptr}
            Dim result As IntPtr = IntPtr.Zero
            Dim sent As Boolean = SendMessageTimeoutW(hwnd, Program.WM_COPYDATA, IntPtr.Zero, cds, SMTO_ABORTIFHUNG,
                                                      CUInt(Math.Max(1, timeoutMs)), result) <> IntPtr.Zero
            If Not sent Then Return -1
            Return If(result.ToInt64() = 1L, 1, 0)
        Catch ex As Exception
            Return -1
        Finally
            Marshal.FreeHGlobal(ptr)
        End Try
    End Function

    ''' <summary>The answer in the reply file. An answer whose <c>requestId</c> is empty or is not this request's (APP-COMMANDS 0.2
    ''' item I.3: the receiver could not read the line) is unreadable here: exit 2, never the answer's own code.</summary>
    Private Function WaitForReply(replyPath As String, completionMs As Integer, requestId As String) As CommandResponse
        Dim watch As Stopwatch = Stopwatch.StartNew()
        Try
            While watch.ElapsedMilliseconds < completionMs
                If File.Exists(replyPath) Then
                    Try
                        Dim text As String = File.ReadAllText(replyPath, Encoding.UTF8)
                        Dim response As CommandResponse = Nothing
                        If CommandResponse.TryParse(text, response) Then
                            If response.IsAnswerTo(requestId) Then Return response
                            Return CommandResponse.ForReason(requestId, CommandReasons.Schema, ShareManagerCommandText.ForReason(CommandReasons.Schema))
                        End If
                    Catch ex As IOException
                        ' Being moved into place this instant; the next look reads it whole.
                    End Try
                End If
                Thread.Sleep(PollMs)
            End While
            Return Nothing
        Finally
            Try
                If File.Exists(replyPath) Then File.Delete(replyPath)
            Catch ex As Exception
            End Try
        End Try
    End Function

    ''' <summary>Rule 9: the sentence (or with --json the response line) on standard output, the data into --result, and the
    ''' exit code of the answer.</summary>
    Private Function Emit(response As CommandResponse, json As Boolean, resultPath As String) As Integer
        If resultPath IsNot Nothing Then
            Try
                CommandFiles.WriteResult(SafeFullPath(resultPath), response.Data)
            Catch ex As Exception
                response = New CommandResponse With {.RequestId = response.RequestId, .Status = CommandStatus.Failed,
                                                     .ExitCode = CommandExit.Failed,
                                                     .Message = ShareManagerCommandText.WriteFailed()}
            End Try
        End If
        AttachOutput()
        Try
            If json Then
                Console.Out.Write(response.ToLine() & ControlChars.Lf)
            ElseIf Not String.IsNullOrEmpty(response.Message) Then
                Console.Out.Write(response.Message & Environment.NewLine)
            End If
            Console.Out.Flush()
        Catch ex As IOException
            ' No console and nothing redirected: the exit code is the answer.
        End Try
        Return response.ExitCode
    End Function

    Private attached As Boolean

    ''' <summary>A window program has no console of its own: use a redirected standard output when there is one, otherwise the
    ''' console of the process that started us (rule 9).</summary>
    Private Sub AttachOutput()
        If attached Then Return
        attached = True
        Try
            Dim handle As IntPtr = GetStdHandle(STD_OUTPUT_HANDLE)
            If handle = IntPtr.Zero OrElse handle = New IntPtr(-1) Then
                ' The parent's console keeps its own code page: it is not ours to change.
                AttachConsole(ATTACH_PARENT_PROCESS)
            Else
                ' Redirected into a file or a pipe: UTF-8, as every file of this profile.
                Console.OutputEncoding = New UTF8Encoding(False)
            End If
        Catch ex As Exception
        End Try
    End Sub

End Module
