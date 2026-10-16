#If Not NETFRAMEWORK Then
Option Strict On

' Ticket 187 T-4: the words of the viewer's own exchange entries - the "Add from an exchange server.." dialog, the per-entry groups of the
' directory view, signing out. One sentence per state and per refusal. The product name appears only where it is the NAME of another program
' (Share Manager), a proper name that is not translated. A server, a login and a fingerprint are shown to the person who typed them and are
' never written to a log. House style: ".." for an ellipsis, a plain hyphen, ё in Russian. The tables: Localization.ExchangeEntries.vb.

Friend NotInheritable Class ExchangeEntryText
    Private Sub New()
    End Sub

    ' ---- the button and the dialog ---------------------------------------------------------------------------

    Friend Shared Function AddButton() As String
        Return Localization.T("Добавить с сервера обмена..")
    End Function

    Friend Shared Function ConnectTitle() As String
        Return Localization.T("Добавить с сервера обмена")
    End Function

    Friend Shared Function ConnectIntro() As String
        Return Localization.T("Вставьте строку подключения, которую передал оператор сервера, или введите адрес сервера (хост:порт). Затем логин и пароль учётной записи: в списке появятся все её папки - на других компьютерах и на этом.")
    End Function

    Friend Shared Function InputLabel() As String
        Return Localization.T("Строка подключения или адрес сервера:")
    End Function

    Friend Shared Function PasteButton() As String
        Return Localization.T("Вставить")
    End Function

    Friend Shared Function OpenFileButton() As String
        Return Localization.T("Открыть файл..")
    End Function

    Friend Shared Function OpenFileFilter() As String
        Return Localization.T("Строка подключения (*.fmsexch)|*.fmsexch|Все файлы (*.*)|*.*")
    End Function

    ''' <summary>Rule 13 of the grammar proposal: the destination of the password, shown before the password field takes focus.</summary>
    Friend Shared Function Destination(hostPort As String) As String
        Return Localization.TF("Пароль будет отправлен на: {0}", hostPort)
    End Function

    ''' <summary>The `n` of the string: a hint written by whoever made it, never the title of the window and never in place of the address.</summary>
    Friend Shared Function ServerLabel(name As String) As String
        Return Localization.TF("Название в строке (не проверено): {0}", name)
    End Function

    Friend Shared Function LoginLabel() As String
        Return Localization.T("Логин:")
    End Function

    Friend Shared Function PasswordLabel() As String
        Return Localization.T("Пароль:")
    End Function

    Friend Shared Function ConnectButton() As String
        Return Localization.T("Подключиться")
    End Function

    ' ---- registration with an invitation (226 rule 1) -----------------------------------------------------------

    Friend Shared Function RegisterIntro() As String
        Return Localization.T("Строка - приглашение: в ней уже назван сервер. Придумайте логин и пароль учётной записи - программа создаст её на сервере и запомнит вход на этом компьютере.")
    End Function

    Friend Shared Function RegisterButton() As String
        Return Localization.T("Создать учётную запись")
    End Function

    ''' <summary>The new password is typed twice; the label of the second field.</summary>
    Friend Shared Function PasswordTwiceLabel() As String
        Return Localization.T("Пароль ещё раз:")
    End Function

    ''' <summary>The words of one row of the common message table (item CZ) in the current language, as the table has them (item DA).
    ''' A code the table does not know reads as its generic refusal.</summary>
    Friend Shared Function TableText(message As ConnectionDiagnosisMessage) As String
        Return ExchangeResourceText.SealedText(message)
    End Function

    ''' <summary>The server's password policy in words, shown before the first refusal.</summary>
    Friend Shared Function PasswordPolicyNote() As String
        Return Localization.T("Пароль: не короче 10 символов, с заглавной латинской буквой, со строчной латинской буквой и с цифрой.")
    End Function

    ''' <summary>The policy's own sentence of a password that does not pass (verdict 0-3 of ExchangePasswordVerdict).</summary>
    Friend Shared Function PasswordPolicyFailure(verdict As Integer) As String
        Select Case verdict
            Case 0 : Return Localization.T("Пароль короче 10 символов.")
            Case 1 : Return Localization.T("В пароле нет заглавной латинской буквы.")
            Case 2 : Return Localization.T("В пароле нет строчной латинской буквы.")
            Case Else : Return Localization.T("В пароле нет цифры.")
        End Select
    End Function

    Friend Shared Function PasswordsDiffer() As String
        Return Localization.T("Пароли в двух полях не совпали.")
    End Function

    ''' <summary>The refusals of a registration, one sentence each - a used code and a wrong code say the same (224 rule 6).</summary>
    Friend Shared Function RegisterFailure(outcome As ExchangeConsumerEnrollOutcome, refusal As ExchangeRefusal) As String
        Select Case outcome
            Case ExchangeConsumerEnrollOutcome.BadInput
                Return Localization.T("Укажите логин и пароль.")
            Case ExchangeConsumerEnrollOutcome.Refused
                ' each refusal in the words of its own row of the common message table (items CZ, DA: `invitation-invalid`, `login-taken`,
                ' `password-policy`, `version-unsupported` ..). `bad-credentials` is what a server older than the amendments of item AQ
                ' answered to every registration refusal: it keeps the one sentence that covers its three causes.
                If refusal = ExchangeRefusal.BadCredentials Then Return Localization.T("Приглашение не подошло, уже использовано, или такой логин уже занят.")
                Return TableText(ConnectionDiagnosisMessages.OfWire(ExchangeRefusals.WireName(refusal)))
            Case Else
                Return EnrollFailure(outcome, refusal)
        End Select
    End Function

    Friend Shared Function Connecting() As String
        Return Localization.T("Подключаюсь..")
    End Function

    Friend Shared Function Added(name As String) As String
        Return Localization.TF("Сервер «{0}» добавлен. Отметьте папки, которые нужны в списке ресурсов.", name)
    End Function

    ' ---- what a refused input is not --------------------------------------------------------------------------

    Friend Shared Function InputRefusal(reason As String, looks As String) As String
        Select Case reason
            Case ExchangeConnectionStringReason.NewerMajor
                Return Localization.T("Эту строку сделала более новая версия. Обновите программу.")
            Case ExchangeConnectionStringReason.NotAString
                If looks = ExchangeConnectionStringReason.LooksShareCode Then
                    Return Localization.T("Это код общей папки, а не строка подключения к серверу обмена. Код добавляют кнопкой «Добавить..» в ресурсах.")
                End If
                If looks = ExchangeConnectionStringReason.LooksUrl Then
                    Return Localization.T("Это веб-адрес, а не строка подключения. Строка подключения начинается с FMSEXCH1:")
                End If
                Return Localization.T("Это не строка подключения к серверу обмена и не адрес сервера.")
            Case ExchangeConnectionStringReason.BadX
                Return Localization.T("В строке неверный адрес сервера.")
            Case ExchangeConnectionStringReason.BadFingerprint
                Return Localization.T("В строке неверный отпечаток сертификата.")
            Case ExchangeConnectionStringReason.FileTooLong, ExchangeConnectionStringReason.FileLines
                Return Localization.T("Файл не похож на строку подключения: в нём должна быть одна короткая строка.")
            Case Else
                Return Localization.T("Строка повреждена или скопирована не целиком. Скопируйте её ещё раз.")
        End Select
    End Function

    ' ---- the trust rule --------------------------------------------------------------------------------------

    ''' <summary>A leaf that is not the one the string pinned: both fingerprints, nothing pinned, no password sent.</summary>
    Friend Shared Function PinMismatch(hostPort As String, expected As String, seen As String) As String
        Return Localization.TF("Сертификат сервера {0} не тот, что назван в строке подключения.{1}Ожидался: {2}{1}Предъявлен: {3}{1}{1}Пароль не отправлялся и ничего не запомнено. Возможно, строку дал не тот человек или на этом адресе отвечает чужой сервер.",
                               hostPort, vbLf, expected, seen)
    End Function

    ''' <summary>A leaf the platform does not accept and no string pinned: the fingerprint is shown and the answer starts at No.</summary>
    Friend Shared Function TrustQuestion(hostPort As String, fingerprint As String) As String
        Return Localization.TF("Сертификат сервера обмена {0} программе незнаком.{1}Его отпечаток:{1}{2}{1}{1}Сверьте его с тем, что назвал оператор сервера. Доверять этому серверу и запомнить его?",
                               hostPort, vbLf, fingerprint)
    End Function

    Friend Shared Function TrustTitle() As String
        Return Localization.T("Новый сервер обмена")
    End Function

    ' ---- the answer of the server ----------------------------------------------------------------------------

    Friend Shared Function EnrollFailure(outcome As ExchangeConsumerEnrollOutcome, refusal As ExchangeRefusal) As String
        Select Case outcome
            Case ExchangeConsumerEnrollOutcome.BadInput
                Return Localization.T("Укажите адрес сервера, логин и пароль.")
            Case ExchangeConsumerEnrollOutcome.Untrusted
                Return Localization.T("Сертификат сервера не подтверждён, поэтому ничего не отправлено.")
            Case ExchangeConsumerEnrollOutcome.Refused
                Select Case refusal
                    Case ExchangeRefusal.BadCredentials : Return Localization.T("Логин или пароль не подошли.")
                    Case ExchangeRefusal.VersionUnsupported : Return TableText(ConnectionDiagnosisMessages.VersionUnsupported)   ' item CZ: no "update" demand
                    Case ExchangeRefusal.RateLimited : Return Localization.T("Слишком много попыток входа. Подождите минуту и повторите.")
                    Case ExchangeRefusal.Capacity : Return Localization.T("Сервер сейчас не принимает новые устройства.")
                    Case ExchangeRefusal.IdCollision : Return Localization.T("Сервер уже знает это устройство в другой учётной записи. Повторите попытку.")
                    Case Else : Return Localization.T("Сервер отказал во входе.")
                End Select
            Case ExchangeConsumerEnrollOutcome.TlsFailed
                Return Localization.T("По этому адресу нет сервера обмена, или он не использует защищённое соединение.")
            Case ExchangeConsumerEnrollOutcome.Network
                Return Localization.T("Сервер не отвечает. Проверьте адрес и сеть.")
            Case ExchangeConsumerEnrollOutcome.TimedOut
                Return Localization.T("Сервер не ответил вовремя.")
            Case ExchangeConsumerEnrollOutcome.StoreFailed
                Return Localization.T("Вход выполнен, но ключ входа не удалось сохранить на этом компьютере. Повторите попытку.")
            Case ExchangeConsumerEnrollOutcome.TooMany
                Return Localization.TF("Серверов уже {0}: удалите ненужные и повторите.", ExchangeEntryStore.MaxEntries)
            Case Else
                Return Localization.T("Сервер ответил непонятно. Повторите попытку позже.")
        End Select
    End Function

    ' ---- the directory view, one group per entry -------------------------------------------------------------

    Friend Shared Function NoServerYet() As String
        Return Localization.T("Сервер обмена ещё не добавлен. Нажмите «Добавить с сервера обмена..»: адрес, логин и пароль - и здесь появятся все папки вашей учётной записи.")
    End Function

    Friend Shared Function EntryHeader(displayName As String, login As String, state As String) As String
        Return Localization.TF("{0} ({1}) - {2}", displayName, login, state)
    End Function

    Friend Shared Function StateText(state As ExchangeAgentState) As String
        Select Case state
            Case ExchangeAgentState.Online : Return Localization.T("на связи")
            Case ExchangeAgentState.Connecting, ExchangeAgentState.Reconnecting : Return Localization.T("соединяюсь..")
            Case ExchangeAgentState.SignInAgain : Return Localization.T("нужно войти заново")
            Case ExchangeAgentState.PinMismatch : Return Localization.T("сертификат сервера изменился")
            Case ExchangeAgentState.UpdateNeeded : Return TableText(ConnectionDiagnosisMessages.VersionUnsupported)   ' item CZ: different versions, no "update" demand
            Case Else : Return Localization.T("нет связи")
        End Select
    End Function

    Friend Shared Function SignOutButton() As String
        Return Localization.T("Выйти из этого сервера")
    End Function

    Friend Shared Function SignInAgainButton() As String
        Return Localization.T("Войти заново..")
    End Function

    Friend Shared Function AttachAllButton() As String
        Return Localization.T("Подключить всё, что в списке")
    End Function

    Friend Shared Function AutoAttachCheckbox() As String
        Return Localization.T("Подключать новые папки этой учётной записи сами")
    End Function

    Friend Shared Function AutoAttachNote() As String
        Return Localization.T("Выключено: новая папка появляется в списке с пустой отметкой, и вы решаете сами. Включено: каждая новая папка этой учётной записи попадает в ресурсы без вопроса.")
    End Function

    Friend Shared Function SignOutQuestion(displayName As String, login As String) As String
        Return Localization.TF("Выйти из сервера обмена «{0}» ({1})?{2}{2}Подключённые ресурсы останутся в списке, но не откроются, пока вы не войдёте снова. Ключ входа и запомненный отпечаток на этом компьютере будут удалены.",
                               displayName, login, vbLf)
    End Function

    Friend Shared Function SignedOut(displayName As String) As String
        Return Localization.TF("Вы вышли из сервера «{0}». Подключённые ресурсы остались в списке.", displayName)
    End Function

    Friend Shared Function AttachedCount(count As Integer) As String
        Return Localization.TF("Подключено папок: {0}.", count)
    End Function

    ''' <summary>The state text of an attached resource whose entry is gone or has no token (the registry keeps it).</summary>
    Friend Shared Function ResourceNeedsSignIn() As String
        Return Localization.T("нужно войти заново")
    End Function

    ' ---- a welcome that names another address (the entries proposal's ask 2) ----------------------------------

    ''' <summary>The address the server announced, shown under the entry's own header: the user decides, nothing switches silently.</summary>
    Friend Shared Function AnnouncedAddress(address As String) As String
        Return Localization.TF("Сервер сообщил новый адрес: {0}", address)
    End Function

    Friend Shared Function UpdateAddressButton() As String
        Return Localization.T("Обновить адрес")
    End Function

    ''' <summary>The edit keeps the device, the sign-in key, the remembered certificate and every attached folder.</summary>
    Friend Shared Function UpdateAddressQuestion(oldAddress As String, newAddress As String) As String
        Return Localization.TF("Сервер обмена сменил адрес.{0}Был: {1}{0}Стал: {2}{0}{0}Обновить адрес записи? Устройство, ключ входа, отпечаток и подключённые папки сохраняются.",
                               vbLf, oldAddress, newAddress)
    End Function

    Friend Shared Function AddressUpdated(displayName As String) As String
        Return Localization.TF("Адрес сервера «{0}» обновлён.", displayName)
    End Function

    ''' <summary>The edit would make a twin of another entry: refused, nothing changed.</summary>
    Friend Shared Function AddressAlreadyAdded() As String
        Return Localization.T("Этот адрес с этим логином уже добавлен как другой сервер в списке.")
    End Function

    ' ---- handing a server on from an entry (ask 7) ------------------------------------------------------------

    Friend Shared Function HandOnButton() As String
        Return Localization.T("Скопировать строку подключения")
    End Function

    Friend Shared Function HandOnCopied() As String
        Return Localization.T("Строка подключения скопирована. Передайте её тому, кто должен добавить этот сервер.")
    End Function

    ''' <summary>No announced address means nothing to hand on - the entry's own address is never a guess (ask 7).</summary>
    Friend Shared Function HandOnNoEndpoint() As String
        Return Localization.T("Строка недоступна: сервер не сообщил адрес для клиентов.")
    End Function

    Friend Shared Function HandOnCopyFailed() As String
        Return Localization.T("Не удалось скопировать строку в буфер обмена.")
    End Function

    ' ---- changing the account's password (226 rule 4) ----------------------------------------------------------

    Friend Shared Function ChangePasswordButton() As String
        Return Localization.T("Сменить пароль..")
    End Function

    Friend Shared Function ChangePasswordTitle(accountName As String) As String
        Return Localization.TF("Смена пароля: {0}", accountName)
    End Function

    Friend Shared Function ChangePasswordOldLabel() As String
        Return Localization.T("Старый пароль:")
    End Function

    Friend Shared Function ChangePasswordNewLabel() As String
        Return Localization.T("Новый пароль:")
    End Function

    Friend Shared Function ChangePasswordRepeatLabel() As String
        Return Localization.T("Новый пароль ещё раз:")
    End Function

    Friend Shared Function ChangePasswordNeedOld() As String
        Return Localization.T("Укажите старый пароль.")
    End Function

    ''' <summary>The success sentence: this device keeps working, the account's OTHER devices must sign in again.</summary>
    Friend Shared Function PasswordChanged() As String
        Return Localization.T("Пароль сменён. Этот компьютер продолжает работать, а другие устройства учётной записи должны войти заново.")
    End Function

    ''' <summary>The refusals of a password change, one sentence each.</summary>
    Friend Shared Function ChangePasswordFailure(refusal As ExchangeRefusal) As String
        Select Case refusal
            Case ExchangeRefusal.BadCredentials : Return Localization.T("Старый пароль не подошёл.")
            Case ExchangeRefusal.RateLimited : Return Localization.T("Слишком много попыток. Подождите минуту и повторите.")
            Case Else : Return Localization.T("Сервер отказал в смене пароля.")
        End Select
    End Function

    Friend Shared Function ChangePasswordNoStream() As String
        Return Localization.T("Нет связи с сервером. Подключитесь и повторите.")
    End Function

End Class
#End If
