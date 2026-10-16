Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Globalization

' The words of scheduled file operations (ticket 222, section 7.6).
'
' Everything a person reads about an operation is composed here, at display time, from codes and numbers
' the engine stored - which is what lets a history written in one language be read in another, and keeps
' every sentence in the Share Manager's own string tables (13 languages, same change, parity tests).
' Nothing here reads a file or the registry; it is pure over its arguments, so it is held by a test.
Friend Module ScheduledText

    ' --- the vocabulary --------------------------------------------------------------------------------

    Friend Function ActionName(action As ScheduledAction) As String
        Select Case action
            Case ScheduledAction.Move : Return Localization.T("Перенос")
            Case ScheduledAction.Delete : Return Localization.T("Удаление")
            Case ScheduledAction.Archive : Return Localization.T("Архивация")
            Case Else : Return Localization.T("Копирование")
        End Select
    End Function

    Friend Function ConflictName(mode As ScheduledConflict) As String
        Select Case mode
            Case ScheduledConflict.Overwrite : Return Localization.T("Перезаписать")
            Case ScheduledConflict.KeepBoth : Return Localization.T("Оставить обе копии")
            Case Else : Return Localization.T("Пропустить")
        End Select
    End Function

    Friend Function TimeFilterName(filter As ScheduledTimeFilter) As String
        Select Case filter
            Case ScheduledTimeFilter.SinceLastRun : Return Localization.T("Новые с прошлого запуска")
            Case ScheduledTimeFilter.LastHour : Return Localization.T("За последний час")
            Case ScheduledTimeFilter.LastDay : Return Localization.T("За последние сутки")
            Case Else : Return Localization.T("За всё время")
        End Select
    End Function

    Friend Function MissedPolicyName(policy As MissedRunPolicy) As String
        Return If(policy = MissedRunPolicy.Skip,
                  Localization.T("Пропустить"),
                  Localization.T("Выполнить один раз при запуске менеджера"))
    End Function

    ' --- the schedule ----------------------------------------------------------------------------------

    ''' <summary>"6 h", "1 h 30 min", "15 min" - an interval in the shortest honest words.</summary>
    Friend Function IntervalText(minutes As Integer) As String
        Dim total As Integer = ScheduleMath.NormalizeInterval(minutes)
        Dim hours As Integer = total \ 60
        Dim rest As Integer = total Mod 60
        If hours = 0 Then Return Localization.TF("{0} мин", rest)
        If rest = 0 Then Return Localization.TF("{0} ч", hours)
        Return Localization.TF("{0} ч {1} мин", hours, rest)
    End Function

    Friend Function TimeOfDay(anchorMinutes As Integer) As String
        Dim wrapped As Integer = ScheduleMath.NormalizeAnchor(anchorMinutes)
        Return (wrapped \ 60).ToString("00", CultureInfo.InvariantCulture) & ":" & (wrapped Mod 60).ToString("00", CultureInfo.InvariantCulture)
    End Function

    ''' <summary>"daily at 03:00", "every 6 h from 03:00", "every 2 days at 03:00".</summary>
    Friend Function ScheduleText(op As ScheduledOperation) As String
        Dim interval As Integer = ScheduleMath.NormalizeInterval(op.IntervalMinutes)
        Dim at As String = TimeOfDay(op.AnchorMinutes)
        If interval = ScheduleMath.MinutesPerDay Then Return Localization.TF("ежедневно в {0}", at)
        If interval Mod ScheduleMath.MinutesPerDay = 0 Then
            Return Localization.TF("каждые {0} дн. в {1}", interval \ ScheduleMath.MinutesPerDay, at)
        End If
        Return Localization.TF("каждые {0} с {1}", IntervalText(interval), at)
    End Function

    ''' <summary>A wall-clock time for a row: the date only when it is not today.</summary>
    Friend Function WhenText(value As DateTime?, today As DateTime) As String
        If Not value.HasValue Then Return "-"
        Dim v As DateTime = value.Value
        Return If(v.Date = today.Date,
                  v.ToString("t", CultureInfo.CurrentCulture),
                  v.ToString("g", CultureInfo.CurrentCulture))
    End Function

    ''' <summary>The autostart setting's state with the warning that goes with it (D2): the schedule works only
    ''' while the Share Manager runs. Nothing here switches the setting; the editor and the page both say it.</summary>
    Friend Function AutostartLine(enabled As Boolean, paused As Boolean) As String
        Dim text As String = If(enabled,
            Localization.T("Менеджер запускается при входе в Windows. Расписание работает, пока менеджер запущен."),
            Localization.T("Менеджер не запускается при входе в Windows, поэтому расписание работает только пока он открыт. Включить автозапуск можно в настройках менеджера."))
        If paused Then text &= " " & Localization.T("Расписание приостановлено.")
        Return text
    End Function

    ' --- failures and notes ----------------------------------------------------------------------------

    ''' <summary>Splits "code|argument".</summary>
    Friend Sub SplitDetail(detail As String, ByRef code As String, ByRef argument As String)
        Dim text As String = If(detail, "")
        Dim bar As Integer = text.IndexOf("|"c)
        If bar < 0 Then
            code = text
            argument = ""
        Else
            code = text.Substring(0, bar)
            argument = text.Substring(bar + 1)
        End If
    End Sub

    ''' <summary>A recorded failure or wall detail as a sentence, in the language of now.</summary>
    Friend Function DescribeDetail(detail As String) As String
        Dim code As String = ""
        Dim argument As String = ""
        SplitDetail(detail, code, argument)
        If code.Length = 0 Then Return ""
        Select Case code
            Case "locked" : Return Localization.TF("файл занят другой программой: {0}", argument)
            Case "denied" : Return Localization.TF("нет доступа: {0}", argument)
            Case "missing" : Return Localization.TF("файл не найден: {0}", argument)
            Case "changed" : Return Localization.TF("файл изменился, пока шла работа: {0}", argument)
            Case "exists" : Return Localization.TF("имя уже занято: {0}", argument)
            Case "invalid" : Return Localization.TF("недопустимое имя или путь: {0}", argument)
            Case "mismatch" : Return Localization.TF("копия не совпала с оригиналом: {0}", argument)
            Case "memory" : Return Localization.TF("не хватило памяти: {0}", argument)
            Case "kept-both" : Return Localization.TF("скопирован, но исходный файл остался на месте: {0}", argument)
            Case "transport" : Return Localization.TF("связь с папкой прервалась: {0}", argument)
            Case "disk-full" : Return Localization.TF("на диске не хватило места: {0}", argument)
            Case "archive-unverified" : Return Localization.T("архив не прошёл проверку, исходные файлы оставлены на месте")
            Case "source-unreachable" : Return Localization.TF("исходная папка недоступна: {0}", argument)
            Case "source-denied" : Return Localization.TF("нет доступа к исходной папке: {0}", argument)
            Case "destination-unreachable" : Return Localization.TF("папка назначения недоступна: {0}", argument)
            Case "destination-denied" : Return Localization.TF("нет доступа к папке назначения: {0}", argument)
            Case "source-no-credential", "destination-no-credential"
                Return Localization.TF("для ресурса не сохранён пароль: {0}", argument)
            Case "source-key-not-pinned", "destination-key-not-pinned"
                Return Localization.TF("ключ сервера не подтверждён: {0}", argument)
            Case "source-device-revoked", "destination-device-revoked"
                Return Localization.TF("устройство отозвано: {0}", argument)
            Case "source-exchange-certificate", "destination-exchange-certificate"
                Return Localization.TF("сертификат сервера обмена изменился: {0}", argument)
            Case "source-signed-out", "destination-signed-out"
                Return Localization.TF("выполнен выход из учётной записи обмена: {0}", argument)
            Case "source-pin-locked", "destination-pin-locked"
                Return Localization.TF("требуется PIN-код или он изменился: {0}", argument)
            Case "source-offline", "destination-offline"
                Return Localization.TF("устройство сейчас не в сети: {0}", argument)
            Case "resource-removed" : Return Localization.TF("ресурс удалён из списка ресурсов: {0}", argument)
            Case "invalid-definition" : Return Localization.T("определение не читается этой версией менеджера")
            Case Else
                If code.StartsWith("issue-", StringComparison.Ordinal) Then Return IssueSentence(code, argument)
                Return Localization.TF("не удалось обработать: {0}", argument)
        End Select
    End Function

    ''' <summary>"issue-DestinationInsideSource|D:\x" is a refusal; the code names the rule, the argument the place.</summary>
    Private Function IssueSentence(code As String, argument As String) As String
        Dim parsed As ScheduledIssueCode
        If [Enum].TryParse(code.Substring("issue-".Length), parsed) Then
            Return IssueText(New ScheduledIssue(parsed, argument))
        End If
        Return Localization.T("определение не прошло проверку")
    End Function

    Friend Function IssueText(issue As ScheduledIssue) As String
        Select Case issue.Code
            Case ScheduledIssueCode.SourceMissing : Return Localization.T("Укажите папку-источник.")
            Case ScheduledIssueCode.DestinationMissing : Return Localization.T("Укажите папку назначения.")
            Case ScheduledIssueCode.SourceNotAbsolute
                Return Localization.T("Источник должен быть полным путём: диск:\папка или \\сервер\ресурс\папка.")
            Case ScheduledIssueCode.DestinationNotAbsolute
                Return Localization.T("Назначение должно быть полным путём: диск:\папка или \\сервер\ресурс\папка.")
            Case ScheduledIssueCode.DestinationInsideSource
                Return Localization.T("Папка назначения совпадает с источником или лежит внутри него.")
            Case ScheduledIssueCode.SourceProtected
                Return Localization.TF("Системные и программные папки нельзя использовать как источник: {0}", issue.Detail)
            Case ScheduledIssueCode.DestinationProtected
                Return Localization.TF("Системные и программные папки нельзя использовать как место назначения: {0}", issue.Detail)
            Case ScheduledIssueCode.TypeMaskEmpty : Return Localization.T("Выберите хотя бы один тип файлов.")
            Case ScheduledIssueCode.AgeBoundsContradict
                Return Localization.T("Возраст «младше» должен быть больше возраста «старше».")
            Case ScheduledIssueCode.SizeBoundsContradict
                Return Localization.T("Размер «меньше» должен быть больше размера «больше».")
            Case ScheduledIssueCode.ArchiveNeedsPaths
                Return Localization.T("Архив пока принимает только обычные папки, не ресурсы.")
            Case ScheduledIssueCode.NegativeBound : Return Localization.T("Значения условий не могут быть отрицательными.")
            Case ScheduledIssueCode.ResourceReadOnly
                Return Localization.T("Ресурс только для чтения: перенос и удаление в нём недоступны.")
            Case ScheduledIssueCode.ResourcePinLocked : Return Localization.T("Корень ресурса защищён PIN-кодом.")
            Case ScheduledIssueCode.ResourceMissing : Return Localization.T("Ресурс не найден в списке ресурсов.")
            Case ScheduledIssueCode.SameResourceFolder
                Return Localization.T("Источник и назначение - одна и та же папка ресурса.")
            Case ScheduledIssueCode.ResourceFolderOutsideRoot
                Return Localization.TF("Папка должна лежать внутри корня ресурса: {0}", issue.Detail)
            Case ScheduledIssueCode.DefinitionUnreadable
                Dim fragment As String = Localization.T("определение не читается этой версией менеджера")
                Return If(fragment.Length > 0, Char.ToUpperInvariant(fragment(0)) & fragment.Substring(1) & ".", fragment)
            Case ScheduledIssueCode.ResourceUnsupported
                Return Localization.T("Этот вид ресурса (ссылка обмена) пока нельзя использовать в операциях по расписанию.")
            Case ScheduledIssueCode.DriveBothSides
                Return Localization.T("Google Диск нельзя выбрать одновременно как источник и как назначение.")
            Case ScheduledIssueCode.DriveDeleteUnsupported
                Return Localization.T("Удаление по расписанию через Google Диск не поддерживается.")
            Case Else : Return Localization.T("Определение не прошло проверку.")
        End Select
    End Function

    ' --- delete verdicts (D3) --------------------------------------------------------------------------

    ''' <summary>"to the Recycle Bin" or "permanently - a network drive has no Recycle Bin": what a deletion
    ''' in a place does, by the viewer's rule.</summary>
    Friend Function VerdictText(decision As DeleteDecision) As String
        If decision Is Nothing OrElse Not decision.IsPermanent Then Return Localization.T("в Корзину")
        Return Localization.TF("безвозвратно - {0}", ReasonText(decision.Reason))
    End Function

    Friend Function ReasonText(reason As PermanentReason) As String
        Select Case reason
            Case PermanentReason.NoBinOnNetwork : Return Localization.T("на сетевом диске Корзины нет")
            Case PermanentReason.NoBinOnRemovable : Return Localization.T("на съёмном носителе Корзины нет")
            Case PermanentReason.BinDisabledOnVolume : Return Localization.T("Корзина отключена для этого диска")
            Case PermanentReason.FileExceedsBinQuota : Return Localization.T("файл больше, чем вмещает Корзина")
            Case PermanentReason.VolumeUnknown : Return Localization.T("это расположение не поддерживает Корзину")
            Case Else : Return Localization.T("в настройках просмотрщика Корзина выключена")
        End Select
    End Function

    ''' <summary>The recorded verdict of a run ("recycle", "permanent:NoBinOnNetwork", "mixed") as words.</summary>
    Friend Function RecordedVerdictText(verdict As String) As String
        If String.IsNullOrEmpty(verdict) Then Return ""
        If verdict = "recycle" Then Return Localization.T("в Корзину")
        If verdict = "mixed" Then Return Localization.T("частично в Корзину, частично безвозвратно")
        If verdict = "permanent:BinDidNotTakeIt" Then
            Return Localization.TF("безвозвратно - {0}", Localization.T("Корзина не приняла файл"))
        End If
        If verdict.StartsWith("permanent:", StringComparison.Ordinal) Then
            Dim reason As PermanentReason
            If [Enum].TryParse(verdict.Substring("permanent:".Length), reason) Then
                Return Localization.TF("безвозвратно - {0}", ReasonText(reason))
            End If
        End If
        Return ""
    End Function

    ' --- a row of the page -----------------------------------------------------------------------------

    ''' <summary>The operation's name cell on the page: prefixed with "! " while a run was skipped since the last success.</summary>
    Friend Function RowTitleText(op As ScheduledOperation) As String
        Return If(op.Record.SkippedSinceSuccess, "! " & op.DisplayName(), op.DisplayName())
    End Function

    Friend Function ShortLocation(location As ScheduledLocation) As String
        Return location.Describe()
    End Function

    ''' <summary>"source -> target", or just the source for a delete.</summary>
    Friend Function WhereText(op As ScheduledOperation) As String
        If Not op.NeedsDestination Then Return ShortLocation(op.Source)
        Return ShortLocation(op.Source) & "  ->  " & ShortLocation(op.Destination)
    End Function

    ''' <summary>The "last result" column. A failed run says to open the history (7.1).</summary>
    Friend Function LastResultText(state As ScheduledOperationState) As String
        If state.IsRunning Then Return Localization.T("Выполняется..")
        If state.IsQueued Then Return Localization.T("Ждёт очереди")
        Dim op As ScheduledOperation = state.Operation
        Select Case op.Halt
            Case ScheduledHalt.Wall
                Return Localization.TF("Остановлена: {0}", DescribeDetail(op.HaltDetail))
            Case ScheduledHalt.ResourceRemoved
                Return Localization.TF("Остановлена: {0}", DescribeDetail("resource-removed|" & op.HaltDetail))
            Case ScheduledHalt.InvalidDefinition
                Return Localization.TF("Остановлена: {0}", DescribeDetail("invalid-definition"))
        End Select
        Dim record As ScheduledRunRecord = op.Record
        ' The exclamation mark of a skipped run is on the name cell (RowTitleText), once; the result column only words the last run.
        Select Case record.LastOutcome
            Case ScheduledRunOutcome.None : Return Localization.T("Ещё не запускалась")
            Case ScheduledRunOutcome.Ok : Return Localization.TF("Выполнено: {0} файл(ов)", record.LastFileCount)
            Case ScheduledRunOutcome.Skipped : Return Localization.T("Пропущена: устройство не в сети")
            Case ScheduledRunOutcome.Cancelled : Return Localization.T("Прервана")
            Case ScheduledRunOutcome.Refused : Return Localization.T("Не запущена: определение не прошло проверку")
            Case Else : Return Localization.T("Последний запуск не удался. Откройте журнал, чтобы увидеть подробности.")
        End Select
    End Function

    ' --- the history -----------------------------------------------------------------------------------

    Friend Function TriggerText(trigger As String) As String
        Select Case trigger
            Case "runNow" : Return Localization.T("Вручную")
            Case "runAll" : Return Localization.T("Запустить все")
            Case "catchUp" : Return Localization.T("С опозданием")
            Case Else : Return Localization.T("По расписанию")
        End Select
    End Function

    ''' <summary>One history row's result column, from the persisted entry.</summary>
    Friend Function HistoryResultText(entry As JournalEntry) As String
        If entry.Kind = "note" Then
            Select Case entry.Code
                Case "missed" : Return Localization.TF("Пропущено: время запуска ({0}) прошло, пока менеджер был закрыт", WhenOf(entry.Detail))
                Case "late" : Return Localization.TF("Выполняется с опозданием: запуск был назначен на {0}", WhenOf(entry.Detail))
                Case "busy" : Return Localization.T("Отклонено: эта операция уже выполняется или ждёт очереди")
                Case "resumed" : Return Localization.T("Возобновлена: причина остановки устранена")
                Case Else : Return entry.Code
            End Select
        End If
        Select Case entry.Code
            Case "ok"
                Dim text As String = If(entry.Skipped > 0,
                    Localization.TF("Выполнено: {0} файл(ов), пропущено: {1}", entry.Count, entry.Skipped),
                    Localization.TF("Выполнено: {0} файл(ов)", entry.Count))
                Dim verdict As String = RecordedVerdictText(entry.Verdict)
                If verdict.Length > 0 Then text &= " (" & verdict & ")"
                Return text
            Case "failed"
                Dim text As String = Localization.TF("С ошибками: {0}; выполнено: {1} файл(ов)", entry.Failed, entry.Count)
                Dim first As String = DescribeDetail(entry.Detail)
                If first.Length > 0 Then text &= ". " & first
                Return text
            Case "skipped" : Return Localization.TF("Пропущено: {0}", DescribeDetail(entry.Detail))
            Case "wall" : Return Localization.TF("Остановлено: {0}", DescribeDetail(entry.Detail))
            Case "refused" : Return Localization.TF("Не запущено: {0}", DescribeDetail(entry.Detail))
            Case "cancelled" : Return Localization.T("Прервано")
            Case Else : Return entry.Code
        End Select
    End Function

    Private Function WhenOf(isoTime As String) As String
        Dim value As DateTime? = ScheduledJson.ParseTime(isoTime)
        Return If(value.HasValue, value.Value.ToString("g", CultureInfo.CurrentCulture), isoTime)
    End Function

    ''' <summary>The tray item's hint: the last failure in one line.</summary>
    Friend Function FailureHint(info As ScheduledFailureInfo) As String
        If info Is Nothing Then Return ""
        Dim reason As String = DescribeDetail(info.Detail)
        Return If(reason.Length > 0,
                  Localization.TF("{0}: {1}", info.OperationName, reason),
                  Localization.TF("{0}: последний запуск не удался", info.OperationName))
    End Function

End Module
