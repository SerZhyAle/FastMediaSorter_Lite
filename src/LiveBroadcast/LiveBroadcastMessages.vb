#If Not NETFRAMEWORK Then
Option Strict On

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md sections 3.2 and 13 item 8: the outcomes of reading a hand-off
' are never merged into one "invalid file". A descriptor of a later schema is refused with the canonical text of
' DEVICE-EXCHANGE `format-unsupported` (LIVE-BROADCAST 1.1; no update demand), a mode this build
' cannot play is called unsupported, an address only the device itself can reach says so, and only a damaged
' document is called damaged. One table, used by the dialog and by the window, so the two cannot disagree.

Friend Module LiveBroadcastMessages

    Public Function ForReadStatus(status As LiveReadStatus) As String
        Select Case status
            Case LiveReadStatus.UnsupportedVersion
                ' LIVE-BROADCAST 1.1: the canonical local row `format-unsupported` of DEVICE-EXCHANGE, not an "update the app" demand.
                ' A sealed-only descriptor reads the same sentence: this program cannot play it (spec 234 stage E2b).
                Return ConnectionDiagnosisLocalization.FormatUnsupportedText()
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

    ''' <summary>One line in current.log for a descriptor this program refused, naming the contract and, for a later schema, the
    ''' code `format-unsupported` (LIVE-BROADCAST 1.1 / DEVICE-EXCHANGE 1.3: kind local, step `-`). Carries no descriptor text, address or
    ''' secret: only where it happened and the outcome.</summary>
    Public Sub LogRefusal(status As LiveReadStatus, surface As String)
        If status = LiveReadStatus.Ok Then Return
        Dim code As String = If(status = LiveReadStatus.UnsupportedVersion, "format-unsupported", status.ToString())
        AppFileLogger.WriteLine("LIVE-BROADCAST: refused (" & code & ") in " & surface & ": nothing of the descriptor was used")
    End Sub

End Module
#End If
