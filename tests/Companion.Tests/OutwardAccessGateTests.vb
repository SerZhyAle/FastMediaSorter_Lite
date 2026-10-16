Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Reflection
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 239 CR-13 (owner decision Q239-2, ANYWHERE-ACCESS amendment L): the guest invitation and the link with a password are
''' shown as NOT AVAILABLE - one line of reason, no date - and nothing is created behind them, until the file server can confine a
''' login to one folder. ONE compile-time constant (<see cref="OutwardAccessFeatures.Offered"/>) is read by every entry point; the
''' engine (ExchangePublisher's link and guest methods, ExchangeSealedAccess) and ITS tests are not touched.
''' The language of the process is global, so this class shares the UI-language collection with the other text tests.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class OutwardAccessGateTests

    ' ---------------------------------------------------------------- the constant and its test seam

    <Fact>
    Public Sub The_gate_is_closed_in_this_build()
        ' Pinned on purpose: it flips only together with this test, after the worker confines a login to one folder (WORKER-IPC proposal).
        Assert.False(OutwardAccessFeatures.Offered)
        Assert.False(OutwardAccessFeatures.IsOffered())
    End Sub

    <Fact>
    Public Sub IsOffered_follows_the_flow_scope_and_goes_back_when_it_is_disposed()
        Assert.False(OutwardAccessFeatures.IsOffered())
        Using OutwardAccessFeatures.ForFlow(True)
            Assert.True(OutwardAccessFeatures.IsOffered())
            Using OutwardAccessFeatures.ForFlow(False)
                Assert.False(OutwardAccessFeatures.IsOffered())
            End Using
            Assert.True(OutwardAccessFeatures.IsOffered(), "the inner scope must restore the outer one")
        End Using
        Assert.False(OutwardAccessFeatures.IsOffered())
    End Sub

    <Fact>
    Public Sub The_flow_scope_of_one_thread_is_not_seen_by_another()
        Dim seenElsewhere As Boolean? = Nothing
        Using OutwardAccessFeatures.ForFlow(True)
            Dim other As New System.Threading.Thread(Sub() seenElsewhere = OutwardAccessFeatures.IsOffered())
            ' a thread started from this flow inherits it (that is how the UI tests run); a thread that is not started from it does not
            System.Threading.ExecutionContext.SuppressFlow()
            Try
                other.Start()
            Finally
                System.Threading.ExecutionContext.RestoreFlow()
            End Try
            Assert.True(other.Join(5000))
        End Using
        Assert.True(seenElsewhere.HasValue AndAlso Not seenElsewhere.Value, "a thread outside the flow must see the build's own value")
    End Sub

    ' ---------------------------------------------------------------- every entry point reads the gate

    <Theory>
    <InlineData("Forms\Folder_Outside_Access_Form.vb")>
    <InlineData("Forms\Exchange_Invitation_Create_Form.vb")>
    <InlineData("Forms\Exchange_Shared_Invitation_Form.vb")>
    <InlineData("Forms\Share_Settings_Form.ExchangeProducer.vb")>
    <InlineData("Core\ExchangePublishText.vb")>
    Public Sub Each_entry_point_reads_the_one_gate(relative As String)
        Dim code As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource(relative))
        Assert.Contains("OutwardAccessFeatures.IsOffered()", code)
    End Sub

    <Fact>
    Public Sub No_other_source_file_creates_a_link_or_a_guest_invitation_without_asking_the_gate()
        ' The engine itself (ExchangePublisher) defines the methods and stays as it is; every OTHER file that calls one of them is an
        ' entry point and has to name the gate, so a new button cannot open the feature again by accident.
        Dim calls As String() = {".CreateLinkAsync(", ".RotateLinkAsync(", "CreateInvitationAsync(""guest"""}
        Dim offenders As New List(Of String)()
        For Each file As String In ExchangeTestSupport.CompanionSources()
            Dim name As String = IO.Path.GetFileName(file)
            If String.Equals(name, "ExchangePublisher.vb", StringComparison.OrdinalIgnoreCase) Then Continue For
            Dim code As String = ExchangeTestSupport.CodeOnly(IO.File.ReadAllText(file))
            If calls.Any(Function(c) code.Contains(c)) AndAlso Not code.Contains("OutwardAccessFeatures") Then offenders.Add(name)
        Next
        Assert.True(offenders.Count = 0, "creates outward access without the gate: " & String.Join(", ", offenders))
    End Sub

    <Fact>
    Public Sub The_actions_that_create_something_check_the_gate_before_they_call_the_engine()
        Dim folder As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\Folder_Outside_Access_Form.vb"))
        AssertGateBeforeCall(folder, "Private Async Sub OnCreateLinkClicked", "CreateLinkAsync(")
        AssertGateBeforeCall(folder, "Private Async Sub OnCreateGuestClicked", "CreateInvitationAsync(")
        AssertGateBeforeCall(folder, "Private Async Sub OnShowLinkQrClicked", "TryGetLinkCode(")
        AssertGateBeforeCall(folder, "Private Sub OnGeneratePasswordClicked", "RandomNumberGenerator")

        Dim producer As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\Share_Settings_Form.ExchangeProducer.vb"))
        AssertGateBeforeCall(producer, "Private Async Sub OnPxCreateLink", "RunLinkActionAsync(")
        AssertGateBeforeCall(producer, "Private Async Sub OnPxRotateLink", "RunLinkActionAsync(")

        ' withdrawing what exists is never behind the gate: taking access back is not the dangerous direction
        Assert.DoesNotContain("OutwardAccessFeatures", ExchangeTestSupport.MethodBody(producer, "Private Async Sub OnPxRemoveLink"))
        Assert.DoesNotContain("OutwardAccessFeatures", ExchangeTestSupport.MethodBody(folder, "Private Async Sub OnWithdrawLinkClicked"))
        Assert.DoesNotContain("OutwardAccessFeatures", ExchangeTestSupport.MethodBody(folder, "Private Async Sub OnWithdrawGuestClicked"))
    End Sub

    Private Shared Sub AssertGateBeforeCall(code As String, declaration As String, engineCall As String)
        Dim body As String = ExchangeTestSupport.MethodBody(code, declaration)
        Dim gate As Integer = body.IndexOf("OutwardAccessFeatures.IsOffered()", StringComparison.Ordinal)
        Dim action As Integer = body.IndexOf(engineCall, StringComparison.Ordinal)
        Assert.True(gate >= 0, declaration & " does not ask the gate")
        Assert.True(action < 0 OrElse gate < action, declaration & " reaches " & engineCall & " before the gate is asked")
    End Sub

    ' ---------------------------------------------------------------- the words

    <Fact>
    Public Sub The_link_note_is_the_reason_while_the_gate_is_closed_and_the_old_promise_when_it_is_open()
        Assert.Equal(OutwardAccessFeatures.Reason(), ExchangePublishText.LinkNote())
        Using OutwardAccessFeatures.ForFlow(True)
            Dim open As String = ExchangePublishText.LinkNote()
            Assert.False(String.IsNullOrWhiteSpace(open))
            Assert.NotEqual(OutwardAccessFeatures.Reason(), open)
        End Using
    End Sub

    <Fact>
    Public Sub The_reason_is_one_line_without_a_date_and_is_translated_into_all_13_languages()
        Dim saved As String = Localization.CurrentCode
        Try
            Assert.Equal(13, Localization.Codes.Length)
            Dim russian As String = ReasonIn("ru")
            Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
            For Each code As String In Localization.Codes
                Dim text As String = ReasonIn(code)
                Assert.False(String.IsNullOrWhiteSpace(text), code & ": empty")
                Assert.DoesNotContain(vbLf, text)
                Assert.DoesNotContain(vbCr, text)
                Assert.DoesNotContain("...", text)
                Assert.False(text.Any(Function(c) Char.IsDigit(c)), code & ": a date or a number in the reason")
                If code <> "ru" Then Assert.NotEqual(russian, text)
                Assert.True(seen.Add(text), code & ": the same sentence as another language")
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    Private Shared Function ReasonIn(code As String) As String
        Localization.CurrentCode = code
        Return OutwardAccessFeatures.Reason()
    End Function

    <Fact>
    Public Sub The_new_group_title_is_translated_into_every_language()
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                Dim title As String = Localization.T("Гостевое приглашение")
                Assert.False(String.IsNullOrWhiteSpace(title), code)
                If code <> "ru" Then Assert.NotEqual("Гостевое приглашение", title)
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub The_old_claim_that_the_guest_account_is_bound_to_this_folder_is_gone_from_the_program()
        For Each file As String In ExchangeTestSupport.CompanionSources()
            Dim text As String = IO.File.ReadAllText(file)
            Assert.DoesNotContain("доступна только эта папка", text)
            Assert.DoesNotContain("Гостевое приглашение для этой папки", text)
        Next
    End Sub

    ' ---------------------------------------------------------------- the windows (constructed, not shown)

    Private Shared Function F(Of T)(target As Object, name As String) As T
        Dim info As FieldInfo = target.GetType().GetField(name, BindingFlags.Instance Or BindingFlags.NonPublic)
        Assert.True(info IsNot Nothing, name & " not found on " & target.GetType().Name)
        Return DirectCast(info.GetValue(target), T)
    End Function

    Private Shared Function AllControls(root As Control) As List(Of Control)
        Dim result As New List(Of Control)()
        For Each child As Control In root.Controls
            result.Add(child)
            result.AddRange(AllControls(child))
        Next
        Return result
    End Function

    Private Shared Function LabelTexts(root As Control) As List(Of String)
        Return AllControls(root).OfType(Of Label)().Select(Function(l) l.Text).ToList()
    End Function

    Private Shared Sub Call0(target As Object, name As String)
        target.GetType().GetMethod(name, BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(target, Nothing)
    End Sub

    <Fact>
    Public Sub The_outside_access_window_with_the_gate_closed_offers_nothing_to_create()
        ExchangeTestSupport.OnUi(Sub()
                                     Using form As New Folder_Outside_Access_Form("Photos", "D:\Photos", Nothing)
                                         Dim reason As String = OutwardAccessFeatures.Reason()
                                         Assert.Equal(reason, F(Of Label)(form, "_lblGuestReason").Text)
                                         Assert.Equal(reason, F(Of Label)(form, "_lblLinkReason").Text)
                                         Assert.False(F(Of Button)(form, "_btnCreateGuest").Enabled)
                                         Assert.False(F(Of Button)(form, "_btnCreateLink").Enabled)
                                         Assert.False(F(Of Button)(form, "_btnGeneratePassword").Enabled)
                                         Assert.False(F(Of TextBox)(form, "_txtLinkPassword").Enabled)
                                         Assert.False(F(Of Button)(form, "_btnShowLinkQr").Enabled)
                                         ' the click handlers hold the gate too: a generated password is not even made
                                         Call0(form, "OnGeneratePasswordClicked")
                                         Assert.Equal("", F(Of TextBox)(form, "_txtLinkPassword").Text)
                                         ' and the false claim of the old window is not on screen
                                         Assert.DoesNotContain(LabelTexts(form), Function(t) t.Contains("только эта папка"))
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub The_outside_access_window_with_the_gate_open_enables_the_password_controls()
        ExchangeTestSupport.OnUi(Sub()
                                     Using OutwardAccessFeatures.ForFlow(True)
                                         Using form As New Folder_Outside_Access_Form("Photos", "D:\Photos", Nothing)
                                             Assert.True(F(Of TextBox)(form, "_txtLinkPassword").Enabled)
                                             Assert.True(F(Of Button)(form, "_btnGeneratePassword").Enabled)
                                             Call0(form, "OnGeneratePasswordClicked")
                                             Assert.NotEqual("", F(Of TextBox)(form, "_txtLinkPassword").Text)
                                         End Using
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub The_invitation_window_creates_a_family_invitation_only_while_the_gate_is_closed()
        ExchangeTestSupport.OnUi(Sub()
                                     Using form As New Exchange_Invitation_Create_Form()
                                         Dim guest As RadioButton = F(Of RadioButton)(form, "_rbGuest")
                                         Assert.False(guest.Enabled)
                                         guest.Checked = True
                                         Assert.Equal("member", form.Kind)
                                         Assert.Contains(LabelTexts(form), Function(t) t = OutwardAccessFeatures.Reason())
                                     End Using
                                     Using OutwardAccessFeatures.ForFlow(True)
                                         Using form As New Exchange_Invitation_Create_Form()
                                             Dim guest As RadioButton = F(Of RadioButton)(form, "_rbGuest")
                                             Assert.True(guest.Enabled)
                                             guest.Checked = True
                                             Assert.Equal("guest", form.Kind)
                                             Assert.DoesNotContain(LabelTexts(form), Function(t) t = OutwardAccessFeatures.Reason())
                                         End Using
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub The_shared_invitation_window_never_turns_a_guest_invitation_into_a_family_one()
        ExchangeTestSupport.OnUi(Sub()
                                     ' an existing guest-kind shared invitation: shown as it is, cannot be set again, never silently "member"
                                     Using form As New Exchange_Shared_Invitation_Form("guest")
                                         Dim guest As RadioButton = F(Of RadioButton)(form, "_rbGuest")
                                         Assert.False(guest.Enabled)
                                         Assert.True(guest.Checked)
                                         Assert.Equal("member", form.Kind)
                                         Assert.Equal(OutwardAccessFeatures.Reason(), F(Of Label)(form, "_lblGuestReason").Text)
                                         Assert.False(DirectCast(form.AcceptButton, Button).Enabled)
                                     End Using
                                     ' a family one is still set as before
                                     Using form As New Exchange_Shared_Invitation_Form("member")
                                         Assert.False(F(Of RadioButton)(form, "_rbGuest").Enabled)
                                         Assert.True(DirectCast(form.AcceptButton, Button).Enabled)
                                         Assert.Equal("member", form.Kind)
                                     End Using
                                     Using OutwardAccessFeatures.ForFlow(True)
                                         Using form As New Exchange_Shared_Invitation_Form("guest")
                                             Assert.True(F(Of RadioButton)(form, "_rbGuest").Enabled)
                                             Assert.Null(F(Of Label)(form, "_lblGuestReason"))
                                             Assert.Equal("guest", form.Kind)
                                             Assert.True(DirectCast(form.AcceptButton, Button).Enabled)
                                         End Using
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub The_producer_rows_keep_the_link_buttons_off_and_say_why_while_the_gate_is_closed()
        ExchangeTestSupport.OnUi(Sub()
                                     Using ExchangeFeatures.ForFlow(offered:=True)
                                         Using form As New Share_Settings_Form()
                                             BuildProducerRows(form)
                                             Try
                                                 Call0(form, "UpdateProducerLinkButtons")
                                                 Assert.False(F(Of Button)(form, "_btnPxCreate").Enabled)
                                                 Assert.False(F(Of Button)(form, "_btnPxRotate").Enabled)
                                                 ' removing a link that exists stays possible
                                                 Assert.True(F(Of Button)(form, "_btnPxRemoveLink").Enabled)
                                                 Assert.Equal(OutwardAccessFeatures.Reason(), F(Of Label)(form, "_lblPxLinkNote").Text)
                                                 Using OutwardAccessFeatures.ForFlow(True)
                                                     Call0(form, "UpdateProducerLinkButtons")
                                                     Assert.True(F(Of Button)(form, "_btnPxCreate").Enabled)
                                                     Assert.True(F(Of Button)(form, "_btnPxRotate").Enabled)
                                                     Assert.NotEqual(OutwardAccessFeatures.Reason(), F(Of Label)(form, "_lblPxLinkNote").Text)
                                                 End Using
                                             Finally
                                                 Call0(form, "DisposeProducerResources")
                                             End Try
                                         End Using
                                     End Using
                                 End Sub)
    End Sub

    ''' <summary>Builds the producer rows into a section by hand, the way the settings window does when it is built.</summary>
    Private Shared Sub BuildProducerRows(form As Share_Settings_Form)
        Dim section As New CollapsibleSection("exchange-producer-test", "test")
        GetType(Share_Settings_Form).GetMethod("BuildExchangeProducerRows", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(form, New Object() {section})
        section.Expanded = True
    End Sub

End Class
