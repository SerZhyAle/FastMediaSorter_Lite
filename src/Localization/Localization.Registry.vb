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
        AddSettingsExpansionStrings()   ' SPECIFICATION_SETTINGS_EXPANSION: scale, shortcuts, history
        AddSupportStrings()     ' "send the logs to the author" row, dialog and mail body
        AddEditorStrings()      ' the image editor window and its save path
        AddArchiveStrings()     ' browsing an archive as a folder
        AddVideoStrings()       ' the decode cache's settings rows + "Replace with video"
        AddSlotHealthStrings()  ' recipient-slot verdicts, auto-created folders, honest failures
        AddSecureStrings()      ' secure .fd-sec containers: the password dialog and the three outcomes
        AddAppUxStrings()       ' ticket 028: confirmations, failure causes, the download dialog, accessible names
        AddThemeStrings()       ' spec 060: the system / light / dark theme setting
        AddSortableContentStrings()  ' spec 003 PK: the opt-in content kinds
        AddPanelStrings()      ' spec 005 PG: the F3 panel toolbar, details list, report window
        AddActivationStrings() ' ticket 108 H01: the Share Manager hand-off
        AddSettingsImportStrings() ' tickets 103 and 105 L12: the settings import preview
        AddStreamStrings()         ' spec 022: stream catalog producer and validator
    End Sub

End Class
