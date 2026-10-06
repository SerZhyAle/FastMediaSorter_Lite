Option Strict On

''' <summary>
''' The words of the producer half (ticket 148 T-4/T-6): the share service registering its shares on an exchange
''' server. Every sentence is a registered key in <c>Localization.ExchangeProducer.vb</c> (13 languages). The
''' product NAME is never in these strings; a share's name goes through a placeholder, and no sentence ever
''' carries a password, a share id or an endpoint the user did not just type.
''' </summary>
Friend Module ExchangeProducerText

    ' --- the controls -------------------------------------------------------------------------

    Friend Function ModeLabel() As String
        Return Localization.T("Сервер обмена для раздачи:")
    End Function

    Friend Function ModeName(mode As ExchangeMode) As String
        Select Case mode
            Case ExchangeMode.ThisPc : Return Localization.T("Сервер обмена этого ПК")
            Case ExchangeMode.External : Return Localization.T("Другой сервер обмена")
            Case Else : Return Localization.T("Не использовать")
        End Select
    End Function

    Friend Function RegistrationLabel() As String
        Return Localization.T("Адрес сервера обмена (хост:порт):")
    End Function

    Friend Function ClientLabel() As String
        Return Localization.T("Адрес для клиентов (хост:порт):")
    End Function

    Friend Function PasswordLabel() As String
        Return Localization.T("Пароль сервера обмена:")
    End Function

    Friend Function ApplyButton() As String
        Return Localization.T("Применить настройки обмена")
    End Function

    Friend Function PasswordHint(firstTime As Boolean) As String
        If firstTime Then
            Return Localization.T("Введите пароль сервера обмена. Он уходит только службе общего доступа, программа его нигде не сохраняет.")
        End If
        Return Localization.T("Пароль уходит только службе общего доступа, программа его нигде не сохраняет. Оставьте поле пустым, чтобы оставить прежний.")
    End Function

    ' --- what is wrong with the typed values ----------------------------------------------------

    Friend Function Problem(p As ExchangeProducerProblem, password As String) As String
        Select Case p
            Case ExchangeProducerProblem.RegistrationEndpoint
                Return Localization.T("Укажите адрес сервера обмена в виде хост:порт.")
            Case ExchangeProducerProblem.ClientEndpointMalformed
                Return Localization.T("Адрес для клиентов должен быть вида хост:порт.")
            Case ExchangeProducerProblem.ClientEndpointLoopback
                Return Localization.T("Петлевой адрес (127.0.0.1, localhost) телефону недоступен: укажите адрес, по которому устройства видят сервер обмена.")
            Case ExchangeProducerProblem.PasswordMissing
                Return Localization.T("Введите пароль сервера обмена.")
            Case ExchangeProducerProblem.PasswordBelowFloor
                Dim why As String = ExchangeText.PolicyReason(ExchangePasswordPolicy.Check(password))
                Return If(why.Length > 0, why, ExchangeText.PolicyHint())
            Case Else
                Return ""
        End Select
    End Function

    ' --- the state line -----------------------------------------------------------------------

    Friend Function WorkerSilent() As String
        Return Localization.T("Служба общего доступа не отвечает: состояние обмена неизвестно.")
    End Function

    Friend Function WorkerStale() As String
        Return Localization.T("Работающая служба общего доступа старше этого приложения и не умеет регистрироваться на сервере обмена. Обновите приложение.")
    End Function

    Friend Function NotApplied() As String
        Return Localization.T("Настройки обмена ещё не применены.")
    End Function

    Friend Function NoShares() As String
        Return Localization.T("Нет раздаваемых папок: регистрировать нечего.")
    End Function

    Friend Function RegisteredCount(registered As Integer, total As Integer) As String
        Return Localization.TF("Зарегистрировано раздач: {0} из {1}.", registered, total)
    End Function

    ''' <summary>Why a share is not (yet) registered, one sentence per named state. The four that need the user
    ''' say what to do; none of them says the share stopped serving - it keeps serving directly.</summary>
    Friend Function Reason(state As ExchangeShareState) As String
        Select Case state
            Case ExchangeShareState.Connecting
                Return Localization.T("Подключение к серверу обмена..")
            Case ExchangeShareState.RefusedPassword
                Return Localization.T("Сервер обмена отклонил пароль. Введите верный пароль и примените настройки.")
            Case ExchangeShareState.RefusedCapacity
                Return Localization.T("На сервере обмена нет места для новых раздач.")
            Case ExchangeShareState.RefusedPort
                Return Localization.T("Сервер обмена не смог выделить порт.")
            Case ExchangeShareState.CertificateChanged
                Return Localization.T("Сертификат сервера обмена изменился. Сверьте новый отпечаток с владельцем сервера и, если так и должно быть, впишите его в поле отпечатка и примените настройки.")
            Case ExchangeShareState.Unreachable
                Return Localization.T("Сервер обмена недоступен, повтор позже. Прямые пути работают.")
            Case ExchangeShareState.RateLimited
                Return Localization.T("Сервер обмена просит подождать, повтор позже.")
            Case Else
                Return Localization.T("Не зарегистрирована.")
        End Select
    End Function

    ''' <summary>One line of the per-share list.</summary>
    Friend Function ShareLine(name As String, state As ExchangeShareState, relayPort As Integer) As String
        If state = ExchangeShareState.Registered Then
            If relayPort > 0 Then Return Localization.TF("Раздача «{0}» зарегистрирована, порт {1}.", name, relayPort)
            Return Localization.TF("Раздача «{0}» зарегистрирована.", name)
        End If
        Return Localization.TF("Раздача «{0}»: {1}", name, Reason(state))
    End Function

    ' --- the notes ----------------------------------------------------------------------------

    Friend Function ClientEmptyNote() As String
        Return Localization.T("Адрес для клиентов не задан: в QR-коды и файлы не попадут пути через сервер обмена, остальные пути работают как раньше.")
    End Function

    Friend Function FingerprintExternalNote() As String
        Return Localization.T("Сверьте отпечаток с владельцем сервера обмена другим способом: сертификат закрепляется при первой регистрации.")
    End Function

    Friend Function LocalMissing() As String
        Return Localization.T("Сервер обмена этого ПК не установлен.")
    End Function

    Friend Function LocalStopped() As String
        Return Localization.T("Сервер обмена этого ПК не запущен: раздачи не смогут на нём зарегистрироваться.")
    End Function

    ' --- the new link (T-6) ---------------------------------------------------------------------

    Friend Function ReissueLabel() As String
        Return Localization.T("Новая ссылка для раздачи:")
    End Function

    Friend Function ReissueButton() As String
        Return Localization.T("Новая ссылка..")
    End Function

    Friend Function ReissueNote() As String
        Return Localization.T("Ссылка раздачи привязана к её имени: если переименовать раздачу, прежние ссылки через сервер обмена перестанут работать.")
    End Function

    Friend Function ReissueQuestion(name As String) As String
        Return Localization.TF("Выдать новую ссылку для раздачи «{0}»?", name) & vbCrLf & vbCrLf &
               Localization.T("Все выданные раньше QR-коды и файлы перестанут работать через сервер обмена. Прямые адреса (локальная сеть, IPv6, проброс порта) продолжат работать.")
    End Function

    ' --- results ----------------------------------------------------------------------------

    Friend Function Applied() As String
        Return Localization.T("Настройки обмена применены.")
    End Function

    Friend Function Reissued(name As String) As String
        Return Localization.TF("Для раздачи «{0}» выдана новая ссылка.", name)
    End Function

    ''' <summary>An unexpected failure: the generic sentence of the other settings actions, never the exception's
    ''' own text (APP-BEHAVIOUR rule 6) - the log has that.</summary>
    Friend Function Unexpected() As String
        Return Localization.T("Не удалось выполнить действие. Повторите попытку; если не выйдет - отправьте журнал автору: меню значка в трее - «Отправить журнал автору..».")
    End Function

    Friend Function ApplyFailed(reason As String) As String
        Return Localization.TF("Настройки обмена не применены: {0}", reason)
    End Function
End Module

''' <summary>The snapshot of everything the producer group shows, and the pure decision of what to show
''' (hidden rows, not disabled ones - the Hosting rule). No control is touched here.</summary>
Public Enum ExchangeLocalServer
    NotApplicable = 0
    ComponentMissing
    Stopped
    Running
End Enum

Public NotInheritable Class ExchangeProducerSnapshot
    Public Property Mode As ExchangeMode
    Public Property RegistrationEndpoint As String = ""
    Public Property ClientEndpoint As String = ""
    Public Property Worker As WorkerStatus
    Public Property LocalServer As ExchangeLocalServer = ExchangeLocalServer.NotApplicable
End Class

Public NotInheritable Class ExchangeProducerModel
    Public Property ShowRegistration As Boolean
    Public Property ShowClient As Boolean
    Public Property ShowPassword As Boolean
    Public Property ShowFingerprint As Boolean
    Public Property ShowShares As Boolean
    Public Property ShowReissue As Boolean
    Public Property ShowStartLocal As Boolean
    Public Property ShowState As Boolean
    ''' <summary>True until the worker holds an exchange configuration: a password is then required.</summary>
    Public Property PasswordRequired As Boolean
    Public Property StateLine As String = ""
    Public Property ClientNote As String = ""
    Public Property LocalNote As String = ""
    Public Property FingerprintNote As String = ""
    Public Property Fingerprint As String = ""
    Public Property SharesText As String = ""
    Public Property ReissueNames As New List(Of String)()

    Friend Shared Function Build(s As ExchangeProducerSnapshot) As ExchangeProducerModel
        Dim m As New ExchangeProducerModel()
        If s Is Nothing OrElse s.Mode = ExchangeMode.Off Then Return m

        m.ShowRegistration = s.Mode = ExchangeMode.External
        m.ShowClient = True
        m.ShowPassword = True
        m.ShowState = True
        Dim exchange As WorkerExchange = s.Worker?.Exchange
        m.PasswordRequired = exchange Is Nothing

        Select Case ExchangeEndpointRules.CheckClientEndpoint(s.ClientEndpoint)
            Case ExchangeEndpointVerdict.Empty : m.ClientNote = ExchangeProducerText.ClientEmptyNote()
            Case ExchangeEndpointVerdict.Malformed : m.ClientNote = ExchangeProducerText.Problem(ExchangeProducerProblem.ClientEndpointMalformed, "")
            Case ExchangeEndpointVerdict.Loopback : m.ClientNote = ExchangeProducerText.Problem(ExchangeProducerProblem.ClientEndpointLoopback, "")
        End Select

        If s.Mode = ExchangeMode.ThisPc Then
            Select Case s.LocalServer
                Case ExchangeLocalServer.ComponentMissing
                    m.LocalNote = ExchangeProducerText.LocalMissing()
                Case ExchangeLocalServer.Stopped
                    m.LocalNote = ExchangeProducerText.LocalStopped()
                    m.ShowStartLocal = True
            End Select
        End If

        Select Case ExchangeProducerRules.Readiness(s.Worker)
            Case ExchangeWorkerReadiness.NoWorker
                m.StateLine = ExchangeProducerText.WorkerSilent()
                Return m
            Case ExchangeWorkerReadiness.Stale
                m.StateLine = ExchangeProducerText.WorkerStale()
                Return m
        End Select

        If exchange Is Nothing OrElse Not exchange.Enabled Then
            m.StateLine = ExchangeProducerText.NotApplied()
            Return m
        End If

        m.Fingerprint = If(exchange.CertFingerprint, "")
        m.ShowFingerprint = m.Fingerprint.Length > 0
        If m.ShowFingerprint AndAlso s.Mode = ExchangeMode.External Then m.FingerprintNote = ExchangeProducerText.FingerprintExternalNote()

        Dim lines As New List(Of String)()
        Dim registered As Integer = 0
        Dim total As Integer = 0
        Dim firstNeedingUser As ExchangeShareState = ExchangeShareState.NotRegistered
        Dim anyNeedingUser As Boolean = False
        If exchange.Shares IsNot Nothing Then
            For Each share As WorkerExchangeShare In exchange.Shares
                If share Is Nothing OrElse String.IsNullOrEmpty(share.Name) Then Continue For
                total += 1
                Dim st As ExchangeShareState = ExchangeShareStates.Parse(share.State)
                If st = ExchangeShareState.Registered Then registered += 1
                If Not anyNeedingUser AndAlso ExchangeShareStates.NeedsUser(st) Then
                    anyNeedingUser = True
                    firstNeedingUser = st
                End If
                lines.Add(ExchangeProducerText.ShareLine(share.Name, st, share.RelayPort))
                If ExchangeShareIds.IsWellFormed(share.ShareId) Then m.ReissueNames.Add(share.Name)
            Next
        End If
        m.ShowShares = lines.Count > 0
        m.SharesText = String.Join(vbCrLf, lines)
        m.ShowReissue = m.ReissueNames.Count > 0

        If total = 0 Then
            m.StateLine = ExchangeProducerText.NoShares()
        Else
            m.StateLine = ExchangeProducerText.RegisteredCount(registered, total)
            If anyNeedingUser Then m.StateLine &= " " & ExchangeProducerText.Reason(firstNeedingUser)
        End If
        If Not String.IsNullOrWhiteSpace(exchange.LastError) Then m.StateLine &= " " & exchange.LastError.Trim()
        Return m
    End Function
End Class
