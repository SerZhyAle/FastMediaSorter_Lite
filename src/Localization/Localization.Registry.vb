Option Strict On

' <summary>
' The viewer's list of string tables. Each project that compiles Localization.vb
' must supply exactly one RegisterStrings() - the shared constructor calls it, so
' forgetting the file fails the build with BC30451 rather than silently shipping an
' untranslated exe.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub RegisterStrings()
        AddChromeStrings()      ' main-window captions + the first-run help
        AddRuntimeStrings()     ' strings carrying a {0} value + multi-line messages
        AddMainStrings()        ' -> AddMediaStrings, AddFileStrings
        AddNavStrings()             ' .NET 10 shell navigation + page headers
        AddSettingsChromeStrings()  ' classic settings window captions
        AddSettingsStrings()    ' -> AddSettingsHintStrings -> AddSettingsDescriptionStrings, AddOcrStrings
        AddMenuStrings()        ' media context menus, toolbar overflow, status hints
        AddOcrTabStrings()      ' the OCR & translation tab of the settings window
        AddChoiceStrings()      ' drop-down options of the expanded (.NET 10) settings
    End Sub

End Class
