Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>What one action of a card of the remote window came to (ticket L0278 5.6.2, 5.6.3).</summary>
Friend NotInheritable Class RemoteAdminActionResult

    Public Shared ReadOnly Declined As New RemoteAdminActionResult(False, "")

    ''' <summary>True when the server changed, so the card re-reads its list.</summary>
    Public ReadOnly Property Changed As Boolean

    ''' <summary>The sentence the card shows beside its buttons ("" when the person declined: nothing to say).</summary>
    Public ReadOnly Property Line As String

    Public Sub New(changed As Boolean, line As String)
        Me.Changed = changed
        Me.Line = If(line, "")
    End Sub
End Class

''' <summary>One confirmation of the three destructive actions (5.6.3): the composed text that names the server, the object and the
''' consequence, the confirm caption, and the answers. The safe answer always holds the default: <see cref="DefaultIsSafe"/> is True
''' for every question and has no setter. <see cref="DoNotAskAgain"/> is the box the asker ticked (off by default).</summary>
Friend NotInheritable Class RemoteAdminConfirmQuestion

    Public ReadOnly Property Kind As RemoteAdminConfirmKind
    Public ReadOnly Property Caption As String
    ''' <summary>The server's target name and host:port, the object, then the consequence, one per line.</summary>
    Public ReadOnly Property Text As String
    ''' <summary>The caption of the acting button.</summary>
    Public ReadOnly Property ConfirmCaption As String
    ''' <summary>True: the safe answer (decline) is the default button, the Enter answer and the Escape answer.</summary>
    Public ReadOnly Property DefaultIsSafe As Boolean
    ''' <summary>The "do not ask again" box as the asker left it; only a confirmed answer with it ticked is stored.</summary>
    Public Property DoNotAskAgain As Boolean

    Public Sub New(kind As RemoteAdminConfirmKind, caption As String, text As String, confirmCaption As String)
        Me.Kind = kind
        Me.Caption = caption
        Me.Text = text
        Me.ConfirmCaption = confirmCaption
        DefaultIsSafe = True
    End Sub
End Class

''' <summary>Pieces the remote dialogs share: the window skeleton, the answer row and the masked password block. No sentence is
''' sent from here and nothing is kept: a block holds the typed text only in its control.</summary>
Friend NotInheritable Class RemoteAdminFormKit

    Private Sub New()
    End Sub

    Friend Const ContentWidth As Integer = 420

    Friend Shared Function NewButton(text As String, glyph As String, name As String) As Button
        Dim b As New Button With {.Name = name, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(12, 4, 12, 4), .Margin = New Padding(0, 4, 8, 4), .Text = text}
        If glyph.Length > 0 Then ButtonGlyphs.Apply(b, glyph)
        Return b
    End Function

    Friend Shared Function Note(text As String, Optional muted As Boolean = True) As Label
        Return New Label With {.AutoSize = True, .MaximumSize = New Drawing.Size(ContentWidth, 0), .Margin = New Padding(0, 0, 0, 6),
            .ForeColor = If(muted, ThemeService.Palette.TextMuted, ThemeService.Palette.TextPrimary), .Text = text}
    End Function

    ''' <summary>A reason beside a field: hidden until it has a sentence.</summary>
    Friend Shared Function Reason() As Label
        Return New Label With {.AutoSize = True, .MaximumSize = New Drawing.Size(ContentWidth, 0), .Margin = New Padding(0, 2, 0, 4),
            .ForeColor = CollapsibleSection.AttentionColor, .Visible = False}
    End Function

    Friend Shared Sub SetReason(label As Label, text As String)
        label.Text = If(text, "")
        label.Visible = label.Text.Length > 0
    End Sub

    ''' <summary>The skeleton: caption, icon, a fixed dialog, the heading with the exchange mark. The form has set its direction already.</summary>
    Friend Shared Function Begin(form As Form, title As String) As TableLayoutPanel
        form.Text = title
        Dim handle As IntPtr
        form.Icon = ShareIcons.CreateIcon(handle)
        AddHandler form.Disposed, Sub() ShareIcons.FreeIcon(form.Icon, handle)
        form.FormBorderStyle = FormBorderStyle.FixedDialog
        form.MaximizeBox = False
        form.MinimizeBox = False
        form.ShowInTaskbar = False
        form.StartPosition = FormStartPosition.CenterParent
        Dim body As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(14, 12, 14, 6)}
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        Dim heading As Label = ExchangeMark.Heading(form.Text, 32, AddressOf CompanionAssets.OpenBundledAsset)
        heading.MaximumSize = New Drawing.Size(ContentWidth - ContextHelpUi.BadgeEdge, 0)
        body.Controls.Add(heading)
        Return body
    End Function

    Friend Shared Sub Finish(form As Form, body As Control, row As DialogButtonRow)
        form.Controls.Add(body)
        ' Last in the collection: the docked row takes its edge first and reads after the content in the Tab order.
        form.Controls.Add(row)
        row.Bind(form)
        form.AutoSize = True
        form.AutoSizeMode = AutoSizeMode.GrowAndShrink
        DpiLayout.ApplyAutoScale(form)   ' last, once every child exists - see DpiLayout
    End Sub

    Friend Shared Function NewRow(name As String) As DialogButtonRow
        Return New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = name, .Padding = New Padding(14, 4, 14, 10)}
    End Function

    Friend Shared Sub OnLoadFit(form As Form)
        ThemeService.Attach(form)
        OptionRows.ArrangeAll(form)
        DpiLayout.ClampToWorkingArea(form)
        DpiLayout.CenterOnOwner(form)
    End Sub

    ''' <summary>The masked password field of an account or the shared invitation (Q278-17: typed by hand only, so there is no way to
    ''' make one here and none to copy one). The reveal box is off by default; the policy is checked before anything is sent and a
    ''' failure is named beside the field. The control is emptied when the owner window closes.</summary>
    Friend NotInheritable Class PasswordBlock

        Friend ReadOnly Field As TextBox
        Friend ReadOnly Reveal As CheckBox
        Friend ReadOnly ReasonLabel As Label

        Friend Sub New(body As TableLayoutPanel, owner As Form, labelText As String, accessibleName As String)
            body.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 4, 0, 2), .Text = labelText})
            Field = New TextBox With {.Name = "ra_password", .Width = ContentWidth, .UseSystemPasswordChar = True,
                .Margin = New Padding(0, 0, 0, 4), .AccessibleName = accessibleName}
            body.Controls.Add(Field)
            Reveal = New CheckBox With {.Name = "ra_reveal", .AutoSize = True, .Margin = New Padding(0, 0, 0, 4),
                .Text = Localization.T("Показать пароль"), .Checked = False}
            AddHandler Reveal.CheckedChanged, Sub() Field.UseSystemPasswordChar = Not Reveal.Checked
            body.Controls.Add(OptionRows.InFlow(Reveal))
            body.Controls.Add(Note(ExchangeText.PolicyHint()))
            ReasonLabel = Reason()
            body.Controls.Add(ReasonLabel)
            AddHandler owner.FormClosed, Sub() Clear()
        End Sub

        Friend Sub Clear()
            Try
                Field.Text = ""
                Reveal.Checked = False
            Catch
            End Try
        End Sub

        ''' <summary>True when the typed text passes the policy; otherwise the sentence is shown beside the field and nothing may be sent.</summary>
        Friend Function Passes() As Boolean
            Dim verdict As ExchangePasswordPolicy.Verdict = ExchangePasswordPolicy.Check(Field.Text)
            If verdict = ExchangePasswordPolicy.Verdict.Accepted Then
                SetReason(ReasonLabel, "")
                Return True
            End If
            SetReason(ReasonLabel, ExchangeText.PolicyReason(verdict))
            Field.Focus()
            Return False
        End Function
    End Class
End Class

''' <summary>Every mutating action of the cards: the dialog or the confirmation, the operation, the sentence (5.6.2, 5.6.3).
''' A dialog is shown first on the calling (UI) thread; the operation then runs through the controller, which drops an answer that
''' arrives after the window moved on (<see cref="RemoteAdminActionResult.Declined"/>). The question, the modal show and the stored
''' opt-out are seams so a fact needs no visible window.</summary>
Friend NotInheritable Class RemoteAdminActions

    Private Sub New()
    End Sub

    ''' <summary>The question of a destructive action: True = confirmed. The default shows the confirmation window.</summary>
    Friend Shared AskConfirm As Func(Of IWin32Window, RemoteAdminConfirmQuestion, Boolean) = AddressOf AskWithForm

    ''' <summary>The store of the per-kind "do not ask again" values.</summary>
    Friend Shared ConfirmStore As Func(Of RemoteAdminConfirm) = AddressOf RemoteAdminConfirm.CreateDefault

    ''' <summary>Shows a dialog modally and returns how it was closed.</summary>
    Friend Shared RunDialog As Func(Of IWin32Window, Form, DialogResult) = AddressOf ShowModal

    Friend Shared Sub ResetSeams()
        AskConfirm = AddressOf AskWithForm
        ConfirmStore = AddressOf RemoteAdminConfirm.CreateDefault
        RunDialog = AddressOf ShowModal
    End Sub

    Private Shared Function ShowModal(owner As IWin32Window, form As Form) As DialogResult
        If owner Is Nothing Then Return form.ShowDialog()
        Return form.ShowDialog(owner)
    End Function

    Private Shared Function AskWithForm(owner As IWin32Window, question As RemoteAdminConfirmQuestion) As Boolean
        Using form As New Remote_Admin_Confirm_Form(question)
            Dim answer As DialogResult = ShowModal(owner, form)
            question.DoNotAskAgain = form.DoNotAskAgain
            Return answer = DialogResult.OK
        End Using
    End Function

    ' ----------------------------------------------------------------------------------------------------- words and the shape of a result

    Private Shared Function Named(controller As RemoteAdminController, sentence As String) As String
        Dim state As RemoteAdminState = controller.State
        Return Localization.TF("Сервер «{0}» ({1}): {2}", state.TargetName, Localization.LtrValue(state.TargetAddress), sentence)
    End Function

    Private Shared Function ServerLine(controller As RemoteAdminController) As String
        Dim state As RemoteAdminState = controller.State
        Return Localization.TF("Сервер «{0}» ({1})", state.TargetName, Localization.LtrValue(state.TargetAddress))
    End Function

    Private Shared Function Sentence(result As RemoteAdminResult) As String
        Return RemoteAdminWords.ForOutcome(result.Outcome, True)
    End Function

    ''' <summary>The shape every mutation ends in: nothing came back = declined; failed = no change and the sentence; done = changed.</summary>
    Private Shared Function Finished(result As RemoteAdminResult, controller As RemoteAdminController, doneLine As String) As RemoteAdminActionResult
        If result Is Nothing Then Return RemoteAdminActionResult.Declined
        If result.IsDone Then Return New RemoteAdminActionResult(True, Named(controller, doneLine))
        Return New RemoteAdminActionResult(False, Sentence(result))
    End Function

    ''' <summary>A refusal the form can name beside its field (a login taken, a password the policy refuses, a limit).</summary>
    Private Shared Function IsNameable(result As RemoteAdminResult) As Boolean
        If result Is Nothing OrElse result.Kind <> ExchangeRemoteAdminOutcomeKind.Failed Then Return False
        Return result.Code = ExchangeAdminCodes.Exists OrElse result.Code = ExchangeAdminCodes.BadLogin OrElse
               result.Code = ExchangeAdminCodes.WeakPassword OrElse result.Code = ExchangeAdminCodes.BadLimit OrElse
               result.Code = ExchangeAdminCodes.BadKind
    End Function

    Private Shared Function KindWord(kind As String) As String
        Return If(String.Equals(kind, "guest", StringComparison.OrdinalIgnoreCase), Localization.T("Гость"), Localization.T("Своё устройство или семья"))
    End Function

    Private Shared Function KindOf(kind As String) As String
        Return If(String.Equals(kind, "guest", StringComparison.OrdinalIgnoreCase), "guest", "member")
    End Function

    ' ----------------------------------------------------------------------------------------------------- the confirmations

    Friend Shared Function AccountRemoveQuestion(controller As RemoteAdminController, login As String) As RemoteAdminConfirmQuestion
        Return New RemoteAdminConfirmQuestion(RemoteAdminConfirmKind.AccountRemove, Localization.T("Удаление аккаунта"),
            String.Join(vbCrLf, {ServerLine(controller), Localization.TF("Удалить аккаунт «{0}»?", login),
                                 Localization.T("Его устройства, ресурсы и буфер обмена будут удалены с сервера.")}),
            Localization.T("Удалить"))
    End Function

    Friend Shared Function DeviceRevokeQuestion(controller As RemoteAdminController, login As String, device As RemoteAdminDeviceRow) As RemoteAdminConfirmQuestion
        Dim shown As String = If(String.IsNullOrWhiteSpace(device.DeviceName), device.DeviceId, device.DeviceName)
        Return New RemoteAdminConfirmQuestion(RemoteAdminConfirmKind.DeviceRevoke, Localization.T("Отзыв устройства"),
            String.Join(vbCrLf, {ServerLine(controller), Localization.TF("Отозвать устройство «{0}» аккаунта «{1}»?", shown, login),
                                 Localization.T("Чтобы вернуться, ему придётся войти заново.")}),
            Localization.T("Отозвать"))
    End Function

    Friend Shared Function WithdrawQuestion(controller As RemoteAdminController, row As RemoteAdminInvitationRow) As RemoteAdminConfirmQuestion
        Return New RemoteAdminConfirmQuestion(RemoteAdminConfirmKind.InvitationWithdraw, Localization.T("Отзыв приглашения"),
            String.Join(vbCrLf, {ServerLine(controller), Localization.TF("Отозвать приглашение ({0}), созданное {1}?", KindWord(row.Kind), Localization.LtrValue(row.CreatedAt)),
                                 Localization.T("Его больше нельзя будет использовать.")}),
            Localization.T("Отозвать"))
    End Function

    ''' <summary>The stored opt-out runs the action with no window; otherwise the question is asked and the answer recorded (only a
    ''' confirmed answer with the box ticked writes, for that kind alone).</summary>
    Private Shared Function Confirmed(owner As IWin32Window, kind As RemoteAdminConfirmKind, question As Func(Of RemoteAdminConfirmQuestion)) As Boolean
        Dim store As RemoteAdminConfirm = ConfirmStore()
        If store.IsSkipped(kind) Then Return True
        Dim asked As RemoteAdminConfirmQuestion = question()
        Dim yes As Boolean = AskConfirm(owner, asked)
        store.RecordAnswer(kind, yes, asked.DoNotAskAgain)
        Return yes
    End Function

    ' ----------------------------------------------------------------------------------------------------- accounts

    Friend Shared Async Function AddAccountAsync(owner As IWin32Window, controller As RemoteAdminController) As Task(Of RemoteAdminActionResult)
        Using form As New Remote_Admin_Account_Form(Remote_Admin_Account_Form.DialogMode.Add, "", "member")
            Do
                If RunDialog(owner, form) <> DialogResult.OK Then Return RemoteAdminActionResult.Declined
                Dim login As String = form.LoginValue
                Dim password As String = form.PasswordValue
                Dim kind As String = form.KindValue
                form.ForgetPassword()
                Dim result As RemoteAdminResult(Of RemoteAdminAccountAdded) = Await controller.CallAsync(
                    Function(ops As RemoteAdminOps, ct As CancellationToken) ops.AccountAddAsync(login, password, kind, ct), isMutation:=True)
                If result Is Nothing Then Return RemoteAdminActionResult.Declined
                If IsNameable(result) Then
                    form.ShowServerRefusal(result.Code, ExchangeAdminText.Failure(result.Code))
                    Continue Do
                End If
                If Not result.IsDone Then Return New RemoteAdminActionResult(False, Sentence(result))
                Dim line As String = ExchangeAdminText.AccountAdded(result.Value.Login)
                If result.Value.OverCap Then line = line & " " & Localization.T("Аккаунтов теперь больше, чем разрешает предел сервера.")
                Return New RemoteAdminActionResult(True, Named(controller, line))
            Loop
        End Using
    End Function

    Friend Shared Async Function ChangePasswordAsync(owner As IWin32Window, controller As RemoteAdminController, login As String) As Task(Of RemoteAdminActionResult)
        Using form As New Remote_Admin_Account_Form(Remote_Admin_Account_Form.DialogMode.Password, login, "member")
            Do
                If RunDialog(owner, form) <> DialogResult.OK Then Return RemoteAdminActionResult.Declined
                Dim password As String = form.PasswordValue
                form.ForgetPassword()
                Dim result As RemoteAdminResult(Of RemoteAdminPasswordChanged) = Await controller.CallAsync(
                    Function(ops As RemoteAdminOps, ct As CancellationToken) ops.AccountPasswdAsync(login, password, ct), isMutation:=True)
                If result Is Nothing Then Return RemoteAdminActionResult.Declined
                If IsNameable(result) Then
                    form.ShowServerRefusal(result.Code, ExchangeAdminText.Failure(result.Code))
                    Continue Do
                End If
                If Not result.IsDone Then Return New RemoteAdminActionResult(False, Sentence(result))
                Return New RemoteAdminActionResult(True, Named(controller, ExchangeAdminText.PasswordReset(login, result.Value.SignedOut > 0)))
            Loop
        End Using
    End Function

    Friend Shared Async Function ChangeKindAsync(owner As IWin32Window, controller As RemoteAdminController, row As RemoteAdminAccountRow) As Task(Of RemoteAdminActionResult)
        Dim login As String = row.Login
        Dim same As New RemoteAdminActionResult(False, Named(controller, Localization.TF("Тип аккаунта «{0}» уже такой, менять нечего.", login)))
        Using form As New Remote_Admin_Account_Form(Remote_Admin_Account_Form.DialogMode.Kind, login, KindOf(row.Kind))
            If RunDialog(owner, form) <> DialogResult.OK Then Return RemoteAdminActionResult.Declined
            Dim kind As String = form.KindValue
            If String.Equals(kind, KindOf(row.Kind), StringComparison.Ordinal) Then Return same
            Dim result As RemoteAdminResult(Of Boolean) = Await controller.CallAsync(
                Function(ops As RemoteAdminOps, ct As CancellationToken) ops.AccountKindAsync(login, kind, ct), isMutation:=True)
            If result Is Nothing Then Return RemoteAdminActionResult.Declined
            If result.IsDone AndAlso Not result.Value Then Return same
            Return Finished(result, controller, Localization.TF("Тип аккаунта «{0}» изменён.", login))
        End Using
    End Function

    Friend Shared Async Function EditAccountLimitsAsync(owner As IWin32Window, controller As RemoteAdminController, row As RemoteAdminAccountRow) As Task(Of RemoteAdminActionResult)
        Dim login As String = row.Login
        Using form As New Remote_Admin_Limits_Form(Remote_Admin_Limits_Form.DialogMode.Account, login, row.Limits, Nothing)
            Do
                If RunDialog(owner, form) <> DialogResult.OK Then Return RemoteAdminActionResult.Declined
                Dim limits As RemoteAdminLimits = form.LimitsValue
                Dim result As RemoteAdminResult = Await controller.CallAsync(
                    Function(ops As RemoteAdminOps, ct As CancellationToken) ops.AccountLimitsAsync(login, limits, ct), isMutation:=True)
                If result Is Nothing Then Return RemoteAdminActionResult.Declined
                If IsNameable(result) Then
                    form.ShowServerRefusal(result.Code, ExchangeAdminText.Failure(result.Code))
                    Continue Do
                End If
                Return Finished(result, controller, If(limits.IsEmpty, Localization.TF("Лимиты аккаунта «{0}» сняты.", login),
                                                       Localization.TF("Лимиты аккаунта «{0}» сохранены.", login)))
            Loop
        End Using
    End Function

    Friend Shared Async Function RemoveAccountAsync(owner As IWin32Window, controller As RemoteAdminController, row As RemoteAdminAccountRow) As Task(Of RemoteAdminActionResult)
        Dim login As String = row.Login
        If Not Confirmed(owner, RemoteAdminConfirmKind.AccountRemove, Function() AccountRemoveQuestion(controller, login)) Then Return RemoteAdminActionResult.Declined
        Dim result As RemoteAdminResult = Await controller.CallAsync(
            Function(ops As RemoteAdminOps, ct As CancellationToken) ops.AccountRemoveAsync(login, ct), isMutation:=True)
        Return Finished(result, controller, Localization.TF("Аккаунт «{0}» удалён.", login))
    End Function

    ' ----------------------------------------------------------------------------------------------------- devices

    Friend Shared Async Function RevokeDeviceAsync(owner As IWin32Window, controller As RemoteAdminController, login As String, device As RemoteAdminDeviceRow) As Task(Of RemoteAdminActionResult)
        If Not Confirmed(owner, RemoteAdminConfirmKind.DeviceRevoke, Function() DeviceRevokeQuestion(controller, login, device)) Then Return RemoteAdminActionResult.Declined
        Dim deviceId As String = device.DeviceId
        Dim result As RemoteAdminResult = Await controller.CallAsync(
            Function(ops As RemoteAdminOps, ct As CancellationToken) ops.DeviceRevokeAsync(login, deviceId, ct), isMutation:=True)
        If result IsNot Nothing AndAlso result.Kind = ExchangeRemoteAdminOutcomeKind.Failed AndAlso result.Code = ExchangeAdminCodes.NotFound Then
            ' gone already: the list is stale, so it is re-read
            Return New RemoteAdminActionResult(True, Named(controller, RemoteAdminWords.DeviceRevokeNotFound()))
        End If
        Dim shown As String = If(String.IsNullOrWhiteSpace(device.DeviceName), deviceId, device.DeviceName)
        Return Finished(result, controller, Localization.TF("Устройство «{0}» отозвано.", shown))
    End Function

    Friend Shared Async Function ShowPairingCodeAsync(owner As IWin32Window, controller As RemoteAdminController, login As String) As Task(Of RemoteAdminActionResult)
        Dim result As RemoteAdminResult(Of RemoteAdminPairingCode) = Await controller.CallAsync(
            Function(ops As RemoteAdminOps, ct As CancellationToken) ops.DevicePairingCodeAsync(login, ct), isMutation:=True)
        If result Is Nothing Then Return RemoteAdminActionResult.Declined
        If Not result.IsDone Then Return New RemoteAdminActionResult(False, Sentence(result))
        Using form As Remote_Admin_Invitation_Form = Remote_Admin_Invitation_Form.ForPairingCode(result.Value.Code, result.Value.ExpiresAt, login)
            RunDialog(owner, form)
        End Using
        Return New RemoteAdminActionResult(False, Named(controller, Localization.TF("Код привязки для «{0}» показан один раз.", login)))
    End Function

    ' ----------------------------------------------------------------------------------------------------- invitations

    Friend Shared Async Function CreateInvitationAsync(owner As IWin32Window, controller As RemoteAdminController) As Task(Of RemoteAdminActionResult)
        Using form As Remote_Admin_Invitation_Form = Remote_Admin_Invitation_Form.ForCreate()
            Do
                If RunDialog(owner, form) <> DialogResult.OK Then Return RemoteAdminActionResult.Declined
                Dim kind As String = form.KindValue
                Dim expected As String = form.ExpectedLoginValue
                Dim result As RemoteAdminResult(Of RemoteAdminInvitationCreated) = Await controller.CallAsync(
                    Function(ops As RemoteAdminOps, ct As CancellationToken) ops.InvitationCreateAsync(kind, expected, ct), isMutation:=True)
                If result Is Nothing Then Return RemoteAdminActionResult.Declined
                If IsNameable(result) Then
                    form.ShowServerRefusal(result.Code, ExchangeAdminText.Failure(result.Code))
                    Continue Do
                End If
                If Not result.IsDone Then Return New RemoteAdminActionResult(False, Sentence(result))
                Using shown As Remote_Admin_Invitation_Form = Remote_Admin_Invitation_Form.ForCreated(result.Value.Code, result.Value.ConnectionString, kind)
                    RunDialog(owner, shown)
                End Using
                Return New RemoteAdminActionResult(True, Named(controller, Localization.T("Приглашение создано")))
            Loop
        End Using
    End Function

    Friend Shared Async Function WithdrawInvitationAsync(owner As IWin32Window, controller As RemoteAdminController, row As RemoteAdminInvitationRow) As Task(Of RemoteAdminActionResult)
        If Not Confirmed(owner, RemoteAdminConfirmKind.InvitationWithdraw, Function() WithdrawQuestion(controller, row)) Then Return RemoteAdminActionResult.Declined
        Dim codeHash As String = row.CodeHash
        Dim result As RemoteAdminResult = Await controller.CallAsync(
            Function(ops As RemoteAdminOps, ct As CancellationToken) ops.InvitationWithdrawAsync(codeHash, ct), isMutation:=True)
        Return Finished(result, controller, Localization.T("Приглашение отозвано."))
    End Function

    Friend Shared Async Function SetSharedInvitationAsync(owner As IWin32Window, controller As RemoteAdminController, status As RemoteAdminSharedStatus) As Task(Of RemoteAdminActionResult)
        Using form As New Remote_Admin_Shared_Form(KindOf(If(status Is Nothing, "member", status.Kind)))
            Do
                If RunDialog(owner, form) <> DialogResult.OK Then Return RemoteAdminActionResult.Declined
                Dim kind As String = form.KindValue
                Dim password As String = form.PasswordValue
                form.ForgetPassword()
                Dim result As RemoteAdminResult = Await controller.CallAsync(
                    Function(ops As RemoteAdminOps, ct As CancellationToken) ops.SharedInvitationSetAsync(password, kind, ct), isMutation:=True)
                If result Is Nothing Then Return RemoteAdminActionResult.Declined
                If IsNameable(result) Then
                    form.ShowServerRefusal(result.Code, ExchangeAdminText.Failure(result.Code))
                    Continue Do
                End If
                Return Finished(result, controller, Localization.T("Общий пароль приглашения установлен."))
            Loop
        End Using
    End Function

    ''' <summary>Off is its own button and runs at once, with no dialog: the state line shows the result.</summary>
    Friend Shared Async Function TurnSharedInvitationOffAsync(owner As IWin32Window, controller As RemoteAdminController, status As RemoteAdminSharedStatus) As Task(Of RemoteAdminActionResult)
        Dim kind As String = KindOf(If(status Is Nothing, "member", status.Kind))
        Dim result As RemoteAdminResult = Await controller.CallAsync(
            Function(ops As RemoteAdminOps, ct As CancellationToken) ops.SharedInvitationOffAsync(kind, ct), isMutation:=True)
        Return Finished(result, controller, Localization.T("Общий пароль приглашения выключен."))
    End Function

    ' ----------------------------------------------------------------------------------------------------- limits and policy

    Friend Shared Async Function EditPolicyAsync(owner As IWin32Window, controller As RemoteAdminController, view As RemoteAdminPolicyView) As Task(Of RemoteAdminActionResult)
        Dim current As RemoteAdminLimits = If(view Is Nothing, RemoteAdminLimits.Empty, view.Defaults)
        Dim currentMax As Integer? = If(view Is Nothing, Nothing, view.MaxAccounts)
        Using form As New Remote_Admin_Limits_Form(Remote_Admin_Limits_Form.DialogMode.Policy, "", current, currentMax)
            Do
                If RunDialog(owner, form) <> DialogResult.OK Then Return RemoteAdminActionResult.Declined
                Dim maxAccounts As Integer? = form.MaxAccountsValue
                Dim limits As RemoteAdminLimits = form.LimitsValue
                ' only what changed is sent: Nothing stays as the server has it
                Dim sendMax As Integer? = If(maxAccounts.HasValue AndAlso Not Nullable.Equals(maxAccounts, currentMax), maxAccounts, Nothing)
                Dim sendLimits As RemoteAdminLimits = Changed(limits, current)
                If Not sendMax.HasValue AndAlso sendLimits.IsEmpty Then
                    Return New RemoteAdminActionResult(False, Named(controller, Localization.T("Правила сервера не изменились.")))
                End If
                Dim result As RemoteAdminResult(Of RemoteAdminPolicyView) = Await controller.CallAsync(
                    Function(ops As RemoteAdminOps, ct As CancellationToken) ops.PolicySetAsync(sendMax, sendLimits, ct), isMutation:=True)
                If result Is Nothing Then Return RemoteAdminActionResult.Declined
                If IsNameable(result) Then
                    form.ShowServerRefusal(result.Code, ExchangeAdminText.Failure(result.Code))
                    Continue Do
                End If
                If Not result.IsDone Then Return New RemoteAdminActionResult(False, Sentence(result))
                Dim line As String = Localization.T("Правила сервера сохранены.")
                If result.Value IsNot Nothing AndAlso Not result.Value.Persisted Then line = line & " " & Localization.T("Изменение действует до перезапуска сервера.")
                Return New RemoteAdminActionResult(True, Named(controller, line))
            Loop
        End Using
    End Function

    ''' <summary>The members of <paramref name="typed"/> that differ from what the server has, as a limits value (Nothing members stay unchanged).</summary>
    Friend Shared Function Changed(typed As RemoteAdminLimits, server As RemoteAdminLimits) As RemoteAdminLimits
        Return New RemoteAdminLimits(
            If(typed.DevicesPerAccount.HasValue AndAlso Not Nullable.Equals(typed.DevicesPerAccount, server.DevicesPerAccount), typed.DevicesPerAccount, Nothing),
            If(typed.ResourcesPerAccount.HasValue AndAlso Not Nullable.Equals(typed.ResourcesPerAccount, server.ResourcesPerAccount), typed.ResourcesPerAccount, Nothing),
            If(typed.BroadcastsPerAccount.HasValue AndAlso Not Nullable.Equals(typed.BroadcastsPerAccount, server.BroadcastsPerAccount), typed.BroadcastsPerAccount, Nothing),
            If(typed.BroadcastsPerDevice.HasValue AndAlso Not Nullable.Equals(typed.BroadcastsPerDevice, server.BroadcastsPerDevice), typed.BroadcastsPerDevice, Nothing),
            If(typed.ClipboardMaxBytes.HasValue AndAlso Not Nullable.Equals(typed.ClipboardMaxBytes, server.ClipboardMaxBytes), typed.ClipboardMaxBytes, Nothing))
    End Function

End Class
