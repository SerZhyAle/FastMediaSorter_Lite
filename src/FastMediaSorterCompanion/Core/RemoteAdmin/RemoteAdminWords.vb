Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

' Ticket L0278 T-5 (specification section 5.7): from what a sign-in or a call came to, to the sentence the window shows. The table of 5.7
' in full, in one place.
'
'   - No sentence is invented where the contract has one, and no contract row is reworded (item DA): a row of the common connection
'     message table (CZ) is shown as the table has it, at the language of the window, through ConnectionDiagnosisLocalization, with
'     nothing of this program in front of it. An unknown code reads as the generic refusal row, never as the code.
'   - Where the contract has no sentence the product writes one, a TAGGED local sentence in the Companion's own table
'     (Localization.RemoteAdmin.vb). Each carries the source tag "registry-note candidate (L0278 9.3)" on the line before its
'     declaration; RemoteAdminWordsTests reads this file for it. They are: the wrong administrator password at sign-in (CZ's row names
'     a login), the lapsed session, the reply this program does not understand, and "the change may or may not have been made".
'   - A failed call's ten reply codes go through ExchangeAdminText.Failure; "internal" has no Case there and reads as its generic Else.
'   - Nothing the wire said and nothing of an outcome's Detail ever reaches a sentence: the library keeps the enum only, and Detail is
'     for the log. A cancelled sign-in or call has NO sentence (cancellation is an outcome, not an error); neither has a success, a
'     first-contact question (the window asks it in its own words) or a call refused as Busy (a bug of the window, never a message).
'   - The failure stays in the window, in a status line of the card; there is no toast.

