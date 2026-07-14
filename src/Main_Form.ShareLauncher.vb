Option Strict On

Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms

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

    Private shareFolderMenu As ContextMenuStrip
    Private shareMenuItem As ToolStripMenuItem

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

    ''' <summary>Wires the folder-box right-click "Share this folder.." item. Called
    ''' once from Form1_Load. Right-click on the media surfaces is already file
    ''' navigation, so this attaches to the folder box + its label instead.</summary>
    Friend Sub InitializeShareEntryPoint()
        If shareFolderMenu Is Nothing Then
            shareFolderMenu = New ContextMenuStrip()
            shareMenuItem = New ToolStripMenuItem()
            AddHandler shareMenuItem.Click, Sub() ActivateShareEntryPoint()
            shareFolderMenu.Items.Add(shareMenuItem)
            If cmbox_Media_Folder IsNot Nothing Then cmbox_Media_Folder.ContextMenuStrip = shareFolderMenu
            If lbl_Folder IsNot Nothing Then lbl_Folder.ContextMenuStrip = shareFolderMenu
        End If
        LocalizeShareEntryPoint()
    End Sub

    ''' <summary>Re-localizes the folder context-menu item. Called from LngCh.</summary>
    Friend Sub LocalizeShareEntryPoint()
        If shareMenuItem IsNot Nothing Then
            shareMenuItem.Text = If(Is_Russian_Language, "Поделиться этой папкой с телефоном..", "Share this folder with phone..")
        End If
    End Sub

    ''' <summary>The one action LITE knows about sharing: find/wake Fast Media
    ''' Sorter: Share Manager with the folder currently being viewed. Not found
    ''' next to this exe -&gt; a clear message, never a silent no-op (graceful
    ''' degradation - the app never fails silently when an optional companion
    ''' component is missing).</summary>
    Friend Sub ActivateShareEntryPoint()
        Dim folder As String = ResolveCurrentFolder()
        Dim exePath As String = CompanionExePath()
        If String.IsNullOrEmpty(exePath) OrElse Not File.Exists(exePath) Then
            MessageBox.Show(Me,
                If(Is_Russian_Language,
                    "Fast Media Sorter: Share Manager не найден рядом. Переустановите приложение.",
                    "Fast Media Sorter: Share Manager was not found alongside this app. Please reinstall."),
                If(Is_Russian_Language, "Общий доступ", "Folder sharing"),
                MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim existingMutex As Mutex = Nothing
        If Mutex.TryOpenExisting(CompanionMutexName, existingMutex) Then
            existingMutex.Close()
            ForwardFolderToCompanion(folder)
        Else
            Try
                ' Let the freshly-launched Companion raise its window in front of us
                ' (we don't have its PID yet, so grant to any process).
                Try : AllowSetForegroundWindow(ASFW_ANY) : Catch : End Try
                Dim psi As New ProcessStartInfo(exePath) With {.UseShellExecute = True}
                If folder.Length > 0 Then psi.Arguments = """" & folder & """"
                Process.Start(psi)
            Catch
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
        Try
            For Each proc As Process In Process.GetProcessesByName(CompanionProcessName)
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
