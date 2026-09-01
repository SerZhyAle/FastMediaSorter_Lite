Option Strict On

Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Result of the "Проверить доступ из интернета" button: runs the SFTP probe against
''' the share's external address and reports it right where the user clicked.
'''
''' Before this dialog the answer landed in the main window's bottom status strip - the
''' opposite corner from the button, which made it read as an unrelated line rather than
''' as the answer to the click, and left no room for the reasoning behind the verdict.
''' Here the verdict, the address it applies to and the next step arrive together, and
''' the caller still keeps the one-line verdict in the status strip as a lasting trace.
'''
''' The probe itself starts on <see cref="Form.Shown"/>, so the dialog appears
''' immediately with a "checking.." line instead of the window freezing for the probe's
''' few seconds. Built in code (no Designer), like every other Companion dialog.
''' </summary>
Public NotInheritable Class Share_Access_Test_Form
    Inherits Form

    Private ReadOnly _host As String
    Private ReadOnly _port As Integer
    Private ReadOnly _endpoint As String

    Private _headline As Label
    Private _target As Label
    Private _detail As Label
    Private _btnClose As Button
    Private _iconHandle As IntPtr
    Private _probeStarted As Boolean
    Private _resultLine As String = ""

    ''' <summary>The one-line verdict, for the caller's status strip. "" while the probe
    ''' has not finished - a dialog closed early leaves the strip untouched rather than
    ''' claiming a result nobody waited for.</summary>
    Public ReadOnly Property ResultLine As String
        Get
            Return _resultLine
        End Get
    End Property

    Public Sub New(host As String, port As Integer)
        _host = If(host, "")
        _port = port
        _endpoint = _host & ":" & port.ToString()
        BuildUi()
    End Sub

    Private Sub BuildUi()
        ' Script font + text direction for the active language, before any control exists -
        ' children inherit both (013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md block A').
        UiLanguage.ApplyTo(Me)
        Me.Text = ShareText.AccessTestTitle()
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed, Sub() ShareIcons.FreeIcon(Me.Icon, _iconHandle)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        ' AutoSize + TableLayoutPanel, no absolute geometry: the verdict and the detail wrap
        ' at a fixed content width and the dialog grows to fit them at any display scaling.
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink

        Const contentWidth As Integer = 460   ' text wrap width in design units (scaled by the font)

        Dim tlp As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .ColumnCount = 1,
            .Padding = New Padding(16, 16, 16, 12), .GrowStyle = TableLayoutPanelGrowStyle.AddRows}
        tlp.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        _headline = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 8),
            .MaximumSize = New Size(contentWidth, 0), .MinimumSize = New Size(contentWidth, 0),
            .Font = New Font(Me.Font.FontFamily, Me.Font.Size * 1.15F, FontStyle.Bold),
            .Text = ShareText.AccessTestRunningLine(_endpoint)}
        _target = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 8),
            .MaximumSize = New Size(contentWidth, 0), .ForeColor = SystemColors.GrayText,
            .Text = ShareText.AccessTestTargetLine(_endpoint)}
        _detail = New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 4),
            .MaximumSize = New Size(contentWidth, 0), .MinimumSize = New Size(contentWidth, 0),
            .Text = ""}

        Dim btnFlow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Anchor = AnchorStyles.Right, .Margin = New Padding(0, 8, 0, 0),
            .FlowDirection = FlowDirection.LeftToRight, .WrapContents = False}
        _btnClose = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(12, 6, 12, 6), .Margin = New Padding(0),
            .Text = Localization.T("Закрыть"), .DialogResult = DialogResult.OK}
        btnFlow.Controls.Add(_btnClose)

        tlp.Controls.Add(_headline)
        tlp.Controls.Add(_target)
        tlp.Controls.Add(_detail)
        tlp.Controls.Add(btnFlow)

        Me.Controls.Add(tlp)
        Me.AcceptButton = _btnClose
        Me.CancelButton = _btnClose

        AddHandler Me.Shown, AddressOf OnShownRunProbe

        DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout
    End Sub

    ''' <summary>Runs the probe once the window is on screen. Shown may fire again (the
    ''' dialog is re-shown by the parent), so the guard keeps it to one probe.</summary>
    Private Async Sub OnShownRunProbe(sender As Object, e As EventArgs)
        If _probeStarted Then Return
        _probeStarted = True

        Dim res As SftpProbe.ProbeResult
        Try
            res = Await SftpProbe.ProbeAsync(_host, _port)
        Catch
            ' The probe maps every network failure to a ProbeResult itself, so this is only
            ' the truly unexpected - report it plainly instead of guessing a verdict.
            If Me.IsDisposed Then Return
            _resultLine = Localization.T("Не удалось выполнить проверку.")
            _headline.ForeColor = SystemColors.ControlText
            _headline.Text = _resultLine
            _detail.Text = ""
            Return
        End Try

        If Me.IsDisposed Then Return   ' closed while probing - nothing to report to
        _resultLine = ShareText.AccessTestResultLine(res, _endpoint)
        _headline.ForeColor = HeadlineColor(res)
        _headline.Text = _resultLine
        _detail.Text = ShareText.AccessTestDetail(res)
    End Sub

    ''' <summary>Green only for a confirmed answer from the SFTP server itself; amber for
    ''' "something is there but it is not us"; plain text for the inconclusive negative -
    ''' the probe leaves from this same PC, so a red verdict would overstate what it knows.</summary>
    Private Shared Function HeadlineColor(res As SftpProbe.ProbeResult) As Color
        Select Case res
            Case SftpProbe.ProbeResult.SshOk
                Return Color.ForestGreen
            Case SftpProbe.ProbeResult.PortOpen
                Return Color.FromArgb(176, 96, 0)
            Case Else
                Return SystemColors.ControlText
        End Select
    End Function

End Class
