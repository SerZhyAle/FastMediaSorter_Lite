Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Reflection
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 180 T-4 and T-5: the "Exchange account" group. The pure model of which rows exist (hidden, never disabled), the words (one sentence per state and
''' per refusal, never the raw reason), the group's absence while the client gate is shut, and the window's drawing from a running agent: the account, this
''' device marked, the other devices with presence, a pairing code that leaves the screen and the memory when it expires. The modal confirmations and the
''' clipboard are not driven here (they need a person); the agent behind them is tested in ExchangeDeviceAgentTests.
''' </summary>
<Collection("Exchange device host")>
Public Class ExchangeAccountGroupTests

    Private Shared Function Status(state As ExchangeAgentState, Optional features As String() = Nothing, Optional reason As String = "") As ExchangeDeviceStatus
        Return New ExchangeDeviceStatus(True, state, reason, If(features, New String() {"directory", "tunnel", "pairing"}), ExchangeWireTestKit.IdOf("D"c), "PC", "alice", "host:22022",
                                        ExchangeCertificateKind.Pinned, "SHA256:x")
    End Function

    ' ---------------------------------------------------------------- which rows exist

    <Fact>
    Public Sub With_the_gate_shut_there_is_no_group_at_all()
        Dim m As ExchangeAccountGroupModel = ExchangeAccountGroupModel.Build(Status(ExchangeAgentState.Online), False)
        Assert.False(m.Visible)
        Assert.False(m.ShowSignIn Or m.ShowAcceptCertificate Or m.ShowAddDevice Or m.ShowDevices Or m.ShowRevoke Or m.ShowDisconnect)
    End Sub

    <Fact>
    Public Sub Nothing_configured_offers_only_the_sign_in()
        For Each s As ExchangeDeviceStatus In {Nothing, New ExchangeDeviceStatus(False, ExchangeAgentState.NotConfigured, "", Array.Empty(Of String)(), "", "", "", "", ExchangeCertificateKind.None, "")}
            Dim m As ExchangeAccountGroupModel = ExchangeAccountGroupModel.Build(s, True)
            Assert.True(m.Visible)
            Assert.True(m.ShowSignIn)
            Assert.False(m.ShowAcceptCertificate Or m.ShowAddDevice Or m.ShowDevices Or m.ShowRevoke Or m.ShowDisconnect)
        Next
    End Sub

    <Fact>
    Public Sub Online_offers_pairing_the_devices_and_disconnect_by_what_the_server_offers()
        Dim all As ExchangeAccountGroupModel = ExchangeAccountGroupModel.Build(Status(ExchangeAgentState.Online), True)
        Assert.True(all.ShowAddDevice And all.ShowDevices And all.ShowRevoke And all.ShowDisconnect)
        Assert.False(all.ShowSignIn Or all.ShowAcceptCertificate)

        ' a server without `pairing` has no "Add a device"; one without `directory` has no list - the rows are absent, not disabled
        Dim noPairing As ExchangeAccountGroupModel = ExchangeAccountGroupModel.Build(Status(ExchangeAgentState.Online, {"directory"}), True)
        Assert.False(noPairing.ShowAddDevice)
        Assert.True(noPairing.ShowDevices)
        Dim noDirectory As ExchangeAccountGroupModel = ExchangeAccountGroupModel.Build(Status(ExchangeAgentState.Online, {"pairing"}), True)
        Assert.True(noDirectory.ShowAddDevice)
        Assert.False(noDirectory.ShowDevices Or noDirectory.ShowRevoke)
    End Sub

    <Fact>
    Public Sub A_sign_in_again_state_offers_sign_in_and_disconnect_and_a_changed_certificate_offers_the_explicit_accept()
        Dim signIn As ExchangeAccountGroupModel = ExchangeAccountGroupModel.Build(Status(ExchangeAgentState.SignInAgain), True)
        Assert.True(signIn.ShowSignIn And signIn.ShowDisconnect)
        Assert.False(signIn.ShowAddDevice Or signIn.ShowDevices Or signIn.ShowAcceptCertificate)
        Dim pin As ExchangeAccountGroupModel = ExchangeAccountGroupModel.Build(Status(ExchangeAgentState.PinMismatch), True)
        Assert.True(pin.ShowAcceptCertificate And pin.ShowDisconnect)
        Assert.False(pin.ShowSignIn Or pin.ShowAddDevice Or pin.ShowDevices)
    End Sub

    <Theory>
    <InlineData("Connecting")>
    <InlineData("Reconnecting")>
    <InlineData("UpdateNeeded")>
    <InlineData("Defect")>
    <InlineData("Stopped")>
    Public Sub Every_other_state_offers_only_disconnect(stateName As String)
        Dim state As ExchangeAgentState = DirectCast([Enum].Parse(GetType(ExchangeAgentState), stateName), ExchangeAgentState)
        Dim m As ExchangeAccountGroupModel = ExchangeAccountGroupModel.Build(Status(state), True)
        Assert.True(m.ShowDisconnect)
        Assert.False(m.ShowSignIn Or m.ShowAcceptCertificate Or m.ShowAddDevice Or m.ShowDevices Or m.ShowRevoke)
    End Sub

    ' ---------------------------------------------------------------- the words

    <Fact>
    Public Sub Every_state_has_its_own_sentence()
        Dim lines As New Dictionary(Of ExchangeAgentState, String)
        For Each state As ExchangeAgentState In [Enum].GetValues(GetType(ExchangeAgentState))
            If state = ExchangeAgentState.NotConfigured Then Continue For
            lines(state) = ExchangeDeviceText.StateLine(Status(state))
            Assert.False(String.IsNullOrWhiteSpace(lines(state)), state.ToString())
        Next
        Assert.Equal(lines.Count, lines.Values.Distinct().Count())   ' no two states share a sentence
        Assert.NotEqual(lines(ExchangeAgentState.Reconnecting), ExchangeDeviceText.StateLine(Status(ExchangeAgentState.Reconnecting, Nothing, "rate-limited")))
        Assert.NotEqual(ExchangeDeviceText.StateLine(Status(ExchangeAgentState.Reconnecting, Nothing, "rate-limited")), ExchangeDeviceText.StateLine(Status(ExchangeAgentState.Reconnecting, Nothing, "capacity")))
        Assert.Contains("alice", lines(ExchangeAgentState.Online))
        Assert.Contains("host:22022", lines(ExchangeAgentState.Online))
        Assert.NotEqual(lines(ExchangeAgentState.SignInAgain), ExchangeDeviceText.StateLine(Nothing))
    End Sub

    <Fact>
    Public Sub A_reason_is_one_sentence_and_never_the_raw_wire_token()
        Dim seen As New HashSet(Of String)
        For Each reason As String In {"bad-credentials", "device-revoked", "signed-out", "no-token", "bad-address"}
            Dim detail As String = ExchangeDeviceText.StateDetail(Status(ExchangeAgentState.SignInAgain, Nothing, reason))
            Assert.False(String.IsNullOrWhiteSpace(detail), reason)
            Assert.DoesNotContain(reason, detail)
            Assert.True(seen.Add(detail), "two reasons share a sentence: " & reason)
        Next
        Assert.Equal("", ExchangeDeviceText.StateDetail(Status(ExchangeAgentState.SignInAgain, Nothing, "anything-new")))
        Assert.NotEqual(ExchangeDeviceText.StateDetail(Status(ExchangeAgentState.PinMismatch, Nothing, "pin-mismatch")), ExchangeDeviceText.StateDetail(Status(ExchangeAgentState.PinMismatch, Nothing, "untrusted")))
    End Sub

    <Fact>
    Public Sub Every_enrollment_outcome_and_refusal_has_one_sentence_and_success_has_none()
        Dim sentences As New List(Of String)
        For Each outcome As ExchangeEnrollOutcome In [Enum].GetValues(GetType(ExchangeEnrollOutcome))
            If outcome = ExchangeEnrollOutcome.Refused Then Continue For
            Dim text As String = ExchangeDeviceText.EnrollFailure(New ExchangeEnrollResult(outcome, ExchangeRefusal.Unknown, ""))
            If outcome = ExchangeEnrollOutcome.Enrolled OrElse outcome = ExchangeEnrollOutcome.Cancelled Then
                Assert.Equal("", text)
            Else
                Assert.False(String.IsNullOrWhiteSpace(text), outcome.ToString())
                sentences.Add(text)
            End If
        Next
        For Each r As ExchangeRefusal In [Enum].GetValues(GetType(ExchangeRefusal))
            Dim text As String = ExchangeDeviceText.EnrollFailure(New ExchangeEnrollResult(ExchangeEnrollOutcome.Refused, r, ""))
            Assert.False(String.IsNullOrWhiteSpace(text), r.ToString())
            For Each wire As String In {"bad-credentials", "rate-limited", "id-collision", "version-unsupported", "tls-required", "device-revoked", "capacity"}
                Assert.DoesNotContain(wire, text)
            Next
        Next
        ' DEVICE-EXCHANGE item DA (ticket 239 CR-9): a refusal is shown as ITS row of the message table, never a sentence of this
        ' program: an unknown reason is the table's generic `refused`, `unavailable` has its own row
        Assert.Equal(ExchangeDeviceText.TableText(ConnectionDiagnosisMessages.Refused),
                     ExchangeDeviceText.EnrollFailure(New ExchangeEnrollResult(ExchangeEnrollOutcome.Refused, ExchangeRefusal.Unknown, "")))
        Assert.Equal(ExchangeDeviceText.TableText(ConnectionDiagnosisMessages.Unavailable),
                     ExchangeDeviceText.EnrollFailure(New ExchangeEnrollResult(ExchangeEnrollOutcome.Refused, ExchangeRefusal.Unavailable, "")))
        Assert.NotEqual(ExchangeDeviceText.EnrollFailure(New ExchangeEnrollResult(ExchangeEnrollOutcome.Refused, ExchangeRefusal.Unknown, "")),
                        ExchangeDeviceText.EnrollFailure(New ExchangeEnrollResult(ExchangeEnrollOutcome.Refused, ExchangeRefusal.Unavailable, "")))
        Assert.NotEqual(ExchangeDeviceText.EnrollFailure(New ExchangeEnrollResult(ExchangeEnrollOutcome.Refused, ExchangeRefusal.BadCredentials, "")),
                        ExchangeDeviceText.EnrollFailure(New ExchangeEnrollResult(ExchangeEnrollOutcome.Refused, ExchangeRefusal.RateLimited, "")))
        ' the "no way to reach a certificate that cannot be checked" case is said in words, never offered as a switch
        Assert.Contains(sentences, Function(s) s.Length > 20)
    End Sub

    <Fact>
    Public Sub A_pairing_failure_says_whether_the_server_answered()
        Assert.Equal("", ExchangeDeviceText.PairingFailure(New ExchangePairingResult(True, "X", "", ExchangeRefusal.Unknown, True)))
        Dim silent As String = ExchangeDeviceText.PairingFailure(New ExchangePairingResult(False, "", "", ExchangeRefusal.Unknown, False))
        Dim refused As String = ExchangeDeviceText.PairingFailure(New ExchangePairingResult(False, "", "", ExchangeRefusal.RateLimited, True))
        Assert.NotEqual(silent, refused)
        Assert.False(String.IsNullOrWhiteSpace(silent))
    End Sub

    <Fact>
    Public Sub The_countdown_is_minutes_and_seconds_and_never_negative()
        Dim ten As String = ExchangeDeviceText.ValidFor(TimeSpan.FromMinutes(10))
        Assert.Contains("10:00", ten)
        Assert.Contains("9:05", ExchangeDeviceText.ValidFor(TimeSpan.FromSeconds(545)))
        Assert.Contains("0:00", ExchangeDeviceText.ValidFor(TimeSpan.FromSeconds(-5)))
    End Sub

    ' ---------------------------------------------------------------- the window

    <Fact>
    Public Sub With_the_gate_shut_the_window_has_no_exchange_account_group()
        ' the state of a build whose constant is False (since ticket 223 T-3 the shipped build is not one), set for this flow only
        Using ExchangeFeatures.ForFlow(offered:=False, switchOn:=False, componentInstalled:=False)
            ExchangeTestSupport.OnUi(Sub()
                                         Using form As New Share_Settings_Form()
                                             Dim sections As CollapsibleSection() = DirectCast(GetType(Share_Settings_Form).GetMethod("AllSettingsSections", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(form, Nothing), CollapsibleSection())
                                             Assert.DoesNotContain(sections, Function(s) s.Key = "exchange-account")
                                             Dim field As FieldInfo = GetType(Share_Settings_Form).GetField("_secAccount", BindingFlags.Instance Or BindingFlags.NonPublic)
                                             Assert.Null(field.GetValue(form))
                                         End Using
                                     End Sub)
        End Using
    End Sub

    ''' <summary>Builds the group on a form WITHOUT flipping the gate (a process-wide switch would leak into parallel tests): the builder is called by hand.</summary>
    Private Shared Function BuildGroup(form As Share_Settings_Form) As CollapsibleSection
        Dim sec As New CollapsibleSection("exchange-account", ExchangeDeviceText.GroupTitle())
        GetType(Share_Settings_Form).GetField("_secAccount", BindingFlags.Instance Or BindingFlags.NonPublic).SetValue(form, sec)
        GetType(Share_Settings_Form).GetMethod("BuildAccountSection", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(form, New Object() {sec})
        ' a folded section's body reports every child as not visible (Control.Visible is ancestor-aware), so open it to read the rows' own state
        sec.Expanded = True
        Return sec
    End Function

    Private Shared Function Field(Of T)(form As Share_Settings_Form, name As String) As T
        Return DirectCast(GetType(Share_Settings_Form).GetField(name, BindingFlags.Instance Or BindingFlags.NonPublic).GetValue(form), T)
    End Function

    Private Shared Sub Call0(form As Share_Settings_Form, name As String)
        GetType(Share_Settings_Form).GetMethod(name, BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(form, Nothing)
    End Sub

    <Fact>
    Public Sub With_no_agent_the_group_shows_the_state_line_and_only_the_sign_in()
        ExchangeTestSupport.OnUi(Sub()
                                     Using form As New Share_Settings_Form()
                                         BuildGroup(form)
                                         Call0(form, "ApplyAccountState")
                                         Assert.Equal(ExchangeDeviceText.StateLine(Nothing), Field(Of Label)(form, "_accState").Text)
                                         Assert.True(Field(Of Button)(form, "_btnAccSignIn").Visible)
                                         Assert.False(Field(Of Button)(form, "_btnAccAccept").Visible)
                                         Assert.False(Field(Of Button)(form, "_btnAccAdd").Visible)
                                         Assert.False(Field(Of Button)(form, "_btnAccDisconnect").Visible)
                                         Assert.False(Field(Of Button)(form, "_btnAccRevoke").Visible)
                                         Assert.False(Field(Of ListView)(form, "_accDevices").Visible)
                                         Assert.False(Field(Of FlowLayoutPanel)(form, "_accPairing").Visible)
                                         Assert.False(Field(Of Label)(form, "_accDetail").Visible)
                                         Call0(form, "DisposeAccountResources")
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Async Function A_running_agent_fills_the_group_marks_this_device_and_hides_revoke_until_a_row_is_selected() As Task
        Using acc As New ExchangeStubAccount()
            acc.Devices.Add("{""deviceId"":""PH"",""deviceName"":""Phone"",""product"":""fms-android"",""presence"":""online"",""lastSeen"":""2026-10-07T12:00:00Z""}")
            acc.Devices.Add("{""deviceId"":""WA"",""deviceName"":""Watch"",""product"":""fms-wear"",""presence"":""offline"",""lastSeen"":""2026-10-06T08:30:00Z""}")
            Using rig As New ExchangeDeviceAgentTests.Rig()
                Dim deviceId As String = ExchangeWireTestKit.IdOf("D"c)
                Dim token As String = acc.PreEnroll(deviceId)
                acc.Devices.Add("{""deviceId"":""" & deviceId & """,""deviceName"":""TESTPC"",""product"":""fms-windows"",""presence"":""online""}")
                rig.Ports.Accounts.Save(New ExchangeAccount(acc.Address, "alice", acc.Fingerprint, ExchangeCertificateKind.Pinned, deviceId, "TESTPC"))
                Assert.True(rig.Ports.Credentials.Save(deviceId, token))
                Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.StartIfConfigured(True, rig.Ports, Function(p) New ExchangeDeviceAgent(p))
                Try
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetStatus().State = ExchangeAgentState.Online AndAlso agent.GetFullDirectory().Devices.Count = 3))
                    ExchangeTestSupport.OnUi(Sub()
                                                 Using form As New Share_Settings_Form()
                                                     Dim sec As CollapsibleSection = BuildGroup(form)
                                                     Call0(form, "ApplyAccountState")
                                                     Assert.Contains("alice", Field(Of Label)(form, "_accState").Text)
                                                     Assert.True(Field(Of Button)(form, "_btnAccAdd").Visible)
                                                     Assert.True(Field(Of Button)(form, "_btnAccDisconnect").Visible)
                                                     Assert.False(Field(Of Button)(form, "_btnAccSignIn").Visible)
                                                     Dim list As ListView = Field(Of ListView)(form, "_accDevices")
                                                     Dim unused As IntPtr = list.Handle   ' selection events are raised by the native control once it exists
                                                     Call0(form, "ApplyAccountState")
                                                     Assert.True(list.Visible)
                                                     Assert.Equal(3, list.Items.Count)
                                                     Assert.Equal(ExchangeDeviceText.DeviceName("TESTPC", True), list.Items(0).Text)   ' this device first, marked
                                                     Assert.Equal("Phone", list.Items(1).Text)
                                                     Assert.Equal("Watch", list.Items(2).Text)
                                                     Assert.Equal(ExchangeDeviceText.PresenceText(True), list.Items(1).SubItems(1).Text)
                                                     Assert.Equal(ExchangeDeviceText.PresenceText(False), list.Items(2).SubItems(1).Text)
                                                     Assert.False(String.IsNullOrEmpty(list.Items(1).SubItems(2).Text))   ' last seen, in the user's culture
                                                     ' hidden, not disabled: no row selected means no Revoke
                                                     Assert.False(Field(Of Button)(form, "_btnAccRevoke").Visible)
                                                     list.Items(1).Selected = True
                                                     Assert.True(Field(Of Button)(form, "_btnAccRevoke").Visible)
                                                     Assert.Equal(ExchangeDeviceText.StateLine(agent.GetStatus()), sec.Summary)
                                                     Call0(form, "DisposeAccountResources")
                                                 End Using
                                             End Sub)
                Finally
                    ExchangeDeviceHost.StopAtExit()
                End Try
            End Using
        End Using
    End Function

    <Fact>
    Public Sub A_pairing_code_leaves_the_screen_and_the_memory_when_it_expires()
        ExchangeTestSupport.OnUi(Sub()
                                     Using form As New Share_Settings_Form()
                                         BuildGroup(form)
                                         Dim t As Type = GetType(Share_Settings_Form)
                                         Const flags As BindingFlags = BindingFlags.Instance Or BindingFlags.NonPublic
                                         t.GetField("_accCodeText", flags).SetValue(form, "ABCD2345")
                                         t.GetField("_accCodeExpires", flags).SetValue(form, DateTimeOffset.UtcNow.AddMinutes(5))
                                         Call0(form, "ShowPairingCode")
                                         Assert.True(Field(Of FlowLayoutPanel)(form, "_accPairing").Visible)
                                         Assert.Contains("ABCD2345", Field(Of Label)(form, "_accCode").Text)
                                         Assert.True(Field(Of Button)(form, "_btnAccCopy").Visible)

                                         ' the code expires
                                         t.GetField("_accCodeExpires", flags).SetValue(form, DateTimeOffset.UtcNow.AddSeconds(-1))
                                         Call0(form, "UpdateAccountCountdown")
                                         Assert.Equal("", Field(Of String)(form, "_accCodeText"))
                                         Assert.DoesNotContain("ABCD2345", Field(Of Label)(form, "_accCode").Text)
                                         Assert.False(Field(Of Button)(form, "_btnAccCopy").Visible)
                                         Assert.Equal(Localization.T("Код истёк."), Field(Of Label)(form, "_accValid").Text)

                                         ' closing the window drops a code that is still valid
                                         t.GetField("_accCodeText", flags).SetValue(form, "ZZZZ9999")
                                         Call0(form, "DisposeAccountResources")
                                         Assert.Equal("", Field(Of String)(form, "_accCodeText"))
                                     End Using
                                 End Sub)
    End Sub

    ' ---------------------------------------------------------------- the sign-in dialog

    Private Shared Function DialogField(Of T)(dlg As Exchange_Account_Form, name As String) As T
        Return DirectCast(GetType(Exchange_Account_Form).GetField(name, BindingFlags.Instance Or BindingFlags.NonPublic).GetValue(dlg), T)
    End Function

    <Fact>
    Public Sub The_sign_in_dialog_opens_with_no_accept_button_and_nothing_trusted_and_the_default_is_cancel()
        ExchangeTestSupport.OnUi(Sub()
                                     Using rig As New ExchangeDeviceAgentTests.Rig()
                                         Using agent As ExchangeDeviceAgent = rig.NewAgent()
                                             Using dlg As New Exchange_Account_Form(agent, "host.example:22022", "alice")
                                                 dlg.Show()
                                                 Try
                                                     Assert.Null(dlg.AcceptButton)   ' the default is Cancel: no Enter key accepts anything
                                                     Assert.Same(DialogField(Of Button)(dlg, "_btnCancel"), dlg.CancelButton)
                                                     Assert.True(DialogField(Of Button)(dlg, "_btnSignIn").Visible)   ' sign-in button is visible so the user knows how to apply
                                                     Assert.False(DialogField(Of FlowLayoutPanel)(dlg, "_pnlCert").Visible)
                                                     Assert.False(DialogField(Of CheckBox)(dlg, "_chkStart").Checked)   ' start-with-Windows is off by default (Q180-2)
                                                     Assert.Equal("host.example:22022", DialogField(Of TextBox)(dlg, "_txtServer").Text)
                                                     Assert.Equal("alice", DialogField(Of TextBox)(dlg, "_txtLogin").Text)
                                                     Assert.True(DialogField(Of TextBox)(dlg, "_txtSecret").UseSystemPasswordChar)   ' masked
                                                     Assert.True(DialogField(Of RadioButton)(dlg, "_rbPassword").Checked)
                                                     Assert.Equal("TESTPC".Length > 0, DialogField(Of TextBox)(dlg, "_txtName").Text.Length > 0)
                                                     Assert.Equal(0, rig.Transport.Attempts)   ' nothing is contacted by opening the dialog
                                                     Assert.Equal(0, rig.Port.Writes)
                                                 Finally
                                                     dlg.Close()
                                                 End Try
                                             End Using
                                         End Using
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub Clicking_sign_in_with_empty_credentials_prompts_user_without_contacting_network()
        ExchangeTestSupport.OnUi(Sub()
                                     Using rig As New ExchangeDeviceAgentTests.Rig()
                                         Using agent As ExchangeDeviceAgent = rig.NewAgent()
                                             Using dlg As New Exchange_Account_Form(agent, "host.example:22022", "")
                                                 dlg.Show()
                                                 Try
                                                     Dim btnSignIn As Button = DialogField(Of Button)(dlg, "_btnSignIn")
                                                     Dim lblResult As Label = DialogField(Of Label)(dlg, "_lblResult")
                                                     btnSignIn.PerformClick()
                                                     Assert.True(lblResult.Visible)
                                                     Assert.Equal(0, rig.Transport.Attempts)
                                                 Finally
                                                     dlg.Close()
                                                 End Try
                                             End Using
                                         End Using
                                     End Using
                                 End Sub)
    End Sub

    ' ---------------------------------------------------------------- the dormant rule, host side

    <Fact>
    Public Sub Without_the_gate_or_an_account_the_host_creates_no_agent()
        ' The shut gate is set for this flow only (ticket 223 T-3 opened the shipped build; the opened state with no account is
        ' ExchangeDependencySplitTests.The_features_on_and_no_account_configured_construct_no_agent).
        Using ExchangeFeatures.ForFlow(offered:=False, switchOn:=False, componentInstalled:=False)
            Dim before As Integer = ExchangeDeviceAgent.Constructed
            Assert.Null(ExchangeDeviceHost.Current)
            ExchangeDeviceHost.StartIfConfigured()   ' the production start with the gate shut
            Assert.Null(ExchangeDeviceHost.Current)
            Assert.Equal(before, ExchangeDeviceAgent.Constructed)
            Assert.Null(ExchangeDeviceHost.GetOrCreate())   ' the sign-in cannot even create one while the gate is shut
        End Using
    End Sub

End Class
