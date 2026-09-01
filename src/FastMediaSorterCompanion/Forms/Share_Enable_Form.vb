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

    Private _title As Label
    Private _body As Label
    Private _status As Label
    Private _btnEnable As Button
    Private _btnCancel As Button
    Private _iconHandle As IntPtr

    Public Sub New()
        BuildUi()
    End Sub

    Private Sub BuildUi()
        ' Script font + text direction for the active language, before any control
        ' exists - children inherit both (013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md block A').
        UiLanguage.ApplyTo(Me)
        Me.Text = ShareText.ServerEnableTitle()
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed, Sub() ShareIcons.FreeIcon(Me.Icon, _iconHandle)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        ' AutoSize + a TableLayoutPanel (no absolute Left/Top/Width/Height): the labels wrap at
        ' a fixed content width and the whole dialog grows to fit at any display scaling, instead
        ' of clipping the body text or overlapping the buttons in fixed-height boxes.
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink

        Const contentWidth As Integer = 440   ' text wrap width in design units (scaled by the font)

        Dim tlp As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 1, .Padding = New Padding(16, 16, 16, 12), .GrowStyle = TableLayoutPanelGrowStyle.AddRows}
        tlp.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        _title = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 8), .MaximumSize = New Size(contentWidth, 0),
            .Font = New Font(Me.Font.FontFamily, Me.Font.Size * 1.15F, FontStyle.Bold),
            .Text = ShareText.ServerEnableTitle()}
        _body = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 8), .MaximumSize = New Size(contentWidth, 0),
            .Text = ShareText.ServerEnableBody()}
        _status = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 4), .MaximumSize = New Size(contentWidth, 0),
            .MinimumSize = New Size(contentWidth, 0), .ForeColor = Color.DimGray, .Text = ""}

        Dim btnFlow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Anchor = AnchorStyles.Right,
            .Margin = New Padding(0, 8, 0, 0), .FlowDirection = FlowDirection.LeftToRight, .WrapContents = False}
        _btnEnable = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 6, 12, 6),
            .Margin = New Padding(0, 0, 8, 0), .Text = ShareText.ServerEnableButton()}
        _btnCancel = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 6, 12, 6),
            .Margin = New Padding(0), .Text = ShareText.ServerEnableCancel(), .DialogResult = DialogResult.Cancel}
        AddHandler _btnEnable.Click, AddressOf OnEnableClicked
        btnFlow.Controls.Add(_btnEnable)
        btnFlow.Controls.Add(_btnCancel)

        tlp.Controls.Add(_title)
        tlp.Controls.Add(_body)
        tlp.Controls.Add(_status)
        tlp.Controls.Add(btnFlow)

        Me.Controls.Add(tlp)
        Me.AcceptButton = _btnEnable
        Me.CancelButton = _btnCancel

        If Not ServerFeatures.CanEnable() Then
            _btnEnable.Enabled = False
            _status.Text = ShareText.ServerEnableUnavailable()
        End If

        DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout
    End Sub

    Private Sub OnEnableClicked(sender As Object, e As EventArgs)
        _btnEnable.Enabled = False
        _btnCancel.Enabled = False
        _status.ForeColor = Color.DimGray
        _status.Text = ShareText.ServerEnableWorking()
        Me.Refresh()

        ' Blocks on the UAC prompt (modal, brief) - the app itself stays non-elevated.
        Dim res As ServerFeatures.EnableResult = ServerFeatures.EnableViaElevation()

        Select Case res
            Case ServerFeatures.EnableResult.Enabled
                Me.DialogResult = DialogResult.OK
                Me.Close()
            Case ServerFeatures.EnableResult.Declined
                _status.ForeColor = Color.Firebrick
                _status.Text = ShareText.ServerEnableDeclined()
                _btnEnable.Enabled = True
                _btnCancel.Enabled = True
            Case ServerFeatures.EnableResult.Unavailable
                _status.ForeColor = Color.Firebrick
                _status.Text = ShareText.ServerEnableUnavailable()
                _btnCancel.Enabled = True
            Case Else
                _status.ForeColor = Color.Firebrick
                _status.Text = ShareText.ServerEnableFailed()
                _btnEnable.Enabled = True
                _btnCancel.Enabled = True
        End Select
    End Sub

End Class
