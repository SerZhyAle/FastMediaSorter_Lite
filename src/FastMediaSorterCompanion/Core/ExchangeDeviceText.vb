Option Strict On

' Ticket 180 T-4: the words of the "Exchange account" group and its sign-in dialog, and the pure model that decides which rows exist.
'
' The model is separate from the words and from the window so the rule "hidden rows, not disabled ones" is a table that a test can
' read. A row that cannot be used in the current state is not drawn; a server-side reason is never shown raw - each one has its own
' sentence, an unknown one the generic.

''' <summary>Which rows of the group exist in a given state.</summary>
Friend NotInheritable Class ExchangeAccountGroupModel

    ''' <summary>The group itself exists only while the client gate is open (<see cref="ExchangeFeatures"/>); otherwise the window is what it was.</summary>
    Public ReadOnly Visible As Boolean
    Public ReadOnly ShowSignIn As Boolean
    ''' <summary>A changed certificate is a named stop; only the user's explicit act replaces the pin.</summary>
    Public ReadOnly ShowAcceptCertificate As Boolean
    Public ReadOnly ShowAddDevice As Boolean
    Public ReadOnly ShowDevices As Boolean
    ''' <summary>Revoke acts on the selected device; it exists wherever the list does.</summary>
    Public ReadOnly ShowRevoke As Boolean
    ''' <summary>226 rule 4: the password change speaks over the live control stream, so it exists wherever the account is on line.</summary>
    Public ReadOnly ShowChangePassword As Boolean
    Public ReadOnly ShowDisconnect As Boolean

    Private Sub New(visible As Boolean, signIn As Boolean, acceptCertificate As Boolean, addDevice As Boolean, devices As Boolean, revoke As Boolean, changePassword As Boolean, disconnect As Boolean)
        Me.Visible = visible
        ShowSignIn = signIn
        ShowAcceptCertificate = acceptCertificate
        ShowAddDevice = addDevice
        ShowDevices = devices
        ShowRevoke = revoke
        ShowChangePassword = changePassword
        ShowDisconnect = disconnect
    End Sub

    ''' <summary>The rows for a status. <paramref name="status"/> may be Nothing (no agent: nothing configured).</summary>
    Public Shared Function Build(status As ExchangeDeviceStatus, gateOpen As Boolean) As ExchangeAccountGroupModel
        If Not gateOpen Then Return New ExchangeAccountGroupModel(False, False, False, False, False, False, False, False)
        If status Is Nothing OrElse Not status.Configured Then
            Return New ExchangeAccountGroupModel(True, True, False, False, False, False, False, False)
        End If
        Select Case status.State
            Case ExchangeAgentState.Online
                Dim pairing As Boolean = status.HasFeature(ExchangeWire.FeaturePairing)
                Dim directory As Boolean = status.HasFeature(ExchangeWire.FeatureDirectory)
                Return New ExchangeAccountGroupModel(True, False, False, pairing, directory, directory, True, True)
            Case ExchangeAgentState.SignInAgain
                Return New ExchangeAccountGroupModel(True, True, False, False, False, False, False, True)
            Case ExchangeAgentState.PinMismatch
                Return New ExchangeAccountGroupModel(True, False, True, False, False, False, False, True)
            Case Else
                ' connecting, waiting to reconnect, update needed, a defect, stopped: the account can still be forgotten
                Return New ExchangeAccountGroupModel(True, False, False, False, False, False, False, True)
        End Select
    End Function

End Class

''' <summary>226 rule 4: the refusals and the success of a password change, one sentence each. The success names the
''' consequence the owner decided: this device stays signed in, the account's OTHER devices sign in again (224 rule 9).</summary>
Friend Module ExchangePasswordChangeText

    Friend Function Success() As String
        Return Localization.T("Пароль сменён. Этот компьютер продолжает работать, а другие устройства учётной записи должны войти заново.")
    End Function

    Friend Function Failure(refusal As ExchangeRefusal) As String
        Select Case refusal
            Case ExchangeRefusal.BadCredentials : Return Localization.T("Старый пароль не подошёл.")
            Case ExchangeRefusal.RateLimited : Return Localization.T("Слишком много попыток. Подождите минуту и повторите.")
            Case ExchangeRefusal.PasswordPolicy : Return ExchangeDeviceText.TableText(ConnectionDiagnosisMessages.PasswordPolicy)   ' item AT, the table's own words (item DA)
            Case Else : Return Localization.T("Сервер отказал в смене пароля.")
        End Select
    End Function

    Friend Function NoStream() As String
        Return Localization.T("Нет связи с сервером. Подключитесь и повторите.")
    End Function

End Module

