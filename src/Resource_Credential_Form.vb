#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Asks for the password of a resource (007 §3, §5): when none is stored, when the stored one cannot be
''' read back, and when the server refused it. Built in code from 96-dpi positions; DialogLayout scales it and
''' reflows a wrapped caption (the viewer has been PerMonitorV2 since the chrome migration, 124 UX-202).
'''
''' Three rules, not taste:
''' - The password is returned, never kept: the caller decides whether to store it (DPAPI, through
'''   <see cref="ResourceCommit"/>) and only after the connection it was typed for has worked.
''' - "Remember" is a visible choice and starts ON only because the store is the user's own DPAPI key, never
'''   a file or a plain registry value; unchecked, the password lives in memory for this run alone.
''' - Escape, the close box and Cancel are one path and change nothing.
''' </summary>
Friend NotInheritable Class Resource_Credential_Form
    Inherits Form

    Private ReadOnly prompt As Label
    Private ReadOnly field As TextBox
    Private ReadOnly reveal As CheckBox
    Private ReadOnly remember As CheckBox
    Private ReadOnly revealRow As OptionRowHost
    Private ReadOnly rememberRow As OptionRowHost
    Private ReadOnly warning As Label
    Private ReadOnly okButton As Button
    Private ReadOnly dismissButton As Button
    Private ReadOnly answerRow As DialogButtonRow
    Private script_Font As Font
    Private ReadOnly pinMode As Boolean

    ''' <summary>The password as typed, never trimmed: a space at either end is part of it.</summary>
    Friend ReadOnly Property Credential As String
        Get
            Return field.Text
        End Get
    End Property

    ''' <summary>Whether to keep it for the next run (stored through DPAPI by the caller).</summary>
    Friend ReadOnly Property RememberCredential As Boolean
        Get
            Return remember.Checked
        End Get
    End Property

    ''' <param name="retry">True when a password was just refused, so the dialog opens carrying the reason.</param>
    ''' <param name="showRemember">False when the caller stores the password regardless (an import does), so the dialog does not offer a choice that changes nothing.</param>
    Friend Sub New(resourceName As String, userName As String, retry As Boolean, Optional showRemember As Boolean = True, Optional pinMode As Boolean = False)
        Me.pinMode = pinMode
        Me.Text = If(pinMode, Localization.T("PIN-код ресурса"), Localization.T("Пароль ресурса"))
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        ' The authored content ends at 178 (the two 40 px option rows, then the warning line); the bottom row below it is 50 tall
        ' (8 + one 32 line + 10) at 96 dpi.
        Me.ClientSize = New Size(460, 228)

        Dim family As String = Localization.FontFamily()
        If family IsNot Nothing Then
            script_Font = New Font(family, Me.Font.Size)
            Me.Font = script_Font
        End If
        If Localization.IsRightToLeft() Then Me.RightToLeft = RightToLeft.Yes

        prompt = New Label With {
            .Name = "resource_Prompt",
            .AutoSize = False,
            .Location = New Point(14, 14),
            .Size = New Size(432, 34),
            .Text = If(pinMode, Localization.TF("Введите PIN-код для «{0}»", resourceName), Localization.TF("Введите пароль для «{0}» (пользователь {1})", resourceName, userName))
        }

        field = New TextBox With {
            .Name = "resource_Password",
            .Location = New Point(14, 52),
            .Size = New Size(432, 24),
            .MaxLength = If(pinMode, 128, 32767),
            .UseSystemPasswordChar = True
        }

        reveal = New CheckBox With {
            .Name = "resource_Reveal",
            .Text = If(pinMode, Localization.T("Показать PIN-код"), Localization.T("Показать пароль"))
        }
        AddHandler reveal.CheckedChanged, Sub() field.UseSystemPasswordChar = Not reveal.Checked

        Dim offerRemember As Boolean = showRemember AndAlso Not pinMode
        remember = New CheckBox With {
            .Name = "resource_Remember",
            .Checked = True,
            .Visible = offerRemember,
            .Text = Localization.T("Запомнить пароль (защищён средствами Windows)")
        }

        ' Both are option rows of the Settings anatomy (ticket 243 T-11b, BOOLEAN-OPTION 2.0): the same check boxes with the
        ' same names, state and handlers, a 20 px box in a 32 px target, the caption beside it. The hidden one hides its row.
        revealRow = DialogOptionRow.CreateAt(reveal, Nothing, 14, 80, 460)
        rememberRow = DialogOptionRow.CreateAt(remember, Nothing, 14, 120, 460)
        rememberRow.Visible = offerRemember

        warning = New Label With {
            .Name = "resource_Warning",
            .AutoSize = False,
            .Location = New Point(14, 162),
            .Size = New Size(432, 16),
            .ForeColor = Color.Firebrick,
            .Text = If(retry, If(pinMode, Localization.T("PIN-код не подошёл. Введите его ещё раз."), Localization.T("Пароль не подошёл. Введите его ещё раз.")), "")
        }

        okButton = New Button With {
            .Name = "resource_Ok",
            .Text = Localization.T("Подключиться"),
            .DialogResult = DialogResult.None
        }
        AddHandler okButton.Click, AddressOf Confirm

        dismissButton = New Button With {
            .Name = "resource_Cancel",
            .Text = Localization.T("Отмена"),
            .DialogResult = DialogResult.Cancel
        }

        ' The answers ride the shared bottom row (ticket 243 T-8b, BUTTONS 2.0 sections 3 and 4): Cancel first, the final
        ' affirmative answer last, widths from the caption and its glyph. Bind keeps the meaning they had: Escape is
        ' Cancel, Enter is Connect (the safe default - it connects, it destroys nothing).
        answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "resource_Buttons", .Padding = New Padding(14, 8, 14, 10)}
        answerRow.Add(okButton, DialogButtonRole.Affirmative, "action.confirm")
        answerRow.Add(dismissButton, DialogButtonRole.Dismissing, "action.cancel")

        Me.Controls.Add(prompt)
        Me.Controls.Add(field)
        Me.Controls.Add(revealRow)
        Me.Controls.Add(rememberRow)
        Me.Controls.Add(warning)
        Me.Controls.Add(answerRow)
        answerRow.Bind(Me)

        ' LAST line of the builder: scaling touches only the children that exist by now.
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        ' After the base: the children are gone by then and nothing paints with it.
        If disposing Then script_Font?.Dispose()
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DialogLayout.FitContent(Me)
        DialogLayout.KeepRoomForRow(Me, answerRow)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        field.Focus()
    End Sub

    ''' <summary>An empty password is refused here: a server that wants none is not an SFTP login we can
    ''' store (ResourceCommit refuses it too), and saying so now is kinder than failing later.</summary>
    Private Sub Confirm(sender As Object, e As EventArgs)
        If field.Text.Length = 0 Then
            warning.Text = If(pinMode, Localization.T("PIN-код ресурса"), Localization.T("Введите пароль"))
            field.Focus()
            Return
        End If
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

End Class

Friend NotInheritable Class ResourcePinPromptUi
    Private Sub New()
    End Sub
    Friend Shared Function Ask(owner As Form, resourceName As String, Optional retry As Boolean = False) As String
        Using dialog As New Resource_Credential_Form(resourceName, "", retry, showRemember:=False, pinMode:=True)
            If DialogLayer.ShowViewerDialog(dialog, owner) <> DialogResult.OK Then Return Nothing
            Return dialog.Credential
        End Using
    End Function
End Class
#End If
