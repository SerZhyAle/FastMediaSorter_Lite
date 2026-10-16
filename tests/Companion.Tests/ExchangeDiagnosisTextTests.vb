Option Strict On

Imports System.Linq
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 239 CR-1 and CR-9, DEVICE-EXCHANGE 1.1 items BR, CZ and DA: what the Share Manager tells a person about a refused or failed
''' connection is the row of the one message table, shown as the table has it (item DA) and never a sentence of this program in
''' front of it. The English column is pinned verbatim here; the 13 languages of every row are pinned by the viewer's
''' ConnectionDiagnosisTests, which reads the same table.
''' </summary>
<Collection("Exchange device host")>
Public Class ExchangeDiagnosisTextTests

    ''' <summary>Runs the body in English, whatever the machine speaks, and puts the language back.</summary>
    Private Shared Sub InEnglish(body As Action)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = "en"
            body()
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    Private Shared Function Refused(reason As ExchangeRefusal) As ExchangeEnrollResult
        Return New ExchangeEnrollResult(ExchangeEnrollOutcome.Refused, reason, "")
    End Function

    Private Shared Function Outcome(kind As ExchangeEnrollOutcome) As ExchangeEnrollResult
        Return New ExchangeEnrollResult(kind, ExchangeRefusal.Unknown, "")
    End Function

    Private Shared Function Status(state As ExchangeAgentState, reason As String) As ExchangeDeviceStatus
        Return New ExchangeDeviceStatus(True, state, reason, New String() {"directory", "tunnel", "pairing"}, ExchangeWireTestKit.IdOf("D"c), "PC", "alice", "host:22022",
                                        ExchangeCertificateKind.Pinned, "SHA256:x")
    End Function

    ' ---------------------------------------------------------------- registration refusals (item AQ, CZ)

    <Theory>
    <InlineData("InvitationInvalid", "This invitation does not work.")>
    <InlineData("LoginTaken", "This login is already taken. Choose another one.")>
    <InlineData("PasswordPolicy", "The password is too simple. Use at least 10 characters, with capital and small Latin letters and digits.")>
    <InlineData("RateLimited", "Too many attempts in a row.")>
    <InlineData("Capacity", "The server cannot take this: it has reached a limit of its own, or the item is too big to be listed.")>
    Public Sub Each_refusal_of_a_registration_is_its_own_row_of_the_table_and_not_the_generic_sentence(reasonName As String, english As String)
        InEnglish(Sub()
                      Dim reason As ExchangeRefusal = DirectCast([Enum].Parse(GetType(ExchangeRefusal), reasonName), ExchangeRefusal)
                      Dim text As String = ExchangeJoinText.RegisterFailure(Refused(reason))
                      Assert.Equal(english, text)
                      Assert.NotEqual("The server refused this.", text)
                  End Sub)
    End Sub

    <Fact>
    Public Sub The_three_registration_refusals_are_three_different_sentences_in_every_language()
        For Each code As String In {"en", "ru", "uk", "de", "es", "fr", "it", "ja", "ko", "pl", "pt", "tr", "zh"}
            Dim saved As String = Localization.CurrentCode
            Try
                Localization.CurrentCode = code
                Dim texts As String() = {ExchangeJoinText.RegisterFailure(Refused(ExchangeRefusal.InvitationInvalid)),
                                         ExchangeJoinText.RegisterFailure(Refused(ExchangeRefusal.LoginTaken)),
                                         ExchangeJoinText.RegisterFailure(Refused(ExchangeRefusal.PasswordPolicy)),
                                         ExchangeJoinText.RegisterFailure(Refused(ExchangeRefusal.Unknown))}
                Assert.Equal(4, texts.Distinct().Count())
                Assert.All(texts, Sub(t) Assert.False(String.IsNullOrWhiteSpace(t), code))
            Finally
                Localization.CurrentCode = saved
            End Try
        Next
    End Sub

    <Fact>
    Public Sub An_older_server_that_answers_bad_credentials_to_every_registration_refusal_keeps_the_one_sentence_covering_all_three()
        InEnglish(Sub()
                      Dim older As String = ExchangeJoinText.RegisterFailure(Refused(ExchangeRefusal.BadCredentials))
                      Assert.False(String.IsNullOrWhiteSpace(older))
                      For Each r As ExchangeRefusal In {ExchangeRefusal.InvitationInvalid, ExchangeRefusal.LoginTaken, ExchangeRefusal.PasswordPolicy, ExchangeRefusal.Unknown}
                          Assert.NotEqual(older, ExchangeJoinText.RegisterFailure(Refused(r)))
                      Next
                  End Sub)
    End Sub

    <Fact>
    Public Sub An_unknown_reason_of_a_registration_is_the_generic_refusal_of_the_table()
        InEnglish(Sub() Assert.Equal("The server refused this.", ExchangeJoinText.RegisterFailure(Refused(ExchangeRefusal.Unknown))))
    End Sub

    ' ---------------------------------------------------------------- the account dialog (item DA)

    <Theory>
    <InlineData("BadCredentials", False, "The login or the password is wrong.")>
    <InlineData("BadCredentials", True, "The login or the one-time code is wrong.")>
    <InlineData("DeviceRevoked", False, "This device was removed from the account.")>
    <InlineData("SignedOut", False, "The password of this account was changed, so this device was signed out.")>
    <InlineData("VersionUnsupported", False, "This program and the server speak different versions of the exchange.")>
    <InlineData("TlsRequired", False, "The server accepts this only over a protected connection, and this one is not protected.")>
    <InlineData("Unavailable", False, "The device or the server is not available right now.")>
    <InlineData("Unknown", False, "The server refused this.")>
    Public Sub The_account_dialog_shows_the_row_of_the_table_as_it_is_and_nothing_before_it(reasonName As String, oneTimeCode As Boolean, english As String)
        InEnglish(Sub()
                      Dim reason As ExchangeRefusal = DirectCast([Enum].Parse(GetType(ExchangeRefusal), reasonName), ExchangeRefusal)
                      Assert.Equal(english, ExchangeDeviceText.EnrollFailure(Refused(reason), oneTimeCode))
                      Assert.Equal(english, ExchangeDeviceText.TableText(ExchangeDeviceText.EnrollRow(Refused(reason), oneTimeCode)))
                  End Sub)
    End Sub

    <Fact>
    Public Sub The_version_refusal_asks_for_no_update()
        ' item CZ: "version-unsupported carries no version and no update request"
        For Each code As String In {"en", "ru", "uk"}
            Dim saved As String = Localization.CurrentCode
            Try
                Localization.CurrentCode = code
                Dim fromEnroll As String = ExchangeDeviceText.EnrollFailure(Refused(ExchangeRefusal.VersionUnsupported))
                Dim fromState As String = ExchangeDeviceText.StateLine(Status(ExchangeAgentState.UpdateNeeded, "version-unsupported"))
                Assert.Equal(fromEnroll, fromState)
                Assert.DoesNotContain("update", fromState, StringComparison.OrdinalIgnoreCase)
                Assert.DoesNotContain("обнов", fromState, StringComparison.OrdinalIgnoreCase)
                Assert.DoesNotContain("онов", fromState, StringComparison.OrdinalIgnoreCase)
            Finally
                Localization.CurrentCode = saved
            End Try
        Next
    End Sub

    <Theory>
    <InlineData("Network", "Cannot reach the server.")>
    <InlineData("TlsFailed", "A protected connection could not be set up.")>
    <InlineData("TimedOut", "The server did not answer.")>
    <InlineData("PinMismatch", "This server does not look like the one you connected to before.")>
    Public Sub A_failed_first_contact_is_the_row_of_the_table_for_that_failure(outcomeName As String, english As String)
        InEnglish(Sub()
                      Dim kind As ExchangeEnrollOutcome = DirectCast([Enum].Parse(GetType(ExchangeEnrollOutcome), outcomeName), ExchangeEnrollOutcome)
                      Assert.Equal(english, ExchangeDeviceText.EnrollFailure(Outcome(kind)))
                  End Sub)
    End Sub

    ''' <summary>The first-contact check of the dialog: the server not answering is `unanswered`, never the cast row `timeout` (item CZ:
    ''' that one is "The other device did not answer in time"); a failed handshake is `tls-failed`; the rest cannot be reached.</summary>
    <Theory>
    <InlineData("TimedOut", "unanswered")>
    <InlineData("TlsFailed", "tls-failed")>
    <InlineData("Network", "unreachable-server")>
    Public Sub The_connect_check_never_reads_a_timeout_as_the_cast_row(failureName As String, code As String)
        Dim failure As ExchangeConnectFailure = DirectCast([Enum].Parse(GetType(ExchangeConnectFailure), failureName), ExchangeConnectFailure)
        Dim row As ConnectionDiagnosisMessage = Exchange_Account_Form.ConnectFailureRow(failure)
        Assert.Equal(code, row.Code)
        Assert.NotEqual(ConnectionDiagnosisMessages.Timeout.Code, row.Code)
    End Sub

    ' ---------------------------------------------------------------- the agent's state (item AT, BR)

    <Fact>
    Public Sub A_signed_out_device_says_the_row_of_the_table_and_a_removed_one_says_its_own()
        InEnglish(Sub()
                      Dim signedOut As String = ExchangeDeviceText.StateDetail(Status(ExchangeAgentState.SignInAgain, "signed-out"))
                      Assert.Equal("The password of this account was changed, so this device was signed out.", signedOut)
                      Assert.NotEqual(signedOut, ExchangeDeviceText.StateDetail(Status(ExchangeAgentState.SignInAgain, "device-revoked")))
                      Assert.NotEqual(signedOut, ExchangeDeviceText.StateDetail(Status(ExchangeAgentState.SignInAgain, "bad-credentials")))
                  End Sub)
    End Sub

    <Fact>
    Public Sub A_password_outside_the_policy_is_refused_with_the_row_of_the_table_on_a_change_too()
        InEnglish(Sub()
                      Assert.Equal("The password is too simple. Use at least 10 characters, with capital and small Latin letters and digits.",
                                   ExchangePasswordChangeText.Failure(ExchangeRefusal.PasswordPolicy))
                  End Sub)
    End Sub

End Class
