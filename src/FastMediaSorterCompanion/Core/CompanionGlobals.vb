''' <summary>
''' The handful of LITE globals the ported Share code depends on. Kept as a tiny
''' module so the port stays almost verbatim (spec §6.1/§6.3). The registry
''' coordinates are IDENTICAL to LITE's (HKCU VB settings under SZA\FastMediaSorter),
''' so Companion reads/writes the same keys LITE always used - no settings
''' migration (spec §9.1) and the language choice stays in sync (invariant 9).
''' </summary>
Public Module CompanionGlobals

    Public ReadOnly App_name As String = "SZA"
    Public ReadOnly Second_App_Name As String = "FastMediaSorter"

    ' The old RU/EN boolean is gone: since SPECIFICATION_THIRTEEN_UI_LANGUAGES.md block A'
    ' the language is an ISO code and every call site goes through Localization.T/TF.
    ' Nothing here needs a two-way branch, so keeping a derived boolean would only invite
    ' new ones - a parity test asserts none comes back.

    ''' <summary>
    ''' Load the UI language from the same registry values LITE persists, so a switch
    ''' in either program shows in the other on next launch (invariant 9). Companion is
    ''' now a full peer rather than a reader: it can set the language too, and
    ''' first-run detection falls back to the Windows display language exactly as
    ''' in LITE.
    ''' </summary>
    Public Sub LoadLanguage()
        Try
            Localization.LoadFromSettings()
        Catch
            Localization.CurrentCode = "en"
        End Try
    End Sub

End Module
