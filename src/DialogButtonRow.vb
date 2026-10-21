#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Text
Imports System.Windows.Forms

' The ONE button row of the viewer's secondary windows (ticket 243, slice T-8a; BUTTONS 2.0 sections 3 and 4).
'
' WHY: every hand-built dialog row so far chose its own fixed widths (110 / 120 / 190, 92 x 28, 105), its own
' heights (26 / 28 / 30) and its own order (the acting answer first), and none of them wrapped. This file holds the
' decisions once, pure and tested, and one small control that applies them:
'   * widths follow the content (caption + glyph + 12 px on each side) and are never stretched or made equal;
'   * a whole button moves to the next line before a caption wraps; a button too wide even alone wraps its caption
'     and grows taller;
'   * every line has ONE height - its tallest member, never below the 32 px target;
'   * the answers read Cancel / Apply / final affirmative from the reading start, so a right-to-left window shows
'     the same order mirrored, by the control's own RightToLeft and nothing else (never RightToLeftLayout);
'   * a destructive acting answer takes the affirmative place and is never the default.
'
' Linked into the test project: the model names no window; the control names Button and ButtonGlyphs only.

''' <summary>What a button in a <see cref="DialogButtonRow"/> means. The enum order IS the reading order of the
''' groups (<see cref="DialogButtonRowModel.ReadingOrder"/>): commands (tools beside the answers), the dismissing
''' answer, Apply-like answers, the final affirmative one, the destructive acting one.</summary>
Friend Enum DialogButtonRole
    ''' <summary>A command of the window that is not an answer ("Copy", "Reset all"): first, in the order added,
    ''' set apart from the answers by the group gap. Never the default.</summary>
    Command = 0
    ''' <summary>Cancel, Close without applying: the first answer; Escape's target.</summary>
    Dismissing = 1
    ''' <summary>Apply and the other intermediate answers.</summary>
    Neutral = 2
    ''' <summary>OK, Save, Open: the final affirmative action, last.</summary>
    Affirmative = 3
    ''' <summary>The acting button of a confirmation of an irreversible act: takes the affirmative place, shown with
    ''' the danger face by <see cref="ButtonGlyphRole.Irreversible"/>, and is NEVER the default.</summary>
    Destructive = 4
End Enum

''' <summary>One button as the pure model sees it, in device pixels.</summary>
Friend NotInheritable Class DialogButtonRowItem

    ''' <param name="contentWidth">The caption and the glyph before it, without padding.</param>
    ''' <param name="hasCaption">False for a glyph-only button (the only kind that has a width floor).</param>
    ''' <param name="contentHeight">The height one line of the caption needs, vertical padding included.</param>
    ''' <param name="startsGroup">True for the first button of a semantic group after the first one: the gap before
    ''' it is the group gap, and the group moves to the next line as a whole when it fits there.</param>
    Friend Sub New(contentWidth As Integer, hasCaption As Boolean, contentHeight As Integer, Optional startsGroup As Boolean = False)
        Me.ContentWidth = Math.Max(0, contentWidth)
        Me.HasCaption = hasCaption
        Me.ContentHeight = Math.Max(0, contentHeight)
        Me.StartsGroup = startsGroup
    End Sub

    Friend ReadOnly Property ContentWidth As Integer
    Friend ReadOnly Property HasCaption As Boolean
    Friend ReadOnly Property ContentHeight As Integer
    Friend ReadOnly Property StartsGroup As Boolean
End Class

