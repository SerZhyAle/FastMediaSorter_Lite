Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Linq
Imports System.Reflection
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Microsoft.Win32
Imports Xunit

''' <summary>
''' Ticket L0278 T-6, T-7 and T-8 through the REAL window: the buttons are clicked, the cards read and change through a scripted handshake
''' and a lock-step stream with a real <see cref="ExchangeRemoteAdminSession"/> inside, and every request is compared with what the script
''' expects byte by byte. The gate is lit by a test scope only; the registry hive is a scratch one; no real network and no real clipboard.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class RemoteAdminWindowTests

    Friend Const TargetName As String = "Office"
    Friend Const TargetAddress As String = "exchange.test:22022"
    Friend Const Secret As String = "AdminSecretPass1"
    Friend Const NewPassword As String = "Correct-Horse-9-Battery"
    Private Const Flags As BindingFlags = BindingFlags.Instance Or BindingFlags.NonPublic Or BindingFlags.Public

    ' ------------------------------------------------------------------------------------------------ the script

    Private Shared Function AuthSteps() As List(Of ExchangeTranscriptStep)
        Return ExchangeTranscriptFile.AdminNative().StreamNamed("admin-ops").Steps.Take(2).ToList()
    End Function

    Private Shared Function AuthToken() As String
        Using doc As System.Text.Json.JsonDocument = System.Text.Json.JsonDocument.Parse(AuthSteps()(1).Json)
            Return doc.RootElement.GetProperty("token").GetString()
        End Using
    End Function

    Private Shared Function JsonOf(env As ExchangeEnvelope) As String
        Return Encoding.UTF8.GetString(ExchangeEnvelopeCodec.Encode(env))
    End Function

    ''' <summary>What the server is scripted to see and answer after a sign-in, one request and its reply at a time.</summary>
    Private NotInheritable Class Script
        Private ReadOnly _steps As List(Of ExchangeTranscriptStep) = AuthSteps()
        Private ReadOnly _token As String = AuthToken()
        Private _id As Long

        Private Function Ask(request As Func(Of String, Long, ExchangeEnvelope)) As Long
            _id += 1
            _steps.Add(ExchangeTranscriptStep.Expect(JsonOf(request.Invoke(_token, _id))))
            Return _id
        End Function

        Public Function Op(request As Func(Of String, Long, ExchangeEnvelope), Optional dataJson As String = Nothing) As Script
            Dim id As Long = Ask(request)
            Dim json As String = "{""schemaVersion"":2,""type"":""admin-reply"",""id"":" & id.ToString(Globalization.CultureInfo.InvariantCulture) & ",""ok"":true"
            If dataJson IsNot Nothing Then json &= ",""data"":" & dataJson
            _steps.Add(ExchangeTranscriptStep.Supply(json & "}"))
            Return Me
        End Function

        Public Function Refuse(request As Func(Of String, Long, ExchangeEnvelope), code As String) As Script
            Dim id As Long = Ask(request)
            _steps.Add(ExchangeTranscriptStep.Supply("{""schemaVersion"":2,""type"":""admin-reply"",""id"":" & id.ToString(Globalization.CultureInfo.InvariantCulture) &
                                                     ",""ok"":false,""code"":""" & code & """,""error"":""words of the server""}"))
            Return Me
        End Function

        ''' <summary>The stream of a session that ends properly: the goodbye, then the server closes.</summary>
        Public Function Ending() As LockStepStream
            _steps.Add(ExchangeTranscriptStep.Expect(JsonOf(New ExchangeEnvelope(ExchangeEnvelopeTypes.Bye))))
            _steps.Add(ExchangeTranscriptStep.Closed())
            Return New LockStepStream(_steps)
        End Function

        ''' <summary>The stream of a session the server drops when this request arrives.</summary>
        Public Function DroppedOn(request As Func(Of String, Long, ExchangeEnvelope)) As LockStepStream
            Ask(request)
            _steps.Add(ExchangeTranscriptStep.Closed())
            Return New LockStepStream(_steps)
        End Function

        ''' <summary>The stream of a session whose goodbye is sent and never answered: only its bound ends it.</summary>
        Public Function ByeNeverAnswered() As LockStepStream
            _steps.Add(ExchangeTranscriptStep.Expect(JsonOf(New ExchangeEnvelope(ExchangeEnvelopeTypes.Bye))))
            Return New LockStepStream(_steps)
        End Function
    End Class

    Private Const AliceJson As String = "[{""login"":""alice"",""kind"":""member"",""devices"":1}]"
    Private Const AliceBobJson As String = "[{""login"":""alice"",""kind"":""member"",""devices"":1},{""login"":""bob"",""kind"":""member"",""devices"":0}]"
    Private Const PhoneJson As String = "[{""deviceId"":""d1"",""deviceName"":""Phone"",""product"":""fms-android"",""platform"":""android"",""lastSeen"":""2026-10-09T00:00:00Z"",""online"":true}]"
    Private Const InvitationJson As String = "[{""codeHash"":""abc123"",""kind"":""member"",""createdAt"":""2026-10-09T00:00:00Z"",""expectedLogin"":""carol"",""used"":false}]"
    Private Const PolicyJson As String = "{""maxAccounts"":10,""accounts"":1,""defaults"":{""devices"":3},""persisted"":true}"

    Private Shared ReadOnly ListAccounts As Func(Of String, Long, ExchangeEnvelope) = Function(t, i) ExchangeAdminWire.AccountList(t, i)
    Private Shared ReadOnly GrantsOfAlice As Func(Of String, Long, ExchangeEnvelope) = Function(t, i) ExchangeAdminWire.GrantList(t, i, "alice", "")
    Private Shared ReadOnly DevicesOfAlice As Func(Of String, Long, ExchangeEnvelope) = Function(t, i) ExchangeAdminWire.DeviceList(t, i, "alice")
    Private Shared ReadOnly ListInvitations As Func(Of String, Long, ExchangeEnvelope) = Function(t, i) ExchangeAdminWire.InvitationList(t, i)
    Private Shared ReadOnly SharedStatus As Func(Of String, Long, ExchangeEnvelope) = Function(t, i) ExchangeAdminWire.SharedInvitationStatus(t, i)
    Private Shared ReadOnly PolicyStatus As Func(Of String, Long, ExchangeEnvelope) = Function(t, i) ExchangeAdminWire.PolicyStatus(t, i)
    Private Shared ReadOnly ServerStatus As Func(Of String, Long, ExchangeEnvelope) = Function(t, i) ExchangeAdminWire.Status(t, i)

    ' ------------------------------------------------------------------------------------------------ the rig

    ''' <summary>A real window with a scripted controller. Made and disposed on the UI thread of the fact.</summary>
    Private NotInheritable Class Rig
        Implements IDisposable

        Public ReadOnly Time As New ManualAdminTime()
        Public ReadOnly Handshake As New ScriptedAdminHandshake()
        Public ReadOnly Port As New ExchangeDeviceAgentTests.MemoryPort()
        Public ReadOnly ConfirmPort As New ExchangeDeviceAgentTests.MemoryPort()
        Public ReadOnly Targets As ExchangeAdminTargets
        Public ReadOnly Asked As New List(Of RemoteAdminConfirmQuestion)()
        Public ReadOnly Lines As New List(Of String)()
        Public Answer As Boolean = True
        Public Tick As Boolean
        Public Dialog As Func(Of Form, DialogResult)
        Public Dialogs As Integer
        Public Controller As RemoteAdminController
        Public Window As Share_Settings_Form

        Private ReadOnly _gate As IDisposable
        Private ReadOnly _previousApp As String
        Private ReadOnly _previousFactory As Func(Of RemoteAdminController)
        Private ReadOnly _scratch As String

        Public Sub New(Optional gateOpen As Boolean = True)
            ExchangeTestSupport.Reset()
            _gate = ExchangeRemoteAdminFeatures.ForFlow(switchOn:=gateOpen, offered:=False)
            _previousApp = CompanionGlobals.SettingsAppNameOverride
            _previousFactory = Share_Settings_Form.RemoteControllerFactory
            _scratch = "FmsRemoteAdminTest_" & Guid.NewGuid().ToString("N")
            CompanionGlobals.SettingsAppNameOverride = _scratch
            Targets = New ExchangeAdminTargets(Port)
            Share_Settings_Form.RemoteControllerFactory =
                Function()
                    Controller = New RemoteAdminController(Targets, Handshake, AddressOf Time.Now, AddressOf Time.Delay, Sub(line As String) Lines.Add(line))
                    Return Controller
                End Function
            RemoteAdminActions.ConfirmStore = Function() New RemoteAdminConfirm(ConfirmPort)
            RemoteAdminActions.AskConfirm =
                Function(owner As IWin32Window, question As RemoteAdminConfirmQuestion)
                    Asked.Add(question)
                    question.DoNotAskAgain = Tick
                    Return Answer
                End Function
            RemoteAdminActions.RunDialog =
                Function(owner As IWin32Window, form As Form)
                    Dialogs += 1
                    Return If(Dialog Is Nothing, DialogResult.Cancel, Dialog.Invoke(form))
                End Function
        End Sub

        Public Function AddTarget(Optional name As String = TargetName, Optional address As String = TargetAddress) As ExchangeAdminTarget
            Dim target As ExchangeAdminTarget = Nothing
            Assert.Equal(ExchangeAdminTargetResult.Saved, Targets.Add(name, address, target))
            Return target
        End Function

        Public Function Open() As Share_Settings_Form
            Window = New Share_Settings_Form(destination:=SettingsDestination.RemoteExchange)
            Window.ConfirmDraftClose = Function(isExchange) DialogResult.No
            Window.Opacity = 0
            Window.ShowInTaskbar = False
            ' what MainWindow.OpenSettings does before it shows a settings window: the theme, and with it the help badge
            ThemeService.Attach(Window)
            Window.Show()
            Application.DoEvents()
            Return Window
        End Function

        Public Function Field(Of T)(name As String) As T
            Return FieldOf(Of T)(Window, name)
        End Function

        ''' <summary>Picks the only target as the list does.</summary>
        Public Sub PickTarget(Optional index As Integer = 0)
            Field(Of ComboBox)("_remoteTargetList").SelectedIndex = index
            Pump(Function() Controller.State.Phase = RemoteAdminPhase.SignedOut, "signed out on the chosen target")
        End Sub

        ''' <summary>Types the password and clicks Sign in.</summary>
        Public Sub ClickSignIn()
            Field(Of TextBox)("_remotePassword").Text = Secret
            Application.DoEvents()
            Field(Of Button)("_remoteSignIn").PerformClick()
        End Sub

        Public Sub SignIn(stream As LockStepStream)
            Handshake.EnqueueProbe(True).EnqueueConnect(stream)
            ClickSignIn()
            Pump(Function() Controller.State.Phase = RemoteAdminPhase.SignedIn, "signed in")
        End Sub

        Public Function Card(id As String) As RemoteAdminCardBase
            Dim cards As IReadOnlyList(Of RemoteAdminCard) = Field(Of IReadOnlyList(Of RemoteAdminCard))("_remoteCards")
            Return DirectCast(cards.First(Function(c As RemoteAdminCard) c.RailId = id), RemoteAdminCardBase)
        End Function

        ''' <summary>A signed-in window showing this card, with its first read played.</summary>
        Public Function SignedInOn(id As String, stream As LockStepStream, playedAfterRead As Integer) As RemoteAdminCardBase
            AddTarget()
            Open()
            PickTarget()
            Dim shown As RemoteAdminCardBase = Card(id)
            ExchangeTestSupport.ShowSettingsCardOf(Window, shown)
            SignIn(stream)
            Played(stream, playedAfterRead, "the first read of " & id)
            Settled(shown)
            Return shown
        End Function

        ''' <summary>Runs an action button of the card and waits until it and its re-read have finished.</summary>
        Public Sub Act(shown As RemoteAdminCardBase, index As Integer)
            Dim button As Button = FieldOf(Of List(Of Button))(shown, "_actions")(index)
            Assert.True(button.Enabled, "action " & index.ToString(Globalization.CultureInfo.InvariantCulture) & " of " & shown.RailId & " is disabled")
            button.PerformClick()
            Settled(shown)
        End Sub

        Public Sub Settled(shown As RemoteAdminCardBase)
            Pump(Function() Not FieldOf(Of Boolean)(shown, "_acting") AndAlso Not FieldOf(Of Boolean)(shown, "_reading"), "the card at rest")
        End Sub

        ''' <summary>Clicks Sign out, waits for the goodbye and proves the script ran to its very end.</summary>
        Public Sub SignOutAndVerify()
            Field(Of Button)("_remoteSignOut").PerformClick()
            Pump(Function() Controller.State.Phase <> RemoteAdminPhase.SignedIn, "signed out")
            Assert.True(RemoteAdminController.WaitForEndings(TimeSpan.FromSeconds(3)), "the goodbye did not finish")
            Handshake.Verify()
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                If Window IsNot Nothing AndAlso Not Window.IsDisposed Then
                    Window.Close()
                    Window.Dispose()
                End If
                Application.DoEvents()
                RemoteAdminController.WaitForEndings(TimeSpan.FromSeconds(2))
            Finally
                Share_Settings_Form.RemoteControllerFactory = _previousFactory
                RemoteAdminActions.ResetSeams()
                CompanionGlobals.SettingsAppNameOverride = _previousApp
                Try
                    Microsoft.VisualBasic.Interaction.DeleteSetting(_scratch)
                Catch ex As ArgumentException
                    ' nothing was written under the scratch name
                End Try
                ExchangeTestSupport.Reset()
                _gate.Dispose()
            End Try
        End Sub
    End Class

    ''' <summary>Reads a field through the whole base chain: the private fields of a card live in its base class.</summary>
    Private Shared Function FieldOf(Of T)(owner As Object, name As String) As T
        Dim type As Type = owner.GetType()
        While type IsNot Nothing
            Dim info As FieldInfo = type.GetField(name, Flags Or BindingFlags.DeclaredOnly)
            If info IsNot Nothing Then Return DirectCast(info.GetValue(owner), T)
            type = type.BaseType
        End While
        Assert.Fail("no field " & name & " on " & owner.GetType().Name)
        Return Nothing
    End Function

    ''' <summary>Runs the message loop until the condition holds: continuations and marshalled paints need it.</summary>
    Private Shared Sub Pump(condition As Func(Of Boolean), what As String, Optional limitMs As Integer = 3000)
        Dim watch As Stopwatch = Stopwatch.StartNew()
        Do
            Application.DoEvents()
            If condition.Invoke() Then Return
            If watch.ElapsedMilliseconds > limitMs Then Assert.Fail("not reached in " & limitMs.ToString(Globalization.CultureInfo.InvariantCulture) & " ms: " & what)
            Thread.Sleep(1)
        Loop
    End Sub

    Private Shared Sub Played(stream As LockStepStream, steps As Integer, what As String)
        Pump(Function() stream.Played >= steps, what & " (played " & stream.Played.ToString(Globalization.CultureInfo.InvariantCulture) & " of " & steps.ToString(Globalization.CultureInfo.InvariantCulture) & ")")
    End Sub

    Private Shared Sub SelectRow(shown As RemoteAdminCardBase, index As Integer)
        shown.ListAt(0).Items(index).Selected = True
        Application.DoEvents()
    End Sub

    ''' <summary>The steps a sign-in and N requests with replies take.</summary>
    Private Shared Function StepsOf(requests As Integer) As Integer
        Return 2 + 2 * requests
    End Function

    Private Shared Sub Run(body As Action(Of Rig), Optional gateOpen As Boolean = True)
        ExchangeTestSupport.OnUi(
            Sub()
                Using rig As New Rig(gateOpen)
                    body.Invoke(rig)
                End Using
            End Sub)
    End Sub

    ' ------------------------------------------------------------------------------------------------ T-6: the window, phase by phase

    <Fact>
    Public Sub The_window_starts_at_no_target_touches_nothing_and_has_the_six_cards_in_order()
        Run(Sub(rig As Rig)
                rig.Open()
                Assert.Equal(RemoteAdminPhase.NoTarget, rig.Controller.State.Phase)
                Assert.Empty(rig.Handshake.Calls())
                Assert.Equal(0, rig.Port.Writes)
                Assert.Equal(0, rig.ConfirmPort.Writes)
                Dim ids As String() = ExchangeTestSupport.SettingsGroups(rig.Window, "remote-exchange").Select(Function(g) g.Id).ToArray()
                Assert.Equal({"status", "accounts", "devices", "invitations", "shared-invitation", "limits"}, ids)
                For Each id As String In {"status", "accounts", "devices", "invitations", "shared-invitation", "limits"}
                    Assert.False(rig.Card(id).ActionsEnabled, id & " offers an action with no session")
                    Assert.False(rig.Card(id).RefreshEnabled, id & " offers a refresh with no session")
                    Assert.False(rig.Card(id).ShowsContent, id & " shows content with no session")
                    Assert.NotEqual("", rig.Card(id).StateLine)
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub Every_phase_shows_its_own_controls()
        Run(Sub(rig As Rig)
                rig.AddTarget()
                rig.Open()
                Dim password As TextBox = rig.Field(Of TextBox)("_remotePassword")
                Dim signIn As Button = rig.Field(Of Button)("_remoteSignIn")
                Dim cancel As Button = rig.Field(Of Button)("_remoteCancel")
                Dim signOut As Button = rig.Field(Of Button)("_remoteSignOut")
                Dim targets As ComboBox = rig.Field(Of ComboBox)("_remoteTargetList")
                Dim edit As Button = rig.Field(Of Button)("_remoteEdit")
                Dim trust As FlowLayoutPanel = rig.Field(Of FlowLayoutPanel)("_remoteTrust")
                Dim off As FlowLayoutPanel = rig.Field(Of FlowLayoutPanel)("_remoteDisabled")

                ' NoTarget
                Assert.True(targets.Enabled)
                Assert.False(edit.Enabled)
                Assert.False(password.Enabled)
                Assert.False(signIn.Enabled)
                Assert.False(cancel.Enabled)
                Assert.False(signOut.Enabled)
                Assert.False(trust.Visible)
                Assert.False(off.Visible)

                ' SignedOut: the empty field holds the sign-in back and says why
                rig.PickTarget()
                Assert.True(edit.Enabled)
                Assert.True(password.Enabled)
                Assert.False(signIn.Enabled)
                Assert.True(rig.Field(Of Label)("_remoteSignInHint").Visible)
                password.Text = Secret
                Application.DoEvents()
                Assert.True(signIn.Enabled)
                Assert.False(rig.Field(Of Label)("_remoteSignInHint").Visible)
                password.Clear()

                ' CheckingServer, and its Cancel
                rig.Handshake.EnqueueProbeHang()
                rig.ClickSignIn()
                Pump(Function() rig.Controller.State.Phase = RemoteAdminPhase.CheckingServer, "checking the server")
                Assert.True(cancel.Enabled)
                Assert.False(signIn.Enabled)
                Assert.False(targets.Enabled)
                Assert.False(signOut.Enabled)
                cancel.PerformClick()
                Pump(Function() rig.Controller.State.Phase = RemoteAdminPhase.SignedOut, "back after Cancel")

                ' AskingTrust: the panel names the fingerprint, "no" is the default answer, and "no" connects to nothing
                rig.Handshake.EnqueueProbe(False)
                rig.ClickSignIn()
                Pump(Function() rig.Controller.State.Phase = RemoteAdminPhase.AskingTrust, "asking about the certificate")
                Assert.True(trust.Visible)
                Assert.Equal(ScriptedTransport.DefaultFingerprint, rig.Field(Of TextBox)("_remoteFingerprint").Text)
                Assert.Same(rig.Field(Of Button)("_remoteTrustNo"), rig.Window.AcceptButton)
                Assert.Contains(TargetName, rig.Field(Of Label)("_remoteTrustNote").Text)
                Assert.Contains(TargetAddress, rig.Field(Of Label)("_remoteTrustNote").Text)
                rig.Field(Of Button)("_remoteTrustNo").PerformClick()
                Pump(Function() rig.Controller.State.Phase = RemoteAdminPhase.SignedOut, "back after No")
                Assert.False(trust.Visible)
                Assert.Equal("", rig.Field(Of TextBox)("_remoteFingerprint").Text)
                Assert.DoesNotContain(rig.Handshake.Calls(), Function(call_ As String) call_.StartsWith("connect", StringComparison.Ordinal))

                ' SigningIn (the server takes the password and never answers), and its Cancel
                rig.Handshake.EnqueueProbe(True).EnqueueConnect(New LockStepStream(New ExchangeTranscriptStep() {AuthSteps()(0)}))
                rig.ClickSignIn()
                Pump(Function() rig.Controller.State.Phase = RemoteAdminPhase.SigningIn, "signing in")
                Assert.True(cancel.Enabled)
                Assert.False(password.Enabled)
                cancel.PerformClick()
                Pump(Function() rig.Controller.State.Phase = RemoteAdminPhase.SignedOut, "back after Cancel of the sign-in")

                ' Disabled: its own card; Try again is a new sign-in
                Dim refused As New LockStepStream(New ExchangeTranscriptStep() {AuthSteps()(0), ExchangeTranscriptStep.Supply("{""schemaVersion"":2,""type"":""refused"",""reason"":""admin-disabled""}")})
                rig.Handshake.EnqueueProbe(True).EnqueueConnect(refused)
                rig.ClickSignIn()
                Pump(Function() rig.Controller.State.Phase = RemoteAdminPhase.Disabled, "administration off")
                Assert.True(off.Visible)
                Assert.NotEqual("", rig.Field(Of Label)("_remoteDisabledNote").Text)
                password.Text = Secret
                Application.DoEvents()
                Assert.True(rig.Field(Of Button)("_remoteTryAgain").Enabled)
                rig.Handshake.EnqueueProbe(True).EnqueueConnect(New Script().Op(ServerStatus, "{""accounts"":2}").Ending())
                rig.Field(Of Button)("_remoteTryAgain").PerformClick()

                ' SignedIn: the field is empty, it is shut, and the first card holds what the server said
                Pump(Function() rig.Controller.State.Phase = RemoteAdminPhase.SignedIn, "signed in by Try again")
                Assert.False(off.Visible)
                Assert.Equal(0, password.TextLength)
                Assert.False(password.Enabled)
                Assert.False(signIn.Enabled)
                Assert.True(targets.Enabled, "another server can be chosen while signed in: the choice ends this session first")
                Assert.False(edit.Enabled)
                Assert.True(signOut.Enabled)
                Pump(Function() rig.Card("status").RowCount = 1, "the first card filled")
                Assert.Contains("2", rig.Card("status").RowText(0))
                rig.Settled(rig.Card("status"))
                Assert.Empty(rig.Lines.Where(Function(line As String) line.Contains(Secret)))
                rig.SignOutAndVerify()
                Assert.False(signOut.Enabled)
                Assert.True(password.Enabled)
            End Sub)
    End Sub

    <Fact>
    Public Sub A_changed_certificate_names_both_fingerprints_in_the_banner_and_does_not_read_the_password()
        Run(Sub(rig As Rig)
                Dim otherPin As String = "SHA256:" & New String("B"c, 43)
                Dim target As ExchangeAdminTarget = rig.AddTarget()
                Assert.True(rig.Targets.SetPins(target.Id, otherPin, "SHA256:" & New String("C"c, 43)))
                rig.Open()
                rig.PickTarget()
                rig.Handshake.EnqueueConnectFailure(ExchangeConnectFailure.PinMismatch, ScriptedTransport.DefaultFingerprint).EnqueueProbe(False)
                rig.ClickSignIn()
                Dim banner As Label = rig.Field(Of Label)("_remoteBanner")
                Pump(Function() rig.Controller.StoppedByCertificate IsNot Nothing, "the stop")
                Application.DoEvents()
                Assert.Contains(otherPin, banner.Text)
                Assert.Contains(ScriptedTransport.DefaultFingerprint, banner.Text)
                Assert.True(rig.Field(Of Label)("_remoteStopNote").Visible)
                Assert.Equal(Secret, rig.Field(Of TextBox)("_remotePassword").Text)

                ' a second click checks the certificate again, and a certificate still changed still reads no password
                rig.Handshake.EnqueueConnectFailure(ExchangeConnectFailure.PinMismatch, ScriptedTransport.DefaultFingerprint).EnqueueProbe(False)
                rig.Field(Of Button)("_remoteSignIn").PerformClick()
                Pump(Function() rig.Handshake.Calls().Count = 4 AndAlso rig.Controller.StoppedByCertificate IsNot Nothing, "the second stop")
                Assert.Equal(Secret, rig.Field(Of TextBox)("_remotePassword").Text)
                rig.Handshake.Verify()
            End Sub)
    End Sub

    <Fact>
    Public Sub A_dropped_session_keeps_the_rows_grey_under_the_last_known_caption_and_a_new_theme_keeps_them_grey()
        Run(Sub(rig As Rig)
                Dim stream As LockStepStream = New Script().Op(ListAccounts, AliceJson).DroppedOn(ListAccounts)
                Dim card As RemoteAdminCardBase = rig.SignedInOn("accounts", stream, StepsOf(1))
                Assert.Equal(1, card.RowCount)
                Assert.False(card.IsGreyed)

                FieldOf(Of Button)(card, "_btnRefresh").PerformClick()
                Pump(Function() rig.Controller.State.Phase <> RemoteAdminPhase.SignedIn, "the session lost")
                rig.Settled(card)
                Assert.Equal(RemoteAdminPhase.SignedOut, rig.Controller.State.Phase)
                Assert.True(card.IsGreyed)
                Assert.Equal(1, card.RowCount)
                Assert.NotEqual("", card.LastKnownCaption)
                Assert.False(card.ActionsEnabled)
                Assert.False(card.RefreshEnabled)
                Assert.Equal(ThemeService.Palette.ColorOf(ThemeRole.TextDisabled), card.ListAt(0).ForeColor)

                ' a live theme change paints every card again, by role
                card.ListAt(0).ForeColor = Drawing.Color.Magenta
                FieldOf(Of Label)(card, "_lblLast").ForeColor = Drawing.Color.Magenta
                DirectCast(rig.Window, IThemeAware).ApplyTheme(ThemeService.Palette)
                Assert.Equal(ThemeService.Palette.ColorOf(ThemeRole.TextDisabled), card.ListAt(0).ForeColor)
                Assert.Equal(ThemeService.Palette.ColorOf(ThemeRole.TextMuted), FieldOf(Of Label)(card, "_lblLast").ForeColor)
            End Sub)
    End Sub

    <Fact>
    Public Sub A_card_not_on_screen_reads_nothing_until_it_is_shown()
        Run(Sub(rig As Rig)
                ' the Status card is on screen at the sign-in, so it alone reads; choosing Limits then reads Limits, once
                Dim stream As LockStepStream = New Script().Op(ServerStatus, "{""accounts"":2}").Op(PolicyStatus, PolicyJson).Ending()
                rig.AddTarget()
                rig.Open()
                rig.PickTarget()
                ExchangeTestSupport.ShowSettingsCardOf(rig.Window, rig.Card("status"))
                rig.SignIn(stream)
                Played(stream, StepsOf(1), "the status read")
                rig.Settled(rig.Card("status"))
                Assert.Equal(0, rig.Card("limits").RowCount)
                ExchangeTestSupport.ShowSettingsCardOf(rig.Window, rig.Card("limits"))
                Played(stream, StepsOf(2), "the limits read")
                rig.Settled(rig.Card("limits"))
                Assert.NotEqual(0, rig.Card("limits").RowCount)
                rig.SignOutAndVerify()
            End Sub)
    End Sub

    ' ------------------------------------------------------------------------------------------------ T-7: every mutation through its button

    <Fact>
    Public Sub Adding_an_account_writes_it_and_reads_the_list_again()
        Run(Sub(rig As Rig)
                Dim stream As LockStepStream = New Script().Op(ListAccounts, AliceJson).
                    Op(Function(t, i) ExchangeAdminWire.AccountAdd(t, i, "bob", NewPassword, "member"), "{""login"":""bob"",""overCap"":false}").
                    Op(ListAccounts, AliceBobJson).Ending()
                Dim card As RemoteAdminCardBase = rig.SignedInOn("accounts", stream, StepsOf(1))
                rig.Dialog = Function(form As Form)
                                 Dim dialog As Remote_Admin_Account_Form = DirectCast(form, Remote_Admin_Account_Form)
                                 dialog.LoginControl.Text = "bob"
                                 dialog.PasswordControl.Text = NewPassword
                                 Return If(dialog.TryAccept(), DialogResult.OK, DialogResult.Abort)
                             End Function
                rig.Act(card, 0)
                Assert.Equal(2, card.RowCount)
                Assert.Contains(TargetName, card.StateLine)
                Assert.Contains(TargetAddress, card.StateLine)
                Assert.DoesNotContain(NewPassword, card.StateLine)
                rig.SignOutAndVerify()
            End Sub)
    End Sub

    <Fact>
    Public Sub A_refused_login_is_named_beside_its_field_and_the_dialog_stays()
        Run(Sub(rig As Rig)
                Dim stream As LockStepStream = New Script().Op(ListAccounts, AliceJson).
                    Refuse(Function(t, i) ExchangeAdminWire.AccountAdd(t, i, "alice", NewPassword, "member"), ExchangeAdminCodes.Exists).Ending()
                Dim card As RemoteAdminCardBase = rig.SignedInOn("accounts", stream, StepsOf(1))
                Dim reason As String = Nothing
                rig.Dialog = Function(form As Form)
                                 Dim dialog As Remote_Admin_Account_Form = DirectCast(form, Remote_Admin_Account_Form)
                                 If rig.Dialogs > 1 Then
                                     reason = dialog.LoginReasonLabel.Text
                                     Return DialogResult.Cancel
                                 End If
                                 dialog.LoginControl.Text = "alice"
                                 dialog.PasswordControl.Text = NewPassword
                                 Return If(dialog.TryAccept(), DialogResult.OK, DialogResult.Abort)
                             End Function
                rig.Act(card, 0)
                Assert.Equal(2, rig.Dialogs)
                Assert.False(String.IsNullOrEmpty(reason), "the refusal must be named beside the login")
                Assert.Equal(1, card.RowCount)
                rig.SignOutAndVerify()
            End Sub)
    End Sub

    <Fact>
    Public Sub Changing_a_password_sends_it_and_reads_the_list_again()
        Run(Sub(rig As Rig)
                Dim stream As LockStepStream = New Script().Op(ListAccounts, AliceJson).Op(GrantsOfAlice, "[]").
                    Op(Function(t, i) ExchangeAdminWire.AccountPasswd(t, i, "alice", NewPassword), "{""signedOut"":0}").
                    Op(ListAccounts, AliceJson).Op(GrantsOfAlice, "[]").Ending()
                Dim card As RemoteAdminCardBase = rig.SignedInOn("accounts", stream, StepsOf(1))
                SelectRow(card, 0)
                Played(stream, StepsOf(2), "the grants of the chosen account")
                rig.Dialog = Function(form As Form)
                                 Dim dialog As Remote_Admin_Account_Form = DirectCast(form, Remote_Admin_Account_Form)
                                 dialog.PasswordControl.Text = NewPassword
                                 Return If(dialog.TryAccept(), DialogResult.OK, DialogResult.Abort)
                             End Function
                rig.Act(card, 1)
                Played(stream, StepsOf(5), "the list and the grants again")
                rig.Settled(card)
                rig.SignOutAndVerify()
            End Sub)
    End Sub

    <Fact>
    Public Sub Changing_a_kind_and_the_limits_of_an_account_write_them_and_read_the_list_again()
        Run(Sub(rig As Rig)
                Dim limits As New RemoteAdminLimits(3, Nothing, Nothing, Nothing, Nothing)
                Dim stream As LockStepStream = New Script().Op(ListAccounts, AliceJson).Op(GrantsOfAlice, "[]").
                    Op(Function(t, i) ExchangeAdminWire.AccountKind(t, i, "alice", "guest"), "{""changed"":true}").
                    Op(ListAccounts, AliceJson).Op(GrantsOfAlice, "[]").
                    Op(Function(t, i) ExchangeAdminWire.AccountLimits(t, i, "alice", limits.ToWire())).
                    Op(ListAccounts, AliceJson).Op(GrantsOfAlice, "[]").Ending()
                Dim card As RemoteAdminCardBase = rig.SignedInOn("accounts", stream, StepsOf(1))
                SelectRow(card, 0)
                Played(stream, StepsOf(2), "the grants of the chosen account")

                rig.Dialog = Function(form As Form)
                                 FieldOf(Of RadioButton)(form, "_rbGuest").Checked = True
                                 Return If(DirectCast(form, Remote_Admin_Account_Form).TryAccept(), DialogResult.OK, DialogResult.Abort)
                             End Function
                rig.Act(card, 2)
                Played(stream, StepsOf(5), "the re-read after the kind")
                rig.Settled(card)

                rig.Dialog = Function(form As Form)
                                 Dim dialog As Remote_Admin_Limits_Form = DirectCast(form, Remote_Admin_Limits_Form)
                                 dialog.Box(0).Text = "3"
                                 Return If(dialog.TryAccept(), DialogResult.OK, DialogResult.Abort)
                             End Function
                rig.Act(card, 3)
                Played(stream, StepsOf(8), "the re-read after the limits")
                rig.Settled(card)
                rig.SignOutAndVerify()
            End Sub)
    End Sub

    <Fact>
    Public Sub Removing_an_account_asks_first_a_decline_sends_nothing_and_a_yes_removes_and_reads_again()
        Run(Sub(rig As Rig)
                Dim stream As LockStepStream = New Script().Op(ListAccounts, AliceJson).Op(GrantsOfAlice, "[]").
                    Op(Function(t, i) ExchangeAdminWire.AccountRemove(t, i, "alice")).
                    Op(ListAccounts, "[]").Ending()
                Dim card As RemoteAdminCardBase = rig.SignedInOn("accounts", stream, StepsOf(1))
                SelectRow(card, 0)
                Played(stream, StepsOf(2), "the grants of the chosen account")

                rig.Answer = False
                rig.Act(card, 4)
                Assert.Single(rig.Asked)
                Assert.Equal(StepsOf(2), stream.Played)
                Assert.Equal(1, card.RowCount)

                rig.Answer = True
                rig.Act(card, 4)
                Assert.Equal(2, rig.Asked.Count)
                Assert.Equal(RemoteAdminConfirmKind.AccountRemove, rig.Asked(1).Kind)
                Assert.Contains(TargetName, rig.Asked(1).Text)
                Assert.Contains(TargetAddress, rig.Asked(1).Text)
                Assert.Contains("alice", rig.Asked(1).Text)
                Assert.Equal(0, card.RowCount)
                rig.SignOutAndVerify()
            End Sub)
    End Sub

    <Fact>
    Public Sub Revoking_a_device_asks_first_and_a_device_gone_already_still_reads_again()
        Run(Sub(rig As Rig)
                Dim stream As LockStepStream = New Script().Op(ListAccounts, AliceJson).Op(DevicesOfAlice, PhoneJson).
                    Refuse(Function(t, i) ExchangeAdminWire.DeviceRevoke(t, i, "alice", "d1"), ExchangeAdminCodes.NotFound).
                    Op(ListAccounts, AliceJson).Op(DevicesOfAlice, PhoneJson).
                    Op(Function(t, i) ExchangeAdminWire.DeviceRevoke(t, i, "alice", "d1")).
                    Op(ListAccounts, AliceJson).Op(DevicesOfAlice, "[]").Ending()
                Dim card As RemoteAdminCardBase = rig.SignedInOn("devices", stream, StepsOf(2))
                Assert.Equal(1, card.RowCount)
                SelectRow(card, 0)
                rig.Act(card, 0)
                Assert.Equal(RemoteAdminConfirmKind.DeviceRevoke, rig.Asked(0).Kind)
                Assert.Contains(TargetName, rig.Asked(0).Text)
                Assert.Contains(TargetAddress, rig.Asked(0).Text)
                Assert.Contains("Phone", rig.Asked(0).Text)
                Assert.Contains(RemoteAdminWords.DeviceRevokeNotFound(), card.StateLine)
                Assert.Equal(1, card.RowCount)

                SelectRow(card, 0)
                rig.Act(card, 0)
                Assert.Equal(0, card.RowCount)
                rig.SignOutAndVerify()
            End Sub)
    End Sub

    <Fact>
    Public Sub A_pairing_code_is_shown_once_and_needs_no_read_again()
        Run(Sub(rig As Rig)
                Dim stream As LockStepStream = New Script().Op(ListAccounts, AliceJson).Op(DevicesOfAlice, PhoneJson).
                    Op(Function(t, i) ExchangeAdminWire.DevicePairingCode(t, i, "alice"), "{""code"":""9HG46Z75"",""expiresAt"":""2026-10-07T12:26:09Z""}").Ending()
                Dim card As RemoteAdminCardBase = rig.SignedInOn("devices", stream, StepsOf(2))
                Dim shown As String = Nothing
                rig.Dialog = Function(form As Form)
                                 shown = DirectCast(form, Remote_Admin_Invitation_Form).CodeBox.Text
                                 Return DialogResult.OK
                             End Function
                rig.Act(card, 1)
                Assert.Equal("9HG46Z75", shown)
                Assert.DoesNotContain("9HG46Z75", card.StateLine)
                rig.SignOutAndVerify()
            End Sub)
    End Sub

    <Fact>
    Public Sub Creating_and_withdrawing_an_invitation_read_the_list_again_and_the_withdraw_asks_first()
        Run(Sub(rig As Rig)
                Dim stream As LockStepStream = New Script().Op(ListInvitations, InvitationJson).
                    Op(Function(t, i) ExchangeAdminWire.InvitationCreate(t, i, "member", ""), "{""code"":""CODE-ONE"",""invitation"":{""codeHash"":""def456"",""kind"":""member"",""createdAt"":""2026-10-09T00:00:00Z"",""used"":false},""connectionString"":""""}").
                    Op(ListInvitations, InvitationJson).
                    Op(Function(t, i) ExchangeAdminWire.InvitationWithdraw(t, i, "abc123")).
                    Op(ListInvitations, "[]").Ending()
                Dim card As RemoteAdminCardBase = rig.SignedInOn("invitations", stream, StepsOf(1))
                Dim code As String = Nothing
                Dim noStringCopy As Boolean = False
                rig.Dialog = Function(form As Form)
                                 Dim dialog As Remote_Admin_Invitation_Form = DirectCast(form, Remote_Admin_Invitation_Form)
                                 If rig.Dialogs = 1 Then Return DialogResult.OK
                                 code = dialog.CodeBox.Text
                                 noStringCopy = dialog.CopyStringButton Is Nothing AndAlso dialog.ConnectionStringBox Is Nothing
                                 Return DialogResult.OK
                             End Function
                rig.Act(card, 0)
                Assert.Equal("CODE-ONE", code)
                Assert.True(noStringCopy, "an invitation with no connection string offers nothing to copy for it")
                Assert.DoesNotContain("CODE-ONE", card.StateLine)
                Assert.DoesNotContain("CODE-ONE", card.RowText(0))

                SelectRow(card, 0)
                rig.Act(card, 1)
                Assert.Equal(RemoteAdminConfirmKind.InvitationWithdraw, rig.Asked(0).Kind)
                Assert.Contains(TargetName, rig.Asked(0).Text)
                Assert.Contains(TargetAddress, rig.Asked(0).Text)
                Assert.Equal(0, card.RowCount)
                rig.SignOutAndVerify()
            End Sub)
    End Sub

    <Fact>
    Public Sub The_shared_invitation_is_set_and_turned_off_each_by_its_own_button()
        Run(Sub(rig As Rig)
                Dim stream As LockStepStream = New Script().Op(SharedStatus, "{""enabled"":true,""kind"":""member""}").
                    Op(Function(t, i) ExchangeAdminWire.SharedInvitationSet(t, i, NewPassword, "member")).
                    Op(SharedStatus, "{""enabled"":true,""kind"":""member""}").
                    Op(Function(t, i) ExchangeAdminWire.SharedInvitationSet(t, i, "", "member")).
                    Op(SharedStatus, "{""enabled"":false}").Ending()
                Dim card As RemoteAdminCardBase = rig.SignedInOn("shared-invitation", stream, StepsOf(1))
                rig.Dialog = Function(form As Form)
                                 Dim dialog As Remote_Admin_Shared_Form = DirectCast(form, Remote_Admin_Shared_Form)
                                 dialog.PasswordControl.Text = NewPassword
                                 Return If(dialog.TryAccept(), DialogResult.OK, DialogResult.Abort)
                             End Function
                rig.Act(card, 0)
                rig.Dialog = Nothing
                Dim before As Integer = rig.Dialogs
                rig.Act(card, 1)
                Assert.Equal(before, rig.Dialogs)
                Assert.False(card.ActionsEnabled AndAlso FieldOf(Of List(Of Button))(card, "_actions")(1).Enabled, "off is not offered once it is off")
                rig.SignOutAndVerify()
            End Sub)
    End Sub

    <Fact>
    Public Sub The_policy_sends_only_what_changed_and_reads_again_and_an_unchanged_form_sends_nothing()
        Run(Sub(rig As Rig)
                Dim stream As LockStepStream = New Script().Op(PolicyStatus, PolicyJson).
                    Op(Function(t, i) ExchangeAdminWire.PolicySet(t, i, 12, RemoteAdminLimits.Empty.ToWire()), PolicyJson).
                    Op(PolicyStatus, PolicyJson).Ending()
                Dim card As RemoteAdminCardBase = rig.SignedInOn("limits", stream, StepsOf(1))
                rig.Dialog = Function(form As Form) If(DirectCast(form, Remote_Admin_Limits_Form).TryAccept(), DialogResult.OK, DialogResult.Abort)
                rig.Act(card, 0)
                Assert.Equal(StepsOf(1), stream.Played)
                Assert.NotEqual("", card.StateLine)

                rig.Dialog = Function(form As Form)
                                 Dim dialog As Remote_Admin_Limits_Form = DirectCast(form, Remote_Admin_Limits_Form)
                                 dialog.MaxAccountsBox.Text = "12"
                                 Return If(dialog.TryAccept(), DialogResult.OK, DialogResult.Abort)
                             End Function
                rig.Act(card, 0)
                rig.SignOutAndVerify()
            End Sub)
    End Sub

    ' ------------------------------------------------------------------------------------------------ T-7: confirmations and the dialogs themselves

    <Fact>
    Public Sub Every_confirmation_names_the_target_declines_by_default_and_on_escape_and_on_enter()
        Run(Sub(rig As Rig)
                rig.AddTarget()
                rig.Open()
                rig.PickTarget()
                Dim questions As RemoteAdminConfirmQuestion() = {
                    RemoteAdminActions.AccountRemoveQuestion(rig.Controller, "alice"),
                    RemoteAdminActions.DeviceRevokeQuestion(rig.Controller, "alice", New RemoteAdminDeviceRow("d1", "Phone", "fms-android", "", "android", New List(Of String)(), "", "2026-10-09T00:00:00Z", True, False)),
                    RemoteAdminActions.WithdrawQuestion(rig.Controller, New RemoteAdminInvitationRow("abc123", "member", "2026-10-09T00:00:00Z", "carol", False, "", ""))}
                Dim process As MethodInfo = GetType(Form).GetMethod("ProcessDialogKey", Flags)
                For Each question As RemoteAdminConfirmQuestion In questions
                    Assert.Contains(TargetName, question.Text)
                    Assert.Contains(TargetAddress, question.Text)
                    Using form As New Remote_Admin_Confirm_Form(question)
                        form.Opacity = 0
                        form.ShowInTaskbar = False
                        form.Show()
                        Application.DoEvents()
                        Assert.Same(form.SafeButton, form.AcceptButton)
                        Assert.Same(form.SafeButton, form.CancelButton)
                        Assert.Same(form.SafeButton, form.ActiveControl)
                        Assert.False(form.NeverBox.Checked, "the opt-out starts unticked")
                        process.Invoke(form, New Object() {Keys.Escape})
                        Assert.Equal(DialogResult.Cancel, form.DialogResult)
                        form.DialogResult = DialogResult.None
                        process.Invoke(form, New Object() {Keys.Enter})
                        Assert.Equal(DialogResult.Cancel, form.DialogResult)
                        form.DialogResult = DialogResult.None
                        form.ActButton.PerformClick()
                        Assert.Equal(DialogResult.OK, form.DialogResult)
                        form.DialogResult = DialogResult.None
                    End Using
                Next
                Assert.Equal(0, rig.ConfirmPort.Writes)
            End Sub)
    End Sub

    <Fact>
    Public Sub A_ticked_box_skips_only_its_own_kind_and_ask_again_switches_on_and_resets()
        Run(Sub(rig As Rig)
                Dim stream As LockStepStream = New Script().Op(ListAccounts, AliceBobJson).Op(GrantsOfAlice, "[]").
                    Op(Function(t, i) ExchangeAdminWire.AccountRemove(t, i, "alice")).Op(ListAccounts, AliceBobJson).Op(GrantsOfAlice, "[]").
                    Op(Function(t, i) ExchangeAdminWire.AccountRemove(t, i, "alice")).Op(ListAccounts, AliceBobJson).Op(GrantsOfAlice, "[]").
                    Op(Function(t, i) ExchangeAdminWire.AccountRemove(t, i, "alice")).Op(ListAccounts, AliceBobJson).Op(GrantsOfAlice, "[]").Ending()
                Dim card As RemoteAdminCardBase = rig.SignedInOn("accounts", stream, StepsOf(1))
                Dim askAgain As Button = rig.Field(Of Button)("_remoteAskAgain")
                Assert.False(askAgain.Enabled)
                SelectRow(card, 0)
                Played(stream, StepsOf(2), "the grants of the chosen account")

                rig.Tick = True
                rig.Act(card, 4)
                Played(stream, StepsOf(5), "the first removal")
                rig.Settled(card)
                Assert.Single(rig.Asked)
                Pump(Function() askAgain.Enabled, "Ask again after the ticked answer")
                Dim store As New RemoteAdminConfirm(rig.ConfirmPort)
                Assert.True(store.IsSkipped(RemoteAdminConfirmKind.AccountRemove))
                Assert.False(store.IsSkipped(RemoteAdminConfirmKind.DeviceRevoke))
                Assert.False(store.IsSkipped(RemoteAdminConfirmKind.InvitationWithdraw))

                rig.Tick = False
                rig.Act(card, 4)
                Played(stream, StepsOf(8), "the second removal")
                rig.Settled(card)
                Assert.Single(rig.Asked)

                askAgain.PerformClick()
                Assert.False(askAgain.Enabled)
                Assert.False(store.IsSkipped(RemoteAdminConfirmKind.AccountRemove))
                rig.Act(card, 4)
                Played(stream, StepsOf(11), "the third removal")
                rig.Settled(card)
                Assert.Equal(2, rig.Asked.Count)
                rig.SignOutAndVerify()
            End Sub)
    End Sub

    ''' <summary>Shows a dialog modally and closes it as soon as it is on screen: it comes back undisposed, as a caller holds it.</summary>
    Private Shared Sub ShowAndClose(form As Form, closing As DialogResult)
        form.Opacity = 0
        form.ShowInTaskbar = False
        AddHandler form.Shown, Sub(sender As Object, e As EventArgs) form.DialogResult = closing
        form.ShowDialog()
    End Sub

    <Fact>
    Public Sub No_dialog_field_keeps_a_password_after_it_closes()
        ExchangeTestSupport.OnUi(
            Sub()
                Using form As New Remote_Admin_Account_Form(Remote_Admin_Account_Form.DialogMode.Add, "", "member")
                    form.LoginControl.Text = "bob"
                    form.PasswordControl.Text = NewPassword
                    form.RevealControl.Checked = True
                    Assert.True(form.TryAccept())
                    ShowAndClose(form, DialogResult.OK)
                    Assert.Equal("", form.PasswordControl.Text)
                    Assert.False(form.RevealControl.Checked)
                    Assert.Equal(NewPassword, form.PasswordValue)
                    form.ForgetPassword()
                    Assert.Equal("", form.PasswordValue)
                End Using
                Using form As New Remote_Admin_Account_Form(Remote_Admin_Account_Form.DialogMode.Password, "alice", "member")
                    form.PasswordControl.Text = NewPassword
                    ShowAndClose(form, DialogResult.Cancel)
                    Assert.Equal("", form.PasswordControl.Text)
                End Using
                Using form As New Remote_Admin_Shared_Form("member")
                    form.PasswordControl.Text = NewPassword
                    Assert.True(form.TryAccept())
                    ShowAndClose(form, DialogResult.OK)
                    Assert.Equal("", form.PasswordControl.Text)
                    form.ForgetPassword()
                    Assert.Equal("", form.PasswordValue)
                End Using
                Using form As Remote_Admin_Invitation_Form = Remote_Admin_Invitation_Form.ForCreated("CODE-ONE", "FMSEXCH1:test", "member")
                    ShowAndClose(form, DialogResult.OK)
                    Assert.Equal("", form.CodeBox.Text)
                    Assert.Equal("", form.ConnectionStringBox.Text)
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub The_copy_goes_through_the_guarded_clipboard_and_nothing_here_copies_a_password()
        Dim code As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource(IO.Path.Combine("Forms", "Remote_Admin_Invitation_Form.vb")))
        Assert.Contains("SecureClipboard.SetSecret", code)
        For Each name As String In {"Remote_Admin_Invitation_Form.vb", "Remote_Admin_Account_Form.vb", "Remote_Admin_Shared_Form.vb", "Remote_Admin_Limits_Form.vb",
                                    "Remote_Admin_Target_Form.vb", "Remote_Admin_Confirm_Form.vb", "Share_Settings_Form.RemoteExchange.vb", "Share_Settings_Form.RemoteExchangeActions.vb",
                                    "Share_Settings_Form.RemoteExchangeCards.vb", "Share_Settings_Form.RemoteExchangeCardsAccounts.vb", "Share_Settings_Form.RemoteExchangeCardsOther.vb"}
            Dim text As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource(IO.Path.Combine("Forms", name)))
            Assert.DoesNotContain("Clipboard.SetText", text)
            Assert.DoesNotContain("Clipboard.SetDataObject", text)
            If name <> "Remote_Admin_Invitation_Form.vb" Then Assert.DoesNotContain("SecureClipboard", text)
        Next
    End Sub

    <Fact>
    Public Sub Every_input_has_an_accessible_name_and_every_dialog_has_its_help_badge_room()
        Run(Sub(rig As Rig)
                rig.AddTarget()
                rig.Open()
                rig.PickTarget()
                Assert.NotNull(ContextHelpUi.AttachedButton(rig.Window))
                For Each c As Control In Descendants(rig.Field(Of TableLayoutPanel)("_remoteTop"))
                    AssertNamed("the top of the window", c)
                Next
                Dim dialogs As New List(Of Form) From {
                    New Remote_Admin_Target_Form(rig.Targets),
                    New Remote_Admin_Account_Form(Remote_Admin_Account_Form.DialogMode.Add, "", "member"),
                    New Remote_Admin_Limits_Form(Remote_Admin_Limits_Form.DialogMode.Policy, "", RemoteAdminLimits.Empty, Nothing),
                    New Remote_Admin_Shared_Form("member"),
                    Remote_Admin_Invitation_Form.ForCreate(),
                    Remote_Admin_Invitation_Form.ForCreated("CODE-ONE", "FMSEXCH1:test", "member"),
                    New Remote_Admin_Confirm_Form(RemoteAdminActions.AccountRemoveQuestion(rig.Controller, "alice"))}
                For Each form As Form In dialogs
                    Using form
                        form.Opacity = 0
                        form.ShowInTaskbar = False
                        form.Show()
                        Application.DoEvents()
                        Assert.True(ContextHelpUi.AttachedButton(form) IsNot Nothing, form.GetType().Name & " has no help badge")
                        For Each c As Control In Descendants(form)
                            AssertNamed(form.GetType().Name, c)
                        Next
                    End Using
                Next
            End Sub)
    End Sub

    Private Shared Sub AssertNamed(where As String, c As Control)
        If TypeOf c Is TextBox OrElse TypeOf c Is ComboBox OrElse TypeOf c Is ListView Then
            Assert.False(String.IsNullOrWhiteSpace(c.AccessibleName), where & ": " & c.GetType().Name & " " & c.Name & " has no accessible name")
        End If
    End Sub

    Private Shared Function Descendants(parent As Control) As List(Of Control)
        Dim found As New List(Of Control)()
        For Each child As Control In parent.Controls
            found.Add(child)
            found.AddRange(Descendants(child))
        Next
        Return found
    End Function

    ' ------------------------------------------------------------------------------------------------ T-8: leaving the session

    Private Shared Function SignedInWindow(rig As Rig) As RemoteAdminCardBase
        Return rig.SignedInOn("status", New Script().Op(ServerStatus, "{""accounts"":2}").Ending(), StepsOf(1))
    End Function

    Private Shared Sub AssertEndedWith(rig As Rig, cause As String)
        Pump(Function() rig.Controller.State.Phase = RemoteAdminPhase.SignedOut, "the session ended by " & cause)
        Assert.True(RemoteAdminController.WaitForEndings(TimeSpan.FromSeconds(3)), "the goodbye did not finish")
        Assert.Contains(cause, rig.Controller.State.ReasonLine)
        rig.Handshake.Verify()
    End Sub

    <Theory>
    <InlineData(SessionSwitchReason.SessionLock, "lock")>
    <InlineData(SessionSwitchReason.SessionLogoff, "windows")>
    <InlineData(SessionSwitchReason.ConsoleDisconnect, "windows")>
    <InlineData(SessionSwitchReason.RemoteDisconnect, "windows")>
    Public Sub A_session_switch_ends_the_session_and_says_why(reason As SessionSwitchReason, which As String)
        Run(Sub(rig As Rig)
                SignedInWindow(rig)
                rig.Window.OnRemoteSessionSwitch(Nothing, New SessionSwitchEventArgs(reason))
                AssertEndedWith(rig, If(which = "lock", RemoteAdminWords.CauseScreenLocked(), RemoteAdminWords.CauseWindowsSession()))
            End Sub)
    End Sub

    <Fact>
    Public Sub Suspend_ends_the_session_and_says_why()
        Run(Sub(rig As Rig)
                SignedInWindow(rig)
                rig.Window.OnRemotePowerModeChanged(Nothing, New PowerModeChangedEventArgs(PowerModes.Suspend))
                AssertEndedWith(rig, RemoteAdminWords.CauseSleep())
            End Sub)
    End Sub

    <Fact>
    Public Sub Minimizing_an_unlock_and_a_resume_leave_the_session_alone()
        Run(Sub(rig As Rig)
                SignedInWindow(rig)
                rig.Window.WindowState = FormWindowState.Minimized
                rig.Window.OnRemoteSessionSwitch(Nothing, New SessionSwitchEventArgs(SessionSwitchReason.SessionUnlock))
                rig.Window.OnRemotePowerModeChanged(Nothing, New PowerModeChangedEventArgs(PowerModes.Resume))
                For i As Integer = 1 To 20
                    Application.DoEvents()
                    Thread.Sleep(1)
                Next
                Assert.Equal(RemoteAdminPhase.SignedIn, rig.Controller.State.Phase)
                rig.Window.WindowState = FormWindowState.Normal
                rig.SignOutAndVerify()
            End Sub)
    End Sub

    <Fact>
    Public Sub Choosing_another_target_ends_the_old_session_first()
        Run(Sub(rig As Rig)
                rig.AddTarget()
                rig.AddTarget("Home", "home.test:22022")
                rig.Open()
                rig.PickTarget()
                Dim first As String = rig.Controller.State.Target.Id
                ExchangeTestSupport.ShowSettingsCardOf(rig.Window, rig.Card("status"))
                Dim stream As LockStepStream = New Script().Op(ServerStatus, "{""accounts"":2}").Ending()
                rig.SignIn(stream)
                Played(stream, StepsOf(1), "the status read")
                rig.Settled(rig.Card("status"))
                Dim rows As Integer = rig.Card("status").RowCount
                Assert.NotEqual(0, rows)

                Dim list As ComboBox = rig.Field(Of ComboBox)("_remoteTargetList")
                Assert.True(list.Enabled)
                list.SelectedIndex = If(list.SelectedIndex = 0, 1, 0)
                Pump(Function() rig.Controller.State.Target.Id <> first AndAlso rig.Controller.State.Phase = RemoteAdminPhase.SignedOut, "the target switch")
                Assert.True(RemoteAdminController.WaitForEndings(TimeSpan.FromSeconds(3)), "the goodbye did not finish")
                Assert.True(stream.ClientClosed, "the old session must be closed")
                Assert.Equal(0, rig.Card("status").RowCount)
                rig.Handshake.Verify()
            End Sub)
    End Sub

    <Fact>
    Public Sub Closing_the_window_says_goodbye_detaches_every_handler_and_empties_the_field()
        Run(Sub(rig As Rig)
                Dim before As Integer = Share_Settings_Form.RemoteAttachedHandlerCount
                Dim stream As LockStepStream = New Script().Op(ServerStatus, "{""accounts"":2}").Ending()
                rig.SignedInOn("status", stream, StepsOf(1))
                Assert.Equal(before + 2, Share_Settings_Form.RemoteAttachedHandlerCount)
                rig.Window.Close()
                Assert.Equal(before, Share_Settings_Form.RemoteAttachedHandlerCount)
                Assert.True(RemoteAdminController.WaitForEndings(TimeSpan.FromSeconds(3)), "the goodbye did not finish")
                Assert.True(stream.ClientClosed)
                rig.Handshake.Verify()
            End Sub)
    End Sub

    <Fact>
    Public Sub The_exit_closes_every_remote_window_and_waits_for_the_goodbye()
        Run(Sub(rig As Rig)
                Dim before As Integer = Share_Settings_Form.RemoteAttachedHandlerCount
                ' The goodbye is sent and never answered, so only the limit of the wait lets the exit go: an exit that did not wait returns at once.
                Dim stream As LockStepStream = New Script().Op(ServerStatus, "{""accounts"":2}").ByeNeverAnswered()
                rig.SignedInOn("status", stream, StepsOf(1))
                Dim watch As Stopwatch = Stopwatch.StartNew()
                Share_Settings_Form.CloseRemoteWindowsForExit()
                watch.Stop()
                Assert.True(rig.Window.IsDisposed, "the exit must close the window")
                Assert.Equal(before, Share_Settings_Form.RemoteAttachedHandlerCount)
                Assert.True(watch.ElapsedMilliseconds >= 1000, "the exit must wait for a goodbye on its way")
                Assert.True(watch.ElapsedMilliseconds < 3000, "the exit must stop waiting at its limit")
                Assert.Equal(1, rig.Time.Fire(ExchangeRemoteAdminSession.ByeBound))
                Assert.True(RemoteAdminController.WaitForEndings(TimeSpan.FromSeconds(3)), "the bound must end the goodbye")
                Assert.True(stream.ClientClosed, "the goodbye must close the stream")
                rig.Handshake.Verify()
            End Sub)
    End Sub

    <Fact>
    Public Sub With_no_remote_window_the_exit_returns_at_once()
        ExchangeTestSupport.OnUi(
            Sub()
                Dim watch As Stopwatch = Stopwatch.StartNew()
                Share_Settings_Form.CloseRemoteWindowsForExit()
                Assert.True(watch.ElapsedMilliseconds < 200, "the exit waited with nothing to wait for")
            End Sub)
    End Sub

    ' The handler count is checked exactly after every cycle, so one leaked handler fails the first cycle; ten cycles keep
    ' the run (about 0.8 s per settings window) well inside the 30 s limit of OnUi, also on a loaded machine.
    Private Const LeakCycles As Integer = 10

    <Fact>
    Public Sub Repeated_openings_and_closings_leave_no_handler_behind()
        Run(Sub(rig As Rig)
                Dim before As Integer = Share_Settings_Form.RemoteAttachedHandlerCount
                For i As Integer = 1 To LeakCycles
                    rig.Open()
                    Assert.Equal(before + 2, Share_Settings_Form.RemoteAttachedHandlerCount)
                    rig.Window.Close()
                    rig.Window.Dispose()
                    Assert.Equal(before, Share_Settings_Form.RemoteAttachedHandlerCount)
                Next
            End Sub)
    End Sub

    ' ------------------------------------------------------------------------------------------------ the gate

    <Fact>
    Public Sub With_the_gate_open_the_button_of_the_account_page_opens_one_window_and_it_touches_nothing()
        Run(Sub(rig As Rig)
                Using main As New MainWindow(Nothing)
                    main.OpenSettings(SettingsDestination.Manager, "exchange-account")
                    Dim windows As Dictionary(Of SettingsDestination, Share_Settings_Form) = FieldOf(Of Dictionary(Of SettingsDestination, Share_Settings_Form))(main, "_settingsWindows")
                    Dim manager As Share_Settings_Form = windows(SettingsDestination.Manager)
                    manager.ConfirmDraftClose = Function(isExchange) DialogResult.No
                    Dim button As Button = FieldOf(Of Button)(manager, "_btnAccRemoteAdmin")
                    Assert.NotNull(button)
                    ExchangeTestSupport.ShowSettingsCardOf(manager, button)
                    button.PerformClick()

                    Dim remote As Share_Settings_Form = windows(SettingsDestination.RemoteExchange)
                    remote.ConfirmDraftClose = Function(isExchange) DialogResult.No
                    Assert.True(remote.Visible)
                    Assert.False(remote.Modal)
                    Assert.NotNull(ContextHelpUi.AttachedButton(remote))
                    Assert.Equal(RemoteAdminPhase.NoTarget, rig.Controller.State.Phase)
                    Assert.Empty(rig.Handshake.Calls())
                    Assert.Equal(0, rig.Port.Writes)

                    button.PerformClick()
                    Assert.Same(remote, windows(SettingsDestination.RemoteExchange))
                    remote.Close()
                    manager.Close()
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub With_the_gate_shut_the_account_page_has_no_button_and_nothing_was_written()
        Run(Sub(rig As Rig)
                Assert.False(ExchangeRemoteAdminFeatures.IsEnabled())
                Using window As Share_Settings_Form = ManagerWindow()
                    Assert.Null(FieldOf(Of Button)(window, "_btnAccRemoteAdmin"))
                    window.Close()
                End Using
                Assert.Equal(0, rig.Port.Writes)
                Assert.Equal(0, rig.ConfirmPort.Writes)
                Assert.Empty(rig.Handshake.Calls())
            End Sub, gateOpen:=False)
    End Sub

    Private Shared Function ManagerWindow() As Share_Settings_Form
        ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
        Dim window As New Share_Settings_Form(destination:=SettingsDestination.Manager)
        window.ConfirmDraftClose = Function(isExchange) DialogResult.No
        ' Lazy page loaders reach for the service manager and the network; nothing here needs them.
        GetType(Share_Settings_Form).GetField("_exLoaded", Flags).SetValue(window, True)
        GetType(Share_Settings_Form).GetField("_hostingLoaded", Flags).SetValue(window, True)
        window.Opacity = 0
        window.ShowInTaskbar = False
        Return window
    End Function

End Class
