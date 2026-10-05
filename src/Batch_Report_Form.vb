#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms

''' <summary>
''' What a batch did to each file (005_SPECIFICATION_THUMBNAIL_GRID_TRIAGE_DOTNET10.md PG-6): the
''' one place where a SkipReason or a failure category is spelled out per file. Opened from the
''' report line at the bottom of the F3 panel - never by itself, because a modal window that
''' appears when a run ends is exactly the interruption the strip was built to avoid.
'''
''' Since 126 UX-610 the report can be copied: Ctrl+C or the "Копировать" button puts the full
''' path of every selected row (all rows when none is selected) on the clipboard - the path used
''' to live in a tooltip only - and "Показать в проводнике" opens a folder window on the first
''' selected row's file.
''' </summary>
Friend NotInheritable Class Batch_Report_Form
    Inherits Form

    Private ReadOnly list As New ListView()
    Private ReadOnly summary As New Label()
    Private ReadOnly close_Button As New Button()
    Private ReadOnly copy_Button As New Button()
    Private ReadOnly folder_Button As New Button()

    Friend Sub New(report As BatchReport)
        Text = Localization.T("Отчёт об операции")
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.SizableToolWindow
        ShowInTaskbar = False
        MinimumSize = New Size(480, 260)
        Size = New Size(720, 460)
        KeyPreview = True

        Dim family As String = Localization.FontFamily()
        Font = New Font(family, 9.0F)
        If Localization.IsRightToLeft() Then RightToLeft = RightToLeft.Yes

        summary.Dock = DockStyle.Top
        summary.Height = 34
        summary.Padding = New Padding(10, 8, 10, 0)
        summary.Text = report.SummaryLine()
        summary.AutoEllipsis = True

        close_Button.Text = Localization.T("Закрыть")
        close_Button.DialogResult = DialogResult.OK
        AcceptButton = close_Button
        CancelButton = close_Button

        copy_Button.Text = Localization.T("Копировать")
        AddHandler copy_Button.Click, Sub(s, e) CopyRows()

        folder_Button.Text = Localization.T("Показать в проводнике")
        folder_Button.Enabled = False
        AddHandler folder_Button.Click, Sub(s, e) ShowFirstSelectedInFolder()

        ' The buttons ride one bottom band, way out on the right like the panel's own strip.
        ' Reverse add order, reverse dock order: the LAST control added docks first, so
        ' "Закрыть" - added last - ends up rightmost, under the right hand.
        Dim bottom As New Panel With {.Dock = DockStyle.Bottom, .Height = 32, .Padding = New Padding(8, 0, 0, 0)}
        close_Button.Dock = DockStyle.Right
        close_Button.Width = 110
        copy_Button.Dock = DockStyle.Right
        copy_Button.Width = 120
        folder_Button.Dock = DockStyle.Right
        folder_Button.Width = 190
        bottom.Controls.Add(folder_Button)
        bottom.Controls.Add(copy_Button)
        bottom.Controls.Add(close_Button)

        list.Dock = DockStyle.Fill
        list.View = View.Details
        list.FullRowSelect = True
        list.HideSelection = False
        list.MultiSelect = True
        list.Columns.Add(Localization.T("Файл"), 280)
        list.Columns.Add(Localization.T("Состояние"), 100)
        list.Columns.Add(Localization.T("Подробности"), 300)
        AddHandler list.SelectedIndexChanged, Sub(s, e) folder_Button.Enabled = list.SelectedItems.Count > 0

        ' Worst first: the failures and the skipped are what somebody opens this for; the
        ' hundred that simply went through follow.
        Dim ordered As List(Of BatchItemResult) = report.Results.OrderBy(Function(r) Rank(r.State)).ToList()
        list.BeginUpdate()
        For Each result As BatchItemResult In ordered
            Dim item As New ListViewItem(result.Name)
            item.SubItems.Add(BatchReport.StateText(result.State))
            item.SubItems.Add(BatchReport.DetailText(result))
            item.ToolTipText = result.Path
            ' The full path is what a copy is for; the tag carries it out of the tooltip.
            item.Tag = result.Path
            list.Items.Add(item)
        Next
        list.EndUpdate()

        Controls.Add(list)
        Controls.Add(bottom)
        Controls.Add(summary)

        ' LAST line of the builder: scaling touches only the children that exist by now (124 UX-202).
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    ''' <summary>Ctrl+C: the report's whole point since 126 UX-610. The form carries the
    ''' chord (KeyPreview is on), so it copies whatever holds focus - the list, or one of
    ''' the buttons after a tab.</summary>
    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        MyBase.OnKeyDown(e)
        If e.Control AndAlso Not e.Alt AndAlso e.KeyCode = Keys.C Then
            e.Handled = True
            CopyRows()
        End If
    End Sub

    ''' <summary>The full path of every selected row, one per line - or of every row when
    ''' the selection is empty, which is what "send the list of problem files" means. The
    ''' clipboard can be held by another process; a refusal here is logged, not thrown.</summary>
    Private Sub CopyRows()
        Dim items As IEnumerable(Of ListViewItem) =
            If(list.SelectedItems.Count > 0,
               list.SelectedItems.Cast(Of ListViewItem)(),
               list.Items.Cast(Of ListViewItem)())
        Dim paths As IEnumerable(Of String) = items.
            Select(Function(item) TryCast(item.Tag, String)).
            Where(Function(path) Not String.IsNullOrEmpty(path))
        Dim text As String = String.Join(Environment.NewLine, paths)
        If text.Length = 0 Then Return

        Try
            Clipboard.SetText(text)
        Catch ex As Exception
            AppFileLogger.LogException("Batch report: copying to the clipboard", ex)
        End Try
    End Sub

    ''' <summary>Opens a folder window on the first selected row's file. A row whose file
    ''' has already gone somewhere else falls back to its folder; a row from nowhere
    ''' stays a click that does nothing but log.</summary>
    Private Sub ShowFirstSelectedInFolder()
        Dim item As ListViewItem = If(list.SelectedItems.Count > 0, list.SelectedItems(0), Nothing)
        If item Is Nothing Then Return
        ' Named filePath rather than "path": a local of that name would shadow System.IO.Path
        ' (VB ignores case) and the Path calls below would compile against the String.
        Dim filePath As String = TryCast(item.Tag, String)
        If String.IsNullOrEmpty(filePath) Then Return

        Try
            If File.Exists(filePath) Then
                Process.Start(New ProcessStartInfo("explorer.exe", "/select,""" & filePath & """") With {.UseShellExecute = True})
            ElseIf Directory.Exists(Path.GetDirectoryName(filePath)) Then
                Process.Start(New ProcessStartInfo("explorer.exe", """" & Path.GetDirectoryName(filePath) & """") With {.UseShellExecute = True})
            End If
        Catch ex As Exception
            AppFileLogger.LogException("Batch report: show in Explorer", ex)
        End Try
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' WinForms never scales a ListView's column widths; they are logical 96-dpi pixels until the window has its DPI.
        list.Columns(0).Width = DialogLayout.Device(list, 280)
        list.Columns(1).Width = DialogLayout.Device(list, 100)
        list.Columns(2).Width = DialogLayout.Device(list, 300)
    End Sub

    ''' <summary>The order of the groups. OrderBy is stable, so inside a group the files keep the
    ''' order they were given in.</summary>
    Private Shared Function Rank(state As BatchItemState) As Integer
        Select Case state
            Case BatchItemState.Failed : Return 0
            Case BatchItemState.NotStarted : Return 1
            Case BatchItemState.Skipped : Return 2
            Case Else : Return 3
        End Select
    End Function

End Class
#End If
