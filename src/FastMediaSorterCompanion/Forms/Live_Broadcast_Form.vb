Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' Ticket 199 sections 2, 4, 6: the "Live broadcast" window of the Share Manager - the one place a
' broadcast starts (safety rule 1: only by the user's explicit act, never from a cast, the network
' or logon), the visible on-air state (safety rule 2: what is captured and how many listeners), the
' level meter and mute/gain of the microphone (producer rule 6), and the three hand-off channels of
' section 5 (clipboard text, .fmsbcast file, QR image) once the broadcast is really on air.
'
' The window owns no capture logic: the controller (LiveBroadcastController, driven by
' LiveBroadcastHost's ports) is the whole engine, and the window is buttons, rows and labels. It is
' reachable only while the gate is open (LiveBroadcastHost.Offered) - the tray item and the settings
' row check the same thing, and LiveBroadcastGateGuardTests holds the surface rule.

Public Class Live_Broadcast_Form
    Inherits Form

    Private _iconHandle As IntPtr
    Private ReadOnly _controller As LiveBroadcastController

    Private _lblSource As Label
    Private _rbMicrophone As RadioButton
    Private _rbSystemAudio As RadioButton
    Private _rbScreen As RadioButton
    Private _rbCamera As RadioButton
    Private _lblScreen As Label
    Private _cboMonitor As ComboBox
    Private _chkCursor As CheckBox
    Private _chkNoSound As CheckBox
    Private _monitors As List(Of LiveScreenMonitor)
    Private _screenRows As TableLayoutPanel
    Private _lblCamera As Label
    Private _cboCamera As ComboBox
    Private _lblCameraMode As Label
    Private _cboCameraMode As ComboBox
    Private _cameras As List(Of LiveCameraDevice)
    Private _cameraModes As List(Of LiveCameraMode)
    Private _cameraRows As TableLayoutPanel
    Private _videoRows As TableLayoutPanel
    Private _cameraFilled As Boolean
    Private _chkAllowLanViewers As CheckBox
    Private _txtTitle As TextBox
    Private _txtPassword As TextBox
    Private _btnGenPassword As Button
    Private _numPort As NumericUpDown
    Private _numBitrate As NumericUpDown
    Private _gainTrack As TrackBar
    Private _gainValue As Label
    Private _meterPanel As Panel
    Private _btnStart As Button
    Private _btnStop As Button
    Private _lblState As Label
    Private _lblListeners As Label
    Private _btnCopyLink As Button
    Private _btnCopyCode As Button
    Private _btnSaveFile As Button
    Private _btnShowQr As Button
    Private _btnCast As Button
    Private _activeCastReceiverId As String = ""
    Private _activeCastReceiverName As String = ""
    Private _levelPeak As Double
    Private _onAirChip As LiveOnAirChip
    Private _fileNumber As Integer
    Private _systemAudioWarn As Label

    Public Sub New()
        UiLanguage.ApplyTo(Me)
        _controller = LiveBroadcastHost.Controller()
        AddHandler _controller.ListenersChanged, Sub() SafeUi(AddressOf RefreshOnAir)
        AddHandler _controller.AudioLevel, Sub(peak) SafeUi(
            Sub()
                If peak > _levelPeak Then
                    _levelPeak = peak
                Else
                    _levelPeak = _levelPeak * 0.85 + peak * 0.15
                End If
                _meterPanel?.Invalidate()
            End Sub)
        BuildUi()
    End Sub

    Private Sub SafeUi(action As Action)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        Try
            BeginInvoke(action)
        Catch
        End Try
    End Sub

    Private Sub BuildUi()
        Me.Text = Localization.T("Живая трансляция")
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed, Sub() ShareIcons.FreeIcon(Me.Icon, _iconHandle)
        Me.FormBorderStyle = FormBorderStyle.Sizable
        Me.StartPosition = FormStartPosition.CenterScreen
        Me.MinimumSize = New Size(580, 480)
        Me.Size = New Size(620, 520)
        AddHandler Me.FormClosed, Sub() StopChip()

        Dim pnl As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .Padding = New Padding(16),
            .ColumnCount = 1,
            .RowCount = 10
        }
        For i As Integer = 0 To 9
            pnl.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Next
        pnl.RowStyles(7) = New RowStyle(SizeType.Percent, 100.0F)

        ' --- the source ---
        _lblSource = New Label With {.AutoSize = True, .Text = Localization.T("Источник:")}
        pnl.Controls.Add(_lblSource, 0, 0)

        Dim sourceFlow As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.LeftToRight}
        _rbMicrophone = New RadioButton With {.AutoSize = True, .Text = Localization.T("Микрофон"), .Checked = True}
        _rbSystemAudio = New RadioButton With {.AutoSize = True, .Text = Localization.T("Системный звук")}
        _rbScreen = New RadioButton With {.AutoSize = True, .Text = Localization.T("Экран")}
        _rbCamera = New RadioButton With {.AutoSize = True, .Text = Localization.T("Камера")}
        sourceFlow.Controls.Add(_rbMicrophone)
        sourceFlow.Controls.Add(_rbSystemAudio)
        sourceFlow.Controls.Add(_rbScreen)
        sourceFlow.Controls.Add(_rbCamera)
        pnl.Controls.Add(sourceFlow, 0, 1)

        ' --- the settings rows ---
        Dim rows As New TableLayoutPanel With {
            .AutoSize = True,
            .ColumnCount = 2,
            .RowCount = 5,
            .Margin = New Padding(0, 8, 0, 4)
        }
        rows.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 170))
        rows.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        rows.Controls.Add(New Label With {.AutoSize = True, .Text = Localization.T("Название:")}, 0, 0)
        _txtTitle = New TextBox With {.Dock = DockStyle.Fill}
        rows.Controls.Add(_txtTitle, 1, 0)

        rows.Controls.Add(New Label With {.AutoSize = True, .Text = Localization.T("Порт:")}, 0, 1)
        Dim portFlow As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.LeftToRight}
        _numPort = New NumericUpDown With {
            .Minimum = LiveProducerSettings.MinPort,
            .Maximum = LiveProducerSettings.MaxPort,
            .Value = LiveProducerContract.DefaultAudioPort,
            .Width = 90
        }
        portFlow.Controls.Add(_numPort)
        rows.Controls.Add(portFlow, 1, 1)

        rows.Controls.Add(New Label With {.AutoSize = True, .Text = Localization.T("Битрейт, кбит/с:")}, 0, 2)
        _numBitrate = New NumericUpDown With {
            .Minimum = 32,
            .Maximum = 320,
            .Value = LiveProducerContract.DefaultAudioBitrateKbps,
            .Width = 90
        }
        rows.Controls.Add(_numBitrate, 1, 2)

        rows.Controls.Add(New Label With {.AutoSize = True, .Text = Localization.T("Усиление микрофона:")}, 0, 3)
        Dim gainFlow As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.LeftToRight}
        _gainTrack = New TrackBar With {
            .Minimum = 10,
            .Maximum = 40,
            .Value = 10,
            .TickFrequency = 10,
            .Width = 140
        }
        AddHandler _gainTrack.ValueChanged, Sub() UpdateGainLabel()
        _gainValue = New Label With {.AutoSize = True, .Margin = New Padding(6, 4, 0, 0)}
        gainFlow.Controls.Add(_gainTrack)
        gainFlow.Controls.Add(_gainValue)
        rows.Controls.Add(gainFlow, 1, 3)
        UpdateGainLabel()

        rows.Controls.Add(New Label With {.AutoSize = True, .Text = Localization.T("Пароль:")}, 0, 4)
        Dim pwdFlow As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.LeftToRight}
        _txtPassword = New TextBox With {.Width = 140}
        _btnGenPassword = New Button With {.AutoSize = True, .Text = Localization.T("Случайный")}
        AddHandler _btnGenPassword.Click, Sub()
            _txtPassword.Text = LiveProducerTokens.NewId().Substring(0, 8)
        End Sub
        pwdFlow.Controls.Add(_txtPassword)
        pwdFlow.Controls.Add(_btnGenPassword)
        rows.Controls.Add(pwdFlow, 1, 4)
        pnl.Controls.Add(rows, 0, 2)

        ' --- the video rows (B2 screen, B3 camera): the pickers, the cursor toggle, the no-sound
        ' tick. One stack, one visible half per source kind - both are the video modes of section 3.
        Dim videoRows As New TableLayoutPanel With {
            .AutoSize = True,
            .ColumnCount = 1,
            .RowCount = 3,
            .Margin = New Padding(0, 4, 0, 4),
            .Visible = False
        }
        For i As Integer = 0 To 2
            videoRows.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Next

        Dim screenRows As New TableLayoutPanel With {
            .AutoSize = True,
            .ColumnCount = 2,
            .RowCount = 2,
            .Margin = New Padding(0),
            .Visible = False
        }
        screenRows.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 170))
        screenRows.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        _lblScreen = New Label With {.AutoSize = True, .Text = Localization.T("Экран:")}
        _cboMonitor = New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 260}
        FillMonitorPicker()
        screenRows.Controls.Add(_lblScreen, 0, 0)
        screenRows.Controls.Add(_cboMonitor, 1, 0)

        Dim cursorFlow As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.LeftToRight}
        _chkCursor = New CheckBox With {.AutoSize = True, .Text = Localization.T("Показывать курсор"), .Checked = True}
        cursorFlow.Controls.Add(_chkCursor)
        screenRows.Controls.Add(New Label With {.AutoSize = True}, 0, 1)
        screenRows.Controls.Add(cursorFlow, 1, 1)
        videoRows.Controls.Add(screenRows, 0, 0)
        _screenRows = screenRows

        ' --- the camera rows (B3): the device and the mode picker of section 4 ------
        Dim cameraRows As New TableLayoutPanel With {
            .AutoSize = True,
            .ColumnCount = 2,
            .RowCount = 2,
            .Margin = New Padding(0),
            .Visible = False
        }
        cameraRows.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 170))
        cameraRows.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        _lblCamera = New Label With {.AutoSize = True, .Text = Localization.T("Камера:")}
        _cboCamera = New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 260}
        cameraRows.Controls.Add(_lblCamera, 0, 0)
        cameraRows.Controls.Add(_cboCamera, 1, 0)

        _lblCameraMode = New Label With {.AutoSize = True, .Text = Localization.T("Режим:")}
        _cboCameraMode = New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 260}
        cameraRows.Controls.Add(_lblCameraMode, 0, 1)
        cameraRows.Controls.Add(_cboCameraMode, 1, 1)
        videoRows.Controls.Add(cameraRows, 0, 1)
        _cameraRows = cameraRows

        ' The no-sound tick belongs to both video sources: VIDEO_AUDIO's sound is the microphone
        ' (one source kind per broadcast), VIDEO_ONLY drops it.
        Dim noSoundFlow As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.LeftToRight}
        _chkNoSound = New CheckBox With {.AutoSize = True, .Text = Localization.T("Только видео, без звука")}
        noSoundFlow.Controls.Add(_chkNoSound)
        videoRows.Controls.Add(noSoundFlow, 0, 2)

        pnl.Controls.Add(videoRows, 0, 3)
        _videoRows = videoRows
        AddHandler _rbScreen.CheckedChanged, Sub() UpdateVideoRowsVisibility()
        AddHandler _rbCamera.CheckedChanged, Sub() UpdateVideoRowsVisibility()
        AddHandler _rbScreen.CheckedChanged, Sub() UpdateSystemAudioWarning()
        AddHandler _rbSystemAudio.CheckedChanged, Sub() UpdateSystemAudioWarning()
        AddHandler _cboCamera.SelectedIndexChanged, Sub() FillCameraModePicker()

        ' --- the level meter and the audio rows ---
        ' The live level meter (section 4): the producer's own view of the gain it applies.
        Dim audioFlow As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.LeftToRight, .Margin = New Padding(0, 4, 0, 0)}
        _meterPanel = New Panel With {.Width = 220, .Height = 18, .BackColor = SystemColors.Control}
        AddHandler _meterPanel.Paint, AddressOf PaintMeter
        audioFlow.Controls.Add(_meterPanel)
        pnl.Controls.Add(audioFlow, 0, 4)

        _chkAllowLanViewers = New CheckBox With {
            .AutoSize = True,
            .Text = Localization.T("Разрешить зрителям в моей сети"),
            .Visible = False
        }
        ' Section 3.4: the LAN video listener is off by default; the tick is the video sources' own
        ' row (video without it starts exchange-only, 173 - not built, so a start is refused until
        ' the user ticks it). The camera's picture is exactly the screen's "anyone on the segment".
        AddHandler _rbScreen.CheckedChanged, Sub() UpdateAllowLanVisibility()
        AddHandler _rbCamera.CheckedChanged, Sub() UpdateAllowLanVisibility()
        pnl.Controls.Add(_chkAllowLanViewers, 0, 5)

        Dim warn As New Label With {
            .AutoSize = True,
            .MaximumSize = New Size(540, 0),
            .ForeColor = Color.DimGray,
            .Text = Localization.T("Системный звук захватывает всё, что воспроизводит этот ПК, включая саму эту трансляцию, если слушать её на этом же ПК.")
        }
        pnl.Controls.Add(warn, 0, 6)
        _systemAudioWarn = warn
        UpdateSystemAudioWarning()

        ' --- the start/stop pair and the state lines ---
        Dim buttonFlow As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.LeftToRight, .Margin = New Padding(0, 10, 0, 0)}
        _btnStart = New Button With {.AutoSize = True, .Text = Localization.T("Начать трансляцию")}
        AddHandler _btnStart.Click, AddressOf OnStart
        _btnStop = New Button With {.AutoSize = True, .Text = Localization.T("Остановить трансляцию"), .Enabled = False}
        AddHandler _btnStop.Click, Sub() OnStop()
        buttonFlow.Controls.Add(_btnStart)
        buttonFlow.Controls.Add(_btnStop)
        pnl.Controls.Add(buttonFlow, 0, 7)

        Dim stateFlow As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.TopDown}
        _lblState = New Label With {.AutoSize = True, .Text = Localization.T("Трансляция не начата.")}
        _lblListeners = New Label With {.AutoSize = True, .Visible = False}
        stateFlow.Controls.Add(_lblState)
        stateFlow.Controls.Add(_lblListeners)
        pnl.Controls.Add(stateFlow, 0, 8)

        ' --- the hand-off buttons, on-air only ---
        Dim handoffFlow As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.LeftToRight, .Margin = New Padding(0, 6, 0, 0), .WrapContents = True}
        _btnCopyLink = New Button With {.AutoSize = True, .Text = Localization.T("Скопировать ссылку на трансляцию"), .Enabled = False}
        AddHandler _btnCopyLink.Click, AddressOf OnCopyLink
        _btnCopyCode = New Button With {.AutoSize = True, .Text = Localization.T("Скопировать код для импорта"), .Enabled = False}
        AddHandler _btnCopyCode.Click, AddressOf OnCopyCode
        _btnSaveFile = New Button With {.AutoSize = True, .Text = Localization.T("Сохранить файл для импорта"), .Enabled = False}
        AddHandler _btnSaveFile.Click, AddressOf OnSaveFile
        _btnShowQr = New Button With {.AutoSize = True, .Text = Localization.T("Показать QR-код"), .Enabled = False}
        AddHandler _btnShowQr.Click, AddressOf OnShowQr
        _btnCast = New Button With {.AutoSize = True, .Text = Localization.T("Воспроизвести на другом устройстве.."), .Enabled = False}
        AddHandler _btnCast.Click, AddressOf OnCastClick
        handoffFlow.Controls.Add(_btnCopyLink)
        handoffFlow.Controls.Add(_btnCopyCode)
        handoffFlow.Controls.Add(_btnSaveFile)
        handoffFlow.Controls.Add(_btnShowQr)
        handoffFlow.Controls.Add(_btnCast)
        pnl.Controls.Add(handoffFlow, 0, 9)

        Me.Controls.Add(pnl)
        LoadSettingsIntoUi()
    End Sub

    Private Sub UpdateGainLabel()
        If _gainValue IsNot Nothing Then
            _gainValue.Text = "x" & (CurrentGain()).ToString("0.0", Globalization.CultureInfo.InvariantCulture)
        End If
    End Sub

    ''' <summary>The monitor picker's rows, in the enumeration order the stored index refers to.</summary>
    Private Sub FillMonitorPicker()
        If _cboMonitor Is Nothing Then Return
        _monitors = ScreenCaptureSource.EnumerateMonitors()
        _cboMonitor.Items.Clear()
        For Each m As LiveScreenMonitor In _monitors
            Dim text As String = Localization.TF("Экран {0} - {1}x{2}", m.Index + 1, m.Width, m.Height)
            If m.IsPrimary Then text &= " " & Localization.T("(основной)")
            _cboMonitor.Items.Add(text)
        Next
        If _cboMonitor.Items.Count > 0 Then _cboMonitor.SelectedIndex = 0
    End Sub

    Private Sub UpdateVideoRowsVisibility()
        Dim screen As Boolean = _rbScreen IsNot Nothing AndAlso _rbScreen.Checked
        Dim camera As Boolean = _rbCamera IsNot Nothing AndAlso _rbCamera.Checked
        If _videoRows IsNot Nothing Then _videoRows.Visible = screen OrElse camera
        If _screenRows IsNot Nothing Then _screenRows.Visible = screen
        If _cameraRows IsNot Nothing Then _cameraRows.Visible = camera
        ' The camera's device list is read only when the user asks for the camera (safety rule 1's
        ' spirit: nothing device-shaped happens until the user's act; the constructor enumerates
        ' nothing).
        If camera AndAlso Not _cameraFilled Then FillCameraPicker()
    End Sub

    Private Sub UpdateAllowLanVisibility()
        If _chkAllowLanViewers Is Nothing Then Return
        Dim video As Boolean = (_rbScreen IsNot Nothing AndAlso _rbScreen.Checked) OrElse
                               (_rbCamera IsNot Nothing AndAlso _rbCamera.Checked)
        _chkAllowLanViewers.Visible = video
    End Sub

    ''' <summary>The device picker's rows: the cameras the frame-source groups expose, metadata
    ''' only (no capture session). The stored row is restored where the count still holds.</summary>
    Private Sub FillCameraPicker()
        If _cboCamera Is Nothing Then Return
        _cameraFilled = True
        _cameras = CameraCaptureSource.EnumerateCameras()
        If _cameras Is Nothing Then _cameras = New List(Of LiveCameraDevice)()
        Dim selected As Integer = _cboCamera.SelectedIndex
        _cboCamera.Items.Clear()
        For Each d As LiveCameraDevice In _cameras
            _cboCamera.Items.Add(If(d.Name, Localization.T("Камера")))
        Next
        If _cboCamera.Items.Count > 0 Then
            Dim settings As LiveProducerSettings = LiveProducerSettings.Load(Function(key) GetSetting(App_name, Second_App_Name, key, ""))
            _cboCamera.SelectedIndex = Math.Max(0, Math.Min(settings.CameraDeviceIndex, _cboCamera.Items.Count - 1))
            If selected >= 0 AndAlso _cboCamera.SelectedIndex <> selected Then
                _cboCamera.SelectedIndex = Math.Min(selected, _cboCamera.Items.Count - 1)
            End If
        End If
        FillCameraModePicker()
    End Sub

    ''' <summary>The mode picker's rows: "Авто" (the automatic best of LiveCameraModes) plus the
    ''' device's own list. Reading the modes opens the selected camera briefly and releases it - a
    ''' user act (the picker row), behind the gate; the broadcast start reads them again itself.</summary>
    Private Sub FillCameraModePicker()
        If _cboCameraMode Is Nothing OrElse _cboCamera Is Nothing Then Return
        Dim selected As Integer = _cboCameraMode.SelectedIndex
        _cboCameraMode.Items.Clear()
        _cboCameraMode.Items.Add(Localization.T("Авто"))
        _cameraModes = New List(Of LiveCameraMode)()
        If _cboCamera.SelectedIndex >= 0 Then
            _cameraModes = CameraCaptureSource.EnumerateModes(_cboCamera.SelectedIndex)
            If _cameraModes IsNot Nothing Then
                For Each m As LiveCameraMode In _cameraModes
                    _cboCameraMode.Items.Add(Localization.TF("{0}x{1}, {2} к/с", m.Width, m.Height, m.FramesPerSecond))
                Next
            End If
        End If
        If selected < 0 Then
            Dim settings As LiveProducerSettings = LiveProducerSettings.Load(Function(key) GetSetting(App_name, Second_App_Name, key, ""))
            selected = settings.CameraModeIndex + 1
        End If
        _cboCameraMode.SelectedIndex = Math.Max(0, Math.Min(selected, _cboCameraMode.Items.Count - 1))
    End Sub

    ''' <summary>Section 4's warning sentence: shown only where Windows cannot cut the feedback loop
    ''' (pre-2004 builds); where the process-loopback exclusion exists the loop is already cut.</summary>
    Private Sub UpdateSystemAudioWarning()
        If _systemAudioWarn Is Nothing Then Return
        Dim systemAudio As Boolean = _rbSystemAudio IsNot Nothing AndAlso _rbSystemAudio.Checked
        _systemAudioWarn.Visible = systemAudio AndAlso Not WasapiAudioSource.SupportsProcessLoopbackExclusion
    End Sub

    Private Function CurrentGain() As Double
        If _gainTrack Is Nothing Then Return 1.0
        Return _gainTrack.Value / 10.0
    End Function

    Private Sub PaintMeter(sender As Object, e As PaintEventArgs)
        If e Is Nothing OrElse e.Graphics Is Nothing Then Return
        Dim g As Graphics = e.Graphics
        g.Clear(SystemColors.Control)
        Dim width As Integer = If(_meterPanel Is Nothing, 0, _meterPanel.Width)
        Dim filled As Integer = CInt(Math.Max(0.0, Math.Min(1.0, _levelPeak)) * (width - 4))
        Using brush As New SolidBrush(Color.Firebrick)
            g.FillRectangle(brush, 2, 2, filled, If(_meterPanel Is Nothing, 14, _meterPanel.Height - 4))
        End Using
    End Sub

    ' --- the settings rows load and save through the same hive the controller reads -------------

    Private Sub LoadSettingsIntoUi()
        Dim settings As LiveProducerSettings = LiveProducerSettings.Load(Function(key) GetSetting(App_name, Second_App_Name, key, ""))
        If _numPort IsNot Nothing Then _numPort.Value = LiveProducerSettings.ClampPort(settings.AudioPort)
        If _numBitrate IsNot Nothing Then
            _numBitrate.Value = Math.Max(CInt(_numBitrate.Minimum), Math.Min(CInt(_numBitrate.Maximum), settings.AudioBitrateKbps))
        End If
        If _txtTitle IsNot Nothing Then _txtTitle.Text = settings.Title
        If _chkAllowLanViewers IsNot Nothing Then _chkAllowLanViewers.Checked = settings.AllowLanVideoViewers
        If _gainTrack IsNot Nothing Then
            _gainTrack.Value = CInt(Math.Max(_gainTrack.Minimum, Math.Min(_gainTrack.Maximum, CInt(settings.MicrophoneGain * 10))))
            UpdateGainLabel()
        End If
        If _chkCursor IsNot Nothing Then _chkCursor.Checked = settings.ScreenCursor
        If _cboMonitor IsNot Nothing AndAlso _cboMonitor.Items.Count > 0 Then
            _cboMonitor.SelectedIndex = Math.Min(settings.ScreenMonitorIndex, _cboMonitor.Items.Count - 1)
        End If
        If _cboCameraMode IsNot Nothing AndAlso _cboCameraMode.Items.Count > 0 Then
            _cboCameraMode.SelectedIndex = Math.Max(0, Math.Min(settings.CameraModeIndex + 1, _cboCameraMode.Items.Count - 1))
        End If
    End Sub

    Private Sub SaveSettingsFromUi()
        Dim settings As LiveProducerSettings = LiveProducerSettings.Load(Function(key) GetSetting(App_name, Second_App_Name, key, ""))
        If _numPort IsNot Nothing Then settings.AudioPort = CInt(_numPort.Value)
        If _numBitrate IsNot Nothing Then settings.AudioBitrateKbps = CInt(_numBitrate.Value)
        If _txtTitle IsNot Nothing Then settings.Title = _txtTitle.Text.Trim()
        If _chkAllowLanViewers IsNot Nothing Then settings.AllowLanVideoViewers = _chkAllowLanViewers.Checked
        If _gainTrack IsNot Nothing Then settings.MicrophoneGain = CurrentGain()
        If _chkCursor IsNot Nothing Then settings.ScreenCursor = _chkCursor.Checked
        If _cboMonitor IsNot Nothing Then settings.ScreenMonitorIndex = Math.Max(0, _cboMonitor.SelectedIndex)
        If _cboCamera IsNot Nothing Then settings.CameraDeviceIndex = Math.Max(0, _cboCamera.SelectedIndex)
        ' Combo row 0 is "Авто"; the stored -1 is exactly that.
        If _cboCameraMode IsNot Nothing Then settings.CameraModeIndex = _cboCameraMode.SelectedIndex - 1
        settings.Save(Sub(key, value) SaveSetting(App_name, Second_App_Name, key, value))
    End Sub

    ' --- start and stop ------------------------------------------------------------------------

    Private Sub OnStart(sender As Object, e As EventArgs)
        SaveSettingsFromUi()

        ' The mode follows the source (section 3): the two audio sources are AUDIO_ONLY; the video
        ' sources (screen, camera) are VIDEO_AUDIO - their sound is the microphone - or VIDEO_ONLY
        ' behind the no-sound tick.
        Dim kind As LiveProducerSourceKind
        Dim mode As String
        If _rbScreen.Checked Then
            kind = LiveProducerSourceKind.Screen
            mode = If(_chkNoSound.Checked, "VIDEO_ONLY", "VIDEO_AUDIO")
        ElseIf _rbCamera.Checked Then
            kind = LiveProducerSourceKind.Camera
            mode = If(_chkNoSound.Checked, "VIDEO_ONLY", "VIDEO_AUDIO")
        ElseIf _rbSystemAudio.Checked Then
            kind = LiveProducerSourceKind.SystemAudio
            mode = "AUDIO_ONLY"
        Else
            kind = LiveProducerSourceKind.Microphone
            mode = "AUDIO_ONLY"
        End If
        Dim request As New LiveStartRequest With {
            .Kind = kind,
            .Mode = mode,
            .Title = If(_txtTitle.Text.Trim(), ""),
            .Password = If(_txtPassword IsNot Nothing, _txtPassword.Text.Trim(), "")
        }

        Dim result As LiveStartResult = _controller.Start(request)
        If Not result.Ok Then
            ShowRefusal(result)
            Return
        End If

        _btnStart.Enabled = False
        _btnStop.Enabled = True
        _rbMicrophone.Enabled = False
        _rbSystemAudio.Enabled = False
        _rbScreen.Enabled = False
        _rbCamera.Enabled = False
        _cboMonitor.Enabled = False
        _chkCursor.Enabled = False
        _chkNoSound.Enabled = False
        _cboCamera.Enabled = False
        _cboCameraMode.Enabled = False
        _numPort.Enabled = False
        _numBitrate.Enabled = False
        _chkAllowLanViewers.Enabled = False
        If _txtPassword IsNot Nothing Then _txtPassword.Enabled = False
        If _btnGenPassword IsNot Nothing Then _btnGenPassword.Enabled = False
        If _btnCopyLink IsNot Nothing Then _btnCopyLink.Enabled = True
        _btnCopyCode.Enabled = True
        _btnSaveFile.Enabled = True
        _btnShowQr.Enabled = True
        If _btnCast IsNot Nothing Then
            _btnCast.Enabled = True
            _btnCast.Text = Localization.T("Воспроизвести на другом устройстве..")
        End If
        _activeCastReceiverId = ""
        _activeCastReceiverName = ""
        RefreshOnAir()
        StartChip()
    End Sub

    Private Sub OnStop()
        If Not String.IsNullOrEmpty(_activeCastReceiverId) Then
            Dim receiverId As String = _activeCastReceiverId
            Dim broadcastId As String = _controller.State.BroadcastId
            Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
            If agent IsNot Nothing AndAlso Not String.IsNullOrEmpty(broadcastId) Then
                Try
                    Task.Run(Function() agent.CastStopAsync(broadcastId, receiverId, CancellationToken.None))
                Catch
                End Try
            End If
            _activeCastReceiverId = ""
            _activeCastReceiverName = ""
        End If
        _controller.Stop()
        BroadcastStopped()
    End Sub

    Private Sub BroadcastStopped()
        _btnStart.Enabled = True
        _btnStop.Enabled = False
        _rbMicrophone.Enabled = True
        _rbSystemAudio.Enabled = True
        _rbScreen.Enabled = True
        _rbCamera.Enabled = True
        _cboMonitor.Enabled = True
        _chkCursor.Enabled = True
        _chkNoSound.Enabled = True
        _cboCamera.Enabled = True
        _cboCameraMode.Enabled = True
        _numPort.Enabled = True
        _numBitrate.Enabled = True
        _chkAllowLanViewers.Enabled = True
        If _txtPassword IsNot Nothing Then _txtPassword.Enabled = True
        If _btnGenPassword IsNot Nothing Then _btnGenPassword.Enabled = True
        If _btnCopyLink IsNot Nothing Then _btnCopyLink.Enabled = False
        _btnCopyCode.Enabled = False
        _btnSaveFile.Enabled = False
        _btnShowQr.Enabled = False
        If _btnCast IsNot Nothing Then
            _btnCast.Enabled = False
            _btnCast.Text = Localization.T("Воспроизвести на другом устройстве..")
        End If
        _activeCastReceiverId = ""
        _activeCastReceiverName = ""
        _lblState.Text = Localization.T("Трансляция остановлена.")
        _lblListeners.Visible = False
        _levelPeak = 0.0
        _meterPanel?.Invalidate()
        StopChip()
    End Sub

    Private Sub ShowRefusal(result As LiveStartResult)
        Select Case result.Refusal
            Case LiveStartRefusal.GateShut
                _lblState.Text = Localization.T("Эта функция выключена.")
            Case LiveStartRefusal.AlreadyRunning
                _lblState.Text = Localization.T("Другая трансляция уже идёт.")
            Case LiveStartRefusal.NoLanAddress
                _lblState.Text = Localization.T("В локальной сети нет доступного адреса. Трансляция не начата.")
            Case LiveStartRefusal.PortTaken
                _lblState.Text = Localization.TF("Порт {0} занят. Трансляция не начата.", CInt(_numPort.Value))
            Case LiveStartRefusal.EncoderUnavailable
                _lblState.Text = Localization.T("Кодировщик недоступен. Скачайте FFmpeg из программы и начните снова.")
            Case LiveStartRefusal.SourceUnavailable
                _lblState.Text = Localization.T("Windows не дала доступ к устройству.")
            Case Else
                _lblState.Text = Localization.T("Трансляция не начата.")
        End Select
    End Sub

    Private Sub RefreshOnAir()
        Dim state As LiveProducerState = _controller.State
        If Not state.Running Then Return
        If Not String.IsNullOrEmpty(_activeCastReceiverName) Then
            _lblState.Text = Localization.TF("Воспроизводится на: {0}", _activeCastReceiverName)
        Else
            _lblState.Text = Localization.TF("Идёт живая трансляция - {0}.", state.SourceName)
        End If
        _lblListeners.Text = Localization.TF("Слушателей: {0}", state.Listeners)
        _lblListeners.Visible = True
    End Sub

    ' --- the three hand-off channels (section 5, producer rule 3: the same description) -------

    Private Sub OnCopyLink(sender As Object, e As EventArgs)
        Dim state As LiveProducerState = _controller.State
        If String.IsNullOrEmpty(state.BarcodeText) Then Return
        ' LIVE-BROADCAST item M: hand off the complete descriptor; tell the password separately.
        Dim url As String = "fmsbcast://live?payload=" & Uri.EscapeDataString(state.BarcodeText)
        If SecureClipboard.SetSecret(url) Then
            _lblState.Text = Localization.T("Ссылка скопирована.")
        Else
            _lblState.Text = Localization.T("Не удалось скопировать.")
        End If
    End Sub

    Private Async Sub OnCastClick(sender As Object, e As EventArgs)
        If Not String.IsNullOrEmpty(_activeCastReceiverId) Then
            Dim receiverId As String = _activeCastReceiverId
            Dim broadcastId As String = _controller.State.BroadcastId
            Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
            _activeCastReceiverId = ""
            _activeCastReceiverName = ""
            _btnCast.Text = Localization.T("Воспроизвести на другом устройстве..")
            If agent IsNot Nothing AndAlso Not String.IsNullOrEmpty(broadcastId) Then
                Await agent.CastStopAsync(broadcastId, receiverId, CancellationToken.None)
            End If
            RefreshOnAir()
            Return
        End If

        Dim currentAgent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
        If currentAgent Is Nothing Then
            _lblState.Text = Localization.T("Нет доступных устройств для воспроизведения.")
            Return
        End If

        Dim state As LiveProducerState = _controller.State
        If Not state.Running Then Return

        Dim candidates As List(Of ExchangeDeviceRecord) = currentAgent.GetCastCandidates(state.Mode, If(String.IsNullOrEmpty(state.RecordDescriptorJson), state.DescriptorJson, state.RecordDescriptorJson))
        If candidates Is Nothing OrElse candidates.Count = 0 Then
            _lblState.Text = Localization.T("Нет доступных устройств для воспроизведения.")
            Return
        End If

        Dim menu As New ContextMenuStrip()
        For Each candidate In candidates
            Dim c As ExchangeDeviceRecord = candidate
            Dim displayName As String = If(String.IsNullOrEmpty(c.DeviceName), c.DeviceId, c.DeviceName)
            Dim item As ToolStripItem = menu.Items.Add(displayName)
            AddHandler item.Click, Async Sub()
                                       Await InitiateCastAsync(currentAgent, state.BroadcastId, c.DeviceId, displayName)
                                   End Sub
        Next
        menu.Show(_btnCast, New Point(0, _btnCast.Height))
    End Sub

    Private Async Function InitiateCastAsync(agent As ExchangeDeviceAgent, broadcastId As String, receiverDeviceId As String, receiverName As String) As Task
        _lblState.Text = Localization.T("Отправка запроса..")
        _btnCast.Enabled = False
        Try
            Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(10))
                Dim result As Tuple(Of Boolean, String) = Await agent.CastAsync(broadcastId, receiverDeviceId, cts.Token)
                If result.Item1 Then
                    _activeCastReceiverId = receiverDeviceId
                    _activeCastReceiverName = receiverName
                    _btnCast.Text = Localization.T("Остановить воспроизведение")
                    _lblState.Text = Localization.TF("Воспроизводится на: {0}", receiverName)
                Else
                    Dim reasonText As String
                    Select Case If(result.Item2, "").ToLowerInvariant()
                        Case "declined"
                            reasonText = Localization.T("Отклонено")
                        Case "unsupported"
                            reasonText = Localization.T("Не поддерживается")
                        Case "unavailable"
                            reasonText = Localization.T("Недоступно")
                        Case "timeout"
                            reasonText = Localization.T("Таймаут")
                        Case Else
                            reasonText = Localization.T("Не удалось воспроизвести.")
                    End Select
                    _lblState.Text = reasonText
                End If
            End Using
        Catch
            _lblState.Text = Localization.T("Не удалось воспроизвести.")
        Finally
            _btnCast.Enabled = True
        End Try
    End Function

    Private Sub OnCopyCode(sender As Object, e As EventArgs)
        Dim text As String = _controller.State.BarcodeText
        If text.Length = 0 Then Return
        If SecureClipboard.SetSecret(text) Then
            _lblState.Text = Localization.T("Код скопирован.")
        Else
            _lblState.Text = Localization.T("Не удалось скопировать.")
        End If
    End Sub

    Private Sub OnSaveFile(sender As Object, e As EventArgs)
        Dim state As LiveProducerState = _controller.State
        If state.DescriptorJson.Length = 0 Then Return
        _fileNumber += 1
        Using dialog As New SaveFileDialog With {
            .Title = Localization.T("Сохранить файл для импорта"),
            .Filter = "Live broadcast (*.fmsbcast)|*.fmsbcast",
            .FileName = LiveProducerDescriptorWriter.FileName(_fileNumber)
        }
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                File.WriteAllBytes(dialog.FileName, LiveProducerDescriptorWriter.FileContent(state.DescriptorJson))
                _lblState.Text = Localization.TF("Файл сохранён: {0}", Path.GetFileName(dialog.FileName))
            Catch
                _lblState.Text = Localization.T("Не удалось записать файл.")
            End Try
        End Using
    End Sub

    Private Sub OnShowQr(sender As Object, e As EventArgs)
        Dim payload As String = _controller.State.BarcodeText
        If payload.Length = 0 Then Return
        Using gen As New QRCoder.QRCodeGenerator()
            Using data As QRCoder.QRCodeData = gen.CreateQrCode(payload, QRCoder.QRCodeGenerator.ECCLevel.M)
                Dim png As New QRCoder.PngByteQRCode(data)
                Dim bytes As Byte() = png.GetGraphic(10)
                Using ms As New MemoryStream(bytes)
                    Using img As Image = Image.FromStream(ms)
                        Qr_Zoom_Form.ShowImage(Me, CType(img.Clone(), Image), Localization.T("QR-код трансляции"), carriesAccess:=False)
                    End Using
                End Using
            End Using
        End Using
    End Sub

    ' --- the on-air chip (safety rule 2: a small topmost chip, a setting, default on for screen) ---

    Private Sub StartChip()
        StopChip()
        _onAirChip = New LiveOnAirChip()
        AddHandler _onAirChip.Dismissed, Sub() SafeUi(New Action(AddressOf OnStop))
    End Sub

    Private Sub StopChip()
        If _onAirChip IsNot Nothing Then
            _onAirChip.Close()
            _onAirChip.Dispose()
            _onAirChip = Nothing
        End If
    End Sub

    ''' <summary>The small topmost "ON AIR" chip: a visible on-air state on the desktop itself. It is
    ''' a setting of the broadcast window (default on for the screen); clicking it stops the
    ''' broadcast - the one gesture that must always be at hand while a screen is being shown.</summary>
    Friend Class LiveOnAirChip
        Inherits Form

        Public Event Dismissed As EventHandler

        Public Sub New()
            UiLanguage.ApplyTo(Me)
            Me.Text = Localization.T("Живая трансляция")
            Me.FormBorderStyle = FormBorderStyle.None
            Me.ShowInTaskbar = False
            Me.TopMost = True
            Me.StartPosition = FormStartPosition.Manual
            Me.BackColor = Color.Firebrick
            Me.Size = New Size(120, 34)
            PositionBottomRight()
        End Sub

        Private Sub PositionBottomRight()
            Dim area As Rectangle = Screen.PrimaryScreen.Bounds
            Me.Location = New Point(area.Right - Me.Width - 24, area.Bottom - Me.Height - 64)
        End Sub

        Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
            MyBase.OnMouseClick(e)
            RaiseEvent Dismissed(Me, EventArgs.Empty)
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)
            If e Is Nothing OrElse e.Graphics Is Nothing Then Return
            TextRenderer.DrawText(e.Graphics, Localization.T("В ЭФИРЕ"), Me.Font,
                                  New Rectangle(Point.Empty, Me.Size), Color.White,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
        End Sub

    End Class

End Class
