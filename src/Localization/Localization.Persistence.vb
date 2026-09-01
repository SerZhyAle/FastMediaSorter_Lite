Option Strict On

Imports Microsoft.VisualBasic

' <summary>
' Where the chosen UI language lives and how an existing install migrates onto it.
' See docs/specifications/013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md §2.4.
' </summary>
Partial Public NotInheritable Class Localization

    ' <summary>The registry value holding the ISO code, e.g. "de".</summary>
    Public Const UiLanguageValue As String = "UiLanguage"

    ' <summary>
    ' The pre-13-languages value. Still written on every save as a derived mirror, so
    ' rolling back to an older exe - or running a Share Manager that has not been
    ' migrated yet - still lands on a sane language instead of a blank setting.
    ' </summary>
    Public Const LegacyRussianValue As String = "Is_Russian_Language"

    ' <summary>
    ' Resolve the language to start in. First match wins:
    '   1. UiLanguage, when it names a language this build ships;
    '   2. Is_Russian_Language from an older install ("1" -> ru, "0" -> en);
    '   3. the Windows display language, else English.
    ' </summary>
    Public Shared Sub LoadFromSettings()
        Dim stored = Interaction.GetSetting(App_name, Second_App_Name, UiLanguageValue, "")
        If stored IsNot Nothing AndAlso stored.Length > 0 Then
            For i = 0 To Codes.Length - 1
                If String.Equals(Codes(i), stored, StringComparison.OrdinalIgnoreCase) Then
                    CurrentCode = Codes(i)
                    Return
                End If
            Next
            ' A code this build does not ship (a newer version wrote it, or the x64 exe
            ' picked Hindi and the user then started the x86 one). Fall through to the
            ' legacy value rather than silently pinning English forever.
        End If

        Dim legacy = Interaction.GetSetting(App_name, Second_App_Name, LegacyRussianValue, "")
        If legacy = "1" Then
            CurrentCode = "ru"
        ElseIf legacy = "0" Then
            CurrentCode = "en"
        Else
            CurrentCode = DefaultCode()
        End If
    End Sub

    ' <summary>Persist the active language, plus the legacy mirror.</summary>
    Public Shared Sub SaveToSettings()
        Interaction.SaveSetting(App_name, Second_App_Name, UiLanguageValue, CurrentCode)
        Interaction.SaveSetting(App_name, Second_App_Name, LegacyRussianValue,
                                If(CurrentCode = "ru", "1", "0"))
    End Sub

End Class
