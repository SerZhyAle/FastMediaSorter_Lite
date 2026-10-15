#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' "Change the password" of one exchange account (ticket 226 rule 4): the old password and the new one twice, the server's
''' password policy in words beside the field (so the person reads it before the first refusal), and nothing else. The
''' window itself sends nothing: it returns what was typed, and the caller holds the session that speaks.
'''
''' Rules held here:
'''   * the destination never changes - the account this dialog was opened for is named in the title, and the old password
'''     is what proves the person's right to change it;
'''   * the policy is checked locally for IMMEDIATE feedback only; the server has the last word (a wrong old password is
'''     the common refusal of the table, and a weak new one is refused too);
'''   * the new password is typed twice and the twin must match before anything is sent;
'''   * the passwords live in this window's fields and in the one frame the caller builds; the fields are cleared on every
'''     outcome and on close.
''' Built in code from 96-dpi positions; DialogLayout scales it (the last line of the builder).
''' </summary>
Friend NotInheritable Class Exchange_Password_Change_Form
    Inherits Form

    Private ReadOnly oldLabel As Label
    Private ReadOnly oldBox As TextBox
    Private ReadOnly newLabel As Label
    Private ReadOnly newBox As TextBox
    Private ReadOnly repeatLabel As Label
    Private ReadOnly repeatBox As TextBox
    Private ReadOnly revealBox As CheckBox
    Private ReadOnly policyNote As Label
    Private ReadOnly statusLabel As Label
    Private ReadOnly okButton As Button
    Private ReadOnly cancelButton_ As Button
    Private script_Font As Font

    ''' <summary>The old password, taken at the moment of OK (the fields are cleared on close).</summary>
    Friend ReadOnly Property OldPassword As String
        Get
            Return _old
        End Get
    End Property

    ''' <summary>The new password, or "" when the window closed without one.</summary>
    Friend ReadOnly Property NewPassword As String
        Get
            Return _new
        End Get
    End Property

    Private _old As String = ""
    Private _new As String = ""

    ''' <param name="accountName">The account this dialog changes the password of: its server and login, in the title.</param>
    Friend Sub New(accountName As String)
        Me.Text = ExchangeEntryText.ChangePasswordTitle(accountName)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ClientSize = New Size(420, 282)

        Dim family As String = Localization.FontFamily()
        If family IsNot Nothing Then
            script_Font = New Font(family, Me.Font.Size)
            Me.Font = script_Font
        End If
        If Localization.IsRightToLeft() Then Me.RightToLeft = RightToLeft.Yes

        oldLabel = New Label With {.Name = "xpc_Old", .AutoSize = False, .Location = New Point(14, 14), .Size = New Size(392, 16), .Text = ExchangeEntryText.ChangePasswordOldLabel()}
        oldBox = New TextBox With {.Name = "xpc_OldBox", .Location = New Point(14, 32), .Size = New Size(392, 24), .UseSystemPasswordChar = True,
                                   .AccessibleName = ExchangeEntryText.ChangePasswordOldLabel()}
        newLabel = New Label With {.Name = "xpc_New", .AutoSize = False, .Location = New Point(14, 62), .Size = New Size(392, 16), .Text = ExchangeEntryText.ChangePasswordNewLabel()}
        newBox = New TextBox With {.Name = "xpc_NewBox", .Location = New Point(14, 80), .Size = New Size(392, 24), .UseSystemPasswordChar = True,
                                   .AccessibleName = ExchangeEntryText.ChangePasswordNewLabel()}
        repeatLabel = New Label With {.Name = "xpc_Repeat", .AutoSize = False, .Location = New Point(14, 110), .Size = New Size(392, 16), .Text = ExchangeEntryText.ChangePasswordRepeatLabel()}
        repeatBox = New TextBox With {.Name = "xpc_RepeatBox", .Location = New Point(14, 128), .Size = New Size(392, 24), .UseSystemPasswordChar = True,
                                      .AccessibleName = ExchangeEntryText.ChangePasswordRepeatLabel()}
        revealBox = New CheckBox With {.Name = "xpc_Reveal", .AutoSize = True, .Location = New Point(14, 156), .Text = Localization.T("Показать пароль")}
        AddHandler revealBox.CheckedChanged, Sub()
                                                 oldBox.UseSystemPasswordChar = Not revealBox.Checked
                                                 newBox.UseSystemPasswordChar = Not revealBox.Checked
                                                 repeatBox.UseSystemPasswordChar = Not revealBox.Checked
                                             End Sub
        policyNote = New Label With {.Name = "xpc_Policy", .AutoSize = False, .Location = New Point(14, 178), .Size = New Size(392, 30),
                                     .ForeColor = SystemColors.GrayText, .Text = ExchangeEntryText.PasswordPolicyNote()}
        statusLabel = New Label With {.Name = "xpc_Status", .AutoSize = False, .Location = New Point(14, 210), .Size = New Size(392, 18),
                                      .ForeColor = Color.Firebrick, .Text = ""}
        okButton = New Button With {.Name = "xpc_Ok", .Location = New Point(186, 236), .Size = New Size(120, 28), .Text = ExchangeEntryText.ChangePasswordButton()}
        AddHandler okButton.Click, AddressOf OnOk
        ButtonGlyphs.Apply(okButton, "action.confirm", ButtonGlyphRole.Affirmative)
        cancelButton_ = New Button With {.Name = "xpc_Cancel", .Location = New Point(314, 236), .Size = New Size(92, 28), .Text = Localization.T("Отмена"),
                                         .DialogResult = DialogResult.Cancel}
        ButtonGlyphs.Apply(cancelButton_, "action.cancel", ButtonGlyphRole.Dismissing)

        Me.Controls.AddRange(New Control() {oldLabel, oldBox, newLabel, newBox, repeatLabel, repeatBox, revealBox, policyNote, statusLabel, okButton, cancelButton_})
        Me.AcceptButton = okButton
        Me.CancelButton = cancelButton_

        ' LAST line of the builder: scaling touches only the children that exist by now.
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DialogLayout.FitContent(Me)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        oldBox.Focus()
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        oldBox.Text = ""
        newBox.Text = ""
        repeatBox.Text = ""
        MyBase.OnFormClosed(e)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        If disposing Then script_Font?.Dispose()
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        statusLabel.ForeColor = Color.Firebrick
        If oldBox.Text.Length = 0 Then
            statusLabel.Text = ExchangeEntryText.ChangePasswordNeedOld()
            oldBox.Focus()
            Return
        End If
        If Not newBox.Text.Equals(repeatBox.Text, StringComparison.Ordinal) Then
            statusLabel.Text = ExchangeEntryText.PasswordsDiffer()
            newBox.Focus()
            Return
        End If
        Dim verdict As Integer = ExchangePasswordVerdict.Of(newBox.Text)
        If verdict >= 0 Then
            statusLabel.Text = ExchangeEntryText.PasswordPolicyFailure(verdict)
            newBox.Focus()
            Return
        End If
        _old = oldBox.Text
        _new = newBox.Text
        DialogResult = DialogResult.OK
        Close()
    End Sub

