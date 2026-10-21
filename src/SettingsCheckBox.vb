#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Runtime.CompilerServices
Imports System.Windows.Forms

''' <summary>
''' The owner-drawn boolean of the settings window's rows (SPECIFICATION 127 §3.3).
''' The row's editor stays the legacy <see cref="CheckBox"/> instance - its Checked/
''' CheckedChanged handlers, its accessibility primitive and the whole-row caption click
''' belong to it - but every pixel of it is painted here: a 20 logical px rounded box
''' whose checked state differs by fill AND glyph, unambiguous in both themes, with the
''' palette-decided colours only (the source-scan gate fails on literals in this file).
'''
''' WinForms buttons paint themselves, so there is no style hack: the Paint handler runs
''' after the base rendering and covers the whole client with the row surface before
''' drawing the box. The control carries the box plus a margin so the 2 px focus ring
''' always has room.
'''
''' The row anatomy of BOOLEAN-OPTION 2.0 lives here too, so the window and the tests share
''' one implementation: the 32 logical px hit target around the 20 px box (UI-SIZING
''' section 2 rule 4), the 8 px box-to-text gap, the RTL mirror of the whole leading part
''' (<see cref="PlaceLeading"/>), the wrapped-text row height (<see cref="ArrangeRow"/>) and the
''' activation rule of section 3 rule 1 - the caption toggles, the explanation never does
''' (<see cref="BindCaption"/>).
''' </summary>
Friend NotInheritable Class SettingsCheckBox

    ' Logical px, composed with the window's Sp factor (Px). BOOLEAN-OPTION 2.0 section 2 rule 4.
    ' The box, the hit target and the caption gap are the ordinary pointer row of the shared token table
    ' (UiSizing, UI-SIZING 1.1): a Const cannot call the resolver, so each takes the table's own constant, and
    ' a test pins the pairs. The touch and compact rows are for the later slices that wire a profile into the rows.
    ''' <summary>The visible square: at least 20 for a pointer.</summary>
    Friend Const BoxSideLogical As Integer = UiSizing.PointerCheckBox
    ''' <summary>The hit target: 32 by 32 for a pointer (UI-SIZING section 2 rule 4). The box is
    ''' centred in it, so the target reaches 6 px past the box on every side.</summary>
    Friend Const ControlSideLogical As Integer = UiSizing.PointerTarget
    ''' <summary>Visible box to the text cell.</summary>
    Friend Const CaptionGapLogical As Integer = UiSizing.GlyphGap
    ''' <summary>The box's distance from the row's reading-start edge (unchanged from the 28 px control
    ''' era: 14 + 4).</summary>
    Friend Const BoxEdgeInsetLogical As Integer = 18
    ''' <summary>The text cell's distance from the row's reading-end edge.</summary>
    Friend Const TextEndInsetLogical As Integer = 14
    Private Const CornerRadiusLogical As Single = 4.0F
    Private Const BorderWidthLogical As Single = 1.5F
    Private Const TickWidthLogical As Single = 2.5F
    Private Const FocusRingWidthLogical As Single = 2.0F
    Private Const FocusRingOffsetLogical As Single = 2.0F

    Private NotInheritable Class State
        Public Scale As Single = 1.0F
        Public SurfaceBack As Color
        Public UncheckedBack As Color
        Public BorderInk As Color
        Public HoverInk As Color
        Public CheckedBack As Color
        Public TickInk As Color
    End Class

    Private Shared ReadOnly states As New ConditionalWeakTable(Of CheckBox, State)()

    ''' <summary>True once <see cref="Attach"/> ran for this checkbox; the palette walker
    ''' routes its colours here instead of the native BackColor/ForeColor branch.</summary>
    Friend Shared Function IsAttached(check As CheckBox) As Boolean
        Return check IsNot Nothing AndAlso states.TryGetValue(check, Nothing)
    End Function

    ''' <summary>Turns an existing CheckBox into a drawn box of <paramref name="scale"/>
    ''' (the window's Sp factor). Idempotent; the palette arrives through ApplyPalette.</summary>
    Friend Shared Sub Attach(check As CheckBox, scale As Single)
        Dim state As State = Nothing
        If states.TryGetValue(check, state) Then
            state.Scale = scale
            ResizeControl(check, state)
            check.Invalidate()
            Return
        End If
        state = New State With {.Scale = scale}
        states.Add(check, state)
        ResizeControl(check, state)
        check.Text = String.Empty
        check.AutoSize = False
        check.Padding = Padding.Empty
        check.Margin = Padding.Empty
        check.RightToLeft = RightToLeft.No
        check.TabStop = True
        AddHandler check.Paint, AddressOf PaintState
        AddHandler check.CheckedChanged, Sub() check.Invalidate()
        AddHandler check.EnabledChanged, Sub() check.Invalidate()
        AddHandler check.MouseEnter, Sub() check.Invalidate()
        AddHandler check.MouseLeave, Sub() check.Invalidate()
        AddHandler check.GotFocus, Sub() check.Invalidate()
        AddHandler check.LostFocus, Sub() check.Invalidate()
    End Sub

    Private Shared Sub ResizeControl(check As CheckBox, state As State)
        Dim side As Integer = ControlSide(state.Scale)
        check.MinimumSize = Size.Empty
        check.Size = New Size(side, side)
    End Sub

    ''' <summary>A logical size at the window's scale; the same rounding as the window's Sp.</summary>
    Friend Shared Function Px(logical As Integer, scale As Single) As Integer
        If scale = 1.0F Then Return logical
        Return CInt(Math.Round(logical * scale))
    End Function

    ''' <summary>Side of the square hit target.</summary>
    Friend Shared Function ControlSide(scale As Single) As Integer
        Return Math.Max(1, Px(ControlSideLogical, scale))
    End Function

    ''' <summary>Side of the visible box.</summary>
    Friend Shared Function BoxSide(scale As Single) As Integer
        Return Math.Max(4, Px(BoxSideLogical, scale))
    End Function

    ''' <summary>Where a boolean row puts its checkbox control and its text cell. The mirror under
    ''' RightToLeft is explicit coordinate work (the repo never uses RightToLeftLayout).</summary>
    Friend Structure Leading
        ''' <summary>Left edge of the hit-target control, in the row's client coordinates.</summary>
        Public CheckLeft As Integer
        ''' <summary>Left edge of the text cell (title and explanation share it).</summary>
        Public TextLeft As Integer
        Public TextWidth As Integer
    End Structure

    ''' <summary>BOOLEAN-OPTION 2.0 section 2 rules 1 and 4: the box stands at the reading start (left,
    ''' or right under RightToLeft), the text cell takes the rest of the row after an 8 px gap.</summary>
    ''' <param name="edgeInsetLogical">The box's distance from the reading-start edge. The Settings window keeps the
    ''' default; a dialog row (<see cref="DialogOptionRow"/>) passes its own margin so the box stands where the native
    ''' check box stood.</param>
    Friend Shared Function PlaceLeading(rowWidth As Integer, rightToLeft As Boolean, scale As Single,
                                        Optional edgeInsetLogical As Integer = BoxEdgeInsetLogical) As Leading
        Dim side As Integer = ControlSide(scale)
        Dim box As Integer = BoxSide(scale)
        Dim inset As Integer = (side - box) \ 2
        Dim edge As Integer = Px(edgeInsetLogical, scale)
        Dim gap As Integer = Px(CaptionGapLogical, scale)
        Dim endInset As Integer = Px(TextEndInsetLogical, scale)
        Dim minWidth As Integer = Px(40, scale)
        Dim result As New Leading()
        If rightToLeft Then
            Dim boxLeft As Integer = rowWidth - edge - box
            result.CheckLeft = boxLeft - inset
            result.TextWidth = Math.Max(minWidth, boxLeft - gap - endInset)
            result.TextLeft = boxLeft - gap - result.TextWidth
        Else
            result.CheckLeft = edge - inset
            result.TextLeft = edge + box + gap
            result.TextWidth = Math.Max(minWidth, rowWidth - result.TextLeft - endInset)
        End If
        Return result
    End Function

    ''' <summary>
    ''' Places a boolean row's controls and sets the row's height from its content: the title and the
    ''' explanation are measured WITH word break at the final text width, so a long translation or a
    ''' larger scale makes the row taller and never clips or ellipsizes (section 2 rules 5 and 6). The
    ''' checkbox lines up with the FIRST heading line, not with the middle of a long explanation.
    ''' </summary>
    ''' <param name="heading">The optional heading glyph and `?` help button of the option
    ''' (<see cref="OptionHeading"/>); Nothing finds the one registered for <paramref name="check"/>, and a row that has
    ''' none is laid out exactly as it always was.</param>
    Friend Shared Sub ArrangeRow(host As Control, check As CheckBox, title As Label, description As Label, scale As Single,
                                 Optional heading As OptionHeading = Nothing,
                                 Optional edgeInsetLogical As Integer = BoxEdgeInsetLogical)
        Dim rtl As Boolean = host.RightToLeft = RightToLeft.Yes
        Dim lead As Leading = PlaceLeading(host.ClientSize.Width, rtl, scale, edgeInsetLogical)
        If heading Is Nothing Then heading = OptionHeading.Find(check)

        Dim hasTitle As Boolean = title IsNot Nothing AndAlso Not String.IsNullOrEmpty(title.Text)
        Dim hasHeading As Boolean = heading IsNot Nothing AndAlso hasTitle
        If heading IsNot Nothing AndAlso Not hasTitle Then heading.HideParts()

        ' The caption's box: the whole text cell, or what the glyph and the help target leave of it (section 2 rule 2).
        Dim headingPlan As OptionHeadingPlan = Nothing
        Dim captionLeft As Integer = lead.TextLeft
        Dim captionWidth As Integer = lead.TextWidth
        Dim targetHeight As Integer = check.Height
        If hasHeading Then
            headingPlan = heading.PlanFor(lead, rtl, scale)
            captionLeft = headingPlan.CaptionLeft
            captionWidth = headingPlan.CaptionWidth
            If heading.HasHelp Then targetHeight = Math.Max(targetHeight, heading.HelpSide(scale))
        End If

        Dim titleHeight As Integer = 0
        Dim firstLine As Integer = 0
        If title IsNot Nothing Then title.Visible = hasTitle
        If hasTitle Then
            title.AutoEllipsis = False
            titleHeight = SettingsRowText.WrappedHeight(title, captionWidth, Px(16, scale), Px(2, scale))
            firstLine = Math.Min(titleHeight, Math.Max(Px(16, scale), title.Font.Height + Px(2, scale)))
        End If

        Dim hasDescription As Boolean = description IsNot Nothing AndAlso Not String.IsNullOrEmpty(description.Text)
        Dim descriptionHeight As Integer = 0
        If description IsNot Nothing Then description.Visible = hasDescription
        If hasDescription Then
            description.AutoEllipsis = False
            descriptionHeight = SettingsRowText.WrappedHeight(description, lead.TextWidth, Px(14, scale), Px(2, scale))
        End If

        Dim titleTop As Integer = Px(5, scale)
        If hasTitle Then titleTop = Math.Max(titleTop, Px(3, scale) + (targetHeight - firstLine) \ 2)
        Dim contentBottom As Integer = titleTop
        If hasTitle Then
            title.SetBounds(captionLeft, titleTop, captionWidth, titleHeight)
            contentBottom = title.Bottom
        End If
        ' The help target is centred on the first heading line and may reach below it; the explanation starts under it,
        ' so no hit area lies over the text of its own option.
        Dim headingBottom As Integer = If(hasTitle, title.Bottom, titleTop)
        If hasHeading Then
            heading.Place(headingPlan, titleTop, firstLine, scale)
            If heading.HasHelp Then headingBottom = Math.Max(headingBottom, heading.Help.Bottom)
        End If
        If hasDescription Then
            Dim descriptionTop As Integer = If(hasTitle, Math.Max(title.Bottom + Px(2, scale), If(hasHeading, headingBottom, 0)), titleTop)
            description.SetBounds(lead.TextLeft, descriptionTop, lead.TextWidth, descriptionHeight)
            contentBottom = description.Bottom
        ElseIf hasHeading Then
            contentBottom = Math.Max(contentBottom, headingBottom)
        End If
        check.SetBounds(lead.CheckLeft, If(hasTitle, titleTop + (firstLine - check.Height) \ 2, Px(4, scale)), check.Width, check.Height)
        host.Height = Math.Max(contentBottom + Px(5, scale), check.Bottom + Px(5, scale))
    End Sub

    ''' <summary>BOOLEAN-OPTION 2.0 section 3 rule 1 for a settings row: ONLY the caption (the title label)
    ''' toggles the checkbox, with the hand cursor over it. The explanation is deliberately not bound:
    ''' it gets the ordinary cursor and no handler. A disabled checkbox is not toggled from the caption.</summary>
    Friend Shared Sub BindCaption(caption As Label, check As CheckBox)
        caption.Cursor = Cursors.Hand
        caption.Tag = check
        AddHandler caption.Click, AddressOf ToggleFromCaption
    End Sub

    Private Shared Sub ToggleFromCaption(sender As Object, e As EventArgs)
        Dim label As Label = TryCast(sender, Label)
        Dim check As CheckBox = If(label Is Nothing, Nothing, TryCast(label.Tag, CheckBox))
        If check Is Nothing OrElse Not check.Enabled Then Return
        check.Checked = Not check.Checked
        check.Focus()
    End Sub

    ''' <summary>The palette roles of 127 §3.5, applied on build and on every theme change.</summary>
    Friend Shared Sub ApplyPalette(check As CheckBox, surfaceBack As Color, uncheckedBack As Color,
                                   borderInk As Color, hoverInk As Color, checkedBack As Color, tickInk As Color)
        Dim state As State = Nothing
        If Not states.TryGetValue(check, state) Then Return
        state.SurfaceBack = surfaceBack
        state.UncheckedBack = uncheckedBack
        state.BorderInk = borderInk
        state.HoverInk = hoverInk
        state.CheckedBack = checkedBack
        state.TickInk = tickInk
        check.BackColor = surfaceBack
        check.Invalidate()
    End Sub

    Private Shared Sub PaintState(sender As Object, e As PaintEventArgs)
        Dim check As CheckBox = TryCast(sender, CheckBox)
        Dim state As State = Nothing
        If check Is Nothing OrElse Not states.TryGetValue(check, state) Then Return

        Dim g As Graphics = e.Graphics
        Dim client As Rectangle = check.ClientRectangle
        If client.Width < 4 OrElse client.Height < 4 Then Return

        ' The row surface first: nothing of the native glyph survives underneath.
        Using surface As New SolidBrush(state.SurfaceBack)
            g.FillRectangle(surface, client)
        End Using

        Dim enabled As Boolean = check.Enabled
        Dim hovered As Boolean = enabled AndAlso client.Contains(check.PointToClient(Control.MousePosition))
        Dim dimmed As Single = If(enabled, 0.0F, 0.55F)
        Dim boxSide As Integer = SettingsCheckBox.BoxSide(state.Scale)
        Dim box As New Rectangle(client.X + (client.Width - boxSide) \ 2,
                                 client.Y + (client.Height - boxSide) \ 2, boxSide, boxSide)
        Dim border As Color = BlendSettingsInk(hovered, state.HoverInk, state.BorderInk, state.SurfaceBack, dimmed)
        Dim fill As Color = BlendSettingsInk(False, Color.Empty, If(check.Checked, state.CheckedBack, state.UncheckedBack),
                                  state.SurfaceBack, dimmed)

        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim radius As Single = CornerRadiusLogical * state.Scale
        Using path = RoundedPath(box, radius)
            Using fillBrush As New SolidBrush(fill)
                g.FillPath(fillBrush, path)
            End Using
            Using borderPen As New Pen(border, Math.Max(1.0F, BorderWidthLogical * state.Scale))
                g.DrawPath(borderPen, path)
            End Using
        End Using

        If check.Checked Then
            ' The tick is a drawn path, never the OS visual-style glyph.
            Dim tick As Color = BlendSettingsInk(False, Color.Empty, state.TickInk, state.SurfaceBack, dimmed)
            Dim third As Single = boxSide / 3.0F
            Dim inset As Single = Math.Max(2.0F, boxSide * 0.22F)
            Using tickPen As New Pen(tick, Math.Max(1.5F, TickWidthLogical * state.Scale)) With {
                    .StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
                g.DrawLines(tickPen, New PointF() {
                            New PointF(box.Left + inset, box.Top + third + inset * 0.4F),
                            New PointF(box.Left + boxSide * 0.44F, box.Bottom - inset),
                            New PointF(box.Right - inset * 0.9F, box.Top + inset)})
            End Using
        End If

        ' The focus ring is never removed - accent on both states, offset so the box and
        ' the ring never touch.
        If check.Focused AndAlso enabled Then
            Dim ring As Rectangle = Rectangle.Inflate(box, CInt(FocusRingOffsetLogical * state.Scale),
                                                      CInt(FocusRingOffsetLogical * state.Scale))
            Using ringPen As New Pen(state.CheckedBack, Math.Max(1.0F, FocusRingWidthLogical * state.Scale))
                Using path = RoundedPath(ring, radius + FocusRingOffsetLogical * state.Scale)
                    g.DrawPath(ringPen, path)
                End Using
            End Using
        End If
        g.SmoothingMode = SmoothingMode.None
    End Sub

    Private Shared Function RoundedPath(rect As Rectangle, radius As Single) As GraphicsPath
        Dim path As New GraphicsPath()
        Dim d As Single = Math.Min(radius * 2.0F, Math.Min(rect.Width, rect.Height))
        If d <= 1.0F Then
            path.AddRectangle(rect)
            Return path
        End If
        Dim diameter As Integer = CInt(d)
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90)
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90)
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90)
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    ''' <summary>Reduces an ink toward the row surface when disabled, or swaps to the hover
    ''' ink when asked - the row's muted disabled treatment of 120 §15 UX-504.</summary>
    Private Shared Function BlendSettingsInk(hover As Boolean, hoverInk As Color, normalInk As Color,
                                  surface As Color, amount As Single) As Color
        Dim ink As Color = If(hover AndAlso hoverInk <> Color.Empty, hoverInk, normalInk)
        If amount <= 0.0F Then Return ink
        Dim a As Single = Math.Max(0.0F, Math.Min(1.0F, amount))
        ' Integer arithmetic: Byte minus Byte stays a Byte in VB and throws OverflowException when the surface is darker than
        ' the ink, which painted a disabled box in a light palette (white fill on a grey surface) as an unhandled paint error.
        Return Color.FromArgb(CInt(CInt(ink.R) + (CInt(surface.R) - CInt(ink.R)) * a),
                              CInt(CInt(ink.G) + (CInt(surface.G) - CInt(ink.G)) * a),
                              CInt(CInt(ink.B) + (CInt(surface.B) - CInt(ink.B)) * a))
    End Function
