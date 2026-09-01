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

    ''' <summary>The SFTP listen port (015_SPECIFICATION_SHARE_MANUAL_PORT.md). It is a
    ''' GUARANTEED setting, not a mode: this number is printed in every QR code and .fmscfg
    ''' handed out and typed by hand into router forwarding rules, so it moves when the owner
    ''' moves it and at no other time. <see cref="UnsetPort"/> (0) is not "automatic" - it
    ''' only means nobody has recorded a number on THIS side yet, in which case the worker's
    ''' own persisted port (Status.DesiredPort) is the answer; the OS chooses exactly once,
    ''' on the first ever start, and that choice is then permanent too.</summary>
    Public Property ListenPort As Integer = UnsetPort ' Share_ListenPort

    ''' <summary>"No number recorded here" - never a request to let the port float.</summary>
    Public Const UnsetPort As Integer = 0

    ''' <summary>Floor for a chosen port. NOT a permission constraint - unlike Unix, Windows
    ''' lets any process bind 22 or 80 - but a footgun guard: low ports collide with system
    ''' listeners (OpenSSH on 22, HTTP.SYS on 80/443, WinRM on 5985) and the bind failure
    ''' reads as our bug.</summary>
    Public Const MinFixedPort As Integer = 1024

    Public Const MaxFixedPort As Integer = 65535

    ''' <summary>Recommended ceiling, carried by the UI hint rather than enforced: 49152 is
    ''' where the Windows dynamic/ephemeral range starts, and a port inside it can be taken
    ''' by any outgoing connection from any process before the worker starts - which is the
    ''' one way a guaranteed port can still fail to come up.</summary>
    Public Const RecommendedMaxFixedPort As Integer = 49151

    ''' <summary>Offered only when nothing better is known - the normal path shows the port
    ''' the server is already on, so nobody has to invent a number.</summary>
    Public Const SuggestedFixedPort As Integer = 2222

    ' --- Share Manager window geometry + section state -------------------------
    '
    ' The window used to have neither: a 980x700 MinimumSize scaled to 1715x1225 at 175%
    ' display scaling, i.e. taller than the screen, so ClampToWorkingArea - a safety net -
    ' was doing the sizing and every session opened full height. The window is small and
    ' resizable now (560x420 minimum), which only pays off if it is also remembered.
    '
    ' Sizes are the WINDOW size (outer bounds, what MinimumSize/Size measure) in LOGICAL
    ' px, i.e. divided by the display factor on save and multiplied back on restore, so a
    ' window sized on a 175% monitor reopens the same physical size on a 100% one.
    ' Positions are raw screen coordinates - a logical position means nothing across a
    ' multi-monitor desktop - and are only honoured when they still land on a screen.

    ''' <summary>Last window position, raw screen px. -1 = never saved -> CenterScreen.</summary>
    Public Property WindowX As Integer = -1               ' Share_WindowX
    Public Property WindowY As Integer = -1               ' Share_WindowY

    ''' <summary>Last window size in logical (96-DPI) px; 0 = never saved -> the default.</summary>
    Public Property WindowWidth As Integer = 0            ' Share_WindowWidth
    Public Property WindowHeight As Integer = 0           ' Share_WindowHeight

    ''' <summary>Restore the window maximised. The size above stays the NORMAL size, taken
    ''' from RestoreBounds, so un-maximising lands back on the remembered rectangle.</summary>
    Public Property WindowMaximized As Boolean = False    ' Share_WindowMaximized

    ''' <summary>Which collapsible sections are open, as a CSV of section keys
    ''' ("access,internet,stats"). ONE value rather than one flag per section: a section
    ''' added later is simply not in the list and starts collapsed - no migration, no
    ''' orphan registry value when one is removed.</summary>
    Public Property ExpandedSections As String = ""       ' Share_ExpandedSections

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
        ListenPort = ClampPort(ReadInt("Share_ListenPort", UnsetPort))
        WindowX = ReadInt("Share_WindowX", -1)
        WindowY = ReadInt("Share_WindowY", -1)
        WindowWidth = ReadInt("Share_WindowWidth", 0)
        WindowHeight = ReadInt("Share_WindowHeight", 0)
        WindowMaximized = ReadBool("Share_WindowMaximized", False)
        ExpandedSections = ReadString("Share_ExpandedSections", "")
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
        WriteInt("Share_ListenPort", ClampPort(ListenPort))
        WriteInt("Share_WindowX", WindowX)
        WriteInt("Share_WindowY", WindowY)
        WriteInt("Share_WindowWidth", WindowWidth)
        WriteInt("Share_WindowHeight", WindowHeight)
        WriteBool("Share_WindowMaximized", WindowMaximized)
        WriteString("Share_ExpandedSections", If(ExpandedSections, ""))
        ' ServerFeaturesEnabled is intentionally NOT written here - ServerFeatures
        ' owns that flag (see the property remark).
    End Sub

    ''' <summary>Clamps a listen-port value: 0 (and anything below it) means "not recorded",
    ''' and a chosen port is kept inside [1024, 65535]. Everything reading or writing the
    ''' setting goes through here, so a hand-edited registry value can never reach the
    ''' worker.</summary>
    Public Shared Function ClampPort(value As Integer) As Integer
        If value <= UnsetPort Then Return UnsetPort
        If value < MinFixedPort Then Return MinFixedPort
        If value > MaxFixedPort Then Return MaxFixedPort
        Return value
    End Function

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

    Private Shared Function ReadString(key As String, def As String) As String
        Return GetSetting(App_name, Second_App_Name, key, def)
    End Function

    Private Shared Sub WriteString(key As String, value As String)
        SaveSetting(App_name, Second_App_Name, key, If(value, ""))
    End Sub

End Class
