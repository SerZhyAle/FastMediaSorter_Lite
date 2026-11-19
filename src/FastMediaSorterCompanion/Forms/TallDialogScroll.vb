Option Strict On
Option Explicit On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Rule S6 (2) of L0361 for an auto-sized dialog: a body of fields (<c>Dock = Fill</c>) over a bottom-docked answer row.
''' An auto-sized window takes its preferred size, so <c>DpiLayout.ClampToWorkingArea</c> cannot shrink it, and a body taller
''' than the work area (a small screen at a high scale, a long language) pushed the answer row below the screen edge.
''' <see cref="KeepReachable"/> is the generic form of what <c>Exchange_Account_Form.FitAccountToWorkingArea</c> does for its
''' own window: when the content does not fit, the window stops auto-sizing, the body scrolls, the row stays whole. A window
''' that fits, or whose shape is not "Fill body over a bottom row", is not touched.
''' </summary>
Friend NotInheritable Class TallDialogScroll

    Private Sub New()
    End Sub

    ''' <summary>Call from <c>OnLoad</c>, after <c>MyBase.OnLoad</c> (the window then has its scale and its monitor).</summary>
    Friend Shared Sub KeepReachable(f As Form)
        If f Is Nothing OrElse f.IsDisposed OrElse Not f.AutoSize Then Return
        Dim body As Control = Nothing
        Dim row As Control = Nothing
        For Each child As Control In f.Controls
            If child.Dock = DockStyle.Fill AndAlso body Is Nothing Then body = child
            If child.Dock = DockStyle.Bottom AndAlso row Is Nothing Then row = child
        Next
        If body Is Nothing Then Return
        f.PerformAutoScale()
        Dim area As Rectangle = DpiLayout.WorkingAreaFor(f)
        Dim content As Size = body.GetPreferredSize(Size.Empty)
        Dim rowHeight As Integer = If(row Is Nothing, 0, row.GetPreferredSize(New Size(content.Width, 0)).Height)
        ' Control.SizeFromClientSize is Protected: the window frame (title bar and borders) is the difference between
        ' the outer size and the client size, which does not depend on the content.
        Dim frame As Size = f.Size - f.ClientSize
        Dim desired As Size = New Size(content.Width + frame.Width, content.Height + rowHeight + frame.Height)
        If desired.Height <= area.Height AndAlso desired.Width <= area.Width Then Return
        ' What scrolls is the body of fields; the row never does, so the answers stay on screen.
        f.AutoSize = False
        Dim scrollable As ScrollableControl = TryCast(body, ScrollableControl)
        If scrollable Is Nothing Then Return
        scrollable.AutoScroll = True
        Dim table As TableLayoutPanel = TryCast(body, TableLayoutPanel)
        If table IsNot Nothing Then
            ' A table's own scroll extent is short of its rows (it leaves the padding out): the extent follows the rows.
            OptionRows.KeepScrollExtent(table)
            AddHandler table.Layout, Sub() OptionRows.KeepScrollExtent(table)
        End If
        Dim bar As Integer = If(desired.Height > area.Height, UiSizing.VerticalScrollBarWidthForDpi(f.DeviceDpi), 0)
        f.Size = New Size(Math.Min(desired.Width + bar, area.Width), Math.Min(desired.Height, area.Height))
        DpiLayout.ClampToWorkingArea(f)
    End Sub

End Class
