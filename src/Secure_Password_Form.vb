#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The one dialog of the secure-container feature
''' (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §3.2).
'''
''' Built in code from 96-dpi positions. The viewer has been PerMonitorV2 since the chrome migration, so at
''' 175 % the point font grows while literal pixels do not: DialogLayout.ApplyAutoScale is the LAST line of the
''' builder and DialogLayout.FitContent runs from OnLoad (124 UX-202/UX-203), so a wrapped warning pushes the
''' buttons down instead of lying over them. The font is only touched for a script the pinned face cannot draw
''' at all.
'''
''' Three things about it are rules rather than taste:
'''
''' - It names the CONTAINER, never a true name it does not yet know. It cannot know one:
'''   the name is inside the sealed metadata, behind the credential being asked for.
''' - It states, when the field is empty and the user confirms anyway, that an empty
'''   credential is NOT protection. FDSEC-BEHAVIOUR §3.3 requires that sentence on every
'''   surface that accepts one, and the wording must not reassure: obfuscated containers
'''   exist, and somebody meets one rarely enough to believe a friendly message.
''' - "Remember while the program is running" is MEMORY ONLY. Nothing here reaches the
'''   registry, a file, or the DPAPI store the OCR API keys use - that store is a
'''   convenience for a third-party service token, and this is the user's own data.
'''
''' Escape, the close box and Cancel are one path and change nothing (APP-BEHAVIOUR rule 1).
''' </summary>
Friend NotInheritable Class Secure_Password_Form
    Inherits Form

    Private ReadOnly prompt As Label
    Private ReadOnly field As TextBox
    Private ReadOnly reveal As CheckBox
    Private ReadOnly remember As CheckBox
    Private ReadOnly warning As Label
    Private ReadOnly okButton As Button
    Private ReadOnly dismissButton As Button

    ''' <summary>True once the user has been shown the empty-credential statement and
    ''' confirmed anyway - the second press is the answer, the first one is the warning.</summary>
    Private emptyWarned As Boolean

    ''' <summary>The credential the user typed. Never trimmed: a leading or trailing space
    ''' is part of it (FDSEC-BEHAVIOUR §13.1, mistake 5).</summary>
    Friend ReadOnly Property Credential As String
        Get
            Return field.Text
        End Get
    End Property

    ''' <summary>Whether to hold it for the rest of the session (§5.5).</summary>
    Friend ReadOnly Property RememberForSession As Boolean
        Get
            Return remember.Checked
        End Get
    End Property

    ''' <param name="containerName">The container's own visible name - the only name that
    ''' exists before the credential does.</param>
    ''' <param name="retry">True when a previous attempt did not open it, so the dialog
    ''' opens carrying the reason instead of looking like the first ask.</param>
    Friend Sub New(containerName As String, retry As Boolean)
        Me.Text = Localization.T("Защищённый файл")
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ClientSize = New Size(460, 186)

        Dim family As String = Localization.FontFamily()
        If family IsNot Nothing Then
            script_Font = New Font(family, Me.Font.Size)
            Me.Font = script_Font
        End If
        If Localization.IsRightToLeft() Then Me.RightToLeft = RightToLeft.Yes

        prompt = New Label With {
            .Name = "secure_Prompt",
            .AutoSize = False,
            .Location = New Point(14, 14),
            .Size = New Size(432, 34),
            .TextAlign = ContentAlignment.TopLeft,
            .Text = Localization.TF("Введите пароль к файлу {0}", containerName)
        }

        field = New TextBox With {
            .Name = "secure_Password",
            .Location = New Point(14, 54),
            .Size = New Size(432, 24),
            .UseSystemPasswordChar = True
        }
        AddHandler field.TextChanged, Sub() ClearWarning()

        reveal = New CheckBox With {
            .Name = "secure_Reveal",
            .AutoSize = True,
            .Location = New Point(14, 86),
            .Text = Localization.T("Показать пароль")
        }
        ' A deliberate reveal, and it starts off: somebody typing a password over a
        ' shoulder-height monitor gets to choose, and the default chooses for nobody.
        AddHandler reveal.CheckedChanged, Sub() field.UseSystemPasswordChar = Not reveal.Checked

        remember = New CheckBox With {
            .Name = "secure_Remember",
            .AutoSize = True,
            .Location = New Point(14, 110),
            .Checked = True,
            .Text = Localization.T("Помнить, пока программа открыта")
        }

        warning = New Label With {
            .Name = "secure_Warning",
            .AutoSize = False,
            .Location = New Point(14, 134),
            .Size = New Size(432, 16),
            .ForeColor = Color.Firebrick,
            .Text = If(retry, Localization.T("Не открылось: пароль может быть неверным, файл может не быть контейнером, либо его изменили."), "")
        }

        okButton = New Button With {
            .Name = "secure_Ok",
            .Location = New Point(266, 150),
            .Size = New Size(86, 28),
            .Text = Localization.T("Открыть"),
            .DialogResult = DialogResult.None
        }
        AddHandler okButton.Click, AddressOf Confirm
        ButtonGlyphs.Apply(okButton, "action.confirm", ButtonGlyphRole.Affirmative)

        dismissButton = New Button With {
            .Name = "secure_Cancel",
            .Location = New Point(360, 150),
            .Size = New Size(86, 28),
            .Text = Localization.T("Отмена"),
            .DialogResult = DialogResult.Cancel
        }
        ButtonGlyphs.Apply(dismissButton, "action.cancel", ButtonGlyphRole.Dismissing)

        Me.Controls.Add(prompt)
        Me.Controls.Add(field)
        Me.Controls.Add(reveal)
        Me.Controls.Add(remember)
        Me.Controls.Add(warning)
        Me.Controls.Add(okButton)
        Me.Controls.Add(dismissButton)
        Me.AcceptButton = okButton
        ' Escape, the close box and Cancel are the same path, and it changes nothing.
        Me.CancelButton = dismissButton

        ' LAST line of the builder: scaling touches only the children that exist by now.
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    ''' <summary>The face for a script the pinned font cannot draw. A Control never disposes
    ''' a Font it was handed, so the dialog does (057.S17-6) - it is shown once per unlock
    ''' attempt, and each one used to leave a GDI font handle to the finalizer.</summary>
    Private script_Font As Font

    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        ' After the base: the children are gone by then and nothing paints with it.
        If disposing Then script_Font?.Dispose()
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DialogLayout.FitContent(Me)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        field.Focus()
    End Sub

    Private Sub ClearWarning()
        If emptyWarned Then
            emptyWarned = False
            warning.Text = ""
        End If
    End Sub

    ''' <summary>
    ''' The empty credential is ACCEPTED - the format defines it and refusing it would
    ''' leave this viewer unable to open files that are perfectly valid. What it is not is
    ''' protection, and the first press says so; the second press is the answer.
    ''' </summary>
    Private Sub Confirm(sender As Object, e As EventArgs)
        If field.Text.Length = 0 AndAlso Not emptyWarned Then
            emptyWarned = True
            warning.Text = Localization.T("Пустой пароль - это не защита: такой файл лишь скрыт от беглого взгляда. Нажмите ещё раз, чтобы открыть.")
            DialogLayout.FitLabel(warning)
            Return
        End If
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

End Class
#End If
