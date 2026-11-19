#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

' Every sentence the viewer's command profile says, in the UI language (Localization.AppCommands.vb).
' The pure library answers in reason tokens; this is where they become words.
Namespace AppCommands

    Friend Module AppCommandsText

        Friend Function SettingLabel() As String
            Return Localization.T("Разрешить команды от других программ")
        End Function

        ''' <summary>The product a "not running" sentence names: the viewer's compact name unless the caller is another
        ''' program of the profile (the Share Manager passes its own, ticket L0316).</summary>
        Private Function ProductOr(productName As String) As String
            Return If(String.IsNullOrEmpty(productName), ProductNames.Compact, productName)
        End Function

        Friend Function ForRefusal(refusal As ParseRefusal, Optional productName As String = Nothing) As String
            If refusal Is Nothing Then Return Localization.T("Команда не выполнена.")
            Dim token As String = If(refusal.Token, "?")
            Select Case refusal.Detail
                Case CommandLineParser.DetailUnknownVerb
                    Return Localization.TF("Неизвестная команда «{0}». Список команд: commands.", token)
                Case CommandLineParser.DetailUnknownOption
                    Return Localization.TF("Неизвестный параметр «{0}».", token)
                Case CommandLineParser.DetailMissingValue
                    Return Localization.TF("У параметра «{0}» нет значения.", token)
                Case CommandLineParser.DetailBadValue
                    Return Localization.TF("Недопустимое значение параметра «{0}».", token)
                Case CommandLineParser.DetailMissingArgument
                    Return Localization.TF("Не указан обязательный параметр «{0}».", token)
                Case CommandLineParser.DetailExtraValue
                    Return Localization.T("Лишнее значение в командной строке.")
                Case CommandLineParser.DetailRequestTooLarge, CommandLineParser.DetailArgumentTooLarge
                    Return Localization.T("Команда слишком длинная.")
                Case Else
                    Return ForReason(refusal.Reason, productName)
            End Select
        End Function

        Friend Function ForReason(reason As String, Optional productName As String = Nothing) As String
            Select Case reason
                Case CommandReasons.SwitchOff
                    Return Localization.TF("Команда отклонена: в настройках выключено «{0}».", SettingLabel())
                Case CommandReasons.Busy
                    Return Localization.T("Приложение занято открытым окном. Закройте его и повторите.")
                Case CommandReasons.NotRunning
                    Return Localization.TF("Приложение {0} не запущено.", ProductOr(productName))
                Case CommandReasons.Timeout
                    Return Localization.T("Нет ответа за отведённое время.")
                Case CommandReasons.Schema
                    Return Localization.T("Запущенная копия не понимает этот запрос. Обновите её.")
                Case CommandReasons.Elevation
                    Return Localization.T("Запущенная копия работает с правами администратора, а команда - без них.")
                Case CommandReasons.TooLarge
                    Return Localization.T("Команда слишком длинная.")
                Case CommandReasons.UnknownVerb
                    Return Localization.TF("Неизвестная команда «{0}». Список команд: commands.", "?")
                Case CommandReasons.NoSpace
                    Return Localization.T("Не хватает места для файла.")
                Case CommandReasons.OperationFailed, CommandReasons.ResponseTooLarge
                    Return Localization.T("Команда не выполнена.")
                Case Else
                    Return Localization.T("Команда не выполнена.")
            End Select
        End Function

        ''' <summary>Item N.1: <c>quit</c> refused because file operations run; names what runs (no path, no value, rule 7)
        ''' and the option that consents.</summary>
        Friend Function QuitWhileJobsRun() As String
            Return Localization.TF("Выполняются файловые операции. Чтобы закрыть приложение и остановить их, добавьте параметр «{0}».", "--stop-jobs")
        End Function

        Friend Function OlderCopy() As String
            Return Localization.T("Запущена более старая копия или 32-разрядная сборка, она не понимает команды. Закройте её и повторите.")
        End Function

    End Module

End Namespace
#End If
