#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports fmsl.AppCommands

' The first instance's side of APP-COMMANDS (rules 7, 10, 12, 13, 16): a request arrives as WM_COPYDATA
' with the "FMSC" tag, is answered 1 at once (taken), decided with the same strict parser a cold start
' uses, run on the UI thread, and answered by writing the response line into the reply file the
' request names - only ever inside the user's own %TEMP%\FastMediaSorter-commands folder.
'
' A verb answers in one of two ways. Most return their response at once. A verb that waits for the
' window (open) or must answer before a dialog blocks this call (open of a document, phone-code
' receive) returns Nothing and writes the reply itself, once, through the callback it was given.
Partial Public Class Main_Form

    ''' <summary>Called first for every WM_COPYDATA; True when it was a command request (handled here).</summary>
    Private Function HandleAppCommandCopyData(ByRef m As Message) As Boolean
        Dim cds As COPYDATASTRUCT
        Try
            cds = Marshal.PtrToStructure(Of COPYDATASTRUCT)(m.LParam)
        Catch ex As Exception
            Return False
        End Try
        If cds.dwData <> ViewerCommandClient.Tag_Pointer Then Return False

        Dim text As String = Nothing
        If Not SingleInstanceProtocol.TryDecodePayload(cds.cbData, cds.lpData, text) Then
            AppFileLogger.WriteLine("App command: a request with invalid or excessive data - ignored")
            Return True
        End If
        Dim request As CommandRequest = Nothing
        If Not CommandRequest.TryParse(text, request) Then
            ' Item I.3: a line that cannot be read as a request is answered `refused` with an empty requestId into the reply
            ' file it names (only the one folder of this profile); a line that names none gets nothing.
            Dim unreadablePath As String = Nothing
            Dim unreadableReason As String = Nothing
            If CommandDispatch.TryAnswerUnreadable(text, ViewerCommandTable.ReplyFolder, unreadablePath, unreadableReason) Then
                AppFileLogger.WriteLine("App command: a request line that could not be read - refused")
                m.Result = New IntPtr(1)
                BeginInvoke(New Action(Sub() AnswerUnreadableAppCommand(unreadablePath, unreadableReason)))
            Else
                AppFileLogger.WriteLine("App command: a request that is not a request line - ignored")
            End If
            Return True
        End If
        If Not CommandFiles.IsValidReplyPath(ViewerCommandTable.ReplyFolder, request) Then
            ' Never write a file where a request says: only the one folder of this profile.
            AppFileLogger.WriteLine("App command: a request with a reply path outside the reply folder - ignored")
            Return True
        End If

        ' Answer at once and act after (the sender counts a delivery only on 1, rule 8).
        m.Result = New IntPtr(1)
        Dim taken As CommandRequest = request
        BeginInvoke(New Action(Sub() RunAppCommand(taken)))
        Return True
    End Function

    ''' <summary>The answer to a line that could not be read (item I.3): `refused`, requestId `""`, the reason's own sentence.</summary>
    Private Sub AnswerUnreadableAppCommand(replyPath As String, reason As String)
        Try
            Dim response As CommandResponse = CommandResponse.ForReason(String.Empty, reason, AppCommandsText.ForReason(reason))
            CommandFiles.WriteWholeText(replyPath, response.ToLine() & ControlChars.Lf)
        Catch ex As Exception
            AppFileLogger.LogException("App command: writing the answer to an unreadable line", ex)
        End Try
    End Sub

    ''' <summary>Busy (rule 10): a modal window is open, the welcome wizard is up, or the window is going.</summary>
    Private Function AppCommandBusy() As Boolean
        If IsDisposed OrElse Disposing OrElse Not IsHandleCreated Then Return True
        If welcome_Open Then Return True
        If Not Enabled Then Return True
        For Each f As Form In Application.OpenForms
            If f IsNot Me AndAlso Not f.IsDisposed AndAlso f.Modal Then Return True
        Next
        Return False
    End Function

    Private Sub RunAppCommand(request As CommandRequest)
        Dim response As CommandResponse
        Dim closeAfter As Boolean = False
        Try
            Dim manifest As CommandManifest = ViewerCommandTable.Build()
            Dim decision As DispatchDecision = CommandDispatch.Decide(request, manifest, AppCommandsSetting.IsAllowed(),
                                                                      AppCommandBusy(), SingleInstanceProtocol.IsProcessElevated())
            If decision.RefusalReason IsNot Nothing Then
                Dim message As String = If(decision.Refusal IsNot Nothing, AppCommandsText.ForRefusal(decision.Refusal),
                                           AppCommandsText.ForReason(decision.RefusalReason))
                response = CommandResponse.ForReason(request.RequestId, decision.RefusalReason, message)
            Else
                response = ExecuteAppCommand(request, decision.Parsed, closeAfter,
                                             Sub(late As CommandResponse) FinishAppCommand(request, late, False))
            End If
        Catch ex As Exception
            AppFileLogger.LogException("App command", ex)
            response = CommandResponse.ForReason(request.RequestId, CommandReasons.OperationFailed,
                                                 AppCommandsText.ForReason(CommandReasons.OperationFailed))
        End Try

        ' Nothing: the verb answers later, once, by itself.
        If response Is Nothing Then Return
        FinishAppCommand(request, response, closeAfter)
    End Sub

    ''' <summary>The one place a reply is logged (verb, origin, caller, outcome - never a value, rule 16) and written.</summary>
    Private Sub FinishAppCommand(request As CommandRequest, response As CommandResponse, closeAfter As Boolean)
        AppFileLogger.WriteLine(CommandDispatch.DescribeForLog(request, response))
        Try
            CommandFiles.WriteWholeText(request.ReplyPath,
                response.ToBoundedLine(AppCommandsText.ForReason(CommandReasons.ResponseTooLarge)) & ControlChars.Lf)
        Catch ex As Exception
            AppFileLogger.LogException("App command: writing the reply", ex)
        End Try
        If closeAfter Then Close()
    End Sub

    Private Function ExecuteAppCommand(request As CommandRequest, parsed As ParsedCommand, ByRef closeAfter As Boolean,
                                       finish As Action(Of CommandResponse)) As CommandResponse
        Select Case parsed.Spec.Name
            Case "status"
                Dim data As New fmsl.AppCommands.JsonObject()
                data.Add("version", Application.ProductVersion)
                data.Add("pid", Environment.ProcessId)
                data.Add("exe", IO.Path.GetFileName(If(Environment.ProcessPath, ViewerCommandTable.ExeName)))
                data.Add("ready", True)
                data.Add("acceptsRestrictedCommands", AppCommandsSetting.IsAllowed())
                Return CommandResponse.Done(request.RequestId,
                    Localization.TF("Приложение {0} {1} запущено и готово.", ProductNames.Compact, Application.ProductVersion), data)
            Case "show"
                RestoreMainWindow()
                Return CommandResponse.Done(request.RequestId, Localization.T("Окно показано."))
            Case "quit"
                If (file_Op_Queue IsNot Nothing AndAlso file_Op_Queue.PendingCount > 0) OrElse BatchIsRunning Then
                    ' Closing now would ask about the unfinished operations: a dialog no command may depend on (rule 4).
                    ' APP-COMMANDS 0.2 item N.1: the refusal says what runs; --stop-jobs is the consent - the batch is told to
                    ' start nothing further (the same call the window's own close makes) and the close drains the one in flight.
                    If Not parsed.Flag("stop-jobs") Then
                        Return CommandResponse.ForReason(request.RequestId, CommandReasons.Busy, AppCommandsText.QuitWhileJobsRun())
                    End If
                    CancelBatch()
                End If
                closeAfter = True
                Return CommandResponse.Done(request.RequestId, Localization.T("Приложение закрыто."))
            Case "open"
                Return ExecuteOpen(request, parsed, finish)
            Case "phone-code receive"
                Return StartPhoneCodeReceive(request.RequestId, OpenTargets.ResolveAgainst(request.WorkingDir, parsed.Value("secret-file")))
            Case "file current", "file next", "file previous", "file first", "file last", "file goto"
                Return ExecuteFileVerb(request, parsed, finish)
            Case "slideshow start", "slideshow stop"
                Return ExecuteSlideshow(request, parsed)
            Case "screenshot window"
                Return ExecuteScreenshot(request, parsed)
            Case Else
                Return CommandResponse.ForReason(request.RequestId, CommandReasons.UnknownVerb, AppCommandsText.ForReason(CommandReasons.UnknownVerb))
        End Select
    End Function

    ' ---------------------------------------------------------------------------------------------
    ' open (L0317): the bare launch as a verb. The entry is the one the window message has always used
    ' (External_message -> ProcessArgument -> the open coordinator); only the answer is new.
    ' ---------------------------------------------------------------------------------------------

    ''' <summary>One open the command is waiting on: answered once, when the window shows the target (or fails to).</summary>
    Private NotInheritable Class AppOpenWatch
        Friend ReadOnly Target As String
        Friend ReadOnly Finish As Action(Of CommandResponse)
        Friend ReadOnly RequestId As String
        Friend ReadOnly DeadlineMs As Long
        Friend Replied As Boolean
        Friend Poll As System.Windows.Forms.Timer

        Friend Sub New(requestId As String, target As String, finish As Action(Of CommandResponse), deadlineMs As Long)
            Me.RequestId = requestId
            Me.Target = target
            Me.Finish = finish
            Me.DeadlineMs = deadlineMs
        End Sub
    End Class

    ''' <summary>The watch the next accepted explicit open belongs to; set around the one synchronous call that starts it.</summary>
    Private app_Open_Watch As AppOpenWatch

    ''' <summary>What the filesystem said about the argument the coordinator last applied (OpenHost.Apply).</summary>
    Private app_Last_Probe As ArgumentProbe

    ''' <summary>The receiver bound for a landing open: the manifest's completion bound less the sender's margin.</summary>
    Private Const AppOpenMarginMs As Integer = 5000

    Private Function ExecuteOpen(request As CommandRequest, parsed As ParsedCommand, finish As Action(Of CommandResponse)) As CommandResponse
        ' A request that carries --target-file instead of the target was not read by its sender (item H.2: the sender
        ' reads the file); the first instance never reads a file a request names, so it has no target.
        Dim target As String = If(parsed.Positionals.Count > 0, parsed.Positionals(0), String.Empty)
        Dim targetClass As OpenTargetClass = OpenTargets.Classify(target)
        Dim resolved As String = OpenTargets.ResolveAgainst(request.WorkingDir, target)
        If targetClass = OpenTargetClass.Internal OrElse targetClass = OpenTargetClass.Empty OrElse resolved Is Nothing Then
            ' An internal ::name:: token would reach the phone-code door without the gate of that verb.
            Return CommandResponse.ForReason(request.RequestId, CommandReasons.BadArgument,
                AppCommandsText.ForRefusal(New ParseRefusal With {.Reason = CommandReasons.BadArgument,
                                                                  .Detail = CommandLineParser.DetailBadValue, .Token = "target"}))
        End If
        Dim argument As String = OpenTargets.ToLegacyArgument(resolved, parsed.Flag("no-back"))

        If targetClass <> OpenTargetClass.Path Then
            ' A connect or confirmation dialog may open and it blocks the call that shows it, so the answer is
            ' written first: "handed over". Nothing is changed until the person confirms.
            Dim data As New fmsl.AppCommands.JsonObject()
            data.Add("kind", OpenTargets.KindResource)
            BeginInvoke(New Action(Sub() External_message(argument)))
            Return CommandResponse.Done(request.RequestId, Localization.T("Передано в окно, которое читает этот документ или адрес."), data)
        End If

        Dim watch As New AppOpenWatch(request.RequestId, resolved, finish,
                                      MonotonicClock.NowMs() + ViewerCommandTable.Build().Find("open").CompletionMs() - AppOpenMarginMs)
        app_Open_Watch = watch
        Try
            External_message(argument)
        Catch
            app_Open_Watch = Nothing
            Throw
        End Try
        If app_Open_Watch Is watch Then
            ' Nothing took the watch: the open did not start (an unsaved text asked first and was declined).
            app_Open_Watch = Nothing
            ReplyOpen(watch, CommandResponse.ForReason(watch.RequestId, CommandReasons.Cancelled,
                Localization.T("Открытие отменено или заменено другим действием.")))
        End If
        Return Nothing
    End Function

    ''' <summary>Called by ProcessArgumentAsync with the task of the open it just accepted.</summary>
    Private Sub NoteAppOpenTask(task As Task(Of OpenOutcome))
        Dim watch As AppOpenWatch = app_Open_Watch
        If watch Is Nothing Then Return
        app_Open_Watch = Nothing
        task.ContinueWith(
            Sub(t As Task(Of OpenOutcome))
                Try
                    BeginInvoke(New Action(Sub() AppOpenLanded(watch, t)))
                Catch ex As Exception
                    ' The window is gone: nobody is left to answer.
                End Try
            End Sub, TaskScheduler.Default)
    End Sub

    Private Sub ReplyOpen(watch As AppOpenWatch, response As CommandResponse)
        If watch.Replied Then Return
        watch.Replied = True
        If watch.Poll IsNot Nothing Then
            watch.Poll.Stop()
            watch.Poll.Dispose()
            watch.Poll = Nothing
        End If
        watch.Finish(response)
    End Sub

    Private Sub AppOpenLanded(watch As AppOpenWatch, task As Task(Of OpenOutcome))
        If watch.Replied Then Return
        Try
            If task.IsFaulted OrElse task.IsCanceled Then
                ReplyOpen(watch, New CommandResponse With {.RequestId = watch.RequestId, .Status = CommandStatus.Failed,
                                                           .ExitCode = CommandExit.Failed, .Message = Localization.T("Команда не выполнена.")})
                Return
            End If
            Select Case task.Result
                Case OpenOutcome.Applied
                    AppOpenFacts(watch)
                Case OpenOutcome.Unavailable
                    ReplyOpen(watch, NetworkAnswer(watch.RequestId))
                Case Else
                    ReplyOpen(watch, CommandResponse.ForReason(watch.RequestId, CommandReasons.Cancelled,
                        Localization.T("Открытие отменено или заменено другим действием.")))
            End Select
        Catch ex As Exception
            AppFileLogger.LogException("App command: open landing", ex)
            ReplyOpen(watch, New CommandResponse With {.RequestId = watch.RequestId, .Status = CommandStatus.Failed,
                                                       .ExitCode = CommandExit.Failed, .Message = Localization.T("Команда не выполнена.")})
        End Try
    End Sub

    Private Shared Function NetworkAnswer(requestId As String) As CommandResponse
        Return New CommandResponse With {.RequestId = requestId, .Status = CommandStatus.Failed, .ExitCode = CommandExit.Network,
                                         .Message = Localization.T("Сетевой ресурс не ответил.")}
    End Function

    ''' <summary>The window committed the open: say what it turned out to be, once the listing is in.</summary>
    Private Sub AppOpenFacts(watch As AppOpenWatch)
        Dim probe As ArgumentProbe = app_Last_Probe
        If probe Is Nothing OrElse (Not probe.Exists AndAlso Not probe.IsDirectory) Then
            If probe IsNot Nothing AndAlso probe.Denied Then
                ' Locked by a writer: the existing watcher opens it when it unlocks, so this is not a failure and not proof.
                ReplyOpen(watch, CommandResponse.ForReason(watch.RequestId, CommandReasons.Timeout,
                    Localization.T("Файл занят, откроется сам, когда освободится.")))
            ElseIf probe IsNot Nothing AndAlso probe.Missing Then
                ReplyOpen(watch, CommandResponse.ForReason(watch.RequestId, CommandReasons.TargetMissing,
                    Localization.T("Файл или папка не найдены.")))
            Else
                ReplyOpen(watch, NetworkAnswer(watch.RequestId))
            End If
            Return
        End If

        If Not IsFolderScanRunning() Then
            ReplyOpenFacts(watch, probe)
            Return
        End If
        ' A slow listing (a share that is waking up): look again until it lands or the bound runs out.
        watch.Poll = New System.Windows.Forms.Timer With {.Interval = 100}
        AddHandler watch.Poll.Tick,
            Sub()
                If watch.Replied Then Return
                If Not IsFolderScanRunning() Then
                    ReplyOpenFacts(watch, probe)
                ElseIf MonotonicClock.NowMs() >= watch.DeadlineMs Then
                    ReplyOpen(watch, CommandResponse.ForReason(watch.RequestId, CommandReasons.Timeout,
                        AppCommandsText.ForReason(CommandReasons.Timeout)))
                End If
            End Sub
        watch.Poll.Start()
    End Sub

    Private Sub ReplyOpenFacts(watch As AppOpenWatch, probe As ArgumentProbe)
        Dim kind As String
        If probe.IsDirectory Then
            kind = OpenTargets.KindFolder
        ElseIf ArchiveEntryFilter.IsArchivePath(watch.Target) Then
            kind = OpenTargets.KindArchive
        Else
            kind = KindWord(MediaKindClassifier.KindOf(watch.Target))
        End If
        Dim data As New fmsl.AppCommands.JsonObject()
        data.Add("kind", kind)
        data.Add("count", total_File_Count)
        ReplyOpen(watch, CommandResponse.Done(watch.RequestId, Localization.T("Открыто."), data))
    End Sub

    Private Shared Function KindWord(kind As MediaKind) As String
        Select Case kind
            Case MediaKind.Image : Return OpenTargets.KindImage
            Case MediaKind.Video : Return OpenTargets.KindVideo
            Case MediaKind.Audio : Return OpenTargets.KindAudio
            Case MediaKind.Document, MediaKind.Book : Return OpenTargets.KindDocument
            Case MediaKind.Container : Return OpenTargets.KindArchive
            Case Else : Return OpenTargets.KindOther
        End Select
    End Function

    ' ---------------------------------------------------------------------------------------------
    ' phone-code receive (L0317): the verb behind ::fms-phone-code:: and ::fms-phone-code-file::.
    ' ---------------------------------------------------------------------------------------------

    ''' <summary>
    ''' Reads the file the caller named (rule 5: UTF-8, one line end dropped, at most 32 KB - a QR picture goes to the
    ''' panel's own bounded reader), then queues the import dialog and answers BEFORE it opens: the dialog is modal
    ''' and blocks the call that shows it. The code is held only by the queued action, never written or logged.
    ''' </summary>
    Private Function StartPhoneCodeReceive(requestId As String, path As String) As CommandResponse
        Dim badFile As CommandResponse = CommandResponse.ForReason(requestId, CommandReasons.BadArgument,
            AppCommandsText.ForRefusal(New ParseRefusal With {.Reason = CommandReasons.BadArgument,
                                                              .Detail = CommandLineParser.DetailBadValue, .Token = "--secret-file"}))
        If String.IsNullOrEmpty(path) Then Return badFile
        If Not File.Exists(path) Then
            Return CommandResponse.ForReason(requestId, CommandReasons.TargetMissing, Localization.T("Файл или папка не найдены."))
        End If
        If PhoneConnectionIntake.IsImageFile(path) Then
            BeginInvoke(New Action(Sub()
                                       RestoreMainWindow()
                                       OpenPhoneConnectionCodeFile(path)
                                   End Sub))
        Else
            Dim secret As String = Nothing
            If Not CommandFiles.TryReadSecret(path, secret) OrElse String.IsNullOrWhiteSpace(secret) Then Return badFile
            BeginInvoke(New Action(Sub()
                                       RestoreMainWindow()
                                       OpenPhoneConnectionCode(secret)
                                   End Sub))
        End If
        Return CommandResponse.Done(requestId, Localization.T("Окно импорта показано."))
    End Function

    ''' <summary>A cold <c>phone-code receive</c> made this process the viewer: once its window is up, the file is taken
    ''' as a running copy takes it. Called from Main_Form_Shown.</summary>
    Private Sub RunColdAppCommand()
        Dim file As String = ViewerCommandClient.TakeColdPhoneCodeFile()
        If file Is Nothing Then Return
        Dim response As CommandResponse = StartPhoneCodeReceive(Guid.NewGuid().ToString("D"), file)
        If response.ExitCode <> CommandExit.Done Then
            AppFileLogger.WriteLine("App command: phone-code receive at start - " & response.Status & "/" & response.Reason)
        End If
    End Sub

End Class
#End If
