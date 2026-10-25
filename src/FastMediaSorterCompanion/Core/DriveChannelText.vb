Option Strict On

Imports System.Net

''' <summary>
''' Every sentence of the Google Drive sign-in page (specification 203 section 5.9, ticket T-UI-1), composed from a state, a
''' failure kind or an outcome - never from an exception: no exception text, no HTTP status, no client id and no token ever
''' reaches the person (the <c>NoExceptionTextReachesTheUser</c> gate). Pure: it reads nothing and starts nothing.
''' "Google Drive" is a product name and stays Latin in every language.
''' </summary>
Friend NotInheritable Class DriveChannelText

    Private Sub New()
    End Sub

    ''' <summary>The page's name in the rail and its title. A product name: it is not translated.</summary>
    Public Shared Function PageTitle() As String
        Return "Google Drive"
    End Function

    Public Shared Function PageDescription() As String
        Return Localization.T("Вход в Google Drive для связи этого ПК с вашими устройствами.")
    End Function

    Public Shared Function AccountGroupTitle() As String
        Return Localization.T("Вход")
    End Function

    Public Shared Function DeviceGroupTitle() As String
        Return Localization.T("Этот ПК и данные")
    End Function

    Public Shared Function SignInButton() As String
        Return Localization.T("Войти..")
    End Function

    Public Shared Function CancelButton() As String
        Return Localization.T("Отмена")
    End Function

    Public Shared Function SignOutButton() As String
        Return Localization.T("Выйти на этом устройстве")
    End Function

    ''' <summary>The bold line: what the channel is doing now.</summary>
    Public Shared Function StateLine(state As DriveChannelState) As String
        Select Case state
            Case DriveChannelState.NotConfigured : Return Localization.T("Вход в Google Drive не настроен в этой сборке")
            Case DriveChannelState.SigningIn : Return Localization.T("Ожидание входа в браузере..")
            Case DriveChannelState.SignedIn : Return Localization.T("Вход в Google Drive выполнен")
            Case DriveChannelState.Unavailable : Return Localization.T("Google Drive сейчас недоступен")
            Case DriveChannelState.SigningOut : Return Localization.T("Выход из Google Drive..")
            Case Else : Return Localization.T("Вход в Google Drive не выполнен")
        End Select
    End Function

    ''' <summary>The muted line under it: the reason, or what to do. "" when there is nothing to add.</summary>
    Public Shared Function StateDetail(state As DriveChannelState, failure As ExchangeDriveFailure) As String
        Select Case state
            Case DriveChannelState.NotConfigured
                Return Localization.T("Эта сборка не содержит ключа доступа к Google Drive, поэтому вход недоступен. Остальные функции менеджера работают как обычно.")
            Case DriveChannelState.SignedOut
                Return If(failure = ExchangeDriveFailure.SignedOut,
                          Localization.T("Вход больше не действует: доступ отозван или истёк. Войдите снова."),
                          Localization.T("Войдите в свой Google Drive, чтобы связать этот ПК с вашими устройствами."))
            Case DriveChannelState.SigningIn
                Return Localization.T("Завершите вход в открывшемся окне браузера. Чтобы отказаться, нажмите «Отмена».")
            Case DriveChannelState.SignedIn
                Return Localization.T("Этот ПК связан с вашим Google Drive.")
            Case DriveChannelState.Unavailable
                Return FailureDetail(failure)
            Case Else
                Return ""
        End Select
    End Function

    ''' <summary>The reason for "unavailable": a kind of failure in words.</summary>
    Public Shared Function FailureDetail(failure As ExchangeDriveFailure) As String
        Select Case failure
            Case ExchangeDriveFailure.Offline
                Return Localization.T("Нет связи с Google Drive. Повторная попытка выполняется автоматически.")
            Case ExchangeDriveFailure.DailyLimit
                Return Localization.T("Достигнут суточный лимит Google Drive на загрузку. Повторите завтра.")
            Case ExchangeDriveFailure.DriveFull
                Return Localization.T("На вашем Google Drive нет свободного места.")
            Case Else
                Return Localization.T("Google Drive отвечает ошибкой или ограничивает запросы. Повторная попытка выполняется автоматически.")
        End Select
    End Function

    ''' <summary>How a sign-in attempt ended, for the line under the buttons. "" when it succeeded (the state line says it).</summary>
    Public Shared Function SignInOutcome(outcome As ExchangeDriveSignInOutcome, cancelled As Boolean) As String
        Select Case outcome
            Case ExchangeDriveSignInOutcome.Signed
                Return ""
            Case ExchangeDriveSignInOutcome.NotConfigured
                Return StateDetail(DriveChannelState.NotConfigured, ExchangeDriveFailure.None)
            Case ExchangeDriveSignInOutcome.ConsentDenied
                Return Localization.T("Доступ не предоставлен. Вход отменён.")
            Case ExchangeDriveSignInOutcome.TimedOut
                Return If(cancelled, Localization.T("Вход отменён."), Localization.T("Вход не завершён вовремя. Попробуйте ещё раз."))
            Case ExchangeDriveSignInOutcome.NoBrowser
                Return Localization.T("Не удалось открыть браузер. Проверьте браузер по умолчанию и повторите вход.")
            Case ExchangeDriveSignInOutcome.ScopeMismatch
                Return Localization.T("Google предоставил не тот доступ, который запрошен, поэтому вход отклонён. Разрешите только доступ к данным самого приложения.")
            Case ExchangeDriveSignInOutcome.StorageFailed
                Return Localization.T("Не удалось сохранить вход на этом ПК.")
            Case ExchangeDriveSignInOutcome.Offline
                Return Localization.T("Нет связи с Google. Проверьте подключение и повторите вход.")
            Case Else
                Return Localization.T("Не удалось завершить вход в Google. Повторите попытку.")
        End Select
    End Function

    ''' <summary>What the sign-out removed and what Google confirmed. "" when there is no result.</summary>
    Public Shared Function SignOutOutcome(result As ExchangeDriveSignOutResult) As String
        If result Is Nothing Then Return ""
        If Not result.RevocationConfirmed Then
            Return Localization.T("Вы вышли из Google Drive на этом ПК, но подтвердить отзыв доступа у Google не удалось. Отзовите его вручную в настройках аккаунта Google.")
        End If
        If Not result.RecordsDeleted Then
            Return Localization.T("Вы вышли из Google Drive на этом ПК. Доступ отозван, но записи этого ПК на Google Drive удалить не удалось.")
        End If
        Return Localization.TF("Вы вышли из Google Drive. Доступ отозван, записей этого ПК удалено: {0}.", result.DeletedCount)
    End Function

    ''' <summary>The one question before signing out: the safe answer is No; it names what happens, with no lecture.</summary>
    Public Shared Function SignOutQuestion() As String
        Return Localization.T("Выйти из Google Drive на этом ПК? Доступ будет отозван у Google и удалён с этого ПК, а записи этого ПК будут удалены с вашего Google Drive.")
    End Function

    ''' <summary>What the app can see and where the sign-in is kept: the scope is the app's own hidden folder, nothing else.</summary>
    Public Shared Function ScopeNote() As String
        Return Localization.T("Приложению доступна только его собственная скрытая папка на вашем Google Drive, а не ваши файлы. Вход хранится на этом ПК под защитой Windows.")
    End Function

    Public Shared Function PrivacyNote() As String
        Return Localization.T("Через Google Drive файлы и буфер хранятся в вашем собственном аккаунте Google, как и другие ваши файлы на Google Drive; сервер обмена в этом не участвует.")
    End Function

    ''' <summary>This PC's name and short id in the channel. "" until a sign-in resolved the id.</summary>
    Public Shared Function DeviceLine(name As String, id As String) As String
        If String.IsNullOrEmpty(id) Then Return ""
        Dim shortId As String = If(id.Length > 8, id.Substring(0, 8) & "..", id)
        Return Localization.TF("Имя этого ПК в Google Drive: «{0}». Идентификатор: {1}.", If(String.IsNullOrEmpty(name), "-", name), shortId)
    End Function

    ''' <summary>The page the browser shows after Google redirects back. The same page whatever the answer was (it is sent before
    ''' the answer is examined), so it says neither "signed in" nor "failed". No script, no external resource.</summary>
    Public Shared Function LandingPage() As String
        Dim text As String = WebUtility.HtmlEncode(Localization.T("Ответ получен. Эту вкладку можно закрыть и вернуться в программу."))
        Return "<!doctype html><html><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1"">" &
               "<meta name=""referrer"" content=""no-referrer""><title>Google Drive</title></head>" &
               "<body style=""font-family:Segoe UI,Arial,sans-serif;margin:3em auto;max-width:32em;padding:0 1em;color:#222"">" &
               "<p>" & text & "</p></body></html>"
    End Function

End Class
