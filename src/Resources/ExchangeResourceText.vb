#If Not NETFRAMEWORK Then
Option Strict On

' Ticket 171: the words of the "My devices" view, the rows it adds to the resource list, and the tunnel's prompts. One sentence per state and per
' outcome; a state is never shown as an error ("not supported by this version" is a state, not a failure). The product name appears only where it is
' the NAME of another program (Share Manager), which is a proper name and is not translated. House style: ".." for an ellipsis, a plain hyphen, ё.

Friend NotInheritable Class ExchangeResourceText
    Private Sub New()
    End Sub

    Friend Shared Function SectionTitle() As String
        Return Localization.T("Мои устройства")
    End Function

    Friend Shared Function Availability(state As ExchangeDevicesState, hasRows As Boolean) As String
        Select Case state.Availability
            Case ExchangeDevicesAvailability.NotRunning
                Return Localization.T("Share Manager не запущен, поэтому папки ваших устройств не видны.")
            Case ExchangeDevicesAvailability.NotSignedIn
                Return Localization.T("Войдите в учётную запись обмена в Share Manager - и здесь появятся папки ваших устройств.")
            Case ExchangeDevicesAvailability.Offline
                Return Localization.T("Нет связи с сервером обмена. Список появится, когда связь восстановится.")
            Case ExchangeDevicesAvailability.Ready
                Return If(hasRows, "", Localization.T("У других ваших устройств пока нет опубликованных папок. Включите публикацию в Share Manager на другом компьютере или на телефоне."))
            Case Else
                Return ""
        End Select
    End Function

    Friend Shared Function StartButton() As String
        Return Localization.T("Запустить Share Manager")
    End Function

    Friend Shared Function Starting() As String
        Return Localization.T("Запускаю Share Manager..")
    End Function

    Friend Shared Function StartFailed() As String
        Return Localization.T("Не удалось запустить Share Manager.")
    End Function

    Friend Shared Function DeviceHeader(name As String, online As Boolean) As String
        Return name & " - " & If(online, Localization.T("в сети"), Localization.T("не в сети"))
    End Function

    Friend Shared Function NoFolders() As String
        Return Localization.T("нет опубликованных папок")
    End Function

    ''' <summary>The state text of a row of the view: an attached one says what it is now; an unattached one says why it cannot be ticked.</summary>
    Friend Shared Function RowState(row As ExchangeDeviceRow) As String
        If Not row.Supported Then Return Localization.T("не поддерживается этой версией")
        If row.Attached Then
            Select Case row.State
                Case ExchangeResourceState.DeviceOffline : Return Localization.T("недоступно: устройство не в сети")
                Case ExchangeResourceState.NoLongerPublished : Return Localization.T("больше не опубликовано")
                Case Else : Return ""
            End Select
        End If
        Return If(row.DeviceOnline, "", Localization.T("не в сети, подключить сейчас нельзя"))
    End Function

    ''' <summary>The state text of an attached resource in the main list (the same words).</summary>
    Friend Shared Function ListState(state As ExchangeResourceState) As String
        Select Case state
            Case ExchangeResourceState.DeviceOffline : Return Localization.T("недоступно: устройство не в сети")
            Case ExchangeResourceState.NoLongerPublished : Return Localization.T("больше не опубликовано")
            Case Else : Return ""
        End Select
    End Function

    Friend Shared Function TypeOfDevice(deviceName As String) As String
        Return Localization.TF("Моё устройство: {0}", If(String.IsNullOrEmpty(deviceName), "?", deviceName))
    End Function

    Friend Shared Function TypeOfLink() As String
        Return Localization.T("Ссылка через сервер обмена")
    End Function

    Friend Shared Function AccessUpdatedCell() As String
        Return Localization.T("доступ обновлён")
    End Function

    Friend Shared Function NoticeLine(name As String) As String
        Return Localization.TF("«{0}»: адрес, ключ или пароль этого ресурса обновило его устройство.", name)
    End Function

    Friend Shared Function Attached(name As String) As String
        Return Localization.TF("Папка «{0}» добавлена в ресурсы.", name)
    End Function

    Friend Shared Function AttachFailure(outcome As ExchangeAttachOutcome) As String
        Select Case outcome
            Case ExchangeAttachOutcome.AlreadyAttached : Return Localization.T("Эта папка уже подключена.")
            Case ExchangeAttachOutcome.NoAccess : Return Localization.T("Устройство не в сети: подключить папку сейчас нельзя.")
            Case ExchangeAttachOutcome.Unusable : Return Localization.T("Эту папку нельзя открыть в этой программе.")
            Case ExchangeAttachOutcome.AgentUnavailable : Return Localization.T("Share Manager не ответил.")
            Case Else : Return Localization.T("Не удалось сохранить ресурс.")
        End Select
    End Function

    Friend Shared Function CertificateQuestion(host As String, fingerprint As String) As String
        Return Localization.TF("Сертификат сервера обмена {0} программе незнаком.{1}Его отпечаток:{1}{2}{1}{1}Сверьте его с тем, что дал человек, приславший ссылку. Доверять этому серверу и запомнить его для этой ссылки?",
                               host, vbLf, fingerprint)
    End Function

    Friend Shared Function CertificateTitle() As String
        Return Localization.T("Новый сервер обмена")
    End Function

End Class
#End If
