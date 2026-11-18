Option Strict On

Imports FastMediaSorterCompanion.AppCommands

''' <summary>
''' Every sentence the Share Manager's command line says, in the UI language (ticket L0316). The refusals and reasons are the
''' shared ones of <see cref="AppCommandsText"/> (linked from the viewer's library) with this program's own name in the
''' "not running" sentence; the clipboard verbs' sentence is <see cref="ClipboardMessages"/>'s, never composed here. Words
''' live in the tables (Localization.AppCommands.vb, Localization.AppCommands.ShareManager.vb), never in the callers.
''' </summary>
Friend Module ShareManagerCommandText

    Friend Function ForRefusal(refusal As ParseRefusal) As String
        Return AppCommandsText.ForRefusal(refusal, ProductNames.ShareManager)
    End Function

    Friend Function ForReason(reason As String) As String
        Return AppCommandsText.ForReason(reason, ProductNames.ShareManager)
    End Function

    Friend Function OlderCopy() As String
        Return AppCommandsText.OlderCopy()
    End Function

    ''' <summary>Of a malformed folder or path: the one "bad value" sentence naming what was typed wrong.</summary>
    Friend Function BadValue(token As String) As String
        Return Localization.TF("Недопустимое значение параметра «{0}».", token)
    End Function

    Friend Function Ready(version As String) As String
        Return Localization.TF("Приложение {0} {1} запущено и готово.", ProductNames.ShareManager, version)
    End Function

    Friend Function WindowShown() As String
        Return Localization.T("Окно показано.")
    End Function

    Friend Function Closed() As String
        Return Localization.T("Приложение закрыто.")
    End Function

    Friend Function NotStarted() As String
        Return Localization.T("Приложение не было запущено.")
    End Function

    Friend Function WriteFailed() As String
        Return Localization.T("Не удалось записать файл результата.")
    End Function

    Friend Function Failed() As String
        Return Localization.T("Команда не выполнена.")
    End Function

    ' --- the observe verbs (ticket L0319) ---

    Friend Function SharedFolders(count As Integer) As String
        Return Localization.TF("Общих папок: {0}.", count)
    End Function

    Friend Function ScheduledOperations(count As Integer) As String
        Return Localization.TF("Запланированных операций: {0}.", count)
    End Function

    ' --- scheduled operation CLI verbs (ticket L0345) ---

    Friend Function ScheduleSaved(name As String, created As Boolean) As String
        If created Then Return Localization.TF("Операция по расписанию «{0}» создана.", name)
        Return Localization.TF("Операция по расписанию «{0}» обновлена.", name)
    End Function

    Friend Function ScheduleRemoved(name As String) As String
        Return Localization.TF("Операция по расписанию «{0}» удалена.", name)
    End Function

    Friend Function ScheduleNotFound(target As String) As String
        Return Localization.TF("Операция по расписанию «{0}» не найдена.", target)
    End Function

    Friend Function ScheduleAmbiguous(target As String) As String
        Return Localization.TF("Найдено несколько операций с именем «{0}». Укажите идентификатор.", target)
    End Function

    Friend Function ScheduleAlreadyRunning(name As String) As String
        If String.IsNullOrEmpty(name) Then Return Localization.T("Операция уже выполняется или ждёт очереди.")
        Return Localization.TF("Операция «{0}» уже выполняется или ждёт очереди.", name)
    End Function

    Friend Function ScheduleQueued(count As Integer) As String
        Return Localization.TF("Поставлено в очередь операций: {0}.", count)
    End Function

    Friend Function ScheduleRunCompleted(succeeded As Integer, failed As Integer) As String
        Return Localization.TF("Выполнено операций: {0}; с ошибками: {1}.", succeeded, failed)
    End Function

    Friend Function ScheduleHistoryEmpty() As String
        Return Localization.T("Журнал операций по расписанию пуст.")
    End Function

    Friend Function WorkerSilent() As String
        Return Localization.T("Служба общего доступа не отвечает.")
    End Function

    Friend Function ExchangeNotSetUp() As String
        Return Localization.T("Учётная запись обмена не настроена.")
    End Function

    ''' <summary>The configured account's line: the closed state token of <see cref="ShareManagerCommandTable"/> and the number of
    ''' requests that wait for the person.</summary>
    Friend Function ExchangeState(stateToken As String, pendingRequests As Integer) As String
        Return Localization.TF("Учётная запись обмена настроена. Состояние: {0}. Запросов в ожидании: {1}.", stateToken, pendingRequests)
    End Function

    ''' <summary>What became of a folder proposal (the closed tokens of <see cref="ShareManagerCommandTable"/>).</summary>
    Friend Function ForProposal(token As String) As String
        Select Case token
            Case ShareManagerCommandTable.ProposalDuplicate
                Return Localization.T("Эта папка уже ждёт ответа.")
            Case ShareManagerCommandTable.ProposalAlreadyShared
                Return Localization.T("Эта папка уже открыта для общего доступа.")
            Case ShareManagerCommandTable.ProposalFull
                Return Localization.T("Слишком много предложений ждёт ответа. Повторите позже.")
            Case Else
                Return Localization.T("Папка предложена: ответ даст пользователь в окне менеджера.")
        End Select
    End Function

End Module
