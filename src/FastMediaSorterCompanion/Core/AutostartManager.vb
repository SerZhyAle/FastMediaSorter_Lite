Option Strict On

Imports System.Runtime.InteropServices
Imports System.Windows.Forms
Imports Microsoft.Win32

''' <summary>
''' Channel-aware logon autostart for Fast Media Sorter: Share Manager.
'''
''' Unpackaged (portable ZIP / Inno / winget): opt-in HKCU Run value pointing at
''' <b>Companion.exe</b> (NOT the bare worker) - it starts silently into the tray
''' and brings the worker up itself, so the user gets the icon/controls at logon.
''' Written/removed at RUNTIME only (an install-time Run write can land in the wrong
''' hive when the install is elevated/machine-scoped). The value NAME
''' (FastMediaSorterShare) is frozen so an existing autostart correlates across the
''' upgrade; <see cref="MigrateRunTargetIfNeeded"/> rewrites a stale value that
''' still points at the worker (spec §9.3).
'''
''' Packaged (Store MSIX): the manifest uap5:StartupTask is authoritative and an
''' HKCU Run write is virtualized and silently ignored - so the checkbox is
''' read-only/explanatory there and this module reports "packaged".
''' </summary>
Public Module AutostartManager

    Private Const RunKeyPath As String = "Software\Microsoft\Windows\CurrentVersion\Run"
    Private Const RunValueName As String = "FastMediaSorterShare"

    ''' <summary>Where the shell records its own veto over a Run entry - see
    ''' <see cref="IsBlockedByShell"/>. Same value name as under Run.</summary>
    Private Const StartupApprovedPath As String = "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"

    ' GetCurrentPackageFullName returns this when the process has no package identity.
    Private Const APPMODEL_ERROR_NO_PACKAGE As Integer = 15700

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=False)>
    Private Function GetCurrentPackageFullName(ByRef packageFullNameLength As Integer, packageFullName As Char()) As Integer
    End Function

    ''' <summary>
    ''' True when running inside an MSIX/AppX package (the Store build). Uses the
    ''' documented GetCurrentPackageFullName probe; on pre-Windows-8 the export is
    ''' absent (throws) and we treat the process as unpackaged.
    ''' </summary>
    Public Function IsPackaged() As Boolean
        Try
            Dim length As Integer = 0
            Dim rc As Integer = GetCurrentPackageFullName(length, Nothing)
            Return rc <> APPMODEL_ERROR_NO_PACKAGE
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' True when Windows itself has vetoed this startup entry. Task Manager's "Startup
    ''' apps" page and Settings -> Apps -> Startup do NOT delete the Run value when you
    ''' switch an item off: they leave it in place and record the veto here, and the shell
    ''' then skips the entry at logon. So the Run value alone answers "is it registered",
    ''' never "will it actually start" - which is how a ticked checkbox, a correct Run
    ''' value and no tray icon at logon coexisted for days before this was found.
    ''' Byte 0 carries the state and its low bit IS the veto (02/06 = allowed,
    ''' 01/03/07 = blocked); the following bytes are the FILETIME of the change.
    ''' </summary>
    Private Function IsBlockedByShell() As Boolean
        Try
            Using k As RegistryKey = Registry.CurrentUser.OpenSubKey(StartupApprovedPath, writable:=False)
                If k Is Nothing Then Return False
                Return IsShellVetoRecord(TryCast(k.GetValue(RunValueName), Byte()))
            End Using
        Catch
            ' Unreadable - report "not blocked" rather than unticking a checkbox the
            ' user set, since the common case by far is that no veto exists.
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Reads one StartupApproved record: True when it says "blocked". Kept pure and
    ''' separate from the registry so the byte layout - the one part of this that is
    ''' guesswork about someone else's format - can be unit-tested. An absent or empty
    ''' record is NOT a veto: the shell only writes here once an item has been switched
    ''' off or back on, so "no record" is the untouched, allowed state.
    ''' </summary>
    Public Function IsShellVetoRecord(raw As Byte()) As Boolean
        If raw Is Nothing OrElse raw.Length = 0 Then Return False
        Return (raw(0) And 1) <> 0
    End Function

    ''' <summary>The "allowed" record, byte-identical to what Task Manager writes when an
    ''' item is switched back on: state 2 and a zeroed change FILETIME.</summary>
    Public Function ShellAllowRecord() As Byte()
        Return New Byte() {2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}
    End Function

    ''' <summary>
    ''' Lifts the shell's veto by writing the same "allowed" record Task Manager writes.
    ''' Called ONLY from <see cref="SetEnabled"/> when the user has just asked for
    ''' autostart in our own UI - that request is the consent that makes overwriting an
    ''' earlier Task Manager "off" the right move, because otherwise ticking the box
    ''' would visibly change nothing. Never creates the key: no key means nothing was
    ''' ever vetoed, and there is nothing to lift.
    ''' </summary>
    Private Sub ClearShellBlock()
        Try
            Using k As RegistryKey = Registry.CurrentUser.OpenSubKey(StartupApprovedPath, writable:=True)
                If k Is Nothing Then Return
                If Not IsShellVetoRecord(TryCast(k.GetValue(RunValueName), Byte())) Then Return
                k.SetValue(RunValueName, ShellAllowRecord(), RegistryValueKind.Binary)
            End Using
        Catch
            ' Best-effort: the Run value is still written, so the state is no worse than
            ' before - the user can also flip the item back on in Task Manager.
        End Try
    End Sub

    ''' <summary>
    ''' Current autostart state, as in "will it actually start at the next logon" - the
    ''' registered Run value AND no shell veto over it (<see cref="IsBlockedByShell"/>).
    ''' For packaged builds the manifest StartupTask owns this, so we report True (the
    ''' checkbox is read-only there anyway).
    ''' </summary>
    Public Function IsEnabled() As Boolean
        If IsPackaged() Then Return True
        Try
            Using k As RegistryKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable:=False)
                If k Is Nothing Then Return False
                If k.GetValue(RunValueName) Is Nothing Then Return False
            End Using
        Catch
            Return False
        End Try
        Return Not IsBlockedByShell()
    End Function

    ''' <summary>
    ''' Enables/disables logon autostart of the worker (unpackaged channels only).
    ''' Writes the quoted worker path to HKCU Run, or removes the value. Returns
    ''' False when not applicable (packaged) or on failure - the caller keeps the
    ''' checkbox in sync with the real state.
    ''' </summary>
    Public Function SetEnabled(enabled As Boolean) As Boolean
        If IsPackaged() Then Return False

        Dim exe As String = CompanionExePath()
        If enabled AndAlso (exe.Length = 0 OrElse Not IO.File.Exists(exe)) Then Return False

        Try
            Using k As RegistryKey = Registry.CurrentUser.CreateSubKey(RunKeyPath)
                If k Is Nothing Then Return False
                If enabled Then
                    k.SetValue(RunValueName, RunCommand(exe), RegistryValueKind.String)
                    ' Writing the value is not enough on a machine where the entry was
                    ' switched off in Task Manager - see ClearShellBlock.
                    ClearShellBlock()
                ElseIf k.GetValue(RunValueName) IsNot Nothing Then
                    k.DeleteValue(RunValueName, throwOnMissingValue:=False)
                End If
            End Using
            Return True
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " companion autostart: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' One-shot upgrade migration (spec §9.3): if logon autostart is enabled but its
    ''' HKCU Run value still points at the old target (the bare worker exe, as written
    ''' by the pre-Companion LITE), rewrite it to Companion.exe so the tray icon +
    ''' controls appear at logon instead of a headless worker. No-op when packaged,
    ''' when autostart is off (value absent), or when the value already matches.
    ''' Called once from startup; never throws.
    ''' </summary>
    Public Sub MigrateRunTargetIfNeeded()
        If IsPackaged() Then Return
        Try
            Using k As RegistryKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable:=True)
                If k Is Nothing Then Return
                Dim current As String = TryCast(k.GetValue(RunValueName), String)
                If String.IsNullOrEmpty(current) Then Return   ' autostart not enabled - nothing to migrate
                Dim companion As String = CompanionExePath()
                If companion.Length = 0 Then Return
                ' Rewrite when the exe target OR the silent-tray flag is missing (an
                ' old value points at the worker, or a value without --tray would pop
                ' the window at every logon).
                If Not String.Equals(current, RunCommand(companion), StringComparison.OrdinalIgnoreCase) Then
                    k.SetValue(RunValueName, RunCommand(companion), RegistryValueKind.String)
                End If
            End Using
        Catch
        End Try
    End Sub

    ''' <summary>The HKCU Run command: quoted Companion path + the silent-tray flag,
    ''' so a logon launch goes straight to the tray (spec §4.5.1).</summary>
    Private Function RunCommand(exe As String) As String
        Return """" & exe & """ " & Program.TrayFlag
    End Function

    ''' <summary>Companion's own exe path - the autostart target.</summary>
    Private Function CompanionExePath() As String
        Try
            Return Application.ExecutablePath
        Catch
            Return ""
        End Try
    End Function

End Module
