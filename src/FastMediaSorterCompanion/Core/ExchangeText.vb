Option Strict On

''' <summary>
''' The words of the "Media Exchange Server" group, in one place (ticket 142; the sibling of
''' <see cref="HostingText"/>): status lines, notes, questions and results.
'''
''' House rules held here: the product's NAME comes only from <see cref="ProductNames.ExchangeServer"/>
''' (a caption cites the constant, a sentence takes a <c>{0}</c> placeholder) - it is Latin in every
''' language and is never spelled in a table; the translated ROLE description is "сервер обмена"
''' (the exchange server); UI strings write ".." for an ellipsis and a plain hyphen.
''' </summary>
Friend Module ExchangeText

    ' --- the group and its state ------------------------------------------------------------

    ''' <summary>The section title: the name, never translated.</summary>
    Friend Function Title() As String
        Return ProductNames.ExchangeServer
    End Function

    Friend Function Intro() As String
        Return Localization.T("Сервер обмена - посредник для серверов раздачи, у которых нет ни доступного адреса, ни проброса порта. Он запускается только по вашей команде и сам не стартует.")
    End Function

    Friend Function StateLine(m As ExchangeGroupModel) As String
        Select Case m.Kind
            Case ExchangeRunKind.RunningConsole
                Return Localization.TF("Сервер обмена работает (процесс {0}).", m.Pid)
            Case ExchangeRunKind.ServiceRunning
                Return Localization.T("Сервер обмена работает как служба Windows.")
            Case ExchangeRunKind.ServiceStarting
                Return Localization.T("Служба сервера обмена запускается..")
            Case ExchangeRunKind.ServiceStopping
                Return Localization.T("Служба сервера обмена останавливается..")
            Case ExchangeRunKind.ServiceStopped
                Return Localization.T("Служба сервера обмена остановлена.")
            Case ExchangeRunKind.NotConfigured
                Return Localization.T("Сервер обмена не настроен: пароль не задан.")
            Case ExchangeRunKind.Unreadable
                Return Localization.T("Состояние сервера обмена не удалось прочитать.")
            Case Else
                Return Localization.T("Сервер обмена не запущен.")
        End Select
    End Function

    ''' <summary>133 section 5: the program cannot know whether the world reaches the port, so it says so
    ''' instead of implying it.</summary>
    Friend Function ReachabilityNote() As String
        Return Localization.T("Доступность из интернета не проверяется. Откройте порт в брандмауэре и на роутере и проверьте подключение с другого устройства.")
    End Function

    Friend Function ServiceWinsNote() As String
        Return Localization.T("Пока служба зарегистрирована, она главная: запуск из менеджера скрыт.")
    End Function

    ' --- the listen port --------------------------------------------------------------------

    Friend Function PortNote() As String
        Return Localization.T("Порт, на котором сервер обмена принимает подключения. Его же нужно открыть в брандмауэре и пробросить на роутере.")
    End Function

    Friend Function PortQuestion(newPort As Integer, restarts As Boolean) As String
        If restarts Then
            Return Localization.TF("Применить порт {0}?" & vbCrLf & vbCrLf &
                                   "Работающий сервер обмена будет перезапущен: подключённые серверы раздачи отключатся и подключатся заново.", newPort)
        End If
        Return Localization.TF("Применить порт {0}?" & vbCrLf & vbCrLf &
                               "Служба применит его при следующем запуске.", newPort)
    End Function

    Friend Function PortPending(newPort As Integer) As String
        Return Localization.TF("Порт {0} ещё не применён - нажмите «Применить порт».", newPort)
    End Function

    Friend Function PortResult(outcome As ExchangeProcess.ListenOutcome, restarted As Boolean) As String
        Select Case outcome
            Case ExchangeProcess.ListenOutcome.Saved
                Return If(restarted, Localization.T("Порт сохранён. Сервер обмена перезапущен."),
                                     Localization.T("Порт сохранён. Он вступит в силу при следующем запуске сервера обмена."))
            Case ExchangeProcess.ListenOutcome.BadAddress
                Return Localization.T("Этот порт сервер обмена не принял.")
            Case Else
                Return Localization.T("Не удалось записать порт в настройки сервера обмена.")
        End Select
    End Function

    ' --- the password -----------------------------------------------------------------------

    Friend Function PasswordNote() As String
        Return Localization.T("Пароль нужен серверу обмена, чтобы принимать подключения. В файле настроек хранится только его хеш, а не сам пароль.")
    End Function

    Friend Function PasswordState(m As ExchangeGroupModel) As String
        If m.PasswordBroken Then Return Localization.T("Запись пароля в настройках повреждена - задайте пароль заново.")
        If m.PasswordSet Then Return Localization.T("Пароль задан.")
        Return Localization.T("Пароль не задан.")
    End Function

    Friend Function SetPasswordButton() As String
        Return Localization.T("Задать пароль..")
    End Function

    Friend Function GeneratePasswordButton() As String
        Return Localization.T("Создать пароль..")
    End Function

    Friend Function SetPasswordTitle() As String
        Return Localization.T("Пароль сервера обмена")
    End Function

    Friend Function PolicyHint() As String
        Return Localization.T("Не короче 10 символов, с заглавной и строчной латинской буквой и цифрой.")
    End Function

    ''' <summary>The reason beside the field - one sentence per way a password can fail the floor.</summary>
    Friend Function PolicyReason(v As ExchangePasswordPolicy.Verdict) As String
        Select Case v
            Case ExchangePasswordPolicy.Verdict.TooShort
                Return Localization.T("Пароль должен быть не короче 10 символов.")
            Case ExchangePasswordPolicy.Verdict.NoUpper
                Return Localization.T("Нужна хотя бы одна заглавная латинская буква.")
            Case ExchangePasswordPolicy.Verdict.NoLower
                Return Localization.T("Нужна хотя бы одна строчная латинская буква.")
            Case ExchangePasswordPolicy.Verdict.NoDigit
                Return Localization.T("Нужна хотя бы одна цифра.")
            Case Else
                Return ""
        End Select
    End Function

    Friend Function SavePasswordButton() As String
        Return Localization.T("Сохранить пароль")
    End Function

    Friend Function GenerateTitle() As String
        Return Localization.T("Новый пароль сервера обмена")
    End Function

    Friend Function GenerateIntro() As String
        Return Localization.T("Это единственный раз, когда пароль показан. Скопируйте его и передайте владельцам серверов раздачи, которые будут подключаться.")
    End Function

    Friend Function GenerateQuestion() As String
        Return Localization.T("Создать новый пароль?" & vbCrLf & vbCrLf &
                              "Прежний пароль перестанет работать: серверы раздачи, которые его используют, придётся перенастроить.")
    End Function

    Friend Function CopyPasswordButton() As String
        Return Localization.T("Скопировать пароль")
    End Function

    Friend Function PasswordCopied() As String
        Return Localization.T("Пароль скопирован.")
    End Function

    Friend Function CopyFailed() As String
        Return Localization.T("Не удалось скопировать: буфер обмена занят. Повторите попытку.")
    End Function

    Friend Function CloseWithoutCopyQuestion() As String
        Return Localization.T("Закрыть, не скопировав пароль?" & vbCrLf & vbCrLf &
                              "Показать его снова нельзя - придётся создать новый.")
    End Function

    Friend Function PasswordResult(outcome As ExchangeProcess.PasswordOutcome, serverRunning As Boolean) As String
        Select Case outcome
            Case ExchangeProcess.PasswordOutcome.Saved
                Return If(serverRunning, Localization.T("Пароль сохранён. Он вступит в силу после перезапуска сервера обмена."),
                                         Localization.T("Пароль сохранён."))
            Case ExchangeProcess.PasswordOutcome.PolicyRefused
                Return Localization.T("Пароль не принят: он не соответствует правилам.")
            Case ExchangeProcess.PasswordOutcome.ConfigProblem
                Return Localization.T("Не удалось сохранить пароль: файл настроек сервера обмена не читается или не записывается.")
            Case Else
                Return Localization.T("Не удалось сохранить пароль.")
        End Select
    End Function

    ' --- the fingerprint ----------------------------------------------------------------------

    Friend Function FingerprintLabel() As String
        Return Localization.T("Отпечаток сертификата:")
    End Function

    Friend Function FingerprintNote() As String
        Return Localization.T("Сравните этот отпечаток с тем, что показывает другое устройство, прежде чем доверять серверу обмена.")
    End Function

    Friend Function FingerprintMissing() As String
        Return Localization.T("Отпечаток появится после первого запуска сервера обмена.")
    End Function

    Friend Function CopyFingerprintButton() As String
        Return Localization.T("Скопировать отпечаток")
    End Function

    Friend Function FingerprintCopied() As String
        Return Localization.T("Отпечаток скопирован.")
    End Function

    ' --- start / stop -------------------------------------------------------------------------

    Friend Function StartButton() As String
        Return Localization.T("Запустить сервер")
    End Function

    Friend Function StopButton() As String
        Return Localization.T("Остановить сервер")
    End Function

    Friend Function KeepRunningCheckbox() As String
        Return Localization.T("Не останавливать сервер обмена при выходе из менеджера")
    End Function

    Friend Function StartResult(outcome As ExchangeProcess.StartOutcome, port As Integer) As String
        Select Case outcome
            Case ExchangeProcess.StartOutcome.Started
                Return Localization.T("Сервер обмена запущен.")
            Case ExchangeProcess.StartOutcome.AlreadyRunning
                Return Localization.T("Сервер обмена уже работает.")
            Case ExchangeProcess.StartOutcome.NoPassword
                Return Localization.T("Не запущен: пароль не задан или его запись повреждена.")
            Case ExchangeProcess.StartOutcome.ConfigProblem
                Return Localization.T("Не запущен: файл настроек сервера обмена не читается.")
            Case ExchangeProcess.StartOutcome.BesideService
                Return Localization.T("Не запущен: уже работает служба сервера обмена, она главная.")
            Case ExchangeProcess.StartOutcome.Unavailable
                Return Localization.T("Файл сервера обмена не найден.")
            Case Else
                Return Localization.TF("Не запущен: сервер обмена завершился с ошибкой (возможно, порт {0} занят другой программой). Подробности - в его журнале.", port)
        End Select
    End Function

    Friend Function StopResult(outcome As ExchangeProcess.StopOutcome) As String
        Select Case outcome
            Case ExchangeProcess.StopOutcome.Stopped, ExchangeProcess.StopOutcome.NotRunning
                Return Localization.T("Сервер обмена остановлен.")
            Case ExchangeProcess.StopOutcome.Killed
                Return Localization.T("Сервер обмена не отвечал на просьбу остановиться и был завершён принудительно.")
            Case Else
                Return Localization.T("Не удалось остановить сервер обмена.")
        End Select
    End Function

    ' --- the firewall -------------------------------------------------------------------------

    Friend Function FirewallState(s As ExchangeFirewall.RuleState) As String
        Select Case s
            Case ExchangeFirewall.RuleState.ThisProgram
                Return Localization.T("Брандмауэр: для этой программы есть разрешающее правило.")
            Case ExchangeFirewall.RuleState.OtherProgram
                Return Localization.T("Брандмауэр: есть правило для другой копии программы (например, для службы).")
            Case ExchangeFirewall.RuleState.None
                Return Localization.T("Брандмауэр: разрешающего правила нет - с других устройств сервер обмена недоступен.")
            Case Else
                Return Localization.T("Брандмауэр: состояние не удалось прочитать.")
        End Select
    End Function

    Friend Function FirewallEnableButton() As String
        Return Localization.T("Разрешить доступ с других устройств..")
    End Function

    Friend Function FirewallRemoveButton() As String
        Return Localization.T("Убрать правило..")
    End Function

    Friend Function FirewallNote() As String
        Return Localization.T("Добавляет одно входящее правило брандмауэра Windows - только для программы сервера обмена, без номеров портов. Без него другие устройства и проброс порта на роутере не достучатся до сервера обмена. Нужно одно подтверждение администратора.")
    End Function

    Friend Function FirewallRemoveQuestion() As String
        Return Localization.T("Убрать правило брандмауэра?" & vbCrLf & vbCrLf &
                              "Другие устройства перестанут подключаться к серверу обмена.")
    End Function

    Friend Function FirewallResult(outcome As ExchangeElevation.ElevatedOutcome, withdrew As Boolean) As String
        Select Case outcome
            Case ExchangeElevation.ElevatedOutcome.Succeeded
                Return If(withdrew, Localization.T("Правило убрано."), Localization.T("Правило добавлено."))
            Case ExchangeElevation.ElevatedOutcome.Declined
                Return Localization.T("Не выполнено: не получены права администратора.")
            Case ExchangeElevation.ElevatedOutcome.Unavailable
                Return Localization.T("Файл сервера обмена или его помощник не найден.")
            Case Else
                Return Localization.T("Не удалось выполнить действие. Повторите попытку; если не выйдет - отправьте журнал автору: меню значка в трее - «Отправить журнал автору..».")
        End Select
    End Function

    ' --- the service --------------------------------------------------------------------------

    Friend Function ServiceNote(registered As Boolean) As String
        If registered Then
            Return Localization.T("Служба запускается вместе с Windows и работает без входа в систему. Запускать и останавливать её можно отсюда без запроса прав администратора.")
        End If
        Return Localization.T("Служба запускается вместе с Windows и работает без входа в систему от имени LocalService. Регистрация добавляет правило брандмауэра для службы и переносит сертификат и настройки из вашей папки, если они там есть. Нужно одно подтверждение администратора.")
    End Function

    Friend Function RegisterButton() As String
        Return Localization.T("Зарегистрировать как службу Windows..")
    End Function

    Friend Function RemoveServiceButton() As String
        Return Localization.T("Удалить службу сервера обмена..")
    End Function

    Friend Function RegisterQuestion() As String
        Return Localization.T("Зарегистрировать сервер обмена как службу Windows?" & vbCrLf & vbCrLf &
                              "Служба будет запускаться вместе с Windows и работать без входа в систему. Пока она есть, запуск из менеджера недоступен.")
    End Function

    Friend Function RemoveServiceQuestion() As String
        Return Localization.T("Удалить службу сервера обмена?" & vbCrLf & vbCrLf &
                              "Служба и её правило брандмауэра будут удалены. Папка с настройками и сертификатом останется.")
    End Function

    Friend Function ServiceResult(r As ServiceControl.ManageResult) As String
        Select Case r
            Case ServiceControl.ManageResult.Succeeded
                Return Localization.T("Готово.")
            Case ServiceControl.ManageResult.Declined
                Return Localization.T("Не выполнено: не получены права администратора.")
            Case ServiceControl.ManageResult.Unavailable
                Return Localization.T("Файл сервера обмена или его помощник не найден.")
            Case Else
                Return Localization.T("Не удалось выполнить действие. Повторите попытку; если не выйдет - отправьте журнал автору: меню значка в трее - «Отправить журнал автору..».")
        End Select
    End Function

    ' --- the tray ----------------------------------------------------------------------------

    ''' <summary>The tray menu's one item (ticket 142 T-6): the name, then what it is doing.</summary>
    Friend Function TrayItem(kind As ExchangeRunKind) As String
        Select Case kind
            Case ExchangeRunKind.RunningConsole
                Return Localization.TF("{0}: работает", ProductNames.ExchangeServer)
            Case ExchangeRunKind.ServiceRunning, ExchangeRunKind.ServiceStarting
                Return Localization.TF("{0}: работает как служба", ProductNames.ExchangeServer)
            Case Else
                Return Localization.TF("{0}: остановлен", ProductNames.ExchangeServer)
        End Select
    End Function

    ''' <summary>Which of the tray's states applies right now, from two cheap reads: the service's state and
    ''' whether the console server runs. No process is started, no helper is run.</summary>
    Friend Function TrayKind(consoleRunning As Boolean, serviceExists As Boolean, state As ServiceControl.ServiceState) As ExchangeRunKind
        If serviceExists Then
            Select Case state
                Case ServiceControl.ServiceState.Running : Return ExchangeRunKind.ServiceRunning
                Case ServiceControl.ServiceState.Starting : Return ExchangeRunKind.ServiceStarting
                Case Else : Return ExchangeRunKind.ServiceStopped
            End Select
        End If
        Return If(consoleRunning, ExchangeRunKind.RunningConsole, ExchangeRunKind.Stopped)
    End Function

End Module
