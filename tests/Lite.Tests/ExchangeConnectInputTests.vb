#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Linq
Imports Xunit

''' <summary>
''' Ticket 187 T-4: what the "Add from an exchange server.." dialog is given, classified once. A connection string, a host:port typed by hand and
''' neither - and a share code or a URL is never mistaken for an address. The words for each refusal are one sentence per reason, all in the
''' language layer.
''' </summary>
Public Class ExchangeConnectInputTests

    <Fact>
    Public Sub A_connection_string_is_read_and_its_destination_and_pin_are_available_before_any_connection()
        Dim input As ExchangeConnectInput = ExchangeConnectInput.Classify("  FMSEXCH1:x=exchange.example.org%3A22022&n=Home+NAS&u=Alice" & vbCrLf)
        Assert.Equal(ExchangeConnectInputKind.ConnectionString, input.Kind)
        Assert.Equal("exchange.example.org:22022", input.Value.Endpoint)
        Assert.Equal("Home NAS", input.Value.Name)
        Assert.Equal("alice", input.Value.Login)
        Assert.False(input.HasPin)
    End Sub

    <Fact>
    Public Sub A_string_with_fp_carries_the_pin_as_the_trust_anchor()
        Dim fp As String = "SHA256:AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8"
        Dim input As ExchangeConnectInput = ExchangeConnectInput.Classify("FMSEXCH1:x=203.0.113.7%3A22022&fp=" & ExchangeConnectionString.Escape(fp))
        Assert.True(input.HasPin)
        Assert.Equal(fp, input.Value.Fingerprint)
    End Sub

    <Theory>
    <InlineData("203.0.113.7:22022", "203.0.113.7:22022")>
    <InlineData("Exchange.Example.org:22022", "exchange.example.org:22022")>
    <InlineData("[2001:db8::7]:22022", "[2001:db8::7]:22022")>
    Public Sub A_typed_host_and_port_is_an_address_with_no_pin(text As String, endpoint As String)
        Dim input As ExchangeConnectInput = ExchangeConnectInput.Classify(text)
        Assert.Equal(ExchangeConnectInputKind.Address, input.Kind)
        Assert.Equal(endpoint, input.Value.Endpoint)
        Assert.False(input.HasPin)
    End Sub

    <Theory>
    <InlineData("FMSSFTP2:AbCdEf", "not-a-string", "share-code")>
    <InlineData("https://exchange.example.org:22022/", "not-a-string", "url")>
    <InlineData("hello world", "not-a-string", "")>
    <InlineData("FMSEXCH2:x=a.b%3A1", "newer-major", "")>
    <InlineData("FMSEXCH1:x=a.b%3A0", "bad-x", "")>
    <InlineData("FMSEXCH1:fp=abc", "missing-x", "")>
    Public Sub Anything_else_is_refused_by_name_and_never_taken_for_an_address(text As String, reason As String, looks As String)
        Dim input As ExchangeConnectInput = ExchangeConnectInput.Classify(text)
        Assert.Equal(ExchangeConnectInputKind.Refused, input.Kind)
        Assert.Equal(reason, input.Reason)
        Assert.Equal(looks, input.Looks)
        Assert.False(input.IsUsable)
        Assert.NotEqual("", ExchangeEntryText.InputRefusal(input.Reason, input.Looks))
    End Sub

    <Fact>
    Public Sub Empty_text_is_empty()
        Assert.Equal(ExchangeConnectInputKind.Empty, ExchangeConnectInput.Classify("  ").Kind)
        Assert.Equal(ExchangeConnectInputKind.Empty, ExchangeConnectInput.Classify(Nothing).Kind)
    End Sub

    <Fact>
    Public Sub A_file_is_read_the_same_way_with_a_byte_order_mark_tolerated()
        Dim line As Byte() = System.Text.Encoding.UTF8.GetBytes("FMSEXCH1:x=203.0.113.7%3A22022" & vbCrLf)
        Dim withBom As Byte() = New Byte() {&HEF, &HBB, &HBF}.Concat(line).ToArray()
        Assert.Equal(ExchangeConnectInputKind.ConnectionString, ExchangeConnectInput.ClassifyFile(line).Kind)
        Assert.Equal(ExchangeConnectInputKind.ConnectionString, ExchangeConnectInput.ClassifyFile(withBom).Kind)
        Assert.Equal(ExchangeConnectInputKind.Refused, ExchangeConnectInput.ClassifyFile(New Byte(4096) {}).Kind)
    End Sub

    <Fact>
    Public Sub An_argument_that_begins_like_a_string_is_recognised_for_the_dialog_only()
        Assert.True(ExchangeConnectInput.LooksLikeConnectionString("FMSEXCH1:x=a.b%3A1"))
        Assert.True(ExchangeConnectInput.LooksLikeConnectionString("  FMSEXCH2:x"))        ' it is the dialog that says `format-unsupported`
        Assert.False(ExchangeConnectInput.LooksLikeConnectionString("C:\photos"))
        Assert.False(ExchangeConnectInput.LooksLikeConnectionString("sftp://host/dir"))
    End Sub

    <Fact>
    Public Sub Every_reason_of_the_codec_has_a_sentence_and_the_sentences_differ_where_the_remedy_differs()
        Dim reasons As String() = {ExchangeConnectionStringReason.NotAString, ExchangeConnectionStringReason.NewerMajor, ExchangeConnectionStringReason.TooLong,
                                   ExchangeConnectionStringReason.Whitespace, ExchangeConnectionStringReason.NotAscii, ExchangeConnectionStringReason.BadEscape,
                                   ExchangeConnectionStringReason.BadUtf8, ExchangeConnectionStringReason.MalformedField, ExchangeConnectionStringReason.RepeatedKey,
                                   ExchangeConnectionStringReason.MissingX, ExchangeConnectionStringReason.BadX, ExchangeConnectionStringReason.BadFingerprint,
                                   ExchangeConnectionStringReason.BadName, ExchangeConnectionStringReason.BadLogin, ExchangeConnectionStringReason.FileTooLong,
                                   ExchangeConnectionStringReason.FileLines}
        For Each r As String In reasons
            Assert.NotEqual("", ExchangeEntryText.InputRefusal(r, ""))
        Next
        ' a share code is not "format-unsupported", and "format-unsupported" is not "damaged"
        Assert.NotEqual(ExchangeEntryText.InputRefusal(ExchangeConnectionStringReason.NewerMajor, ""), ExchangeEntryText.InputRefusal(ExchangeConnectionStringReason.TooLong, ""))
        Assert.NotEqual(ExchangeEntryText.InputRefusal(ExchangeConnectionStringReason.NotAString, ExchangeConnectionStringReason.LooksShareCode),
                        ExchangeEntryText.InputRefusal(ExchangeConnectionStringReason.NotAString, ""))
    End Sub

    ''' <summary>DEVICE-EXCHANGE 1.3 amendment of 2026-10-17: a higher FMSEXCH prefix - typed, pasted or in a .fmsexch file - is refused cleanly with
    ''' the canonical local row `format-unsupported` in the language shown, and carries no "update" demand. The internal reason id
    ''' `newer-major` is the vector vocabulary and stays.</summary>
    <Fact>
    Public Sub A_higher_prefix_is_refused_with_the_canonical_format_unsupported_row_in_the_language_shown()
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                Dim typed As ExchangeConnectInput = ExchangeConnectInput.Classify("FMSEXCH2:x=a.b%3A1")
                Assert.Equal(ExchangeConnectInputKind.Refused, typed.Kind)
                Assert.Equal(ExchangeConnectionStringReason.NewerMajor, typed.Reason)
                Assert.Equal(ConnectionDiagnosisLocalization.GetMessageText("format-unsupported", code), ExchangeEntryText.InputRefusal(typed.Reason, typed.Looks))
            Next
            Dim file As ExchangeConnectInput = ExchangeConnectInput.ClassifyFile(System.Text.Encoding.UTF8.GetBytes("FMSEXCH9:x=a.b%3A1" & vbCrLf))
            Assert.Equal(ExchangeConnectInputKind.Refused, file.Kind)
            Assert.Equal(ExchangeConnectionStringReason.NewerMajor, file.Reason)
            Localization.CurrentCode = "en"
            Assert.Equal("This format is newer than this program supports. Nothing was imported. Use a compatible program.", ExchangeEntryText.InputRefusal(file.Reason, file.Looks))
            Localization.CurrentCode = "ru"
            Assert.Equal("Этот формат новее, чем поддерживает программа. Ничего не импортировано. Используйте совместимую программу.", ExchangeEntryText.InputRefusal(file.Reason, file.Looks))
            Localization.CurrentCode = "uk"
            Assert.Equal("Цей формат новіший, ніж підтримує програма. Нічого не імпортовано. Скористайтеся сумісною програмою.", ExchangeEntryText.InputRefusal(file.Reason, file.Looks))
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub Every_failure_of_the_join_has_a_sentence_and_the_refusals_that_need_another_remedy_do_not_share_one()
        For Each o As ExchangeConsumerEnrollOutcome In [Enum].GetValues(GetType(ExchangeConsumerEnrollOutcome))
            If o = ExchangeConsumerEnrollOutcome.Enrolled Then Continue For
            Assert.NotEqual("", ExchangeEntryText.EnrollFailure(o, ExchangeRefusal.Unknown))
        Next
        Dim seen As New System.Collections.Generic.HashSet(Of String)()
        For Each r As ExchangeRefusal In ExchangeRefusals.All()
            seen.Add(ExchangeEntryText.EnrollFailure(ExchangeConsumerEnrollOutcome.Refused, r))
        Next
        Assert.True(seen.Count >= 5, "the refusals that need a different remedy do not share one sentence")
    End Sub

    ''' <summary>Ticket 239 CR-1 and CR-9, DEVICE-EXCHANGE items CZ and DA: the viewer's registration shows the row of the message table of
    ''' each refusal, as the table has it - not one sentence for three causes, and no "update" request for a version refusal.</summary>
    <Fact>
    Public Sub A_registration_refusal_is_its_own_row_of_the_message_table_and_a_version_refusal_asks_for_no_update()
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = "en"
            Dim refused As ExchangeConsumerEnrollOutcome = ExchangeConsumerEnrollOutcome.Refused
            Assert.Equal("This invitation does not work.", ExchangeEntryText.RegisterFailure(refused, ExchangeRefusal.InvitationInvalid))
            Assert.Equal("This login is already taken. Choose another one.", ExchangeEntryText.RegisterFailure(refused, ExchangeRefusal.LoginTaken))
            Assert.Equal("The password is too simple. Use at least 10 characters, with capital and small Latin letters and digits.",
                         ExchangeEntryText.RegisterFailure(refused, ExchangeRefusal.PasswordPolicy))
            Assert.Equal("The server refused this.", ExchangeEntryText.RegisterFailure(refused, ExchangeRefusal.Unknown))
            ' a server older than the amendments of item AQ answers bad-credentials to all three: one sentence, not any of the three rows
            Dim older As String = ExchangeEntryText.RegisterFailure(refused, ExchangeRefusal.BadCredentials)
            Assert.NotEqual("", older)
            For Each r As ExchangeRefusal In {ExchangeRefusal.InvitationInvalid, ExchangeRefusal.LoginTaken, ExchangeRefusal.PasswordPolicy}
                Assert.NotEqual(older, ExchangeEntryText.RegisterFailure(refused, r))
            Next
            Dim version As String = "This program and the server speak different versions of the exchange."
            Assert.Equal(version, ExchangeEntryText.EnrollFailure(refused, ExchangeRefusal.VersionUnsupported))
            Assert.Equal(version, ExchangeEntryText.StateText(ExchangeAgentState.UpdateNeeded))
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

End Class
#End If