Friend NotInheritable Class RemoteAdminWords

    Private Sub New()
    End Sub

    ' --- a sign-in ----------------------------------------------------------------------------------------------

    ''' <summary>The row of the common message table a sign-in reads as, so the window can run the connection diagnosis (the ladder of
    ''' steps) from it; Nothing when the words are this program's own, or when there is nothing to say. <c>admin-disabled</c>,
    ''' <c>rate-limited</c>, <c>capacity</c>, <c>tls-required</c>, <c>unanswered</c>, <c>unreachable-server</c>, <c>tls-failed</c> and
    ''' <c>certificate-changed</c> have their own row; every other refusal reads as the generic <c>refused</c>.</summary>
    Friend Shared Function SignInRow(result As ExchangeRemoteAdminSignIn) As ConnectionDiagnosisMessage
        If result Is Nothing Then Return Nothing
        Select Case result.Kind
            Case ExchangeRemoteAdminSignInKind.Disabled
                Return ConnectionDiagnosisMessages.AdminDisabled
            Case ExchangeRemoteAdminSignInKind.Refused
                Select Case result.Refusal
                    Case ExchangeRefusal.AdminDisabled : Return ConnectionDiagnosisMessages.AdminDisabled
                    Case ExchangeRefusal.BadCredentials : Return Nothing
                    Case ExchangeRefusal.RateLimited : Return ConnectionDiagnosisMessages.RateLimited
                    Case ExchangeRefusal.Capacity : Return ConnectionDiagnosisMessages.Capacity
                    Case ExchangeRefusal.TlsRequired : Return ConnectionDiagnosisMessages.TlsRequired
                    Case Else : Return ConnectionDiagnosisMessages.Refused
                End Select
            Case ExchangeRemoteAdminSignInKind.CertificateChanged
                Return ConnectionDiagnosisMessages.CertificateChanged
            Case ExchangeRemoteAdminSignInKind.Unreachable
                Return UnreachableRow(result.Reason)
            Case Else
                Return Nothing
        End Select
    End Function

    ''' <summary>The sentence of a sign-in result, "" for the ones with nothing to say (signed in, a first-contact question, a cancel).
    ''' A changed certificate carries both fingerprints under the row's words (the stop of 5.4).</summary>
    Friend Shared Function ForSignIn(result As ExchangeRemoteAdminSignIn) As String
        If result Is Nothing Then Return ""
        Select Case result.Kind
            Case ExchangeRemoteAdminSignInKind.SignedIn, ExchangeRemoteAdminSignInKind.NeedsFirstContact, ExchangeRemoteAdminSignInKind.Cancelled
                Return ""
            Case ExchangeRemoteAdminSignInKind.CertificateChanged
                Return CertificateStop(result.PinnedFingerprint, result.PresentedFingerprint)
            Case ExchangeRemoteAdminSignInKind.Refused
                If result.Refusal = ExchangeRefusal.BadCredentials Then Return BadPassword()
                Return Row(SignInRow(result))
            Case ExchangeRemoteAdminSignInKind.Disabled
                Return Row(SignInRow(result))
            Case ExchangeRemoteAdminSignInKind.Unreachable
                If result.Reason = ExchangeRemoteAdminUnreachableReason.Cancelled Then Return ""
                If result.Reason = ExchangeRemoteAdminUnreachableReason.Protocol Then Return NotUnderstood()
                Return Row(UnreachableRow(result.Reason))
            Case Else
                Return NotUnderstood()
        End Select
    End Function

    ' --- a call -------------------------------------------------------------------------------------------------

    ''' <summary>The sentence of one call's outcome, "" for a success, a cancel and a Busy. <paramref name="isMutation"/> is True for an
    ''' operation that changes the server: a timeout or a broken connection in its middle says the change may or may not have been made
    ''' (the session is ended by the caller).</summary>
    Friend Shared Function ForOutcome(outcome As ExchangeRemoteAdminOutcome, Optional isMutation As Boolean = False) As String
        If outcome Is Nothing Then Return ""
        Select Case outcome.Kind
            Case ExchangeRemoteAdminOutcomeKind.Done, ExchangeRemoteAdminOutcomeKind.Busy
                Return ""
            Case ExchangeRemoteAdminOutcomeKind.Failed
                Return ExchangeAdminText.Failure(outcome.Code)
            Case ExchangeRemoteAdminOutcomeKind.Lapsed
                Return SessionEnded()
            Case ExchangeRemoteAdminOutcomeKind.Unreachable
                Select Case outcome.Reason
                    Case ExchangeRemoteAdminUnreachableReason.Cancelled
                        Return ""
                    Case ExchangeRemoteAdminUnreachableReason.Protocol
                        Return NotUnderstood()
                    Case ExchangeRemoteAdminUnreachableReason.Timeout, ExchangeRemoteAdminUnreachableReason.Reset
                        Dim words As String = Row(UnreachableRow(outcome.Reason))
                        If Not isMutation Then Return words
                        Return String.Join(vbCrLf, {words, MayOrMayNot()})
                    Case Else
                        Return Row(UnreachableRow(outcome.Reason))
                End Select
            Case Else
                Return NotUnderstood()
        End Select
    End Function

    ' registry-note candidate (L0278 9.3): tagged local sentence, the lapse (5.7)
    ''' <summary>The words after a session ended on its own: "The session has ended. Sign in again.", with the cause in brackets when one
    ''' is given (a lock or a sleep: <see cref="CauseScreenLocked"/>, <see cref="CauseWindowsSession"/>, <see cref="CauseSleep"/>).</summary>
    Friend Shared Function SessionEnded(Optional cause As String = "") As String
        If String.IsNullOrWhiteSpace(cause) Then Return Localization.T("Сеанс завершён. Войдите снова.")
        Return Localization.TF("Сеанс завершён ({0}). Войдите снова.", cause.Trim())
    End Function

    ''' <summary>The cause of a lock of the screen, for <see cref="SessionEnded"/>.</summary>
    Friend Shared Function CauseScreenLocked() As String
        Return Localization.T("экран заблокирован")
    End Function

    ''' <summary>The cause of a sign-out or a disconnect of the Windows session, for <see cref="SessionEnded"/>.</summary>
    Friend Shared Function CauseWindowsSession() As String
        Return Localization.T("сеанс Windows закрыт или отключён")
    End Function

    ''' <summary>The cause of a sleep of the computer, for <see cref="SessionEnded"/>.</summary>
    Friend Shared Function CauseSleep() As String
        Return Localization.T("компьютер перешёл в спящий режим")
    End Function

    ''' <summary>The words of a device revoke the server answered with not-found: it may be removed already.</summary>
    Friend Shared Function DeviceRevokeNotFound() As String
        Return Localization.T("Устройства нет на сервере: возможно, оно уже удалено.")
    End Function

    ' --- the pieces ---------------------------------------------------------------------------------------------

    ' registry-note candidate (L0278 9.3): tagged local sentence, the wrong administrator password at sign-in (G-2)
    Private Shared Function BadPassword() As String
        Return Localization.T("Пароль администратора неверный.")
    End Function

    ' registry-note candidate (L0278 9.3): tagged local sentence, a reply of no known shape (5.7)
    Private Shared Function NotUnderstood() As String
        Return Localization.T("Сервер ответил так, что программа этого не понимает.")
    End Function

    ' registry-note candidate (L0278 9.3): tagged local sentence, the timeout of a mutating call (5.7)
    Private Shared Function MayOrMayNot() As String
        Return Localization.T("Изменение могло быть выполнено, а могло и нет.")
    End Function

    ''' <summary>The stop of a changed certificate: the row's words, then the fingerprint the target holds and the one the server showed.
    ''' A fingerprint always reads left to right, so it goes through <c>Localization.LtrValue</c> in a right-to-left window.</summary>
    Private Shared Function CertificateStop(pinned As String, presented As String) As String
        Return String.Join(vbCrLf, {Row(ConnectionDiagnosisMessages.CertificateChanged),
                                    Localization.TF("Закреплённый отпечаток: {0}", Localization.LtrValue(If(pinned, ""))),
                                    Localization.TF("Предъявленный отпечаток: {0}", Localization.LtrValue(If(presented, "")))})
    End Function

    ''' <summary>The row an Unreachable result reads as: the sign-in reply that never came (or a connect that ran out of time) is
    ''' <c>unanswered</c>, a handshake that failed is <c>tls-failed</c>, anything else cannot reach the server.</summary>
    Private Shared Function UnreachableRow(reason As ExchangeRemoteAdminUnreachableReason) As ConnectionDiagnosisMessage
        Select Case reason
            Case ExchangeRemoteAdminUnreachableReason.TlsFailed
                Return ConnectionDiagnosisMessages.TlsFailed
            Case ExchangeRemoteAdminUnreachableReason.Timeout, ExchangeRemoteAdminUnreachableReason.Unanswered
                Return ConnectionDiagnosisMessages.Unanswered
            Case Else
                Return ConnectionDiagnosisMessages.UnreachableServer
        End Select
    End Function

    ''' <summary>The words of a row at the language of the window; the generic refusal when the table has none (never the code).</summary>
    Private Shared Function Row(message As ConnectionDiagnosisMessage) As String
        If message Is Nothing Then message = ConnectionDiagnosisMessages.Refused
        Dim locale As String = Localization.CurrentCode
        Dim text As String = ConnectionDiagnosisLocalization.GetMessageText(message.Code, locale)
        If text.Length = 0 OrElse String.Equals(text, message.Code, StringComparison.Ordinal) Then
            text = ConnectionDiagnosisLocalization.GetMessageText(ConnectionDiagnosisMessages.Refused.Code, locale)
        End If
        Return text
    End Function

End Class
