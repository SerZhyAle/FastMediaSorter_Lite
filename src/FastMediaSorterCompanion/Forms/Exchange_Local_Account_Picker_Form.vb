Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' When a local exchange server holds multiple accounts, this lightweight dialog lets the user pick which account
''' to connect this PC to in one click, without entering passwords, IPs, or ports.
''' </summary>
Friend NotInheritable Class Exchange_Local_Account_Picker_Form
    Inherits Form

    Private Const ContentWidth As Integer = 380

    Private ReadOnly _accounts As IReadOnlyList(Of String)
    Private _iconHandle As IntPtr
    Private _lvAccounts As ListView
    Private _btnOk As Button
    Private _btnCancel As Button
    Private _answerRow As DialogButtonRow
    Private _selectedLogin As String = ""

    Friend ReadOnly Property SelectedLogin As String
        Get
            Return _selectedLogin
        End Get
    End Property

    Friend Sub New(accounts As IReadOnlyList(Of String))
        _accounts = If(accounts, New List(Of String)())
        BuildUi()
    End Sub

    Private Sub BuildUi()
        UiLanguage.ApplyTo(Me)
        Me.Text = ExchangeJoinText.JoinLocalAccountsTitle()
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed, Sub() ShareIcons.FreeIcon(Me.Icon, _iconHandle)

        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink

        Dim root As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 1,
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(14, 12, 14, 6)
        }
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        Dim lblIntro As New Label With {
            .AutoSize = True,
            .MaximumSize = New Size(ContentWidth, 0),
            .Margin = New Padding(0, 0, 0, 8),
            .Text = ExchangeJoinText.JoinLocalAccountsIntro()
        }
        ExchangeMark.Apply(lblIntro, 32, AddressOf CompanionAssets.OpenBundledAsset)
        root.Controls.Add(lblIntro)

        _lvAccounts = New ListView With {
            .View = View.Details,
            .FullRowSelect = True,
            .HideSelection = False,
            .MultiSelect = False,
            .HeaderStyle = ColumnHeaderStyle.None,
            .Width = ContentWidth,
            .Height = 120,
            .Margin = New Padding(0, 0, 0, 10)
        }
        _lvAccounts.Columns.Add("", Me.LogicalToDeviceUnits(ContentWidth - 25))
        AddHandler _lvAccounts.SelectedIndexChanged, AddressOf OnSelectionChanged
        AddHandler _lvAccounts.DoubleClick, AddressOf OnListDoubleClick

        For Each login As String In _accounts
            Dim item As New ListViewItem(login) With {.Tag = login}
            _lvAccounts.Items.Add(item)
        Next
        If _lvAccounts.Items.Count > 0 Then
            _lvAccounts.Items(0).Selected = True
        End If
        ExchangeMark.ApplyAccounts(_lvAccounts, AddressOf CompanionAssets.OpenBundledAsset)
        root.Controls.Add(_lvAccounts)

        ' The answers ride the shared bottom row (ticket 243 T-8b, BUTTONS 2.0 sections 3 and 4): Cancel first, Connect
        ' last (it stays disabled until an account is selected), Enter connects and Escape cancels, as before.
        _btnCancel = New Button With {
            .Name = "xlap_Cancel",
            .AutoSize = True,
            .Text = Localization.T("Отмена"),
            .DialogResult = DialogResult.Cancel
        }
        _btnOk = New Button With {
            .Name = "xlap_Ok",
            .AutoSize = True,
            .Text = Localization.T("Подключить"),
            .Enabled = _lvAccounts.SelectedItems.Count > 0
        }
        AddHandler _btnOk.Click, AddressOf OnOkClicked

        _answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "xlap_Buttons", .Padding = New Padding(14, 4, 14, 10)}
        DpiLayout.AddAnswer(_answerRow, _btnOk, DialogButtonRole.Affirmative, "action.confirm")
        DpiLayout.AddAnswer(_answerRow, _btnCancel, DialogButtonRole.Dismissing, "nav.close")

        Me.Controls.Add(root)
        ' Last in the collection: the docked row takes its edge first and reads after the list in the Tab order.
        Me.Controls.Add(_answerRow)
        _answerRow.Bind(Me)

        DpiLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
        DpiLayout.CenterOnOwner(Me)
        If _lvAccounts IsNot Nothing Then _lvAccounts.Focus()
    End Sub

    Private Sub OnSelectionChanged(sender As Object, e As EventArgs)
        _btnOk.Enabled = _lvAccounts.SelectedItems.Count > 0
    End Sub

    Private Sub OnListDoubleClick(sender As Object, e As EventArgs)
        If _lvAccounts.SelectedItems.Count > 0 Then
            ConfirmChoice()
        End If
    End Sub

    Private Sub OnOkClicked(sender As Object, e As EventArgs)
        ConfirmChoice()
    End Sub

    Private Sub ConfirmChoice()
        If _lvAccounts.SelectedItems.Count = 0 Then Return
        _selectedLogin = TryCast(_lvAccounts.SelectedItems(0).Tag, String)
        If Not String.IsNullOrEmpty(_selectedLogin) Then
            Me.DialogResult = DialogResult.OK
            Me.Close()
        End If
    End Sub
End Class
