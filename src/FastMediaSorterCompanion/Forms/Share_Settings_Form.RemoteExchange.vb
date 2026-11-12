Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Microsoft.Win32

' Ticket L0278 T-6 shell and T-8 (specification 5.6, 5.6.1, 5.8): the third settings window of the Share Manager, the administration of a
' Media Exchange Server on another PC. The window reads RemoteAdminController.State and repaints on StateChanged; it keeps no state of the
' sign-in of its own and no field of the target type (7.6, one state object). The rail holds the six cards; above it sit the header, the
' banner band and the connection block.

Partial Public NotInheritable Class Share_Settings_Form

    ''' <summary>Builds the controller of a remote administration window. A fact replaces it with a scripted one.</summary>
    Friend Shared RemoteControllerFactory As Func(Of RemoteAdminController) = Function() RemoteAdminController.CreateDefault()

    ' The exit waits this long for the goodbyes of the windows it closed: the session's own bound for `bye` (5.8).
    Private Shared ReadOnly RemoteExitWait As TimeSpan = TimeSpan.FromMilliseconds(1500)

    Private Shared _remoteAttachedHandlers As Integer

    ''' <summary>Test-only: how many SystemEvents handlers the open remote windows hold (two per window: the session switch and the power mode).</summary>
    Friend Shared ReadOnly Property RemoteAttachedHandlerCount As Integer
        Get
            Return Volatile.Read(_remoteAttachedHandlers)
        End Get
    End Property

    Private _secRemote As CollapsibleSection
    Private _remote As RemoteAdminController
    Private _remoteConfirm As RemoteAdminConfirm
    Private _remoteCards As IReadOnlyList(Of RemoteAdminCard)
    Private _remoteEventsAttached As Boolean
    Private _remoteFillingTargets As Boolean

    Private _remoteTop As TableLayoutPanel
    Private _remoteBanner As Label
    Private _remoteTargetList As ComboBox
    Private _remoteAdd As Button
    Private _remoteEdit As Button
    Private _remotePassword As TextBox
    Private _remoteShow As CheckBox
    Private _remoteSignIn As Button
    Private _remoteSignInHint As Label
    Private _remoteCancel As Button
    Private _remoteSignOut As Button
    Private _remoteAskAgain As Button
    Private _remoteTrust As FlowLayoutPanel
    Private _remoteTrustNote As Label
    Private _remoteFingerprint As TextBox
    Private _remoteTrustYes As Button
    Private _remoteTrustNo As Button
    Private _remoteStopNote As Label
    Private _remoteDisabled As FlowLayoutPanel
    Private _remoteDisabledNote As Label
    Private _remoteTryAgain As Button

    ''' <summary>One entry of the target list: the id of the record and its words. The window keeps no target object.</summary>
    Private NotInheritable Class RemoteTargetChoice
        Friend ReadOnly Id As String
        Friend ReadOnly Caption As String

        Friend Sub New(id As String, caption As String)
            Me.Id = id
            Me.Caption = caption
        End Sub

        Public Overrides Function ToString() As String
            Return Caption
        End Function
    End Class

    ' ---------------------------------------------------------------------------------------------- words

    Private Shared Function RemoteWindowName() As String
        Return Localization.T("Администрирование сервера обмена")
    End Function

    ''' <summary>The caption of the window: the target's name, then the name of the window; the name alone with no target (5.6.1).</summary>
    Private Function RemoteWindowCaption() As String
        Dim name As String = _remote?.State?.TargetName
        If String.IsNullOrEmpty(name) Then Return RemoteWindowName()
        Return Localization.TF("{0} - Администрирование сервера обмена", name)
    End Function

    Private Shared Sub RemoteLog(ex As Exception)
        Try
            AppFileLogger.LogStamped("remote-admin: window " & ex.GetType().Name)
        Catch
        End Try
    End Sub

    ' ---------------------------------------------------------------------------------------------- build

    ''' <summary>The page of the window: the six cards, one group each (5.6). Called from BuildUi before the pages are built.</summary>
    Private Function CreateRemoteExchangeSection() As CollapsibleSection
        Dim sec As CollapsibleSection = CreateSettingsSection("remote-exchange", RemoteWindowName())
        _remote = RemoteControllerFactory.Invoke()
        _remoteConfirm = RemoteAdminActions.ConfirmStore.Invoke()
        BuildRemoteTop()
        _remoteCards = RemoteAdminCards.CreateAll(_remote)
        For Each card As RemoteAdminCard In _remoteCards
            AddHandler card.ActionFinished, Sub(sender As Object, e As EventArgs) PaintRemoteConnection()
            sec.AddBodyRow(card)
        Next
        Return sec
    End Function

    ''' <summary>The rail entry id of the card a page row holds ("basic" for anything else).</summary>
    Private Shared Function RemoteRailId(row As Control) As String
        Dim card As RemoteAdminCard = TryCast(row, RemoteAdminCard)
        Return If(card Is Nothing, "basic", card.RailId)
    End Function

    ''' <summary>The caption of the rail entry of the card a page row holds, read again in the current language every time.</summary>
    Private Shared Function RemoteRailCaptionSource(row As Control) As Func(Of String)
        Dim card As RemoteAdminCard = TryCast(row, RemoteAdminCard)
        If card Is Nothing Then Return Function() RemoteWindowName()
        Return Function() card.RailCaption
    End Function

    Private Function RemoteButton(name As String, text As String, glyph As String, onClick As EventHandler,
                                  Optional role As ButtonGlyphRole = ButtonGlyphRole.Neutral) As Button
        Dim b As New Button With {.Name = name, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(12, 5, 12, 5), .Margin = New Padding(0, 2, 8, 2), .Text = text, .UseMnemonic = False}
        ButtonGlyphs.Apply(b, glyph, role)
        AddHandler b.Click, onClick
        Return b
    End Function

    Private Shared Function RemoteFlow(name As String) As FlowLayoutPanel
        Return New FlowLayoutPanel With {.Name = name, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.LeftToRight, .WrapContents = True, .Margin = Padding.Empty,
            .Padding = New Padding(12, 0, 12, 0), .Dock = DockStyle.Top}
    End Function

    Private Shared Function RemoteNote(name As String, Optional muted As Boolean = True) As Label
        Return New Label With {.Name = name, .AutoSize = True, .UseMnemonic = False, .Margin = New Padding(0, 6, 8, 2),
            .ForeColor = If(muted, ThemeService.Palette.TextMuted, ThemeService.Palette.TextPrimary)}
    End Function

    Private Shared Function RemoteCaptionLabel(text As String) As Label
        Return New Label With {.AutoSize = True, .UseMnemonic = False, .Margin = New Padding(0, 8, 8, 2), .Text = text}
    End Function

    ''' <summary>The top band above the rail: row 0 is the header (BuildSettingsHeader puts it there), then the banner, the connection
    ''' block, and the three inline panels that appear in their phase only (first contact, certificate stop, administration off).</summary>
    Private Sub BuildRemoteTop()
        _remoteTop = New TableLayoutPanel With {.Name = "remoteTop", .Dock = DockStyle.Top, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .ColumnCount = 1, .RowCount = 6, .Margin = Padding.Empty,
            .Padding = New Padding(0, 0, 0, 4)}
        _remoteTop.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        For i As Integer = 0 To 5
            _remoteTop.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Next

        ' the banner band: the mark, the target name, host:port and the state words, wrapped and never cut (5.6.1)
        _remoteBanner = New Label With {.Name = "remoteBanner", .AutoSize = True, .UseMnemonic = False, .Dock = DockStyle.Fill,
            .Margin = New Padding(12, 2, 12, 4), .Font = New Font(Me.Font, FontStyle.Bold), .AccessibleRole = AccessibleRole.StaticText}
        AddHandler _remoteBanner.Disposed, Sub() _remoteBanner.Font.Dispose()
        ExchangeMark.Apply(_remoteBanner, 24, AddressOf CompanionAssets.OpenBundledAsset)
        _remoteTop.Controls.Add(_remoteBanner, 0, 1)

        ' the connection block
        Dim connection As New FlowLayoutPanel With {.Name = "remoteConnection", .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Dock = DockStyle.Top, .Margin = Padding.Empty}
        Dim pickRow As FlowLayoutPanel = RemoteFlow("remotePickRow")
        pickRow.Controls.Add(RemoteCaptionLabel(Localization.T("Сервер:")))
        _remoteTargetList = New ComboBox With {.Name = "remoteTargetList", .DropDownStyle = ComboBoxStyle.DropDownList, .Width = 360,
            .Margin = New Padding(0, 4, 8, 2), .IntegralHeight = False, .AccessibleName = Localization.T("Сервер:").TrimEnd(":"c)}
        AddHandler _remoteTargetList.SelectedIndexChanged, AddressOf OnRemoteTargetPicked
        pickRow.Controls.Add(_remoteTargetList)
        _remoteAdd = RemoteButton("remoteAdd", Localization.T("Добавить сервер.."), "action.add", AddressOf OnRemoteAddClicked)
        _remoteEdit = RemoteButton("remoteEdit", Localization.T("Изменить сервер.."), "action.edit", AddressOf OnRemoteEditClicked)
        pickRow.Controls.Add(_remoteAdd)
        pickRow.Controls.Add(_remoteEdit)
        connection.Controls.Add(pickRow)

        Dim passRow As FlowLayoutPanel = RemoteFlow("remotePasswordRow")
        passRow.Controls.Add(RemoteCaptionLabel(Localization.T("Пароль администратора:")))
        _remotePassword = New TextBox With {.Name = "remotePassword", .Width = 260, .UseSystemPasswordChar = True, .Margin = New Padding(0, 4, 8, 2),
            .AccessibleName = Localization.T("Пароль администратора:").TrimEnd(":"c)}
        AddHandler _remotePassword.TextChanged, Sub(sender As Object, e As EventArgs) PaintRemoteConnection()
        passRow.Controls.Add(_remotePassword)
        _remoteShow = New CheckBox With {.Name = "remoteShowPassword", .AutoSize = True, .Text = Localization.T("Показать пароль")}
        AddHandler _remoteShow.CheckedChanged, Sub(sender As Object, e As EventArgs) _remotePassword.UseSystemPasswordChar = Not _remoteShow.Checked
        passRow.Controls.Add(OptionRows.InFlow(_remoteShow, Nothing, OptionRowFit.Content))
        connection.Controls.Add(passRow)

        Dim actionRow As FlowLayoutPanel = RemoteFlow("remoteActionRow")
        _remoteSignIn = RemoteButton("remoteSignIn", Localization.T("Войти"), "action.connect-device", AddressOf OnRemoteSignInClicked)
        _remoteSignInHint = RemoteNote("remoteSignInHint")
        _remoteCancel = RemoteButton("remoteCancel", Localization.T("Отмена"), "action.cancel", AddressOf OnRemoteCancelClicked, ButtonGlyphRole.Dismissing)
        _remoteSignOut = RemoteButton("remoteSignOut", Localization.T("Выйти"), "system.network-off", AddressOf OnRemoteSignOutClicked)
        _remoteAskAgain = RemoteButton("remoteAskAgain", Localization.T("Спрашивать снова перед каждым удалением"), "action.refresh", AddressOf OnRemoteAskAgainClicked)
        actionRow.Controls.Add(_remoteSignIn)
        actionRow.Controls.Add(_remoteSignInHint)
        actionRow.Controls.Add(_remoteCancel)
        actionRow.Controls.Add(_remoteSignOut)
        actionRow.Controls.Add(_remoteAskAgain)
        connection.Controls.Add(actionRow)
        _remoteTop.Controls.Add(connection, 0, 2)

        ' first contact: the fingerprint in full and the two answers, No holds the default (5.4 rule 3)
        _remoteTrust = New FlowLayoutPanel With {.Name = "remoteTrust", .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Dock = DockStyle.Top, .Margin = Padding.Empty,
            .Padding = New Padding(12, 4, 12, 4), .Visible = False}
        _remoteTrustNote = RemoteNote("remoteTrustNote", False)
        _remoteTrust.Controls.Add(_remoteTrustNote)
        _remoteTrust.Controls.Add(New Label With {.AutoSize = True, .UseMnemonic = False, .Margin = New Padding(0, 2, 0, 2),
            .Text = Localization.T("Отпечаток сертификата сервера:")})
        _remoteFingerprint = New TextBox With {.Name = "remoteFingerprint", .ReadOnly = True, .Width = 560, .Margin = New Padding(0, 0, 0, 4),
            .AccessibleName = Localization.T("Отпечаток сертификата сервера:").TrimEnd(":"c)}
        _remoteTrust.Controls.Add(_remoteFingerprint)
        Dim trustButtons As FlowLayoutPanel = RemoteFlow("remoteTrustButtons")
        trustButtons.Padding = Padding.Empty
        _remoteTrustNo = RemoteButton("remoteTrustNo", Localization.T("Не доверять"), "action.cancel", AddressOf OnRemoteTrustNoClicked, ButtonGlyphRole.Dismissing)
        _remoteTrustYes = RemoteButton("remoteTrustYes", Localization.T("Доверять этому сертификату"), "action.confirm", AddressOf OnRemoteTrustYesClicked, ButtonGlyphRole.Caution)
        trustButtons.Controls.Add(_remoteTrustNo)
        trustButtons.Controls.Add(_remoteTrustYes)
        _remoteTrust.Controls.Add(trustButtons)
        _remoteTop.Controls.Add(_remoteTrust, 0, 3)

        ' administration is off on that server: the sentence and Try again
        _remoteDisabled = New FlowLayoutPanel With {.Name = "remoteDisabled", .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Dock = DockStyle.Top, .Margin = Padding.Empty,
            .Padding = New Padding(12, 4, 12, 4), .Visible = False}
        _remoteDisabledNote = RemoteNote("remoteDisabledNote", False)
        _remoteTryAgain = RemoteButton("remoteTryAgain", Localization.T("Попробовать снова"), "action.refresh", AddressOf OnRemoteSignInClicked)
        _remoteDisabled.Controls.Add(_remoteDisabledNote)
        _remoteDisabled.Controls.Add(_remoteTryAgain)
        _remoteTop.Controls.Add(_remoteDisabled, 0, 4)

        ' a stop by a changed certificate: the way out is the target dialog (5.4 rule 5)
        _remoteStopNote = RemoteNote("remoteStopNote", False)
        _remoteStopNote.Dock = DockStyle.Top
        _remoteStopNote.Margin = New Padding(12, 2, 12, 2)
        _remoteStopNote.Visible = False
        _remoteTop.Controls.Add(_remoteStopNote, 0, 5)

        AddHandler _remoteTop.SizeChanged, Sub(sender As Object, e As EventArgs) FitRemoteTop()
    End Sub

    ''' <summary>The wrapping labels of the band take the width of the window, so a long sentence wraps and is never cut.</summary>
    Private Sub FitRemoteTop()
        If _remoteTop Is Nothing Then Return
        Dim width As Integer = Math.Max(1, _remoteTop.ClientSize.Width)
        _remoteBanner.MaximumSize = New Size(Math.Max(1, width - _remoteBanner.Margin.Horizontal), 0)
        Dim inner As Integer = Math.Max(1, width - _remoteTrust.Padding.Horizontal)
        _remoteTrustNote.MaximumSize = New Size(inner, 0)
        _remoteDisabledNote.MaximumSize = New Size(inner, 0)
        _remoteStopNote.MaximumSize = New Size(Math.Max(1, width - _remoteStopNote.Margin.Horizontal), 0)
        _remoteSignInHint.MaximumSize = New Size(Math.Max(1, width \ 2), 0)
        _remoteFingerprint.Width = Math.Max(120, Math.Min(inner, LogicalToDeviceUnits(560)))
    End Sub

    ''' <summary>Puts the top band above the rail at form level: the header, the banner and the connection block, the rail below (5.6).</summary>
    Private Sub AddRemoteTopToForm()
        If _remoteTop Is Nothing Then Return
        Me.Controls.Add(_remoteTop)
        _root.BringToFront()
    End Sub

    ''' <summary>The one link a remote window shows reaches the Manager window.</summary>
    Private Function RemoteDestinationLinkTargets() As SettingsDestination()
        If Destination = SettingsDestination.RemoteExchange Then Return {SettingsDestination.Manager}
        Return {SettingsDestination.Manager, SettingsDestination.ExchangeServer}
    End Function

    ''' <summary>The first paint and the subscriptions, once every control exists (T-8: the session events are attached when the window opens).</summary>
    Private Sub StartRemoteWindow()
        If _remote Is Nothing Then Return
        LoadRemoteTargets()
        AddHandler _remote.StateChanged, AddressOf OnRemoteStateChanged
        AttachRemoteSessionEvents()
        PaintRemote()
    End Sub

    Private Sub ApplyRemoteTheme(palette As ThemePalette)
        If _remoteBanner Is Nothing Then Return
        _remoteBanner.BackColor = palette.SurfaceRaised
        PaintRemoteBannerInk(palette)
        _remoteSignInHint.ForeColor = palette.TextMuted
        _remoteTrustNote.ForeColor = palette.TextPrimary
        _remoteDisabledNote.ForeColor = palette.TextPrimary
        _remoteStopNote.ForeColor = palette.ColorOf(ThemeRole.Warning)
        If _remoteCards Is Nothing Then Return
        For Each card As RemoteAdminCard In _remoteCards
            card.ApplyThemeInks()
        Next
    End Sub

    Private Sub PaintRemoteBannerInk(palette As ThemePalette)
        Dim role As ThemeRole = If(RemoteAdminBanner.Ink(_remote.State.Phase) = RemoteAdminInk.Warning, ThemeRole.Warning, ThemeRole.Info)
        _remoteBanner.ForeColor = palette.ColorOf(role)
    End Sub

    ' ---------------------------------------------------------------------------------------------- paint

    Private Sub OnRemoteStateChanged(sender As Object, e As EventArgs)
        If IsDisposed Then Return
        If InvokeRequired Then
            Try
                BeginInvoke(New Action(AddressOf PaintRemote))
            Catch ex As InvalidOperationException
                RemoteLog(ex)
            End Try
            Return
        End If
        PaintRemote()
    End Sub

    ''' <summary>Everything the window shows, from the controller's state. Nothing is remembered between two paints.</summary>
    Friend Sub PaintRemote()
        If IsDisposed OrElse _remote Is Nothing OrElse _remoteBanner Is Nothing Then Return
        Dim state As RemoteAdminState = _remote.State
        Text = DestinationCaption()
        _remoteBanner.Text = RemoteAdminBanner.Text(state)
        PaintRemoteBannerInk(ThemeService.Palette)

        ' the password never outlives the phase that reads it; the reveal is off again at every sign-in and lapse
        If Not state.CanSignIn Then
            If _remoteShow.Checked Then _remoteShow.Checked = False
            If state.Phase = RemoteAdminPhase.SignedIn AndAlso _remotePassword.TextLength > 0 Then _remotePassword.Clear()
        End If
        PaintRemoteConnection()

        _remoteTrust.Visible = state.ShowsTrustPanel
        Dim asked As ExchangeRemoteAdminSignIn = _remote.AskedTrust
        If state.ShowsTrustPanel AndAlso asked IsNot Nothing Then
            _remoteTrustNote.Text = Localization.TF("Сертификат сервера «{0}» ({1}) выдан не доверенным центром сертификации. Доверять этому сертификату?",
                                                    state.TargetName, Localization.LtrValue(state.TargetAddress))
            _remoteFingerprint.Text = asked.Fingerprint
            AcceptButton = _remoteTrustNo
            If Not _remoteTrustNo.Focused AndAlso _remoteTrust.CanFocus Then _remoteTrustNo.Focus()
        Else
            _remoteFingerprint.Text = ""
            AcceptButton = Nothing
        End If

        _remoteDisabled.Visible = state.ShowsDisabledCard
        _remoteDisabledNote.Text = state.ReasonLine
        _remoteTryAgain.Enabled = state.CanSignIn AndAlso Not _remote.SignInHeld AndAlso _remotePassword.TextLength > 0

        Dim stopped As Boolean = _remote.StoppedByCertificate IsNot Nothing
        _remoteStopNote.Visible = stopped
        If stopped Then _remoteStopNote.Text = Localization.T("Сертификат сервера изменился, войти нельзя. Откройте «Изменить сервер..» и выберите «Забыть принятый сертификат».")

        For Each card As RemoteAdminCard In _remoteCards
            card.ApplyState()
        Next
        FitRemoteTop()
    End Sub

    ''' <summary>The enabled state of the connection block (5.6, "Controls"): a pure function of the state, the hold after a rate limit
    ''' and whether the field holds anything.</summary>
    Private Sub PaintRemoteConnection()
        If _remote Is Nothing OrElse _remoteSignIn Is Nothing Then Return
        Dim state As RemoteAdminState = _remote.State
        Dim typed As Boolean = _remotePassword.TextLength > 0
        _remoteTargetList.Enabled = state.CanChooseTarget
        _remoteAdd.Enabled = state.CanChooseTarget
        _remoteEdit.Enabled = state.CanEditTarget
        _remotePassword.Enabled = state.CanSignIn
        _remoteShow.Enabled = state.CanSignIn
        _remoteSignIn.Enabled = state.CanSignIn AndAlso Not _remote.SignInHeld AndAlso typed
        _remoteTryAgain.Enabled = state.CanSignIn AndAlso Not _remote.SignInHeld AndAlso typed
        _remoteCancel.Enabled = state.CanCancel
        _remoteSignOut.Enabled = state.CanSignOut
        Dim emptyOnly As Boolean = state.CanSignIn AndAlso Not _remote.SignInHeld AndAlso Not typed
        _remoteSignInHint.Text = If(emptyOnly, Localization.T("Введите пароль администратора, чтобы войти."), "")
        _remoteSignInHint.Visible = emptyOnly
        Dim anyStored As Boolean = False
        Try
            anyStored = _remoteConfirm IsNot Nothing AndAlso _remoteConfirm.AnySkipped()
        Catch ex As Exception
            RemoteLog(ex)
        End Try
        _remoteAskAgain.Enabled = anyStored
        SyncRemoteTargetSelection(state)
    End Sub

    ''' <summary>The list shows the controller's target: matched by id, never held.</summary>
    Private Sub SyncRemoteTargetSelection(state As RemoteAdminState)
        Dim wanted As Integer = -1
        If state.Target IsNot Nothing Then
            For i As Integer = 0 To _remoteTargetList.Items.Count - 1
                Dim choice As RemoteTargetChoice = TryCast(_remoteTargetList.Items(i), RemoteTargetChoice)
                If choice IsNot Nothing AndAlso String.Equals(choice.Id, state.Target.Id, StringComparison.Ordinal) Then
                    wanted = i
                    Exit For
                End If
            Next
        End If
        If _remoteTargetList.SelectedIndex = wanted Then Return
        _remoteFillingTargets = True
        Try
            _remoteTargetList.SelectedIndex = wanted
        Finally
            _remoteFillingTargets = False
        End Try
    End Sub

    ''' <summary>Fills the list from the store, the most recently used first. A store read, never a network call.</summary>
    Private Sub LoadRemoteTargets()
        _remoteFillingTargets = True
        Try
            _remoteTargetList.Items.Clear()
            For Each target As ExchangeAdminTarget In _remote.Targets.LoadByRecentUse()
                _remoteTargetList.Items.Add(New RemoteTargetChoice(target.Id,
                    Localization.TF("{0} ({1})", target.Name, Localization.LtrValue(target.Address))))
            Next
        Finally
            _remoteFillingTargets = False
        End Try
        SyncRemoteTargetSelection(_remote.State)
    End Sub

    Private Sub RefreshRemoteLanguage()
        If _remote Is Nothing OrElse _remoteBanner Is Nothing Then Return
        _remoteAdd.Text = Localization.T("Добавить сервер..")
        _remoteEdit.Text = Localization.T("Изменить сервер..")
        _remoteShow.Text = Localization.T("Показать пароль")
        _remoteSignIn.Text = Localization.T("Войти")
        _remoteCancel.Text = Localization.T("Отмена")
        _remoteSignOut.Text = Localization.T("Выйти")
        _remoteAskAgain.Text = Localization.T("Спрашивать снова перед каждым удалением")
        _remoteTrustNo.Text = Localization.T("Не доверять")
        _remoteTrustYes.Text = Localization.T("Доверять этому сертификату")
        _remoteTryAgain.Text = Localization.T("Попробовать снова")
        LoadRemoteTargets()
        PaintRemote()
    End Sub

    ' ---------------------------------------------------------------------------------------------- the clicks

    Private Async Sub OnRemoteTargetPicked(sender As Object, e As EventArgs)
        If _remoteFillingTargets OrElse _remote Is Nothing Then Return
        Dim choice As RemoteTargetChoice = TryCast(_remoteTargetList.SelectedItem, RemoteTargetChoice)
        If choice Is Nothing Then Return
        Try
            Await _remote.ChooseTargetAsync(_remote.Targets.Find(choice.Id))
        Catch ex As Exception
            RemoteLog(ex)
        End Try
    End Sub

    ''' <summary>The password read: the field into a new array and the field emptied at once, so nothing else holds the text (5.8).</summary>
    Private Function ReadRemotePassword() As Char()
        Dim chars As Char() = _remotePassword.Text.ToCharArray()
        _remotePassword.Clear()
        _remoteShow.Checked = False
        Return chars
    End Function

    Private Async Sub OnRemoteSignInClicked(sender As Object, e As EventArgs)
        If _remote Is Nothing Then Return
        Try
            Await _remote.SignInAsync(AddressOf ReadRemotePassword)
        Catch ex As Exception
            RemoteLog(ex)
        End Try
    End Sub

    Private Sub OnRemoteCancelClicked(sender As Object, e As EventArgs)
        If _remote IsNot Nothing Then _remote.CancelSignIn()
    End Sub

    Private Async Sub OnRemoteSignOutClicked(sender As Object, e As EventArgs)
        If _remote Is Nothing Then Return
        Try
            Await _remote.EndSessionAsync()
        Catch ex As Exception
            RemoteLog(ex)
        End Try
    End Sub

    Private Async Sub OnRemoteTrustYesClicked(sender As Object, e As EventArgs)
        If _remote Is Nothing Then Return
        Try
            Await _remote.AnswerTrustAsync(True)
        Catch ex As Exception
            RemoteLog(ex)
        End Try
    End Sub

    Private Async Sub OnRemoteTrustNoClicked(sender As Object, e As EventArgs)
        If _remote Is Nothing Then Return
        Try
            Await _remote.AnswerTrustAsync(False)
        Catch ex As Exception
            RemoteLog(ex)
        End Try
    End Sub

    Private Sub OnRemoteAskAgainClicked(sender As Object, e As EventArgs)
        Try
            If _remoteConfirm IsNot Nothing Then _remoteConfirm.ResetAll()
        Catch ex As Exception
            RemoteLog(ex)
        End Try
        PaintRemoteConnection()
    End Sub

    Private Async Sub OnRemoteAddClicked(sender As Object, e As EventArgs)
        If _remote Is Nothing Then Return
        Try
            Dim savedId As String = ""
            Using dialog As New Remote_Admin_Target_Form(_remote.Targets)
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                savedId = dialog.SavedId
            End Using
            LoadRemoteTargets()
            Await _remote.ChooseTargetAsync(_remote.Targets.Find(savedId))
        Catch ex As Exception
            RemoteLog(ex)
        End Try
    End Sub

    Private Async Sub OnRemoteEditClicked(sender As Object, e As EventArgs)
        If _remote Is Nothing OrElse _remote.State.Target Is Nothing Then Return
        Try
            Using dialog As New Remote_Admin_Target_Form(_remote.Targets, _remote.State.Target.Id)
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            End Using
            LoadRemoteTargets()
            Await _remote.ReloadTargetAsync()
        Catch ex As Exception
            RemoteLog(ex)
        End Try
    End Sub

    ' ---------------------------------------------------------------------------------------------- T-8: leaving the session

    Private Sub AttachRemoteSessionEvents()
        If _remoteEventsAttached Then Return
        _remoteEventsAttached = True
        AddHandler SystemEvents.SessionSwitch, AddressOf OnRemoteSessionSwitch
        AddHandler SystemEvents.PowerModeChanged, AddressOf OnRemotePowerModeChanged
        Interlocked.Add(_remoteAttachedHandlers, 2)
    End Sub

    Private Sub DetachRemoteSessionEvents()
        If Not _remoteEventsAttached Then Return
        _remoteEventsAttached = False
        RemoveHandler SystemEvents.SessionSwitch, AddressOf OnRemoteSessionSwitch
        RemoveHandler SystemEvents.PowerModeChanged, AddressOf OnRemotePowerModeChanged
        Interlocked.Add(_remoteAttachedHandlers, -2)
    End Sub

    ''' <summary>Runs on a thread of the system events: the work is marshalled to the window once it has a handle.</summary>
    Friend Sub OnRemoteSessionSwitch(sender As Object, e As SessionSwitchEventArgs)
        Select Case e.Reason
            Case SessionSwitchReason.SessionLock
                EndRemoteSessionFromEvent(RemoteAdminWords.CauseScreenLocked())
            Case SessionSwitchReason.SessionLogoff, SessionSwitchReason.ConsoleDisconnect, SessionSwitchReason.RemoteDisconnect
                EndRemoteSessionFromEvent(RemoteAdminWords.CauseWindowsSession())
        End Select
    End Sub

    Friend Sub OnRemotePowerModeChanged(sender As Object, e As PowerModeChangedEventArgs)
        If e.Mode = PowerModes.Suspend Then EndRemoteSessionFromEvent(RemoteAdminWords.CauseSleep())
    End Sub

    Private Sub EndRemoteSessionFromEvent(cause As String)
        If _remote Is Nothing OrElse IsDisposed OrElse Not IsHandleCreated Then Return
        Dim reason As String = RemoteAdminWords.SessionEnded(cause)
        Try
            BeginInvoke(New Action(Sub() EndRemoteSessionNow(reason)))
        Catch ex As InvalidOperationException
            RemoteLog(ex)
        End Try
    End Sub

    Private Async Sub EndRemoteSessionNow(reason As String)
        If IsDisposed OrElse _remote Is Nothing Then Return
        Try
            Await _remote.EndSessionAsync(reason)
        Catch ex As Exception
            RemoteLog(ex)
        End Try
    End Sub

    ''' <summary>The application exits (5.8): every open remote window closes while the message loop still runs, which starts its `bye`, and
    ''' the exit waits for those goodbyes under their bound. With no such window, which is every run with the gate shut, it returns at once.</summary>
    Friend Shared Sub CloseRemoteWindowsForExit()
        Dim windows As List(Of Share_Settings_Form) = Application.OpenForms.OfType(Of Share_Settings_Form)().
            Where(Function(window As Share_Settings_Form) window._remote IsNot Nothing).ToList()
        For Each window As Share_Settings_Form In windows
            Try
                window.Close()
            Catch ex As Exception
                RemoteLog(ex)
            End Try
        Next
        RemoteAdminController.WaitForEndings(RemoteExitWait)
    End Sub

    ''' <summary>Closing the window ends what the window held: the events are detached, the field is emptied and the controller is
    ''' disposed (its `bye` runs on in the background). Safe twice, from FormClosed and from Disposed.</summary>
    Private Sub ReleaseRemoteResources()
        DetachRemoteSessionEvents()
        Dim controller As RemoteAdminController = _remote
        If controller Is Nothing Then Return
        Try
            RemoveHandler controller.StateChanged, AddressOf OnRemoteStateChanged
            If _remotePassword IsNot Nothing AndAlso Not _remotePassword.IsDisposed Then _remotePassword.Clear()
            controller.Dispose()
        Catch ex As Exception
            RemoteLog(ex)
        End Try
    End Sub

End Class
