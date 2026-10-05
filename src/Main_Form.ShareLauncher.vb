#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms

' NOT IN THE x86 VIEWER (owner decision, 2026-07-16; epic §1.1 / O-7). The Share
' feature CANNOT work there, and never could: the Companion app it wakes is
' net10.0-windows x64, so it does not start on Windows 7/8.1 or on 32-bit Windows -
' precisely the machines the x86 exe exists for. Shipping the entry point there is
' a button that leads nowhere, so it is compiled out rather than left to fail.
'
' The gate is "#If Not NETFRAMEWORK" and not a new FEATURE_FULL constant on purpose:
' there is exactly ONE net48 build and it IS the x86 fallback, so NETFRAMEWORK already
' names the thing being cut precisely. A second constant plus a build configuration
' (the mechanism O-7 asked about) would add a way for the two to disagree and buy
' nothing. OCR and translation stay in x86 - unlike Share, they work there.

''' <summary>
''' The viewer's entire remaining surface for the Android Folder Share feature after the
''' Stage 3 migration to the standalone Companion app ("Share
''' Manager - Fast Media Sorter & Sharing", src/FastMediaSorterCompanion/) - SPECIFICATION_SHARE_COMPANION_APP.md
''' §4.2/§5. The viewer knows NOTHING about the worker, the pipe, ServerFeatures or any
''' opt-in gate - all of that is Companion's own concern now. This file only finds
''' and wakes Companion, forwarding the folder currently being viewed. Two entry
''' points remain: the folder-box right-click item (this file) and a button on the
''' Settings window's "Files and system" tab (Table_Form.ShareLauncher.vb) - both
''' call <see cref="ActivateShareEntryPoint"/>. The toolbar button and Shift+S
''' hotkey are gone (owner decision, 2026-07).
'''
''' The wake protocol reuses, in the opposite direction, the exact mutex +
''' WM_COPYDATA pattern Application_Events.vb already uses for the viewer's own
''' single-instance argument forwarding.
''' </summary>
Partial Public Class Main_Form

    Private Const CompanionMutexName As String = "FastMediaSorterCompanionSingleInstanceMutex"
    Private Const CompanionProcessName As String = "FastMediaSorterCompanion"
    Private Const CompanionExeFileName As String = "FastMediaSorterCompanion.exe"
    ''' <summary>Where a missing Share component is fetched from: the latest release,
    ''' whose setup.exe carries the component and installs over the current version
    ''' in place (same AppId, same directory, settings untouched).</summary>
    Private Const ShareDownloadPageUrl As String = "https://github.com/SerZhyAle/FastMediaSorter_Lite/releases/latest"
    ''' <summary>Empty-call marker (no folder) - Companion just shows its window.</summary>
    Private Const CompanionShowWindowCommand As String = "::fms-show-window::"
    Private Const CompanionBroadcastCommandPrefix As String = "::fms-broadcast::"
    Private Const WM_COPYDATA_SHARE As Integer = &H4A

    <StructLayout(LayoutKind.Sequential)>
    Private Structure COMPANION_COPYDATASTRUCT
        Public dwData As IntPtr
        Public cbData As Integer
        Public lpData As IntPtr
    End Structure

    Private Delegate Function EnumWindowsCompanionProc(hWnd As IntPtr, lParam As IntPtr) As Boolean

    ' Every declaration here is renamed to avoid colliding with the same imports in
    ' other Main_Form partials, so each one MUST name its real export: without an
    ' EntryPoint the runtime looks up the VB method name in user32.dll, finds nothing
    ' and throws EntryPointNotFoundException - which is how the wake silently did
    ' nothing until spec 040 (DllImportEntryPointTests guards the whole tree now).

    ''' <summary>SendMessage with a deadline (APP-ACTIVATION rule 3): a hung Share
    ''' Manager must not freeze the viewer's UI thread with it.</summary>
    <DllImport("user32.dll", EntryPoint:="SendMessageTimeoutW", SetLastError:=True)>
    Private Shared Function SendMessageTimeoutCompanion(hWnd As IntPtr, msg As Integer, wParam As IntPtr,
                                                        ByRef lParam As COMPANION_COPYDATASTRUCT,
                                                        flags As Integer, timeout As Integer,
                                                        ByRef result As IntPtr) As IntPtr
    End Function

    Private Const SMTO_ABORTIFHUNG_COMPANION As Integer = &H2

    <DllImport("user32.dll", EntryPoint:="EnumWindows", SetLastError:=True)>
    Private Shared Function EnumWindowsCompanion(callback As EnumWindowsCompanionProc, lParam As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="GetWindowThreadProcessId", SetLastError:=True)>
    Private Shared Function GetWindowThreadProcessIdCompanion(hWnd As IntPtr, ByRef processId As Integer) As Integer
    End Function

    ' Grants a target process the right to bring one of its windows to the
    ' foreground on its next attempt. Companion runs in the background (tray), so
    ' when we wake it, Windows would otherwise block its SetForegroundWindow and the
    ' Share Manager window would open silently BEHIND us. We are the foreground
    ' process at the moment of the click, so we are allowed to hand that right over.
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function AllowSetForegroundWindow(dwProcessId As Integer) As Boolean
    End Function

    ''' <summary>ASFW_ANY - allow any process to set the foreground window (used
    ''' before a cold Process.Start, when we don't yet have the child's PID).</summary>
    Private Const ASFW_ANY As Integer = -1

    ' The right-click item that leads here now lives in Main_Form.FolderMenu.vb, next to
    ' "Select folder..": the menu belongs to the folder box, not to the Share feature.
    ' It moved for a reason - the plain "cmbox_Media_Folder.ContextMenuStrip = .."
    ' that used to be here never appeared on an editable ComboBox, so the command was
    ' effectively unreachable. See that file.

    ''' <summary>Settings window "Manage the SFTP server.." button: open the Share
    ''' Manager's OWN window (a management entry point), never routed into the
    ''' share-this-folder wizard. Forwarding the currently-viewed folder (as the
    ''' folder-box right-click does) pushes a tray-resident Companion straight into
    ''' the package wizard for that folder - not what "Manage.." asks for - so this
    ''' path deliberately passes no folder and just raises the manager window, which
    ''' is also the most robust wake (show-window, no wizard construction to fail).</summary>
    Friend Sub OpenShareManagerWindow()
        ActivateShareEntryPoint(managerOnly:=True)
    End Sub

    ''' <summary>The one action the viewer knows about sharing: find/wake the Share
    ''' Manager. When <paramref name="managerOnly"/> is False (the
    ''' folder-box right-click) it offers the folder currently being viewed so
    ''' Companion can put "share this folder" to the user; when True (the Settings button) it
    ''' sends nothing and just raises the manager window. Not found next to this
    ''' exe -&gt; a clear message, never a silent no-op (graceful degradation - the
    ''' app never fails silently when an optional companion component is missing).
    '''
    ''' A folder is sent only to a Share Manager that is known to ask the user before it shares
    ''' one (ShareActivationPolicy); an older one is woken and the user told to add the folder.
    ''' A Share Manager that is running but does not answer is reported, never "fixed" by
    ''' starting a second one or by replaying the request (an uncertain send is not retried
    ''' as a mutation).</summary>
    Friend Async Sub ActivateShareEntryPoint(Optional managerOnly As Boolean = False)
        Try
            Await ActivateShareEntryPointAsync(managerOnly)
        Catch ex As Exception
            AppFileLogger.LogException("ShareLauncher", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Viewer entry point for broadcasting a file or folder to StreamsPlayer.
    ''' Forwards ::fms-broadcast::<path> to Companion.
    ''' </summary>
    Friend Async Sub ActivateBroadcastEntryPoint(targetPath As String)
        Try
            Await ActivateBroadcastEntryPointAsync(targetPath)
        Catch ex As Exception
            AppFileLogger.LogException("BroadcastLauncher", ex)
        End Try
    End Sub

    Private Async Function ActivateBroadcastEntryPointAsync(targetPath As String) As Task
        If String.IsNullOrWhiteSpace(targetPath) Then Return
        Dim exePath As String = CompanionExePath()
        If String.IsNullOrEmpty(exePath) OrElse Not File.Exists(exePath) Then
            AppFileLogger.WriteLine("BroadcastLauncher: Companion exe not found at '" & If(exePath, "") & "'")
            OfferShareComponentDownload()
            Return
        End If

        Dim running As Boolean? = ProbeCompanionMutex()
        Dim payload As String = CompanionBroadcastCommandPrefix & targetPath

        If running = True Then
            AppFileLogger.WriteLine("BroadcastLauncher: Companion running - forwarding broadcast command")
            Dim outcome As ForwardOutcome = Await Task.Run(Function() ForwardToCompanion(payload))
            If outcome = ForwardOutcome.Delivered Then Return
            AppFileLogger.WriteLine("BroadcastLauncher: Companion running but not reachable (" & outcome.ToString() & ")")
            ' News, not a question: the status line (and, in full screen without panels, a toast) says it (124 UX-206).
            Notify(Localization.T("Share Manager уже запущен, но не ответил на запрос. Подождите немного и повторите; второй экземпляр не запускается."), NotifyLevel.Warning)
            Return
        End If

        ColdStartCompanion(exePath, payload)
    End Function

    Private Async Function ActivateShareEntryPointAsync(managerOnly As Boolean) As Task
        Dim folder As String = If(managerOnly, "", ResolveCurrentFolder())
        Dim exePath As String = CompanionExePath()
        If String.IsNullOrEmpty(exePath) OrElse Not File.Exists(exePath) Then
            AppFileLogger.WriteLine("ShareLauncher: Companion exe not found at '" & If(exePath, "") & "'")
            OfferShareComponentDownload()
            Return
        End If

        Dim running As Boolean? = ProbeCompanionMutex()
        ' The version that matters is the one that will RECEIVE the folder: the running process
        ' if it can be read, otherwise (not running) the exe about to be started. A running
        ' process whose version cannot be read is "unknown", and unknown is not proof.
        Dim receiver As Version = If(running = True, RunningCompanionVersion(), FileVersionOfExe(exePath))
        Dim plan As ActivationPlan = ShareActivationPolicy.PlanFor(folder, receiver)
        Dim sentFolder As String = If(plan = ActivationPlan.SendFolder, folder, "")

        If running = True Then
            AppFileLogger.WriteLine("ShareLauncher: Companion running - " & plan.ToString())
            Dim payload As String = If(sentFolder.Length > 0, sentFolder, CompanionShowWindowCommand)
            Dim outcome As ForwardOutcome = Await Task.Run(Function() ForwardToCompanion(payload))
            If outcome = ForwardOutcome.Delivered Then
                If plan = ActivationPlan.WakeAndAdvise Then AdviseManualAdd(folder)
                Return
            End If
            ' Uncertain: the Share Manager exists but did not acknowledge in time. Starting it again
            ' would only repeat the request through another path (and a second manager must never
            ' start), so the user is told what happened.
            AppFileLogger.WriteLine("ShareLauncher: Companion running but not reachable (" & outcome.ToString() & ")")
            ' News, not a question: the status line (and, in full screen without panels, a toast) says it (124 UX-206).
            Notify(Localization.T("Share Manager уже запущен, но не ответил на запрос. Подождите немного и повторите; второй экземпляр не запускается."), NotifyLevel.Warning)
            Return
        End If

        ColdStartCompanion(exePath, sentFolder)
        If plan = ActivationPlan.WakeAndAdvise Then AdviseManualAdd(folder)
    End Function

    Private Sub AdviseManualAdd(folder As String)
        ViewerBox.Show(Me,
            Localization.TF("Share Manager старой версии не умеет безопасно принимать папку отсюда. Обновите его или добавьте папку в нём вручную:" & vbCrLf & "{0}", folder),
            Localization.T("Общий доступ"), MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    ''' <summary>Does a Share Manager already own its mutex? Nothing = could not tell. The
    ''' handle that was opened is always released; access denied counts as "yes" (S13-9).</summary>
    Private Shared Function ProbeCompanionMutex() As Boolean?
        Dim opened As Mutex = Nothing
        Dim failure As Exception = Nothing
        Dim found As Boolean = False
        Try
            found = Mutex.TryOpenExisting(CompanionMutexName, opened)
        Catch ex As Exception
            failure = ex
        Finally
            If opened IsNot Nothing Then
                Try : opened.Dispose() : Catch : End Try
            End If
        End Try
        If failure IsNot Nothing AndAlso Not TypeOf failure Is UnauthorizedAccessException Then
            AppFileLogger.LogException("ShareLauncher mutex probe", failure)
        End If
        Return ShareActivationPolicy.ExistingInstanceHint(found, failure)
    End Function

    Private Shared Function FileVersionOfExe(path As String) As Version
        Try
            Dim info As FileVersionInfo = FileVersionInfo.GetVersionInfo(path)
            Return ShareActivationPolicy.VersionOf(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart)
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>The oldest version among the running Share Manager processes, or Nothing when
    ''' any of them cannot be read (an elevated one refuses MainModule) - unknown, not "new".</summary>
    Private Shared Function RunningCompanionVersion() As Version
        Dim oldest As Version = Nothing
        Dim processes As Process() = Nothing
        Try
            processes = Process.GetProcessesByName(CompanionProcessName)
            If processes.Length = 0 Then Return Nothing
            For Each proc As Process In processes
                Dim v As Version = Nothing
                Try
                    v = FileVersionOfExe(proc.MainModule.FileName)
                Catch
                    v = Nothing
                End Try
                If v Is Nothing Then Return Nothing
                If oldest Is Nothing OrElse v < oldest Then oldest = v
            Next
        Catch ex As Exception
            AppFileLogger.LogException("ShareLauncher running version", ex)
            Return Nothing
        Finally
            If processes IsNot Nothing Then
                For Each proc As Process In processes
                    Try : proc.Dispose() : Catch : End Try
                Next
            End If
        End Try
        Return oldest
    End Function
    Private Sub ColdStartCompanion(exePath As String, folder As String)
        Try
            ' Let the freshly-launched Companion raise its window in front of us
            ' (we don't have its PID yet, so grant to any process).
            Try : AllowSetForegroundWindow(ASFW_ANY) : Catch : End Try
            Dim psi As New ProcessStartInfo(exePath) With {.UseShellExecute = True}
            ' With no folder we still have to SAY "show your window": a bare launch
            ' obeys Companion's own "open the manager window at startup" option (off
            ' by default), so without the marker this button would cold-start a
            ' tray-only process and look like it did nothing.
            ' QuoteArgument, not a bare pair of quotes: "E:\" reads back as E:" (053 F-13).
            psi.Arguments = If(folder.Length > 0, Utils.QuoteArgument(folder), CompanionShowWindowCommand)
            AppFileLogger.WriteLine("ShareLauncher: cold-starting Companion '" & exePath & "'")
            ' We never wait on it - release the handle instead of leaving it to the
            ' finalizer (this runs per user click).
            Dim started As Process = Process.Start(psi)
            If started IsNot Nothing Then started.Dispose()
        Catch ex As Exception
            AppFileLogger.LogException("ShareLauncher cold-start", ex)
            ViewerBox.Show(Me,
                Localization.TF("Не удалось запустить {1}." & vbCrLf & "{0}", FailureText.Describe(ex), ProductNames.ShareManager),
                Localization.T("Общий доступ"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    ''' <summary>
    ''' The Share Manager is not on this machine at all - offer to fetch it instead of
    ''' telling the user to "reinstall", which is both vague and, for the most common
    ''' cause, wrong advice: reinstalling the same package the same way lands the same
    ''' missing component. Share is an OPTIONAL installer component, so a legitimately
    ''' installed app can be without it - a compact install, a cleared checkbox, or (until
    ''' the installer's admin gate was dropped) any unattended winget install, which ran
    ''' Setup in non administrative install mode and skipped every optional payload.
    ''' Running the current setup.exe over the existing installation adds the component in
    ''' place - same AppId, same directory, settings and associations untouched.
    ''' </summary>
    Private Sub OfferShareComponentDownload()
        Dim answer As DialogResult = ViewerBox.Show(Me,
            Localization.TF("Компонент общего доступа не установлен ({0})." & vbCrLf & vbCrLf &
                            "Он входит в полный установщик: запустите его поверх текущей версии - настройки сохранятся." & vbCrLf & vbCrLf &
                            "Открыть страницу загрузки?", ProductNames.ShareManager),
            Localization.T("Общий доступ"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Information)
        If answer <> DialogResult.Yes Then Return

        Try
            AppFileLogger.WriteLine("ShareLauncher: opening the download page for the missing Share component")
            Dim psi As New ProcessStartInfo(ShareDownloadPageUrl) With {.UseShellExecute = True}
            Dim started As Process = Process.Start(psi)
            If started IsNot Nothing Then started.Dispose()
        Catch ex As Exception
            AppFileLogger.LogException("ShareLauncher download page", ex)
            ViewerBox.Show(Me,
                Localization.TF("Не удалось открыть страницу загрузки." & vbCrLf & "{0}", FailureText.Describe(ex)),
                Localization.T("Общий доступ"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    ''' <summary>
    ''' Where the Share Manager actually is. Next to this exe is the normal case, but
    ''' NOT the only one: the Server edition installs the Share Manager on its own, in
    ''' its own directory, and a machine that already has it is deliberately installed
    ''' WITHOUT a second copy beside the viewer. Looking only beside the exe made the
    ''' button report "reinstall the application" on a perfectly healthy machine whose
    ''' manager was one folder away. Mirrors TrayContext.ViewerExePath() on the
    ''' Companion side, which finds a normally installed viewer exactly this way.
    ''' </summary>
    Private Function CompanionExePath() As String
        Try
            Dim dir As String = Path.GetDirectoryName(Application.ExecutablePath)
            Dim beside As String = Path.Combine(dir, CompanionExeFileName)
            If File.Exists(beside) Then Return beside
        Catch
        End Try

        ' The Server edition's frozen Inno AppId - the same key its own installer
        ' writes. Per-user installs land in HKCU, machine-wide ones in HKLM (and under
        ' Wow6432Node on some upgrade paths).
        Const serverEditionKey As String =
            "Software\Microsoft\Windows\CurrentVersion\Uninstall\{A9F3C61B-4D2E-4F58-9C7A-1E6B0D3F82A4}_is1"
        For Each hive As Microsoft.Win32.RegistryKey In {Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine}
            For Each sub_key As String In {serverEditionKey, serverEditionKey.Replace("Software\", "Software\WOW6432Node\")}
                Try
                    Using k As Microsoft.Win32.RegistryKey = hive.OpenSubKey(sub_key, False)
                        If k Is Nothing Then Continue For
                        Dim loc As String = TryCast(k.GetValue("InstallLocation"), String)
                        If String.IsNullOrWhiteSpace(loc) Then Continue For
                        Dim candidate As String = Path.Combine(loc.Trim().TrimEnd(Path.DirectorySeparatorChar), CompanionExeFileName)
                        If File.Exists(candidate) Then Return candidate
                    End Using
                Catch
                End Try
            Next
        Next
        Return ""
    End Function

    ''' <summary>Companion is already running (possibly tray-only, hidden window) - hand it the
    ''' payload (a folder, or the bare show-window marker) via WM_COPYDATA, tagged for the Share
    ''' Manager. One monotonic 6 s deadline for the whole attempt, each send bounded by what
    ''' remains of it, the first acknowledgement ends it. Blocking: call it off the UI thread.
    ''' A payload that cannot be sent whole is refused, never shortened.</summary>
    Private Function ForwardToCompanion(payload As String) As ForwardOutcome
        Dim bytes As Byte() = Nothing
        Dim refusal As PayloadRefusal
        If Not CopyDataTransport.TryEncode(payload, bytes, refusal) Then
            AppFileLogger.WriteLine("ShareLauncher: payload refused (" & refusal.ToString() & "), not sent")
            Return ForwardOutcome.TimedOut
        End If
        Return CopyDataTransport.RunForward(
            AddressOf MonotonicClock.NowMs,
            Sub(pauseMs) Thread.Sleep(pauseMs),
            Function(sendTimeoutMs) SendToCompanionOnce(bytes, sendTimeoutMs))
    End Function

    Private Function SendToCompanionOnce(bytes As Byte(), sendTimeoutMs As Integer) As ForwardAttempt
        ' Every Process object is owned (and disposed) by the discovery; a Companion that exits
        ' between the listing and the send costs only that candidate.
        Dim targets As List(Of ViewerProcessTarget) = ViewerProcessDiscovery.Discover(
            {CompanionProcessName}, -1,
            Function(name As String) As IList(Of IViewerProcess)
                Dim wrapped As New List(Of IViewerProcess)()
                For Each proc As Process In Process.GetProcessesByName(name)
                    wrapped.Add(New RealViewerProcess(proc))
                Next
                Return wrapped
            End Function,
            AddressOf GetCompanionTopLevelWindows)
        If targets.Count = 0 Then Return ForwardAttempt.NoTarget

        For Each target As ViewerProcessTarget In targets
            ' Let the (background) Companion pull its window in front of us when
            ' it windowHandles the wake - otherwise the window opens silently behind.
            Try : AllowSetForegroundWindow(target.Pid) : Catch : End Try

            ' The wake (WM_COPYDATA) is handled ONLY by Companion's hidden MessageWindow, never by
            ' its main form - so send to ALL of the process's top-level windows.
            Dim windowHandles As New List(Of IntPtr)(target.TopLevelHandles)
            If target.MainWindowHandle <> IntPtr.Zero AndAlso Not windowHandles.Contains(target.MainWindowHandle) Then
                windowHandles.Add(target.MainWindowHandle)
            End If
            If windowHandles.Count = 0 Then Continue For

            Dim ptr As IntPtr = Marshal.AllocHGlobal(bytes.Length + 1)
            Try
                Marshal.Copy(bytes, 0, ptr, bytes.Length)
                Marshal.WriteByte(ptr, bytes.Length, 0)
                Dim cds As New COMPANION_COPYDATASTRUCT With {
                    .dwData = New IntPtr(CopyDataTransport.CompanionTag), .cbData = bytes.Length, .lpData = ptr}
                For Each h As IntPtr In windowHandles
                    Dim sendResult As IntPtr = IntPtr.Zero
                    Dim sent As Boolean = SendMessageTimeoutCompanion(h, WM_COPYDATA_SHARE, IntPtr.Zero, cds,
                                                                      SMTO_ABORTIFHUNG_COMPANION, sendTimeoutMs,
                                                                      sendResult) <> IntPtr.Zero
                    ' Delivered only on the receiver's own answer - a helper window of the process
                    ' answers 0, and a bare successful send proves nothing (S13-8).
                    If sent AndAlso sendResult = New IntPtr(1) Then
                        AppFileLogger.WriteLine("ShareLauncher: request acknowledged by the Share Manager")
                        Return ForwardAttempt.Acknowledged
                    End If
                Next
            Catch ex As Exception
                AppFileLogger.LogException("ShareLauncher forward", ex)
            Finally
                Marshal.FreeHGlobal(ptr)
            End Try
        Next
        Return ForwardAttempt.NotAcknowledged
    End Function
    ''' <summary>All top-level windows of a process, including hidden ones - a
    ''' tray-resident Companion has MainWindowHandle = Zero.</summary>
    Private Function GetCompanionTopLevelWindows(processId As Integer) As List(Of IntPtr)
        Dim result As New List(Of IntPtr)
        Try
            EnumWindowsCompanion(Function(hWnd As IntPtr, lParam As IntPtr) As Boolean
                                     Dim pid As Integer = 0
                                     GetWindowThreadProcessIdCompanion(hWnd, pid)
                                     If pid = processId Then result.Add(hWnd)
                                     Return True
                                 End Function, IntPtr.Zero)
        Catch ex As Exception
            AppFileLogger.LogException("ShareLauncher enumerate windows", ex)
        End Try
        Return result
    End Function

    Private Function ResolveCurrentFolder() As String
        ' Inside an archive the only folder that exists on disk is the temporary session
        ' directory - never something to hand to another program (010 §2.2, §7). The menu item
        ' is hidden there; this is the guard for any other way in.
        If IsArchiveMode() OrElse IsArchiveOpenPending() Then Return ""
        Try
            Dim f As String = If(Current_Folder_Path, "").Trim()
            If f.Length > 0 AndAlso Directory.Exists(f) Then Return f
            If Not String.IsNullOrEmpty(Current_Image_Path) Then
                Dim d As String = Path.GetDirectoryName(Current_Image_Path)
                If Not String.IsNullOrEmpty(d) AndAlso Directory.Exists(d) Then Return d
            End If
        Catch
        End Try
        Return ""
    End Function

End Class
#End If
