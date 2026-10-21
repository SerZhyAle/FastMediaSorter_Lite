Option Strict On

' Ticket 187 T-5: the words of the share service's quick join by connection string and its clean leave. One sentence per refusal and per
' outcome; the product name appears only through ProductNames. A server, a login and a fingerprint are shown to the person who typed them and
' are never written to a log. House style: ".." for an ellipsis, a plain hyphen, ё in Russian. The tables: Localization.ExchangeJoin.vb.

Friend NotInheritable Class ExchangeJoinText
    Private Sub New()
    End Sub

    Friend Shared Function StringLabel() As String
        Return Localization.T("Строка подключения (необязательно):")
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

    ''' <summary>The string was read: the destination of the password first (rule 13 of the grammar proposal), never the string's own name.</summary>
    Friend Shared Function StringRead(hostPort As String) As String
        Return Localization.TF("Из строки: сервер {0}. Укажите логин и пароль.", hostPort)
    End Function

    Friend Shared Function PinNote() As String
        Return Localization.T("Отпечаток сертификата взят из строки: он и есть доверенный, сравнивать его вручную не нужно.")
    End Function

    Friend Shared Function PublishCheckbox() As String
        Return Localization.T("Сразу публиковать мои общие папки на этом сервере")
    End Function

    Friend Shared Function PublishButton() As String
        Return Localization.T("Опубликовать через сервер обмена..")
    End Function

    Friend Shared Function ReplaceQuestion(oldServer As String, oldLogin As String, newServer As String) As String
        Return Localization.TF("Это устройство уже подключено к серверу обмена {0} как {1}. Быть можно только на одном сервере сразу: покинуть его и войти на {2}?{3}{3}Публикация общих папок на старом сервере прекратится, а сами папки, их пароль и QR-коды не изменятся.",
                               oldServer, oldLogin, newServer, vbLf)
    End Function

    Friend Shared Function DisconnectQuestion() As String
        Return Localization.TF("Покинуть сервер обмена на этом ПК?{0}{0}Публикация общих папок на нём прекратится, это устройство будет удалено из списка устройств учётной записи, а токен и настройки сотрутся. Сами общие папки, их пароль и QR-коды не меняются.", vbLf)
    End Function

    ''' <summary>226 rule 1: the policy's own sentence of a password that does not pass (the registration's immediate feedback,
    ''' beside the field and before anything is sent; the server still has the last word).</summary>
    Friend Shared Function PolicyFailure(verdict As ExchangePasswordPolicy.Verdict) As String
        Select Case verdict
            Case ExchangePasswordPolicy.Verdict.TooShort : Return Localization.T("Пароль короче 10 символов.")
            Case ExchangePasswordPolicy.Verdict.NoUpper : Return Localization.T("В пароле нет заглавной латинской буквы.")
            Case ExchangePasswordPolicy.Verdict.NoLower : Return Localization.T("В пароле нет строчной латинской буквы.")
            Case Else : Return Localization.T("В пароле нет цифры.")
        End Select
    End Function

    ''' <summary>226 rule 1: the refusals of a registration, each in the words of its own row of the common message table (item CZ, shown
    ''' as the table has it, item DA): `invitation-invalid` says the same for a code nobody made, a used one and a wrong shared password
    ''' (224 rule 6, no oracle), `login-taken` and `password-policy` have their own rows (the invitation is not consumed by them, item AQ).
    ''' `bad-credentials` is what a server older than the amendments of item AQ answered to every registration refusal: it keeps the one
    ''' sentence that covers all three of its causes. An unknown reason is the table's generic refusal.</summary>
    Friend Shared Function RegisterFailure(result As ExchangeEnrollResult) As String
        If result.Outcome = ExchangeEnrollOutcome.Refused Then
            If result.Refusal = ExchangeRefusal.BadCredentials Then Return Localization.T("Приглашение не подошло, уже использовано, или такой логин уже занят.")
            Return ExchangeDeviceText.TableText(ConnectionDiagnosisMessages.OfWire(ExchangeRefusals.WireName(result.Refusal)))
        End If
        Return ExchangeDeviceText.EnrollFailure(result)
    End Function

    Friend Shared Function LeftCleanly() As String
        Return Localization.T("Это устройство покинуло сервер обмена и удалено из списка учётной записи.")
    End Function

    ''' <summary>The server could not be told: forgotten here, and the old record stays until another device removes it (never retried in the background).</summary>
    Friend Shared Function LeftLocally() As String
        Return Localization.T("На этом ПК учётная запись забыта, но сервер не ответил: запись об устройстве останется в списке учётной записи, пока другое устройство её не отзовёт.")
    End Function

    Friend Shared Function InputRefusal(reason As String, looks As String) As String
        Select Case reason
            Case ExchangeConnectionStringReason.NewerMajor
                ' DEVICE-EXCHANGE 1.3 amendment of 2026-10-17: a higher FMSEXCH prefix is the local row `format-unsupported`, in the
                ' words of the one table (the viewer reads the same row), and it demands no update.
                Return ConnectionDiagnosisLocalization.FormatUnsupportedText()
            Case ExchangeConnectionStringReason.NotAString
                If looks = ExchangeConnectionStringReason.LooksShareCode Then
                    Return Localization.T("Это код общей папки, а не строка подключения к серверу обмена. Код добавляют кнопкой «Добавить..» в ресурсах.")
                End If
                If looks = ExchangeConnectionStringReason.LooksUrl Then
                    Return Localization.T("Это веб-адрес, а не строка подключения. Строка подключения начинается с FMSEXCH1:")
                End If
                Return Localization.T("Это не строка подключения к серверу обмена и не адрес сервера.")
            Case Else
                Return Localization.T("Строка повреждена или скопирована не целиком. Скопируйте её ещё раз.")
        End Select
    End Function

    ''' <summary>A leaf that is not the one the string named: both fingerprints, nothing pinned, no password sent.</summary>
    Friend Shared Function PinMismatch(expected As String, seen As String) As String
        Return Localization.TF("Сертификат сервера не тот, что назван в строке подключения.{0}Ожидался: {1}{0}Предъявлен: {2}{0}{0}Пароль не отправлялся и ничего не запомнено.",
                               vbLf, expected, seen)
    End Function

    Friend Shared Function JoinLocalButton() As String
        Return Localization.T("Объявить этот ПК на сервере")
    End Function

    Friend Shared Function JoinLocalDescription() As String
        Return Localization.T("Подключает этот компьютер к локальному серверу обмена и публикует общие папки в один клик без ввода паролей и портов.")
    End Function

    Friend Shared Function JoinLocalAccountsTitle() As String
        Return Localization.T("Выберите учётную запись")
    End Function

    Friend Shared Function JoinLocalAccountsIntro() As String
        Return Localization.T("На локальном сервере найдено несколько учётных записей. Выберите, к какой из них подключить этот компьютер:")
    End Function

    Friend Shared Function DevicesSelectAccountHint() As String
        Return Localization.T("Выберите учётную запись в списке выше для просмотра подключённых устройств.")
    End Function

    Friend Shared Function LocalJoinOutcomeText(outcome As ExchangeLocalJoinOutcome) As String
        Select Case outcome
            Case ExchangeLocalJoinOutcome.Joined
                Return Localization.T("Этот компьютер подключён к серверу обмена, общие папки опубликованы.")
            Case ExchangeLocalJoinOutcome.AlreadyJoined
                Return Localization.T("Этот компьютер уже подключён к локальному серверу обмена, публикация включена.")
            Case ExchangeLocalJoinOutcome.NoAccounts
                Return Localization.T("На локальном сервере нет учётных записей. Сначала создайте учётную запись.")
            Case ExchangeLocalJoinOutcome.ServerNotRunning
                Return Localization.T("Локальный сервер обмена не запущен. Запустите сервер для подключения.")
            Case ExchangeLocalJoinOutcome.CertificateMismatch
                Return Localization.T("Сертификат запущенного сервера не совпадает с локальной конфигурацией.")
            Case ExchangeLocalJoinOutcome.CodeRefused
                Return Localization.T("Локальный сервер не выдал код сопряжения.")
            Case ExchangeLocalJoinOutcome.EnrollFailed
                Return Localization.T("Не удалось зарегистрировать устройство на сервере.")
            Case Else
                Return Localization.T("Не удалось подключиться к локальному серверу.")
        End Select
    End Function
End Class
