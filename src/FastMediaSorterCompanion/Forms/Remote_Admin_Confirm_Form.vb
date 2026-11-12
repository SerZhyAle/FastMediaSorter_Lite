Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The confirmation of one of the three destructive actions of the remote administration window (ticket L0278 5.6.3): the server,
''' the object and the consequence in words, the acting button and, at the reading start, the "do not ask again" box of that kind
''' (off by default). The safe answer holds the default focus and is the Enter and Escape answer; the acting button is never the
''' default. It is a form of its own because the viewer's linked question window has no box (<c>ManagerBox</c> shows it with the
''' stock answers only); nothing of the viewer's files is changed for this.
''' </summary>
Friend NotInheritable Class Remote_Admin_Confirm_Form
    Inherits Form

    Private _chkNever As CheckBox
    Private _btnSafe As Button
    Private _btnAct As Button
    Private _answerRow As DialogButtonRow

    Friend Sub New(question As RemoteAdminConfirmQuestion)
        If question Is Nothing Then Throw New ArgumentNullException(NameOf(question))
        BuildUi(question)
    End Sub

    ''' <summary>The box as the person left it.</summary>
    Friend ReadOnly Property DoNotAskAgain As Boolean
        Get
            Return _chkNever IsNot Nothing AndAlso _chkNever.Checked
        End Get
    End Property

    ''' <summary>The button that declines: the default, the Enter button and the Escape button.</summary>
    Friend ReadOnly Property SafeButton As Button
        Get
            Return _btnSafe
        End Get
    End Property

    Friend ReadOnly Property ActButton As Button
        Get
            Return _btnAct
        End Get
    End Property

    Friend ReadOnly Property NeverBox As CheckBox
        Get
            Return _chkNever
        End Get
    End Property

    Private Sub BuildUi(question As RemoteAdminConfirmQuestion)
        UiLanguage.ApplyTo(Me)
        Dim body As TableLayoutPanel = RemoteAdminFormKit.Begin(Me, question.Caption)
        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(RemoteAdminFormKit.ContentWidth, 0),
            .Margin = New Padding(0, 0, 0, 8), .Text = question.Text})

        _chkNever = New CheckBox With {.Name = "rac_never", .AutoSize = True, .Margin = New Padding(0, 0, 0, 4),
            .Text = Localization.T("Больше не спрашивать"), .Checked = False}
        body.Controls.Add(OptionRows.InFlow(_chkNever))

        ' The safe answer is the Dismissing one: Cancel reads first, holds the default focus, Enter and Escape reach it.
        ' The acting button takes the Destructive place and is never the default (BUTTONS section 4).
        _btnSafe = RemoteAdminFormKit.NewButton(Localization.T("Отмена"), "action.cancel", "rac_No")
        _btnSafe.DialogResult = DialogResult.Cancel
        _btnAct = RemoteAdminFormKit.NewButton(question.ConfirmCaption, "action.confirm", "rac_Yes")
        _btnAct.DialogResult = DialogResult.OK
        _answerRow = RemoteAdminFormKit.NewRow("rac_Buttons")
        DpiLayout.AddAnswer(_answerRow, _btnSafe, DialogButtonRole.Dismissing, "action.cancel")
        DpiLayout.AddAnswer(_answerRow, _btnAct, DialogButtonRole.Destructive, "action.confirm")

        RemoteAdminFormKit.Finish(Me, body, _answerRow)
        Me.AcceptButton = _btnSafe
        Me.CancelButton = _btnSafe
        Me.ActiveControl = _btnSafe
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        RemoteAdminFormKit.OnLoadFit(Me)
        _btnSafe.Focus()
    End Sub
End Class
