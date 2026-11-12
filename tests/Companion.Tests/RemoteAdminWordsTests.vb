Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket L0278 T-5, specification section 7.3 (row <c>RemoteAdminWordsTests</c>) and 5.7: every outcome of a sign-in and of a call reads as
''' a sentence in each of the 13 languages (or as nothing, for the ones with nothing to say), a contract row is shown as the table has it,
''' the four local sentences carry the registry-note tag in the source, and nothing the wire said ever reaches a sentence. The test file
''' holds no Russian literal: a language is read through the table, never typed here.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class RemoteAdminWordsTests

    Private Const SecretDetail As String = "SECRET-DETAIL-9f3a"
    Private Const Pinned As String = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    Private Const Presented As String = "SHA256:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"

    ' ---------------------------------------------------------------- helpers

    Private Shared Sub InEachLanguage(body As Action(Of String))
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                body(code)
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    Private Shared Sub InEnglish(body As Action)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = "en"
            body()
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    Private Shared Function Failed(code As String) As ExchangeRemoteAdminOutcome
        Return ExchangeRemoteAdminOutcome.NewFailed(code)
    End Function

    Private Shared Function Done() As ExchangeRemoteAdminOutcome
        Dim none As JsonElement = Nothing
        Return ExchangeRemoteAdminOutcome.NewDone(False, none)
    End Function

    Private Shared Function Cut(reason As ExchangeRemoteAdminUnreachableReason) As ExchangeRemoteAdminOutcome
        Return ExchangeRemoteAdminOutcome.NewUnreachable(reason, SecretDetail)
    End Function

    Private Shared ReadOnly TenCodes As String() = {"bad-request", "no-account", "exists", "bad-login", "weak-password", "not-found", "capacity", "bad-kind", "bad-limit", "internal"}

    ''' <summary>The words of one row of the common message table at the language now selected, as the table has them.</summary>
    Private Shared Function TableRow(code As String) As String
        Return ConnectionDiagnosisLocalization.GetMessageText(code, Localization.CurrentCode)
    End Function

    ''' <summary>Every sign-in result that has something to say, by a label. The label is only for the failure message.</summary>
    Private Shared Function SpeakingSignIns() As List(Of KeyValuePair(Of String, ExchangeRemoteAdminSignIn))
        Dim list As New List(Of KeyValuePair(Of String, ExchangeRemoteAdminSignIn))()
        Dim add As Action(Of String, ExchangeRemoteAdminSignIn) = Sub(label, value) list.Add(New KeyValuePair(Of String, ExchangeRemoteAdminSignIn)(label, value))
        add("disabled", ExchangeRemoteAdminSignIn.Disabled())
        For Each refusal As ExchangeRefusal In {ExchangeRefusal.BadCredentials, ExchangeRefusal.RateLimited, ExchangeRefusal.Capacity, ExchangeRefusal.TlsRequired,
                                                ExchangeRefusal.AdminDisabled, ExchangeRefusal.Unknown, ExchangeRefusal.DeviceRevoked, ExchangeRefusal.VersionUnsupported}
            add("refused " & refusal.ToString(), ExchangeRemoteAdminSignIn.Refused(refusal))
        Next
        add("certificate changed", ExchangeRemoteAdminSignIn.CertificateChanged(Pinned, Presented))
        For Each reason As ExchangeRemoteAdminUnreachableReason In [Enum].GetValues(GetType(ExchangeRemoteAdminUnreachableReason)).Cast(Of ExchangeRemoteAdminUnreachableReason)()
            If reason = ExchangeRemoteAdminUnreachableReason.Cancelled Then Continue For
            add("unreachable " & reason.ToString(), ExchangeRemoteAdminSignIn.Unreachable(reason, SecretDetail))
        Next
        Return list
    End Function

    ' ---------------------------------------------------------------- a sign-in

    <Fact>
    Public Sub Every_sign_in_that_has_something_to_say_reads_as_a_sentence_in_each_of_13_languages()
        Assert.Equal(13, Localization.Codes.Length)
        InEachLanguage(Sub(code)
                           For Each pair As KeyValuePair(Of String, ExchangeRemoteAdminSignIn) In SpeakingSignIns()
                               Dim text As String = RemoteAdminWords.ForSignIn(pair.Value)
                               Assert.False(String.IsNullOrWhiteSpace(text), code & " " & pair.Key)
                               Assert.DoesNotContain(SecretDetail, text)
                           Next
                       End Sub)
    End Sub

    <Fact>
    Public Sub A_sign_in_with_nothing_to_say_reads_as_nothing()
        InEachLanguage(Sub(code)
                           Assert.Equal("", RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.SignedIn(Nothing)))
                           Assert.Equal("", RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.NeedsFirstContact(Pinned, "")))
                           Assert.Equal("", RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Cancelled()))
                           Assert.Equal("", RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Cancelled, SecretDetail)))
                           Assert.Equal("", RemoteAdminWords.ForSignIn(Nothing))
                       End Sub)
    End Sub

    <Fact>
    Public Sub The_rows_of_the_common_table_are_shown_as_the_table_has_them_in_every_language()
        InEachLanguage(Sub(code)
                           Assert.Equal(TableRow("admin-disabled"), RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Disabled()))
                           Assert.Equal(TableRow("admin-disabled"), RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.AdminDisabled)))
                           Assert.Equal(TableRow("rate-limited"), RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.RateLimited)))
                           Assert.Equal(TableRow("capacity"), RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.Capacity)))
                           Assert.Equal(TableRow("tls-required"), RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.TlsRequired)))
                           Assert.Equal(TableRow("refused"), RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.Unknown)))
                           Assert.Equal(TableRow("refused"), RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.DeviceRevoked)))
                           Assert.Equal(TableRow("refused"), RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.VersionUnsupported)))
                           Assert.Equal(TableRow("unanswered"), RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Unanswered, "")))
                           Assert.Equal(TableRow("unanswered"), RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Timeout, "")))
                           Assert.Equal(TableRow("tls-failed"), RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.TlsFailed, "")))
                           Assert.Equal(TableRow("unreachable-server"), RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Network, "")))
                           Assert.Equal(TableRow("unreachable-server"), RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Reset, "")))
                           Assert.Equal(TableRow("unreachable-server"), RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.None, "")))
                       End Sub)
    End Sub

    <Fact>
    Public Sub A_changed_certificate_carries_the_row_and_both_fingerprints()
        InEachLanguage(Sub(code)
                           Dim text As String = RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.CertificateChanged(Pinned, Presented))
                           Dim lines As String() = text.Split({vbCrLf}, StringSplitOptions.None)
                           Assert.Equal(3, lines.Length)
                           Assert.Equal(TableRow("certificate-changed"), lines(0))
                           Assert.Contains(Pinned, lines(1))
                           Assert.Contains(Presented, lines(2))
                           Assert.NotEqual(lines(1), lines(2))
                           If code = "ar" OrElse code = "ur" Then
                               Assert.Contains(ChrW(&H202A) & Pinned & ChrW(&H202C), lines(1))
                               Assert.Contains(ChrW(&H202A) & Presented & ChrW(&H202C), lines(2))
                           Else
                               Assert.False(text.Contains(ChrW(&H202A).ToString()), code)
                           End If
                       End Sub)
    End Sub

    <Fact>
    Public Sub A_changed_certificate_with_no_fingerprint_still_reads_without_a_fault()
        InEachLanguage(Sub(code)
                           Dim text As String = RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.CertificateChanged("", ""))
                           Assert.Equal(3, text.Split({vbCrLf}, StringSplitOptions.None).Length)
                           Assert.False(String.IsNullOrWhiteSpace(text), code)
                       End Sub)
    End Sub

    <Fact>
    Public Sub The_sign_in_sentences_that_mean_different_things_differ_in_every_language()
        InEachLanguage(Sub(code)
                           Dim sentences As New List(Of String)()
                           sentences.Add(RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Disabled()))
                           sentences.Add(RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.BadCredentials)))
                           sentences.Add(RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.RateLimited)))
                           sentences.Add(RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.Capacity)))
                           sentences.Add(RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.TlsRequired)))
                           sentences.Add(RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.Unknown)))
                           sentences.Add(RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.CertificateChanged(Pinned, Presented)))
                           sentences.Add(RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Unanswered, "")))
                           sentences.Add(RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.TlsFailed, "")))
                           sentences.Add(RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Network, "")))
                           sentences.Add(RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Protocol, "")))
                           Assert.Equal(sentences.Count, sentences.Distinct(StringComparer.Ordinal).Count())
                       End Sub)
    End Sub

    <Fact>
    Public Sub The_wrong_password_is_this_programs_own_sentence_and_not_a_table_row()
        InEachLanguage(Sub(code)
                           Dim wrong As String = RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.BadCredentials))
                           Assert.False(String.IsNullOrWhiteSpace(wrong))
                           Assert.NotEqual(TableRow("bad-credentials"), wrong)
                           Assert.NotEqual(TableRow("refused"), wrong)
                       End Sub)
    End Sub

    <Fact>
    Public Sub A_sign_in_answer_this_program_does_not_understand_has_its_own_sentence_and_names_nothing()
        InEachLanguage(Sub(code)
                           Dim protocol As String = RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Protocol, SecretDetail))
                           Assert.False(String.IsNullOrWhiteSpace(protocol))
                           Assert.DoesNotContain(SecretDetail, protocol)
                           Assert.NotEqual(TableRow("refused"), protocol)
                           Assert.NotEqual(TableRow("unreachable-server"), protocol)
                       End Sub)
    End Sub

    ' ---------------------------------------------------------------- the row of a sign-in

    <Fact>
    Public Sub SignInRow_names_the_row_the_diagnosis_ladder_starts_from()
        Assert.Same(ConnectionDiagnosisMessages.AdminDisabled, RemoteAdminWords.SignInRow(ExchangeRemoteAdminSignIn.Disabled()))
        Assert.Same(ConnectionDiagnosisMessages.AdminDisabled, RemoteAdminWords.SignInRow(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.AdminDisabled)))
        Assert.Same(ConnectionDiagnosisMessages.RateLimited, RemoteAdminWords.SignInRow(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.RateLimited)))
        Assert.Same(ConnectionDiagnosisMessages.Capacity, RemoteAdminWords.SignInRow(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.Capacity)))
        Assert.Same(ConnectionDiagnosisMessages.TlsRequired, RemoteAdminWords.SignInRow(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.TlsRequired)))
        Assert.Same(ConnectionDiagnosisMessages.Refused, RemoteAdminWords.SignInRow(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.Unknown)))
        Assert.Same(ConnectionDiagnosisMessages.CertificateChanged, RemoteAdminWords.SignInRow(ExchangeRemoteAdminSignIn.CertificateChanged(Pinned, Presented)))
        Assert.Same(ConnectionDiagnosisMessages.Unanswered, RemoteAdminWords.SignInRow(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Timeout, "")))
        Assert.Same(ConnectionDiagnosisMessages.Unanswered, RemoteAdminWords.SignInRow(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Unanswered, "")))
        Assert.Same(ConnectionDiagnosisMessages.TlsFailed, RemoteAdminWords.SignInRow(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.TlsFailed, "")))
        Assert.Same(ConnectionDiagnosisMessages.UnreachableServer, RemoteAdminWords.SignInRow(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Reset, "")))
        Assert.Same(ConnectionDiagnosisMessages.UnreachableServer, RemoteAdminWords.SignInRow(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Network, "")))
    End Sub

    <Fact>
    Public Sub SignInRow_is_nothing_where_the_words_are_this_programs_own_or_there_are_none()
        Assert.Null(RemoteAdminWords.SignInRow(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.BadCredentials)))
        Assert.Null(RemoteAdminWords.SignInRow(ExchangeRemoteAdminSignIn.SignedIn(Nothing)))
        Assert.Null(RemoteAdminWords.SignInRow(ExchangeRemoteAdminSignIn.NeedsFirstContact(Pinned, "")))
        Assert.Null(RemoteAdminWords.SignInRow(ExchangeRemoteAdminSignIn.Cancelled()))
        Assert.Null(RemoteAdminWords.SignInRow(Nothing))
    End Sub

    ' ---------------------------------------------------------------- a call

    <Fact>
    Public Sub An_outcome_with_nothing_to_say_reads_as_nothing_in_every_language()
        InEachLanguage(Sub(code)
                           Assert.Equal("", RemoteAdminWords.ForOutcome(Done()))
                           Assert.Equal("", RemoteAdminWords.ForOutcome(Done(), True))
                           Assert.Equal("", RemoteAdminWords.ForOutcome(ExchangeRemoteAdminOutcome.NewBusy()))
                           Assert.Equal("", RemoteAdminWords.ForOutcome(Cut(ExchangeRemoteAdminUnreachableReason.Cancelled)))
                           Assert.Equal("", RemoteAdminWords.ForOutcome(Cut(ExchangeRemoteAdminUnreachableReason.Cancelled), True))
                           Assert.Equal("", RemoteAdminWords.ForOutcome(Nothing))
                       End Sub)
    End Sub

    <Fact>
    Public Sub The_ten_reply_codes_read_as_ten_different_sentences_in_every_language()
        InEachLanguage(Sub(code)
                           Dim sentences As List(Of String) = TenCodes.Select(Function(c) RemoteAdminWords.ForOutcome(Failed(c))).ToList()
                           Assert.All(sentences, Sub(s) Assert.False(String.IsNullOrWhiteSpace(s), code))
                           Assert.Equal(10, sentences.Distinct(StringComparer.Ordinal).Count())
                           For i As Integer = 0 To TenCodes.Length - 1
                               Assert.Equal(ExchangeAdminText.Failure(TenCodes(i)), sentences(i))
                           Next
                       End Sub)
    End Sub

    <Fact>
    Public Sub The_internal_code_reads_as_the_generic_failure_and_so_does_a_code_nobody_knows()
        InEachLanguage(Sub(code)
                           Dim generic As String = ExchangeAdminText.Failure("a-code-of-a-newer-server")
                           Assert.Equal(generic, RemoteAdminWords.ForOutcome(Failed("internal")))
                           Assert.Equal(generic, RemoteAdminWords.ForOutcome(Failed("a-code-of-a-newer-server")))
                           Assert.Equal(generic, RemoteAdminWords.ForOutcome(Failed("")))
                           Assert.DoesNotContain("a-code-of-a-newer-server", generic)
                       End Sub)
    End Sub

    <Fact>
    Public Sub A_failed_call_reads_the_same_whether_or_not_it_was_a_mutation()
        InEachLanguage(Sub(code)
                           For Each c As String In TenCodes
                               Assert.Equal(RemoteAdminWords.ForOutcome(Failed(c)), RemoteAdminWords.ForOutcome(Failed(c), True))
                           Next
                       End Sub)
    End Sub

    <Fact>
    Public Sub A_lapsed_call_reads_as_the_session_ended_sentence_in_every_language()
        InEachLanguage(Sub(code)
                           Dim lapsed As String = RemoteAdminWords.ForOutcome(ExchangeRemoteAdminOutcome.NewLapsed(SecretDetail))
                           Assert.Equal(RemoteAdminWords.SessionEnded(), lapsed)
                           Assert.False(String.IsNullOrWhiteSpace(lapsed))
                           Assert.DoesNotContain(SecretDetail, lapsed)
                           Assert.Equal(lapsed, RemoteAdminWords.ForOutcome(ExchangeRemoteAdminOutcome.NewLapsed(SecretDetail), True))
                       End Sub)
    End Sub

    <Fact>
    Public Sub An_unreachable_call_reads_as_a_row_of_the_table_and_never_as_its_detail()
        InEachLanguage(Sub(code)
                           For Each reason As ExchangeRemoteAdminUnreachableReason In [Enum].GetValues(GetType(ExchangeRemoteAdminUnreachableReason)).Cast(Of ExchangeRemoteAdminUnreachableReason)()
                               If reason = ExchangeRemoteAdminUnreachableReason.Cancelled Then Continue For
                               Dim text As String = RemoteAdminWords.ForOutcome(Cut(reason))
                               Assert.False(String.IsNullOrWhiteSpace(text), code & " " & reason.ToString())
                               Assert.DoesNotContain(SecretDetail, text)
                           Next
                           Assert.Equal(TableRow("unanswered"), RemoteAdminWords.ForOutcome(Cut(ExchangeRemoteAdminUnreachableReason.Timeout)))
                           Assert.Equal(TableRow("unanswered"), RemoteAdminWords.ForOutcome(Cut(ExchangeRemoteAdminUnreachableReason.Unanswered)))
                           Assert.Equal(TableRow("tls-failed"), RemoteAdminWords.ForOutcome(Cut(ExchangeRemoteAdminUnreachableReason.TlsFailed)))
                           Assert.Equal(TableRow("unreachable-server"), RemoteAdminWords.ForOutcome(Cut(ExchangeRemoteAdminUnreachableReason.Network)))
                           Assert.Equal(TableRow("unreachable-server"), RemoteAdminWords.ForOutcome(Cut(ExchangeRemoteAdminUnreachableReason.Reset)))
                       End Sub)
    End Sub

    <Fact>
    Public Sub A_reply_no_known_shape_fits_reads_as_the_not_understood_sentence_and_names_nothing()
        InEachLanguage(Sub(code)
                           Dim protocol As String = RemoteAdminWords.ForOutcome(ExchangeRemoteAdminOutcome.NewProtocol(SecretDetail))
                           Assert.False(String.IsNullOrWhiteSpace(protocol))
                           Assert.DoesNotContain(SecretDetail, protocol)
                           Assert.Equal(protocol, RemoteAdminWords.ForOutcome(Cut(ExchangeRemoteAdminUnreachableReason.Protocol)))
                           Assert.Equal(protocol, RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Protocol, "")))
                       End Sub)
    End Sub

    <Fact>
    Public Sub A_timeout_or_a_reset_inside_a_mutation_adds_the_may_or_may_not_line_on_its_own_line()
        InEachLanguage(Sub(code)
                           For Each reason As ExchangeRemoteAdminUnreachableReason In {ExchangeRemoteAdminUnreachableReason.Timeout, ExchangeRemoteAdminUnreachableReason.Reset}
                               Dim plain As String = RemoteAdminWords.ForOutcome(Cut(reason))
                               Dim mutation As String = RemoteAdminWords.ForOutcome(Cut(reason), True)
                               Dim lines As String() = mutation.Split({vbCrLf}, StringSplitOptions.None)
                               Assert.Equal(2, lines.Length)
                               Assert.Equal(plain, lines(0))
                               Assert.False(String.IsNullOrWhiteSpace(lines(1)), code)
                               Assert.NotEqual(lines(0), lines(1))
                               Assert.DoesNotContain(SecretDetail, mutation)
                           Next
                           ' the same second line after either cause, and not after a cause that says the change was not sent
                           Dim afterTimeout As String = RemoteAdminWords.ForOutcome(Cut(ExchangeRemoteAdminUnreachableReason.Timeout), True).Split({vbCrLf}, StringSplitOptions.None)(1)
                           Dim afterReset As String = RemoteAdminWords.ForOutcome(Cut(ExchangeRemoteAdminUnreachableReason.Reset), True).Split({vbCrLf}, StringSplitOptions.None)(1)
                           Assert.Equal(afterTimeout, afterReset)
                           For Each reason As ExchangeRemoteAdminUnreachableReason In {ExchangeRemoteAdminUnreachableReason.Network, ExchangeRemoteAdminUnreachableReason.TlsFailed, ExchangeRemoteAdminUnreachableReason.Unanswered}
                               Assert.Equal(RemoteAdminWords.ForOutcome(Cut(reason)), RemoteAdminWords.ForOutcome(Cut(reason), True))
                           Next
                       End Sub)
    End Sub

    ' ---------------------------------------------------------------- the local sentences

    <Fact>
    Public Sub The_session_ended_sentence_takes_a_cause_in_brackets_in_every_language()
        InEachLanguage(Sub(code)
                           Dim plain As String = RemoteAdminWords.SessionEnded()
                           Assert.Equal(plain, RemoteAdminWords.SessionEnded(""))
                           Assert.Equal(plain, RemoteAdminWords.SessionEnded("   "))
                           Dim causes As String() = {RemoteAdminWords.CauseScreenLocked(), RemoteAdminWords.CauseWindowsSession(), RemoteAdminWords.CauseSleep()}
                           Assert.All(causes, Sub(c) Assert.False(String.IsNullOrWhiteSpace(c), code))
                           Assert.Equal(3, causes.Distinct(StringComparer.Ordinal).Count())
                           Dim sentences As New List(Of String)()
                           For Each cause As String In causes
                               Dim withCause As String = RemoteAdminWords.SessionEnded(cause)
                               Assert.Contains(cause, withCause)
                               Assert.NotEqual(plain, withCause)
                               sentences.Add(withCause)
                           Next
                           Assert.Equal(3, sentences.Distinct(StringComparer.Ordinal).Count())
                       End Sub)
    End Sub

    <Fact>
    Public Sub The_device_revoke_not_found_words_are_a_sentence_of_their_own_in_every_language()
        InEachLanguage(Sub(code)
                           Dim words As String = RemoteAdminWords.DeviceRevokeNotFound()
                           Assert.False(String.IsNullOrWhiteSpace(words), code)
                           Assert.NotEqual(ExchangeAdminText.Failure("not-found"), words)
                       End Sub)
    End Sub

    <Fact>
    Public Sub English_sentences_are_pinned()
        InEnglish(Sub()
                      Assert.Equal("Too many attempts in a row.", RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.RateLimited)))
                      Assert.Equal("The server refused this.", RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.Unknown)))
                      Assert.Equal("The server did not answer.", RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Unanswered, "")))
                      Assert.Equal("Cannot reach the server.", RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Network, "")))
                      Assert.Equal("A protected connection could not be set up.", RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.TlsFailed, "")))
                      Assert.Equal("The administrator password is wrong.", RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Refused(ExchangeRefusal.BadCredentials)))
                      Assert.Equal("The server answered in a way this program does not understand.", RemoteAdminWords.ForOutcome(ExchangeRemoteAdminOutcome.NewProtocol("")))
                      Assert.Equal("The session has ended. Sign in again.", RemoteAdminWords.ForOutcome(ExchangeRemoteAdminOutcome.NewLapsed("")))
                      Assert.Equal("The session has ended (the screen was locked). Sign in again.", RemoteAdminWords.SessionEnded(RemoteAdminWords.CauseScreenLocked()))
                      Assert.Equal("The session has ended (the Windows session was closed or disconnected). Sign in again.", RemoteAdminWords.SessionEnded(RemoteAdminWords.CauseWindowsSession()))
                      Assert.Equal("The session has ended (the PC went to sleep). Sign in again.", RemoteAdminWords.SessionEnded(RemoteAdminWords.CauseSleep()))
                      Assert.Equal("The server did not answer." & vbCrLf & "The change may or may not have been made.",
                                   RemoteAdminWords.ForOutcome(Cut(ExchangeRemoteAdminUnreachableReason.Timeout), True))
                      Assert.Equal("The device is not on the server: it may be removed already.", RemoteAdminWords.DeviceRevokeNotFound())
                      Assert.Equal("This server does not look like the one you connected to before." & vbCrLf & "Pinned fingerprint: " & Pinned & vbCrLf & "Presented fingerprint: " & Presented,
                                   RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.CertificateChanged(Pinned, Presented)))
                  End Sub)
    End Sub

    ' ---------------------------------------------------------------- every sentence, all together

    <Fact>
    Public Sub No_sentence_holds_a_smart_double_quote_or_the_detail_and_a_language_never_shows_another_ones_script()
        InEachLanguage(Sub(code)
                           Dim sentences As New List(Of String)()
                           For Each pair As KeyValuePair(Of String, ExchangeRemoteAdminSignIn) In SpeakingSignIns()
                               sentences.Add(RemoteAdminWords.ForSignIn(pair.Value))
                           Next
                           For Each c As String In TenCodes
                               sentences.Add(RemoteAdminWords.ForOutcome(Failed(c)))
                           Next
                           sentences.Add(RemoteAdminWords.ForOutcome(ExchangeRemoteAdminOutcome.NewLapsed(SecretDetail)))
                           sentences.Add(RemoteAdminWords.ForOutcome(ExchangeRemoteAdminOutcome.NewProtocol(SecretDetail)))
                           sentences.Add(RemoteAdminWords.ForOutcome(Cut(ExchangeRemoteAdminUnreachableReason.Timeout), True))
                           sentences.Add(RemoteAdminWords.ForOutcome(Cut(ExchangeRemoteAdminUnreachableReason.Reset), True))
                           sentences.Add(RemoteAdminWords.SessionEnded(RemoteAdminWords.CauseScreenLocked()))
                           sentences.Add(RemoteAdminWords.SessionEnded(RemoteAdminWords.CauseWindowsSession()))
                           sentences.Add(RemoteAdminWords.SessionEnded(RemoteAdminWords.CauseSleep()))
                           sentences.Add(RemoteAdminWords.DeviceRevokeNotFound())
                           Dim cyrillicAllowed As Boolean = (code = "ru" OrElse code = "uk")
                           For Each s As String In sentences
                               Assert.False(String.IsNullOrWhiteSpace(s), code)
                               Assert.False(s.Contains(ChrW(&H201C).ToString()), code)
                               Assert.False(s.Contains(ChrW(&H201D).ToString()), code)
                               Assert.False(s.Contains(ChrW(&H201E).ToString()), code)
                               Assert.DoesNotContain(SecretDetail, s)
                               If Not cyrillicAllowed Then
                                   Assert.False(s.Any(Function(ch) AscW(ch) >= &H400 AndAlso AscW(ch) <= &H4FF), code & ": a Cyrillic letter leaked into " & s)
                               End If
                           Next
                       End Sub)
    End Sub

    ' ---------------------------------------------------------------- the tag in the source

    ''' <summary>The comment lines directly above a declaration, walking up over contiguous comment lines (the doc block included).</summary>
    Private Shared Function CommentBlockAbove(lines As String(), declarationFragment As String) As String
        Dim index As Integer = Array.FindIndex(lines, Function(l) l.Contains(declarationFragment))
        Assert.True(index >= 0, declarationFragment & " not found in RemoteAdminWords.vb")
        Dim block As New List(Of String)()
        Dim i As Integer = index - 1
        While i >= 0 AndAlso lines(i).TrimStart().StartsWith("'", StringComparison.Ordinal)
            block.Add(lines(i))
            i -= 1
        End While
        Return String.Join(vbLf, block)
    End Function

    <Fact>
    Public Sub The_four_local_sentences_carry_the_registry_note_tag_in_the_source()
        Dim text As String = ExchangeTestSupport.ReadSource(Path.Combine("Core", "RemoteAdmin", "RemoteAdminWords.vb"))
        Dim lines As String() = text.Split({vbCrLf, vbLf}, StringSplitOptions.None)
        For Each declaration As String In {"Function SessionEnded(", "Function BadPassword(", "Function NotUnderstood(", "Function MayOrMayNot("}
            Dim above As String = CommentBlockAbove(lines, declaration)
            Assert.True(above.Contains("registry-note candidate (L0278 9.3)"), declaration & " has no registry-note tag in the comment lines above it")
        Next
    End Sub

    <Fact>
    Public Sub The_tag_is_not_put_on_a_sentence_the_contract_already_has()
        Dim text As String = ExchangeTestSupport.ReadSource(Path.Combine("Core", "RemoteAdmin", "RemoteAdminWords.vb"))
        Dim lines As String() = text.Split({vbCrLf, vbLf}, StringSplitOptions.None)
        For Each declaration As String In {"Function Row(", "Function UnreachableRow(", "Function CertificateStop(", "Function SignInRow("}
            Assert.DoesNotContain("registry-note candidate", CommentBlockAbove(lines, declaration))
        Next
    End Sub

    <Fact>
    Public Sub No_sentence_is_a_literal_of_this_file_but_goes_through_the_table_or_the_layer()
        ' a sentence of the contract is never retyped here (item DA); a local sentence is a key of the Companion's own table
        Dim code As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource(Path.Combine("Core", "RemoteAdmin", "RemoteAdminWords.vb")))
        Assert.DoesNotContain("The server did not answer", code)
        Assert.DoesNotContain("Too many attempts", code)
        Assert.DoesNotContain("MessageBox", code)
        Assert.DoesNotContain("Toast", code)
        Assert.Contains("ConnectionDiagnosisLocalization.GetMessageText", code)
        Assert.Contains("ExchangeAdminText.Failure", code)
    End Sub

End Class
