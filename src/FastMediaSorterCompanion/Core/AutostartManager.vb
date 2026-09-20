Option Strict On

Imports System.Runtime.InteropServices
Imports System.Windows.Forms
Imports Microsoft.Win32

''' <summary>
''' What will ACTUALLY happen at the next logon - not the same question as "did the user
''' ask for autostart". A registered Run value the shell has vetoed is indistinguishable
''' from no autostart at all through <see cref="AutostartManager.IsEnabled"/>, and that is
''' exactly how a Server-edition machine came back from a reboot with its service running,
''' its folders reachable, and no management console anywhere.
''' </summary>
Public Enum AutostartState
    ''' <summary>Store build: the manifest StartupTask owns this and an HKCU write is virtualized.</summary>
    Packaged
    ''' <summary>Registered and not vetoed - the tray icon appears at the next logon.</summary>
    Active
    ''' <summary>No Run value: autostart was never asked for, or was switched off in our own UI.</summary>
    NotRegistered
    ''' <summary>Registered, but the shell records a veto over it - Windows skips it silently.</summary>
    BlockedByShell
End Enum

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

    ''' <summary>Per-user "the Server-edition offer has already been made" flag - see
    ''' <see cref="OfferForSystemServiceHostOnce"/>. Lives in the same hive as every
    ''' other Share_* setting, and is written whether the offer was taken or refused,
    ''' so it is asked exactly once per account.</summary>
    Private Const OfferedRegValue As String = "Share_AutostartOffered"

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
    ''' Whether an HKCU Run value is registered at all - "did anyone ask for autostart",
    ''' which is NOT the same as "will it start" (<see cref="IsBlockedByShell"/>).
    ''' </summary>
    Private Function IsRegistered() As Boolean
        Try
            Using k As RegistryKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable:=False)
                If k Is Nothing Then Return False
                Return k.GetValue(RunValueName) IsNot Nothing
            End Using
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' The full three-way answer, so a caller can tell "never asked for" from "asked for
    ''' and Windows refuses". <see cref="IsEnabled"/> collapses the last two into False,
    ''' which is right for a checkbox and wrong for anything that has to explain itself.
    ''' </summary>
    Public Function State() As AutostartState
        If IsPackaged() Then Return AutostartState.Packaged
        If Not IsRegistered() Then Return AutostartState.NotRegistered
        Return If(IsBlockedByShell(), AutostartState.BlockedByShell, AutostartState.Active)
    End Function

    ''' <summary>
    ''' Current autostart state, as in "will it actually start at the next logon" - the
    ''' registered Run value AND no shell veto over it (<see cref="IsBlockedByShell"/>).
    ''' For packaged builds the manifest StartupTask owns this, so we report True (the
    ''' checkbox is read-only there anyway).
    ''' </summary>
    Public Function IsEnabled() As Boolean
        Dim s As AutostartState = State()
        Return s = AutostartState.Packaged OrElse s = AutostartState.Active
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

    ''' <summary>
    ''' The decision behind <see cref="OfferForSystemServiceHostOnce"/>, kept pure so the
    ''' one thing that matters here - that it stays quiet in every case but the first -
    ''' is unit-tested rather than reasoned about.
    '''
    ''' It fires only when the machine hosts the SFTP service (Server edition, where the
    ''' tray icon is the ONLY management surface a reboot leaves behind), this account has
    ''' not been offered before, and nothing about autostart has been decided yet: no Run
    ''' value, and no shell record either way. That last clause is the important one - a
    ''' veto is the user's own Task Manager decision, and silently undoing it would make
    ''' this a program that switches itself back on. Those machines get the note in
    ''' Settings instead, which says what happened and how to restore it.
    ''' </summary>
    Public Function ShouldOfferAutostart(isServiceHost As Boolean, alreadyOffered As Boolean,
                                         runValuePresent As Boolean, shellRecordPresent As Boolean) As Boolean
        If Not isServiceHost Then Return False
        If alreadyOffered Then Return False
        Return Not runValuePresent AndAlso Not shellRecordPresent
    End Function

    ''' <summary>
    ''' On a Server-edition machine, register the console's own logon autostart the first
    ''' time we run in this account - see <see cref="ShouldOfferAutostart"/> for when that
    ''' is allowed. Without it the service came up at boot, the folders were reachable and
    ''' there was no tray icon and no window: the only way back in was a Start-menu entry
    ''' the user had no reason to look for.
    '''
    ''' Called once from startup, after <see cref="MigrateRunTargetIfNeeded"/> so it sees
    ''' the migrated value rather than racing it. Never throws; the flag is written even
    ''' when the write fails, because a failing offer repeated at every logon is worse
    ''' than one that has to be ticked by hand.
    ''' </summary>
    Public Sub OfferForSystemServiceHostOnce()
        If IsPackaged() Then Return
        Try
            Dim offered As Boolean = GetSetting(App_name, Second_App_Name, OfferedRegValue, "0") = "1"
            If Not ShouldOfferAutostart(ServerFeatures.IsSystemServiceHost(), offered,
                                        IsRegistered(), ShellRecordExists()) Then Return
            SaveSetting(App_name, Second_App_Name, OfferedRegValue, "1")
            If SetEnabled(True) Then SaveSetting(App_name, Second_App_Name, "Share_AutostartEnabled", "1")
        Catch
        End Try
    End Sub

    ''' <summary>Whether the shell holds ANY record for our entry - allowed or vetoed.
    ''' Either way somebody has already been through Task Manager's Startup page for it,
    ''' and <see cref="ShouldOfferAutostart"/> then leaves the decision alone.</summary>
    Private Function ShellRecordExists() As Boolean
        Try
            Using k As RegistryKey = Registry.CurrentUser.OpenSubKey(StartupApprovedPath, writable:=False)
                If k Is Nothing Then Return False
                Return k.GetValue(RunValueName) IsNot Nothing
            End Using
        Catch
            ' Unreadable - assume a record exists, so an unreadable hive can never be the
            ' reason we write an autostart entry the user did not ask for.
            Return True
        End Try
    End Function

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
