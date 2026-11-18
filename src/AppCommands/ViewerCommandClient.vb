#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System.Diagnostics
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading

' The second process of APP-COMMANDS (rules 6 to 10): it decides its role from the command line
' before anything else of the viewer is loaded, answers "commands" and "version" itself, hands
' every other verb to the running copy over WM_COPYDATA with its own tag, waits for the reply file
' the request names, prints the answer to the console it was started from and exits with its code.
' Called from MyApplication_Startup before the single-instance mutex; the mainline only.
Namespace AppCommands

    Friend Module ViewerCommandClient

        ''' <summary>"FMSC" - the dwData of a command request. The file-forward tag "FMSV" is unchanged, so
        ''' an older viewer or the x86 build answers this one with 0: DefWindowProc's answer.</summary>
        Friend Const Tag As Integer = &H464D5343

        Friend ReadOnly Property Tag_Pointer As IntPtr
            Get
                Return New IntPtr(Tag)
            End Get
        End Property

        ''' <summary>True once a cold-start <c>show</c> made this process the first instance: the
        ''' command line was a command, not a file, so Form1_Load and the welcome wizard read it as empty.</summary>
        Friend Property LaunchArgumentsConsumed As Boolean

        ''' <summary>What a cold-start <c>open</c> hands the launch in place of the command line: the target as the text the
        ''' bare launch has always taken (L0317). Empty for a cold <c>show</c>.</summary>
        Friend Property ColdLaunchText As String

        Private coldPhoneCodeFile As String

        ''' <summary>The launch argument text of Form1_Load and the welcome wizard: the command line itself, except when a
        ''' command made this process the viewer - then what that command carries, or nothing.</summary>
        Friend Function LaunchArgumentText(commandLine As String) As String
            If LaunchArgumentsConsumed Then Return If(ColdLaunchText, String.Empty)
            Return commandLine
        End Function

        ''' <summary>The file a cold <c>phone-code receive</c> names, once: the viewer takes it when its window is up.</summary>
        Friend Function TakeColdPhoneCodeFile() As String
            Dim file As String = coldPhoneCodeFile
            coldPhoneCodeFile = Nothing
            Return file
        End Function

        Private Const PollMs As Integer = 20

        Private Enum Delivery
            Delivered
            OlderCopy
            NoTarget
            NoAnswer
        End Enum

        <DllImport("user32.dll", CharSet:=CharSet.Auto)>
        Private Function SendMessageTimeout(hWnd As IntPtr, msg As Integer, wParam As IntPtr,
                                            ByRef lParam As Main_Form.COPYDATASTRUCT,
                                            flags As Integer, timeout As Integer,
                                            ByRef result As IntPtr) As IntPtr
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

        Private Const WM_COPYDATA As Integer = &H4A
        Private Const SMTO_ABORTIFHUNG As Integer = &H2
        Private Const STD_OUTPUT_HANDLE As Integer = -11
        Private Const ATTACH_PARENT_PROCESS As Integer = -1

        ''' <summary>
        ''' True when the command line was a command and this process is done: <paramref name="exitCode"/>
        ''' is the process exit code. False when it is an ordinary launch (a path, an empty line, a frozen
        ''' spelling) or a cold-start <c>show</c>, and the viewer starts as before.
        ''' </summary>
        Friend Function TryRun(args As String(), ByRef exitCode As Integer) As Boolean
            exitCode = CommandExit.Done
            If Not ViewerCommandTable.IsCommandLine(args) Then Return False
            Try
                Localization.LoadFromSettings()
            Catch ex As Exception
                ' English stays; the answer still carries its code.
            End Try

            Dim manifest As CommandManifest = ViewerCommandTable.Build()
            Dim parsed As ParsedCommand = Nothing
            Dim refusal As ParseRefusal = CommandLineParser.Parse(args, manifest, parsed)
            Dim requestId As String = Guid.NewGuid().ToString("D")
            If refusal IsNot Nothing Then
                Dim json As Boolean = Array.IndexOf(args, "--json") >= 0
                exitCode = Emit(CommandResponse.ForReason(requestId, refusal.Reason, AppCommandsText.ForRefusal(refusal)), json, Nothing)
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
                Case "stream-catalog build", "stream-catalog check"
                    ' Oneshot (rule 10): runs in this process, never touches the mutex or a window.
                    exitCode = RunStreamCatalog(parsed, requestId, wantsJson, resultPath)
                    Return True
                Case "open"
                    If parsed.Options.ContainsKey("target-file") Then
                        ' Item H.2: the target is read here, once, and stands in the request as the positional it replaces.
                        Dim targetFile As String = SafeFullPath(parsed.Value("target-file"))
                        If targetFile Is Nothing OrElse Not IO.File.Exists(targetFile) Then
                            exitCode = Emit(CommandResponse.ForReason(requestId, CommandReasons.TargetMissing,
                                                                      Localization.T("Файл или папка не найдены.")), wantsJson, Nothing)
                            Return True
                        End If
                        Dim fromFile As String = Nothing
                        If Not CommandFiles.TryReadSecret(targetFile, fromFile) OrElse String.IsNullOrWhiteSpace(fromFile) OrElse
                           fromFile.IndexOf(ControlChars.Cr) >= 0 OrElse fromFile.IndexOf(ControlChars.Lf) >= 0 Then
                            exitCode = Emit(CommandResponse.ForReason(requestId, CommandReasons.BadArgument, AppCommandsText.ForRefusal(
                                New ParseRefusal With {.Reason = CommandReasons.BadArgument, .Detail = CommandLineParser.DetailBadValue, .Token = "--target-file"})),
                                wantsJson, Nothing)
                            Return True
                        End If
                        CommandLineParser.InlineFileValue(parsed, "target-file", fromFile.Trim())
                    End If
                    Dim targetClass As OpenTargetClass = OpenTargets.Classify(parsed.Positionals(0))
                    If targetClass = OpenTargetClass.Internal OrElse targetClass = OpenTargetClass.Empty OrElse
                       OpenTargets.ResolveAgainst(Environment.CurrentDirectory, parsed.Positionals(0)) Is Nothing Then
                        exitCode = Emit(CommandResponse.ForReason(requestId, CommandReasons.BadArgument, AppCommandsText.ForRefusal(
                            New ParseRefusal With {.Reason = CommandReasons.BadArgument, .Detail = CommandLineParser.DetailBadValue, .Token = "target"})),
                            wantsJson, Nothing)
                        Return True
                    End If
            End Select

            Dim request As New CommandRequest With {
                .Command = parsed.Spec.Name, .WorkingDir = Environment.CurrentDirectory, .Origin = CommandOrigin.Cli,
                .RequestId = requestId, .Wait = parsed.Spec.Wait, .CallerPid = Environment.ProcessId,
                .CallerElevated = SingleInstanceProtocol.IsProcessElevated(),
                .ReplyPath = CommandFiles.ReplyPathFor(ViewerCommandTable.ReplyFolder, requestId)}
            request.Args.AddRange(parsed.RawArgs)
            If resultPath IsNot Nothing Then request.ResultPath = SafeFullPath(resultPath)

            Dim outcome As Delivery = Deliver(request)
            Select Case outcome
                Case Delivery.NoTarget
                    Select Case parsed.Spec.ColdStart
                        Case "start"
                            ' show, open or phone-code receive with nothing running: this process becomes the viewer
                            ' (rule 10) - unless the command is refused before it starts anything.
                            If Not TryColdStart(parsed, requestId, wantsJson, resultPath, exitCode) Then Return True
                            Return False
                        Case "oneshot"
                            ' quit with nothing running is done (rule 17).
                            exitCode = Emit(CommandResponse.Done(requestId, Localization.T("Приложение не было запущено.")), wantsJson, resultPath)
                            Return True
                        Case Else
                            exitCode = Emit(CommandResponse.ForReason(requestId, CommandReasons.NotRunning,
                                                                      AppCommandsText.ForReason(CommandReasons.NotRunning)), wantsJson, resultPath)
                            Return True
                    End Select
                Case Delivery.OlderCopy
                    If parsed.Spec.Name = "show" Then ForwardLegacyShow()
                    If parsed.Spec.Name = "open" Then ForwardLegacyOpen(parsed)
                    exitCode = Emit(New CommandResponse With {.RequestId = requestId, .Status = CommandStatus.Failed,
                                                              .ExitCode = CommandExit.NotProven, .Reason = CommandReasons.Schema,
                                                              .Message = AppCommandsText.OlderCopy()}, wantsJson, Nothing)
                    Return True
                Case Delivery.NoAnswer
                    exitCode = Emit(CommandResponse.ForReason(requestId, CommandReasons.Timeout,
                                                              AppCommandsText.ForReason(CommandReasons.Timeout)), wantsJson, Nothing)
                    Return True
            End Select

            Dim response As CommandResponse = WaitForReply(request.ReplyPath, parsed.Spec.CompletionMs(), requestId)
            If response Is Nothing Then
                response = CommandResponse.ForReason(requestId, CommandReasons.Timeout, AppCommandsText.ForReason(CommandReasons.Timeout))
            End If
            exitCode = Emit(response, wantsJson, If(response.ExitCode = CommandExit.Done, request.ResultPath, Nothing))
            Return True
        End Function

        ''' <summary>
        ''' A command of cold start <c>start</c> found no running viewer. True: this process becomes the viewer, with
        ''' what the command carries kept for Form1_Load. False: it was refused here (the setting of rule 13 is off, or
        ''' the file is not there) and <paramref name="exitCode"/> is the answer already printed.
        ''' </summary>
        Private Function TryColdStart(parsed As ParsedCommand, requestId As String, wantsJson As Boolean, resultPath As String,
                                      ByRef exitCode As Integer) As Boolean
            Select Case parsed.Spec.Name
                Case "open"
                    ColdLaunchText = OpenTargets.ToLegacyArgument(
                        OpenTargets.ResolveAgainst(Environment.CurrentDirectory, parsed.Positionals(0)), parsed.Flag("no-back"))
                Case "phone-code receive"
                    If Not CommandDispatch.IsAdmittedByRisk(parsed.Spec, False, AppCommandsSetting.IsAllowed()) Then
                        exitCode = Emit(CommandResponse.ForReason(requestId, CommandReasons.SwitchOff,
                                                                  AppCommandsText.ForReason(CommandReasons.SwitchOff)), wantsJson, resultPath)
                        Return False
                    End If
                    Dim file As String = SafeFullPath(parsed.Value("secret-file"))
                    If Not IO.File.Exists(file) Then
                        exitCode = Emit(CommandResponse.ForReason(requestId, CommandReasons.TargetMissing,
                                                                  Localization.T("Файл или папка не найдены.")), wantsJson, resultPath)
                        Return False
                    End If
                    coldPhoneCodeFile = file
            End Select
            LaunchArgumentsConsumed = True
            Return True
        End Function

        ''' <summary>
        ''' <c>stream-catalog build</c> and <c>check</c> (L0317): the same producer and validator as the shipped switch
        ''' spellings, with the table of rule 11 for the exit code. The verb is held by the setting of rule 13; the
        ''' shipped spellings are not (they ran ungated before this profile and stay as they were).
        ''' </summary>
        Private Function RunStreamCatalog(parsed As ParsedCommand, requestId As String, wantsJson As Boolean, resultPath As String) As Integer
            If Not CommandDispatch.IsAdmittedByRisk(parsed.Spec, False, AppCommandsSetting.IsAllowed()) Then
                Return Emit(CommandResponse.ForReason(requestId, CommandReasons.SwitchOff,
                                                      AppCommandsText.ForReason(CommandReasons.SwitchOff)), wantsJson, resultPath)
            End If
            ' The producer prints its report as it always did: to the console this process was started from.
            AttachOutput()
            Dim run As Streams.StreamCatalogRunResult
            Dim isBuild As Boolean = (parsed.Spec.Name = "stream-catalog build")
            Try
                If isBuild Then
                    run = Streams.StreamCatalogCli.BuildCore(parsed.Positionals(0), parsed.Value("out"), parsed.Flag("force"), parsed.Flag("overwrite"))
                Else
                    run = Streams.StreamCatalogCli.CheckCore(parsed.Positionals(0))
                End If
            Catch ex As Exception
                AppFileLogger.LogException("App command: stream-catalog", ex)
                Return Emit(New CommandResponse With {.RequestId = requestId, .Status = CommandStatus.Failed,
                                                      .ExitCode = CommandExit.Failed, .Message = Localization.T("Команда не выполнена.")},
                            wantsJson, Nothing)
            End Try

            Dim response As CommandResponse
            Select Case run.ExitCode
                Case Streams.StreamCatalogCli.ExitPass
                    Dim data As New JsonObject()
                    If isBuild Then
                        data.Add("rows", run.Rows)
                        data.Add("passed", run.Passed)
                        response = CommandResponse.Done(requestId, Localization.TF("Каталог собран: {0}. Строк: {1}.", run.OutputPath, run.Rows), data)
                    Else
                        data.Add("passed", run.Passed)
                        data.Add("failedChecks", run.FailedChecks)
                        response = CommandResponse.Done(requestId, Localization.TF("Проверка каталога пройдена: {0}.", run.OutputPath), data)
                    End If
                Case Streams.StreamCatalogCli.ExitInputMissing
                    response = CommandResponse.ForReason(requestId, CommandReasons.TargetMissing, Localization.T("Файл или папка не найдены."))
                Case Else
                    response = New CommandResponse With {.RequestId = requestId, .Status = CommandStatus.Failed, .ExitCode = CommandExit.Failed,
                        .Message = If(isBuild, Localization.TF("Каталог не собран: {0}", run.Detail),
                                      Localization.TF("Проверка каталога не пройдена, нарушений: {0}.", run.FailedChecks))}
            End Select
            Return Emit(response, wantsJson, If(response.ExitCode = CommandExit.Done, resultPath, Nothing))
        End Function

        Private Function SafeFullPath(path As String) As String
            Try
                Return IO.Path.GetFullPath(path)
            Catch ex As Exception
                Return path
            End Try
        End Function

        ''' <summary>Rule 8: the delivery bound is one second for the whole attempt, every send inside it.</summary>
        Private Function Deliver(request As CommandRequest) As Delivery
            Dim payload As Byte() = Nothing
            Dim refusal As PayloadRefusal
            If Not CopyDataTransport.TryEncode(request.ToLine(), payload, refusal) Then Return Delivery.NoAnswer
            Dim watch As Stopwatch = Stopwatch.StartNew()
            Dim sawViewer As Boolean = False
            Dim sawZero As Boolean = False
            Do
                Dim remaining As Integer = CommandBounds.DeliveryMs - CInt(watch.ElapsedMilliseconds)
                If remaining <= 0 Then Exit Do
                Dim targets As List(Of ViewerProcessTarget) = My.MyApplication.GetRunningViewerTargets(Environment.ProcessId)
                If targets.Count = 0 Then
                    If Not sawViewer Then Return Delivery.NoTarget
                Else
                    sawViewer = True
                End If
                For Each target As ViewerProcessTarget In targets
                    Dim windowHandles As New List(Of IntPtr)()
                    If target.MainWindowHandle <> IntPtr.Zero Then
                        windowHandles.Add(target.MainWindowHandle)
                    ElseIf target.TopLevelHandles IsNot Nothing Then
                        windowHandles.AddRange(target.TopLevelHandles)
                    End If
                    Try
                        AllowSetForegroundWindow(target.Pid)
                    Catch ex As Exception
                    End Try
                    For Each handle As IntPtr In windowHandles
                        remaining = CommandBounds.DeliveryMs - CInt(watch.ElapsedMilliseconds)
                        If remaining <= 0 Then Exit For
                        Dim answer As Integer = SendOnce(handle, payload, remaining)
                        If answer = 1 Then Return Delivery.Delivered
                        If answer = 0 Then sawZero = True
                    Next
                Next
                Thread.Sleep(50)
            Loop
            If Not sawViewer Then Return Delivery.NoTarget
            Return If(sawZero, Delivery.OlderCopy, Delivery.NoAnswer)
        End Function

        ''' <summary>1 = taken by a viewer that knows the tag, 0 = answered by something that does not, -1 = no answer.</summary>
        Private Function SendOnce(handle As IntPtr, payload As Byte(), timeoutMs As Integer) As Integer
            Dim ptr As IntPtr = Marshal.AllocHGlobal(payload.Length + 1)
            Try
                Marshal.Copy(payload, 0, ptr, payload.Length)
                Marshal.WriteByte(ptr, payload.Length, 0)
                Dim cds As New Main_Form.COPYDATASTRUCT()
                cds.dwData = Tag_Pointer
                cds.cbData = payload.Length
                cds.lpData = ptr
                Dim result As IntPtr = IntPtr.Zero
                Dim sent As Boolean = SendMessageTimeout(handle, WM_COPYDATA, IntPtr.Zero, cds, SMTO_ABORTIFHUNG, timeoutMs, result) <> IntPtr.Zero
                If Not sent Then Return -1
                Return If(result.ToInt64() = 1, 1, 0)
            Catch ex As Exception
                Return -1
            Finally
                Marshal.FreeHGlobal(ptr)
            End Try
        End Function

        ''' <summary>Rule 2 kept for an older running copy: <c>show</c> still brings its window back over the
        ''' old transport, and the answer still says it must be restarted (exit 2).</summary>
        Private Sub ForwardLegacyShow()
            Try
                Dim payload As Byte() = Nothing
                Dim refusal As PayloadRefusal
                If CopyDataTransport.TryEncode(Main_Form.Show_Window_Command, payload, refusal) Then
                    My.MyApplication.TryForwardOnce(payload, Environment.ProcessId, CommandBounds.DeliveryMs)
                End If
            Catch ex As Exception
                AppFileLogger.LogException("App command: show to an older copy", ex)
            End Try
        End Sub

        ''' <summary>Rule 2 kept for an older running copy, as for <c>show</c>: the target goes out in the spelling the older copy
        ''' reads (the path, and its shipped switch after it), resolved against this caller's working folder. The answer still
        ''' says it must be restarted (exit 2); a target that is no path is never forwarded.</summary>
        Private Sub ForwardLegacyOpen(parsed As ParsedCommand)
            Try
                Dim full As String = OpenTargets.ResolveAgainst(Environment.CurrentDirectory, parsed.Positionals(0))
                If full Is Nothing Then Return
                Dim payload As Byte() = Nothing
                Dim refusal As PayloadRefusal
                If CopyDataTransport.TryEncode(OpenTargets.ToLegacyArgument(full, parsed.Flag("no-back")), payload, refusal) Then
                    My.MyApplication.TryForwardOnce(payload, Environment.ProcessId, CommandBounds.DeliveryMs)
                End If
            Catch ex As Exception
                AppFileLogger.LogException("App command: open to an older copy", ex)
            End Try
        End Sub

        ''' <summary>The answer in the reply file. An answer whose <c>requestId</c> is empty or is not this request's
        ''' (APP-COMMANDS 0.2 item I.3: the receiver could not read the line) is unreadable here: exit 2, never the
        ''' answer's own code.</summary>
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
                                Return CommandResponse.ForReason(requestId, CommandReasons.Schema, AppCommandsText.ForReason(CommandReasons.Schema))
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

        ''' <summary>Rule 9: the sentence (or with --json the response line) on standard output, the data into
        ''' --result, and the exit code of the answer.</summary>
        Private Function Emit(response As CommandResponse, json As Boolean, resultPath As String) As Integer
            If resultPath IsNot Nothing Then
                Try
                    CommandFiles.WriteResult(SafeFullPath(resultPath), response.Data)
                Catch ex As Exception
                    response = New CommandResponse With {.RequestId = response.RequestId, .Status = CommandStatus.Failed,
                                                         .ExitCode = CommandExit.Failed,
                                                         .Message = Localization.T("Не удалось записать файл результата.")}
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

        ''' <summary>A window program has no console of its own: use a redirected standard output when there
        ''' is one, otherwise the console of the process that started us (rule 9).</summary>
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

End Namespace
#End If
