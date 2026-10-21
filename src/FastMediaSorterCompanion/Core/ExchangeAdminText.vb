Option Strict On

''' <summary>
''' The words of the server administration of ticket 168 (accounts, devices, the address clients use, the third-party
''' ports, the certificate, the statement and the "use this PC as my server" wizard), in one place, beside
''' <see cref="ExchangeText"/>. One sentence per outcome: a server's own reason or error text is NEVER shown - every
''' admin answer is a stable code and the words are ours.
'''
''' House rules held here: the product's NAME comes only from <see cref="ProductNames"/>; UI strings write ".." for an
''' ellipsis and a plain hyphen; a sentence that carries a value takes a {0} placeholder (TF), never concatenation;
''' no address of a real host is spelled in a string - the example is a documentation address (203.0.113.x).
''' </summary>
Friend Module ExchangeAdminText

    ' --- accounts -----------------------------------------------------------------------------

    Friend Function AccountsTitle() As String
        Return Localization.T("Учётные записи")
    End Function

    Friend Function AccountsIntro() As String
        Return Localization.T("Учётную запись создаёт оператор сервера - то есть вы. Каждый человек входит в свою запись со всех своих устройств; чужих записей и устройств он не видит. Пароль сервер хранит только в виде хеша.")
    End Function

    Friend Function Totals(accounts As Integer, devices As Integer) As String
        Return Localization.TF("Учётных записей: {0}, устройств: {1}.", accounts, devices)
    End Function

    Friend Function FirstAccountHint() As String
        Return Localization.T("Сервер не запустится, пока нет ни одной учётной записи. Добавьте первую.")
    End Function

    Friend Function OfflineNote() As String
        Return Localization.T("Сервер не запущен: изменения записываются прямо в его файлы и вступят в силу при запуске.")
    End Function

    Friend Function NotReached() As String
        Return Localization.T("Не удалось связаться с сервером обмена, чтобы прочитать учётные записи.")
    End Function

    Friend Function ColumnLogin() As String
        Return Localization.T("Логин")
    End Function

    Friend Function ColumnDevices() As String
        Return Localization.T("Устройств")
    End Function

    Friend Function ColumnCreated() As String
        Return Localization.T("Создана")
    End Function

    Friend Function ColumnProduct() As String
        Return Localization.T("Программа")
    End Function

    Friend Function ColumnPlatform() As String
        Return Localization.T("Система")
    End Function

    Friend Function AddAccountButton() As String
        Return Localization.T("Добавить учётную запись..")
    End Function

    Friend Function ResetPasswordButton() As String
        Return Localization.T("Сбросить пароль..")
    End Function

    ' --- keeping the account passwords on this PC (an opt-in, off by default) ---

    Friend Function ShowPasswordButton() As String
        Return Localization.T("Показать пароль..")
    End Function

    Friend Function OwnPasswordButton() As String
        Return Localization.T("Задать свой пароль..")
    End Function

    Friend Function KeepPasswordsCheckbox() As String
        Return Localization.T("Хранить пароли учётных записей на этом ПК")
    End Function

    Friend Function KeepPasswordsNote() As String
        Return Localization.T("Выключено: пароль показывается один раз, и его нельзя прочитать снова - только сбросить. Включено: пароль сохраняется на этом ПК, зашифрованным для вашей учётной записи Windows, и его можно показать кнопкой «Показать пароль..».")
    End Function

    ''' <summary>The two questions the switch asks, both with the default on No: the first names what keeping means, the second what happens to the copies.</summary>
    Friend Function KeepPasswordsConfirmOn() As String
        Return Localization.T("Хранить пароли учётных записей на этом ПК?" & vbCrLf & vbCrLf &
                              "Пароль будет храниться на этом ПК в виде, который открывается только под вашей учётной записью Windows. Любой процесс, работающий под ней, сможет его прочитать. Сервер хранит только хэш, поэтому эта копия станет единственной. Если этим ПК пользуются и другие люди, выберите «Нет».")
    End Function

    Friend Function KeepPasswordsConfirmOff() As String
        Return Localization.T("Перестать хранить пароли на этом ПК?" & vbCrLf & vbCrLf &
                              "Сохранённые пароли будут удалены с этого ПК. Учётные записи и их пароли на сервере не меняются, но прочитать их здесь будет нельзя.")
    End Function

    Friend Function KeptTitle(login As String) As String
        Return Localization.TF("Пароль учётной записи «{0}»", login)
    End Function

    Friend Function KeptIntro() As String
        Return Localization.T("Пароль сохранён на этом ПК. Его видите вы и любой процесс под вашей учётной записью Windows. Копируйте его только туда, где он нужен.")
    End Function

    Friend Function NotKept(login As String) As String
        Return Localization.TF("Пароль учётной записи «{0}» на этом ПК не сохранён: он задан до того, как включили хранение. Сбросьте пароль, и новый сохранится.", login)
    End Function

    ''' <summary>Said after the account was made or reset when its password could not be kept: a typed one too, so no "copy it now".</summary>
    Friend Function KeepFailed() As String
        Return Localization.T("Пароль не удалось сохранить на этом ПК: показать его здесь потом будет нельзя.")
    End Function

    Friend Function RemoveAccountButton() As String
        Return Localization.T("Удалить учётную запись..")
    End Function

    Friend Function DevicesTitle(login As String) As String
        Return Localization.TF("Устройства учётной записи «{0}»", login)
    End Function

    Friend Function RemoveAccountQuestion(login As String) As String
        Return Localization.TF("Удалить учётную запись «{0}»?" & vbCrLf & vbCrLf &
                               "Все её устройства будут выведены из системы, а их записи исчезнут. Отменить это нельзя.", login)
    End Function

    Friend Function RevokeDeviceQuestion(name As String) As String
        ' the same sentence the device agent's own group asks (ticket 180), so the two never say different things
        Return Localization.TF("Отозвать устройство «{0}»? Оно потеряет доступ к учётной записи, а его ресурсы исчезнут из списка.", name)
    End Function

    ''' <summary>One sentence per failure code of an admin answer. The server's own text is never used.</summary>
    Friend Function Failure(code As String) As String
        Select Case code
            Case ExchangeAdminCodes.BadRequest
                Return Localization.T("Сервер не принял этот запрос. Проверьте введённое и повторите.")
            Case ExchangeAdminCodes.NoAccount
                Return Localization.T("Такой учётной записи нет.")
            Case ExchangeAdminCodes.Exists
                Return Localization.T("Это уже есть на сервере.")
            Case ExchangeAdminCodes.BadLogin
                Return Localization.T("Логин не принят: от 3 до 64 символов - строчные латинские буквы, цифры, точка, подчёркивание и дефис.")
            Case ExchangeAdminCodes.WeakPassword
                Return Localization.T("Пароль не принят: он не соответствует правилам.")
            Case ExchangeAdminCodes.NotFound
                Return Localization.T("Этого нет на сервере. Возможно, оно уже удалено.")
            Case ExchangeAdminCodes.Capacity
                Return Localization.T("Сервер сейчас не может это принять: достигнут его предел. Повторите позже.")
            Case ExchangeAdminCodes.BadKind
                Return Localization.T("Учётная запись бывает либо «своё устройство или семья», либо «гость».")
            Case ExchangeAdminCodes.BadLimit
                Return Localization.T("Предел не принят: нужно целое число в границах, которые принимает сервер.")
            Case ExchangeAdminCodes.NotReached
                Return Localization.T("Не удалось связаться с сервером обмена.")
            Case Else
                Return Localization.T("Серверу не удалось это сделать. Подробности - в журнале сервера.")
        End Select
    End Function

    Friend Function AccountAdded(login As String) As String
        Return Localization.TF("Учётная запись «{0}» создана.", login)
    End Function

    Friend Function PasswordReset(login As String, signedOut As Boolean) As String
        If signedOut Then Return Localization.TF("Пароль учётной записи «{0}» изменён, все её устройства выведены из системы.", login)
        Return Localization.TF("Пароль учётной записи «{0}» изменён.", login)
    End Function

    Friend Function AccountRemoved(login As String) As String
        Return Localization.TF("Учётная запись «{0}» удалена.", login)
    End Function

    Friend Function DeviceRevoked(name As String) As String
        Return Localization.TF("Устройство «{0}» отозвано.", name)
    End Function

    ' --- the add and reset dialog ---------------------------------------------------------------

    Friend Function AddAccountTitle() As String
        Return Localization.T("Новая учётная запись")
    End Function

    Friend Function ResetPasswordTitle(login As String) As String
        Return Localization.TF("Сброс пароля: {0}", login)
    End Function

    Friend Function LoginHint() As String
        Return Localization.T("Логин: от 3 до 64 символов - строчные латинские буквы, цифры, точка, подчёркивание и дефис.")
    End Function

    Friend Function GenerateChoice() As String
        Return Localization.T("Создать пароль (его покажут один раз)")
    End Function

    Friend Function TypeChoice() As String
        Return Localization.T("Ввести пароль самому")
    End Function

    Friend Function SignOutEverywhere() As String
        Return Localization.T("Сразу вывести из системы все устройства этой учётной записи")
    End Function

    Friend Function CreateButton() As String
        Return Localization.T("Создать")
    End Function

    Friend Function ResetButton() As String
        Return Localization.T("Сбросить")
    End Function

    Friend Function StatementBeforeFirst() As String
        Return Localization.T("Прежде чем создать первую учётную запись, прочтите, что видит оператор сервера:")
    End Function

    ' --- the statement ---------------------------------------------------------------------------

    Friend Function StatementTitle() As String
        Return Localization.T("Что видит оператор сервера")
    End Function

    Friend Function StatementShow() As String
        Return Localization.T("Показать, что видит оператор сервера..")
    End Function

    Friend Function StatementHide() As String
        Return Localization.T("Свернуть")
    End Function

    ' --- the address clients use -----------------------------------------------------------------

    Friend Function EndpointLabel() As String
        Return Localization.T("Адрес для клиентов (хост:порт):")
    End Function

    Friend Function EndpointNote() As String
        Return Localization.T("Этот адрес сервер сообщает каждому устройству. Пока поле пустое, клиенты не получают адресов для трансляций и доступа к папкам, и эти возможности скрыты. Изменение вступит в силу при следующем запуске сервера.")
    End Function

    Friend Function EndpointApply() As String
        Return Localization.T("Применить адрес")
    End Function

    Friend Function EndpointPending() As String
        Return Localization.T("Адрес ещё не применён - нажмите «Применить адрес».")
    End Function

    Friend Function EndpointSuggest(suggestion As String) As String
        Return Localization.TF("Подставить {0}", suggestion)
    End Function

    Friend Function EndpointVerdict(v As ExchangeOperatorEndpoint.Verdict) As String
        Select Case v
            Case ExchangeOperatorEndpoint.Verdict.Loopback
                Return Localization.T("Адрес не может быть локальным (localhost, 127.0.0.1, 0.0.0.0): другие устройства по нему не подключатся.")
            Case ExchangeOperatorEndpoint.Verdict.NotHostPort, ExchangeOperatorEndpoint.Verdict.BadPort, ExchangeOperatorEndpoint.Verdict.NoHost
                Return Localization.T("Адрес должен выглядеть как хост:порт, например 203.0.113.7:22022.")
            Case Else
                Return ""
        End Select
    End Function

    Friend Function ConfigResult(outcome As ExchangeProcess.ConfigOutcome, savedText As String) As String
        Select Case outcome
            Case ExchangeProcess.ConfigOutcome.Saved
                Return savedText
            Case ExchangeProcess.ConfigOutcome.Refused
                Return Localization.T("Сервер обмена не принял это значение.")
            Case ExchangeProcess.ConfigOutcome.Unavailable
                Return Localization.T("Файл сервера обмена не найден.")
            Case Else
                Return Localization.T("Не удалось записать настройки сервера обмена: файл не читается или не записывается.")
        End Select
    End Function

    Friend Function EndpointSaved() As String
        Return Localization.T("Адрес сохранён. Он вступит в силу при следующем запуске сервера обмена.")
    End Function

    Friend Function EndpointCleared() As String
        Return Localization.T("Адрес очищен. Это вступит в силу при следующем запуске сервера обмена.")
    End Function

    ' --- the third-party ports --------------------------------------------------------------------

    Friend Function RangeLabel() As String
        Return Localization.T("Порты для сторонних программ:")
    End Function

    Friend Function RangeFrom() As String
        Return Localization.T("от")
    End Function

    Friend Function RangeTo() As String
        Return Localization.T("до")
    End Function

    Friend Function RangeNote() As String
        Return Localization.T("Через эти порты наружу открываются общие папки: SFTP-клиенты подключаются прямо к ним. На домашнем роутере пробросьте порт сервера и весь этот диапазон. Правило брандмауэра Windows относится к самой программе, поэтому смена диапазона не требует прав администратора.")
    End Function

    Friend Function RangeCount(count As Integer) As String
        Return Localization.TF("Портов в диапазоне: {0}.", count)
    End Function

    Friend Function RangeApply() As String
        Return Localization.T("Применить диапазон")
    End Function

    Friend Function RangePending() As String
        Return Localization.T("Диапазон ещё не применён - нажмите «Применить диапазон».")
    End Function

    Friend Function RangeVerdict(v As ExchangePortRangeRules.Verdict) As String
        Select Case v
            Case ExchangePortRangeRules.Verdict.Order
                Return Localization.T("Нижний порт больше верхнего.")
            Case ExchangePortRangeRules.Verdict.Bounds
                Return Localization.TF("Допустимы порты от {0} до {1}.", ExchangeSettings.MinPort, ExchangeSettings.MaxPort)
            Case ExchangePortRangeRules.Verdict.ContainsListenPort
                Return Localization.T("В диапазон попал порт самого сервера - сдвиньте диапазон.")
            Case Else
                Return ""
        End Select
    End Function

    Friend Function RangeSaved() As String
        Return Localization.T("Диапазон сохранён. Он вступит в силу при следующем запуске сервера обмена.")
    End Function

    ' --- the certificate ---------------------------------------------------------------------------

    Friend Function CertificateKind(imported As Boolean) As String
        Return If(imported, Localization.T("Сертификат: импортированный."), Localization.T("Сертификат: самоподписанный, создан сервером при первом запуске."))
    End Function

    Friend Function ReplaceCertificateButton() As String
        Return Localization.T("Заменить на новый самоподписанный сертификат..")
    End Function

    Friend Function ImportCertificateButton() As String
        Return Localization.T("Импортировать сертификат и ключ..")
    End Function

    Friend Function ReplaceCertificateQuestion() As String
        Return Localization.T("Заменить сертификат сервера?" & vbCrLf & vbCrLf &
                              "Все уже подключённые устройства откажутся работать с этим сервером, пока их пользователи не закрепят новый отпечаток.")
    End Function

    Friend Function ReplaceCertificateSure() As String
        Return Localization.T("Вы уверены?" & vbCrLf & vbCrLf &
                              "Прежний сертификат и его ключ будут удалены без возможности восстановления.")
    End Function

    Friend Function ImportCertificateQuestion() As String
        Return Localization.T("Использовать выбранные сертификат и ключ?" & vbCrLf & vbCrLf &
                              "Устройства, закрепившие прежний сертификат, перестанут доверять серверу, пока их пользователи не закрепят новый отпечаток (устройства, которым подходит системная проверка, не затронуты).")
    End Function

    Friend Function PickCertificateTitle() As String
        Return Localization.T("Сертификат сервера (PEM)")
    End Function

    Friend Function PickKeyTitle() As String
        Return Localization.T("Закрытый ключ сертификата (PEM)")
    End Function

    Friend Function PemFilter() As String
        Return Localization.T("Файлы PEM") & "|*.pem;*.crt;*.cer;*.key|" & Localization.T("Все файлы") & "|*.*"
    End Function

    Friend Function CertificateResult(r As ExchangeProcess.CertificateResult) As String
        Select Case r.Outcome
            Case ExchangeProcess.CertificateOutcome.Done
                Return Localization.TF("Сертификат сохранён. Отпечаток: {0}. Он начнёт действовать при следующем запуске сервера обмена.", r.Fingerprint)
            Case ExchangeProcess.CertificateOutcome.Mismatch
                Return Localization.T("Ключ не подходит к сертификату: ничего не изменено.")
            Case ExchangeProcess.CertificateOutcome.Expired
                Return Localization.T("Срок действия сертификата истёк: ничего не изменено.")
            Case ExchangeProcess.CertificateOutcome.NotYetValid
                Return Localization.T("Сертификат ещё не действует: ничего не изменено.")
            Case ExchangeProcess.CertificateOutcome.Unreadable
                Return Localization.T("Файлы не удалось прочитать как PEM: ничего не изменено.")
            Case ExchangeProcess.CertificateOutcome.Unavailable
                Return Localization.T("Файл сервера обмена не найден.")
            Case Else
                Return Localization.T("Не удалось изменить сертификат: ничего не изменено или изменено не полностью. Подробности - в журнале.")
        End Select
    End Function

    Friend Function CertificateServiceNote() As String
        Return Localization.T("Служба хранит сертификат в своей папке, куда у этого пользователя нет доступа. Заменить или импортировать сертификат можно, пока сервер запускается отсюда.")
    End Function

    ' --- the wizard ---------------------------------------------------------------------------------

    Friend Function WizardButton() As String
        Return Localization.T("Использовать этот ПК как мой сервер..")
    End Function

    Friend Function WizardTitle() As String
        Return Localization.T("Мой сервер на этом ПК")
    End Function

    Friend Function WizardIntro() As String
        Return Localization.T("Один шаг: создать первую учётную запись, запустить сервер на этом ПК и подключить к нему этот же ПК. Пароль будет показан один раз.")
    End Function

    Friend Function WizardLoginLabel() As String
        Return Localization.T("Логин вашей учётной записи:")
    End Function

    Friend Function WizardStart() As String
        Return Localization.T("Начать")
    End Function

    Friend Function WizardStep(s As ExchangeWizardStep) As String
        Select Case s
            Case ExchangeWizardStep.CreatingAccount
                Return Localization.T("Создаю учётную запись..")
            Case ExchangeWizardStep.StartingServer
                Return Localization.T("Запускаю сервер..")
            Case ExchangeWizardStep.CheckingCertificate
                Return Localization.T("Проверяю сертификат..")
            Case Else
                Return Localization.T("Подключаю этот ПК..")
        End Select
    End Function

    ''' <summary>The sentence for this PC being made the first device of an account that was just added or reset (no account is
    ''' created by that flow, so the wizard's own sentences, which say so, are not used). "" when nothing is to be said.</summary>
    Friend Function ThisPcEnrolment(r As ExchangeWizardResult) As String
        Select Case r.Outcome
            Case ExchangeWizardOutcome.Done
                Return Localization.TF("Этот ПК подключён к учётной записи «{0}» как первое устройство.", r.Login)
            Case ExchangeWizardOutcome.NotAvailable
                Return Localization.T("Этот ПК к учётной записи не подключён: сервер обмена не ответил на запрос.")
            Case ExchangeWizardOutcome.ServerNotAnswering
                Return Localization.T("Сервер запущен, но не отвечает на своём порту. Возможно, порт занят другой программой.")
            Case ExchangeWizardOutcome.CertificateMismatch
                Return Localization.T("На порту отвечает не этот сервер: сертификат не совпал. Пароль никуда не отправлен.")
            Case ExchangeWizardOutcome.EnrollFailed
                Return Localization.TF("Этот ПК не удалось подключить к учётной записи. {0}", ExchangeDeviceText.EnrollFailure(r.EnrollResult))
            Case Else
                Return ""
        End Select
    End Function

    Friend Function FactThisPc() As String
        Return Localization.T("Адрес для этого ПК:")
    End Function

    Friend Function FactOthers() As String
        Return Localization.T("Адрес для других устройств:")
    End Function

    Friend Function FactOthersUnset() As String
        Return Localization.T("не задан - укажите его в настройках сервера обмена")
    End Function

    Friend Function FactLogin() As String
        Return Localization.T("Логин:")
    End Function

    Friend Function FactPassword() As String
        Return Localization.T("Пароль (показан один раз):")
    End Function

    Friend Function FactFingerprintHint() As String
        Return Localization.T("Другим устройствам понадобятся адрес, логин, пароль и этот отпечаток: при первом подключении они сверяют его.")
    End Function

    ''' <summary>One sentence per way the wizard ended. An enrolment refusal adds the agent's own sentence after it.</summary>
    Friend Function WizardOutcome(r As ExchangeWizardResult, Optional enrollWasChosen As Boolean = True) As String
        Select Case r.Outcome
            Case ExchangeWizardOutcome.Done
                Return Localization.T("Готово: сервер работает, учётная запись создана, этот ПК подключён к ней.")
            Case ExchangeWizardOutcome.DoneNoAgent
                If Not enrollWasChosen Then Return Localization.T("Сервер работает, учётная запись создана. Подключение этого компьютера пропущено по вашему выбору.")
                Return Localization.T("Сервер работает, учётная запись создана. Этот ПК к ней не подключён: клиентские возможности обмена в этой сборке не включены.")
            Case ExchangeWizardOutcome.NotAvailable
                Return Localization.T("Мастер недоступен: сервер обмена не найден, не читается или им управляет служба.")
            Case ExchangeWizardOutcome.LoginRefused
                Return LoginHint()
            Case ExchangeWizardOutcome.AccountExists
                Return Localization.T("Такая учётная запись уже есть - выберите другой логин.")
            Case ExchangeWizardOutcome.AccountFailed
                Return Localization.T("Не удалось создать учётную запись. Ничего не сделано.")
            Case ExchangeWizardOutcome.StartFailed
                Return Localization.T("Учётная запись создана, но сервер не запустился. Её пароль показан ниже один раз; причину ищите в журнале сервера.")
            Case ExchangeWizardOutcome.ServerNotAnswering
                Return Localization.T("Сервер запущен, но не отвечает на своём порту. Возможно, порт занят другой программой.")
            Case ExchangeWizardOutcome.CertificateMismatch
                Return Localization.T("На порту отвечает не этот сервер: сертификат не совпал. Пароль никуда не отправлен.")
            Case ExchangeWizardOutcome.EnrollFailed
                Return Localization.TF("Сервер работает, учётная запись создана, но подключить этот ПК не удалось. {0}", ExchangeDeviceText.EnrollFailure(r.EnrollResult))
            Case Else
                Return Localization.T("Остановлено.")
        End Select
    End Function

    ' --- spec 246: management visibility ---

    Friend Function ResourcesTitle() As String
        Return Localization.T("Опубликованные ресурсы")
    End Function

    Friend Function ColumnResourceName() As String
        Return Localization.T("Имя ресурса")
    End Function

    Friend Function ColumnSourceDevice() As String
        Return Localization.T("Устройство-источник")
    End Function

    Friend Function ColumnChannels() As String
        Return Localization.T("Каналы")
    End Function

    Friend Function ColumnPinProtected() As String
        Return Localization.T("Защита PIN")
    End Function

    Friend Function ColumnSealed() As String
        Return Localization.T("Шифрование E2EE")
    End Function

    Friend Function ColumnPublished() As String
        Return Localization.T("Опубликовано")
    End Function

    Friend Function UnpublishResourceButton() As String
        Return Localization.T("Снять с публикации")
    End Function

    Friend Function UnpublishResourceConfirm(name As String) As String
        Return Localization.TF("Снять с публикации ресурс «{0}»?", name)
    End Function

    Friend Function BroadcastsTitle() As String
        Return Localization.T("Активные трансляции")
    End Function

    Friend Function ColumnBroadcastId() As String
        Return Localization.T("ID / Название")
    End Function

    Friend Function ColumnKind() As String
        Return Localization.T("Тип")
    End Function

    Friend Function ColumnListeners() As String
        Return Localization.T("Слушателей")
    End Function

    Friend Function ColumnDuration() As String
        Return Localization.T("Длительность")
    End Function

    Friend Function StopBroadcastButton() As String
        Return Localization.T("Остановить трансляцию")
    End Function

    Friend Function StopBroadcastConfirm(id As String) As String
        Return Localization.TF("Остановить трансляцию «{0}»?", id)
    End Function

    Friend Function ClipboardTitle() As String
        Return Localization.T("Буфер обмена учётной записи")
    End Function

    Friend Function ClipboardEmpty() As String
        Return Localization.T("Буфер обмена пуст.")
    End Function

    Friend Function ClipboardSealedNote() As String
        Return Localization.T("Сквозное шифрование (содержимое недоступно оператору)")
    End Function

    Friend Function ClearClipboardButton() As String
        Return Localization.T("Очистить буфер")
    End Function

    Friend Function ClearClipboardConfirm(login As String) As String
        Return Localization.TF("Очистить буфер обмена учётной записи «{0}»?", login)
    End Function

    Friend Function AuditLogButton() As String
        Return Localization.T("Журнал действий..")
    End Function

    Friend Function AuditDialogTitle() As String
        Return Localization.T("Журнал действий администратора")
    End Function

    Friend Function ActiveSessionsTitle() As String
        Return Localization.T("Активные сессии администрирования")
    End Function

    Friend Function ColumnSeq() As String
        Return Localization.T("№")
    End Function

    Friend Function ColumnTime() As String
        Return Localization.T("Время (UTC)")
    End Function

    Friend Function ColumnRemoteAddr() As String
        Return Localization.T("Адрес")
    End Function

    Friend Function ColumnOperator() As String
        Return Localization.T("Оператор")
    End Function

    Friend Function ColumnOperation() As String
        Return Localization.T("Операция")
    End Function

    Friend Function ColumnTarget() As String
        Return Localization.T("Цель")
    End Function

    Friend Function ColumnStatus() As String
        Return Localization.T("Результат")
    End Function

    ' --- the browser surface (WEB-BINDING, ticket 244) ------------------------------------------

    ''' <summary>What the certificate buttons' explanation says about renewal. It is true of both kinds of client: a program of this
    ''' product accepts a same-key renewal without asking (DEVICE-EXCHANGE item AY), a browser asks again after ANY new leaf, the same
    ''' key or not (WEB-BINDING certificate-trust 1 and 2).</summary>
    Friend Function CertificateRenewalNote() As String
        Return Localization.T("Продление сохраняет ключ, и программы принимают такой сертификат без вопросов. При замене ключа каждое устройство должно снова подтвердить сертификат. Браузер спрашивает снова после любого нового сертификата.")
    End Function

    ''' <summary>The sentence the surface that renews the certificate adds (WEB-BINDING certificate-trust 4, last paragraph).</summary>
    Friend Function RenewBrowserNote() As String
        Return Localization.T("Каждый браузер спросит снова, поэтому продлевайте сертификат только при необходимости.")
    End Function

    ''' <summary>The canonical warning of WEB-BINDING certificate-trust 4, verbatim in English, Russian and Ukrainian (item DA: not
    ''' reworded, shortened or joined with other text). It is true only for a certificate the server made itself, which is what has
    ''' a fingerprint to show.</summary>
    Friend Function BrowserWarning() As String
        Return Localization.T("Браузер предупредит, что соединение не защищено, потому что сервер создал сертификат сам. Ниже его отпечаток: если ваш браузер показывает сертификат, можно сравнить их. Продолжайте, только если доверяете этому серверу.")
    End Function

    ''' <summary>What the browser dialog says when there is no fingerprint to show - a certificate from a certificate authority, or
    ''' none made yet. The canonical warning is not shown then: it promises a fingerprint "below" and a warning the browser will not give.</summary>
    Friend Function BrowserNoFingerprintNotice() As String
        Return Localization.T("Отпечатка для показа нет: сертификат этого сервера выдан центром сертификации, либо сервер его ещё не создал. Такой сертификат браузер проверяет сам. Если браузер всё равно предупреждает, продолжайте, только если доверяете этому серверу.")
    End Function

    ''' <summary>The one sentence on top of the browser dialog: the canonical warning while there is a fingerprint, the truthful one
    ''' while there is not.</summary>
    Friend Function BrowserTrustNotice(fingerprintHex As String) As String
        Return If(String.IsNullOrWhiteSpace(fingerprintHex), BrowserNoFingerprintNotice(), BrowserWarning())
    End Function

    ''' <summary>WEB-BINDING page-served-by-the-server 3: the sentence for a page the server itself serves, canonical in English,
    ''' Russian and Ukrainian, shown next to the text of item DE and never joined with other text.</summary>
    Friend Function PageServedNotice() As String
        Return Localization.T("Эту страницу отдаёт сам сервер обмена, поэтому тот, кто им управляет, мог бы изменить её так, чтобы читать то, что вы отправляете. Пользуйтесь ею только на сервере, которым управляете вы сами или человек, которому вы доверяете.")
    End Function

End Module
