#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Diagnostics
Imports System.Drawing
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Spec 232 / DEVICE-EXCHANGE 0.27 section 25 item DA:
''' The "Why doesn't it connect?.." dialog showing what happened, what to try,
''' Windows settings integration, step walk, and privacy-safe diagnostic codes.
''' </summary>
Public Class Connection_Diagnosis_Dialog
    Inherits Form

    Private Const DialogWidth As Integer = 520

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
        Me.ClientSize = New Size(DialogWidth, 380)

        Dim layout As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 1,
            .Padding = New Padding(18, 16, 18, 14),
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

        ' 5. Buttons row
        Dim buttonsFlow As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom,
            .FlowDirection = FlowDirection.LeftToRight,
            .AutoSize = True,
            .Margin = New Padding(0, 10, 0, 0)
        }

        _btnNextStep = New Button With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(12, 5, 12, 5),
            .Margin = New Padding(0, 0, 8, 0),
            .Text = ConnectionDiagnosisLocalization.GetUiText("btn_next_step")
        }
        AddHandler _btnNextStep.Click, AddressOf OnNextStepClicked
        buttonsFlow.Controls.Add(_btnNextStep)

        _btnDetails = New Button With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(12, 5, 12, 5),
            .Margin = New Padding(0, 0, 8, 0),
            .Text = ConnectionDiagnosisLocalization.GetUiText("btn_details")
        }
        AddHandler _btnDetails.Click, AddressOf OnDetailsClicked
        buttonsFlow.Controls.Add(_btnDetails)

        _btnCopy = New Button With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(12, 5, 12, 5),
            .Margin = New Padding(0, 0, 8, 0),
            .Text = ConnectionDiagnosisLocalization.GetUiText("btn_copy")
        }
        AddHandler _btnCopy.Click, AddressOf OnCopyClicked
        buttonsFlow.Controls.Add(_btnCopy)

        _btnClose = New Button With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(12, 5, 12, 5),
            .Margin = New Padding(0, 0, 8, 0),
            .Text = ConnectionDiagnosisLocalization.GetUiText("btn_close")
        }
        AddHandler _btnClose.Click, Sub() Me.Close()
        buttonsFlow.Controls.Add(_btnClose)

        _lblCopied = New Label With {
            .AutoSize = True,
            .ForeColor = Color.ForestGreen,
            .Text = ConnectionDiagnosisLocalization.GetUiText("codes_copied"),
            .Visible = False,
            .Margin = New Padding(8, 6, 0, 0)
        }
        buttonsFlow.Controls.Add(_lblCopied)

        layout.Controls.Add(buttonsFlow)
        Me.Controls.Add(layout)

        ' Set initial step index from result
        If _result.FirstFailedStep.HasValue Then
            Dim idx As Integer = _result.WalkedSteps.IndexOf(_result.FirstFailedStep.Value)
            If idx >= 0 Then _currentStepIndex = idx
        End If
    End Sub

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
        If _pnlDetails.Visible Then
            Me.ClientSize = New Size(DialogWidth, 450)
        Else
            Me.ClientSize = New Size(DialogWidth, 380)
        End If
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
