Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Phone access: the address rows, the internet verdict, the counters, and the one method
''' that pushes a WorkerStatus into all of them.
'''
''' The rule that shapes this file is §2 rule 2 of
''' SPECIFICATION_SHARE_MANAGER_COMPACT_WINDOW.md - "collapsed is not hidden". Every
''' section's one-line SUMMARY is filled by the same code path that fills its body
''' (<see cref="ApplyStatusToUi"/> and the 10 s stats tick), so a folded section is live
''' rather than frozen and still answers the question it exists for. A summary refreshed
''' anywhere else would be the first thing to go stale.
''' </summary>
Partial Public NotInheritable Class MainWindow

    ' --- section bodies ---------------------------------------------------------

    ''' <summary>
    ''' "Доступ с телефона": the six address rows. Structure that survives any DPI and any
    ''' scrollbar - a caption|value|copy|extra sub-grid whose columns are all AutoSize, so a
    ''' long value ellipsizes inside its own cell instead of widening the section.
    ''' </summary>
    Private Sub BuildAccessSection(sec As CollapsibleSection)
        Dim grid As New TableLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 4, .Margin = New Padding(0)}
        For i As Integer = 0 To 3
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Next
        AddServerRow(grid, Localization.T("Через интернет:"), AddressOf InternetStatusDisplay, AddressOf InternetAddressValue, True)
        AddServerRow(grid, Localization.T("Дома (Wi-Fi):"), AddressOf LanDisplay, AddressOf CurrentLanAddress, True)
        AddServerRow(grid, Localization.T("IPv6:"), AddressOf Ipv6Value, AddressOf Ipv6Value, False)
        AddServerRow(grid, Localization.T("Ключ узла:"), AddressOf FingerprintValue, AddressOf FingerprintValue, False)
        AddServerRow(grid, Localization.T("Логин:"), AddressOf LoginValue, AddressOf LoginValue, False)

        ' The password is the one value that must not be ambient: any screenshot of this
        ' window - including the one that produced the specification - published a live
        ' credential, and nothing in the normal flow (scan a QR) needs it on screen. Masked
        ' by default, revealed per session only, and the copy button works in both states.
        btnRevealPassword = New Button With {.Width = 28, .Height = 26, .Margin = New Padding(2, 3, 0, 3),
            .Image = _eyeGlyph, .ImageAlign = ContentAlignment.MiddleCenter, .TabStop = False,
            .Anchor = AnchorStyles.Left, .AccessibleName = Localization.T("Показать пароль")}
        AddHandler btnRevealPassword.Click, Sub() SetPasswordRevealed(Not _passwordRevealed)
        toolTip.SetToolTip(btnRevealPassword, Localization.T("Показать пароль"))
        AddServerRow(grid, Localization.T("Пароль:"), AddressOf PasswordDisplay, AddressOf PasswordValue, False, btnRevealPassword)

        sec.AddBodyRow(grid)
    End Sub

    ''' <summary>
    ''' "Доступ из интернета": what actually works right now, the one next step, and the
    ''' three controls that change it. The pair of lines exists because the address grid is
    ''' ambiguous on its own - a running share prints the LAN address and the internet
    ''' address one under the other, which reads as "both work" even when the router was
    ''' never configured.
    ''' </summary>
    Private Sub BuildInternetSection(sec As CollapsibleSection)
        lblAccessState = New Label With {.AutoSize = True, .MaximumSize = New Size(560, 0), .Margin = New Padding(0, 2, 0, 2)}
        sec.AddBodyRow(lblAccessState)
        lblAccessNext = New Label With {.AutoSize = True, .MaximumSize = New Size(560, 0),
            .ForeColor = SystemColors.GrayText, .Margin = New Padding(0, 0, 0, 2)}
        sec.AddBodyRow(lblAccessNext)

        btnTest = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Anchor = AnchorStyles.Left, .Padding = New Padding(12, 5, 12, 5), .Margin = New Padding(0, 8, 0, 2),
            .Text = Localization.T("Проверить доступ из интернета"), .Visible = False}
        AddHandler btnTest.Click, AddressOf OnTestClicked
        sec.AddBodyRow(btnTest)

        lnkRouter = New LinkLabel With {.AutoSize = True, .Margin = New Padding(0, 8, 0, 8)}
        AddHandler lnkRouter.LinkClicked, Sub() OnOpenRouter(Me, EventArgs.Empty)
        sec.AddBodyRow(lnkRouter)

        btnGuide = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Anchor = AnchorStyles.Left, .Padding = New Padding(12, 5, 12, 5), .Margin = New Padding(0, 2, 0, 4),
            .Text = Localization.T("Как настроить доступ через интернет")}
        AddHandler btnGuide.Click, AddressOf OnInternetAccess
        sec.AddBodyRow(btnGuide)
    End Sub

    ''' <summary>
    ''' "Статистика": the three local counters, plus the one button to the window that can
    ''' RESET them. The main window never grew a reset of its own - Share_Status_Form
    ''' already exists, is reachable from the tray, and duplicating it here is what put a
    ''' four-row reporting block on a window nobody opens to read reports.
    ''' </summary>
    Private Sub BuildStatsSection(sec As CollapsibleSection)
        pnlStats = New TableLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 2, .Margin = New Padding(0)}
        pnlStats.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        pnlStats.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        lblStatLast = AddStatRow(pnlStats, Localization.T("Последнее подключение:"), 0)
        lblStatConns = AddStatRow(pnlStats, Localization.T("Подключений:"), 1)
        lblStatFiles = AddStatRow(pnlStats, Localization.T("Файлов отдано:"), 2)
        toolTip.SetToolTip(lblStatConns, Localization.T("Считается каждый сеанс связи. Один телефон может подключаться несколько раз (проверка доступа, просмотр файла, переподключение)."))
        sec.AddBodyRow(pnlStats)

        btnStatsDetails = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Anchor = AnchorStyles.Left, .Padding = New Padding(12, 4, 12, 4), .Margin = New Padding(0, 8, 0, 2),
            .Text = Localization.T("Подробнее..")}
        AddHandler btnStatsDetails.Click, AddressOf OnStatsDetailsClicked
        sec.AddBodyRow(btnStatsDetails)
    End Sub

    Private Sub OnStatsDetailsClicked(sender As Object, e As EventArgs)
        Using dlg As New Share_Status_Form(_status)
            dlg.ShowDialog(Me)
        End Using
        ' The counters can have been reset in there - re-read rather than keep showing the
        ' numbers this window happened to be holding.
        Dim t As Task = RefreshStatsAsync()
    End Sub

    ' --- small UI builders ------------------------------------------------------

    ''' <summary>Adds a caption|value|copy row to the server grid (no visible borders).
    ''' The value label width is capped so long values (host key, password) ellipsize and
    ''' the copy column stays aligned. <paramref name="extra"/> (the password reveal
    ''' toggle) rides in the row's fourth column.</summary>
    Private Sub AddServerRow(grid As TableLayoutPanel, caption As String, valueFunc As Func(Of String), copyFunc As Func(Of String), alwaysShow As Boolean, Optional extra As Control = Nothing)
        Dim row As Integer = grid.RowCount
        grid.RowCount = row + 1
        grid.RowStyles.Add(New RowStyle(SizeType.AutoSize))

        Dim cap As New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 6, 12, 6), .ForeColor = Color.DimGray, .Text = caption}
        ' Fixed-width single-line value cell with ellipsis: a long fingerprint/IPv6 is
        ' truncated (user copies it with the button) instead of wrapping to 3 lines or
        ' pushing the copy button off the edge. Scales with AutoScaleMode.Font.
        Dim val As New Label With {.AutoSize = False, .Height = 24, .Width = 190, .Anchor = AnchorStyles.Left,
            .AutoEllipsis = True, .TextAlign = ContentAlignment.MiddleLeft, .Margin = New Padding(0, 4, 8, 4)}
        ' NB: default (Standard) FlatStyle - a System-styled button is OS-drawn and ignores
        ' the managed Image, so the copy glyph would not appear.
        Dim copy As New Button With {.Width = 28, .Height = 26, .Margin = New Padding(2, 3, 0, 3), .Image = _copyGlyph,
            .ImageAlign = ContentAlignment.MiddleCenter, .TabStop = False, .Anchor = AnchorStyles.Left, .Tag = copyFunc}
        toolTip.SetToolTip(copy, Localization.T("Копировать в буфер"))
        AddHandler copy.Click, AddressOf OnCopyClick

        grid.Controls.Add(cap, 0, row)
        grid.Controls.Add(val, 1, row)
        grid.Controls.Add(copy, 2, row)
        If extra IsNot Nothing Then grid.Controls.Add(extra, 3, row)
        _serverRows.Add(New ServerRow(cap, val, copy, valueFunc, copyFunc, alwaysShow, extra))
    End Sub

    ''' <summary>Caption (col0) + value label (col1) for the usage-stats block.</summary>
    Private Shared Function AddStatRow(tlp As TableLayoutPanel, caption As String, row As Integer) As Label
        tlp.RowCount = row + 1
        tlp.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        tlp.Controls.Add(New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 3, 12, 1), .ForeColor = Color.DimGray, .Text = caption}, 0, row)
        Dim val As New Label With {.AutoSize = True, .MaximumSize = New Size(300, 0), .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 3, 0, 1), .Text = "-"}
        tlp.Controls.Add(val, 1, row)
        Return val
    End Function

    Private Sub OnCopyClick(sender As Object, e As EventArgs)
        Dim btn As Button = TryCast(sender, Button)
        If btn Is Nothing Then Return
        Dim provider As Func(Of String) = TryCast(btn.Tag, Func(Of String))
        If provider Is Nothing Then Return
        Dim value As String = provider()
        If String.IsNullOrEmpty(value) Then Return
        Try
            Clipboard.SetText(value)
            SetHint(Localization.T("Скопировано в буфер."))
        Catch
        End Try
    End Sub

    ' --- value providers (raw values for copy; display shown in the value cell) -

    Private Function InternetAddressValue() As String
        Dim st As WorkerStatus = _status
        If st Is Nothing OrElse Not st.Running OrElse st.Reachability Is Nothing Then Return ""
        Dim r As WorkerReachability = st.Reachability
        If r.IsCgnat OrElse String.IsNullOrEmpty(r.ExternalHost) Then Return ""
        Dim port As Integer = If(r.ExternalPort > 0, r.ExternalPort, st.ListenPort)
        Return r.ExternalHost & ":" & port.ToString()
    End Function

    Private Function InternetStatusDisplay() As String
        Dim running As Boolean = _status IsNot Nothing AndAlso _status.Running
        Dim reach As WorkerReachability = If(_status IsNot Nothing, _status.Reachability, Nothing)
        If Not running Then Return "-"
        If reach Is Nothing Then Return Localization.T("определяется..")
        If reach.IsCgnat Then Return Localization.T("за CGNAT (недоступно)")
        Dim addr As String = InternetAddressValue()
        Return If(addr.Length > 0, addr, Localization.T("адрес неизвестен"))
    End Function

    Private Function CurrentLanAddress() As String
        Dim st As WorkerStatus = _status
        If st Is Nothing OrElse Not st.Running OrElse st.Reachability Is Nothing Then Return ""
        Dim lan As String = If(st.Reachability.LanAddress, "")
        If lan.Length = 0 OrElse st.ListenPort <= 0 Then Return ""
        Return lan & ":" & st.ListenPort.ToString()
    End Function

    Private Function LanDisplay() As String
        Dim addr As String = CurrentLanAddress()
        Return If(addr.Length > 0, addr, "-")
    End Function

    Private Function Ipv6Value() As String
        Dim st As WorkerStatus = _status
        If st Is Nothing OrElse st.Reachability Is Nothing OrElse String.IsNullOrEmpty(st.Reachability.Ipv6Address) Then Return ""
        Return st.Reachability.Ipv6Address & ":" & st.ListenPort.ToString()
    End Function

    Private Function FingerprintValue() As String
        Return If(_status IsNot Nothing, If(_status.Fingerprint, ""), "")
    End Function

    Private Function LoginValue() As String
        Dim st As WorkerStatus = _status
        If st Is Nothing OrElse Not st.Running Then Return ""
        Return If(String.IsNullOrEmpty(st.Username), "fms", st.Username)
    End Function

    Private Function PasswordValue() As String
        Return If(_status IsNot Nothing, If(_status.Password, ""), "")
    End Function

    ''' <summary>What the password CELL shows - dots unless the user asked (§3.6). The copy
    ''' button is wired to <see cref="PasswordValue"/> instead, so copying never requires
    ''' putting the credential on screen first.</summary>
    Private Function PasswordDisplay() As String
        Dim raw As String = PasswordValue()
        If raw.Length = 0 Then Return ""
        Return If(_passwordRevealed, raw, New String("•"c, 8))
    End Function

    ''' <summary>Reveal is per session and never persisted; it is dropped when the section
    ''' collapses (see OnSectionExpandedChanged) and when the window loses focus.</summary>
    Private Sub SetPasswordRevealed(value As Boolean)
        If _passwordRevealed = value Then Return
        _passwordRevealed = value
        If btnRevealPassword IsNot Nothing Then
            Dim tip As String = If(value, Localization.T("Скрыть пароль"), Localization.T("Показать пароль"))
            btnRevealPassword.AccessibleName = tip
            toolTip.SetToolTip(btnRevealPassword, tip)
        End If
        For Each sr As ServerRow In _serverRows
            If sr.Extra Is btnRevealPassword AndAlso btnRevealPassword IsNot Nothing Then sr.Value.Text = sr.ValueFunc()
        Next
    End Sub

    ' --- status -> UI -----------------------------------------------------------

    Private Sub ApplyStatusToUi()
        Dim running As Boolean = _status IsNot Nothing AndAlso _status.Running

        _rootsWithoutAccess = RefreshFolderAccessWarnings()

        btnToggle.Text = If(running, Localization.T("Остановить раздачу"), Localization.T("Начать раздачу"))
        UpdateStateLine(running)

        For Each sr As ServerRow In _serverRows
            Dim disp As String = sr.ValueFunc()
            Dim show As Boolean = running AndAlso (sr.AlwaysShow OrElse Not String.IsNullOrEmpty(disp))
            sr.Cap.Visible = show
            sr.Value.Visible = show
            sr.Value.Text = disp
            sr.Copy.Visible = show AndAlso Not String.IsNullOrEmpty(sr.CopyFunc())
            If sr.Extra IsNot Nothing Then sr.Extra.Visible = show AndAlso Not String.IsNullOrEmpty(sr.CopyFunc())
        Next

        UpdateAccessSummary(running)
        RefreshTestButton()
        lnkRouter.Visible = running
        If running Then SetRouterLink()

        btnShare.Enabled = running AndAlso Not _busy
        btnGuide.Enabled = running AndAlso Not _busy
        If miRouterSearch IsNot Nothing Then miRouterSearch.Enabled = running

        If running AndAlso Not _routerRequested Then Dim t As Task = DetectRouterAsync()

        UpdateStatsBlock()
        UpdateHostingBlock()
        UpdateSectionSummaries(running)
        RaiseEvent ServerStateChanged(running)
    End Sub

    ''' <summary>
    ''' The header's one-line answer to "is anything being shared right now" - now carrying
    ''' the folder count and the port too (decision F: this is the line a user reads for
    ''' that question, so the facts belong on it rather than on the list's own header).
    ''' While the list is empty the intro takes its place: instruction on the first run,
    ''' wallpaper on every later one (decision D).
    ''' </summary>
    Private Sub UpdateStateLine(running As Boolean)
        Dim empty As Boolean = lvFolders IsNot Nothing AndAlso lvFolders.Items.Count = 0
        lblIntro.Visible = empty
        lblState.Visible = Not empty
        lblStateDot.Visible = Not empty

        If running AndAlso _rootsWithoutAccess > 0 Then
            ' A listener that answers and then refuses every folder is not a working
            ' share, and green is what made that state invisible from this PC: the
            ' phone got "permission denied" while the window said everything was fine.
            lblState.Text = HostingText.RunningNoAccessLine(_rootsWithoutAccess)
            lblState.ForeColor = CollapsibleSection.AttentionColor
        ElseIf running Then
            Dim count As Integer = CountSharedFolders()
            Dim port As Integer = If(_status IsNot Nothing, _status.ListenPort, 0)
            lblState.Text = Localization.TF("Раздача работает - {0}, порт {1}", FolderCountText(count), port)
            lblState.ForeColor = Color.ForestGreen
        Else
            lblState.Text = Localization.T("Раздача выключена")
            lblState.ForeColor = Color.DimGray
        End If
        lblStateDot.ForeColor = lblState.ForeColor
    End Sub

    Private Function CountSharedFolders() As Integer
        If lvFolders Is Nothing Then Return 0
        Dim n As Integer = 0
        For Each it As ListViewItem In lvFolders.Items
            If it.Checked Then n += 1
        Next
        Return n
    End Function

    ''' <summary>Three plural forms, chosen by the Slavic rule the Russian source needs; the
    ''' languages whose rule is simpler map two of the three onto the same wording, which is
    ''' correct for them and costs nothing.</summary>
    Private Shared Function FolderCountText(count As Integer) As String
        Dim tail As Integer = count Mod 100
        If tail >= 11 AndAlso tail <= 14 Then Return Localization.TF("{0} папок", count)
        Select Case count Mod 10
            Case 1 : Return Localization.TF("{0} папка", count)
            Case 2, 3, 4 : Return Localization.TF("{0} папки", count)
            Case Else : Return Localization.TF("{0} папок", count)
        End Select
    End Function

    ''' <summary>
    ''' The live one-liners the folded sections show. Called from the same place the bodies
    ''' are filled, which is what makes "collapsed is not hidden" true rather than
    ''' aspirational - and from the 10 s tick, so the counters move under a folded header.
    ''' </summary>
    Private Sub UpdateSectionSummaries(running As Boolean)
        If _secAccess Is Nothing Then Return

        Dim lan As String = CurrentLanAddress()
        Dim login As String = LoginValue()
        If running AndAlso lan.Length > 0 Then
            _secAccess.Summary = If(login.Length > 0, lan & " - " & login, lan)
        Else
            _secAccess.Summary = "-"
        End If
        _secAccess.SummaryColor = SystemColors.GrayText

        Dim reach As WorkerReachability = If(_status IsNot Nothing, _status.Reachability, Nothing)
        If running Then
            Dim port As Integer = 0
            If _status IsNot Nothing Then
                port = If(reach IsNot Nothing AndAlso reach.ExternalPort > 0, reach.ExternalPort, _status.ListenPort)
            End If
            ' Verbatim from ShareText - no second wording, so the folded and the unfolded
            ' state cannot drift apart (§5.3).
            _secInternet.Summary = ShareText.AccessStateLine(reach, port)
            _secInternet.SummaryColor = AccessStateColor(reach)
        Else
            _secInternet.Summary = "-"
            _secInternet.SummaryColor = SystemColors.GrayText
        End If

        ' The amber case, and the only auto-expand in the design: the user set something up
        ' and it does not answer from outside. FlagAttention opens the section ONCE per
        ' window session, so a manual collapse afterwards stands.
        If running AndAlso reach IsNot Nothing AndAlso reach.ExternalPortChecked AndAlso Not reach.ExternalPortOpen Then
            If _secInternet.FlagAttention(_secInternet.Summary) Then RelayoutContent()
        End If

        UpdateStatsSummary()
    End Sub

    Private Sub UpdateStatsSummary()
        If _secStats Is Nothing Then Return
        Dim s As WorkerStats = If(_status IsNot Nothing, _status.Stats, Nothing)
        If s Is Nothing Then
            _secStats.Summary = "-"
            Return
        End If
        _secStats.Summary = Localization.TF("{0} подключений, {1} файлов", s.TotalConnections, s.FilesServedTotal)
        _secStats.SummaryColor = SystemColors.GrayText
    End Sub

    ''' <summary>Fills the usage-stats block from the current Status.Stats. The whole section
    ''' is hidden when the server is off or the (older) worker sends no stats - the same rule
    ''' the block itself always followed, applied one level up.</summary>
    Private Sub UpdateStatsBlock()
        Dim running As Boolean = _status IsNot Nothing AndAlso _status.Running
        Dim s As WorkerStats = If(_status IsNot Nothing, _status.Stats, Nothing)
        If Not running OrElse s Is Nothing Then
            If _secStats IsNot Nothing Then _secStats.Visible = False
            Return
        End If
        If _secStats IsNot Nothing Then _secStats.Visible = True
        Dim never As String = Localization.T("ещё не было")
        Dim lastAt As String = Share_Status_Form.FormatTime(s.LastConnectionAt)
        If lastAt.Length = 0 Then
            lblStatLast.Text = never
        ElseIf String.IsNullOrEmpty(s.LastConnectionAddress) Then
            lblStatLast.Text = lastAt
        Else
            lblStatLast.Text = lastAt & "  -  " & s.LastConnectionAddress
        End If
        lblStatConns.Text = String.Format(Localization.T("всего {0} (с запуска {1})"), s.TotalConnections, s.ConnectionsSinceStart)
        lblStatFiles.Text = String.Format(Localization.T("всего {0} (с запуска {1})"), s.FilesServedTotal, s.FilesServedSinceStart)
    End Sub

    ''' <summary>Set while a stats fetch is out, so the 10 s tick cannot stack requests behind
    ''' one that is waiting on a worker which stopped answering.</summary>
    Private _statsFetchInFlight As Boolean = False

    Private Sub OnStatsTick(sender As Object, e As EventArgs)
        If _statsFetchInFlight Then Return
        Dim t As Task = RefreshStatsAsync()
    End Sub

    ''' <summary>Re-fetches status just for the usage block and the folded summaries (light -
    ''' does not run the full ApplyStatusToUi, so it never fights the user's in-flight
    ''' actions).</summary>
    Private Async Function RefreshStatsAsync() As Task
        If _busy OrElse Not Me.Visible Then Return

        _statsFetchInFlight = True
        Dim st As WorkerStatus
        Try
            st = Await ShareController.GetStatusAsync()
        Finally
            _statsFetchInFlight = False
        End Try

        If IsDisposed OrElse st Is Nothing Then Return
        _status = st
        UpdateStatsBlock()
        UpdateStatsSummary()
    End Function

    ''' <summary>Keeps the hosting line honest after every status refresh: the mode can
    ''' change under the window (an elevated install/remove ran, or the service was
    ''' stopped from services.msc), and a stale "Windows service" line would promise
    ''' availability nobody is providing.
    '''
    ''' Only the SERVER edition shows it here. In User mode the same fact lives in the
    ''' settings window next to the console that changes it; in Server mode it must stay
    ''' visible without opening anything, because there neither startup checkbox decides
    ''' whether the folders are reachable - the service does (§6).</summary>
    Private Sub UpdateHostingBlock()
        If lblHosting Is Nothing Then Return
        Dim line As String = If(ServerFeatures.IsSystemServiceHost(), HostingText.HostModeLine(ServerFeatures.HostMode()), "")
        lblHosting.Text = line
        lblHosting.Visible = line.Length > 0
    End Sub

    ''' <summary>Fills the "what works right now" + "what to do next" pair. Hidden entirely
    ''' while nothing is being served - the state line already says so, and advice about a
    ''' share that is off would only be noise.</summary>
    Private Sub UpdateAccessSummary(running As Boolean)
        If Not running Then
            lblAccessState.Visible = False
            lblAccessNext.Visible = False
            Return
        End If
        Dim reach As WorkerReachability = If(_status IsNot Nothing, _status.Reachability, Nothing)
        ' The port the outside world would knock on - the mapped external one when a mapping
        ' exists, else the listen port (which is what a manual forward is usually set to).
        Dim port As Integer = 0
        If _status IsNot Nothing Then
            port = If(reach IsNot Nothing AndAlso reach.ExternalPort > 0, reach.ExternalPort, _status.ListenPort)
        End If
        Dim state As String = ShareText.AccessStateLine(reach, port)
        Dim nextStep As String = ShareText.AccessNextStepLine(reach)
        lblAccessState.Text = state
        lblAccessState.ForeColor = AccessStateColor(reach)
        lblAccessState.Visible = state.Length > 0
        lblAccessNext.Text = nextStep
        lblAccessNext.Visible = nextStep.Length > 0
    End Sub

    ''' <summary>Colour for the state line. Green ONLY once an outside check actually confirmed
    ''' the internet path, amber when such a check ran and failed (something the user set up is
    ''' broken), plain text otherwise: LAN-only is a normal, perfectly usable state and must not
    ''' be painted as a problem just because the internet half is unproven.</summary>
    Private Shared Function AccessStateColor(reach As WorkerReachability) As Color
        If reach Is Nothing Then Return SystemColors.GrayText
        If reach.ExternalPortChecked Then
            Return If(reach.ExternalPortOpen, Color.ForestGreen, CollapsibleSection.AttentionColor)
        End If
        Return SystemColors.ControlText
    End Function

    ' --- router -----------------------------------------------------------------

    Private Async Function DetectRouterAsync() As Task
        If _routerRequested Then Return
        _routerRequested = True
        Try
            _router = Await Task.Run(Function() RouterInfo.Detect())
            If Me.IsDisposed Then Return
            SetRouterLink()
        Catch
        End Try
    End Function

    Private Sub SetRouterLink()
        Dim model As String = If(_router IsNot Nothing, _router.DisplayName(), "")
        lnkRouter.Text = If(model.Length > 0, Localization.TF("Роутер: {0}", model), (Localization.T("Роутер: не определён")))
        lnkRouter.Enabled = True
    End Sub

    Private Async Function GetRouterAsync() As Task(Of RouterIdentity)
        If _router Is Nothing Then _router = Await Task.Run(Function() RouterInfo.Detect())
        Return _router
    End Function

    Private Sub OnOpenRouter(sender As Object, e As EventArgs)
        Dim url As String = NetworkInfo.DefaultGatewayUrl()
        If url.Length = 0 Then
            SetHint(Localization.T("Не удалось определить адрес роутера."))
            Return
        End If
        NetworkInfo.OpenInBrowser(url)
    End Sub

    Private Async Sub OnOpenRouterSearch(sender As Object, e As EventArgs)
        SetHint(Localization.T("Определяем роутер.."))
        Dim rt As RouterIdentity = Await GetRouterAsync()
        NetworkInfo.OpenInBrowser(RouterInfo.SearchUrl(rt))
        SetHint(If(rt.DisplayName().Length > 0, Localization.TF("Роутер: {0}", rt.DisplayName()),
            Localization.T("Модель не определена - открыт общий поиск.")))
    End Sub

    ' --- internet test ----------------------------------------------------------

    Private Sub OnTestClicked(sender As Object, e As EventArgs)
        If _testing Then Return
        Dim st As WorkerStatus = _status
        Dim reach As WorkerReachability = If(st IsNot Nothing, st.Reachability, Nothing)
        Dim host As String = If(reach IsNot Nothing, If(reach.ExternalHost, ""), "")
        Dim port As Integer = 0
        If reach IsNot Nothing Then port = If(reach.ExternalPort > 0, reach.ExternalPort, If(st IsNot Nothing, st.ListenPort, 0))
        If String.IsNullOrEmpty(host) OrElse port <= 0 Then
            SetHint(Localization.T("Адрес из интернета ещё не определён."))
            Return
        End If
        ' The answer goes into a modal, not into the bottom status strip: the button lives
        ' in a section and the strip is the opposite corner of the window, so the result
        ' read as an unrelated line rather than as the answer to this click.
        ' The strip still keeps the one-line verdict afterwards, as a lasting trace.
        _testing = True
        btnTest.Enabled = False
        Try
            Using dlg As New Share_Access_Test_Form(host, port)
                dlg.ShowDialog(Me)
                If dlg.ResultLine.Length > 0 Then SetHint(dlg.ResultLine)
            End Using
        Finally
            _testing = False
            RefreshTestButton()
        End Try
    End Sub

    Private Sub RefreshTestButton()
        If btnTest Is Nothing Then Return
        Dim st As WorkerStatus = _status
        Dim can As Boolean = st IsNot Nothing AndAlso st.Running AndAlso st.Reachability IsNot Nothing AndAlso
                             Not st.Reachability.IsCgnat AndAlso Not String.IsNullOrEmpty(st.Reachability.ExternalHost)
        btnTest.Visible = can
        btnTest.Enabled = can AndAlso Not _testing AndAlso Not _busy
    End Sub

    Private Sub OnInternetAccess(sender As Object, e As EventArgs)
        Using dlg As New InternetAccessForm(_status)
            dlg.ShowDialog(Me)
        End Using
    End Sub

    ' --- reachability polling ---------------------------------------------------

    Private Sub CancelReachabilityPoll()
        _reachPollGen += 1
    End Sub

    Private Async Function PollReachabilityAsync() As Task
        _reachPollGen += 1
        Dim myGen As Integer = _reachPollGen
        For i As Integer = 1 To 20
            Await Task.Delay(1000)
            If myGen <> _reachPollGen OrElse Me.IsDisposed Then Return
            Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
            If st Is Nothing Then Continue For
            _status = st
            ApplyStatusToUi()
            If Not st.Running OrElse st.Reachability IsNot Nothing Then Return
        Next
    End Function

End Class
