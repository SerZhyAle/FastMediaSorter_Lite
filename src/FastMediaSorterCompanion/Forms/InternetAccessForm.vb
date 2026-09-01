Option Strict On

Imports System.Drawing
Imports System.Text
Imports System.Windows.Forms

''' <summary>
''' "Инструкция по пробросу порта" - the port-forward setup guidance, opened by a
''' button from the main window's server panel. The addresses/IPv6/router/credentials
''' now live (copyable) in the server panel and the useful links along the main
''' window's bottom; THIS dialog keeps only the instruction itself: the honest
''' reachability status, the step-by-step text (security note + how to forward the
''' port, or the UPnP/CGNAT explanation), a self SFTP probe ("Тест"), and a link to
''' the richer offline HTML guide (prefilled with your values + detected router).
''' Built with a TableLayoutPanel + AutoSize controls (NOT absolute pixel Left/Top/
''' Width) so the Test/Refresh/Close buttons and the guide link never get clipped
''' at high display scaling - the row heights/widths follow actual control content
''' instead of a fixed pixel layout computed for 96 DPI.
''' </summary>
Public NotInheritable Class InternetAccessForm
    Inherits Form

    Private _status As WorkerStatus
    Private _router As RouterIdentity
    Private _testing As Boolean
    Private _iconHandle As IntPtr

    Private lblNet As Label
    Private btnTestNet As Button
    Private btnRefresh As Button
    Private lnkGuide As LinkLabel
    Private txtForward As TextBox
    Private lblHint As Label
    Private btnClose As Button

    Public Sub New(status As WorkerStatus)
        _status = status
        BuildUi()
    End Sub


    Private Sub BuildUi()
        ' Script font + text direction for the active language, before any control
        ' exists - children inherit both (013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md block A').
        UiLanguage.ApplyTo(Me)
        Me.Text = Localization.T("Инструкция по пробросу порта")
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed, Sub() ShareIcons.FreeIcon(Me.Icon, _iconHandle)
        Me.FormBorderStyle = FormBorderStyle.Sizable
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.MinimumSize = New Size(560, 420)
        Me.ClientSize = New Size(680, 520)

        Dim tip As New ToolTip()

        ' Single root TableLayoutPanel, one column, explicit rows - no Dock-stacking
        ' order to guess at. Header/footer rows are AutoSize (sized to the actual
        ' button/label content); the guide link gets a fixed roomy row so its
        ' two-dot sentence can wrap to two lines instead of being clipped; the
        ' instructions text box is the only row that stretches (Percent 100).
        Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 4, .Padding = New Padding(12)}
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 44.0F))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))

        ' --- row 0: status line + Test/Refresh (buttons AutoSize -> never clipped) --
        Dim header As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 2, .Margin = New Padding(0, 0, 0, 10)}
        header.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        header.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        lblNet = New Label With {.Dock = DockStyle.Fill, .AutoEllipsis = True, .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font(Me.Font, FontStyle.Bold), .Margin = New Padding(0, 6, 12, 0)}
        Dim btnFlow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .WrapContents = False, .Margin = New Padding(0)}
        btnTestNet = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 4, 12, 4),
            .Margin = New Padding(0, 0, 8, 0), .Text = Localization.T("Тест")}
        btnRefresh = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 4, 12, 4),
            .Margin = New Padding(0), .Text = Localization.T("Обновить")}
        AddHandler btnTestNet.Click, AddressOf OnTestNet
        AddHandler btnRefresh.Click, AddressOf OnRefresh
        tip.SetToolTip(btnTestNet, Localization.T("Проверить внешний адрес с этого ПК (не окончательно - роутер может не пускать на свой адрес изнутри)."))
        btnFlow.Controls.Add(btnTestNet)
        btnFlow.Controls.Add(btnRefresh)
        header.Controls.Add(lblNet, 0, 0)
        header.Controls.Add(btnFlow, 1, 0)
        root.Controls.Add(header, 0, 0)

        ' --- row 1: link to the detailed offline guide (wraps within its own row) --
        lnkGuide = New LinkLabel With {.Dock = DockStyle.Fill, .AutoSize = False, .Margin = New Padding(0, 0, 0, 10),
            .Text = Localization.T("Открыть подробную инструкцию (HTML, с вашими значениями и моделью роутера)..")}
        AddHandler lnkGuide.LinkClicked, AddressOf OnOpenGuide
        root.Controls.Add(lnkGuide, 0, 1)

        ' --- row 2: the instructions themselves, the only part that stretches ------
        txtForward = New TextBox With {.Dock = DockStyle.Fill, .Margin = New Padding(0, 0, 0, 10),
            .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical, .BorderStyle = BorderStyle.FixedSingle,
            .BackColor = SystemColors.Window}
        root.Controls.Add(txtForward, 0, 2)

        ' --- row 3: hint + Close ----------------------------------------------------
        Dim footer As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 2, .Margin = New Padding(0)}
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        lblHint = New Label With {.Dock = DockStyle.Fill, .AutoEllipsis = True, .ForeColor = Color.DimGray,
            .TextAlign = ContentAlignment.MiddleLeft, .Margin = New Padding(0, 6, 12, 0)}
        btnClose = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(16, 4, 16, 4),
            .Margin = New Padding(0), .Text = Localization.T("Закрыть"), .DialogResult = DialogResult.OK}
        footer.Controls.Add(lblHint, 0, 0)
        footer.Controls.Add(btnClose, 1, 0)
        root.Controls.Add(footer, 0, 3)

        Me.Controls.Add(root)
        Me.CancelButton = btnClose
        DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout

        AddHandler Me.Shown, AddressOf OnShownFirst
        Render()
    End Sub

    ''' <summary>Keep the (font-scaled) window within the monitor working area at high DPI. It is
    ''' Sizable with a stretchy instructions box, so a capped size just shortens that box.</summary>
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
    End Sub

    Private Async Sub OnShownFirst(sender As Object, e As EventArgs)
        Await RefreshStatusAsync(poll:=True)
    End Sub

    Private Async Sub OnRefresh(sender As Object, e As EventArgs)
        Await RefreshStatusAsync(poll:=True)
    End Sub

    Private Async Function RefreshStatusAsync(poll As Boolean) As Task
        SetHint(Localization.T("Обновление.."))
        Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
        If st IsNot Nothing Then _status = st
        Render()
        If poll AndAlso _status IsNot Nothing AndAlso _status.Running AndAlso _status.Reachability Is Nothing Then
            For i As Integer = 1 To 15
                Await Task.Delay(1000)
                If Me.IsDisposed Then Return
                st = Await ShareController.GetStatusAsync()
                If st IsNot Nothing Then _status = st
                Render()
                If _status Is Nothing OrElse Not _status.Running OrElse _status.Reachability IsNot Nothing Then Exit For
            Next
        End If
        SetHint("")
    End Function

    ''' <summary>Fills the status line + guidance text (CGNAT / UPnP-mapped / needs-forward
    ''' / unknown), mirroring the LITE UpdateInternetUi.</summary>
    Private Sub Render()
        Dim st As WorkerStatus = _status
        Dim running As Boolean = st IsNot Nothing AndAlso st.Running
        Dim reach As WorkerReachability = If(st IsNot Nothing, st.Reachability, Nothing)
        lnkGuide.Enabled = running

        If Not running Then
            lblNet.Text = Localization.T("Запустите общий доступ, чтобы настроить интернет.")
            txtForward.Text = ""
            RefreshTestButton()
            Return
        End If
        If reach Is Nothing Then
            lblNet.Text = Localization.T("Определяем внешний адрес..")
            txtForward.Text = ""
            RefreshTestButton()
            Return
        End If

        Dim port As Integer = st.ListenPort
        Dim routerIp As String = NetworkInfo.DefaultGatewayIp()
        Dim lanIp As String = If(Not String.IsNullOrEmpty(reach.LanAddress), reach.LanAddress, NetworkInfo.LocalIPv4())
        Dim extHost As String = If(reach.ExternalHost, "")
        Dim extPort As Integer = reach.ExternalPort
        Dim isCgnat As Boolean = reach.IsCgnat
        Dim mapped As Boolean = Not String.IsNullOrEmpty(reach.PortMapMethod)
        Dim routerText As String = If(routerIp.Length > 0, "http://" & routerIp, Localization.T("адрес роутера"))
        Dim lanText As String = If(lanIp.Length > 0, lanIp, Localization.T("IP этого ПК"))

        Dim sb As New StringBuilder()
        sb.AppendLine(ShareText.SecurityText()).AppendLine()
        If isCgnat Then
            lblNet.Text = Localization.T("За CGNAT - извне недоступно.")
            sb.Append(ShareText.CgnatText())
        ElseIf mapped Then
            lblNet.Text = Localization.TF("Доступно из интернета: {0}", extHost & ":" & (If(extPort > 0, extPort, port)).ToString())
            If reach.ExternalVerified Then
                sb.Append(Localization.T("Порт открыт автоматически (UPnP) - настраивать роутер не нужно. Адрес уже в QR-коде и файле .fmscfg. Учтите: это не подтверждает работу извне - точный тест только с телефона по мобильной сети. При долгой работе общего доступа проверка может устареть; если телефон не подключается, отключите и снова включите общий доступ."))
            Else
                sb.Append(ShareText.ExternalUnverifiedText())
            End If
        ElseIf extHost.Length > 0 Then
            lblNet.Text = Localization.TF("Внешний адрес: {0} (нужен проброс порта)", extHost & ":" & port.ToString())
            sb.Append(ShareText.PortForwardText(routerText, port, lanText, port))
        Else
            lblNet.Text = Localization.T("Внешний адрес неизвестен - узнайте в роутере.")
            sb.Append(ShareText.PortForwardText(routerText, port, lanText, port))
        End If
        txtForward.Text = sb.ToString()
        RefreshTestButton()
    End Sub

    Private Sub RefreshTestButton()
        Dim st As WorkerStatus = _status
        Dim can As Boolean = st IsNot Nothing AndAlso st.Running AndAlso st.Reachability IsNot Nothing AndAlso
                             Not st.Reachability.IsCgnat AndAlso Not String.IsNullOrEmpty(st.Reachability.ExternalHost)
        btnTestNet.Enabled = Not _testing AndAlso can
    End Sub

    ' --- probe ------------------------------------------------------------------

    Private Async Sub OnTestNet(sender As Object, e As EventArgs)
        If _testing Then Return
        Dim st As WorkerStatus = _status
        Dim reach As WorkerReachability = If(st IsNot Nothing, st.Reachability, Nothing)
        Dim host As String = If(reach IsNot Nothing, If(reach.ExternalHost, ""), "")
        Dim port As Integer = 0
        If reach IsNot Nothing Then port = If(reach.ExternalPort > 0, reach.ExternalPort, If(st IsNot Nothing, st.ListenPort, 0))
        If String.IsNullOrEmpty(host) OrElse port <= 0 Then
            SetHint(Localization.T("Адрес ещё не определён."))
            Return
        End If
        _testing = True
        RefreshTestButton()
        SetHint(Localization.TF("Проверка {0} ..", host & ":" & port.ToString()))
        Try
            Dim res As SftpProbe.ProbeResult = Await SftpProbe.ProbeAsync(host, port)
            SetHint(DescribeProbe(res, host, port))
        Catch
            SetHint(Localization.T("Не удалось выполнить проверку."))
        Finally
            _testing = False
            RefreshTestButton()
        End Try
    End Sub

    Private Shared Function DescribeProbe(res As SftpProbe.ProbeResult, host As String, port As Integer) As String
        Dim ep As String = host & ":" & port.ToString()
        Select Case res
            Case SftpProbe.ProbeResult.SshOk
                Return Localization.TF("✓ SFTP-сервер доступен: {0}", ep)
            Case SftpProbe.ProbeResult.PortOpen
                Return Localization.TF("Порт открыт, но SFTP не ответил: {0}", ep)
            Case SftpProbe.ProbeResult.Timeout, SftpProbe.ProbeResult.Refused
                Return Localization.TF("✗ С этого ПК не отвечает ({0}). Роутер может не пускать на свой внешний адрес изнутри - проверьте с телефона по мобильной сети.", ep)

            Case Else
                Return Localization.T("Адрес некорректен.")
        End Select
    End Function

    ' --- offline HTML guide -----------------------------------------------------

    Private Async Sub OnOpenGuide(sender As Object, e As LinkLabelLinkClickedEventArgs)
        Dim port As Integer = If(_status IsNot Nothing, _status.ListenPort, 0)
        Dim reach As WorkerReachability = If(_status IsNot Nothing, _status.Reachability, Nothing)
        Dim lanIp As String = If(reach IsNot Nothing AndAlso Not String.IsNullOrEmpty(reach.LanAddress), reach.LanAddress, NetworkInfo.LocalIPv4())
        SetHint(Localization.T("Определяем роутер.."))
        Dim rt As RouterIdentity = Await GetRouterAsync()
        Dim model As String = rt.DisplayName()
        If Not ShareGuide.OpenPortForwardGuide(NetworkInfo.DefaultGatewayUrl(), port, lanIp, port, model, RouterInfo.SearchUrl(rt)) Then
            SetHint(Localization.T("Не удалось открыть инструкцию."))
        Else
            SetHint(If(model.Length > 0, Localization.TF("Роутер: {0}", model), ""))
        End If
    End Sub

    Private Async Function GetRouterAsync() As Task(Of RouterIdentity)
        If _router Is Nothing Then _router = Await Task.Run(Function() RouterInfo.Detect())
        Return _router
    End Function

    Private Sub SetHint(text As String)
        lblHint.Text = If(text, "")
    End Sub

End Class
