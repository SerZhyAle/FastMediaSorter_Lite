Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms

''' <summary>
''' The history of scheduled operations (ticket 222, 7.5): the newest 200 lines of the log, newest first,
''' composed into words NOW from the codes the log holds - so a run written while the interface was in
''' one language reads in the language of today. "Clear history" is one action and leaves the mutation
''' journal alone.
''' </summary>
Friend NotInheritable Class Scheduled_History_Form
    Inherits Form

    Private ReadOnly engine As ScheduledEngine
    Private list As ListView
    Private btnRefresh As Button
    Private btnClear As Button
    Private btnClose As Button
    Private _answerRow As DialogButtonRow
    Private lblEmpty As Label
    Private _iconHandle As IntPtr

    Friend Sub New(engine As ScheduledEngine)
        Me.engine = engine
        BuildUi()
        LoadRows()
    End Sub

    Private Sub BuildUi()
        SuspendLayout()
        UiLanguage.ApplyTo(Me)
        Text = Localization.T("Журнал операций по расписанию")
        Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler FormClosed, Sub() ShareIcons.FreeIcon(Icon, _iconHandle)
        FormBorderStyle = FormBorderStyle.Sizable
        MinimizeBox = False
        MaximizeBox = True
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        MinimumSize = New Size(560, 320)
        ClientSize = New Size(860, 480 + ContextHelpUi.BadgeEdge)

        list = New ListView With {.Dock = DockStyle.Fill, .View = View.Details, .FullRowSelect = True, .HideSelection = False,
            .MultiSelect = False, .Name = "lvSchedHistory", .AccessibleName = Localization.T("Журнал операций по расписанию")}
        list.Columns.Add(Localization.T("Время"), LogicalToDeviceUnits(140))
        list.Columns.Add(Localization.T("Операция"), LogicalToDeviceUnits(170))
        list.Columns.Add(Localization.T("Запуск"), LogicalToDeviceUnits(120))
        list.Columns.Add(Localization.T("Результат"), LogicalToDeviceUnits(380))

        lblEmpty = New Label With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(12, 8, 12, 4),
            .ForeColor = ThemeService.Palette.TextMuted, .Name = "lblSchedHistoryEmpty",
            .Text = Localization.T("Журнал пуст."), .Visible = False}

        ' The answers ride the shared bottom row (ticket 243 T-8b batch C, BUTTONS 2.0 sections 3 and 4): Refresh and Clear are
        ' COMMANDS of the journal (first, set apart from the answer, never the default - Clear still asks, with No as its
        ' default), Close is the one answer. Escape is Close; Enter is Close too (it dismisses, nothing is cleared).
        btnClose = New Button With {.Text = Localization.T("Закрыть"), .DialogResult = DialogResult.Cancel, .Name = "btnSchedHistoryClose"}
        btnClear = New Button With {.Text = Localization.T("Очистить журнал"), .Name = "btnSchedHistoryClear"}
        btnRefresh = New Button With {.Text = Localization.T("Обновить"), .Name = "btnSchedHistoryRefresh"}
        ButtonGlyphs.Apply(btnClear, "action.clear-all", ButtonGlyphRole.Caution)
        AddHandler btnClear.Click, AddressOf OnClearClicked
        AddHandler btnRefresh.Click, Sub() LoadRows()
        _answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "btnSchedHistoryButtons", .Padding = New Padding(12, 8, 12, 8)}
        DpiLayout.AddAnswer(_answerRow, btnRefresh, DialogButtonRole.Command, "action.refresh")
        DpiLayout.AddAnswer(_answerRow, btnClear, DialogButtonRole.Command, "action.clear-all")
        DpiLayout.AddAnswer(_answerRow, btnClose, DialogButtonRole.Dismissing, "action.cancel")

        Controls.Add(list)
        Controls.Add(lblEmpty)
        Controls.Add(_answerRow)
        ' The strip the help badge sits in, so it does not lie over the first column (the last one under right-to-left reading).
        ' Added last: it takes the top edge first and the empty-journal line stands under it.
        Controls.Add(ContextHelpUi.CornerBand())
        _answerRow.Bind(Me)
        AddHandler list.SizeChanged, Sub() FillResultColumn()
        ResumeLayout(True)
        DpiLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' WinForms never scales a ListView's column widths: they were measured before the window had its own DPI.
        list.Columns(0).Width = LogicalToDeviceUnits(140)
        list.Columns(1).Width = LogicalToDeviceUnits(170)
        list.Columns(2).Width = LogicalToDeviceUnits(120)
        FillResultColumn()
        DpiLayout.ClampToWorkingArea(Me)
    End Sub

    ''' <summary>The result is the long column: it takes the width the other three leave, never less than its authored width.</summary>
    Private Sub FillResultColumn()
        If list Is Nothing OrElse list.Columns.Count < 4 Then Return
        Dim others As Integer = list.Columns(0).Width + list.Columns(1).Width + list.Columns(2).Width
        list.Columns(3).Width = Math.Max(LogicalToDeviceUnits(380), list.ClientSize.Width - others)
    End Sub

    Private Sub LoadRows()
        list.BeginUpdate()
        Try
            list.Items.Clear()
            Dim entries As List(Of JournalEntry) = engine.Journal.ReadRecent()
            For Each entry As JournalEntry In entries
                Dim row As New ListViewItem(entry.Timestamp.ToString("g", CultureInfo.CurrentCulture))
                row.SubItems.Add(entry.OperationName)
                row.SubItems.Add(If(entry.Kind = "run", ScheduledText.TriggerText(entry.Trigger), ""))
                row.SubItems.Add(ScheduledText.HistoryResultText(entry))
                list.Items.Add(row)
            Next
            lblEmpty.Visible = entries.Count = 0
            btnClear.Enabled = entries.Count > 0
        Finally
            list.EndUpdate()
        End Try
    End Sub

    Private Sub OnClearClicked(sender As Object, e As EventArgs)
        If ManagerBox.Show(Me, Localization.T("Очистить журнал операций по расписанию? Сами операции и их расписание не изменятся."),
                           Localization.T("Журнал операций по расписанию"), MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                           MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        engine.Journal.ClearHistory()
        LoadRows()
    End Sub

End Class
