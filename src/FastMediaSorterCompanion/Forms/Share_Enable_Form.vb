Option Strict On

Imports System.Drawing
Imports System.Threading.Tasks
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
    Private _answerRow As DialogButtonRow
    Private _iconHandle As IntPtr
    Private _titleFont As Font   ' the heading's bold face - owned here, disposed in the FormClosed handler

    Public Sub New()
        BuildUi()
    End Sub

    Private Sub BuildUi()
        ' Script font + text direction for the active language, before any control
        ' exists - children inherit both (013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md block A').
        UiLanguage.ApplyTo(Me)
        Me.Text = ShareText.ServerEnableTitle()
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed,
            Sub()
                ShareIcons.FreeIcon(Me.Icon, _iconHandle)
                Try
                    If _titleFont IsNot Nothing Then _titleFont.Dispose()
                Catch
                End Try
            End Sub
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
            .ColumnCount = 1, .Padding = New Padding(16, 16, 16, 4), .GrowStyle = TableLayoutPanelGrowStyle.AddRows}
        tlp.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        _titleFont = New Font(Me.Font.FontFamily, Me.Font.Size * 1.15F, FontStyle.Bold)
        _title = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 8), .MaximumSize = New Size(contentWidth, 0),
            .Font = _titleFont,
            .Text = ShareText.ServerEnableTitle()}
        _body = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 8), .MaximumSize = New Size(contentWidth, 0),
            .Text = ShareText.ServerEnableBody()}
        ' What the UAC prompt the button raises is for, and how each part is taken back - the
        ' install-trust page (ticket 037 Ф-4). The body names the undo; the page itemizes it.
        Dim lnkTrust As New LinkLabel With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 8),
            .MaximumSize = New Size(contentWidth, 0), .Text = ShareText.TrustLinkText()}
        AddHandler lnkTrust.LinkClicked, Sub() NetworkInfo.OpenInBrowser(ShareText.TrustPageUrl())
        _status = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 4), .MaximumSize = New Size(contentWidth, 0),
            .MinimumSize = New Size(contentWidth, 0), .ForeColor = Color.DimGray, .Text = ""}

        ' The answers ride the shared bottom row (ticket 243 T-8b batch C, BUTTONS 2.0 sections 3 and 4): Cancel first, the
        ' enabling action last (it keeps its Caution glyph - the UAC prompt it raises is the real gate and nothing is
        ' changed before it is accepted). Escape is Cancel, Enter is Enable as it was: the window exists to confirm that
        ' one step, and a refused or closed UAC prompt leaves everything off.
        _btnEnable = New Button With {.Text = ShareText.ServerEnableButton(), .Name = "xse_Enable"}
        _btnCancel = New Button With {.Text = ShareText.ServerEnableCancel(), .DialogResult = DialogResult.Cancel, .Name = "xse_Cancel"}
        ButtonGlyphs.Apply(_btnEnable, "action.install", ButtonGlyphRole.Caution)
        ButtonGlyphs.Apply(_btnCancel, "action.cancel", ButtonGlyphRole.Dismissing)
        AddHandler _btnEnable.Click, AddressOf OnEnableClicked
        _answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "xse_Buttons", .Padding = New Padding(16, 4, 16, 12)}
        DpiLayout.AddAnswer(_answerRow, _btnEnable, DialogButtonRole.Affirmative, "action.install")
        DpiLayout.AddAnswer(_answerRow, _btnCancel, DialogButtonRole.Dismissing, "action.cancel")

        tlp.Controls.Add(_title)
        tlp.Controls.Add(_body)
        tlp.Controls.Add(lnkTrust)
        tlp.Controls.Add(_status)

        Me.Controls.Add(tlp)
        ' Last in the collection: the docked row takes its edge first and reads after the text in the Tab order.
        Me.Controls.Add(_answerRow)
        _answerRow.Bind(Me)

        If Not ServerFeatures.CanEnable() Then
            _btnEnable.Enabled = False
            _status.Text = ShareText.ServerEnableUnavailable()
        End If

        DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout
    End Sub

    Private Async Sub OnEnableClicked(sender As Object, e As EventArgs)
        _btnEnable.Enabled = False
        _btnCancel.Enabled = False
        _status.ForeColor = Color.DimGray
        _status.Text = ShareText.ServerEnableWorking()
        Me.Refresh()

        ' Blocks on the UAC prompt (modal, brief) - the app itself stays non-elevated, and
        ' runs the wait off the UI thread so the dialog keeps painting (ticket 054 H-15).
        Dim res As ServerFeatures.EnableResult = Await Task.Run(Function() ServerFeatures.EnableViaElevation())

        ' The dialog may have been closed while the helper was out.
        If IsDisposed OrElse Disposing Then Return

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
