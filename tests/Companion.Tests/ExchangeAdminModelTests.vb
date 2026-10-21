Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The pure rules of the exchange server administration (ticket 168): the login an account may have, the address clients are
''' told, the third-party port range, what the accounts and devices lists draw in a state, the six points of the statement of
''' what the operator can see - and the words of all of it in every language.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ExchangeAdminModelTests

    ' --- the login ------------------------------------------------------------------------------------

    <Theory>
    <InlineData("alice", "Accepted")>
    <InlineData("Alice", "Accepted")>
    <InlineData("  alice  ", "Accepted")>
    <InlineData("a.b_c-d9", "Accepted")>
    <InlineData("abc", "Accepted")>
    <InlineData("ab", "TooShort")>
    <InlineData("", "TooShort")>
    <InlineData("   ", "TooShort")>
    <InlineData("has space", "BadCharacter")>
    <InlineData("alice@home", "BadCharacter")>
    <InlineData("алиса", "BadCharacter")>
    <InlineData("tab" & vbTab & "x", "BadCharacter")>
    Public Sub A_login_is_three_to_sixty_four_of_lower_case_digits_dot_underscore_hyphen(text As String, expected As String)
        Assert.Equal(expected, ExchangeLoginRules.Check(text).ToString())
    End Sub

    <Fact>
    Public Sub The_length_bounds_are_exact_and_a_login_is_folded_to_lower_case()
        Assert.Equal(ExchangeLoginRules.Verdict.Accepted, ExchangeLoginRules.Check(New String("a"c, 64)))
        Assert.Equal(ExchangeLoginRules.Verdict.TooLong, ExchangeLoginRules.Check(New String("a"c, 65)))
        Assert.Equal("alice", ExchangeLoginRules.Normalize("  ALice "))
        Assert.Equal("", ExchangeLoginRules.Normalize(Nothing))
    End Sub

    <Theory>
    <InlineData("Serhii", "serhii")>
    <InlineData("Jane Doe", "janedoe")>
    <InlineData("Admin", "admin")>
    <InlineData("A", "")>
    <InlineData("", "")>
    <InlineData("Иван", "")>
    Public Sub The_wizards_first_guess_fits_the_rule_or_is_empty(userName As String, expected As String)
        Dim s As String = ExchangeLoginRules.SuggestFrom(userName)
        Assert.Equal(expected, s)
        If s.Length > 0 Then Assert.Equal(ExchangeLoginRules.Verdict.Accepted, ExchangeLoginRules.Check(s))
    End Sub

    ' --- the address -----------------------------------------------------------------------------------

    <Theory>
    <InlineData("", "Empty")>
    <InlineData("  ", "Empty")>
    <InlineData("203.0.113.7:22022", "Accepted")>
    <InlineData("exchange.example.org:22022", "Accepted")>
    <InlineData("[2001:db8::1]:22022", "Accepted")>
    <InlineData("203.0.113.7", "NotHostPort")>
    <InlineData(":22022", "NotHostPort")>
    <InlineData("2001:db8::1:22022", "NotHostPort")>
    <InlineData("203.0.113.7:0", "BadPort")>
    <InlineData("203.0.113.7:70000", "BadPort")>
    <InlineData("203.0.113.7:abc", "BadPort")>
    <InlineData("203.0.113.7:", "BadPort")>
    <InlineData("a b:22022", "NoHost")>
    <InlineData("user@host:22022", "NoHost")>
    <InlineData("host/path:22022", "NoHost")>
    <InlineData("localhost:22022", "Loopback")>
    <InlineData("LOCALHOST:22022", "Loopback")>
    <InlineData("my.localhost:22022", "Loopback")>
    <InlineData("127.0.0.1:22022", "Loopback")>
    <InlineData("0.0.0.0:22022", "Loopback")>
    <InlineData("[::1]:22022", "Loopback")>
    <InlineData("[::]:22022", "Loopback")>
    Public Sub The_public_address_follows_the_rules_the_binary_applies(text As String, expected As String)
        Assert.Equal(expected, ExchangeOperatorEndpoint.Check(text).ToString())
    End Sub

    <Fact>
    Public Sub The_suggestion_prefers_the_known_public_host_then_the_lan_address_and_never_a_loopback()
        Assert.Equal("203.0.113.7:22022", ExchangeOperatorEndpoint.Suggest("203.0.113.7", "192.168.1.5", 22022))
        Assert.Equal("192.168.1.5:2222", ExchangeOperatorEndpoint.Suggest("", "192.168.1.5", 2222))
        Assert.Equal("192.168.1.5:22022", ExchangeOperatorEndpoint.Suggest(Nothing, "192.168.1.5", 0))
        Assert.Equal("192.168.1.5:22022", ExchangeOperatorEndpoint.Suggest("127.0.0.1", "192.168.1.5", 22022))
        Assert.Equal("[2001:db8::1]:22022", ExchangeOperatorEndpoint.Suggest("2001:db8::1", "", 22022))
        Assert.Equal("", ExchangeOperatorEndpoint.Suggest("", "", 22022))
        Assert.Equal("", ExchangeOperatorEndpoint.Suggest("localhost", "", 22022))
        Assert.Equal("", ExchangeOperatorEndpoint.Suggest("0.0.0.0", "127.0.0.1", 22022))
    End Sub

    ' --- the third-party range -------------------------------------------------------------------------

    <Fact>
    Public Sub The_range_is_checked_for_order_bounds_and_the_listen_port()
        Assert.Equal(ExchangePortRangeRules.Verdict.Accepted, ExchangePortRangeRules.Check(40000, 40100, 22022))
        Assert.Equal(ExchangePortRangeRules.Verdict.Accepted, ExchangePortRangeRules.Check(40000, 40000, 22022))
        Assert.Equal(ExchangePortRangeRules.Verdict.Order, ExchangePortRangeRules.Check(40100, 40000, 22022))
        Assert.Equal(ExchangePortRangeRules.Verdict.Bounds, ExchangePortRangeRules.Check(80, 90, 22022))
        Assert.Equal(ExchangePortRangeRules.Verdict.Bounds, ExchangePortRangeRules.Check(40000, 70000, 22022))
        Assert.Equal(ExchangePortRangeRules.Verdict.ContainsListenPort, ExchangePortRangeRules.Check(22000, 23000, 22022))
        Assert.Equal(ExchangePortRangeRules.Verdict.ContainsListenPort, ExchangePortRangeRules.Check(22022, 22030, 22022))
        Assert.Equal(ExchangePortRangeRules.Verdict.ContainsListenPort, ExchangePortRangeRules.Check(22010, 22022, 22022))
        Assert.Equal(101, ExchangePortRangeRules.Count(40000, 40100))
        Assert.Equal(0, ExchangePortRangeRules.Count(40100, 40000))
    End Sub

    <Theory>
    <InlineData("40000-40100", True, 40000, 40100)>
    <InlineData(" 41000 - 41100 ", True, 41000, 41100)>
    <InlineData("40000", False, 0, 0)>
    <InlineData("a-b", False, 0, 0)>
    <InlineData("1-2-3", False, 0, 0)>
    <InlineData("", False, 0, 0)>
    <InlineData("-5-10", False, 0, 0)>
    Public Sub The_range_the_binary_reports_is_read_back(text As String, ok As Boolean, low As Integer, high As Integer)
        Dim l As Integer, h As Integer
        Assert.Equal(ok, ExchangePortRangeRules.TryParse(text, l, h))
        If ok Then
            Assert.Equal(low, l)
            Assert.Equal(high, h)
            Assert.Equal(ExchangePortRangeRules.Format(low, high), "" & low & "-" & high)
        End If
    End Sub

    ' --- the accounts and devices lists -------------------------------------------------------------------

    Private Shared Function View(accounts As String(), Optional devicesOf As String = "", Optional deviceIds As String() = Nothing,
                                 Optional route As ExchangeAdminRoute = ExchangeAdminRoute.Pipe) As ExchangeAccountsView
        Dim v As New ExchangeAccountsView With {.Reached = True, .Route = route}
        v.Accounts = accounts.Select(Function(a, i) New ExchangeAdminAccount With {.Login = a, .Devices = If(a = devicesOf AndAlso deviceIds IsNot Nothing, deviceIds.Length, i)}).ToList()
        If devicesOf.Length > 0 Then
            v.DevicesOf = devicesOf
            v.Devices = If(deviceIds, Array.Empty(Of String)()).Select(Function(id) New ExchangeAdminDevice With {.DeviceId = id, .Name = "PC " & id}).ToList()
        End If
        Return v
    End Function

    ''' <summary>Ticket 224 rule 1: only an invitation nobody has used can be withdrawn; the list itself is always there.</summary>
    <Fact>
    Public Sub Only_an_unused_invitation_can_be_withdrawn()
        Dim v As ExchangeAccountsView = View({"alice"})
        v.Invitations = {New ExchangeAdminInvitation With {.CodeHash = "open", .Kind = "member", .Used = False},
                         New ExchangeAdminInvitation With {.CodeHash = "spent", .Kind = "guest", .Used = True, .UsedBy = "visitor"}}
        Assert.True(ExchangeAccountsModel.Build(v, "", "").ShowInvitations)
        Assert.False(ExchangeAccountsModel.Build(v, "", "").ShowWithdrawInvitation)
        Assert.True(ExchangeAccountsModel.Build(v, "", "", "open").ShowWithdrawInvitation)
        Assert.False(ExchangeAccountsModel.Build(v, "", "", "spent").ShowWithdrawInvitation)
        Assert.False(ExchangeAccountsModel.Build(v, "", "", "nobody").ShowWithdrawInvitation)
    End Sub

    ''' <summary>Ticket 224 rule 5: the limits button belongs to a selected account, as reset and remove do.</summary>
    <Fact>
    Public Sub The_limits_of_an_account_are_offered_only_for_a_selected_account()
        Dim v As ExchangeAccountsView = View({"alice", "bob"})
        Assert.False(ExchangeAccountsModel.Build(v, "", "").ShowLimits)
        Assert.True(ExchangeAccountsModel.Build(v, "alice", "").ShowLimits)
        Assert.False(ExchangeAccountsModel.Build(v, "carol", "").ShowLimits)
    End Sub

    <Fact>
    Public Sub A_server_that_cannot_be_reached_draws_the_notice_and_no_list()
        Dim m As ExchangeAccountsModel = ExchangeAccountsModel.Build(New ExchangeAccountsView(), "", "")
        Assert.True(m.ShowNotReached)
        Assert.False(m.Reached OrElse m.ShowAdd OrElse m.ShowAccountsList OrElse m.ShowReset OrElse m.ShowRemove OrElse m.ShowDevices OrElse m.ShowRevoke)
        ' not asked at all (the group hides the whole block): not even the notice
        Assert.False(ExchangeAccountsModel.Build(Nothing, "", "").ShowNotReached)
    End Sub

    <Fact>
    Public Sub No_account_says_the_server_will_not_start_and_offers_only_add()
        Dim m As ExchangeAccountsModel = ExchangeAccountsModel.Build(View(Array.Empty(Of String)()), "", "")
        Assert.True(m.Reached)
        Assert.True(m.ShowFirstAccountHint)
        Assert.True(m.ShowAdd)
        Assert.False(m.ShowAccountsList)
        Assert.False(m.ShowReset OrElse m.ShowRemove)
        Assert.Equal(0, m.AccountCount)
    End Sub

    <Fact>
    Public Sub An_account_selected_offers_reset_and_remove_and_its_devices_offer_revoke_only_with_one_selected()
        Dim v As ExchangeAccountsView = View({"alice", "bob"}, "bob", {"AAAAAAAAAAAAAAAAAAAAAA", "BBBBBBBBBBBBBBBBBBBBBB"})
        Dim none As ExchangeAccountsModel = ExchangeAccountsModel.Build(v, "", "")
        Assert.True(none.ShowAccountsList)
        Assert.False(none.ShowReset OrElse none.ShowRemove OrElse none.ShowDevices OrElse none.ShowRevoke)
        Assert.Equal(2, none.AccountCount)

        Dim picked As ExchangeAccountsModel = ExchangeAccountsModel.Build(v, "BOB", "")
        Assert.True(picked.ShowReset)
        Assert.True(picked.ShowRemove)
        Assert.True(picked.ShowDevices)
        Assert.False(picked.ShowRevoke, "no device selected yet")
        Assert.True(ExchangeAccountsModel.Build(v, "bob", "BBBBBBBBBBBBBBBBBBBBBB").ShowRevoke)
        Assert.False(ExchangeAccountsModel.Build(v, "bob", "unknown-device-id").ShowRevoke, "a device the list does not hold")
        ' a selection that is not an account (it was removed meanwhile) offers nothing
        Dim gone As ExchangeAccountsModel = ExchangeAccountsModel.Build(v, "carol", "")
        Assert.False(gone.ShowReset OrElse gone.ShowRemove OrElse gone.ShowDevices)
        ' the devices shown belong to the SELECTED account only
        Assert.False(ExchangeAccountsModel.Build(v, "alice", "").ShowDevices)
    End Sub

    <Fact>
    Public Sub An_offline_read_says_changes_apply_when_the_server_starts()
        Assert.True(ExchangeAccountsModel.Build(View({"alice"}, route:=ExchangeAdminRoute.Offline), "", "").ShowOfflineNote)
        Assert.False(ExchangeAccountsModel.Build(View({"alice"}, route:=ExchangeAdminRoute.Pipe), "", "").ShowOfflineNote)
    End Sub

    <Fact>
    Public Sub The_totals_count_every_device_of_every_account()
        Dim v As ExchangeAccountsView = View({"a1", "b2", "c3"})
        Dim m As ExchangeAccountsModel = ExchangeAccountsModel.Build(v, "", "")
        Assert.Equal(3, m.AccountCount)
        Assert.Equal(0 + 1 + 2, m.DeviceTotal)
    End Sub

    ' --- the statement -----------------------------------------------------------------------------------

    Private Shared Function GoStatementIds() As List(Of String)
        Dim path As String = System.IO.Path.Combine(ExchangeTestSupport.RepoRoot(), "exchange-server", "internal", "statement", "statement.go")
        If Not File.Exists(path) Then Return Nothing
        Dim text As String = File.ReadAllText(path)
        Dim start As Integer = text.IndexOf("var Points", StringComparison.Ordinal)
        Dim ids As New List(Of String)()
        For Each m As Match In Regex.Matches(text.Substring(start), "\{""([a-z]+(?:-[a-z]+)*)"",\s*""")
            ids.Add(m.Groups(1).Value)
        Next
        Return ids
    End Function

    ''' <summary>The ids are the Go module's, in its order: the statement shown here is the statement the server owns (167 rule 8).</summary>
    <Fact>
    Public Sub The_statements_points_are_the_go_modules_points_in_the_same_order()
        Dim go As List(Of String) = GoStatementIds()
        If go Is Nothing Then Return   ' a clone without the exchange-server tree has nothing to compare with
        Assert.Equal(go, ExchangeStatement.Ids.ToList())
        Assert.Equal(7, ExchangeStatement.Ids.Length)
    End Sub

    <Fact>
    Public Sub Every_point_is_said_in_every_language_and_no_two_points_share_a_sentence()
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                Dim texts As List(Of String) = ExchangeStatement.Paragraphs()
                Assert.Equal(8, texts.Count)
                Assert.All(texts, Sub(t) Assert.False(String.IsNullOrWhiteSpace(t), code))
                Assert.Equal(8, texts.Distinct().Count())
                If code <> "ru" Then
                    ' a translation, never the Russian source showing through (the layer returns the key for a missing entry)
                    Dim russian As List(Of String)
                    Localization.CurrentCode = "ru"
                    russian = ExchangeStatement.Paragraphs()
                    Localization.CurrentCode = code
                    For i As Integer = 0 To texts.Count - 1
                        Assert.NotEqual(russian(i), texts(i))
                    Next
                End If
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
        Assert.Equal("", ExchangeStatement.Text("not-a-point"))
    End Sub

    ''' <summary>DEVICE-EXCHANGE 7.9 in substance, in English: relayed broadcast, plain tunnel, access in memory, opaque SFTP, the open
    ''' multi-user limit, flat trust and the clipboard value in memory (ticket 229) - the facts an operator must not be left unaware of.</summary>
    <Fact>
    Public Sub The_english_statement_says_what_the_contract_says_the_operator_can_see()
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = "en"
            Dim must As New Dictionary(Of String, String()) From {
                {"relay-visible", {"broadcast", "through this exchange server", "operator", "in the clear", "never written to disk"}},
                {"tunnel-broadcast-plain", {"tunnel", "RTSP", "HTTP", "operator"}},
                {"access-visible", {"access details", "memory", "operator", "never written to disk", "never logged"}},
                {"sftp-opaque", {"SSH", "cannot read"}},
                {"multi-user-limit", {"other people's accounts", "limit", "end to end", "open work"}},
                {"flat-trust", {"every other device", "remove", "pairing codes", "access details", "remove itself"}},
                {"clipboard-visible", {"memory", "nowhere else", "never written to disk", "lost when the server restarts",
                                       "operator can read it in the clear", "one value", "size limit"}}}
            For Each id As String In ExchangeStatement.Ids
                Dim text As String = ExchangeStatement.Text(id)
                For Each k As String In must(id)
                    Assert.True(text.Contains(k, StringComparison.OrdinalIgnoreCase), id & " does not say '" & k & "': " & text)
                Next
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ' --- the words -----------------------------------------------------------------------------------------

    ''' <summary>Every failure, outcome, step and verdict has a sentence of its own in all 13 languages (a code never reaches the screen).</summary>
    <Fact>
    Public Sub Every_outcome_has_its_own_sentence_in_every_language()
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                Dim failures As List(Of String) = {ExchangeAdminCodes.BadRequest, ExchangeAdminCodes.Exists, ExchangeAdminCodes.BadLogin,
                                                   ExchangeAdminCodes.WeakPassword, ExchangeAdminCodes.NotFound, ExchangeAdminCodes.NotReached,
                                                   ExchangeAdminCodes.BadKind, ExchangeAdminCodes.BadLimit,
                                                   ExchangeAdminCodes.NoAccount, ExchangeAdminCodes.Internal}.Select(Function(c) ExchangeAdminText.Failure(c)).ToList()
                Assert.All(failures, Sub(t) Assert.False(String.IsNullOrWhiteSpace(t), code))
                Assert.Equal(failures.Count, failures.Distinct().Count())

                Dim steps As List(Of String) = [Enum].GetValues(GetType(ExchangeWizardStep)).Cast(Of ExchangeWizardStep)().Select(Function(s) ExchangeAdminText.WizardStep(s)).ToList()
                Assert.Equal(steps.Count, steps.Distinct().Count())

                Dim enrollFailed As New ExchangeWizardResult With {.Outcome = ExchangeWizardOutcome.EnrollFailed,
                    .EnrollResult = New ExchangeEnrollResult(ExchangeEnrollOutcome.Refused, ExchangeRefusal.BadCredentials, "")}
                Dim outcomes As List(Of String) = [Enum].GetValues(GetType(ExchangeWizardOutcome)).Cast(Of ExchangeWizardOutcome)().
                    Select(Function(o) ExchangeAdminText.WizardOutcome(New ExchangeWizardResult With {.Outcome = o, .EnrollResult = enrollFailed.EnrollResult})).ToList()
                Assert.All(outcomes, Sub(t) Assert.False(String.IsNullOrWhiteSpace(t), code))
                Assert.Equal(outcomes.Count, outcomes.Distinct().Count())

                For Each v As ExchangeOperatorEndpoint.Verdict In {ExchangeOperatorEndpoint.Verdict.Loopback, ExchangeOperatorEndpoint.Verdict.NotHostPort}
                    Assert.False(String.IsNullOrWhiteSpace(ExchangeAdminText.EndpointVerdict(v)), code & " " & v.ToString())
                Next
                Assert.Equal("", ExchangeAdminText.EndpointVerdict(ExchangeOperatorEndpoint.Verdict.Accepted))
                For Each v As ExchangePortRangeRules.Verdict In {ExchangePortRangeRules.Verdict.Order, ExchangePortRangeRules.Verdict.Bounds, ExchangePortRangeRules.Verdict.ContainsListenPort}
                    Assert.False(String.IsNullOrWhiteSpace(ExchangeAdminText.RangeVerdict(v)), code & " " & v.ToString())
                Next
                Assert.Equal("", ExchangeAdminText.RangeVerdict(ExchangePortRangeRules.Verdict.Accepted))

                Dim certs As List(Of String) = [Enum].GetValues(GetType(ExchangeProcess.CertificateOutcome)).Cast(Of ExchangeProcess.CertificateOutcome)().
                    Select(Function(o) ExchangeAdminText.CertificateResult(New ExchangeProcess.CertificateResult With {.Outcome = o, .Fingerprint = "SHA256:abc"})).ToList()
                Assert.All(certs, Sub(t) Assert.False(String.IsNullOrWhiteSpace(t), code))
                Assert.Equal(certs.Count, certs.Distinct().Count())
                Assert.Contains("SHA256:abc", ExchangeAdminText.CertificateResult(New ExchangeProcess.CertificateResult With {.Outcome = ExchangeProcess.CertificateOutcome.Done, .Fingerprint = "SHA256:abc"}))

                Assert.Contains("alice", ExchangeAdminText.AccountAdded("alice"))
                Assert.Contains("alice", ExchangeAdminText.RemoveAccountQuestion("alice"))
                Assert.Contains("PC 1", ExchangeAdminText.RevokeDeviceQuestion("PC 1"))
                Assert.Contains("12", ExchangeAdminText.RangeCount(12))
                Assert.NotEqual(ExchangeAdminText.PasswordReset("alice", True), ExchangeAdminText.PasswordReset("alice", False))
                Assert.NotEqual(ExchangeAdminText.CertificateKind(True), ExchangeAdminText.CertificateKind(False))
                Assert.Contains("203.0.113.7:22022", ExchangeAdminText.EndpointSuggest("203.0.113.7:22022"))
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ''' <summary>House text style of anything a person reads (never a code literal): ".." and a plain hyphen, no "..." and no em dash;
    ''' the one example address is a documentation address; the product name is never typed into a table.</summary>
    <Fact>
    Public Sub The_new_words_keep_the_house_style()
        For Each file As String In {"Core\ExchangeAdminText.vb", "Localization\Localization.ExchangeAdmin.vb"}
            Dim text As String = ExchangeTestSupport.ReadSource(file)
            Dim code As String = String.Join(vbLf, text.Split({vbLf}, StringSplitOptions.None).Where(Function(l) Not l.TrimStart().StartsWith("'", StringComparison.Ordinal)))
            Assert.DoesNotContain("...", code)
            Assert.DoesNotContain(ChrW(&H2014).ToString(), code)
            Assert.DoesNotContain(ChrW(&H2026).ToString(), code)
            Assert.DoesNotContain(ChrW(&H201C).ToString(), code)
            Assert.DoesNotContain(ChrW(&H201D).ToString(), code)
            Assert.DoesNotContain(ProductNames.ExchangeServer, code)
        Next
        Dim addresses As MatchCollection = Regex.Matches(ExchangeTestSupport.ReadSource("Localization\Localization.ExchangeAdmin.vb"), "\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(?::\d+)?\b")
        For Each m As Match In addresses
            Assert.True(m.Value.StartsWith("203.0.113.", StringComparison.Ordinal) OrElse m.Value.StartsWith("127.0.0.1", StringComparison.Ordinal) OrElse m.Value.StartsWith("0.0.0.0", StringComparison.Ordinal),
                        "an address that is not a documentation or loopback one: " & m.Value)
        Next
    End Sub

End Class