End Class

''' <summary>The server's password policy (the contract's floor, ticket 138), mirrored for IMMEDIATE feedback only - the server
''' has the last word. Counted in characters (Unicode code points), the classes ASCII-only, exactly as the Go rule counts.</summary>
Friend NotInheritable Class ExchangePasswordVerdict

    Private Sub New()
    End Sub

    Public Const MinLength As Integer = 10

    ''' <summary>-1 when the password passes; 0-3 the reason it does not (too short, no upper-case Latin letter, no lower-case one, no digit).</summary>
    Public Shared Function [Of](password As String) As Integer
        Dim text As String = If(password, "")
        Dim count As Integer = 0
        Dim up As Boolean = False, low As Boolean = False, dig As Boolean = False
        For Each rune As System.Text.Rune In text.EnumerateRunes()
            count += 1
            Dim code As Integer = rune.Value
            If code >= AscW("A"c) AndAlso code <= AscW("Z"c) Then
                up = True
            ElseIf code >= AscW("a"c) AndAlso code <= AscW("z"c) Then
                low = True
            ElseIf code >= AscW("0"c) AndAlso code <= AscW("9"c) Then
                dig = True
            End If
        Next
        If count < MinLength Then Return 0
        If Not up Then Return 1
        If Not low Then Return 2
        If Not dig Then Return 3
        Return -1
    End Function

End Class
#End If
