#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Windows.Forms
Imports fmsl.AppCommands

' The viewer's drive-and-observe verbs of APP-COMMANDS (L0318): the six moves of the listing, the slideshow
' and the picture of the window. Each calls the ONE implementation the key or the button already uses -
' ReadShowMediaFile / JumpTo, SetSlideShow / SetRandomSlideShow, SlideShowStop - so a command and a keypress
' can never disagree about what a move is. What is new is only the answer: the arithmetic of the listing is
' the pure ViewerNavigation.Plan, and a call the window silently refused (the 40 ms throttle, a folder being
' read, a dialog) is told from one it took by the display generation moving on.
Partial Public Class Main_Form

    ''' <summary>The completion bound of a move less what the sender keeps for itself.</summary>
    Private Const AppDriveMarginMs As Integer = 1000

    ''' <summary>One move the command is waiting on because the folder is being read off the UI thread (the first flip after
    ''' an external open lists the folder). Answered once, when the read lands or the bound runs out.</summary>
    Private NotInheritable Class AppDriveWatch
        Friend ReadOnly RequestId As String
        Friend ReadOnly Generation As Integer
        Friend ReadOnly Finish As Action(Of CommandResponse)
        Friend ReadOnly DeadlineMs As Long
        Friend ReadOnly WithKind As Boolean
        Friend Replied As Boolean
        Friend Poll As System.Windows.Forms.Timer

        Friend Sub New(requestId As String, generation As Integer, finish As Action(Of CommandResponse), deadlineMs As Long, withKind As Boolean)
            Me.RequestId = requestId
            Me.Generation = generation
            Me.Finish = finish
            Me.DeadlineMs = deadlineMs
            Me.WithKind = withKind
        End Sub
    End Class

    ' ---------------------------------------------------------------------------------------------
    ' answers
    ' ---------------------------------------------------------------------------------------------

    Private Shared Function DriveBusyAnswer(requestId As String) As CommandResponse
        Return CommandResponse.ForReason(requestId, CommandReasons.Busy, AppCommandsText.ForReason(CommandReasons.Busy))
    End Function

    Private Shared Function DriveBadArgumentAnswer(requestId As String, token As String) As CommandResponse
        Return CommandResponse.ForReason(requestId, CommandReasons.BadArgument,
            AppCommandsText.ForRefusal(New ParseRefusal With {.Reason = CommandReasons.BadArgument,
                                                              .Detail = CommandLineParser.DetailBadValue, .Token = token}))
    End Function

    Private Shared Function DriveNothingShownAnswer(requestId As String) As CommandResponse
        Return CommandResponse.ForReason(requestId, CommandReasons.TargetMissing, Localization.T("Нет открытого файла."))
    End Function

    Private Shared Function DriveFailedAnswer(requestId As String) As CommandResponse
        Return CommandResponse.ForReason(requestId, CommandReasons.OperationFailed, AppCommandsText.ForReason(CommandReasons.OperationFailed))
    End Function

    ''' <summary>A move the window cannot take now - what the key would be refused for, or would answer with a dialog that no
    ''' command may depend on (rule 4): a folder or an archive being read, a text edit that is unsaved or being saved.</summary>
    Private Function AppDriveBlocked() As Boolean
        If IsFolderScanRunning() OrElse IsArchiveOpenPending() Then Return True
        Return text_Editing AndAlso (text_Edit_Saving OrElse TextEditDirty())
    End Function

    ''' <summary>The path of the file the window holds: what it shows, or what it is about to show when the display has just
    ''' been asked for.</summary>
    Private Function AppShownFilePath() As String
        If Not String.IsNullOrEmpty(Current_File_Name) Then Return Current_File_Name
        If total_File_Count <= 0 OrElse current_File_Index < 0 OrElse current_File_Index >= total_File_Count Then Return Nothing
        Try
            Return If(is_Files_Array_Active, files_Array(current_File_Index), files_List(current_File_Index))
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

    Private Shared Function AppShownKind(path As String) As String
        If String.IsNullOrEmpty(path) Then Return OpenTargets.KindOther
        If TextFileReader.IsTextExtension(path) Then Return ViewerNavigation.KindText
        Return KindWord(MediaKindClassifier.KindOf(path))
    End Function

    ''' <summary>The place the window holds, as the moves and <c>file current</c> return it.</summary>
    Private Function AppNavigationFacts(requestId As String, withPath As Boolean, withKind As Boolean) As CommandResponse
        Dim path As String = AppShownFilePath()
        If String.IsNullOrEmpty(path) Then Return DriveNothingShownAnswer(requestId)
        Dim data As New fmsl.AppCommands.JsonObject()
        If withPath Then data.Add("path", path)
        data.Add("index", current_File_Index + 1)
        data.Add("count", total_File_Count)
        If withKind Then data.Add("kind", AppShownKind(path))
        Return CommandResponse.Done(requestId,
            Localization.TF("Показан файл {0} из {1}.", current_File_Index + 1, total_File_Count), data)
    End Function

    ' ---------------------------------------------------------------------------------------------
    ' file current / next / previous / first / last / goto
    ' ---------------------------------------------------------------------------------------------

    Private Function ExecuteFileVerb(request As CommandRequest, parsed As ParsedCommand,
                                     finish As Action(Of CommandResponse)) As CommandResponse
        Dim verb As String = parsed.Spec.Name
        If verb = "file current" Then Return AppNavigationFacts(request.RequestId, True, True)

        Dim move As NavigationMove
        If Not ViewerNavigation.TryMoveOfVerb(verb, move) Then
            Return CommandResponse.ForReason(request.RequestId, CommandReasons.UnknownVerb, AppCommandsText.ForReason(CommandReasons.UnknownVerb))
        End If
        Dim number As Integer = 0
        If move = NavigationMove.ToNumber Then
            If Not Integer.TryParse(parsed.Value("number"), Globalization.NumberStyles.AllowLeadingSign,
                                    Globalization.CultureInfo.InvariantCulture, number) Then
                Return DriveBadArgumentAnswer(request.RequestId, "--number")
            End If
        End If

        If AppDriveBlocked() Then Return DriveBusyAnswer(request.RequestId)

        Dim plan As NavigationPlan = ViewerNavigation.Plan(move, current_File_Index, total_File_Count, number, Not was_External_Input_Previously)
        Select Case plan.Outcome
            Case NavigationOutcome.NothingShown
                Return DriveNothingShownAnswer(request.RequestId)
            Case NavigationOutcome.OutOfRange
                Return DriveBadArgumentAnswer(request.RequestId, "--number")
            Case NavigationOutcome.Stay
                Return AppNavigationFacts(request.RequestId, False, move = NavigationMove.StepForward OrElse move = NavigationMove.StepBack)
        End Select

        Dim withKind As Boolean = move = NavigationMove.StepForward OrElse move = NavigationMove.StepBack
        Dim generation As Integer = media_Generation
        ' A running show continues and restarts its timer, or stops by the person's own preference - what every
        ' manual navigation does (MEDIA-VIEWER 1.0 rule 5).
        ManualNavigationStopsShow()
        Select Case move
            Case NavigationMove.StepForward
                ReadShowMediaFile(Mode_Next)
            Case NavigationMove.StepBack
                ReadShowMediaFile(Mode_Prev)
            Case NavigationMove.ToFirst
                JumpTo(plan.Target, "первый файл")
            Case NavigationMove.ToLast
                JumpTo(plan.Target, "последний файл")
            Case Else
                JumpTo(plan.Target)
        End Select

        If IsFolderScanRunning() Then
            ' The first flip after an external open lists the folder off the UI thread and runs the same read again
            ' when it lands: answer then, not before.
            Dim watch As New AppDriveWatch(request.RequestId, generation, finish,
                                           MonotonicClock.NowMs() + parsed.Spec.CompletionMs() - AppDriveMarginMs, withKind)
            watch.Poll = New System.Windows.Forms.Timer With {.Interval = 50}
            AddHandler watch.Poll.Tick, Sub() AppDriveWatchTick(watch)
            watch.Poll.Start()
            Return Nothing
        End If
        Return AppDriveLanded(request.RequestId, generation, withKind)
    End Function

    ''' <summary>The window took the move when its display generation moved on; otherwise the call was refused or replaced
    ''' (the 40 ms throttle, a dialog) and nothing changed on screen.</summary>
    Private Function AppDriveLanded(requestId As String, generation As Integer, withKind As Boolean) As CommandResponse
        If media_Generation = generation Then Return DriveBusyAnswer(requestId)
        Return AppNavigationFacts(requestId, False, withKind)
    End Function

    Private Sub AppDriveWatchTick(watch As AppDriveWatch)
        If watch.Replied Then Return
        Dim response As CommandResponse = Nothing
        Try
            If IsFolderScanRunning() Then
                If MonotonicClock.NowMs() < watch.DeadlineMs Then Return
                response = CommandResponse.ForReason(watch.RequestId, CommandReasons.Timeout, AppCommandsText.ForReason(CommandReasons.Timeout))
            Else
                response = AppDriveLanded(watch.RequestId, watch.Generation, watch.WithKind)
            End If
        Catch ex As Exception
            AppFileLogger.LogException("App command: move landing", ex)
            response = DriveFailedAnswer(watch.RequestId)
        End Try
        watch.Replied = True
        watch.Poll.Stop()
        watch.Poll.Dispose()
        watch.Poll = Nothing
        watch.Finish(response)
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' slideshow start / stop
    ' ---------------------------------------------------------------------------------------------

    Private Function ExecuteSlideshow(request As CommandRequest, parsed As ParsedCommand) As CommandResponse
        If parsed.Spec.Name = "slideshow stop" Then
            ' The single stop point every key and click already goes through.
            SlideShowStop()
            Return CommandResponse.Done(request.RequestId, Localization.T("Слайд-шоу остановлено."))
        End If

        Dim interval As Integer = 0
        If parsed.Has("interval-ms") Then
            If Not Integer.TryParse(parsed.Value("interval-ms"), Globalization.NumberStyles.AllowLeadingSign,
                                    Globalization.CultureInfo.InvariantCulture, interval) OrElse
               Not ViewerNavigation.IsSlideshowIntervalInRange(interval) Then
                Return DriveBadArgumentAnswer(request.RequestId, "--interval-ms")
            End If
        End If
        If AppDriveBlocked() Then Return DriveBusyAnswer(request.RequestId)
        If total_File_Count <= 0 OrElse String.IsNullOrEmpty(AppShownFilePath()) Then Return DriveNothingShownAnswer(request.RequestId)

        Dim randomAsked As Boolean = parsed.Flag("random")
        ' The toolbar button's own order follows the setting for the slideshow order; the random button is random.
        Dim wantRandom As Boolean = randomAsked OrElse (modern_Preferences IsNot Nothing AndAlso modern_Preferences.SlideshowRandomOrder <> "natural")
        If Not (Is_slide_show_mode AndAlso interval = 0 AndAlso is_Slide_Show_Random_Mode = wantRandom) Then
            ' A press on a running show halves its pause; a command restarts it with the pause it has, or the one asked for.
            Dim pause As Integer = If(interval > 0, interval, If(Is_slide_show_mode, SlideShowTimer.Interval, 0))
            If randomAsked Then
                SetRandomSlideShow(False, pause)
            Else
                SetSlideShow(False, pause)
            End If
        End If

        If Not Is_slide_show_mode Then Return DriveBusyAnswer(request.RequestId)
        Dim data As New fmsl.AppCommands.JsonObject()
        data.Add("running", True)
        data.Add("intervalMs", SlideShowTimer.Interval)
        Return CommandResponse.Done(request.RequestId, Localization.T("Слайд-шоу запущено."), data)
    End Function

    ' ---------------------------------------------------------------------------------------------
    ' screenshot window
    ' ---------------------------------------------------------------------------------------------

    <StructLayout(LayoutKind.Sequential)>
    Private Structure CaptureRect
        Friend Left As Integer
        Friend Top As Integer
        Friend Right As Integer
        Friend Bottom As Integer
    End Structure

    <DllImport("user32.dll", EntryPoint:="GetWindowRect", SetLastError:=True)>
    Private Shared Function CaptureGetWindowRect(hWnd As IntPtr, ByRef rect As CaptureRect) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="PrintWindow", SetLastError:=True)>
    Private Shared Function CapturePrintWindow(hWnd As IntPtr, hdcBlt As IntPtr, flags As UInteger) As Boolean
    End Function

    ''' <summary>PW_RENDERFULLCONTENT: the window as the compositor holds it, including the child windows that draw themselves.</summary>
    Private Const CapturePwRenderFullContent As UInteger = 2UI

    ''' <summary>The window as one PNG, its whole rectangle; nothing when it cannot be drawn (not a window, zero size, the
    ''' system refused).</summary>
    Private Function CaptureWindowPng(ByRef width As Integer, ByRef height As Integer) As Byte()
        Dim rect As CaptureRect
        If Not CaptureGetWindowRect(Handle, rect) Then Return Nothing
        width = rect.Right - rect.Left
        height = rect.Bottom - rect.Top
        If width <= 0 OrElse height <= 0 Then Return Nothing
        Using bmp As New Bitmap(width, height, PixelFormat.Format24bppRgb)
            Using g As Graphics = Graphics.FromImage(bmp)
                Dim hdc As IntPtr = g.GetHdc()
                Dim drawn As Boolean
                Try
                    drawn = CapturePrintWindow(Handle, hdc, CapturePwRenderFullContent)
                Finally
                    g.ReleaseHdc(hdc)
                End Try
                If Not drawn Then Return Nothing
            End Using
            Using stream As New MemoryStream()
                bmp.Save(stream, ImageFormat.Png)
                Return stream.ToArray()
            End Using
        End Using
    End Function

    Private Function ExecuteScreenshot(request As CommandRequest, parsed As ParsedCommand) As CommandResponse
        Dim captured As DateTime = DateTime.Now
        Dim target As String
        Try
            target = ViewerNavigation.ResolveScreenshotTarget(parsed.Value("out"), request.WorkingDir, captured,
                                                              Function(p As String) Directory.Exists(p),
                                                              Function(p As String) File.Exists(p))
        Catch ex As ArgumentException
            target = Nothing
        Catch ex As NotSupportedException
            target = Nothing
        End Try
        If target Is Nothing Then Return DriveBadArgumentAnswer(request.RequestId, "--out")
        ' CAPTURE-OUTPUT 0.8 section 5: a path the caller names is used exactly as given and its folder is never created;
        ' a missing folder is refused up front, before the window is captured, with the named reason.
        Dim outFolder As String = Path.GetDirectoryName(target)
        If Not String.IsNullOrEmpty(outFolder) AndAlso Not Directory.Exists(outFolder) Then
            Return CommandResponse.ForReason(request.RequestId, CommandReasons.TargetMissing, Localization.T("Файл или папка не найдены."))
        End If
        Dim overwrite As Boolean = parsed.Flag("overwrite")
        Dim existsAnswer As New CommandResponse With {
            .RequestId = request.RequestId, .Status = CommandStatus.Failed, .ExitCode = CommandExit.NotReady,
            .Message = Localization.TF("Файл уже существует; чтобы заменить его, добавьте параметр «{0}».", "--overwrite")}
        If Not overwrite AndAlso File.Exists(target) Then Return existsAnswer

        If WindowState = FormWindowState.Minimized OrElse Not Visible OrElse Not IsHandleCreated Then Return DriveBusyAnswer(request.RequestId)

        Dim width As Integer = 0
        Dim height As Integer = 0
        Dim png As Byte() = CaptureWindowPng(width, height)
        If png Is Nothing Then Return DriveFailedAnswer(request.RequestId)
        png = PngTimeChunk.Insert(png, captured)
        Try
            If Not CommandFiles.TryWriteOut(target, png, overwrite) Then Return existsAnswer
        Catch ex As DirectoryNotFoundException
            ' The folder vanished between the check and the write (a DirectoryNotFoundException is an IOException: this case comes first).
            Return CommandResponse.ForReason(request.RequestId, CommandReasons.TargetMissing, Localization.T("Файл или папка не найдены."))
        Catch ex As IOException
            If ViewerNavigation.IsDiskFullHResult(ex.HResult) Then
                Return CommandResponse.ForReason(request.RequestId, CommandReasons.NoSpace, Localization.T("Не хватает места для файла."))
            End If
            AppFileLogger.LogException("App command: screenshot", ex)
            Return CommandResponse.ForReason(request.RequestId, CommandReasons.OperationFailed, Localization.T("Не удалось сохранить снимок."))
        Catch ex As UnauthorizedAccessException
            AppFileLogger.LogException("App command: screenshot", ex)
            Return CommandResponse.ForReason(request.RequestId, CommandReasons.OperationFailed, Localization.T("Не удалось сохранить снимок."))
        Catch ex As ArgumentException
            ' A name the file system cannot hold (a character no name may carry): the caller's mistake, not a failure.
            Return DriveBadArgumentAnswer(request.RequestId, "--out")
        Catch ex As NotSupportedException
            Return DriveBadArgumentAnswer(request.RequestId, "--out")
        End Try

        Dim data As New fmsl.AppCommands.JsonObject()
        data.Add("width", width)
        data.Add("height", height)
        Return CommandResponse.Done(request.RequestId, Localization.T("Снимок окна сохранён."), data)
    End Function

End Class
#End If