Friend Module ExchangeDeviceText

    Friend Function GroupTitle() As String
        Return Localization.T("Учётная запись обмена")
    End Function

    ''' <summary>The words of one row of the common message table (DEVICE-EXCHANGE item CZ), in the language of the window. Item DA: the
    ''' text is shown as the table has it - never reworded, shortened or joined with a sentence of this program. A code the table does not
    ''' know reads as its generic refusal, never as the code itself; no Russian key is added for any of it (the table carries 13 languages).</summary>
    Friend Function TableText(message As ConnectionDiagnosisMessage) As String
        Dim locale As String = Localization.CurrentCode
        Dim text As String = ConnectionDiagnosisLocalization.GetMessageText(message.Code, locale)
        If text.Length = 0 OrElse String.Equals(text, message.Code, StringComparison.Ordinal) Then
            text = ConnectionDiagnosisLocalization.GetMessageText(ConnectionDiagnosisMessages.Refused.Code, locale)
        End If
        Return text
    End Function

    ''' <summary>The one line the group shows (and folded, its summary).</summary>
    Friend Function StateLine(status As ExchangeDeviceStatus) As String
        If status Is Nothing OrElse Not status.Configured Then Return Localization.T("Не подключено к серверу обмена.")
        Select Case status.State
            Case ExchangeAgentState.Connecting
                Return Localization.T("Подключение..")
            Case ExchangeAgentState.Online
                Return Localization.TF("На связи: {0} на сервере {1}.", status.Login, status.Server)
            Case ExchangeAgentState.Reconnecting
                Select Case status.LastReason
                    Case "rate-limited" : Return Localization.T("Сервер просит подождать, повтор позже..")
                    Case "capacity" : Return Localization.T("Сервер переполнен, повтор позже..")
                    Case Else : Return Localization.T("Связь с сервером потеряна, пробуем снова..")
                End Select
            Case ExchangeAgentState.SignInAgain
                Return Localization.T("Нужно войти снова.")
            Case ExchangeAgentState.PinMismatch
                Return Localization.T("Сертификат сервера изменился. Дальнейшие попытки остановлены.")
            Case ExchangeAgentState.UpdateNeeded
                ' `version-unsupported` (item CZ): the two sides speak different versions; the table asks for no update
                Return TableText(ConnectionDiagnosisMessages.VersionUnsupported)
            Case ExchangeAgentState.Defect
                Return Localization.T("Ошибка программы при связи с сервером. Подробности в журнале.")
            Case ExchangeAgentState.Stopped
                Return Localization.T("Связь с сервером остановлена.")
            Case Else
                Return Localization.T("Не подключено к серверу обмена.")
        End Select
    End Function

    ''' <summary>The second line: why, when there is a reason worth a sentence ("" otherwise).</summary>
    Friend Function StateDetail(status As ExchangeDeviceStatus) As String
        If status Is Nothing OrElse Not status.Configured Then Return ""
        Select Case status.State
            Case ExchangeAgentState.SignInAgain
                Select Case status.LastReason
                    Case "bad-credentials" : Return Localization.T("Сервер не принял вход этого устройства.")
                    Case "device-revoked" : Return Localization.T("Это устройство отозвано в учётной записи.")
                    Case "signed-out" : Return TableText(ConnectionDiagnosisMessages.SignedOut)   ' item AT/BR: the password was changed; the entry stays and asks for it
                    Case "no-token" : Return Localization.T("На этом ПК нет сохранённого токена.")
                    Case "bad-address" : Return Localization.T("Сохранённый адрес сервера не читается.")
                    Case Else : Return ""
                End Select
            Case ExchangeAgentState.PinMismatch
                Return If(status.LastReason = "untrusted",
                          Localization.T("Сертификат сервера не принят системой, а отпечаток не закреплён."),
                          Localization.T("Сертификат сервера не совпадает с принятым отпечатком."))
            Case Else
                Return ""
        End Select
    End Function

    ''' <summary>The row of the common message table (item CZ) a failed enrollment reads as, or Nothing when the table has none (a bad
    ''' input, a token that could not be kept, a certificate nobody trusts yet, a reply of no known shape: this program's own sentences).
    ''' A refusal reads as the row of its own reason; the wrong-secret row is the one-time-code variant when a pairing code was typed
    ''' (<paramref name="oneTimeCode"/>); an unknown reason is the generic refusal.</summary>
    Friend Function EnrollRow(result As ExchangeEnrollResult, Optional oneTimeCode As Boolean = False) As ConnectionDiagnosisMessage
        Select Case result.Outcome
            Case ExchangeEnrollOutcome.Refused
                Return ConnectionDiagnosisMessages.OfWire(ExchangeRefusals.WireName(result.Refusal), oneTimeCode)
            Case ExchangeEnrollOutcome.PinMismatch
                Return ConnectionDiagnosisMessages.CertificateChanged
            Case ExchangeEnrollOutcome.TlsFailed
                Return ConnectionDiagnosisMessages.TlsFailed
            Case ExchangeEnrollOutcome.Network
                Return ConnectionDiagnosisMessages.UnreachableServer
            Case ExchangeEnrollOutcome.TimedOut
                Return ConnectionDiagnosisMessages.Unanswered   ' the server did not answer; `timeout` is the CAST row of the other device
            Case Else
                Return Nothing
        End Select
    End Function

    ''' <summary>The words for the outcome of an enrollment ("" for success). Never the raw reason. Where the table of item CZ has a row
    ''' for the failure, the row's own text is shown and nothing of this program's before it (item DA); the rest are this program's sentences.</summary>
    Friend Function EnrollFailure(result As ExchangeEnrollResult, Optional oneTimeCode As Boolean = False) As String
        Dim row As ConnectionDiagnosisMessage = EnrollRow(result, oneTimeCode)
        If row IsNot Nothing Then Return TableText(row)
        Select Case result.Outcome
            Case ExchangeEnrollOutcome.Enrolled
                Return ""
            Case ExchangeEnrollOutcome.Untrusted
                Return Localization.T("Сертификат сервера не принят. Проверьте его и подтвердите отпечаток.") & " " &
                       Localization.T("Подключение без проверки сертификата не предусмотрено.")
            Case ExchangeEnrollOutcome.BadInput
                Return Localization.T("Проверьте адрес (хост:порт), логин и пароль или код.")
            Case ExchangeEnrollOutcome.StoreFailed
                Return Localization.T("Вход выполнен, но токен не удалось сохранить на этом ПК. Повторите вход.")
            Case ExchangeEnrollOutcome.Cancelled
                Return ""
            Case Else
                Return Localization.T("Сервер ответил не так, как ожидалось.")
        End Select
    End Function

    ''' <summary>The outcome of a pairing-code request ("" for success).</summary>
    Friend Function PairingFailure(result As ExchangePairingResult) As String
        If result.Succeeded Then Return ""
        If Not result.Answered Then Return Localization.T("Не удалось получить код: нет связи с сервером.")
        Select Case result.Refusal
            Case ExchangeRefusal.RateLimited : Return Localization.T("Слишком много попыток. Подождите немного и повторите.")
            Case ExchangeRefusal.Capacity : Return Localization.T("Сервер переполнен. Повторите позже.")
            Case Else : Return Localization.T("Сервер отклонил запрос.")
        End Select
    End Function

    Friend Function NoConnectionForAction() As String
        Return Localization.T("Не удалось выполнить действие: нет связи с сервером.")
    End Function

    ''' <summary>The device's name, marked when it is this one.</summary>
    Friend Function DeviceName(name As String, isThisDevice As Boolean) As String
        Return If(isThisDevice, Localization.TF("{0} (это устройство)", name), name)
    End Function

    Friend Function PresenceText(online As Boolean) As String
        Return If(online, Localization.T("в сети"), Localization.T("не в сети"))
    End Function

    ''' <summary>The pairing code line and the minutes:seconds still left, as short culture-neutral text.</summary>
    Friend Function CodeLine(code As String) As String
        Return Localization.TF("Код для другого устройства: {0}", code)
    End Function

    Friend Function ValidFor(remaining As TimeSpan) As String
        Dim total As Integer = CInt(Math.Max(0, Math.Floor(remaining.TotalSeconds)))
        Dim text As String = (total \ 60).ToString(Globalization.CultureInfo.InvariantCulture) & ":" & (total Mod 60).ToString("00", Globalization.CultureInfo.InvariantCulture)
        Return Localization.TF("Действует ещё {0}.", text)
    End Function

    ' --- a welcome that names another address (the entries proposal's ask 2) -----------------------------------

    ''' <summary>The address the server announced, shown beside the entry's own: the user decides, nothing switches silently.</summary>
    Friend Function MovedAddress(address As String) As String
        Return Localization.TF("Сервер сообщил новый адрес: {0}", address)
    End Function

    ''' <summary>The edit keeps the device, the sign-in key, the remembered certificate and the published shares.</summary>
    Friend Function UpdateAddressQuestion(oldAddress As String, newAddress As String) As String
        Return Localization.TF("Сервер обмена сменил адрес.{0}Был: {1}{0}Стал: {2}{0}{0}Обновить адрес записи? Устройство, ключ входа, отпечаток и опубликованные папки сохраняются.",
                               vbLf, oldAddress, newAddress)
    End Function

    Friend Function AddressUpdated() As String
        Return Localization.T("Адрес сервера обновлён.")
    End Function

End Module
