#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Asks for the password of a resource (007 §3, §5): when none is stored, when the stored one cannot be
''' read back, and when the server refused it. Built in code and sized in plain pixels, like the other
''' viewer dialogs - the viewer is pinned to a DPI-unaware, pixel-tuned layout (CLAUDE.md, "Modern-only
''' compatibility pins").
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
    Private ReadOnly warning As Label
    Private ReadOnly okButton As Button
    Private ReadOnly dismissButton As Button
    Private script_Font As Font

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
    Friend Sub New(resourceName As String, userName As String, retry As Boolean, Optional showRemember As Boolean = True)
        Me.Text = Localization.T("Пароль ресурса")
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ClientSize = New Size(460, 178)

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
            .Text = Localization.TF("Введите пароль для «{0}» (пользователь {1})", resourceName, userName)
        }

        field = New TextBox With {
            .Name = "resource_Password",
            .Location = New Point(14, 52),
            .Size = New Size(432, 24),
            .UseSystemPasswordChar = True
        }

        reveal = New CheckBox With {
            .Name = "resource_Reveal",
            .AutoSize = True,
            .Location = New Point(14, 82),
            .Text = Localization.T("Показать пароль")
        }
        AddHandler reveal.CheckedChanged, Sub() field.UseSystemPasswordChar = Not reveal.Checked

        remember = New CheckBox With {
            .Name = "resource_Remember",
            .AutoSize = True,
            .Location = New Point(14, 104),
            .Checked = True,
            .Visible = showRemember,
            .Text = Localization.T("Запомнить пароль (защищён средствами Windows)")
        }

        warning = New Label With {
            .Name = "resource_Warning",
            .AutoSize = False,
            .Location = New Point(14, 126),
            .Size = New Size(432, 16),
            .ForeColor = Color.Firebrick,
            .Text = If(retry, Localization.T("Пароль не подошёл. Введите его ещё раз."), "")
        }

        okButton = New Button With {
            .Name = "resource_Ok",
            .Location = New Point(246, 142),
            .Size = New Size(100, 28),
            .Text = Localization.T("Подключиться"),
            .DialogResult = DialogResult.None
        }
        AddHandler okButton.Click, AddressOf Confirm

        dismissButton = New Button With {
            .Name = "resource_Cancel",
            .Location = New Point(354, 142),
            .Size = New Size(92, 28),
            .Text = Localization.T("Отмена"),
            .DialogResult = DialogResult.Cancel
        }

        Me.Controls.Add(prompt)
        Me.Controls.Add(field)
        Me.Controls.Add(reveal)
        Me.Controls.Add(remember)
        Me.Controls.Add(warning)
        Me.Controls.Add(okButton)
        Me.Controls.Add(dismissButton)
        Me.AcceptButton = okButton
        Me.CancelButton = dismissButton
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        ' After the base: the children are gone by then and nothing paints with it.
        If disposing Then script_Font?.Dispose()
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        field.Focus()
    End Sub

    ''' <summary>An empty password is refused here: a server that wants none is not an SFTP login we can
    ''' store (ResourceCommit refuses it too), and saying so now is kinder than failing later.</summary>
    Private Sub Confirm(sender As Object, e As EventArgs)
        If field.Text.Length = 0 Then
            warning.Text = Localization.T("Введите пароль")
            field.Focus()
            Return
        End If
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

End Class
#End If
