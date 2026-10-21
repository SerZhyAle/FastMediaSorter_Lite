Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Runtime.CompilerServices
Imports System.Windows.Forms

''' <summary>How a boolean row takes its width inside a table cell or a flow (<see cref="OptionRows.InFlow"/>).</summary>
Friend Enum OptionRowFit
    ''' <summary>The width of the table cell it stands in (<c>Dock = Top</c>): the caption wraps in what is left of the cell.</summary>
    Fill = 0
    ''' <summary>As wide as its caption needs on one line (capped), for a cell or a flow that takes the widest child: the row
    ''' is as wide as it must be and the caption wraps only when the cap is reached.</summary>
    Content = 1
End Enum

''' <summary>
''' The Share Manager's way onto the boolean option row of BOOLEAN-OPTION 2.0 sections 2 and 3 (ticket 243 T-11c-b): the
''' viewer's <see cref="DialogOptionRow"/> (a leading drawn box of 20 px in a 32 px hit target, a caption that toggles once,
''' an explanation that never toggles, a row as tall as its content), linked into this program, behind two one-line calls so
''' a window changes only where it creates the check box. The check box keeps its name, its handlers, its <c>Checked</c>
''' default, its place in the Tab order and its persistence - the call returns the ROW, which the caller adds to the parent
''' INSTEAD of the check box.
'''
'''   * <see cref="At"/> - a hand-positioned window (96-dpi coordinates, scaled by <c>DpiLayout.ApplyAutoScale</c>). The row
'''     takes the check box's place in its parent's control list; <see cref="Fit"/> (called from <c>OnLoad</c>) moves what
'''     is under a row that grew.
'''   * <see cref="InFlow"/> - a body of table and flow panels: the table makes room for the taller row by itself;
'''     <see cref="ArrangeAll"/> (called from <c>OnLoad</c>, after the window has its scale) lays the rows out.
'''
''' A row is NOT arranged when it is made: the window is authored in 96-dpi pixels and the hit target is the one control whose
''' size was authored (32 px), so a row arranged before the framework scales the window would be scaled twice. The first
''' arrangement is the window's, from <c>OnLoad</c>; every later change of width, font, direction or DPI re-arranges the row
''' itself (<see cref="OptionRowHost"/>). Nothing here is shown in the net48 fallback: this program is net10 only.
''' </summary>
Friend NotInheritable Class OptionRows

    Private Sub New()
    End Sub

    ''' <summary>The box's distance from the cell edge in a flow: the 6 px that the 32 px hit target reaches past the box, so the
    ''' target itself starts at the cell edge and never reaches into a neighbour.</summary>
    Friend Const FlowEdgeInsetLogical As Integer = (UiSizing.PointerTarget - UiSizing.PointerCheckBox) \ 2

    ''' <summary>The longest one-line width a <see cref="OptionRowFit.Content"/> row asks for (96-dpi px); a longer caption wraps.</summary>
    Friend Const ContentWidthCapLogical As Integer = 320

    Private Shared ReadOnly contentFit As New ConditionalWeakTable(Of OptionRowHost, Object)()

    ' --- the two ways to make a row ---------------------------------------------------------------------

    ''' <summary>A row for a hand-positioned window: the check box (and the hint label under it, when the window has one) become
    ''' the row. It takes the check box's place in the parent's control list, so the Tab order is the one it was.</summary>
    ''' <param name="check">The window's check box; its <c>Text</c> becomes the caption.</param>
    ''' <param name="hint">The hint label under it, or Nothing; it becomes the explanation.</param>
    ''' <param name="boxLeft">Where the native check box's left edge was (96-dpi px).</param>
    ''' <param name="top">The row's top edge (96-dpi px).</param>
    ''' <param name="rightEdge">The reading-end edge of the row: the window's client width.</param>
    ''' <param name="height">The authored height at 96 dpi; the real one is decided when the window has its scale.</param>
    Friend Shared Function At(check As CheckBox, hint As Label, boxLeft As Integer, top As Integer, rightEdge As Integer,
                              Optional height As Integer = DialogOptionRow.RowHeightLogical) As OptionRowHost
        If check Is Nothing Then Throw New ArgumentNullException(NameOf(check))
        PrepareHint(hint)
        Dim parent As Control = check.Parent
        Dim place As Integer = -1
        ' The Tab order is the TabIndex, not the place in the list: a new panel would get the next free index (after every
        ' control the window already has), so the row takes the index the box had.
        Dim tab As Integer = check.TabIndex
        If parent IsNot Nothing Then
            ' The row replaces the check box AT ITS PLACE: count what stands before it that stays in the parent.
            place = 0
            For Each sibling As Control In parent.Controls
                If Object.ReferenceEquals(sibling, check) Then Exit For
                If Not Object.ReferenceEquals(sibling, hint) Then place += 1
            Next
        End If
        Dim row As OptionRowHost = DialogOptionRow.CreateAt(check, hint, boxLeft, top, rightEdge, height)
        If parent IsNot Nothing Then
            parent.Controls.Add(row)
            parent.Controls.SetChildIndex(row, Math.Min(place, parent.Controls.Count - 1))
            row.TabIndex = tab
        End If
        Return row
    End Function

    ''' <summary>A row for a table cell or a flow. The caller adds the returned row where the check box would have gone
    ''' (<c>table.Controls.Add(OptionRows.InFlow(check), column, row)</c>, <c>flow.Controls.Add(..)</c>).</summary>
    ''' <param name="check">The window's check box; its <c>Text</c> becomes the caption.</param>
    ''' <param name="hint">The hint label that explained it, or Nothing; it becomes the explanation under the caption.</param>
    ''' <param name="fit"><see cref="OptionRowFit.Fill"/> for a cell that gives the row its width, <see cref="OptionRowFit.Content"/>
    ''' for a cell or a flow that takes the widest child.</param>
    Friend Shared Function InFlow(check As CheckBox, Optional hint As Label = Nothing,
                                  Optional fit As OptionRowFit = OptionRowFit.Fill) As OptionRowHost
        If check Is Nothing Then Throw New ArgumentNullException(NameOf(check))
        PrepareHint(hint)
        Dim row As OptionRowHost = DialogOptionRow.Create(check, hint, New Rectangle(0, 0, 160, DialogOptionRow.RowHeightLogical), FlowEdgeInsetLogical)
        row.Margin = Padding.Empty
        If fit = OptionRowFit.Fill Then
            row.Dock = DockStyle.Top
        Else
            row.Anchor = AnchorStyles.Left Or AnchorStyles.Top
            contentFit.Add(row, Nothing)
        End If
        Return row
    End Function

    ''' <summary>The hint becomes the explanation: a label the row sizes itself (wrapped at the caption's inset, as tall as its text
    ''' needs), not one that sizes itself on one line or hangs on an anchor of its own.</summary>
    Private Shared Sub PrepareHint(hint As Label)
        If hint Is Nothing Then Return
        hint.AutoSize = False
        hint.AutoEllipsis = False
        hint.Dock = DockStyle.None
        hint.Anchor = AnchorStyles.Top Or AnchorStyles.Left
        hint.MaximumSize = Size.Empty
        hint.Margin = Padding.Empty
    End Sub

    ''' <summary>The row a check box stands in, or Nothing for a check box that is still a native control.</summary>
    Friend Shared Function RowOf(check As CheckBox) As OptionRowHost
        Return If(check Is Nothing, Nothing, TryCast(check.Parent, OptionRowHost))
    End Function

    ' --- the window's part: arrange once it has its scale -----------------------------------------------

    ''' <summary>Lays out every row below <paramref name="root"/> for the window's scale, direction and font, then lets the
    ''' tables and flows settle on the heights the rows ended with. Call it from <c>OnLoad</c> after <c>MyBase.OnLoad</c>
    ''' (the window has its scale then); it also serves a panel that was moved into another window after that window was
    ''' scaled. A window with no row is not touched.</summary>
    Friend Shared Sub ArrangeAll(root As Control)
        If root Is Nothing OrElse root.IsDisposed Then Return
        Dim rows As New List(Of OptionRowHost)()
        Collect(root, rows)
        If rows.Count = 0 Then Return
        ' The framework scales a window once, on its first layout; the call is idempotent and makes sure it ran.
        Dim form As ContainerControl = TryCast(root, ContainerControl)
        If form IsNot Nothing AndAlso form.AutoScaleMode <> AutoScaleMode.None Then form.PerformAutoScale()
        For Each row As OptionRowHost In rows
            If contentFit.TryGetValue(row, Nothing) Then row.Width = ContentWidth(row)
            row.Arrange()
        Next
        Relayout(root)
        ' A table gives a Fill row its cell width in the pass above, and the row re-arranges itself at that width; one more
        ' pass of the tables settles the heights that came out of it.
        Relayout(root)
    End Sub

    Private Shared Sub Collect(parent As Control, into As List(Of OptionRowHost))
        For Each child As Control In parent.Controls
            Dim row As OptionRowHost = TryCast(child, OptionRowHost)
            If row IsNot Nothing Then
                into.Add(row)
            Else
                Collect(child, into)
            End If
        Next
    End Sub

    Private Shared Sub Relayout(parent As Control)
        For Each child As Control In parent.Controls
            If child.Controls.Count > 0 AndAlso Not (TypeOf child Is OptionRowHost) Then Relayout(child)
        Next
        parent.PerformLayout()
    End Sub

    ''' <summary>The width a one-line caption needs at the row's own scale and font, capped: the box and its gap, the caption's
    ''' text, the end inset and a little slack for the difference between the measure and the paint.</summary>
    Private Shared Function ContentWidth(row As OptionRowHost) As Integer
        Dim scale As Single = DialogOptionRow.ScaleOf(row)
        Dim lead As Integer = SettingsCheckBox.Px(FlowEdgeInsetLogical, scale) + SettingsCheckBox.BoxSide(scale) + SettingsCheckBox.Px(SettingsCheckBox.CaptionGapLogical, scale)
        Dim tail As Integer = SettingsCheckBox.Px(SettingsCheckBox.TextEndInsetLogical, scale) + SettingsCheckBox.Px(6, scale)
        Dim text As Integer = TextRenderer.MeasureText(row.Caption.Text, row.Caption.Font).Width
        Dim wanted As Integer = lead + text + tail
        Return Math.Max(SettingsCheckBox.ControlSide(scale), Math.Min(wanted, SettingsCheckBox.Px(ContentWidthCapLogical, scale)))
    End Function

    ''' <summary>A scrolling table reaches the bottom edge of its last row and the padding under it. A table's own scroll extent
    ''' is short of its rows (it leaves the padding out), so the tail of the last row would stay out of reach. Call it when the
    ''' table starts to scroll and from its <c>Layout</c> event: it only writes when the wanted extent differs.</summary>
    Friend Shared Sub KeepScrollExtent(body As TableLayoutPanel)
        If body Is Nothing OrElse Not body.AutoScroll Then Return
        Dim wanted As Integer = body.Padding.Vertical + 8
        For Each h As Integer In body.GetRowHeights()
            wanted += h
        Next
        If body.AutoScrollMinSize.Height <> wanted Then body.AutoScrollMinSize = New Size(0, wanted)
    End Sub

    ' --- hand-positioned windows: the controls under a row that grew move down --------------------------

    ''' <summary>The Manager's twin of the viewer's <c>DialogLayout.FitContent</c> for the rows only: every row of a
    ''' hand-positioned window is asked for the height its content needs at the window's scale, the controls under a row that
    ''' grew move down by what it grew (two rows growing side by side push what is under them once), and the window gets
    ''' taller by the total and is kept on its monitor. Call it from <c>OnLoad</c> after <c>MyBase.OnLoad</c>; a window whose
    ''' rows are tall enough is not touched. Docked children stay with their dock.</summary>
    Friend Shared Sub Fit(f As Form)
        If f Is Nothing OrElse f.IsDisposed Then Return
        f.PerformAutoScale()
        Dim kids As New List(Of Control)()
        For Each c As Control In f.Controls
            If c.Visible AndAlso c.Dock = DockStyle.None Then kids.Add(c)
        Next
        If kids.Count = 0 Then Return

        Dim originals As New List(Of Rectangle)()
        Dim needed As New List(Of Integer)()
        Dim any As Boolean = False
        For Each c As Control In kids
            originals.Add(c.Bounds)
        Next
        For Each c As Control In kids
            ' Asking a row arranges it, so a row that needs no more room is laid out too.
            Dim h As Integer = DialogOptionRow.NeededHeight(c)
            needed.Add(h)
            If h > 0 Then any = True
        Next
        If Not any Then Return

        Dim sizeBefore As Size = f.Size
        Dim clientGrowth As Integer
        Dim grown As Rectangle() = GrowVertical(originals, needed, clientGrowth)
        f.SuspendLayout()
        Try
            For i As Integer = 0 To kids.Count - 1
                kids(i).SetBounds(grown(i).Left, grown(i).Top, kids(i).Width, Math.Max(kids(i).Height, grown(i).Height))
            Next
            If clientGrowth > 0 Then f.ClientSize = New Size(f.ClientSize.Width, f.ClientSize.Height + clientGrowth)
        Finally
            f.ResumeLayout(True)
        End Try
        If f.Size <> sizeBefore AndAlso (f.StartPosition = FormStartPosition.CenterParent OrElse f.StartPosition = FormStartPosition.CenterScreen) Then
            f.Location = New Point(f.Left - (f.Width - sizeBefore.Width) \ 2, f.Top - (f.Height - sizeBefore.Height) \ 2)
        End If
        DpiLayout.ClampToWorkingArea(f)
    End Sub

    ''' <summary>New bounds after the items that need more height have grown: the shift of an item is the largest total growth
    ''' along any chain of items above it (an item is "above" when its bottom edge is at or over this one's top). The same rule
    ''' as the viewer's <c>DialogReflow.GrowVertical</c>, which names viewer services and is not linked here.</summary>
    Friend Shared Function GrowVertical(bounds As IList(Of Rectangle), neededHeights As IList(Of Integer), ByRef clientGrowth As Integer) As Rectangle()
        Dim count As Integer = bounds.Count
        Dim result(count - 1) As Rectangle
        Dim delta(count - 1) As Integer
        Dim shift(count - 1) As Integer
        For i As Integer = 0 To count - 1
            Dim need As Integer = If(i < neededHeights.Count, neededHeights(i), 0)
            delta(i) = If(need > bounds(i).Height, need - bounds(i).Height, 0)
        Next
        Dim order As List(Of Integer) = Enumerable.Range(0, count).OrderBy(Function(i) bounds(i).Top).ToList()
        For Each j As Integer In order
            Dim best As Integer = 0
            For Each i As Integer In order
                If i = j OrElse bounds(i).Top >= bounds(j).Top Then Continue For
                If bounds(i).Bottom <= bounds(j).Top Then best = Math.Max(best, shift(i) + delta(i))
            Next
            shift(j) = best
        Next
        Dim oldBottom As Integer = Integer.MinValue
        Dim newBottom As Integer = Integer.MinValue
        For i As Integer = 0 To count - 1
            Dim r As Rectangle = bounds(i)
            result(i) = New Rectangle(r.Left, r.Top + shift(i), r.Width, r.Height + delta(i))
            oldBottom = Math.Max(oldBottom, r.Bottom)
            newBottom = Math.Max(newBottom, result(i).Bottom)
        Next
        clientGrowth = If(count = 0, 0, Math.Max(0, newBottom - oldBottom))
        Return result
    End Function
End Class
