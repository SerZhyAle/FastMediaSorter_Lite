Option Strict On
Option Explicit On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Globalization
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Modal dialog for viewing active remote administration sessions and the recent in-memory audit trail (spec 246 Group D).
''' Shows connected FMS_A / FMS_WEB / local admin streams and the 100-event mutating audit ring buffer.
''' </summary>
Friend NotInheritable Class Exchange_Admin_Audit_Form
    Inherits Form

    Private Const ContentWidth As Integer = 580

    Private _iconHandle As IntPtr
    Private _lvSessions As ListView
    Private _lvRecords As ListView
    Private _btnRefresh As Button
    Private _btnClose As Button
    Private _answerRow As DialogButtonRow
    Private _auditView As ExchangeAdminAuditView

    Friend Sub New(initialView As ExchangeAdminAuditView)
        _auditView = initialView
        BuildUi()
    End Sub

    Private Sub BuildUi()
        UiLanguage.ApplyTo(Me)
        Me.Text = ExchangeAdminText.AuditDialogTitle()
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed, Sub() ShareIcons.FreeIcon(Me.Icon, _iconHandle)

        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ClientSize = New Size(ContentWidth, 520)

        Dim root As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 1,
            .Padding = New Padding(14, 12, 14, 10)
        }
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        Dim lblSessions As New Label With {
            .AutoSize = True,
            .Font = New Font(Me.Font, FontStyle.Bold),
            .Margin = New Padding(0, 0, 0, 4),
            .Text = ExchangeAdminText.ActiveSessionsTitle()
        }
        ExchangeMark.Apply(lblSessions, 24, AddressOf CompanionAssets.OpenBundledAsset)
        root.Controls.Add(lblSessions)

        _lvSessions = New ListView With {
            .View = View.Details,
            .FullRowSelect = True,
            .HideSelection = False,
            .MultiSelect = False,
            .Width = ContentWidth - 28,
            .Height = 110,
            .Margin = New Padding(0, 0, 0, 10)
        }
        _lvSessions.Columns.Add(ExchangeAdminText.ColumnRemoteAddr(), Me.LogicalToDeviceUnits(130))
        _lvSessions.Columns.Add(ExchangeAdminText.ColumnOperator(), Me.LogicalToDeviceUnits(90))
        _lvSessions.Columns.Add(Localization.T("Сессия"), Me.LogicalToDeviceUnits(110))
        _lvSessions.Columns.Add(Localization.T("Подключено"), Me.LogicalToDeviceUnits(100))
        _lvSessions.Columns.Add(Localization.T("Последняя операция"), Me.LogicalToDeviceUnits(110))
        root.Controls.Add(_lvSessions)

        Dim lblAudit As New Label With {
            .AutoSize = True,
            .Font = New Font(Me.Font, FontStyle.Bold),
            .Margin = New Padding(0, 4, 0, 4),
            .Text = Localization.T("Журнал операций (последние 100 событий)")
        }
        root.Controls.Add(lblAudit)

        _lvRecords = New ListView With {
            .View = View.Details,
            .FullRowSelect = True,
            .HideSelection = False,
            .MultiSelect = False,
            .Width = ContentWidth - 28,
            .Height = 220,
            .Margin = New Padding(0, 0, 0, 10)
        }
        _lvRecords.Columns.Add(ExchangeAdminText.ColumnSeq(), Me.LogicalToDeviceUnits(40))
        _lvRecords.Columns.Add(ExchangeAdminText.ColumnTime(), Me.LogicalToDeviceUnits(120))
        _lvRecords.Columns.Add(ExchangeAdminText.ColumnRemoteAddr(), Me.LogicalToDeviceUnits(100))
        _lvRecords.Columns.Add(ExchangeAdminText.ColumnOperator(), Me.LogicalToDeviceUnits(80))
        _lvRecords.Columns.Add(ExchangeAdminText.ColumnOperation(), Me.LogicalToDeviceUnits(110))
        _lvRecords.Columns.Add(ExchangeAdminText.ColumnTarget(), Me.LogicalToDeviceUnits(90))
        _lvRecords.Columns.Add(ExchangeAdminText.ColumnStatus(), Me.LogicalToDeviceUnits(60))
        root.Controls.Add(_lvRecords)

        ' Refresh is a command of the window and Close its one answer, so they share the bottom row (ticket 243 T-8b,
        ' BUTTONS 2.0 sections 3 and 4): the command first, set apart, then Close - the same sides as before. Escape =
        ' Close; Enter now reaches Close too (the safe default of a row without an affirmative answer), never Refresh.
        _btnClose = New Button With {
            .Name = "xaud_Close",
            .Text = Localization.T("Закрыть"),
            .DialogResult = DialogResult.Cancel
        }
        _btnRefresh = New Button With {
            .Name = "xaud_Refresh",
            .Text = Localization.T("Обновить")
        }
        AddHandler _btnRefresh.Click, AddressOf OnRefreshClicked

        _answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "xaud_Buttons", .Padding = New Padding(14, 4, 14, 10)}
        DpiLayout.AddAnswer(_answerRow, _btnRefresh, DialogButtonRole.Command, "action.refresh")
        DpiLayout.AddAnswer(_answerRow, _btnClose, DialogButtonRole.Dismissing, "nav.close")

        Me.Controls.Add(root)
        ' Last in the collection: the docked row takes its edge first and reads after the lists in the Tab order.
        Me.Controls.Add(_answerRow)
        _answerRow.Bind(Me)

        PopulateViews()
        DpiLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
        DpiLayout.CenterOnOwner(Me)
    End Sub

    Private Sub PopulateViews()
        _lvSessions.BeginUpdate()
        Try
            _lvSessions.Items.Clear()
            If _auditView IsNot Nothing AndAlso _auditView.ActiveSessions IsNot Nothing Then
                For Each s As ExchangeAdminSession In _auditView.ActiveSessions
                    Dim item As New ListViewItem(s.RemoteAddr)
                    item.SubItems.Add(If(String.IsNullOrEmpty(s.ClientProduct), "unknown", s.ClientProduct))
                    item.SubItems.Add(s.SessionId)
                    item.SubItems.Add(s.ConnectedAt)
                    item.SubItems.Add(s.LastOp)
                    _lvSessions.Items.Add(item)
                Next
            End If
        Finally
            _lvSessions.EndUpdate()
        End Try

        _lvRecords.BeginUpdate()
        Try
            _lvRecords.Items.Clear()
            If _auditView IsNot Nothing AndAlso _auditView.Records IsNot Nothing Then
                For Each r As ExchangeAdminAuditRecord In _auditView.Records
                    Dim item As New ListViewItem(r.Seq.ToString(CultureInfo.InvariantCulture))
                    item.SubItems.Add(r.Timestamp)
                    item.SubItems.Add(r.RemoteAddr)
                    item.SubItems.Add(If(String.IsNullOrEmpty(r.ClientProduct), r.OperatorName, r.ClientProduct))
                    item.SubItems.Add(r.Op)
                    item.SubItems.Add(If(String.IsNullOrEmpty(r.Target), "-", r.Target))
                    item.SubItems.Add(r.Status)
                    _lvRecords.Items.Add(item)
                Next
            End If
        Finally
            _lvRecords.EndUpdate()
        End Try
    End Sub

    Private Async Sub OnRefreshClicked(sender As Object, e As EventArgs)
        _btnRefresh.Enabled = False
        Try
            Dim reply As ExchangeAdminReply = Await ExchangeAdmin.ListAdminAuditAsync(CancellationToken.None).ConfigureAwait(True)
            If reply.Ok Then
                _auditView = ExchangeAdminParser.Audit(reply.DataJson)
                PopulateViews()
            End If
        Catch ex As Exception
            AppFileLogger.LogException("Exchange: refreshing audit view", ex)
        Finally
            _btnRefresh.Enabled = True
        End Try
    End Sub

End Class
