Option Strict On

Imports System.Linq

' Ticket 170 T-2/T-4/T-6: the words and the pure decision of what the producer rows show. The group is built by
' Share_Settings_Form.ExchangeProducer.vb; nothing here touches a control. A row is HIDDEN or shown by state - never disabled.
'
' One sentence per state and per refusal, never the raw reason (an unknown reason has the generic one). The product name never
' appears in these words. House style: ".." for an ellipsis, a plain hyphen, ё in Russian.

Friend NotInheritable Class ExchangePublishText
    Private Sub New()
    End Sub

    Friend Shared Function GroupTitle() As String
        Return Localization.T("Мои папки в учётной записи обмена")
    End Function

    Friend Shared Function PublishLabel() As String
        Return Localization.T("Публиковать мои общие папки в моей учётной записи обмена")
    End Function

    ''' <summary>Rule 1: in words that say what it does - the credential of the whole server travels with any one folder.</summary>
    Friend Shared Function PublishNote() As String
        Return Localization.T("После включения каждое устройство вашей учётной записи сможет открыть все ваши общие папки: у раздачи один логин, и доступ к любой из папок открывает весь сервер. Деление на папки - удобство, а не замок.")
    End Function

    Friend Shared Function ThirdPartyLabel() As String
        Return Localization.T("Пускать через сервер другие SFTP-программы (WinSCP, FileZilla) к моим папкам")
    End Function

    Friend Shared Function ThirdPartyNote() As String
        Return Localization.T("Это открывает общий TCP-порт на компьютере оператора сервера. Включайте, только если это нужно: подключиться сможет каждый, у кого есть адрес и пароль.")
    End Function

    Friend Shared Function ThirdPartyEndpointLine(endpoint As String) As String
        Return Localization.TF("Адрес для WinSCP и FileZilla: {0}", endpoint)
    End Function

    Friend Shared Function ThirdPartyWaiting() As String
        Return Localization.T("Сервер ещё не выдал порт.")
    End Function

    Friend Shared Function CopyEndpointButton() As String
        Return Localization.T("Скопировать адрес")
    End Function

    Friend Shared Function EndpointCopied() As String
        Return Localization.T("Адрес скопирован.")
    End Function

    Friend Shared Function ColumnFolder() As String
        Return Localization.T("Папка")
    End Function

    Friend Shared Function ColumnLink() As String
        Return Localization.T("Ссылка")
    End Function

    Friend Shared Function LinkCreatedCell() As String
        Return Localization.T("создана")
    End Function

    Friend Shared Function LinkTitle() As String
        Return Localization.T("Ссылка для человека вне учётной записи")
    End Function

    Friend Shared Function LinkNote() As String
        ' Ticket 239 CR-13: while the outward-access gate is closed the note is the reason, not a promise the link cannot keep.
        If Not OutwardAccessFeatures.IsOffered() Then Return OutwardAccessFeatures.Reason()
        Return Localization.T("Ссылка работает без учётной записи, через сервер обмена: у кого она есть, тот открывает эту папку. «Новая ссылка» делает прежние QR-коды и файлы бесполезными, «Удалить ссылку» убирает её совсем.")
    End Function

    Friend Shared Function CreateLinkButton() As String
        Return Localization.T("Создать ссылку..")
    End Function

    Friend Shared Function NewLinkButton() As String
        Return Localization.T("Новая ссылка..")
    End Function

    Friend Shared Function RemoveLinkButton() As String
        Return Localization.T("Удалить ссылку")
    End Function

    Friend Shared Function RotateQuestion(name As String) As String
        Return Localization.TF("Сделать новую ссылку для «{0}»? Все прежние QR-коды и файлы с этой ссылкой перестанут работать.", name)
    End Function

    Friend Shared Function RemoveQuestion(name As String) As String
        Return Localization.TF("Удалить ссылку для «{0}»? Все выданные QR-коды и файлы с ней перестанут работать.", name)
    End Function

    Friend Shared Function LinkCreated(name As String) As String
        Return Localization.TF("Ссылка для «{0}» создана.", name)
    End Function

    Friend Shared Function LinkRotated(name As String) As String
        Return Localization.TF("Ссылка для «{0}» заменена.", name)
    End Function

    Friend Shared Function LinkRemoved(name As String) As String
        Return Localization.TF("Ссылка для «{0}» удалена.", name)
    End Function

    ''' <summary>One sentence for a link action that did not work.</summary>
    Friend Shared Function LinkFailure(result As ExchangeLinkResult) As String
        If result.Refusal = ExchangeRefusal.ProtectionUnsupported Then Return Localization.T("заблокировано: защита не поддерживается")
        Select Case result.Outcome
            Case ExchangeLinkOutcome.NotPublished
                Return Localization.T("Эта папка сейчас не опубликована, поэтому ссылку сделать нельзя.")
            Case ExchangeLinkOutcome.NotConnected
                Return Localization.T("Нет связи с сервером обмена, ссылку сделать нельзя.")
            Case ExchangeLinkOutcome.NoEndpoint
                Return Localization.T("Адрес сервера обмена нельзя передавать людям вне учётной записи (он пустой или указывает на этот же компьютер).")
            Case Else
                Return Localization.T("Сервер не принял ссылку.")
        End Select
    End Function

    ' --- the link window ---

    Friend Shared Function LinkWindowTitle(name As String) As String
        Return Localization.TF("Ссылка для «{0}»", name)
    End Function

    Friend Shared Function LinkWindowNote() As String
        Return Localization.T("Передайте человеку код или файл. В них есть доступ к папке: не публикуйте их.")
    End Function

    Friend Shared Function ShowQrButton() As String
        Return Localization.T("Показать QR-код")
    End Function

    Friend Shared Function CopyLinkTextButton() As String
        Return Localization.T("Копировать текст ссылки")
    End Function

    Friend Shared Function LinkTextCopied() As String
        Return Localization.T("Текст ссылки скопирован.")
    End Function

    Friend Shared Function SaveFileButton() As String
        Return Localization.T("Сохранить файл .fmscfg..")
    End Function

    Friend Shared Function SaveFilter() As String
        Return Localization.T("Файл настроек (*.fmscfg)|*.fmscfg")
    End Function

    Friend Shared Function FileSaved(path As String) As String
        Return Localization.TF("Файл сохранён: {0}", path)
    End Function

    Friend Shared Function FileNotSaved() As String
        Return Localization.T("Не удалось сохранить файл.")
    End Function

    Friend Shared Function QrTooLarge() As String
        Return Localization.T("Код слишком велик для QR - используйте файл.")
    End Function

    Friend Shared Function NoQr() As String
        Return Localization.T("QR-код сейчас недоступен.")
    End Function

    ' --- the state lines ---

    Friend Shared Function StateNotConnected() As String
        Return Localization.T("Нет связи с сервером обмена. Папки появятся в учётной записи, когда связь восстановится.")
    End Function

    Friend Shared Function StateBlocked(block As ExchangePublishBlock) As String
        Select Case block
            Case ExchangePublishBlock.ServerDown
                Return Localization.T("Не опубликовано: раздача не запущена или не отвечает.")
            Case ExchangePublishBlock.PinLocked
                Return Localization.T("Не опубликовано: сервер раздачи не подтвердил защиту папки PIN-кодом. Обновите сервер и примените настройки папки.")
            Case ExchangePublishBlock.NoAddress
                Return Localization.T("Не опубликовано: нет адреса этого компьютера, который можно передать другим устройствам.")
            Case ExchangePublishBlock.NoShares
                Return Localization.T("Пока нечего публиковать: нет общих папок.")
            Case ExchangePublishBlock.ServerNoE2e
                ' the whole row of item DR, in the language of the window (no new string)
                Return ClipboardMessages.ReasonText(ConnectionDiagnosisMessages.ServerNoE2e.Code)
            Case ExchangePublishBlock.NoOwnKey
                Return ClipboardMessages.ReasonText(ConnectionDiagnosisMessages.E2eMissing.Code)
            Case Else
                Return ""
        End Select
    End Function

    Friend Shared Function StatePublished(published As Integer, total As Integer) As String
        Return Localization.TF("Опубликовано папок: {0} из {1}.", published, total)
    End Function

    Friend Shared Function RowState(row As ExchangePublishRow) As String
        Select Case row.State
            Case ExchangePublishState.Published
                Return Localization.T("опубликована") & If(row.PinProtected, " · " & Localization.T("защищено PIN-кодом"), "")
            Case ExchangePublishState.Refused
                Return Refusal(row.Refusal)
            Case Else
                Return Localization.T("ожидает")
        End Select
    End Function

    ''' <summary>One sentence per refusal of a publish, never the raw reason.</summary>
    Friend Shared Function Refusal(reason As ExchangeRefusal) As String
        Select Case reason
            Case ExchangeRefusal.ProtectionUnsupported
                Return Localization.T("заблокировано: защита не поддерживается")
            Case ExchangeRefusal.Capacity
                Return Localization.T("сервер не принял: достигнут предел списка")
            Case ExchangeRefusal.IdCollision
                Return Localization.T("сервер не принял: идентификатор занят")
            Case ExchangeRefusal.PortUnavailable
                Return Localization.T("сервер не выдал внешний порт")
            Case Else
                Return Localization.T("сервер отклонил")
        End Select
    End Function

End Class

''' <summary>What the producer rows show, decided from the facts (a pure value; the form only draws it).</summary>
Friend NotInheritable Class ExchangePublishModel

    ''' <summary>The whole group exists only for a configured account (no account, no row - the dormant rule).</summary>
    Public ShowGroup As Boolean
    Public PublishChecked As Boolean
    Public ThirdPartyChecked As Boolean
    ''' <summary>The third-party option is offered only while publication is on (it rides on a published resource).</summary>
    Public ShowThirdParty As Boolean
    Public StateLine As String = ""
    Public ShowEndpoint As Boolean
    Public EndpointLine As String = ""
    Public ShowShares As Boolean
    Public ShowLinkBlock As Boolean

    Friend Shared Function Build(configured As Boolean, online As Boolean, publish As Boolean, thirdParty As Boolean, snapshot As ExchangePublishSnapshot) As ExchangePublishModel
        Dim m As New ExchangePublishModel()
        If Not configured Then Return m
        m.ShowGroup = True
        m.PublishChecked = publish
        m.ThirdPartyChecked = thirdParty
        m.ShowThirdParty = publish
        If Not publish Then Return m

        If Not online Then
            m.StateLine = ExchangePublishText.StateNotConnected()
        ElseIf snapshot.Block <> ExchangePublishBlock.None AndAlso snapshot.Block <> ExchangePublishBlock.Off Then
            m.StateLine = ExchangePublishText.StateBlocked(snapshot.Block)
        ElseIf snapshot.Block = ExchangePublishBlock.None Then
            Dim published As Integer = snapshot.Rows.Where(Function(r) r.State = ExchangePublishState.Published).Count()
            m.StateLine = ExchangePublishText.StatePublished(published, snapshot.Rows.Count)
        End If

        If thirdParty AndAlso online AndAlso snapshot.Block = ExchangePublishBlock.None Then
            m.ShowEndpoint = True
            m.EndpointLine = If(snapshot.ThirdPartyEndpoint.Length > 0, ExchangePublishText.ThirdPartyEndpointLine(snapshot.ThirdPartyEndpoint), ExchangePublishText.ThirdPartyWaiting())
        End If
        m.ShowShares = online AndAlso snapshot.Block = ExchangePublishBlock.None AndAlso snapshot.Rows.Count > 0
        m.ShowLinkBlock = m.ShowShares
        Return m
    End Function

    ''' <summary>Which link buttons a selected row offers: create when published and linkless; new and remove when a link exists.</summary>
    Friend Shared Sub LinkButtons(row As ExchangePublishRow, ByRef create As Boolean, ByRef rotate As Boolean, ByRef remove As Boolean)
        create = False
        rotate = False
        remove = False
        If row Is Nothing OrElse row.State <> ExchangePublishState.Published Then Return
        create = Not row.HasLink
        rotate = row.HasLink
        remove = row.HasLink
    End Sub

End Class
