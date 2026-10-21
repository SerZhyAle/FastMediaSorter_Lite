Option Strict On

''' <summary>Why a broadcast action or entry did not go through (spec 159 section 3.3).</summary>
Public Enum BroadcastNoticeReason
    ''' <summary>The host offers the page and the worker lacks the verbs: a real mismatch, and the only reason
    ''' the word "update" is ever said.</summary>
    WorkerTooOld
    ''' <summary>The gateway could not start (port, firewall): the worker's own refusal, shown as it is.</summary>
    GatewayRefused
    ''' <summary>Anything else. The detail goes to the log, not to the screen.</summary>
    Failed
End Enum

''' <summary>
''' The ONE place the broadcasts page turns a reason into a sentence. Before spec 159 the same wording
''' was written at three call sites on three surfaces (an inline label, an information box, a warning box);
''' now every call site asks for a reason and shows the answer on one inline surface, never a dialog.
''' </summary>
Public Module BroadcastMessages

    ''' <summary>The sentence for a reason. <paramref name="workerText"/> is the worker's own error text and is
    ''' used only for <see cref="BroadcastNoticeReason.GatewayRefused"/>; an empty one falls back to the
    ''' generic line instead of showing nothing.</summary>
    Public Function Text(reason As BroadcastNoticeReason, Optional workerText As String = "") As String
        Select Case reason
            Case BroadcastNoticeReason.WorkerTooOld
                Return Localization.TF("Эта версия службы трансляций устарела. Обновите {0}.", ProductNames.ShortLegacy)
            Case BroadcastNoticeReason.GatewayRefused
                If Not String.IsNullOrWhiteSpace(workerText) Then Return workerText.Trim()
                Return Localization.T("Не удалось выполнить действие. Подробности в журнале.")
            Case Else
                Return Localization.T("Не удалось выполнить действие. Подробности в журнале.")
        End Select
    End Function

    ''' <summary>The "Expires" cell of one broadcast row. WORKER-IPC 0.16: the worker's <c>expired</c> flag is a
    ''' persisted tombstone, final after a clock rollback and a worker restart, so a row that carries it says so
    ''' whatever its stored end time reads; a record without the flag (an older worker) keeps the end time, or
    ''' "until revoked" when it has none.</summary>
    Public Function ExpiryCell(b As WorkerBroadcast) As String
        If b Is Nothing Then Return ""
        If b.Expired Then Return Localization.T("Срок истёк")
        If String.IsNullOrEmpty(b.ExpiresAt) Then Return Localization.T("До отзыва")
        Return b.ExpiresAt
    End Function

    ''' <summary>
    ''' Classifies the worker's answer to one request. "Update" is said only when the worker itself said it
    ''' does not know the verb (<see cref="BroadcastController.IsUnsupportedWorker"/>, the secondary guard); a
    ''' refusal text is trusted as a reason only for the request that starts the gateway
    ''' (<paramref name="gatewayRequest"/>), where it is exactly the port or firewall explanation. Every other
    ''' failure - no answer, an exchange problem, an error on a create or revoke - is <c>Failed</c>.
    ''' </summary>
    Public Function ReasonFor(resp As WorkerResponse, gatewayRequest As Boolean) As BroadcastNoticeReason
        If resp Is Nothing Then Return BroadcastNoticeReason.Failed
        If BroadcastController.IsUnsupportedWorker(resp.Error) Then Return BroadcastNoticeReason.WorkerTooOld
        If gatewayRequest AndAlso Not resp.Ok AndAlso Not resp.IsProblem AndAlso
           Not String.IsNullOrWhiteSpace(resp.Error) Then Return BroadcastNoticeReason.GatewayRefused
        Return BroadcastNoticeReason.Failed
    End Function

    ''' <summary>The sentence for a request the page does not serve (a forwarded launch, a stale button).
    ''' A worker whose status says it lacks the verbs, under a build that offers the page, is a real mismatch;
    ''' everything else - above all a build that does not offer the page at all - is the generic line, which
    ''' neither advertises the feature nor sends the user to an update that does not exist.</summary>
    Public Function EntryReason(status As WorkerStatus, offeredByBuild As Boolean) As BroadcastNoticeReason
        If offeredByBuild AndAlso status IsNot Nothing AndAlso Not status.BroadcastSupported Then
            Return BroadcastNoticeReason.WorkerTooOld
        End If
        Return BroadcastNoticeReason.Failed
    End Function

End Module
