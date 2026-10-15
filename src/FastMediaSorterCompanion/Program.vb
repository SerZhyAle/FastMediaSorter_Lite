Imports System.Drawing
Imports System.Diagnostics
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms

''' <summary>
''' Entry point for the Share Manager (see ProductNames.ShareManager). Owns single-instance
''' enforcement and the viewer -&gt; Companion wake handshake, then hands off to the
''' tray-resident <see cref="TrayContext"/>. This is the Companion analogue of
''' the viewer's <c>Application_Events.vb</c> (mutex + WM_COPYDATA), with the roles
''' reversed: the viewer is the sender, Companion the receiver.
''' </summary>
Friend Module Program

    ''' <summary>Frozen technical anchor (CLAUDE.md light-rebrand rule) - never renamed.</summary>
    Public Const MutexName As String = "FastMediaSorterCompanionSingleInstanceMutex"

    ''' <summary>WM_COPYDATA payload meaning "just show your window" - no folder to share.
    ''' Frozen: the viewer sends this exact string (Main_Form.ShareLauncher.vb).</summary>
    Public Const ShowWindowCommand As String = "::fms-show-window::"

    ''' <summary>WM_COPYDATA payload meaning "the exe was run again with no request attached".
    ''' Companion-internal: re-running a plain launch while an instance is already up is still
    ''' a plain start, so the running instance applies the same startup option rather than
    ''' popping its window unconditionally.</summary>
    Public Const PlainStartCommand As String = "::fms-plain-start::"

    ''' <summary>Title of the hidden receiver window, used by a second instance to find the first.</summary>
    Friend Const MessageWindowTitle As String = "FastMediaSorterCompanionMessageWindow_{2f6a1c94}"

    ''' <summary>Tag for WM_COPYDATA messages to a Share Manager - one value for the viewer's wake
    ''' and this program's own second-instance forward (see <see cref="CopyDataTransport"/>).</summary>
    Friend Const CopyDataTag As Integer = CopyDataTransport.CompanionTag
    Friend ReadOnly Property CopyDataTagPointer As IntPtr
        Get
            Return New IntPtr(CopyDataTag)
        End Get
    End Property
    Friend Const MaxPayloadBytes As Integer = CopyDataTransport.MaxPayloadBytes
    Friend Const Handled_Result As Integer = 1
    ''' <summary>Autostart passes this so a logon launch stays silently in the tray
    ''' (spec §4.5.1). A launch WITHOUT it is no longer "open the window" by itself -
    ''' see <see cref="ShowWindowFlag"/> and the startup option below.</summary>
    Public Const TrayFlag As String = "--tray"

    ''' <summary>Command line form of <see cref="ShowWindowCommand"/>: "the user asked for the
    ''' window", so it opens whatever the "Open the manager window at startup" option says.
    ''' The viewer's Share Manager button passes it when it COLD-STARTS us (a running instance gets
    ''' the same intent over WM_COPYDATA instead).</summary>
    Public Const ShowWindowFlag As String = "--show"

    ''' <summary>WM_COPYDATA payload meaning "show the window and open the guided add-resource flow" (spec 215: the
    ''' viewer's welcome wizard, on a server row that is green). An intent with no path in it; a receiver that does not
    ''' know it is never sent it (<see cref="ShareActivationPolicy.IsAddResourceCapable"/>).</summary>
    Public Const AddResourceCommand As String = "::fms-add-resource::"
    Public Const AddResourceCameraCommand As String = "::fms-add-resource-camera::"

    ''' <summary>Command line form of <see cref="AddResourceCommand"/>, for a cold start.</summary>
    Public Const AddResourceFlag As String = "--add-resource"
    Public Const AddResourceCameraFlag As String = "--add-resource-camera"

    <STAThread>
    Friend Sub Main(args As String())
        AppFileLogger.Initialize()
        ' What only this program knows (host mode, the worker's reachability), for the hosting report
        ' of "Send the log to the author" (ticket 182 L-4) - registered before anything can ask.
        HostingContext.Register()
        ' Sort the arguments into: silent-tray flag, explicit show-window request, folder.
        Dim silentTray As Boolean = False
        Dim windowRequested As Boolean = False
        Dim addResourceRequested As Boolean = False
        Dim addResourceCameraRequested As Boolean = False
        ' Ticket 222: the viewer's schedule launcher passes ONE token ("::fms-schedule::" + a folder) - never a bare
        ' folder, which this program would take for a folder to share.
        Dim schedulePayload As String = ""
        Dim folder As String = Nothing
        If args IsNot Nothing Then
            For Each a As String In args
                If String.Equals(a, TrayFlag, StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(a, "/tray", StringComparison.OrdinalIgnoreCase) Then
                    silentTray = True
                ElseIf String.Equals(a, AddResourceCameraFlag, StringComparison.OrdinalIgnoreCase) OrElse
                       String.Equals(a, "/add-resource-camera", StringComparison.OrdinalIgnoreCase) OrElse
                       String.Equals(a, AddResourceCameraCommand, StringComparison.Ordinal) Then
                    windowRequested = True
                    addResourceCameraRequested = True
                ElseIf String.Equals(a, AddResourceFlag, StringComparison.OrdinalIgnoreCase) OrElse
                       String.Equals(a, "/add-resource", StringComparison.OrdinalIgnoreCase) OrElse
                       String.Equals(a, AddResourceCommand, StringComparison.Ordinal) Then
                    ' Same shape as the show-window marker below: an explicit request for the window, plus the
                    ' intent of what to open in it. Checked before the folder branch for the same reason.
                    windowRequested = True
                    addResourceRequested = True
                ElseIf a.StartsWith(CompanionCommand.ScheduleCommandPrefix, StringComparison.Ordinal) Then
                    ' Validated by the same parser a running instance applies; an invalid one is just a show-window.
                    windowRequested = True
                    If CompanionCommand.Parse(a).Kind = CompanionCommandKind.Schedule Then schedulePayload = a
                ElseIf String.Equals(a, ShowWindowFlag, StringComparison.OrdinalIgnoreCase) OrElse
                       String.Equals(a, "/show", StringComparison.OrdinalIgnoreCase) OrElse
                       String.Equals(a, ShowWindowCommand, StringComparison.Ordinal) Then
                    ' Checked BEFORE the folder branch: the marker has no leading "-",
                    ' so it would otherwise be taken for a path to share.
                    windowRequested = True
                ElseIf folder Is Nothing AndAlso Not String.IsNullOrWhiteSpace(a) AndAlso Not a.StartsWith("-", StringComparison.Ordinal) Then
                    folder = a
                End If
            Next
        End If

        ' What a second instance forwards to the first: the folder, an explicit show-window
        ' request, or the plain-start marker (which the running instance answers per the
        ' startup option). A silent logon launch that finds us already up forwards NOTHING -
        ' there is nothing to do and a boot must stay quiet.
        Dim payload As String
        If Not String.IsNullOrEmpty(folder) Then
            payload = folder
        ElseIf addResourceCameraRequested Then
            payload = AddResourceCameraCommand
        ElseIf addResourceRequested Then
            payload = AddResourceCommand
        ElseIf schedulePayload.Length > 0 Then
            payload = schedulePayload
        ElseIf windowRequested Then
            payload = ShowWindowCommand
        ElseIf silentTray Then
            payload = ""
        Else
            payload = PlainStartCommand
        End If

        Dim createdNew As Boolean = False
        Dim mtx As Mutex = Nothing
        Dim mutexFailure As Exception = Nothing
        Try
            mtx = New Mutex(True, MutexName, createdNew)
        Catch ex As Exception
            mutexFailure = ex
        End Try

        ' The decision is a function of what the call did (InstanceOwnership): access denied is
        ' "an instance exists" (an elevated one's DACL excludes our token), and an UNEXPECTED
        ' failure is never "first" - that used to start a second tray icon and a second manager
        ' over one worker pipe, with nothing in the log.
        Dim role As InstanceRole = InstanceOwnership.Decide(createdNew, mutexFailure)
        If InstanceOwnership.IsUnexpected(mutexFailure) Then
            AppFileLogger.LogException("Share Manager single-instance mutex", mutexFailure)
        ElseIf mutexFailure IsNot Nothing Then
            AppFileLogger.WriteLine("Mutex creation denied (UnauthorizedAccessException) - an instance exists")
        End If

        Try
            If role = InstanceRole.Unavailable Then
                AppFileLogger.WriteLine("Share Manager not started: the single-instance mutex could not be examined")
                CompanionGlobals.LoadLanguage()
                MessageBox.Show(Localization.T("Не удалось определить, запущен ли Share Manager. Второй экземпляр не запускается."),
                                ProductNames.ShareManager, MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End If

            If role = InstanceRole.Existing Then
                ' Another instance already owns the tray - forward our intent to it and exit.
                ' Never start a competing manager: if it cannot be reached, say so.
                If payload.Length > 0 Then
                    Dim forwarded As ForwardOutcome = ForwardToRunningInstance(payload)
                    If forwarded <> ForwardOutcome.Delivered Then ReportForwardFailure()
                End If
                Return
            End If
            ' Read the shared UI-language flag before any Share text is built, so
            ' Companion matches the viewer's language (invariant 9).
            CompanionGlobals.LoadLanguage()

            ManagerWelcomeStore.CaptureFreshProfile()

            ' ONE line about the machine this log came from (ticket 182 L-8): version, OS and build,
            ' Server or client, session and RDP, host mode, the components, the two services. Only
            ' the primary instance writes it - a forwarding launch exits before this point.
            HostingContext.LogStartState()

            ' Upgrade migration (spec §9.3): if logon autostart still points at the
            ' bare worker (old viewer behavior), repoint it at Companion.exe so the
            ' tray icon appears at logon. No-op otherwise.
            AutostartManager.MigrateRunTargetIfNeeded()

            ' Server edition only, once per account: register our own logon autostart, so a
            ' reboot leaves the tray icon behind and not just a running service with no way
            ' to reach it. Deliberately silent about a decision already taken - see
            ' AutostartManager.ShouldOfferAutostart. Must run AFTER the migration above so
            ' it reads the migrated value instead of racing it.
            AutostartManager.OfferForSystemServiceHostOnce()

            ' Crash logging, the way the viewer has it (ticket 054 H-11). Without it an
            ' exception escaping an Async Sub handler reached the user as the raw WinForms
            ' "Unhandled exception" dialog of a tray app, and nothing of it reached
            ' companion.log - the file "Send the log to the author" ships. Before the first
            ' window: the mode can only be set while no control exists on this thread.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
            AddHandler Application.ThreadException, AddressOf OnThreadException
            AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnUnhandledException
            AddHandler Threading.Tasks.TaskScheduler.UnobservedTaskException, AddressOf OnUnobservedTaskException

            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(False)
            Try
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
            Catch
                ' Older shells may reject PerMonitorV2 at runtime - the manifest still covers it.
            End Try
            ' Set the UI font ONCE, app-wide. Doing this (instead of Me.Font on each form)
            ' keeps the AutoScaleMode.Font baseline consistent, so forms scale correctly at
            ' 125/150/175% DPI instead of shrinking (the fixed-ClientSize-came-out-small bug).
            Try
                Application.SetDefaultFont(New Font("Segoe UI", 9.0F))
            Catch
            End Try
            ' The application theme shared with the viewer (spec 060) - after the rendering
            ' defaults above (it creates a control), before the tray menu and the first window.
            ThemeService.Initialize()
            ' The glyph before a dialog button's caption (WINDOWS-UI section 11) reads the same bundled
            ' catalog files the main window does.
            ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset

            ' Who decides whether the window shows: an EXPLICIT request always wins (a
            ' folder to share, or --show from the viewer's Share Manager button), and every
            ' other launch - logon autostart, a double-click on the exe, a script -
            ' obeys the "Open the manager window at startup" option, off by default.
            Using ctx As New TrayContext(folder, windowRequested, silentTray, addResourceRequested, schedulePayload, addResourceCameraRequested)
                Application.Run(ctx)
            End Using
        Finally
            If mtx IsNot Nothing Then
                ' Released only when this process acquired it; the handle is disposed either way.
                If role = InstanceRole.Primary Then
                    Try : mtx.ReleaseMutex() : Catch : End Try
                End If
                mtx.Dispose()
            End If
        End Try
    End Sub

    ' --- crash logging (ticket 054 H-11) ------------------------------------------

    ''' <summary>An exception escaping a UI event handler. Logged, and the tray app carries on
    ''' - the default dialog offered "Continue" anyway, with a stack trace nobody could act on.</summary>
    Private Sub OnThreadException(sender As Object, e As ThreadExceptionEventArgs)
        AppFileLogger.LogException("Unhandled UI exception", e.Exception)
    End Sub

    ''' <summary>An exception on any other thread. The runtime ends the process after this
    ''' returns; the log line is what survives it.</summary>
    Private Sub OnUnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
        AppFileLogger.LogException("Unhandled exception (terminating=" & e.IsTerminating.ToString() & ")",
                                   TryCast(e.ExceptionObject, Exception))
    End Sub

    ''' <summary>A faulted task nobody awaited - a fire-and-forget refresh, typically. Observed
    ''' here so it is logged rather than lost.</summary>
    Private Sub OnUnobservedTaskException(sender As Object, e As Threading.Tasks.UnobservedTaskExceptionEventArgs)
        AppFileLogger.LogException("Unobserved task exception", e.Exception)
        e.SetObserved()
    End Sub

    ' --- WM_COPYDATA forward to the already-running instance ------------------

    ''' <summary>
    ''' Hands the request to the running instance. The payload is refused whole if it cannot be sent
    ''' whole - never truncated, because a shorter path can name a different folder. One monotonic
    ''' deadline bounds the window search and every send; the first acknowledgement stops it. A
    ''' send that timed out has an UNCERTAIN outcome: the folder request is therefore never replayed
    ''' as a mutation by a fallback - the receiver de-duplicates what it did get.
    ''' </summary>
    Private Function ForwardToRunningInstance(payload As String) As ForwardOutcome
        Dim bytes As Byte() = Nothing
        Dim refusal As PayloadRefusal
        If CompanionCommand.Parse(payload).Kind = CompanionCommandKind.Invalid OrElse
           Not CopyDataTransport.TryEncode(payload, bytes, refusal) Then
            AppFileLogger.WriteLine("Companion forward: payload refused (" & refusal.ToString() & "), not sent")
            Return ForwardOutcome.TimedOut
        End If

        Dim manualAdvice As Boolean = False
        Dim outcome As ForwardOutcome = CopyDataTransport.RunForward(
            AddressOf MonotonicClock.NowMs,
            Sub(pauseMs) Thread.Sleep(pauseMs),
            Function(sendTimeoutMs) SendOnce(bytes, sendTimeoutMs, manualAdvice))
        If outcome = ForwardOutcome.Delivered AndAlso manualAdvice Then
            CompanionGlobals.LoadLanguage()
            MessageBox.Show(Localization.TF("Share Manager старой версии не умеет безопасно принимать папку отсюда. Обновите его или добавьте папку в нём вручную:" & vbCrLf & "{0}", payload),
                            ProductNames.ShareManager, MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
        AppFileLogger.WriteLine("Companion forward: " & outcome.ToString())
        Return outcome
    End Function

    Private Function SendOnce(bytes As Byte(), sendTimeoutMs As Integer, ByRef manualAdvice As Boolean) As ForwardAttempt
        Dim hwnd As IntPtr = FindWindowW(Nothing, MessageWindowTitle)
        ' The instance exists (we do not hold the mutex) but may not have created its message
        ' window yet: that is worth another try inside the deadline.
        If hwnd = IntPtr.Zero Then Return ForwardAttempt.NotAcknowledged

        Dim payload As String = Encoding.UTF8.GetString(bytes)
        Dim receiverVersion As Version = VersionForWindow(hwnd)
        Dim forwarded As String = CompatiblePayload(payload, receiverVersion)
        manualAdvice = Not String.Equals(payload, forwarded, StringComparison.Ordinal) AndAlso
                       CompanionCommand.Parse(payload).Kind = CompanionCommandKind.ShareFolder
        bytes = Encoding.UTF8.GetBytes(forwarded)

        Dim unmanaged As IntPtr = Marshal.AllocHGlobal(bytes.Length)
        Try
            Marshal.Copy(bytes, 0, unmanaged, bytes.Length)
            Dim cds As New COPYDATASTRUCT With {
                .dwData = CopyDataTagPointer,
                .cbData = bytes.Length,
                .lpData = unmanaged
            }
            Dim result As IntPtr = IntPtr.Zero
            Dim sent As Boolean = SendMessageTimeoutW(hwnd, WM_COPYDATA, IntPtr.Zero, cds,
                                                        SMTO_ABORTIFHUNG, CUInt(sendTimeoutMs), result) <> IntPtr.Zero
            ' Delivered only when the send succeeded AND the window answered Handled_Result.
            Return If(sent AndAlso result = New IntPtr(Handled_Result), ForwardAttempt.Acknowledged, ForwardAttempt.NotAcknowledged)
        Finally
            Marshal.FreeHGlobal(unmanaged)
        End Try
    End Function

    Friend Function CompatiblePayload(payload As String, receiverVersion As Version) As String
        Dim command As CompanionCommand = CompanionCommand.Parse(payload)
        If command.Kind = CompanionCommandKind.ShareFolder AndAlso
           ShareActivationPolicy.PlanFor(command.Folder, receiverVersion) <> ActivationPlan.SendFolder Then Return ShowWindowCommand
        If command.Kind = CompanionCommandKind.PlainStart AndAlso Not ShareActivationPolicy.IsConsentCapable(receiverVersion) Then Return ShowWindowCommand
        ' Spec 215: the add-resource intent never reaches a receiver that is not known to understand it - the plain
        ' wake is what an older Share Manager gets, and it opens its window as it always did.
        If command.Kind = CompanionCommandKind.AddResource AndAlso Not ShareActivationPolicy.IsAddResourceCapable(receiverVersion) Then Return ShowWindowCommand
        ' Ticket 222: the schedule intent likewise never reaches a receiver that is not known to understand it.
        If command.Kind = CompanionCommandKind.Schedule AndAlso Not ShareActivationPolicy.IsScheduleCapable(receiverVersion) Then Return ShowWindowCommand
        Return payload
    End Function

    Private Function VersionForWindow(hwnd As IntPtr) As Version
        Try
            Dim pid As UInteger
            GetWindowThreadProcessId(hwnd, pid)
            If pid = 0 Then Return Nothing
            Using process As Process = Process.GetProcessById(CInt(pid))
                Dim info As FileVersionInfo = FileVersionInfo.GetVersionInfo(process.MainModule.FileName)
                Return ShareActivationPolicy.VersionOf(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart)
            End Using
        Catch
            Return Nothing ' Unknown is wake-only, never permission to publish a folder.
        End Try
    End Function

    <DllImport("user32.dll")>
    Private Function GetWindowThreadProcessId(hwnd As IntPtr, ByRef pid As UInteger) As UInteger
    End Function

    ''' <summary>The running Share Manager could not be reached. Said once, plainly, instead of a
    ''' second manager appearing or nothing happening at all.</summary>
    Private Sub ReportForwardFailure()
        Try
            CompanionGlobals.LoadLanguage()
            MessageBox.Show(Localization.T("Share Manager уже запущен, но не ответил на запрос. Подождите немного и повторите; второй экземпляр не запускается."),
                            ProductNames.ShareManager, MessageBoxButtons.OK, MessageBoxIcon.Information)
        Catch ex As Exception
            AppFileLogger.LogException("Share Manager: reporting an unreachable instance", ex)
        End Try
    End Sub
    Friend Const WM_COPYDATA As Integer = &H4A
    Private Const SMTO_ABORTIFHUNG As UInteger = &H2

    <StructLayout(LayoutKind.Sequential)>
    Friend Structure COPYDATASTRUCT
        Public dwData As IntPtr
        Public cbData As Integer
        Public lpData As IntPtr
    End Structure

    <DllImport("user32.dll", EntryPoint:="FindWindowW", ExactSpelling:=True, CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Function FindWindowW(lpClassName As String, lpWindowName As String) As IntPtr
    End Function

    <DllImport("user32.dll", EntryPoint:="SendMessageTimeoutW", ExactSpelling:=True, CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Function SendMessageTimeoutW(hWnd As IntPtr, Msg As Integer, wParam As IntPtr,
                                         ByRef lParam As COPYDATASTRUCT, fuFlags As UInteger,
                                         uTimeout As UInteger, ByRef lpdwResult As IntPtr) As IntPtr
    End Function

End Module
