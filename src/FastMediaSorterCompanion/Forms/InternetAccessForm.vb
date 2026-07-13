Option Strict On

Imports System.Drawing
Imports System.Text
Imports System.Windows.Forms

''' <summary>
''' "Доступ из интернета" - all the internet-reachability guidance that used to live
''' on the LITE Share tab's "Из интернета" page: external address / IPv6, honest
''' reachability status (UPnP-mapped / needs-forward / CGNAT / unverified), a self
''' SFTP probe ("Тест"), open-router, detect-router-model + web-search, the offline
''' port-forward guide, and the security note. Opened from the main window's server
''' panel; read-only informational + helper links (the actual QR/export is the
''' package wizard). Ported near-verbatim from Table_Form.Share.vb §internet.
''' </summary>
Public NotInheritable Class InternetAccessForm
    Inherits Form

    Private _status As WorkerStatus
    Private _router As RouterIdentity
    Private _testing As Boolean

    Private lblNet As Label
    Private lblExtAddr As Label
    Private lblIpv6 As Label
    Private lblRouterUrl As Label
    Private lblRouterModel As Label
    Private btnTestNet As Button
    Private btnOpenRouter As Button
    Private btnRefresh As Button
    Private lnkGuide As LinkLabel
    Private lnkRouterSearch As LinkLabel
    Private lnkWebGuide As LinkLabel
    Private txtForward As TextBox
    Private lblHint As Label
    Private btnClose As Button

    Public Sub New(status As WorkerStatus)
        _status = status
        BuildUi()
    End Sub

    Private Shared ReadOnly Property Rus As Boolean
        Get
            Return Is_Russian_Language
        End Get
    End Property

    Private Sub BuildUi()
        Me.Text = If(Rus, "Доступ из интернета", "Internet access")
        Me.FormBorderStyle = FormBorderStyle.Sizable
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ClientSize = New Size(620, 520)
        Me.MinimumSize = New Size(560, 460)
        Me.Font = New Font("Segoe UI", 9.0F)
        Me.AutoScaleMode = AutoScaleMode.Font

        Dim tip As New ToolTip()

        lblNet = New Label With {.Left = 12, .Top = 12, .Width = 596, .Height = 22, .AutoEllipsis = True,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
            .Font = New Font(Me.Font, FontStyle.Bold)}
        lblExtAddr = New Label With {.Left = 12, .Top = 38, .Width = 400, .Height = 20, .AutoEllipsis = True, .ForeColor = Color.DimGray}
        lblIpv6 = New Label With {.Left = 12, .Top = 60, .Width = 596, .Height = 20, .AutoEllipsis = True, .ForeColor = Color.DimGray,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right}

        btnTestNet = New Button With {.Left = 420, .Top = 36, .Width = 90, .Height = 26, .Text = If(Rus, "Тест", "Test"),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right}
        btnRefresh = New Button With {.Left = 516, .Top = 36, .Width = 92, .Height = 26, .Text = If(Rus, "Обновить", "Refresh"),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right}
        AddHandler btnTestNet.Click, AddressOf OnTestNet
        AddHandler btnRefresh.Click, AddressOf OnRefresh
        tip.SetToolTip(btnTestNet, If(Rus, "Проверить внешний адрес с этого ПК (не окончательно - роутер может не пускать на свой адрес изнутри).",
                                          "Probe the external address from this PC (inconclusive - a router may refuse its own address from inside)."))

        Dim lblRouterCap As New Label With {.Left = 12, .Top = 90, .Width = 60, .Height = 20, .Text = If(Rus, "Роутер:", "Router:")}
        lblRouterModel = New Label With {.Left = 74, .Top = 90, .Width = 318, .Height = 20, .AutoEllipsis = True,
            .Font = New Font(Me.Font, FontStyle.Bold), .Text = If(Rus, "определяется..", "detecting..")}
        lblRouterUrl = New Label With {.Left = 74, .Top = 110, .Width = 318, .Height = 20, .AutoEllipsis = True, .ForeColor = Color.DimGray}
        btnOpenRouter = New Button With {.Left = 420, .Top = 90, .Width = 188, .Height = 26, .Text = If(Rus, "Открыть роутер", "Open router"),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right}
        AddHandler btnOpenRouter.Click, AddressOf OnOpenRouter

        lnkGuide = New LinkLabel With {.Left = 12, .Top = 140, .Width = 300, .Height = 20,
            .Text = If(Rus, "Инструкция по пробросу порта..", "Port-forward guide..")}
        lnkRouterSearch = New LinkLabel With {.Left = 320, .Top = 140, .Width = 288, .Height = 20,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
            .Text = If(Rus, "Найти инструкцию для моей модели..", "Find a guide for my model..")}
        lnkWebGuide = New LinkLabel With {.Left = 12, .Top = 162, .Width = 596, .Height = 20,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
            .Text = If(Rus, "Как публиковать папки для Android (на сайте)..", "How to publish folders for Android (website)..")}
        AddHandler lnkGuide.LinkClicked, AddressOf OnOpenGuide
        AddHandler lnkRouterSearch.LinkClicked, AddressOf OnOpenRouterSearch
        AddHandler lnkWebGuide.LinkClicked, Sub() NetworkInfo.OpenInBrowser(ShareGuide.SiteGuideUrl)

        txtForward = New TextBox With {.Left = 12, .Top = 190, .Width = 596, .Height = 280,
            .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical, .BorderStyle = BorderStyle.FixedSingle,
            .BackColor = SystemColors.Window, .Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right}

        lblHint = New Label With {.Left = 12, .Top = 478, .Width = 480, .Height = 32, .ForeColor = Color.DimGray, .AutoEllipsis = True,
            .Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right}
        btnClose = New Button With {.Left = 526, .Top = 484, .Width = 82, .Height = 28, .Text = If(Rus, "Закрыть", "Close"),
            .Anchor = AnchorStyles.Bottom Or AnchorStyles.Right, .DialogResult = DialogResult.OK}

        Me.Controls.AddRange(New Control() {lblNet, lblExtAddr, lblIpv6, btnTestNet, btnRefresh,
            lblRouterCap, lblRouterModel, lblRouterUrl, btnOpenRouter, lnkGuide, lnkRouterSearch, lnkWebGuide, txtForward, lblHint, btnClose})
        Me.CancelButton = btnClose

        AddHandler Me.Shown, AddressOf OnShownFirst
        Render()
    End Sub

    Private Async Sub OnShownFirst(sender As Object, e As EventArgs)
        ' Detect the router model (UPnP, slow) in the background - the "TP-Link Archer
        ' BE550"-style line from the original LITE Share tab.
        Dim rtTask As Task = DetectRouterAsync()
        ' Fetch a fresh snapshot so the external address is current, then poll a few
        ' times if the worker's reachability probe hasn't finished yet.
        Await RefreshStatusAsync(poll:=True)
    End Sub

    Private Async Function DetectRouterAsync() As Task
        Try
            Dim rt As RouterIdentity = Await GetRouterAsync()
            If Me.IsDisposed Then Return
            Dim model As String = rt.DisplayName()
            lblRouterModel.Text = If(model.Length > 0, model, If(Rus, "модель не определена", "model unknown"))
        Catch
            If Not Me.IsDisposed Then lblRouterModel.Text = If(Rus, "модель не определена", "model unknown")
        End Try
    End Function

    Private Async Sub OnRefresh(sender As Object, e As EventArgs)
        Await RefreshStatusAsync(poll:=True)
    End Sub

    Private Async Function RefreshStatusAsync(poll As Boolean) As Task
        SetHint(If(Rus, "Обновление..", "Refreshing.."))
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

    ''' <summary>Fills the whole form from the current status - the InternetAccessForm
    ''' analogue of the LITE UpdateInternetUi (same branching: CGNAT / UPnP-mapped /
    ''' needs-forward / unknown).</summary>
    Private Sub Render()
        Dim st As WorkerStatus = _status
        Dim running As Boolean = st IsNot Nothing AndAlso st.Running
        Dim reach As WorkerReachability = If(st IsNot Nothing, st.Reachability, Nothing)

        Dim routerIp As String = NetworkInfo.DefaultGatewayIp()
        lblRouterUrl.Text = If(routerIp.Length > 0, "http://" & routerIp, "-")
        btnOpenRouter.Enabled = running AndAlso routerIp.Length > 0
        lnkGuide.Enabled = running
        lnkRouterSearch.Enabled = running
        lblIpv6.Text = If(reach IsNot Nothing AndAlso Not String.IsNullOrEmpty(reach.Ipv6Address),
                          (If(Rus, "IPv6 (в обход NAT): ", "IPv6 (bypasses NAT): ")) & reach.Ipv6Address & ":" & If(st IsNot Nothing, st.ListenPort, 0).ToString(), "")

        If Not running Then
            lblNet.Text = If(Rus, "Запустите общий доступ, чтобы настроить интернет.", "Start sharing to set up internet access.")
            lblExtAddr.Text = ""
            txtForward.Text = ""
            RefreshTestButton()
            Return
        End If
        If reach Is Nothing Then
            lblNet.Text = If(Rus, "Определяем внешний адрес..", "Detecting the external address..")
            lblExtAddr.Text = ""
            txtForward.Text = ""
            RefreshTestButton()
            Return
        End If

        Dim port As Integer = st.ListenPort
        Dim lanIp As String = If(Not String.IsNullOrEmpty(reach.LanAddress), reach.LanAddress, NetworkInfo.LocalIPv4())
        Dim extHost As String = If(reach.ExternalHost, "")
        Dim extPort As Integer = reach.ExternalPort
        Dim isCgnat As Boolean = reach.IsCgnat
        Dim mapped As Boolean = Not String.IsNullOrEmpty(reach.PortMapMethod)
        Dim routerText As String = If(routerIp.Length > 0, "http://" & routerIp, If(Rus, "адрес роутера", "the router address"))
        Dim lanText As String = If(lanIp.Length > 0, lanIp, If(Rus, "IP этого ПК", "this PC's IP"))

        lblExtAddr.Text = If(extHost.Length > 0, (If(Rus, "Внешний адрес: ", "External address: ")) & extHost & ":" & (If(extPort > 0, extPort, port)).ToString(), "")

        Dim sb As New StringBuilder()
        sb.AppendLine(ShareText.SecurityText(Rus)).AppendLine()
        If isCgnat Then
            lblNet.Text = If(Rus, "За CGNAT - извне недоступно.", "Behind CGNAT - not reachable from outside.")
            sb.Append(ShareText.CgnatText(Rus))
        ElseIf mapped Then
            lblNet.Text = (If(Rus, "Доступно из интернета: ", "Reachable from internet: ")) & extHost & ":" & (If(extPort > 0, extPort, port)).ToString()
            If reach.ExternalVerified Then
                sb.Append(If(Rus,
                    "Порт открыт автоматически (UPnP) - настраивать роутер не нужно. Адрес уже в QR-коде и файле .fmscfg. Учтите: это не подтверждает работу извне - точный тест только с телефона по мобильной сети. При долгой работе общего доступа проверка может устареть; если телефон не подключается, отключите и снова включите общий доступ.",
                    "The port was opened automatically (UPnP) - no router setup needed. The address is already in the QR code and .fmscfg file. Note: this does not confirm it actually works from outside - the definitive test is from the phone on mobile data. Long-running sessions can go stale; if the phone can't connect, turn sharing off and back on."))
            Else
                sb.Append(ShareText.ExternalUnverifiedText(Rus))
            End If
        ElseIf extHost.Length > 0 Then
            lblNet.Text = (If(Rus, "Внешний адрес: ", "External address: ")) & extHost & ":" & port.ToString() & If(Rus, " (нужен проброс порта)", " (needs port forwarding)")
            sb.Append(ShareText.PortForwardText(Rus, routerText, port, lanText, port))
        Else
            lblNet.Text = If(Rus, "Внешний адрес неизвестен - узнайте в роутере.", "External address unknown - check the router.")
            sb.Append(ShareText.PortForwardText(Rus, routerText, port, lanText, port))
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
            SetHint(If(Rus, "Адрес ещё не определён.", "No address yet."))
            Return
        End If
        _testing = True
        RefreshTestButton()
        SetHint((If(Rus, "Проверка ", "Testing ")) & host & ":" & port.ToString() & " ..")
        Try
            Dim res As SftpProbe.ProbeResult = Await SftpProbe.ProbeAsync(host, port)
            SetHint(DescribeProbe(res, host, port, Rus))
        Catch
            SetHint(If(Rus, "Не удалось выполнить проверку.", "Could not run the test."))
        Finally
            _testing = False
            RefreshTestButton()
        End Try
    End Sub

    Private Shared Function DescribeProbe(res As SftpProbe.ProbeResult, host As String, port As Integer, rus As Boolean) As String
        Dim ep As String = host & ":" & port.ToString()
        Select Case res
            Case SftpProbe.ProbeResult.SshOk
                Return (If(rus, "✓ SFTP-сервер доступен: ", "✓ SFTP server reachable: ")) & ep
            Case SftpProbe.ProbeResult.PortOpen
                Return (If(rus, "Порт открыт, но SFTP не ответил: ", "Port open, but no SFTP reply: ")) & ep
            Case SftpProbe.ProbeResult.Timeout, SftpProbe.ProbeResult.Refused
                Return If(rus,
                    "✗ С этого ПК не отвечает (" & ep & "). Роутер может не пускать на свой внешний адрес изнутри - проверьте с телефона по мобильной сети.",
                    "✗ No answer from this PC (" & ep & "). Your router may block reaching its own external address from inside - test from the phone on mobile data.")
            Case Else
                Return If(rus, "Адрес некорректен.", "Invalid address.")
        End Select
    End Function

    ' --- router helpers ---------------------------------------------------------

    Private Sub OnOpenRouter(sender As Object, e As EventArgs)
        Dim url As String = NetworkInfo.DefaultGatewayUrl()
        If url.Length = 0 Then
            SetHint(If(Rus, "Не удалось определить адрес роутера.", "Could not determine the router address."))
            Return
        End If
        NetworkInfo.OpenInBrowser(url)
    End Sub

    Private Async Sub OnOpenGuide(sender As Object, e As LinkLabelLinkClickedEventArgs)
        Dim port As Integer = If(_status IsNot Nothing, _status.ListenPort, 0)
        Dim reach As WorkerReachability = If(_status IsNot Nothing, _status.Reachability, Nothing)
        Dim lanIp As String = If(reach IsNot Nothing AndAlso Not String.IsNullOrEmpty(reach.LanAddress), reach.LanAddress, NetworkInfo.LocalIPv4())
        SetHint(If(Rus, "Определяем роутер..", "Detecting router.."))
        Dim rt As RouterIdentity = Await GetRouterAsync()
        Dim model As String = rt.DisplayName()
        If Not ShareGuide.OpenPortForwardGuide(NetworkInfo.DefaultGatewayUrl(), port, lanIp, port, Rus, model, RouterInfo.SearchUrl(rt)) Then
            SetHint(If(Rus, "Не удалось открыть инструкцию.", "Could not open the guide."))
        Else
            SetHint(If(model.Length > 0, (If(Rus, "Роутер: ", "Router: ")) & model, ""))
        End If
    End Sub

    Private Async Sub OnOpenRouterSearch(sender As Object, e As LinkLabelLinkClickedEventArgs)
        SetHint(If(Rus, "Определяем роутер..", "Detecting router.."))
        Dim rt As RouterIdentity = Await GetRouterAsync()
        NetworkInfo.OpenInBrowser(RouterInfo.SearchUrl(rt))
        SetHint(If(rt.DisplayName().Length > 0, (If(Rus, "Роутер: ", "Router: ")) & rt.DisplayName(),
            If(Rus, "Модель не определена - открыт общий поиск.", "Model unknown - opened a general search.")))
    End Sub

    Private Async Function GetRouterAsync() As Task(Of RouterIdentity)
        If _router Is Nothing Then _router = Await Task.Run(Function() RouterInfo.Detect())
        Return _router
    End Function

    Private Sub SetHint(text As String)
        lblHint.Text = If(text, "")
    End Sub

End Class
