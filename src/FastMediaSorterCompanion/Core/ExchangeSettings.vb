Option Strict On

''' <summary>
''' The Share Manager's own preferences about the exchange server (ticket 142), in the same HKCU store
''' as <see cref="ShareSettings"/> (VB SaveSetting/GetSetting, SZA\FastMediaSorter, values named
''' <c>Exchange_*</c>).
'''
''' What is NOT here, on purpose: the password and its hash (the hash lives only in the server's own
''' config file; the plaintext only in the dialog that shows it, and - when the person turned keeping on -
''' in the DPAPI file of <see cref="ExchangeAccountPasswordStore"/>, never in this registry), the certificate
''' fingerprint (read from the binary on demand), and the server's configuration as such - this POCO is UX state,
''' not the server's. The values here are all safe to send in a log package (LogPackage.SafeSettingNames), and the
''' keeping switch is not on that list, so a log package withholds it.
''' </summary>
Public Class ExchangeSettings

    ''' <summary>The Share Manager stops a server it started when it exits unless this is on. Default
    ''' off (Q142-2): the interactive server is the user's foreground choice, never a background
    ''' resident they did not ask for (133 section 3, "no autostart").</summary>
    Public Property KeepRunningOnExit As Boolean = False   ' Exchange_KeepRunningOnExit

    ''' <summary>UX hint: the user has started the server at least once from here. Decides whether the
    ''' state line says "not started yet" or "stopped".</summary>
    Public Property EverStarted As Boolean = False         ' Exchange_EverStarted

    ''' <summary>Keep the account passwords on this PC, so they can be read again (<see cref="ExchangeAccountPasswordStore"/>).
    ''' Default off: a password is shown once and can only be reset - the rule the Share Manager has always had. Turning it on
    ''' is a question the person answers with a warning in front of them (the form does that, not this class).</summary>
    Public Property KeepAccountPasswords As Boolean = False   ' Exchange_KeepAccountPasswords

    ''' <summary>The listen port last written through the Share Manager, or <see cref="UnsetPort"/>. A
    ''' MIRROR for display when the binary cannot be asked: the server's own config file is the only
    ''' authority, and the number reaches it only through <see cref="ExchangeConfigFile"/>.</summary>
    Public Property ListenPort As Integer = UnsetPort      ' Exchange_ListenPort

    ''' <summary>"No number recorded here" - the server then shows its own default.</summary>
    Public Const UnsetPort As Integer = 0

    ''' <summary>The server's default (config.Default in the Go module). Used only as the editor's
    ''' initial value when nothing can be read.</summary>
    Public Const DefaultPort As Integer = 22022

    ''' <summary>Floor, ceiling and the footgun guard of the listen port: the same reasoning as
    ''' <see cref="ShareSettings.MinFixedPort"/> (low ports collide with system listeners).</summary>
    Public Const MinPort As Integer = ShareSettings.MinFixedPort
    Public Const MaxPort As Integer = ShareSettings.MaxFixedPort

    Friend Const KeepRunningName As String = "Exchange_KeepRunningOnExit"
    Friend Const EverStartedName As String = "Exchange_EverStarted"
    Friend Const ListenPortName As String = "Exchange_ListenPort"
    Friend Const KeepAccountPasswordsName As String = "Exchange_KeepAccountPasswords"

    Public Sub Load()
        KeepRunningOnExit = ReadBool(KeepRunningName, False)
        EverStarted = ReadBool(EverStartedName, False)
        ListenPort = ClampPort(ReadInt(ListenPortName, UnsetPort))
        KeepAccountPasswords = ReadBool(KeepAccountPasswordsName, False)
    End Sub

    Public Sub Save()
        WriteBool(KeepRunningName, KeepRunningOnExit)
        WriteBool(EverStartedName, EverStarted)
        WriteInt(ListenPortName, ClampPort(ListenPort))
        WriteBool(KeepAccountPasswordsName, KeepAccountPasswords)
    End Sub

    ''' <summary>0 and below mean "not recorded"; a chosen port stays inside [MinPort, MaxPort]. Every
    ''' reader and writer goes through here, so a hand-edited registry value never reaches the file.</summary>
    Public Shared Function ClampPort(value As Integer) As Integer
        If value <= UnsetPort Then Return UnsetPort
        If value < MinPort Then Return MinPort
        If value > MaxPort Then Return MaxPort
        Return value
    End Function

    Private Shared Function ReadBool(key As String, def As Boolean) As Boolean
        Return GetSetting(App_name, Second_App_Name, key, If(def, "1", "0")) = "1"
    End Function

    Private Shared Sub WriteBool(key As String, value As Boolean)
        SaveSetting(App_name, Second_App_Name, key, If(value, "1", "0"))
    End Sub

    Private Shared Function ReadInt(key As String, def As Integer) As Integer
        Dim raw As String = GetSetting(App_name, Second_App_Name, key, def.ToString(Globalization.CultureInfo.InvariantCulture))
        Dim parsed As Integer
        If Integer.TryParse(raw, Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture, parsed) Then Return parsed
        Return def
    End Function

    Private Shared Sub WriteInt(key As String, value As Integer)
        SaveSetting(App_name, Second_App_Name, key, value.ToString(Globalization.CultureInfo.InvariantCulture))
    End Sub

End Class
