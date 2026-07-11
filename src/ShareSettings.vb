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

    ''' <summary>UX hint: the user has started the worker at least once.</summary>
    Public Property WorkerEverStarted As Boolean = False  ' Share_WorkerEverStarted

    ''' <summary>The user wants the internet-access section shown / intends to
    ''' expose the share to the internet. Default off = LAN only. This is a UI
    ''' intent flag: the worker still auto-attempts UPnP regardless (it has no
    ''' knob), so this drives the guidance + security warning, not enforcement.</summary>
    Public Property ExternalAccessIntent As Boolean = False ' Share_ExternalAccessIntent

    ''' <summary>The §6 "exclude password" safeguard: export .fmscfg/QR with an
    ''' empty password so the recipient types it at import; the sender passes the
    ''' real password out-of-band (shown in the hint while the toggle is on).</summary>
    Public Property ExcludePasswordFromExport As Boolean = False ' Share_ExcludePassword

    Public Sub Load()
        AutostartEnabled = ReadBool("Share_AutostartEnabled", False)
        WorkerEverStarted = ReadBool("Share_WorkerEverStarted", False)
        ExternalAccessIntent = ReadBool("Share_ExternalAccessIntent", False)
        ExcludePasswordFromExport = ReadBool("Share_ExcludePassword", False)
    End Sub

    Public Sub Save()
        WriteBool("Share_AutostartEnabled", AutostartEnabled)
        WriteBool("Share_WorkerEverStarted", WorkerEverStarted)
        WriteBool("Share_ExternalAccessIntent", ExternalAccessIntent)
        WriteBool("Share_ExcludePassword", ExcludePasswordFromExport)
    End Sub

    ' --- registry helpers (SZA\FastMediaSorter) -------------------------------

    Private Shared Function ReadBool(key As String, def As Boolean) As Boolean
        Return GetSetting(App_name, Second_App_Name, key, If(def, "1", "0")) = "1"
    End Function

    Private Shared Sub WriteBool(key As String, value As Boolean)
        SaveSetting(App_name, Second_App_Name, key, If(value, "1", "0"))
    End Sub

End Class
