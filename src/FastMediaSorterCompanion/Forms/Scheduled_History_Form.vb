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
        ClientSize = New Size(860, 480)

        list = New ListView With {.Dock = DockStyle.Fill, .View = View.Details, .FullRowSelect = True, .HideSelection = False,
            .MultiSelect = False, .Name = "lvSchedHistory", .AccessibleName = Localization.T("Журнал операций по расписанию")}
        list.Columns.Add(Localization.T("Время"), LogicalToDeviceUnits(140))
        list.Columns.Add(Localization.T("Операция"), LogicalToDeviceUnits(170))
        list.Columns.Add(Localization.T("Запуск"), LogicalToDeviceUnits(120))
        list.Columns.Add(Localization.T("Результат"), LogicalToDeviceUnits(380))

        lblEmpty = New Label With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(12, 8, 12, 4),
            .ForeColor = ThemeService.Palette.TextMuted, .Name = "lblSchedHistoryEmpty",
            .Text = Localization.T("Журнал пуст."), .Visible = False}

        Dim footer As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.RightToLeft, .Padding = New Padding(12, 8, 12, 8), .WrapContents = False}
        btnClose = New Button With {.Text = Localization.T("Закрыть"), .AutoSize = True, .DialogResult = DialogResult.Cancel,
            .Name = "btnSchedHistoryClose", .Margin = New Padding(8, 0, 0, 0), .Padding = New Padding(14, 5, 14, 5)}
        btnClear = New Button With {.Text = Localization.T("Очистить журнал"), .AutoSize = True, .Name = "btnSchedHistoryClear",
            .Margin = New Padding(8, 0, 0, 0), .Padding = New Padding(14, 5, 14, 5)}
        btnRefresh = New Button With {.Text = Localization.T("Обновить"), .AutoSize = True, .Name = "btnSchedHistoryRefresh",
            .Margin = New Padding(8, 0, 0, 0), .Padding = New Padding(14, 5, 14, 5)}
        ButtonGlyphs.Apply(btnClose, "action.cancel", ButtonGlyphRole.Dismissing)
        ButtonGlyphs.Apply(btnClear, "action.clear-all", ButtonGlyphRole.Caution)
        ButtonGlyphs.Apply(btnRefresh, "action.refresh")
        AddHandler btnClear.Click, AddressOf OnClearClicked
        AddHandler btnRefresh.Click, Sub() LoadRows()
        footer.Controls.AddRange({btnClose, btnClear, btnRefresh})
        CancelButton = btnClose

        Controls.Add(list)
        Controls.Add(lblEmpty)
        Controls.Add(footer)
        ResumeLayout(True)
        DpiLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
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
        If MessageBox.Show(Me, Localization.T("Очистить журнал операций по расписанию? Сами операции и их расписание не изменятся."),
                           Localization.T("Журнал операций по расписанию"), MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                           MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        engine.Journal.ClearHistory()
        LoadRows()
    End Sub

End Class