End Class

''' <summary>How tall a wrapped row label has to be so that nothing of its text is cut off. A row never
''' ellipsizes or clips (BOOLEAN-OPTION 2.0 section 2 rule 5): it grows.</summary>
Friend NotInheritable Class SettingsRowText

    ''' <summary>The height of <paramref name="label"/>'s text wrapped at <paramref name="width"/>, plus
    ''' <paramref name="slack"/>, and never below <paramref name="minimum"/>. It is the larger of the GDI measurement the
    ''' window has always used and what the label itself reports: a label drawn with GDI+ text (the default of a
    ''' process that never calls SetCompatibleTextRenderingDefault(False)) is wider and taller than TextRenderer says,
    ''' and the old ellipsis used to hide that.</summary>
    Friend Shared Function WrappedHeight(label As Label, width As Integer, minimum As Integer, slack As Integer) As Integer
        Dim available As Integer = Math.Max(1, width)
        Dim flags As TextFormatFlags = TextFormatFlags.WordBreak
        If label.RightToLeft = RightToLeft.Yes Then flags = flags Or TextFormatFlags.RightToLeft
        Dim measured As Integer = TextRenderer.MeasureText(label.Text, label.Font, New Size(available, Integer.MaxValue), flags).Height
        Dim reported As Integer = label.GetPreferredSize(New Size(available, 0)).Height
        Return Math.Max(minimum, Math.Max(measured, reported) + slack)
    End Function
End Class
#End If
