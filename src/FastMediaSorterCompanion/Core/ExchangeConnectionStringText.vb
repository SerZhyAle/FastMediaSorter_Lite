Option Strict On

' Ticket 187 T-2: the words of the operator's "Get connection string" action. One sentence per state and per refusal; the
' product name never appears here. House style: ".." for an ellipsis, a plain hyphen, ё in Russian. The tables are
' Localization.ExchangeString.vb (13 languages).

Friend NotInheritable Class ExchangeConnectionStringText
    Private Sub New()
    End Sub

    Friend Shared Function GetButton() As String
        Return Localization.T("Получить строку подключения..")
    End Function

    Friend Shared Function WindowTitle() As String
        Return Localization.T("Строка подключения к серверу обмена")
    End Function

    Friend Shared Function Intro() As String
        Return Localization.T("Эту строку передают тем, кто будет открывать ресурсы с этого сервера: в программе просмотра они выбирают «Добавить с сервера обмена..» и вставляют её. Пароля в строке нет - пароль учётной записи передаётся отдельно.")
    End Function

    Friend Shared Function NameLabel() As String
        Return Localization.T("Название сервера (необязательно):")
    End Function

    Friend Shared Function LoginLabel() As String
        Return Localization.T("Для этого логина (необязательно):")
    End Function

    Friend Shared Function LoginNote() As String
        Return Localization.T("Если указать логин, он подставится в форму входа. Пароль в строку не попадает никогда.")
    End Function

    Friend Shared Function StringLabel() As String
        Return Localization.T("Строка подключения:")
    End Function

    Friend Shared Function CopyButton() As String
        Return Localization.T("Скопировать строку")
    End Function

    Friend Shared Function SaveButton() As String
        Return Localization.T("Сохранить в файл..")
    End Function

    Friend Shared Function Copied() As String
        Return Localization.T("Строка скопирована.")
    End Function

    Friend Shared Function SaveFilter() As String
        Return Localization.T("Строка подключения (*.fmsexch)|*.fmsexch")
    End Function

    ''' <summary>The hint under the disabled button: no string is made from a guess.</summary>
    Friend Shared Function NoEndpoint() As String
        Return Localization.T("Сначала задайте адрес для клиентов: без него строку подключения составить нельзя.")
    End Function

    Friend Shared Function Refused() As String
        Return Localization.T("Строку не удалось составить: название сервера - не более 64 символов, логин - от 3 до 64 знаков (a-z, 0-9, точка, подчёркивание, дефис).")
    End Function

    Friend Shared Function NoCertificate() As String
        Return Localization.T("Строку не удалось составить: у сервера ещё нет сертификата. Запустите сервер один раз.")
    End Function

    Friend Shared Function Failed() As String
        Return Localization.T("Строку не удалось составить: сервер обмена не ответил.")
    End Function

    ''' <summary>The sentence for an outcome of the command.</summary>
    Friend Shared Function ForOutcome(outcome As ExchangeProcess.ConnectionStringOutcome) As String
        Select Case outcome
            Case ExchangeProcess.ConnectionStringOutcome.Refused : Return Refused()
            Case ExchangeProcess.ConnectionStringOutcome.ConfigProblem : Return NoCertificate()
            Case Else : Return Failed()
        End Select
    End Function
End Class
