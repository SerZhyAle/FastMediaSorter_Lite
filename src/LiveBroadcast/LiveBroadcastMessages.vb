#If Not NETFRAMEWORK Then
Option Strict On

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md sections 3.2 and 13 item 8: the outcomes of reading a hand-off
' are never merged into one "invalid file". A descriptor from a newer app asks for an update, a mode this build
' cannot play is called unsupported, an address only the device itself can reach says so, and only a damaged
' document is called damaged. One table, used by the dialog and by the window, so the two cannot disagree.

Friend Module LiveBroadcastMessages

    Public Function ForReadStatus(status As LiveReadStatus) As String
        Select Case status
            Case LiveReadStatus.UnsupportedVersion
                Return Localization.T("Требуется обновить приложение для этой трансляции")
            Case LiveReadStatus.UnsupportedMode
                Return Localization.T("Этот режим пока не поддерживается")
            Case LiveReadStatus.LoopbackHost
                Return Localization.T("Устройство передало локальный адрес (loopback), недоступный с ПК")
            Case LiveReadStatus.TooLarge
                Return Localization.T("Превышен максимальный размер дескриптора (64 КБ)")
            Case LiveReadStatus.InvalidDocument
                Return Localization.T("Неверный формат трансляции или повреждённые данные")
            Case Else
                Return Localization.T("Не удалось распознать трансляцию")
        End Select
    End Function

End Module
#End If
