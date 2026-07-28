Option Strict On

''' <summary>
''' Persisted Android-share preferences, stored in the app's registry store
''' (VB SaveSetting/GetSetting, HKCU ...\SZA\FastMediaSorter) - mirrors the
''' OcrTranslateSettings POCO pattern. The shared-folder list itself is NOT
''' duplicated here: the worker owns and persists it (shares.json). Only
''' LITE-side UX state lives here.
''' </summary>
Public Class ShareSettings

    ''' <summary>Opt-in HKCU Run autostart (unpackaged channels). Default off.</summary>
    Public Property AutostartEnabled As Boolean = False   ' Share_AutostartEnabled

    ''' <summary>Show the Share Manager window on a plain start - the logon autostart, a
    ''' double-click on the exe, a script - instead of the tray icon alone. Independent of
    ''' <see cref="AutostartEnabled"/>: that one decides WHETHER the program starts, this one
    ''' whether it shows itself. Default off, and off means off on every such start; only an
    ''' explicit request (a folder to share, Program.ShowWindowFlag from LITE's Share buttons,
    ''' the tray icon) opens the window regardless.</summary>
    Public Property OpenWindowOnStartup As Boolean = False ' Share_OpenWindowOnStartup

    ''' <summary>UX hint: the user has started the worker at least once.</summary>
    Public Property WorkerEverStarted As Boolean = False  ' Share_WorkerEverStarted

    ''' <summary>The user wants the internet-access section shown / intends to
    ''' expose the share to the internet. Default ON since S1006: the phone now
    ''' races all accessPaths and prefers LAN, so the combined [lan, portforward]
    ''' config is the sensible default ("one scan works home and away") and the
    ''' WAN entry is only actually emitted when a usable non-CGNAT path exists.
    ''' The worker still auto-attempts UPnP regardless (it has no knob), so this
    ''' drives the guidance + security warning, not enforcement.</summary>
    Public Property ExternalAccessIntent As Boolean = True ' Share_ExternalAccessIntent

    ''' <summary>The deliberate privacy opt-out for the Share tab's primary export:
    ''' when ON, the exported QR/.fmscfg carries ONLY the LAN accessPath (the WAN
    ''' address is never embedded, even when a port-forward is available). Default
    ''' OFF = the combined [lan, portforward] config (S1006). Distinct from
    ''' ExternalAccessIntent, which is the wizard's show-internet-section flag.</summary>
    Public Property LanOnlyExport As Boolean = False ' Share_LanOnlyExport

    ''' <summary>The §6 "exclude password" safeguard: export .fmscfg/QR with an
    ''' empty password so the recipient types it at import; the sender passes the
    ''' real password out-of-band (shown in the hint while the toggle is on).</summary>
    Public Property ExcludePasswordFromExport As Boolean = False ' Share_ExcludePassword

    ''' <summary>Maximum simultaneous SFTP connections the worker accepts. Default 10;
    ''' the user may set anything from 1 to 99999 (their server, their call). Pushed to
    ''' the worker via SetNetworkPolicy; the worker clamps and enforces it. See the
    ''' 2026-07-15 security hardening spec (DoS resilience).</summary>
    Public Property MaxConnections As Integer = DefaultMaxConnections ' Share_MaxConnections

    Public Const DefaultMaxConnections As Integer = 10
    Public Const MinMaxConnections As Integer = 1
    Public Const MaxMaxConnections As Integer = 99999

    ''' <summary>Read-only mirror of the server-features consent flag (HKCU
    ''' Share_ServerFeaturesEnabled), the deferred opt-in gate. OWNED and written by
    ''' <see cref="ServerFeatures"/>; loaded here only for convenience and
    ''' deliberately NOT persisted by <see cref="Save"/>, so a stale POCO can never
    ''' clobber a consent the runtime opt-in just recorded.</summary>
    Public Property ServerFeaturesEnabled As Boolean = False ' Share_ServerFeaturesEnabled (read-only mirror)

    Public Sub Load()
        AutostartEnabled = ReadBool("Share_AutostartEnabled", False)
        OpenWindowOnStartup = ReadBool("Share_OpenWindowOnStartup", False)
        WorkerEverStarted = ReadBool("Share_WorkerEverStarted", False)
        ExternalAccessIntent = ReadBool("Share_ExternalAccessIntent", True)
        LanOnlyExport = ReadBool("Share_LanOnlyExport", False)
        ExcludePasswordFromExport = ReadBool("Share_ExcludePassword", False)
        MaxConnections = ClampConnections(ReadInt("Share_MaxConnections", DefaultMaxConnections))
        ServerFeaturesEnabled = ReadBool(ServerFeatures.EnabledRegValue, False)
    End Sub

    Public Sub Save()
        WriteBool("Share_AutostartEnabled", AutostartEnabled)
        WriteBool("Share_OpenWindowOnStartup", OpenWindowOnStartup)
        WriteBool("Share_WorkerEverStarted", WorkerEverStarted)
        WriteBool("Share_ExternalAccessIntent", ExternalAccessIntent)
        WriteBool("Share_LanOnlyExport", LanOnlyExport)
        WriteBool("Share_ExcludePassword", ExcludePasswordFromExport)
        WriteInt("Share_MaxConnections", ClampConnections(MaxConnections))
        ' ServerFeaturesEnabled is intentionally NOT written here - ServerFeatures
        ' owns that flag (see the property remark).
    End Sub

    ''' <summary>Clamps a connection-limit value into the accepted [1, 99999] range;
    ''' a stored 0 (never set) degrades to the default.</summary>
    Public Shared Function ClampConnections(value As Integer) As Integer
        If value <= 0 Then Return DefaultMaxConnections
        If value < MinMaxConnections Then Return MinMaxConnections
        If value > MaxMaxConnections Then Return MaxMaxConnections
        Return value
    End Function

    ' --- registry helpers (SZA\FastMediaSorter) -------------------------------

    Private Shared Function ReadBool(key As String, def As Boolean) As Boolean
        Return GetSetting(App_name, Second_App_Name, key, If(def, "1", "0")) = "1"
    End Function

    Private Shared Sub WriteBool(key As String, value As Boolean)
        SaveSetting(App_name, Second_App_Name, key, If(value, "1", "0"))
    End Sub

    Private Shared Function ReadInt(key As String, def As Integer) As Integer
        Dim raw As String = GetSetting(App_name, Second_App_Name, key, def.ToString(Globalization.CultureInfo.InvariantCulture))
        Dim parsed As Integer
        If Integer.TryParse(raw, Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture, parsed) Then
            Return parsed
        End If
        Return def
    End Function

    Private Shared Sub WriteInt(key As String, value As Integer)
        SaveSetting(App_name, Second_App_Name, key, value.ToString(Globalization.CultureInfo.InvariantCulture))
    End Sub

End Class
