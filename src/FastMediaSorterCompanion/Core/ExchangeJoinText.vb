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
                Return Localization.T("Эту строку сделала более новая версия. Обновите программу.")
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
End Class
