Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The deferred, in-app opt-in for the Android Folder Share "server features"
''' (SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md §3.4). A small modal that explains
''' what/why (a background SFTP server + one Windows Firewall exception, admin once)
''' and, on confirm, runs the single elevated firewall step via
''' <see cref="ServerFeatures.EnableViaElevation"/>. Returns <see cref="DialogResult.OK"/>
''' only when the feature was actually enabled, so callers can then reveal the Share
''' UI live. Built entirely in code (no Designer) - it is a one-off confirmation.
''' </summary>
Public Class Share_Enable_Form
    Inherits Form

    Private ReadOnly _rus As Boolean
    Private _title As Label
    Private _body As Label
    Private _status As Label
    Private _btnEnable As Button
    Private _btnCancel As Button

    Public Sub New(rus As Boolean)
        _rus = rus
        BuildUi()
    End Sub

    Private Sub BuildUi()
        Me.Text = ShareText.ServerEnableTitle(_rus)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.ClientSize = New Size(452, 268)

        _title = New Label With {
            .Left = 16, .Top = 16, .Width = 420, .Height = 24, .AutoSize = False,
            .Font = New Font(Me.Font.FontFamily, Me.Font.Size * 1.15F, FontStyle.Bold),
            .Text = ShareText.ServerEnableTitle(_rus)}
        _body = New Label With {
            .Left = 16, .Top = 48, .Width = 420, .Height = 128, .AutoSize = False,
            .Text = ShareText.ServerEnableBody(_rus)}
        _status = New Label With {
            .Left = 16, .Top = 182, .Width = 420, .Height = 36, .AutoSize = False,
            .ForeColor = Color.DimGray, .Text = ""}

        _btnEnable = New Button With {
            .Top = 226, .Height = 30, .Width = 200, .AutoSize = False,
            .Text = ShareText.ServerEnableButton(_rus)}
        _btnCancel = New Button With {
            .Top = 226, .Height = 30, .Width = 110, .AutoSize = False,
            .Text = ShareText.ServerEnableCancel(_rus), .DialogResult = DialogResult.Cancel}
        ' Right-align the pair inside the 452px client.
        _btnCancel.Left = Me.ClientSize.Width - 16 - _btnCancel.Width
        _btnEnable.Left = _btnCancel.Left - 8 - _btnEnable.Width

        AddHandler _btnEnable.Click, AddressOf OnEnableClicked

        Me.Controls.AddRange(New Control() {_title, _body, _status, _btnEnable, _btnCancel})
        Me.AcceptButton = _btnEnable
        Me.CancelButton = _btnCancel

        If Not ServerFeatures.CanEnable() Then
            _btnEnable.Enabled = False
            _status.Text = ShareText.ServerEnableUnavailable(_rus)
        End If
    End Sub

    Private Sub OnEnableClicked(sender As Object, e As EventArgs)
        _btnEnable.Enabled = False
        _btnCancel.Enabled = False
        _status.ForeColor = Color.DimGray
        _status.Text = ShareText.ServerEnableWorking(_rus)
        Me.Refresh()

        ' Blocks on the UAC prompt (modal, brief) - the app itself stays non-elevated.
        Dim res As ServerFeatures.EnableResult = ServerFeatures.EnableViaElevation()

        Select Case res
            Case ServerFeatures.EnableResult.Enabled
                Me.DialogResult = DialogResult.OK
                Me.Close()
            Case ServerFeatures.EnableResult.Declined
                _status.ForeColor = Color.Firebrick
                _status.Text = ShareText.ServerEnableDeclined(_rus)
                _btnEnable.Enabled = True
                _btnCancel.Enabled = True
            Case ServerFeatures.EnableResult.Unavailable
                _status.ForeColor = Color.Firebrick
                _status.Text = ShareText.ServerEnableUnavailable(_rus)
                _btnCancel.Enabled = True
            Case Else
                _status.ForeColor = Color.Firebrick
                _status.Text = ShareText.ServerEnableFailed(_rus)
                _btnEnable.Enabled = True
                _btnCancel.Enabled = True
        End Select
    End Sub

End Class
