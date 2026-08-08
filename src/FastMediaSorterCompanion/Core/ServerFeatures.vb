Option Strict On

Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' The opt-in gate for the Android Folder Share "server features" - the bundled
''' SFTP worker plus the one Windows Firewall exception that actually makes a
''' shared folder reachable. See SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md.
'''
''' Exposing folders over SFTP and punching a firewall hole is a deliberate,
''' admin-level action, so the WHOLE Share surface (toolbar-button action, the
''' Settings "Поделиться" tab body, the tray indicator, Shift+S, and - crucially -
''' spawning the worker) is gated behind <see cref="IsEnabled"/>. Until the user
''' consents, Fast Media Sorter is a pure image/video viewer and sorter and the
''' worker process is never started.
'''
''' <see cref="IsEnabled"/> is true when ANY of:
'''   * a machine marker file exists (written by the elevated installer when the
'''     new "Install server features" checkbox is ticked). Hive-independent, so it
'''     dodges the "elevated install runs under the admin's profile, HKCU write
'''     lands in the wrong hive" hazard that the same worker's autostart already
'''     documents; it also enables the feature for every user of a (usually
'''     single-user) dedicated server, matching the machine-global firewall rule;
'''   * the per-user flag HKCU ...\SZA\FastMediaSorter\Share_ServerFeaturesEnabled=1
'''     is set (written by the deferred runtime opt-in, which already runs as the
'''     real logged-in user, so HKCU is the correct hive there);
'''   * the process is packaged (Store MSIX) - the manifest already grants the
'''     firewall rule + StartupTask, so a packaged build is always "available";
'''   * a validated Server edition service is registered (the installer did the
'''     privileged work already, so consent is a matter of machine state - see
'''     <see cref="HostMode"/> and SPECIFICATION_SHARE_SYSTEM_SERVICE.md §5).
''' </summary>
Public Module ServerFeatures

    ''' <summary>
    ''' Who hosts the SFTP worker. Not a preference - an observation, and the one
    ''' the whole Share Manager branches on:
    '''   * <c>None</c> - the feature is not enabled at all;
    '''   * <c>UserSession</c> - the Share Manager owns the worker process and
    '''     starts/stops it in the interactive session (the default edition);
    '''   * <c>SystemService</c> - the Windows SCM owns it. The Share Manager is
    '''     then a management console: it connects, it never spawns or kills, and
    '''     closing it has no effect on availability.
    ''' </summary>
    Public Enum ServerHostMode
        None
        UserSession
        SystemService
    End Enum

    Private Const MarkerFileName As String = "server-features.enabled"

    ''' <summary>HKCU value name of the per-user consent flag. Owned/written here;
    ''' ShareSettings only mirrors it read-only (see ShareSettings.Save).</summary>
    Friend Const EnabledRegValue As String = "Share_ServerFeaturesEnabled"

    ''' <summary>The single firewall rule name, shared by the installer, the runtime
    ''' helper and the uninstaller so every path adds/removes the same rule.</summary>
    Public Const FirewallRuleName As String = "FastMediaSorter Companion SFTP"

    ''' <summary>Bundled elevated helper (installed next to the exe by the Inno
    ''' installer). Runtime opt-in prefers it; falls back to direct netsh if absent.</summary>
    Private Const HelperScriptName As String = "enable-share-server.ps1"

    ' Cheap to compute (a File.Exists + one registry read), but read during layout,
    ' so cache it for the process. Refresh() invalidates it after a runtime enable.
    Private _cached As Boolean?

    ''' <summary>The one gate the whole Share UI reads. Cached per process.</summary>
    Public Function IsEnabled() As Boolean
        If _cached.HasValue Then Return _cached.Value
        Dim v As Boolean = ComputeEnabled()
        _cached = v
        Return v
    End Function

    Private Function ComputeEnabled() As Boolean
        Try
            If AutostartManager.IsPackaged() Then Return True
        Catch
        End Try
        ' A registered Server edition service IS the consent: an administrator ran an
        ' elevated installer that created the service, the machine data directory and
        ' the firewall rule. Checked before the per-user flag because a Server machine
        ' commonly has several accounts and none of them needs to opt in again.
        Try
            If HostMode() = ServerHostMode.SystemService Then Return True
        Catch
        End Try
        Try
            Dim marker As String = MarkerPath()
            If marker.Length > 0 AndAlso File.Exists(marker) Then Return True
        Catch
        End Try
        Try
            If GetSetting(App_name, Second_App_Name, EnabledRegValue, "0") = "1" Then Return True
        Catch
        End Try
        Return False
    End Function

    ''' <summary>Clears the cached gate so the next <see cref="IsEnabled"/> re-reads
    ''' the marker + flag. Call after enabling at runtime.</summary>
    Public Sub Refresh()
        _cached = Nothing
        _cachedHostMode = Nothing
    End Sub

    ' --- hosting mode (SPECIFICATION_SHARE_SYSTEM_SERVICE.md §5) ------------------

    ' Cached like IsEnabled: HostMode is read during layout and on every status
    ' refresh, and each miss costs an SCM round trip plus a registry read.
    Private _cachedHostMode As ServerHostMode?

    ''' <summary>
    ''' Which host owns the worker. Authoritative for <c>SystemService</c>: it takes
    ''' the LIVE SCM answer and additionally validates that the registration is our
    ''' Server edition worker, so neither a stale HKLM key nor a per-user flag can
    ''' claim a service that is not there. An unelevated call is enough for both.
    '''
    ''' Deliberately NOT symmetric with <see cref="IsEnabled"/>: a machine can have
    ''' the service while this user has no HKCU flag, and that still means Server.
    ''' </summary>
    Public Function HostMode() As ServerHostMode
        If _cachedHostMode.HasValue Then Return _cachedHostMode.Value
        Dim v As ServerHostMode = ComputeHostMode()
        _cachedHostMode = v
        Return v
    End Function

    Private Function ComputeHostMode() As ServerHostMode
        Try
            If ServiceControl.QueryState() <> ServiceControl.ServiceState.NotInstalled AndAlso
               ServiceControl.IsOurRegistration() Then
                Return ServerHostMode.SystemService
            End If
        Catch
        End Try
        ' No service: User mode when the feature was consented to (marker file, HKCU
        ' flag or a packaged build), otherwise nothing is hosting anything.
        Try
            If AutostartManager.IsPackaged() Then Return ServerHostMode.UserSession
        Catch
        End Try
        Try
            Dim marker As String = MarkerPath()
            If marker.Length > 0 AndAlso File.Exists(marker) Then Return ServerHostMode.UserSession
        Catch
        End Try
        Try
            If GetSetting(App_name, Second_App_Name, EnabledRegValue, "0") = "1" Then Return ServerHostMode.UserSession
        Catch
        End Try
        Return ServerHostMode.None
    End Function

    ''' <summary>Convenience for the many call sites that only ask "am I a console?"
    ''' - the answer that decides whether the worker may be spawned or killed.</summary>
    Public Function IsSystemServiceHost() As Boolean
        Return HostMode() = ServerHostMode.SystemService
    End Function

    ''' <summary>Re-reads the SCM state only (after an elevated install/remove), so a
    ''' management action shows in the UI without restarting the console.</summary>
    Public Sub RefreshHostMode()
        _cachedHostMode = Nothing
        _cached = Nothing
    End Sub

    ''' <summary>Worker payload present -> there is actually something to enable. A
    ''' viewer-only distribution with no bundled worker cannot offer the feature.</summary>
    Public Function CanEnable() As Boolean
        Return WorkerProcess.IsAvailable()
    End Function

    ''' <summary>The machine-side consent marker, next to the worker exe:
    ''' &lt;app dir&gt;\companion\server-features.enabled. "" if the path is unknown.</summary>
    Public Function MarkerPath() As String
        Try
            Dim baseDir As String = Path.GetDirectoryName(Application.ExecutablePath)
            If String.IsNullOrEmpty(baseDir) Then Return ""
            Return Path.Combine(baseDir, "companion", MarkerFileName)
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>Records this user's consent (HKCU flag) and refreshes the gate. The
    ''' runtime opt-in path runs as the real user, so HKCU is the correct hive here;
    ''' the installer uses the marker file instead to dodge the elevated-hive hazard.</summary>
    Public Sub RecordEnabledForThisUser()
        Try
            SaveSetting(App_name, Second_App_Name, EnabledRegValue, "1")
        Catch
        End Try
        Refresh()
    End Sub

    ''' <summary>Outcome of the elevated enable attempt.</summary>
    Public Enum EnableResult
        Enabled       ' firewall rule added (or already present), consent recorded
        Declined      ' the user dismissed the UAC prompt
        Failed        ' the helper ran but returned a non-zero exit code / errored
        Unavailable   ' worker payload missing - nothing to enable
    End Enum

    ''' <summary>
    ''' Performs the single privileged step that makes a shared folder reachable:
    ''' adds the program-scoped inbound firewall allow for the worker exe, via one
    ''' elevated (UAC) helper. On success records this user's consent and refreshes
    ''' the gate. The app itself stays non-elevated - only the short-lived helper
    ''' elevates. Blocks on the UAC prompt; call it from a modal so the wait reads.
    ''' </summary>
    Public Function EnableViaElevation() As EnableResult
        If Not CanEnable() Then Return EnableResult.Unavailable
        Dim worker As String = WorkerProcess.WorkerExePath()
        Dim rc As Integer
        Try
            rc = RunElevatedFirewall(worker, disable:=False)
        Catch ex As System.ComponentModel.Win32Exception
            ' 1223 = ERROR_CANCELLED: the user said No to the UAC prompt.
            If ex.NativeErrorCode = 1223 Then Return EnableResult.Declined
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " server-features enable: " & ex.Message)
            Return EnableResult.Failed
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " server-features enable: " & ex.Message)
            Return EnableResult.Failed
        End Try
        If rc <> 0 Then Return EnableResult.Failed
        RecordEnabledForThisUser()
        Return EnableResult.Enabled
    End Function

    ''' <summary>Runs the elevated firewall helper and returns its exit code. Prefers
    ''' the bundled enable-share-server.ps1 (auditable, carries the -Disable path);
    ''' falls back to a direct elevated netsh when the script is not present (a bare
    ''' portable copy). Throws Win32Exception(1223) when the UAC prompt is declined -
    ''' the caller maps that to <see cref="EnableResult.Declined"/>.</summary>
    Private Function RunElevatedFirewall(workerExe As String, disable As Boolean) As Integer
        Dim script As String = HelperScriptPath()
        Dim psi As ProcessStartInfo
        If script.Length > 0 AndAlso File.Exists(script) Then
            Dim args As String = "-NoProfile -ExecutionPolicy Bypass -File """ & script & """ -ExePath """ & workerExe & """"
            If disable Then args &= " -Disable"
            psi = New ProcessStartInfo("powershell.exe", args)
        Else
            ' No bundled script (dev/portable): drive netsh directly. Program-scoped
            ' inbound allow so the rule survives the worker's dynamic listen port.
            Dim verb As String = If(disable, "delete", "add")
            Dim rule As String = "advfirewall firewall " & verb & " rule name=""" & FirewallRuleName & """"
            If Not disable Then
                rule &= " dir=in action=allow program=""" & workerExe & """ protocol=TCP profile=domain,private,public enable=yes"
            End If
            psi = New ProcessStartInfo("netsh", rule)
        End If
        psi.UseShellExecute = True         ' required for Verb = runas
        psi.Verb = "runas"                 ' the single UAC prompt
        psi.WindowStyle = ProcessWindowStyle.Hidden
        Using p As Process = Process.Start(psi)
            p.WaitForExit()
            Return p.ExitCode
        End Using
    End Function

    Private Function HelperScriptPath() As String
        Try
            Dim baseDir As String = Path.GetDirectoryName(Application.ExecutablePath)
            If String.IsNullOrEmpty(baseDir) Then Return ""
            Return Path.Combine(baseDir, HelperScriptName)
        Catch
            Return ""
        End Try
    End Function

End Module
