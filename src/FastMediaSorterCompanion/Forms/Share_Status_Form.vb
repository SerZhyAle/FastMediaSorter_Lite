Option Strict On

Imports System.Drawing
Imports System.Globalization
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' "Текущее состояние раздачи" - the tray "Status.." window (SPECIFICATION_SHARE_STATS_
''' AND_TRAY_HUB.md §3.4). Shows the live running/port state plus the worker's LOCAL usage
''' counters (last connection, connections total/since-start, files served, first used) read
''' from Status.Stats. NOT telemetry - the counters live only in stats.json on this PC. A
''' Reset button zeroes them; Refresh re-fetches. Built AutoSize + TableLayoutPanel so it
''' fits its content and scales at any DPI; carries the app icon like the other windows.
''' </summary>
Public NotInheritable Class Share_Status_Form
    Inherits Form

    Private _iconHandle As IntPtr
    Private _status As WorkerStatus
    Private _busy As Boolean

    Private lblState As Label
    Private lblLastConn As Label
    Private lblConns As Label
    Private lblFiles As Label
    Private lblFirst As Label
    Private btnReset As Button
    Private btnRefresh As Button

    Public Sub New(Optional initial As WorkerStatus = Nothing)
        _status = initial
        BuildUi()
    End Sub

    Private Shared ReadOnly Property Rus As Boolean
        Get
            Return Is_Russian_Language
        End Get
    End Property

    Private Sub BuildUi()
        Me.Text = If(Rus, "Текущее состояние раздачи", "Sharing status")
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed, Sub() ShareIcons.FreeIcon(Me.Icon, _iconHandle)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink

        Dim tlp As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 2, .Padding = New Padding(18, 16, 18, 12), .GrowStyle = TableLayoutPanelGrowStyle.AddRows}
        tlp.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        tlp.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        Dim r As Integer = 0
        lblState = AddRow(tlp, If(Rus, "Раздача:", "Sharing:"), r) : r += 1
        lblLastConn = AddRow(tlp, If(Rus, "Последнее подключение:", "Last connection:"), r) : r += 1
        lblConns = AddRow(tlp, If(Rus, "Подключений:", "Connections:"), r) : r += 1
        lblFiles = AddRow(tlp, If(Rus, "Файлов отдано:", "Files served:"), r) : r += 1
        lblFirst = AddRow(tlp, If(Rus, "Используется с:", "In use since:"), r) : r += 1

        Dim lblHint As New Label With {.AutoSize = True, .MaximumSize = New Size(440, 0), .ForeColor = Color.DimGray, .Margin = New Padding(0, 12, 0, 0),
            .Text = If(Rus, "Счётчики хранятся только на этом ПК и никуда не отправляются.",
                            "The counters are kept on this PC only and are never sent anywhere.")}
        tlp.Controls.Add(lblHint, 0, r) : tlp.SetColumnSpan(lblHint, 2) : r += 1

        Dim btnRow As New FlowLayoutPanel With {.AutoSize = True, .Anchor = AnchorStyles.Right, .FlowDirection = FlowDirection.LeftToRight, .WrapContents = False, .Margin = New Padding(0, 16, 0, 0)}
        btnReset = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(10, 5, 10, 5), .Margin = New Padding(0, 0, 8, 0), .Text = If(Rus, "Сбросить счётчики", "Reset counters")}
        btnRefresh = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(10, 5, 10, 5), .Margin = New Padding(0, 0, 8, 0), .Text = If(Rus, "Обновить", "Refresh")}
        Dim btnClose As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(14, 5, 14, 5), .Text = If(Rus, "Закрыть", "Close"), .DialogResult = DialogResult.Cancel}
        AddHandler btnReset.Click, AddressOf OnReset
        AddHandler btnRefresh.Click, AddressOf OnRefresh
        btnRow.Controls.AddRange(New Control() {btnReset, btnRefresh, btnClose})
        tlp.Controls.Add(btnRow, 0, r) : tlp.SetColumnSpan(btnRow, 2)

        Me.Controls.Add(tlp)
        Me.CancelButton = btnClose
        Me.MinimumSize = New Size(380, 0)
        AddHandler Me.Shown, AddressOf OnShownFirst
    End Sub

    ''' <summary>Caption in column 0; returns the value label (column 1) to fill later.</summary>
    Private Shared Function AddRow(tlp As TableLayoutPanel, caption As String, row As Integer) As Label
        tlp.Controls.Add(New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 6, 16, 2), .ForeColor = Color.DimGray, .Text = caption}, 0, row)
        Dim val As New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 6, 0, 2), .Text = "-"}
        tlp.Controls.Add(val, 1, row)
        Return val
    End Function

    Private Async Sub OnShownFirst(sender As Object, e As EventArgs)
        If _status Is Nothing Then
            Await RefreshAsync()
        Else
            Populate()
        End If
    End Sub

    Private Async Sub OnRefresh(sender As Object, e As EventArgs)
        Await RefreshAsync()
    End Sub

    Private Async Sub OnReset(sender As Object, e As EventArgs)
        If _busy Then Return
        _busy = True
        btnReset.Enabled = False
        Try
            Dim st As WorkerStatus = Await ShareController.ResetStatsAsync()
            If st IsNot Nothing Then _status = st
        Catch
        End Try
        If Not IsDisposed Then
            btnReset.Enabled = True
            Populate()
        End If
        _busy = False
    End Sub

    Private Async Function RefreshAsync() As Task
        If _busy Then Return
        _busy = True
        btnRefresh.Enabled = False
        Try
            Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
            If st IsNot Nothing Then _status = st
        Catch
        End Try
        If Not IsDisposed Then
            btnRefresh.Enabled = True
            Populate()
        End If
        _busy = False
    End Function

    Private Sub Populate()
        Dim st As WorkerStatus = _status
        If st Is Nothing Then
            lblState.Text = If(Rus, "нет связи с сервером", "worker unavailable")
            lblLastConn.Text = "-" : lblConns.Text = "-" : lblFiles.Text = "-" : lblFirst.Text = "-"
            Return
        End If
        lblState.Text = If(st.Running, (If(Rus, "работает, порт ", "on, port ") & st.ListenPort.ToString()), If(Rus, "выключена", "off"))

        Dim s As WorkerStats = st.Stats
        If s Is Nothing Then
            Dim na As String = If(Rus, "нет данных", "no data")
            lblLastConn.Text = na : lblConns.Text = na : lblFiles.Text = na : lblFirst.Text = na
            Return
        End If

        Dim never As String = If(Rus, "ещё не было", "never")
        Dim lastAt As String = FormatTime(s.LastConnectionAt)
        If lastAt.Length = 0 Then
            lblLastConn.Text = never
        ElseIf String.IsNullOrEmpty(s.LastConnectionAddress) Then
            lblLastConn.Text = lastAt
        Else
            lblLastConn.Text = lastAt & "  -  " & s.LastConnectionAddress
        End If
        lblConns.Text = String.Format(If(Rus, "всего {0} (с запуска {1})", "{0} total ({1} since start)"), s.TotalConnections, s.ConnectionsSinceStart)
        lblFiles.Text = String.Format(If(Rus, "всего {0} (с запуска {1})", "{0} total ({1} since start)"), s.FilesServedTotal, s.FilesServedSinceStart)
        Dim firstAt As String = FormatTime(s.FirstConnectionAt)
        lblFirst.Text = If(firstAt.Length = 0, never, firstAt)
    End Sub

    ''' <summary>RFC3339 UTC -> local "dd.MM.yyyy HH:mm"; "" when empty/unparseable.</summary>
    Public Shared Function FormatTime(iso As String) As String
        If String.IsNullOrEmpty(iso) Then Return ""
        Dim dto As DateTimeOffset
        If DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal Or DateTimeStyles.AdjustToUniversal, dto) Then
            Return dto.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
        End If
        Return ""
    End Function

    ''' <summary>Compact local "dd.MM HH:mm" for the tray tooltip; "" when empty.</summary>
    Public Shared Function ShortTime(iso As String) As String
        If String.IsNullOrEmpty(iso) Then Return ""
        Dim dto As DateTimeOffset
        If DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal Or DateTimeStyles.AdjustToUniversal, dto) Then
            Return dto.ToLocalTime().ToString("dd.MM HH:mm")
        End If
        Return ""
    End Function

End Class
