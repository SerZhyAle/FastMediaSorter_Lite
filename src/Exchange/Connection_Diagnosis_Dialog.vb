#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Diagnostics
Imports System.Drawing
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Spec 232 / DEVICE-EXCHANGE 1.1 section 9 item DA:
''' The "Why doesn't it connect?.." dialog showing what happened, what to try,
''' Windows settings integration, step walk, and privacy-safe diagnostic codes.
''' </summary>
Public Class Connection_Diagnosis_Dialog
    Inherits Form

    Private Const DialogWidth As Integer = 520

    ''' <summary>The authored client heights (96-dpi pixels): the content, a docked "copied" line and the one-line answer row
    ''' of 50 below it; the details box adds 70.</summary>
    Private Const PlainHeight As Integer = 400
    Private Const DetailsHeight As Integer = 470
    Private _rowExtra As Integer

    Private ReadOnly _result As ConnectionDiagnosisResult
    Private _currentStepIndex As Integer = 0

    Private _lblWhatHappenedHeader As Label
    Private _lblWhatHappenedText As Label
    Private _lblWhatToTryHeader As Label
    Private _lblWhatToTryText As Label
    Private _btnOpenSettings As Button
    Private _btnNextStep As Button
    Private _btnDetails As Button
    Private _pnlDetails As Panel
    Private _txtDetails As TextBox
    Private _btnCopy As Button
    Private _btnClose As Button
    Private _lblCopied As Label
    Private _answerRow As DialogButtonRow

    Public Sub New(result As ConnectionDiagnosisResult)
        _result = If(result, New ConnectionDiagnosisResult(ConnectionDiagnosisMessages.UnreachableServer, ConnectionDiagnosisTarget.Server, ConnectionDiagnosisApplies.Network))
        BuildUi()
        UpdateStepDisplay()
    End Sub

    Private Sub BuildUi()
        Me.Text = ConnectionDiagnosisLocalization.GetUiText("dialog_title")
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ClientSize = New Size(DialogWidth, PlainHeight)

        Dim layout As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 1,
            .Padding = New Padding(18, 16, 18, 4),
            .RowCount = 7,
            .AutoScroll = True
        }
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        ' 1. What happened
        _lblWhatHappenedHeader = New Label With {
            .AutoSize = True,
            .Font = New Font(Me.Font, FontStyle.Bold),
            .Text = ConnectionDiagnosisLocalization.GetUiText("what_happened"),
            .Margin = New Padding(0, 0, 0, 4)
        }
        ExchangeMark.Apply(_lblWhatHappenedHeader, 32, ButtonGlyphs.AssetOpener)
        layout.Controls.Add(_lblWhatHappenedHeader)

        _lblWhatHappenedText = New Label With {
            .AutoSize = True,
            .MaximumSize = New Size(DialogWidth - 40, 0),
            .Text = ConnectionDiagnosisLocalization.GetMessageText(_result.InitialMessage.Code),
            .Margin = New Padding(0, 0, 0, 14)
        }
        layout.Controls.Add(_lblWhatHappenedText)

        ' 2. What to try
        _lblWhatToTryHeader = New Label With {
            .AutoSize = True,
            .Font = New Font(Me.Font, FontStyle.Bold),
            .Text = ConnectionDiagnosisLocalization.GetUiText("what_to_try"),
            .Margin = New Padding(0, 0, 0, 4)
        }
        layout.Controls.Add(_lblWhatToTryHeader)

        _lblWhatToTryText = New Label With {
            .AutoSize = True,
            .MaximumSize = New Size(DialogWidth - 40, 0),
            .Margin = New Padding(0, 0, 0, 10)
        }
        layout.Controls.Add(_lblWhatToTryText)

        ' 3. Settings action button (e.g. Open Windows Settings)
        _btnOpenSettings = New Button With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(10, 4, 10, 4),
            .Text = ConnectionDiagnosisLocalization.GetUiText("btn_open_settings"),
            .Margin = New Padding(0, 0, 0, 12),
            .Visible = False
        }
        AddHandler _btnOpenSettings.Click, AddressOf OnOpenSettingsClicked
        layout.Controls.Add(_btnOpenSettings)

        ' 4. Details panel
        _pnlDetails = New Panel With {
            .Visible = False,
            .Dock = DockStyle.Top,
            .AutoSize = True,
            .Margin = New Padding(0, 0, 0, 10)
        }
        _txtDetails = New TextBox With {
            .Multiline = True,
            .ReadOnly = True,
            .ScrollBars = ScrollBars.Vertical,
            .Height = 60,
            .Width = DialogWidth - 40,
            .Text = _result.ToRedactedDiagnosticString()
        }
        _pnlDetails.Controls.Add(_txtDetails)
        layout.Controls.Add(_pnlDetails)

        ' 5. Buttons row: the shared bottom row (ticket 243 T-8b, BUTTONS 2.0 sections 3 and 4). Next step, Details and Copy
        ' are commands of the window (they read first, in this order), Close is its one dismissing answer: Escape is
        ' Close, and Enter - with no question to answer - is Close too (the safe default). The widths come from the
        ' caption and its glyph, never from a padding of the hand-built flow. "Next step" hides when there is no next
        ' step: the row keeps that button's slot at the reading start, so the other buttons do not jump.
        _btnNextStep = New Button With {.Text = ConnectionDiagnosisLocalization.GetUiText("btn_next_step")}
        AddHandler _btnNextStep.Click, AddressOf OnNextStepClicked

        _btnDetails = New Button With {.Text = ConnectionDiagnosisLocalization.GetUiText("btn_details")}
        AddHandler _btnDetails.Click, AddressOf OnDetailsClicked

        _btnCopy = New Button With {.Text = ConnectionDiagnosisLocalization.GetUiText("btn_copy")}
        AddHandler _btnCopy.Click, AddressOf OnCopyClicked

        _btnClose = New Button With {.Text = ConnectionDiagnosisLocalization.GetUiText("btn_close")}
        AddHandler _btnClose.Click, Sub() Me.Close()

        _answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "diagnosis_Buttons", .Padding = New Padding(18, 6, 18, 12)}
        _answerRow.Add(_btnNextStep, DialogButtonRole.Command, "nav.forward")
        _answerRow.Add(_btnDetails, DialogButtonRole.Command, "nav.expand")
        _answerRow.Add(_btnCopy, DialogButtonRole.Command, "action.copy")
        _answerRow.Add(_btnClose, DialogButtonRole.Dismissing, "nav.close")

        ' The "copied" line sits just above the row, where the flow used to carry it after the buttons.
        _lblCopied = New Label With {
            .Dock = DockStyle.Bottom,
            .AutoSize = False,
            .Height = 20,
            .Padding = New Padding(18, 0, 18, 0),
            .ForeColor = Color.ForestGreen,
            .Text = ConnectionDiagnosisLocalization.GetUiText("codes_copied"),
            .Visible = False
        }

        ' Docked bottom edges are taken from the LAST added control inwards and the Fill one takes what remains: the
        ' content first, then the "copied" line, the row last (at the very bottom, and last in the tab order).
        Me.Controls.Add(layout)
        Me.Controls.Add(_lblCopied)
        Me.Controls.Add(_answerRow)
        _answerRow.Bind(Me)

        ' Set initial step index from result
        If _result.FirstFailedStep.HasValue Then
            Dim idx As Integer = _result.WalkedSteps.IndexOf(_result.FirstFailedStep.Value)
            If idx >= 0 Then _currentStepIndex = idx
        End If

        ' LAST line of the builder: scaling touches only the children that exist by now (124 UX-202).
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    ''' <summary>The window is authored with the room of ONE line of the answer row; a row that wraps (a longer language,
    ''' a bigger scale) takes its extra height from the window and not from the text above it.</summary>
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        Dim oneLine As Integer = _answerRow.GetPreferredSize(Size.Empty).Height
        Dim wrapped As Integer = _answerRow.GetPreferredSize(New Size(ClientSize.Width, 0)).Height
        If wrapped > oneLine Then
            _rowExtra = wrapped - oneLine
            ClientSize = New Size(ClientSize.Width, ClientSize.Height + _rowExtra)
        End If
        DialogLayout.ClampToWorkingArea(Me)
    End Sub

    ''' <summary>The client height in device pixels: 96-dpi authored heights, plus the extra line(s) the row needs.</summary>
    Private Function ClientHeight(withDetails As Boolean) As Integer
        Return LogicalToDeviceUnits(If(withDetails, DetailsHeight, PlainHeight)) + _rowExtra
    End Function

    Private Sub UpdateStepDisplay()
        If _result.WalkedSteps.Count = 0 Then
            _lblWhatToTryHeader.Visible = False
            _lblWhatToTryText.Visible = False
            _btnNextStep.Visible = False
            _btnOpenSettings.Visible = False
            Return
        End If

        If _currentStepIndex < 0 OrElse _currentStepIndex >= _result.WalkedSteps.Count Then
            _currentStepIndex = 0
        End If

        Dim stepVal As ConnectionDiagnosisStep = _result.WalkedSteps(_currentStepIndex)
        _lblWhatToTryHeader.Visible = True
        _lblWhatToTryText.Visible = True
        _lblWhatToTryText.Text = ConnectionDiagnosisLocalization.GetStepText(stepVal)

        Dim uri As String = ConnectionDiagnosisStepInfo.WindowsSettingsUriOf(stepVal)
        _btnOpenSettings.Visible = Not String.IsNullOrEmpty(uri)

        _btnNextStep.Visible = (_result.WalkedSteps.Count > 1) AndAlso (_currentStepIndex < _result.WalkedSteps.Count - 1)
    End Sub

    Private Sub OnNextStepClicked(sender As Object, e As EventArgs)
        If _currentStepIndex < _result.WalkedSteps.Count - 1 Then
            _currentStepIndex += 1
            UpdateStepDisplay()
        End If
    End Sub

    Private Sub OnOpenSettingsClicked(sender As Object, e As EventArgs)
        If _currentStepIndex >= 0 AndAlso _currentStepIndex < _result.WalkedSteps.Count Then
            Dim stepVal As ConnectionDiagnosisStep = _result.WalkedSteps(_currentStepIndex)
            Dim uri As String = ConnectionDiagnosisStepInfo.WindowsSettingsUriOf(stepVal)
            If Not String.IsNullOrEmpty(uri) Then
                Try
                    Process.Start(New ProcessStartInfo(uri) With {.UseShellExecute = True})
                Catch ex As Exception
                    AppFileLogger.WriteLine("Connection diagnosis: failed to open settings - " & ex.Message)
                End Try
            End If
        End If
    End Sub

    Private Sub OnDetailsClicked(sender As Object, e As EventArgs)
        _pnlDetails.Visible = Not _pnlDetails.Visible
        Me.ClientSize = New Size(Me.ClientSize.Width, ClientHeight(_pnlDetails.Visible))
    End Sub

    Private Sub OnCopyClicked(sender As Object, e As EventArgs)
        Try
            Clipboard.SetText(_result.ToRedactedDiagnosticString())
            _lblCopied.Visible = True
        Catch ex As Exception
            AppFileLogger.WriteLine("Connection diagnosis: clipboard copy failed - " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Helper to run diagnosis and open the dialog modally.
    ''' </summary>
    Public Shared Async Function RunAndShowAsync(owner As IWin32Window, context As ConnectionDiagnosisContext) As Task
        Dim engine As New ConnectionDiagnosisEngine()
        Dim result As ConnectionDiagnosisResult = Await engine.DiagnoseAsync(context, CancellationToken.None).ConfigureAwait(True)
        Using dlg As New Connection_Diagnosis_Dialog(result)
            dlg.ShowDialog(owner)
        End Using
    End Function

End Class
#End If
