Option Strict On

Imports System.Reflection
Imports System.Drawing
Imports System.Threading
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

<Collection(UiLanguageCollection.Name)>
Public Class ExchangeRegistrationUiTests
    Private Shared Sub OnUi(check As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         check()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)))
        If failure IsNot Nothing Then Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    Private Shared Function Field(Of T)(form As Form, name As String) As T
        Return DirectCast(form.GetType().GetField(name, BindingFlags.Instance Or BindingFlags.NonPublic).GetValue(form), T)
    End Function

    Private Shared Function Connection(Optional invitation As Boolean = False, Optional pin As Boolean = True, Optional login As String = "alice") As String
        Dim built As ExchangeConnectionStringResult = ExchangeConnectionString.Build("exchange.example:443", If(pin, "SHA256:" & New String("A"c, 43), ""), "Test", login,
            If(invitation, ExchangeStubAccount.Invitation, ""))
        Assert.True(built.Succeeded, built.Reason)
        Dim failure As ExchangeConnectionStringResult = Nothing
        Return ExchangeConnectionString.Format(built.Value, failure)
    End Function

    <Theory>
    <InlineData("")>
    <InlineData("invalid")>
    <InlineData("plain")>
    Public Sub Replacing_An_Invitation_Leaves_Registration_And_Forgets_The_Code(replacement As String)
        OnUi(Sub()
                 Using rig As New ExchangeDeviceAgentTests.Rig(), agent As ExchangeDeviceAgent = rig.NewAgent(), form As New Exchange_Account_Form(agent)
                     Dim input As TextBox = Field(Of TextBox)(form, "_txtString")
                     input.Text = Connection(invitation:=True)
                     Assert.True(Field(Of Boolean)(form, "_register"))
                     Field(Of TextBox)(form, "_txtSecretRepeat").Text = "typed-secret"
                     input.Text = If(replacement = "plain", Connection(), replacement)
                     Assert.False(Field(Of Boolean)(form, "_register"))
                     Assert.Equal("", Field(Of String)(form, "_invitationCode"))
                     Assert.Equal("", Field(Of TextBox)(form, "_txtSecretRepeat").Text)
                 End Using
             End Sub)
    End Sub

    <Fact>
    Public Sub Replacing_A_Pinned_String_With_An_Unpinned_String_Requires_New_Trust()
        OnUi(Sub()
                 Using rig As New ExchangeDeviceAgentTests.Rig(), agent As ExchangeDeviceAgent = rig.NewAgent(), form As New Exchange_Account_Form(agent)
                     Dim input As TextBox = Field(Of TextBox)(form, "_txtString")
                     input.Text = Connection()
                     Assert.True(Field(Of Boolean)(form, "_trusted"))
                     input.Text = Connection(pin:=False)
                     Assert.False(Field(Of Boolean)(form, "_trusted"))
                     Assert.Null(Field(Of ExchangeCertificateInfo)(form, "_probe"))
                 End Using
             End Sub)
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("previous-account")>
    Public Sub Login_Hints_Refresh_The_Previous_Hint_And_Preserve_User_Typing(presetLogin As String)
        OnUi(Sub()
                 Using rig As New ExchangeDeviceAgentTests.Rig(), agent As ExchangeDeviceAgent = rig.NewAgent(), form As New Exchange_Account_Form(agent, presetLogin:=presetLogin)
                     Dim input As TextBox = Field(Of TextBox)(form, "_txtString")
                     Dim login As TextBox = Field(Of TextBox)(form, "_txtLogin")
                     input.Text = Connection(login:="alice")
                     input.Text = Connection(login:="bob")
                     Assert.Equal("bob", login.Text)
                     login.Text = "my-choice"
                     input.Text = Connection(login:="carol")
                     Assert.Equal("my-choice", login.Text)
                 End Using
             End Sub)
    End Sub

    <Fact>
    Public Sub A_Pending_Request_Freezes_Its_Target_And_Credentials()
        OnUi(Sub()
                 Using rig As New ExchangeDeviceAgentTests.Rig(), agent As ExchangeDeviceAgent = rig.NewAgent(), form As New Exchange_Account_Form(agent)
                     Assert.Equal(3, Field(Of FlowLayoutPanel)(form, "_stringButtons").Controls.Count)
                     form.GetType().GetMethod("SetBusy", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(form, New Object() {True})
                     For Each name As String In {"_txtString", "_stringButtons", "_txtServer", "_txtLogin", "_txtSecret", "_txtSecretRepeat", "_rbCode"}
                         Assert.False(Field(Of Control)(form, name).Enabled, name)
                     Next
                 End Using
             End Sub)
    End Sub

    <Fact>
    Public Sub A_Tall_Registration_Form_Scrolls_Instead_Of_Losing_Its_Buttons()
        OnUi(Sub()
                 Using rig As New ExchangeDeviceAgentTests.Rig(), agent As ExchangeDeviceAgent = rig.NewAgent(), form As New Exchange_Account_Form(agent)
                     Field(Of TextBox)(form, "_txtString").Text = Connection(invitation:=True)
                     form.AutoScaleMode = AutoScaleMode.None
                     form.Scale(New SizeF(2.0F, 2.0F))
                     Dim scaledFont As New Font(form.Font.FontFamily, form.Font.Size * 2.0F)
                     form.Font = scaledFont
                     form.Show()
                     Try
                         Assert.True(form.AutoScroll, "Tall registration needs a scroll viewport.")
                         Assert.True(form.Height <= Screen.FromControl(form).WorkingArea.Height, "The outer form must fit the monitor.")
                         Dim cancel As Button = Field(Of Button)(form, "_btnCancel")
                         form.ScrollControlIntoView(cancel)
                         Dim bounds As Rectangle = form.RectangleToClient(cancel.RectangleToScreen(cancel.ClientRectangle))
                         Assert.True(form.ClientRectangle.Contains(bounds), "Cancel must remain reachable after scrolling.")
                     Finally
                         form.Close()
                         scaledFont.Dispose()
                     End Try
                 End Using
             End Sub)
    End Sub
End Class
