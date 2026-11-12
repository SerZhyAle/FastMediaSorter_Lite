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

    Friend Shared Function DeviceHeader(name As String, online As Boolean, Optional sharePaused As Boolean = False) As String
        If sharePaused Then Return name & " - " & ExchangeResourceText.SharePaused()
        Return name & " - " & If(online, Localization.T("в сети"), Localization.T("не в сети"))
    End Function

    ''' <summary>Item DD: the row of the common message table, shown in place of plain offline on the device and on each of its resources.</summary>
    Friend Shared Function SharePaused() As String
        Return SealedText(ConnectionDiagnosisMessages.SharePaused)
    End Function

    Friend Shared Function NoFolders() As String
        Return Localization.T("нет опубликованных папок")
    End Function

    ''' <summary>The state text of a row of the view: an attached one says what it is now; an unattached one says why it cannot be ticked.</summary>
    Friend Shared Function RowState(row As ExchangeDeviceRow) As String
        If row.State = ExchangeResourceState.SharePaused Then Return SharePaused()
        If row.PinProtected Then Return If(row.Supported, Localization.T("защищено PIN-кодом"), Localization.T("заблокировано: защита не поддерживается"))
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
            Case ExchangeResourceState.SharePaused : Return SharePaused()
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
            Case ExchangeAttachOutcome.ConnectionFailed : Return ResourceMessages.ForFailure(Nothing)
            Case ExchangeAttachOutcome.SignInAgain : Return Localization.T("Чтобы подключить папки с PIN, войдите в учётную запись сервера обмена ещё раз.")
            Case ExchangeAttachOutcome.E2eMissing : Return SealedText(ConnectionDiagnosisMessages.E2eMissing)
            Case ExchangeAttachOutcome.KeyChanged : Return SealedText(ConnectionDiagnosisMessages.KeyChanged)
            Case ExchangeAttachOutcome.ServerNoE2e : Return SealedText(ConnectionDiagnosisMessages.ServerNoE2e)
            Case ExchangeAttachOutcome.SealedFailed : Return SealedText(ConnectionDiagnosisMessages.SealedFailed)
            Case Else : Return Localization.T("Не удалось сохранить ресурс.")
        End Select
    End Function

    ''' <summary>The row of the common connection-message table for a sealed state (items DI, DJ, DG, DP), in the current language. A code
    ''' the table does not know reads as its generic refusal, never as the code itself; no new string is added for it.</summary>
    Friend Shared Function SealedText(message As ConnectionDiagnosisMessage) As String
        Dim locale As String = Localization.CurrentCode
        Dim text As String = ConnectionDiagnosisLocalization.GetMessageText(message.Code, locale)
        If text.Length = 0 OrElse String.Equals(text, message.Code, StringComparison.Ordinal) Then
            text = ConnectionDiagnosisLocalization.GetMessageText(ConnectionDiagnosisMessages.Refused.Code, locale)
        End If
        Return text
    End Function

    Friend Shared Function CertificateQuestion(host As String, fingerprint As String) As String
        Return Localization.TF("Сертификат сервера обмена {0} программе незнаком.{1}Его отпечаток:{1}{2}{1}{1}Сверьте его с тем, что дал человек, приславший ссылку. Доверять этому серверу и запомнить его для этой ссылки?",
                               host, vbLf, fingerprint)
    End Function

    Friend Shared Function CertificateTitle() As String
        Return Localization.T("Новый сервер обмена")
    End Function

    ' --- a folder reached through Google Drive (ticket 269 P3.3, DEVICE-EXCHANGE items CL, CO, CQ) ---

    ''' <summary>Item CQ's waiting line: the device and the time it last wrote its record, when known.</summary>
    Friend Shared Function WaitingViaDrive(device As String, lastRecord As DateTimeOffset?) As String
        If lastRecord.HasValue Then
            Return Localization.TF("Ожидание «{0}» через GD - последняя запись устройства: {1}", device, lastRecord.Value.ToLocalTime().ToString("g", Globalization.CultureInfo.CurrentCulture))
        End If
        Return Localization.TF("Ожидание «{0}» через GD..", device)
    End Function

    Friend Shared Function OpenedViaDrive(name As String) As String
        Return Localization.TF("Папка «{0}» открыта через GD: ответы приходят с задержкой до двух минут.", name)
    End Function

    ''' <summary>The sentence of a refusal of this channel (<see cref="DriveFailures"/>). A reason this build does not know reads as the
    ''' producer's general refusal, never as the token itself.</summary>
    Friend Shared Function DriveFailure(reason As String) As String
        Select Case reason
            Case ExchangeAgentPipe.ErrorNoAnswer : Return Localization.T("Устройство не ответило через GD.")
            Case ExchangeDriveRequests.ReasonNotPermitted : Return Localization.T("Устройство отказало: этот путь вне опубликованной папки (ответ через GD).")
            Case ExchangeDriveRequests.ReasonPinRequired : Return Localization.T("Папка защищена PIN-кодом, через GD она не открывается.")
            Case ExchangeDriveRequests.ReasonTooLarge : Return Localization.T("Файл или список слишком велик для передачи через GD.")
            Case ExchangeDriveRequests.ReasonDriveFull : Return Localization.T("На Google Drive закончилось место, запрос через GD не отправлен.")
            Case ExchangeAgentPipe.ErrorDriveOffline : Return Localization.T("Нет связи с Google Drive: войдите в Share Manager или проверьте сеть.")
            Case ExchangeAgentPipe.ErrorUnknownResource : Return Localization.T("Другое устройство не публикует эту папку через GD.")
            Case ExchangeAgentPipe.ErrorCorrupt : Return Localization.T("Ответ через GD пришёл повреждённым.")
            Case ExchangeAgentPipe.ErrorBusy : Return Localization.T("Слишком много запросов через GD, повторите чуть позже.")
            Case DriveFailures.ReasonAgentAbsent : Return Localization.T("Share Manager не запущен, поэтому папки ваших устройств не видны.")
            Case Else : Return Localization.T("Этот файл или папка сейчас недоступны на устройстве (ответ через GD).")
        End Select
    End Function

    Friend Shared Function DriveAnswered() As String
        Return Localization.T("Устройство ответило через GD.")
    End Function

    Friend Shared Function DriveEditRefused() As String
        Return Localization.T("Папку, подключённую через GD, нельзя изменить - только удалить.")
    End Function

    Friend Shared Function DriveExportRefused() As String
        Return Localization.T("Папку, подключённую через GD, нельзя экспортировать: у неё нет адреса.")
    End Function

    ''' <summary>The word beside a row of the "via Google Drive" group. Unlike a row of a server, attaching needs no answer from the device:
    ''' nothing has to be fetched from it, so "offline" stays a remark and not a refusal.</summary>
    Friend Shared Function RowStateViaDrive(row As ExchangeDeviceRow) As String
        If row.State = ExchangeResourceState.SharePaused Then Return SharePaused()
        If row.PinProtected Then Return Localization.T("заблокировано: PIN-код через GD не передаётся")
        If Not row.Supported Then Return Localization.T("не поддерживается этой версией")
        If row.Attached Then
            If row.State = ExchangeResourceState.NoLongerPublished Then Return Localization.T("больше не опубликовано")
            If row.State = ExchangeResourceState.DeviceOffline Then Return Localization.T("недоступно: устройство не в сети")
            Return ""
        End If
        Return If(row.DeviceOnline, "", Localization.T("не в сети"))
    End Function

End Class
#End If
