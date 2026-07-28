Option Strict On

' <summary>
' The Share Manager's list of string tables.
'
' The localization ENGINE (Localization.vb + Localization.Persistence.vb) is linked in
' from the viewer, unchanged; only this file and the tables it names are Companion's
' own. The engine's shared constructor calls RegisterStrings(), so a project that
' links the engine and forgets its tables fails the build with BC30451 instead of
' silently shipping an English-only exe.
'
' See docs/specifications/SPECIFICATION_THIRTEEN_UI_LANGUAGES.md block A'.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub RegisterStrings()
        AddShareStrings()       ' tray menu + the shared ShareText prose
        AddManagerStrings()     ' the Share Manager main window
        AddWizardStrings()      ' the one-shot package wizard + QR zoom
        AddInternetStrings()    ' the internet-access tab
        AddParamsStrings()      ' per-folder options + the status window
    End Sub

    ''' <summary>
    ''' Language of the offline port-forward guide (assets/help/port-forward.html).
    '''
    ''' That guide is a long hand-written document, deliberately kept to the three
    ''' languages its author can proofread (spec §3.1) - unlike the UI it is not
    ''' machine-translated. A Hindi or Arabic user gets the English version rather
    ''' than a page that silently does not exist.
    ''' </summary>
    Public Shared Function GuideCode() As String
        Select Case CurrentCode
            Case "ru", "uk" : Return CurrentCode
            Case Else : Return "en"
        End Select
    End Function

End Class
