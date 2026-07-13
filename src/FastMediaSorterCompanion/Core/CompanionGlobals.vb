Imports Microsoft.VisualBasic

''' <summary>
''' The handful of LITE globals the ported Share code depends on. Kept as a tiny
''' module so the port stays almost verbatim (spec §6.1/§6.3). The registry
''' coordinates are IDENTICAL to LITE's (HKCU VB settings under SZA\FastMediaSorter),
''' so Companion reads/writes the same keys LITE always used - no settings
''' migration (spec §9.1) and the language flag stays in sync (invariant 9).
''' </summary>
Public Module CompanionGlobals

    Public ReadOnly App_name As String = "SZA"
    Public ReadOnly Second_App_Name As String = "FastMediaSorter"

    ''' <summary>UI language flag, read from the SAME registry value LITE persists,
    ''' so a switch in either program shows in the other on next launch (invariant 9).
    ''' Companion never owns first-run system-language detection - that stays LITE's;
    ''' here we just read the stored choice (default English).</summary>
    Public Is_Russian_Language As Boolean

    Public Sub LoadLanguage()
        Try
            Is_Russian_Language = (GetSetting(App_name, Second_App_Name, "Is_Russian_Language", "0") = "1")
        Catch
            Is_Russian_Language = False
        End Try
    End Sub

End Module
