Option Strict On

' Spec 230: the words of the two clipboard functions, composed from CODES at the moment they are shown (the language the person
' has THEN, and no sentence ever stored). A failure's reason is a row of the common connection-message table (spec 232): this
' file adds the sentences of the functions themselves and borrows that table for the rest, so the same refusal reads the same
' in the sign-in dialog, the connection check and here.

Friend NotInheritable Class ClipboardMessages

    Private Sub New()
    End Sub

    ''' <summary>The sentence of one result line, in the current language.</summary>
    Public Shared Function TextOf(line As ClipboardLine) As String
        If line Is Nothing Then Return ""
        Select Case line.Code
            Case ClipboardLineCode.Sent
                Return Localization.TF("Отправлено на сервер: {0}", NameOrDash(line.Label))
            Case ClipboardLineCode.NothingToSend
                Return Localization.T("В буфере обмена нет текста, картинки или файла.")
            Case ClipboardLineCode.NotOneObject
                Return Localization.T("Один объект за раз: сначала упакуйте их в архив.")
            Case ClipboardLineCode.ClipboardBusy
                Return Localization.T("Буфер обмена занят другой программой, попробуйте ещё раз через секунду.")
            Case ClipboardLineCode.ClipboardUnreadable
                Return Localization.T("Не удалось прочитать буфер обмена.")
            Case ClipboardLineCode.NoServer
                Return Localization.T("Нет ни одного сервера обмена: подключите сервер в настройках.")
            Case ClipboardLineCode.Failure
                Return OnServer(line.Label, ReasonText(line.Reason))
            Case ClipboardLineCode.ServerEmpty
                Return OnServer(line.Label, ReasonText(If(line.Reason.Length > 0, line.Reason, "clipboard-empty")))
            Case ClipboardLineCode.ReceivedValue
                If line.Device.Length = 0 Then Return Localization.T("Буфер, полученный с сервера")
                Return Localization.TF("Буфер, полученный с сервера, устройство «{0}».", line.Device)
            Case ClipboardLineCode.ReceivedFile
                Return Localization.TF("Файл получен с сервера, устройство «{0}»: сохранён в папку «{1}» и лежит в буфере обмена.",
                                       NameOrDash(line.Device), line.Detail)
            Case ClipboardLineCode.OperationUnderWay
                Return Localization.T("Операция с буфером обмена уже идёт.")
            Case ClipboardLineCode.PlaceFailed
                Return Localization.T("Не удалось положить значение в буфер обмена.")
            Case ClipboardLineCode.SaveFailed
                Return Localization.T("Не удалось сохранить файл в папку для полученных файлов.")
            Case ClipboardLineCode.Incomplete
                ' A put names the server that did not confirm; a get names nobody and says nothing was placed.
                If line.Label.Length > 0 Then Return Localization.TF("Сервер {0} не подтвердил полученное значение.", line.Label)
                Return Localization.T("Значение пришло не полностью: в буфер обмена ничего не положено.")
            Case ClipboardLineCode.Cancelled
                Return Localization.T("Отменено.")
            Case ClipboardLineCode.NotOffered
                Return Localization.TF("Сервер {0} не принимает буфер обмена для этой учётной записи.", NameOrDash(line.Label))
            Case ClipboardLineCode.TooLarge
                Return Localization.T("Значение в буфере обмена слишком велико, чтобы его отправить.")
            Case ClipboardLineCode.ServerNoE2e
                Return OnServer(line.Label, ReasonText(ConnectionDiagnosisMessages.ServerNoE2e.Code))
            Case ClipboardLineCode.E2eMissing
                Return OnServer(line.Label, ReasonText(ConnectionDiagnosisMessages.E2eMissing.Code))
            Case ClipboardLineCode.KeyChanged
                Return OnServer(line.Label, ReasonText(ConnectionDiagnosisMessages.KeyChanged.Code))
            Case ClipboardLineCode.SealedFailed
                Return OnServer(line.Label, ReasonText(ConnectionDiagnosisMessages.SealedFailed.Code))
            Case ClipboardLineCode.JoinedLater
                ' Not an error: a device that joined after the value was sent is not in its box and reads the next one (item DN).
                Return OnServer(line.Label, Localization.T("Это значение отправлено до того, как это устройство присоединилось к аккаунту, и здесь его не открыть. Следующее значение откроется."))
            Case Else
                Return ""
        End Select
    End Function

    ''' <summary>One paragraph, a line per server and per outcome, in the order they were produced.</summary>
    Public Shared Function TextOf(result As ClipboardRunResult) As String
        If result Is Nothing Then Return ""
        Dim parts As New List(Of String)()
        For Each line As ClipboardLine In result.Lines
            Dim text As String = TextOf(line)
            If text.Length > 0 Then parts.Add(text)
        Next
        Return String.Join(Environment.NewLine, parts)
    End Function

    ''' <summary>"Sending the clipboard.. 42 %" - the progress of a large value.</summary>
    Public Shared Function ProgressText(isSend As Boolean, done As Long, total As Long) As String
        Dim percent As Integer = 0
        If total > 0 Then percent = CInt(Math.Min(100L, Math.Max(0L, done * 100L \ total)))
        If isSend Then Return Localization.TF("Отправка буфера обмена.. {0} %", percent)
        Return Localization.TF("Получение буфера обмена.. {0} %", percent)
    End Function

    ''' <summary>The row of the common connection-message table for a reason code, in the current language. A code the table does
    ''' not know reads as its generic refusal, never as the code itself.</summary>
    Public Shared Function ReasonText(code As String) As String
        Dim locale As String = Localization.CurrentCode
        Dim text As String = ConnectionDiagnosisLocalization.GetMessageText(code, locale)
        If text.Length = 0 OrElse String.Equals(text, code, StringComparison.Ordinal) Then
            text = ConnectionDiagnosisLocalization.GetMessageText(ConnectionDiagnosisMessages.Refused.Code, locale)
        End If
        Return text
    End Function

    Private Shared Function OnServer(label As String, reason As String) As String
        If String.IsNullOrEmpty(label) Then Return reason
        Return Localization.TF("Сервер {0}: {1}", label, reason)
    End Function

    Private Shared Function NameOrDash(name As String) As String
        Return If(String.IsNullOrWhiteSpace(name), "-", name)
    End Function

End Class
