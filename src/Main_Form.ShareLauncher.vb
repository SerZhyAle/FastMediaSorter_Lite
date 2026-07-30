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
''' LITE's entire remaining surface for the Android Folder Share feature after the
''' Stage 3 migration to the standalone Companion app ("Fast Media Sorter: Share
''' Manager", src/FastMediaSorterCompanion/) - SPECIFICATION_SHARE_COMPANION_APP.md
''' §4.2/§5. LITE knows NOTHING about the worker, the pipe, ServerFeatures or any
''' opt-in gate - all of that is Companion's own concern now. This file only finds
''' and wakes Companion, forwarding the folder currently being viewed. Two entry
''' points remain: the folder-box right-click item (this file) and a button on the
''' Settings window's "Files and system" tab (Table_Form.ShareLauncher.vb) - both
''' call <see cref="ActivateShareEntryPoint"/>. The toolbar button and Shift+S
''' hotkey are gone (owner decision, 2026-07).
'''
''' The wake protocol reuses, in the opposite direction, the exact mutex +
''' WM_COPYDATA pattern Application_Events.vb already uses for LITE's own
''' single-instance argument forwarding.
''' </summary>
Partial Public Class Main_Form

    Private Const CompanionMutexName As String = "FastMediaSorterCompanionSingleInstanceMutex"
    Private Const CompanionProcessName As String = "FastMediaSorterCompanion"
    Private Const CompanionExeFileName As String = "FastMediaSorterCompanion.exe"
    ''' <summary>Empty-call marker (no folder) - Companion just shows its window.</summary>
    Private Const CompanionShowWindowCommand As String = "::fms-show-window::"
    Private Const WM_COPYDATA_SHARE As Integer = &H4A

    <StructLayout(LayoutKind.Sequential)>
    Private Structure COMPANION_COPYDATASTRUCT
        Public dwData As IntPtr
        Public cbData As Integer
        Public lpData As IntPtr
    End Structure

    Private Delegate Function EnumWindowsCompanionProc(hWnd As IntPtr, lParam As IntPtr) As Boolean

    <DllImport("user32.dll", CharSet:=CharSet.Auto)>
    Private Shared Function SendMessageCompanionCopyData(hWnd As IntPtr, msg As Integer, wParam As IntPtr, ByRef lParam As COMPANION_COPYDATASTRUCT) As Integer
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function EnumWindowsCompanion(callback As EnumWindowsCompanionProc, lParam As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
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

    ''' <summary>The one action LITE knows about sharing: find/wake Fast Media
    ''' Sorter: Share Manager. When <paramref name="managerOnly"/> is False (the
    ''' folder-box right-click) it forwards the folder currently being viewed so
    ''' Companion jumps to "share this folder"; when True (the Settings button) it
    ''' forwards nothing and just raises the manager window. Not found next to this
    ''' exe -&gt; a clear message, never a silent no-op (graceful degradation - the
    ''' app never fails silently when an optional companion component is missing).</summary>
    Friend Sub ActivateShareEntryPoint(Optional managerOnly As Boolean = False)
        Dim folder As String = If(managerOnly, "", ResolveCurrentFolder())
        Dim exePath As String = CompanionExePath()
        If String.IsNullOrEmpty(exePath) OrElse Not File.Exists(exePath) Then
            AppFileLogger.WriteLine("ShareLauncher: Companion exe not found at '" & If(exePath, "") & "'")
            MessageBox.Show(Me,
                Localization.T("Fast Media Sorter: Share Manager не найден рядом. Переустановите приложение."),
                Localization.T("Общий доступ"),
                MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim existingMutex As Mutex = Nothing
        If Mutex.TryOpenExisting(CompanionMutexName, existingMutex) Then
            existingMutex.Close()
            AppFileLogger.WriteLine("ShareLauncher: Companion running - forwarding " &
                                    If(folder.Length > 0, "folder", "show-window"))
            ForwardFolderToCompanion(folder)
        Else
            Try
                ' Let the freshly-launched Companion raise its window in front of us
                ' (we don't have its PID yet, so grant to any process).
                Try : AllowSetForegroundWindow(ASFW_ANY) : Catch : End Try
                Dim psi As New ProcessStartInfo(exePath) With {.UseShellExecute = True}
                ' With no folder we still have to SAY "show your window": a bare launch
                ' obeys Companion's own "open the manager window at startup" option (off
                ' by default), so without the marker this button would cold-start a
                ' tray-only process and look like it did nothing.
                psi.Arguments = If(folder.Length > 0, """" & folder & """", CompanionShowWindowCommand)
                AppFileLogger.WriteLine("ShareLauncher: cold-starting Companion '" & exePath & "'")
                ' We never wait on it - release the handle instead of leaving it to the
                ' finalizer (this runs per user click).
                Dim started As Process = Process.Start(psi)
                If started IsNot Nothing Then started.Dispose()
            Catch ex As Exception
                AppFileLogger.LogException("ShareLauncher cold-start", ex)
                MessageBox.Show(Me,
                    Localization.TF("Не удалось запустить Fast Media Sorter: Share Manager." & vbCrLf & "{0}", ex.Message),
                    Localization.T("Общий доступ"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End If
    End Sub

    Private Function CompanionExePath() As String
        Try
            Dim dir As String = Path.GetDirectoryName(Application.ExecutablePath)
            Return Path.Combine(dir, CompanionExeFileName)
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>Companion is already running (possibly tray-only, hidden window) -
    ''' find its top-level window(s) and forward the folder (or the bare show-window
    ''' marker) via WM_COPYDATA, mirroring Application_Events.vb's own forwarding.</summary>
    Private Sub ForwardFolderToCompanion(folder As String)
        Dim payload As String = If(folder.Length > 0, folder, CompanionShowWindowCommand)
        Dim processes As Process() = Nothing
        Try
            ' Every element of GetProcessesByName holds a process handle. This runs per user
            ' click, so they are released in the Finally below rather than left to the
            ' finalizer - including the ones the loop never reached.
            processes = Process.GetProcessesByName(CompanionProcessName)
            For Each proc As Process In processes
                ' Let the (background) Companion pull its window in front of us when
                ' it handles the wake - otherwise the window opens silently behind.
                Try : AllowSetForegroundWindow(proc.Id) : Catch : End Try

                ' The wake (WM_COPYDATA) is handled ONLY by Companion's hidden
                ' MessageWindow, never by its main form - so send to ALL of the
                ' process's top-level windows. Targeting MainWindowHandle alone (as
                ' before) silently dropped the payload whenever the main window was
                ' up, which read to the user as "nothing happens".
                Dim targets As List(Of IntPtr) = GetCompanionTopLevelWindows(proc.Id)
                If proc.MainWindowHandle <> IntPtr.Zero AndAlso Not targets.Contains(proc.MainWindowHandle) Then
                    targets.Add(proc.MainWindowHandle)
                End If
                If targets.Count = 0 Then Continue For

                Dim bytes() As Byte = Encoding.UTF8.GetBytes(payload)
                Dim ptr As IntPtr = Marshal.AllocHGlobal(bytes.Length + 1)
                Try
                    Marshal.Copy(bytes, 0, ptr, bytes.Length)
                    Marshal.WriteByte(ptr, bytes.Length, 0)
                    Dim cds As New COMPANION_COPYDATASTRUCT With {.dwData = IntPtr.Zero, .cbData = bytes.Length, .lpData = ptr}
                    For Each h As IntPtr In targets
                        SendMessageCompanionCopyData(h, WM_COPYDATA_SHARE, IntPtr.Zero, cds)
                    Next
                Finally
                    Marshal.FreeHGlobal(ptr)
                End Try
                Exit For
            Next
        Catch
        Finally
            If processes IsNot Nothing Then
                For Each proc As Process In processes
                    Try : proc.Dispose() : Catch : End Try
                Next
            End If
        End Try
    End Sub

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
        Catch
        End Try
        Return result
    End Function

    Private Function ResolveCurrentFolder() As String
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
