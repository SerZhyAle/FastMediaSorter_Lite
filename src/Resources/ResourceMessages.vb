#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' The sentences a person reads when a resource does not open (007 §2.2): one per failure CLASS, so a
''' dropped link, a refused password and a missing folder never share a wording. Pure - a class in, a
''' localized sentence out - and the only place that decides what each class is called, so the status
''' line, the manager's "Test" and the import all say the same thing. No sentence carries a host name, a
''' path or a secret: the caller adds the resource's name where it wants one.
''' </summary>
Friend Module ResourceMessages

    Friend Function ForMoveFailure(reason As TransferReason) As String
        Select Case reason
            Case TransferReason.SourceChanged
                Return Localization.T("Исходный файл изменился")
            Case TransferReason.Cancelled
                Return ForFailure(New ResourceFailure(ResourceFailureKind.Cancelled))
            Case TransferReason.SourceDeleteUnsupported, TransferReason.NotDeletable
                Return ForFailure(New ResourceFailure(ResourceFailureKind.Unsupported))
            Case Else
                Return Localization.T("Исходный файл не удалось удалить")
        End Select
    End Function

    ''' <summary>SFTP status codes the provider reports (<see cref="ResourceFailure.StatusCode"/>).</summary>
    Private Const SftpNoSuchFile As Integer = 2
    Private Const SftpPermissionDenied As Integer = 3

    Friend Function ForFailure(failure As ResourceFailure) As String
        If failure Is Nothing Then Return Localization.T("Не удалось подключиться к ресурсу")
        Select Case failure.Kind
            Case ResourceFailureKind.Auth
                Return Localization.T("Сервер не принял логин или пароль")
            Case ResourceFailureKind.KeyMismatch
                ' the tunnel's own stop (ticket 171): the EXCHANGE server's certificate, not the SSH key of the share
                If String.Equals(failure.Detail, ExchangeTunnelRules.CertificateDetail, StringComparison.Ordinal) Then
                    Return Localization.T("Сертификат сервера обмена изменился - подключение через сервер отменено")
                End If
                Return Localization.T("Ключ сервера не совпал с запомненным - подключение отменено")
            Case ResourceFailureKind.Transport
                Return Localization.T("Сервер не отвечает или связь оборвалась")
            Case ResourceFailureKind.Timeout
                Return Localization.T("Сервер не ответил вовремя")
            Case ResourceFailureKind.SftpStatus
                If failure.StatusCode = SftpNoSuchFile Then Return Localization.T("На сервере нет такой папки")
                If failure.StatusCode = SftpPermissionDenied Then Return Localization.T("Сервер не разрешает доступ к этой папке")
                Return Localization.T("Сервер отклонил запрос")
            Case ResourceFailureKind.Cancelled
                Return Localization.T("Подключение отменено")
            Case ResourceFailureKind.CredentialConflict
                Return Localization.T("Windows уже держит подключение к этому серверу под другим именем. Отключите его (net use) и повторите")
            Case ResourceFailureKind.Unsupported
                Return Localization.T("Этот вид ресурса здесь не поддерживается")
            Case ResourceFailureKind.LocalIO
                Return Localization.T("Не удалось сохранить файл на этом компьютере")
            Case Else
                Return Localization.T("Не удалось подключиться к ресурсу")
        End Select
    End Function

    ''' <summary>Why a document was not read, without ever quoting it. A document made by a newer program is
    ''' the one case that names a way out; every other refusal says only that the file is not usable.</summary>
    Friend Function ForImportFailure(failure As FmsConfigFailure) As String
        Select Case failure
            Case FmsConfigFailure.UpdateRequired
                Return Localization.T("Файл создан более новой версией программы. Обновите Fast Media Sorter")
            Case FmsConfigFailure.UnsupportedProtocol
                Return Localization.T("В файле не SFTP-ресурс, его нельзя открыть")
            Case FmsConfigFailure.Empty
                Return Localization.T("Файл пуст")
            Case FmsConfigFailure.TooLarge, FmsConfigFailure.CompressedTooLarge, FmsConfigFailure.InflatedTooLarge
                Return Localization.T("Файл слишком велик для файла настроек ресурса")
            Case FmsConfigFailure.NoUsableAccessPath
                Return Localization.T("В файле нет подходящего адреса сервера")
            Case Else
                Return Localization.T("Файл настроек повреждён или сделан не в Share Manager")
        End Select
    End Function

    ''' <summary>The label of a resource's kind in the manager's list.</summary>
    Friend Function KindLabel(kind As ResourceKind) As String
        Select Case kind
            Case ResourceKind.Sftp
                Return Localization.T("SFTP-сервер")
            Case ResourceKind.Smb, ResourceKind.Unc
                Return Localization.T("Сетевая папка (SMB)")
            Case Else
                Return Localization.T("Папка")
        End Select
    End Function

    ''' <summary>What the folder box and the list show for a resource: the address, never a secret.</summary>
    Friend Function DisplayAddress(resource As ResourceDescriptor) As String
        If resource Is Nothing Then Return ""
        Return ResourceLocator.Build(resource, resource.RootPath)
    End Function

End Module
#End If