''' <summary>The result of <see cref="DialogButtonRowModel.Layout"/>: which buttons share a line, how wide and how
''' tall each is, and what the whole row needs. Indexes are those of the items handed in (reading order).</summary>
Friend NotInheritable Class DialogButtonRowLayout

    Friend Sub New(lines As IReadOnlyList(Of IReadOnlyList(Of Integer)), widths As IReadOnlyList(Of Integer),
                   heights As IReadOnlyList(Of Integer), gapBefore As IReadOnlyList(Of Integer),
                   lineWidths As IReadOnlyList(Of Integer), lineHeights As IReadOnlyList(Of Integer),
                   totalHeight As Integer)
        Me.Lines = lines
        Me.Widths = widths
        Me.Heights = heights
        Me.GapBefore = gapBefore
        Me.LineWidths = lineWidths
        Me.LineHeights = lineHeights
        Me.TotalHeight = totalHeight
    End Sub

    ''' <summary>The button indexes of each line, in reading order.</summary>
    Friend ReadOnly Property Lines As IReadOnlyList(Of IReadOnlyList(Of Integer))
    ''' <summary>The final width of each button.</summary>
    Friend ReadOnly Property Widths As IReadOnlyList(Of Integer)
    ''' <summary>The final height of each button: the height of its line.</summary>
    Friend ReadOnly Property Heights As IReadOnlyList(Of Integer)
    ''' <summary>The gap before each button (0 for the first of a line).</summary>
    Friend ReadOnly Property GapBefore As IReadOnlyList(Of Integer)
    Friend ReadOnly Property LineWidths As IReadOnlyList(Of Integer)
    Friend ReadOnly Property LineHeights As IReadOnlyList(Of Integer)
    ''' <summary>The height of all lines with the gaps between them.</summary>
    Friend ReadOnly Property TotalHeight As Integer

    Friend ReadOnly Property MaxLineWidth As Integer
        Get
            Dim widest As Integer = 0
            For Each w As Integer In LineWidths
                If w > widest Then widest = w
            Next
            Return widest
        End Get
    End Property
End Class

''' <summary>The decisions of the dialog button row, with no WinForms in them (BUTTONS 2.0 sections 3 and 4).</summary>
Friend NotInheritable Class DialogButtonRowModel

    Private Sub New()
    End Sub

    ' --- order and defaults ------------------------------------------------------------------

    ''' <summary>The indexes of <paramref name="roles"/> in the order the eye reads them: commands, the dismissing
    ''' answer, Apply-like answers, the affirmative answer, the destructive one. Stable inside a group, so the
    ''' caller's own order survives between two buttons of one role.</summary>
    Friend Shared Function ReadingOrder(roles As IReadOnlyList(Of DialogButtonRole)) As Integer()
        If roles Is Nothing OrElse roles.Count = 0 Then Return New Integer() {}
        Return Enumerable.Range(0, roles.Count).OrderBy(Function(i) CInt(roles(i))).ThenBy(Function(i) i).ToArray()
    End Function

    ''' <summary>The index of the button Enter selects: the first affirmative one, else the first dismissing one,
    ''' else the first Apply-like one; -1 when there is none. A destructive answer and a command are never it.</summary>
    Friend Shared Function SafeDefault(roles As IReadOnlyList(Of DialogButtonRole)) As Integer
        If roles Is Nothing Then Return -1
        For Each wanted As DialogButtonRole In New DialogButtonRole() {DialogButtonRole.Affirmative, DialogButtonRole.Dismissing, DialogButtonRole.Neutral}
            For i As Integer = 0 To roles.Count - 1
                If roles(i) = wanted Then Return i
            Next
        Next
        Return -1
    End Function

    ''' <summary>The index of the button Escape selects: the first dismissing one, or -1.</summary>
    Friend Shared Function CancelIndex(roles As IReadOnlyList(Of DialogButtonRole)) As Integer
        If roles Is Nothing Then Return -1
        For i As Integer = 0 To roles.Count - 1
            If roles(i) = DialogButtonRole.Dismissing Then Return i
        Next
        Return -1
    End Function

    ' --- widths ------------------------------------------------------------------------------

    ''' <summary>The width of one button: its content plus the padding on both sides - never stretched, never made
    ''' equal to another. Only a glyph-only button has a floor, the target (one square hit target).</summary>
    Friend Shared Function ButtonWidth(contentWidth As Integer, hasCaption As Boolean, padding As Integer, target As Integer) As Integer
        Dim padded As Long = CLng(Math.Max(0, contentWidth)) + 2L * Math.Max(0, padding)
        If Not hasCaption Then padded = Math.Max(padded, Math.Max(1, target))
        Return CInt(Math.Min(padded, Integer.MaxValue \ 4))
    End Function

    ''' <summary>The width a row may use, or Integer.MaxValue for "unknown": a width that is not a finite number of at
    ''' least one pixel (zero, negative, NaN - a control not yet laid out) lays the row out as one natural line
    ''' instead of a tall column of buttons.</summary>
    Friend Shared Function LimitOf(available As Double) As Integer
        If Double.IsNaN(available) OrElse available < 1.0R Then Return Integer.MaxValue
        If available >= CDbl(Integer.MaxValue \ 4) Then Return Integer.MaxValue
        Return CInt(Math.Floor(available))
    End Function

    ' --- wrap --------------------------------------------------------------------------------

    ''' <summary>Decides the lines. <paramref name="items"/> are in READING order. A whole button moves to the next
    ''' line before its caption wraps; a group (items from one that <see cref="DialogButtonRowItem.StartsGroup"/> to
    ''' the next) moves together when it fits a line of its own; a button wider than the line even alone takes the
    ''' line's width and its caption wraps (<paramref name="wrappedHeight"/> says how tall it then is). Every line
    ''' has the height of its tallest member, never below <paramref name="target"/>.</summary>
    ''' <param name="available">The row's width in device pixels; see <see cref="LimitOf"/> for the unknown case.</param>
    ''' <param name="wrappedHeight">(button index, width left for the caption) to the height that button needs;
    ''' Nothing keeps the one-line height.</param>
    Friend Shared Function Layout(items As IReadOnlyList(Of DialogButtonRowItem), available As Double,
                                  padding As Integer, gap As Integer, groupGap As Integer, target As Integer,
                                  wrappedHeight As Func(Of Integer, Integer, Integer)) As DialogButtonRowLayout
        Dim n As Integer = If(items Is Nothing, 0, items.Count)
        Dim pad As Integer = Math.Max(0, padding)
        Dim between As Integer = Math.Max(0, gap)
        Dim apart As Integer = Math.Max(0, groupGap)
        Dim floor As Integer = Math.Max(1, target)
        Dim limit As Integer = LimitOf(available)

        Dim natural As Integer() = New Integer(n - 1) {}
        Dim widths As Integer() = New Integer(n - 1) {}
        Dim heights As Integer() = New Integer(n - 1) {}
        Dim gaps As Integer() = New Integer(n - 1) {}
        For i As Integer = 0 To n - 1
            natural(i) = ButtonWidth(items(i).ContentWidth, items(i).HasCaption, pad, floor)
        Next

        Dim lines As New List(Of List(Of Integer))()
        Dim current As New List(Of Integer)()
        Dim lineWidth As Long = 0
        Dim start As Integer = 0
        While start < n
            Dim stop_ As Integer = start + 1
            While stop_ < n AndAlso Not items(stop_).StartsGroup
                stop_ += 1
            End While
            Dim groupWidth As Long = 0
            For k As Integer = start To stop_ - 1
                groupWidth += natural(k) + If(k = start, 0, between)
            Next
            If current.Count > 0 AndAlso lineWidth + apart + groupWidth > limit AndAlso groupWidth <= limit Then
                lines.Add(current)
                current = New List(Of Integer)()
                lineWidth = 0
            End If
            For k As Integer = start To stop_ - 1
                Dim w As Integer = Math.Min(natural(k), limit)
                Dim g As Integer = If(current.Count = 0, 0, If(k = start, apart, between))
                If current.Count > 0 AndAlso lineWidth + g + w > limit Then
                    lines.Add(current)
                    current = New List(Of Integer)()
                    lineWidth = 0
                    g = 0
                End If
                current.Add(k)
                widths(k) = w
                gaps(k) = g
                lineWidth += g + w
            Next
            start = stop_
        End While
        If current.Count > 0 Then lines.Add(current)

        Dim lineWidths As New List(Of Integer)()
        Dim lineHeights As New List(Of Integer)()
        Dim lineIndexes As New List(Of IReadOnlyList(Of Integer))()
        Dim total As Long = 0
        For Each line As List(Of Integer) In lines
            Dim tallest As Integer = floor
            Dim across As Long = 0
            For Each k As Integer In line
                Dim need As Integer = Math.Max(floor, items(k).ContentHeight)
                If widths(k) < natural(k) AndAlso wrappedHeight IsNot Nothing Then
                    need = Math.Max(need, wrappedHeight(k, Math.Max(1, widths(k) - 2 * pad)))
                End If
                If need > tallest Then tallest = need
                across += gaps(k) + widths(k)
            Next
            For Each k As Integer In line
                heights(k) = tallest
            Next
            lineWidths.Add(CInt(Math.Min(across, Integer.MaxValue)))
            lineHeights.Add(tallest)
            lineIndexes.Add(line.ToArray())
            total += tallest
        Next
        If lines.Count > 1 Then total += CLng(lines.Count - 1) * between
        Return New DialogButtonRowLayout(lineIndexes, widths, heights, gaps, lineWidths, lineHeights,
                                         CInt(Math.Min(total, Integer.MaxValue)))
    End Function
End Class

''' <summary>
''' A row of dialog buttons that lays itself out by <see cref="DialogButtonRowModel"/>: content-fit widths, whole-button
''' wrapping, one height per line, the answers in reading order. It is a plain <see cref="Panel"/> that sets the
''' bounds of its buttons itself; the host docks it (<c>Dock = Bottom</c>: the row keeps its own height as lines
''' wrap) or lets it size itself (<c>AutoSize = True</c> in a table cell).
'''
''' How a window adopts it:
''' <code>
''' Dim row As New DialogButtonRow() With {.Dock = DockStyle.Bottom}
''' row.Add(okButton, DialogButtonRole.Affirmative, "action.confirm")
''' row.Add(cancelButton, DialogButtonRole.Dismissing, "action.cancel")
''' Controls.Add(row)
''' row.Bind(Me)            ' AcceptButton / CancelButton from the roles
''' </code>
''' Reading direction is the window's <c>RightToLeft</c> and nothing else: the row mirrors its own positions.
''' </summary>
Friend NotInheritable Class DialogButtonRow
    Inherits Panel

    Private NotInheritable Class Entry
        Friend ReadOnly Button As Button
        Friend ReadOnly Role As DialogButtonRole
        Friend ReadOnly Seq As Integer

        Friend Sub New(button As Button, role As DialogButtonRole, seq As Integer)
            Me.Button = button
            Me.Role = role
            Me.Seq = seq
        End Sub
    End Class

    ''' <summary>What was measured for the current captions, font and DPI: independent of the width, so a resize
    ''' reuses it and only re-runs the (pure, tiny) wrap.</summary>
    Private NotInheritable Class Measured
        Friend Key As String = ""
        Friend Items As DialogButtonRowItem() = New DialogButtonRowItem() {}
        Friend Padding As Integer
        Friend Gap As Integer
        Friend GroupGap As Integer
        Friend Target As Integer
        Friend VerticalPadding As Integer
    End Class

    Private ReadOnly entries As New List(Of Entry)()
    Private orderedCache As List(Of Entry)
    Private measuredCache As Measured
    Private lastLayoutKey As String = ""
    Private applied As Rectangle() = New Rectangle() {}
    Private arranging As Boolean
    Private nextSeq As Integer
    Private measureRuns As Integer
    Private arrangeRuns As Integer

    Friend Sub New()
        MyBase.New()
        TabStop = False
        Margin = Padding.Empty
        AutoSizeMode = AutoSizeMode.GrowAndShrink
        Height = UiSizing.PointerTarget
    End Sub

    ''' <summary>True (the default) puts each line against the reading END of the row - the right edge in a
    ''' left-to-right window, the left in a right-to-left one - like the answers of a Windows dialog; False starts
    ''' each line at the reading start.</summary>
    <System.ComponentModel.Browsable(False), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property AlignToReadingEnd As Boolean = True

    ''' <summary>The children's scaling is the row's own business: a framework scale pass would resize buttons
    ''' this control has just measured.</summary>
    Protected Overrides ReadOnly Property ScaleChildren As Boolean
        Get
            Return False
        End Get
    End Property

    ' --- the public surface ------------------------------------------------------------------

    ''' <summary>How many times the captions were measured: a resize or a repeated layout must not raise it.</summary>
    Friend ReadOnly Property MeasureCount As Integer
        Get
            Return measureRuns
        End Get
    End Property

    ''' <summary>How many times the buttons were placed: a repeat of the same width must not raise it.</summary>
    Friend ReadOnly Property ArrangeCount As Integer
        Get
            Return arrangeRuns
        End Get
    End Property

    ''' <summary>Puts <paramref name="button"/> in the row with its role. The button keeps its name, handlers,
    ''' accessible name and <c>DialogResult</c>; the row takes its size, position and tab index. The glyph goes
    ''' through <see cref="ButtonGlyphs.Apply"/> - the id given, else the role's default - unless the button already
    ''' carries one. Returns the button.</summary>
    Friend Function Add(button As Button, role As DialogButtonRole, Optional glyphId As String = Nothing) As Button
        If button Is Nothing Then Throw New ArgumentNullException(NameOf(button))
        Dim glyphRole As ButtonGlyphRole = GlyphRoleOf(role)
        If Not ButtonGlyphs.CarriesGlyph(button) Then
            Dim id As String = If(String.IsNullOrEmpty(glyphId), ButtonGlyphs.DefaultGlyph(glyphRole), glyphId)
            If Not String.IsNullOrEmpty(id) Then ButtonGlyphs.Apply(button, id, glyphRole)
        End If
        button.AutoSize = False
        button.Dock = DockStyle.None
        button.Anchor = AnchorStyles.Top Or AnchorStyles.Left
        button.Margin = Padding.Empty
        button.MinimumSize = Size.Empty
        AddHandler button.TextChanged, AddressOf OnChildMeasureChanged
        AddHandler button.FontChanged, AddressOf OnChildMeasureChanged
        entries.Add(New Entry(button, role, nextSeq))
        nextSeq += 1
        orderedCache = Nothing
        measuredCache = Nothing
        SuspendLayout()
        Controls.Add(button)
        ResumeLayout(False)
        Remeasure()
        Return button
    End Function

    ''' <summary>The buttons in reading order: the first one is at the reading start.</summary>
    Friend ReadOnly Property ReadingButtons As IReadOnlyList(Of Button)
        Get
            Return Ordered().Select(Function(e) e.Button).ToList()
        End Get
    End Property

    ''' <summary>The button Escape selects (the first dismissing one), or Nothing.</summary>
    Friend ReadOnly Property CancelTarget As Button
        Get
            Dim at As Integer = DialogButtonRowModel.CancelIndex(Roles())
            Return If(at < 0, Nothing, Ordered()(at).Button)
        End Get
    End Property

    ''' <summary>The button Enter selects, by <see cref="DialogButtonRowModel.SafeDefault"/>, or Nothing.</summary>
    Friend ReadOnly Property DefaultTarget As Button
        Get
            Dim at As Integer = DialogButtonRowModel.SafeDefault(Roles())
            Return If(at < 0, Nothing, Ordered()(at).Button)
        End Get
    End Property

    ''' <summary>Binds the host window's <c>CancelButton</c> to the dismissing button and, unless
    ''' <paramref name="bindAccept"/> is False, its <c>AcceptButton</c> to the safe default. Call it after the
    ''' buttons are added.</summary>
    Friend Sub Bind(host As Form, Optional bindAccept As Boolean = True)
        If host Is Nothing Then Return
        Dim cancel As Button = CancelTarget
        If cancel IsNot Nothing Then host.CancelButton = cancel
        If bindAccept Then
            Dim accept As Button = DefaultTarget
            If accept IsNot Nothing Then host.AcceptButton = accept
        End If
    End Sub

    ' --- order -------------------------------------------------------------------------------

    Private Function Ordered() As List(Of Entry)
        If orderedCache Is Nothing Then
            Dim roles As New List(Of DialogButtonRole)()
            For Each e As Entry In entries
                roles.Add(e.Role)
            Next
            orderedCache = DialogButtonRowModel.ReadingOrder(roles).Select(Function(i) entries(i)).ToList()
        End If
        Return orderedCache
    End Function

    Private Function Roles() As List(Of DialogButtonRole)
        Return Ordered().Select(Function(e) e.Role).ToList()
    End Function

    Private Shared Function GlyphRoleOf(role As DialogButtonRole) As ButtonGlyphRole
        Select Case role
            Case DialogButtonRole.Dismissing : Return ButtonGlyphRole.Dismissing
            Case DialogButtonRole.Affirmative : Return ButtonGlyphRole.Affirmative
            Case DialogButtonRole.Destructive : Return ButtonGlyphRole.Irreversible
            Case Else : Return ButtonGlyphRole.Neutral
        End Select
    End Function

    ' --- measuring ---------------------------------------------------------------------------

    Private Sub Remeasure()
        orderedCache = Nothing
        measuredCache = Nothing
        lastLayoutKey = ""
        PerformLayout()
    End Sub

    Private Sub OnChildMeasureChanged(sender As Object, e As EventArgs)
        If Not arranging Then Remeasure()
    End Sub

    Private Function MeasureKey() As String
        Dim sb As New StringBuilder()
        sb.Append(DeviceDpi).Append("|").Append(Font.Name).Append("|").Append(Font.SizeInPoints.ToString(Globalization.CultureInfo.InvariantCulture))
        For Each e As Entry In Ordered()
            sb.Append("|").Append(e.Button.Text).Append("#").Append(ButtonGlyphs.ExtraWidth(e.Button)).Append("#").Append(CInt(e.Role))
        Next
        Return sb.ToString()
    End Function

    Private Function Measure() As Measured
        Dim key As String = MeasureKey()
        If measuredCache IsNot Nothing AndAlso measuredCache.Key = key Then Return measuredCache
        Dim m As New Measured With {.Key = key}
        m.Padding = LogicalToDeviceUnits(UiSizing.PointerPaddingX)
        m.Gap = LogicalToDeviceUnits(UiSizing.ButtonGap)
        m.GroupGap = LogicalToDeviceUnits(UiSizing.GroupGap)
        m.Target = LogicalToDeviceUnits(UiSizing.PointerTarget)
        m.VerticalPadding = LogicalToDeviceUnits(6)
        Dim list As Entry() = Ordered().ToArray()
        Dim items(list.Length - 1) As DialogButtonRowItem
        For i As Integer = 0 To list.Length - 1
            Dim b As Button = list(i).Button
            Dim hasCaption As Boolean = b.Text.Length > 0
            Dim textExtent As Size = If(hasCaption, TextRenderer.MeasureText(b.Text, b.Font), Size.Empty)
            Dim wanted As Integer = If(hasCaption, textExtent.Height, 0) + 2 * m.VerticalPadding
            Dim startsGroup As Boolean = i > 0 AndAlso list(i).Role <> DialogButtonRole.Command AndAlso list(i - 1).Role = DialogButtonRole.Command
            items(i) = New DialogButtonRowItem(textExtent.Width + ButtonGlyphs.ExtraWidth(b), hasCaption, wanted, startsGroup)
        Next
        m.Items = items
        measuredCache = m
        measureRuns += 1
        Return m
    End Function

    Private Function WrappedHeight(index As Integer, innerWidth As Integer) As Integer
        Dim b As Button = Ordered()(index).Button
        Dim extent As Size = TextRenderer.MeasureText(b.Text, b.Font, New Size(Math.Max(1, innerWidth), Integer.MaxValue), TextFormatFlags.WordBreak)
        Return extent.Height + 2 * LogicalToDeviceUnits(6)
    End Function

    Private Function LayoutFor(available As Double, m As Measured) As DialogButtonRowLayout
        Return DialogButtonRowModel.Layout(m.Items, available, m.Padding, m.Gap, m.GroupGap, m.Target, AddressOf WrappedHeight)
    End Function

    ' --- layout ------------------------------------------------------------------------------

    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        If entries.Count = 0 Then Return MyBase.GetPreferredSize(proposedSize)
        Dim usable As Double = If(proposedSize.Width <= 0 OrElse proposedSize.Width >= Integer.MaxValue \ 4, Double.NaN, proposedSize.Width - Padding.Horizontal)
        Dim result As DialogButtonRowLayout = LayoutFor(usable, Measure())
        Return New Size(result.MaxLineWidth + Padding.Horizontal, result.TotalHeight + Padding.Vertical)
    End Function

    Protected Overrides Sub OnLayout(levent As LayoutEventArgs)
        MyBase.OnLayout(levent)
        Arrange()
    End Sub

    Protected Overrides Sub OnDpiChangedAfterParent(e As EventArgs)
        MyBase.OnDpiChangedAfterParent(e)
        Remeasure()
    End Sub

    Protected Overrides Sub OnFontChanged(e As EventArgs)
        MyBase.OnFontChanged(e)
        If entries.Count > 0 Then Remeasure()
    End Sub

    Protected Overrides Sub OnRightToLeftChanged(e As EventArgs)
        MyBase.OnRightToLeftChanged(e)
        lastLayoutKey = ""
        If entries.Count > 0 Then PerformLayout()
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        If entries.Count > 0 Then Remeasure()
    End Sub

    Private Function AppliedStillHolds() As Boolean
        Dim list As List(Of Entry) = Ordered()
        If applied.Length <> list.Count Then Return False
        For i As Integer = 0 To list.Count - 1
            If list(i).Button.Bounds <> applied(i) Then Return False
        Next
        Return True
    End Function

    ''' <summary>Places the buttons. A repeat of the same width, captions, font, DPI and direction costs one string
    ''' comparison and n rectangle comparisons - a drag of the window edge does not measure text again, and the
    ''' pure wrap only runs for a width not seen last.</summary>
    Private Sub Arrange()
        If arranging OrElse entries.Count = 0 Then Return
        Dim width As Integer = ClientSize.Width
        Dim rtl As Boolean = RightToLeft = RightToLeft.Yes
        Dim key As String = MeasureKey() & "@" & width.ToString(Globalization.CultureInfo.InvariantCulture) & "|" & rtl.ToString() & "|" & AlignToReadingEnd.ToString()
        If key = lastLayoutKey AndAlso AppliedStillHolds() Then Return
        arranging = True
        SuspendLayout()
        Try
            Dim known As Boolean = width >= 1
            Dim usable As Double = If(known, CDbl(width), Double.NaN)
            Dim m As Measured = Measure()
            Dim result As DialogButtonRowLayout = LayoutFor(usable, m)
            Dim list As List(Of Entry) = Ordered()
            Dim span As Integer = If(known, width, result.MaxLineWidth)
            Dim rects(list.Count - 1) As Rectangle
            Dim y As Integer = 0
            For li As Integer = 0 To result.Lines.Count - 1
                Dim start As Integer = If(AlignToReadingEnd AndAlso known, Math.Max(0, span - result.LineWidths(li)), 0)
                Dim offset As Integer = 0
                For Each k As Integer In result.Lines(li)
                    offset += result.GapBefore(k)
                    Dim w As Integer = result.Widths(k)
                    Dim x As Integer = If(rtl, span - (start + offset) - w, start + offset)
                    rects(k) = New Rectangle(x, y, w, result.Heights(k))
                    offset += w
                Next
                y += result.LineHeights(li) + If(li < result.Lines.Count - 1, m.Gap, 0)
            Next
            For k As Integer = 0 To list.Count - 1
                list(k).Button.Bounds = rects(k)
                list(k).Button.TabIndex = k
            Next
            applied = rects
            arrangeRuns += 1
            lastLayoutKey = key
            ' A docked or free row owns its height; an auto-sized one is sized by the layout engine from
            ' GetPreferredSize.
            If Not AutoSize Then
                Dim wanted As Integer = result.TotalHeight + Padding.Vertical
                If Height <> wanted Then Height = wanted
            End If
        Finally
            ResumeLayout(False)
            arranging = False
        End Try
    End Sub

End Class
#End If
