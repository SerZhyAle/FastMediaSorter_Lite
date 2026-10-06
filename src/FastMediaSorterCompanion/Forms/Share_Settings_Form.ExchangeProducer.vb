Option Strict On

Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The producer half of the exchange server (ticket 148 T-4, T-6): rows in the "Сеть" group where the share
''' service is told about ONE exchange server - this PC's own or an external one - plus the per-share state
''' and the "new link" action. The server's own controls are the separate group of ticket 142
''' (Share_Settings_Form.Exchange.vb).
'''
''' Offered only where the feature can exist (the shipping gate of 133 section 8 keeps it out of a released
''' package until contract rung 2): when the exchange server component is installed, or the producer was already
''' configured here, or <c>FMS_EXCHANGE_PRODUCER=1</c>. Otherwise no row is built and the window is what it was.
'''
''' Rules held here: a row is shown or hidden by state (<see cref="ExchangeProducerModel"/> decides), never
''' disabled; the password is typed into a masked box, goes to the worker through the one SetExchange request
''' and is cleared from the box once applied - this class has no field that holds it; a disruptive action asks
''' YesNo with the default on No; nothing runs at construction (the first read waits for Shown), and the status
''' poll runs only while the window is open and the producer is on.
''' </summary>
Partial Public NotInheritable Class Share_Settings_Form

    Private ReadOnly _pxSettings As New ExchangeProducerSettings()
    Private _pxOffered As Boolean
    Private _pxStatus As WorkerStatus
    Private _pxLocal As ExchangeLocalServer = ExchangeLocalServer.NotApplicable
    Private _pxLocalPort As Integer
    Private _pxBusy As Boolean
    Private _pxLoading As Boolean
    Private _pxPolling As Boolean
    Private _pxPushed As Boolean
    Private _pxClientTouched As Boolean
    Private _pxTimer As System.Windows.Forms.Timer

    Private _cmbPxMode As ComboBox
    Private _txtPxReg As TextBox
    Private _txtPxClient As TextBox
    Private _txtPxPw As TextBox
    Private _txtPxPin As TextBox
    Private _txtPxFp As TextBox
    Private _cmbPxShare As ComboBox
    Private _rowPxReg As Control
    Private _rowPxClient As Control
    Private _rowPxPw As Control
    Private _rowPxPin As Control
    Private _rowPxFp As FlowLayoutPanel
    Private _rowPxLocal As FlowLayoutPanel
    Private _rowPxReissue As FlowLayoutPanel
    Private _lblPxState As Label
    Private _lblPxClientNote As Label
    Private _lblPxLocalNote As Label
    Private _lblPxPwHint As Label
    Private _lblPxFpNote As Label
    Private _lblPxShares As Label
    Private _lblPxReissueNote As Label
    Private _lblPxResult As Label
    Private _btnPxApply As Button

    ''' <summary>The facts about this PC's own exchange server, read off the UI thread.</summary>
    Private NotInheritable Class LocalServerFacts
        Public Property State As ExchangeLocalServer = ExchangeLocalServer.ComponentMissing
        Public Property Port As Integer
        Public Property Fingerprint As String = ""
    End Class

    ''' <summary>Where the producer rows exist at all (see the class remark).</summary>
    Private Function ProducerOffered() As Boolean
        If _pxSettings.Mode <> ExchangeMode.Off Then Return True
        If Environment.GetEnvironmentVariable("FMS_EXCHANGE_PRODUCER") = "1" Then Return True
        Try
            Return ExchangeProcess.IsAvailable()
        Catch
            Return False
        End Try
    End Function

    Private Sub BuildExchangeProducerRows(sec As CollapsibleSection)
        Try
            _pxSettings.Load()
        Catch
        End Try
        _pxOffered = ProducerOffered()
        If Not _pxOffered Then Return

        _pxLoading = True
        Try
            sec.AddBodyRow(New Label With {.AutoSize = True, .Margin = New Padding(0, 14, 0, 2),
                .Font = New Font(Me.Font, FontStyle.Bold), .Text = ExchangeProducerText.ModeLabel()})

            _cmbPxMode = New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 220,
                .Margin = New Padding(0, 2, 0, 2), .AccessibleName = ExchangeProducerText.ModeLabel()}
            _cmbPxMode.Items.Add(ExchangeProducerText.ModeName(ExchangeMode.Off))
            _cmbPxMode.Items.Add(ExchangeProducerText.ModeName(ExchangeMode.ThisPc))
            _cmbPxMode.Items.Add(ExchangeProducerText.ModeName(ExchangeMode.External))
            _cmbPxMode.SelectedIndex = CInt(_pxSettings.Mode)
            AddHandler _cmbPxMode.SelectedIndexChanged, AddressOf OnPxModeChanged
            sec.AddBodyRow(_cmbPxMode)

            _lblPxState = NoteLabel("")
            _lblPxState.Margin = New Padding(0, 4, 0, 4)
            sec.AddBodyRow(_lblPxState)

            _lblPxLocalNote = NoteLabel("")
            sec.AddBodyRow(_lblPxLocalNote)
            _rowPxLocal = XFlow()
            _rowPxLocal.Controls.Add(XButton(ExchangeText.StartButton(), AddressOf OnPxStartLocal))
            sec.AddBodyRow(_rowPxLocal)

            _txtPxReg = New TextBox With {.Width = 180, .Text = _pxSettings.RegistrationEndpoint,
                .AccessibleName = ExchangeProducerText.RegistrationLabel()}
            _rowPxReg = SettingsEditorRow(New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 6, 0),
                .Text = ExchangeProducerText.RegistrationLabel()}, _txtPxReg, New Padding(0, 4, 0, 0))
            sec.AddBodyRow(_rowPxReg)

            _txtPxClient = New TextBox With {.Width = 180, .Text = _pxSettings.ClientEndpoint,
                .AccessibleName = ExchangeProducerText.ClientLabel()}
            _pxClientTouched = _pxSettings.ClientEndpoint.Length > 0
            AddHandler _txtPxClient.TextChanged, AddressOf OnPxClientTyped
            _rowPxClient = SettingsEditorRow(New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 6, 0),
                .Text = ExchangeProducerText.ClientLabel()}, _txtPxClient, New Padding(0, 4, 0, 0))
            sec.AddBodyRow(_rowPxClient)
            _lblPxClientNote = NoteLabel("")
            _lblPxClientNote.Margin = New Padding(0, 2, 0, 6)
            sec.AddBodyRow(_lblPxClientNote)

            _txtPxPw = New TextBox With {.Width = 180, .UseSystemPasswordChar = True,
                .AccessibleName = ExchangeProducerText.PasswordLabel()}
            _rowPxPw = SettingsEditorRow(New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 6, 0),
                .Text = ExchangeProducerText.PasswordLabel()}, _txtPxPw, New Padding(0, 4, 0, 0))
            sec.AddBodyRow(_rowPxPw)
            _lblPxPwHint = NoteLabel("")
            _lblPxPwHint.Margin = New Padding(0, 2, 0, 6)
            sec.AddBodyRow(_lblPxPwHint)

            ' An optional pin for an external server: the way to trust a changed certificate, and a stronger
            ' start than pinning on first use. This PC's own server never needs it (the pin is read from it).
            _txtPxPin = New TextBox With {.Width = 180, .AccessibleName = ExchangeText.FingerprintLabel()}
            _rowPxPin = SettingsEditorRow(New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 6, 0),
                .Text = ExchangeText.FingerprintLabel()}, _txtPxPin, New Padding(0, 4, 0, 0))
            sec.AddBodyRow(_rowPxPin)

            _btnPxApply = XButton(ExchangeProducerText.ApplyButton(), AddressOf OnPxApply)
            Dim applyRow As FlowLayoutPanel = XFlow()
            applyRow.Controls.Add(_btnPxApply)
            sec.AddBodyRow(applyRow)

            _lblPxResult = NoteLabel("")
            _lblPxResult.Margin = New Padding(0, 2, 0, 4)
            sec.AddBodyRow(_lblPxResult)

            ' the pinned fingerprint - not a secret
            _rowPxFp = New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                .WrapContents = True, .Margin = New Padding(0, 2, 0, 2), .MaximumSize = New Size(HostingContentWidth, 0)}
            _rowPxFp.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 6, 0), .Text = ExchangeText.FingerprintLabel()})
            _txtPxFp = New TextBox With {.ReadOnly = True, .Width = 300, .Margin = New Padding(0, 2, 6, 0),
                .AccessibleName = ExchangeText.FingerprintLabel()}
            _rowPxFp.Controls.Add(_txtPxFp)
            _rowPxFp.Controls.Add(XButton(ExchangeText.CopyFingerprintButton(), AddressOf OnPxCopyFingerprint))
            sec.AddBodyRow(_rowPxFp)
            _lblPxFpNote = NoteLabel("")
            sec.AddBodyRow(_lblPxFpNote)

            ' one line per share, and the way to give one a new link
            _lblPxShares = NoteLabel("")
            _lblPxShares.ForeColor = SystemColors.ControlText
            _lblPxShares.Margin = New Padding(0, 4, 0, 4)
            sec.AddBodyRow(_lblPxShares)
            _rowPxReissue = XFlow()
            _rowPxReissue.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 6, 0), .Text = ExchangeProducerText.ReissueLabel()})
            _cmbPxShare = New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 180,
                .Margin = New Padding(0, 2, 6, 0), .AccessibleName = ExchangeProducerText.ReissueLabel()}
            _rowPxReissue.Controls.Add(_cmbPxShare)
            _rowPxReissue.Controls.Add(XButton(ExchangeProducerText.ReissueButton(), AddressOf OnPxReissue))
            sec.AddBodyRow(_rowPxReissue)
            _lblPxReissueNote = NoteLabel(ExchangeProducerText.ReissueNote())
            sec.AddBodyRow(_lblPxReissueNote)
        Finally
            _pxLoading = False
        End Try

        ApplyProducerModel()
        AddHandler Me.Shown, Async Sub() Await RefreshProducerAsync(includeLocal:=True)

        _pxTimer = New System.Windows.Forms.Timer With {.Interval = 5000}
        AddHandler _pxTimer.Tick, Async Sub() Await OnPxTickAsync()
        _pxTimer.Start()
    End Sub

    Private Sub DisposeProducerResources()
        Try
            If _pxTimer IsNot Nothing Then
                _pxTimer.Stop()
                _pxTimer.Dispose()
            End If
        Catch
        End Try
        Try
            If _txtPxPw IsNot Nothing Then _txtPxPw.Text = ""   ' the secret does not outlive the window
            _lblPxState?.Font?.Dispose()
        Catch
        End Try
    End Sub

    ' --- reading -------------------------------------------------------------------------------

    Private Function SelectedPxMode() As ExchangeMode
        If _cmbPxMode Is Nothing Then Return ExchangeMode.Off
        Return ExchangeProducerSettings.ClampMode(_cmbPxMode.SelectedIndex)
    End Function

    Private Shared Function ReadLocalServerFacts() As LocalServerFacts
        Dim facts As New LocalServerFacts()
        Try
            If Not ExchangeProcess.IsAvailable() Then Return facts
            Dim report As ExchangeProcess.InspectReport = ExchangeProcess.Inspect()
            If report.Answered Then
                facts.Port = report.Port
                facts.Fingerprint = report.Fingerprint
            End If
            Dim serviceExists As Boolean = ExchangeService.Exists()
            Dim svc As ServiceControl.ServiceState = If(serviceExists, ExchangeService.QueryState(), ServiceControl.ServiceState.NotInstalled)
            Dim kind As ExchangeRunKind = ExchangeText.TrayKind(ExchangeProcess.RunningPid() > 0, serviceExists, svc)
            facts.State = If(kind = ExchangeRunKind.RunningConsole OrElse kind = ExchangeRunKind.ServiceRunning,
                             ExchangeLocalServer.Running, ExchangeLocalServer.Stopped)
        Catch ex As Exception
            AppFileLogger.LogException("Exchange producer: reading the local server", ex)
        End Try
        Return facts
    End Function

    Private Async Function RefreshProducerAsync(includeLocal As Boolean) As Task
        If Not _pxOffered OrElse IsDisposed OrElse Disposing Then Return
        Dim st As WorkerStatus = Nothing
        Try
            st = Await ShareController.GetStatusAsync()
        Catch ex As Exception
            AppFileLogger.LogException("Exchange producer: reading the worker status", ex)
        End Try
        If includeLocal AndAlso SelectedPxMode() = ExchangeMode.ThisPc Then
            Dim facts As LocalServerFacts = Await Task.Run(Function() ReadLocalServerFacts())
            _pxLocal = facts.State
            _pxLocalPort = facts.Port
        End If
        If IsDisposed OrElse Disposing Then Return
        _pxStatus = st
        ApplyProducerModel()
    End Function

    Private Async Function OnPxTickAsync() As Task
        If _pxPolling OrElse _pxBusy OrElse Not _pxOffered OrElse IsDisposed OrElse Disposing Then Return
        If SelectedPxMode() = ExchangeMode.Off OrElse Not Visible OrElse Not _secNetwork.Expanded Then Return
        _pxPolling = True
        Try
            Await RefreshProducerAsync(includeLocal:=False)
        Finally
            _pxPolling = False
        End Try
    End Function

    ' --- showing -------------------------------------------------------------------------------

    Private Function PxSnapshot() As ExchangeProducerSnapshot
        Return New ExchangeProducerSnapshot With {
            .Mode = SelectedPxMode(),
            .RegistrationEndpoint = _txtPxReg.Text,
            .ClientEndpoint = _txtPxClient.Text,
            .Worker = _pxStatus,
            .LocalServer = If(SelectedPxMode() = ExchangeMode.ThisPc, _pxLocal, ExchangeLocalServer.NotApplicable)}
    End Function

    ''' <summary>Shows the rows the model says, with the texts it says. Hidden rows, not disabled ones.</summary>
    Private Sub ApplyProducerModel()
        If Not _pxOffered OrElse _cmbPxMode Is Nothing Then Return
        Dim m As ExchangeProducerModel = ExchangeProducerModel.Build(PxSnapshot())
        _rowPxReg.Visible = m.ShowRegistration
        _rowPxPin.Visible = m.ShowRegistration
        _rowPxClient.Visible = m.ShowClient
        _lblPxClientNote.Visible = m.ShowClient AndAlso m.ClientNote.Length > 0
        _lblPxClientNote.Text = m.ClientNote
        _rowPxPw.Visible = m.ShowPassword
        _lblPxPwHint.Visible = m.ShowPassword
        _lblPxPwHint.Text = ExchangeProducerText.PasswordHint(m.PasswordRequired)
        _lblPxState.Visible = m.ShowState
        _lblPxState.Text = m.StateLine
        _lblPxLocalNote.Visible = m.LocalNote.Length > 0
        _lblPxLocalNote.Text = m.LocalNote
        _rowPxLocal.Visible = m.ShowStartLocal AndAlso HasExchange
        _rowPxFp.Visible = m.ShowFingerprint
        _txtPxFp.Text = m.Fingerprint
        _lblPxFpNote.Visible = m.FingerprintNote.Length > 0
        _lblPxFpNote.Text = m.FingerprintNote
        _lblPxShares.Visible = m.ShowShares
        _lblPxShares.Text = m.SharesText
        _rowPxReissue.Visible = m.ShowReissue
        _lblPxReissueNote.Visible = m.ShowReissue

        Dim keep As String = If(TryCast(_cmbPxShare.SelectedItem, String), "")
        _cmbPxShare.Items.Clear()
        For Each name As String In m.ReissueNames
            _cmbPxShare.Items.Add(name)
        Next
        If _cmbPxShare.Items.Count > 0 Then
            Dim at As Integer = _cmbPxShare.Items.IndexOf(keep)
            _cmbPxShare.SelectedIndex = If(at >= 0, at, 0)
        End If
        _btnPxApply.Enabled = Not _pxBusy
    End Sub

    Private Sub ShowPxResult(text As String, ok As Boolean)
        If _lblPxResult Is Nothing Then Return
        _lblPxResult.ForeColor = If(ok, ThemeService.Palette.TextMuted, Color.Firebrick)
        _lblPxResult.Text = text
    End Sub

    Private Sub SetPxBusy(busy As Boolean)
        _pxBusy = busy
        If _btnPxApply IsNot Nothing Then _btnPxApply.Enabled = Not busy
        If _rowPxReissue IsNot Nothing Then _rowPxReissue.Enabled = Not busy
        UseWaitCursor = busy
    End Sub

    ' --- events --------------------------------------------------------------------------------

    Private Async Sub OnPxModeChanged(sender As Object, e As EventArgs)
        If _pxLoading Then Return
        Dim mode As ExchangeMode = SelectedPxMode()
        ShowPxResult("", True)
        ' Prefill, never overwrite: only an untouched client field is filled, from what is already known.
        If mode <> ExchangeMode.Off AndAlso Not _pxClientTouched AndAlso _txtPxClient.Text.Length = 0 Then
            _pxLoading = True
            Try
                _txtPxClient.Text = ExchangeProducerRules.SuggestClientEndpoint(mode, _txtPxReg.Text, _pxStatus?.Reachability, NetworkInfo.LocalIPv4(), _pxLocalPort)
            Finally
                _pxLoading = False
            End Try
        End If
        ApplyProducerModel()
        If mode <> ExchangeMode.Off Then Await RefreshProducerAsync(includeLocal:=True)
    End Sub

    Private Sub OnPxClientTyped(sender As Object, e As EventArgs)
        If Not _pxLoading Then _pxClientTouched = True
        If Not _pxLoading Then ApplyProducerModel()
    End Sub

    Private Async Sub OnPxStartLocal()
        If Not HasExchange Then Return
        OnXStart()
        ' The start itself runs in the group of ticket 142; look again once it has had time to come up.
        Await Task.Delay(2500)
        If Not (IsDisposed OrElse Disposing) Then Await RefreshProducerAsync(includeLocal:=True)
    End Sub

    Private Sub OnPxCopyFingerprint()
        Dim fp As String = _txtPxFp.Text
        If fp.Length = 0 Then Return
        ' Not a secret: a plain copy, through the one clipboard door.
        Dim data As New DataObject()
        data.SetText(fp, TextDataFormat.UnicodeText)
        If SecureClipboard.TrySetDataObject(data) Then ShowPxResult(ExchangeText.FingerprintCopied(), True)
    End Sub

    ''' <summary>Copies what is typed into <see cref="_pxSettings"/> (not saved yet).</summary>
    Private Sub CollectPxSettings()
        _pxSettings.Mode = SelectedPxMode()
        _pxSettings.RegistrationEndpoint = _txtPxReg.Text.Trim()
        _pxSettings.ClientEndpoint = _txtPxClient.Text.Trim()
    End Sub

    Private Function PxPasswordRequired() As Boolean
        Return _pxStatus Is Nothing OrElse _pxStatus.Exchange Is Nothing
    End Function

    Private Async Sub OnPxApply()
        If _pxBusy Then Return
        CollectPxSettings()
        Dim password As String = _txtPxPw.Text
        Dim problem As ExchangeProducerProblem = ExchangeProducerRules.Validate(_pxSettings, password, PxPasswordRequired())
        If problem <> ExchangeProducerProblem.None Then
            ShowPxResult(ExchangeProducerText.Problem(problem, password), False)
            Return
        End If
        Await SendProducerAsync(password, Nothing, ExchangeProducerText.Applied())
    End Sub

    ''' <summary>The one send of a SetExchange for both the apply and the new link. Saves the settings first (they
    ''' are what the user chose, whether or not the worker is ready), reads the worker's readiness, adds this PC's
    ''' own server's port and pin when that is the mode, and reports in one result line.</summary>
    Private Async Function SendProducerAsync(password As String, reissueName As String, okText As String) As Task
        SetPxBusy(True)
        Try
            _pxSettings.Save()
            Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
            Dim localPort As Integer = 0
            Dim pin As String = If(_pxSettings.Mode = ExchangeMode.External, _txtPxPin.Text.Trim(), "")
            If _pxSettings.Mode = ExchangeMode.ThisPc Then
                Dim facts As LocalServerFacts = Await Task.Run(Function() ReadLocalServerFacts())
                _pxLocal = facts.State
                _pxLocalPort = facts.Port
                localPort = facts.Port
                pin = facts.Fingerprint
            End If
            Dim names As String() = If(String.IsNullOrEmpty(reissueName), Nothing, New String() {reissueName})
            Dim result As ExchangeApplyResult = Await ExchangeProducerController.ApplyAsync(_pxSettings, localPort, password, pin, names, st)
            If IsDisposed OrElse Disposing Then Return
            Select Case result.Outcome
                Case ExchangeApplyOutcome.Applied
                    _pxPushed = True
                    _txtPxPw.Text = ""
                    _pxStatus = If(result.Status, st)
                    ShowPxResult(okText, True)
                Case ExchangeApplyOutcome.NoWorker, ExchangeApplyOutcome.StaleWorker
                    _pxStatus = st
                    ShowPxResult(result.Text, False)
                Case Else
                    _pxStatus = st
                    ShowPxResult(ExchangeProducerText.ApplyFailed(result.Text), False)
            End Select
            ApplyProducerModel()
            If result.Outcome = ExchangeApplyOutcome.Applied Then Await RefreshProducerAsync(includeLocal:=False)
        Catch ex As Exception
            AppFileLogger.LogException("Exchange producer: applying", ex)
            If Not (IsDisposed OrElse Disposing) Then ShowPxResult(ExchangeProducerText.Unexpected(), False)
        Finally
            If Not (IsDisposed OrElse Disposing) Then SetPxBusy(False)
        End Try
    End Function

    Private Async Sub OnPxReissue()
        If _pxBusy Then Return
        Dim name As String = TryCast(_cmbPxShare.SelectedItem, String)
        If String.IsNullOrEmpty(name) Then Return
        ' Names the consequence, default on No (a new link makes every earlier QR and file useless through the relay).
        If MessageBox.Show(Me, ExchangeProducerText.ReissueQuestion(name), Me.Text, MessageBoxButtons.YesNo,
                           MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        CollectPxSettings()
        ' A request built from an invalid endpoint would read as "off" - never send one.
        Dim problem As ExchangeProducerProblem = ExchangeProducerRules.Validate(_pxSettings, "", False)
        If problem <> ExchangeProducerProblem.None Then
            ShowPxResult(ExchangeProducerText.Problem(problem, ""), False)
            Return
        End If
        Await SendProducerAsync("", name, ExchangeProducerText.Reissued(name))
    End Sub
End Class
