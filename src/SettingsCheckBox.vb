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
''' always has room; the hit target stays the caption row as well (120 §5.4).
''' </summary>
Friend NotInheritable Class SettingsCheckBox

    Private Const BoxSideLogical As Integer = 20
    Private Const ControlSideLogical As Integer = 28
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
        Dim side As Integer = Math.Max(1, CInt(Math.Round(ControlSideLogical * state.Scale)))
        check.MinimumSize = Size.Empty
        check.Size = New Size(side, side)
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
        Dim boxSide As Integer = Math.Max(4, CInt(Math.Round(BoxSideLogical * state.Scale)))
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
        Return Color.FromArgb(CInt(ink.R + (surface.R - ink.R) * a),
                              CInt(ink.G + (surface.G - ink.G) * a),
                              CInt(ink.B + (surface.B - ink.B) * a))
    End Function
End Class
#End If
