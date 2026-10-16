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
' See docs/specifications/013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md block A'.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub RegisterStrings()
#If Not NETFRAMEWORK Then
        AddExchangeVocabularyStrings()
#End If
        AddShareStrings()       ' tray menu + the shared ShareText prose
        AddManagerContractStrings()
        AddManagerStrings()     ' the Share Manager main window
        AddWizardStrings()      ' the one-shot package wizard + QR zoom
        AddInternetStrings()    ' the internet-access tab
        AddParamsStrings()      ' per-folder options + the status window
        AddHostingStrings()     ' User vs Server edition hosting + service management
        AddAppUxStrings()       ' ticket 028: confirmations, failure actions, consent, accessible names
        AddActivationStrings()  ' ticket 105/108: single-instance hand-off, folder-request consent
        AddThemeStrings()       ' spec 060: the theme setting (table linked from the viewer)
        AddBroadcastStrings()   ' spec 196: streamsplayer broadcast resource management
        AddDiskStrings()        ' DISK-SHARE: file-backed containers and local drive aliases
        AddExchangeStrings()    ' tickets 142-144: the Media Exchange Server group and tray item
        AddServerWelcomeStrings()      ' spec 185: the server welcome - the steps of the share server and the exchange server
        AddResourceAddStrings()        ' spec 214: guided add with shared resource controls
        AddPhoneCodeStrings()          ' spec 227: the phone's connection code door and the camera relay
        AddExchangeFirstRunStrings()   ' spec 213: local exchange server onboarding
        AddManagerWelcomeStrings()     ' spec 212: share the first folder, startup trigger and replay
        AddExchangePublishStrings()    ' ticket 170: the producer rows - publishing the shares, the third-party port, outsider links (replaces 148's)
        AddExchangeDeviceStrings()     ' ticket 180: the device agent - the "Exchange account" group and its sign-in dialog
        AddExchangeAdminStrings()      ' ticket 168: the exchange server administration - accounts, devices, address, ports, certificate, statement, wizard
        AddExchangeStringStrings()     ' ticket 187 T-2: the operator's "Get connection string" action
        AddExchangeJoinStrings()       ' ticket 187 T-5: the quick join by connection string and the clean leave
        AddLiveBroadcastStrings()      ' ticket 199: the live broadcast window, tray item and refusals (never 016's "Трансляции для StreamsPlayer")
        AddSendLogsStrings()    ' ticket 182 L-3/L-6: a log that cannot be included, no mail program (table linked from the viewer)
        AddScheduledStrings()   ' ticket 222: scheduled file operations - the page, the editor, the history, the tray items
        AddClipboardStrings()   ' spec 230: the clipboard through the exchange server - buttons, tray items, shortcuts, result lines
        AddOutwardAccessStrings()      ' ticket 239 CR-13: the guest invitation and the link with a password shown as not available
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
