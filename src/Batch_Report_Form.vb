#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

''' <summary>
''' What a batch did to each file (005_SPECIFICATION_THUMBNAIL_GRID_TRIAGE_DOTNET10.md PG-6): the
''' one place where a SkipReason or a failure category is spelled out per file. Opened from the
''' report line at the bottom of the F3 panel - never by itself, because a modal window that
''' appears when a run ends is exactly the interruption the strip was built to avoid.
''' </summary>
Friend NotInheritable Class Batch_Report_Form
    Inherits Form

    Private ReadOnly list As New ListView()
    Private ReadOnly summary As New Label()
    Private ReadOnly close_Button As New Button()

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
        close_Button.Dock = DockStyle.Bottom
        close_Button.Height = 32
        close_Button.DialogResult = DialogResult.OK
        AcceptButton = close_Button
        CancelButton = close_Button

        list.Dock = DockStyle.Fill
        list.View = View.Details
        list.FullRowSelect = True
        list.HideSelection = False
        list.MultiSelect = True
        list.Columns.Add(Localization.T("Файл"), 280)
        list.Columns.Add(Localization.T("Состояние"), 100)
        list.Columns.Add(Localization.T("Подробности"), 300)

        ' Worst first: the failures and the skipped are what somebody opens this for; the
        ' hundred that simply went through follow.
        Dim ordered As List(Of BatchItemResult) = report.Results.OrderBy(Function(r) Rank(r.State)).ToList()
        list.BeginUpdate()
        For Each result As BatchItemResult In ordered
            Dim item As New ListViewItem(result.Name)
            item.SubItems.Add(BatchReport.StateText(result.State))
            item.SubItems.Add(BatchReport.DetailText(result))
            item.ToolTipText = result.Path
            list.Items.Add(item)
        Next
        list.EndUpdate()

        Controls.Add(list)
        Controls.Add(close_Button)
        Controls.Add(summary)
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
