#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Windows.Forms

' <summary>
' The pictures of the two welcome windows (the viewer's wizard and the Share Manager's server welcome): a round badge
' with the step's glyph, the step dots, and a row of key caps and glyphs. Linked into both programs like the other
' Icons files. Every picture is a vendored catalog glyph drawn by <see cref="GlyphRenderer"/> in one colour taken from
' the live <see cref="ThemeService"/> palette at paint time - nothing here keeps a colour, so a theme change needs only
' a repaint, and nothing is a Unicode or font stand-in (ICON-RENDER rule 1). The art carries no words: key names are
' Latin on every keyboard, so these rows need no entry in the string tables and read the same in all 13 languages.
' </summary>

''' <summary>A filled circle in the accent colour with one catalog glyph in the accent ink.</summary>
Friend NotInheritable Class StepBadge
    Inherits Control

    Private ReadOnly openAsset As Func(Of String, Stream)
    Private glyph As String = ""

    ''' <param name="logicalSize">The diameter in 96-dpi pixels; the form's auto-scale brings it to the device.</param>
    Friend Sub New(openAsset As Func(Of String, Stream), logicalSize As Integer)
        Me.openAsset = openAsset
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.ResizeRedraw Or ControlStyles.SupportsTransparentBackColor, True)
        SetStyle(ControlStyles.Selectable, False)
        BackColor = Color.Transparent
        TabStop = False
        Size = New Size(logicalSize, logicalSize)
        Margin = New Padding(0)
    End Sub

    ''' <summary>The vocabulary id of the glyph (an empty id draws the plain disc).</summary>
    <Browsable(False), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Friend Property GlyphId As String
        Get
            Return glyph
        End Get
        Set(value As String)
            value = If(value, "")
            If String.Equals(value, glyph, StringComparison.Ordinal) Then Return
            glyph = value
            Invalidate()
        End Set
    End Property

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim palette As ThemePalette = ThemeService.Palette
        Dim side As Integer = Math.Min(ClientSize.Width, ClientSize.Height)
        If side <= 2 Then Return
        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.PixelOffsetMode = PixelOffsetMode.HighQuality
        Using brush As New SolidBrush(palette.Accent)
            g.FillEllipse(brush, 0, 0, side - 1, side - 1)
        End Using
        If glyph.Length = 0 Then Return
        Dim inner As Integer = CInt(Math.Round(side * 0.56))
        Dim offset As Integer = (side - inner) \ 2
        Try
            Using picture As Bitmap = GlyphRenderer.Render(glyph, inner, palette.AccentInk, openAsset)
                g.DrawImage(picture, offset, offset, inner, inner)
            End Using
        Catch
            ' A glyph that cannot be read leaves the disc: the step still works, and the wizard never fails on art.
        End Try
    End Sub

    Protected Overrides Sub OnParentChanged(e As EventArgs)
        MyBase.OnParentChanged(e)
        Invalidate()
    End Sub
End Class

''' <summary>The step dots: finished and current steps in the accent colour, the current one a longer pill.</summary>
Friend NotInheritable Class StepDots
    Inherits Control

    ' Design units (a 10 px tall strip): a dot is 8 wide, the current one 24, the gap 6.
    Private Const DesignHeight As Integer = 10

    Private total As Integer
    Private current As Integer

    Friend Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.ResizeRedraw Or ControlStyles.SupportsTransparentBackColor, True)
        BackColor = Color.Transparent
        TabStop = False
        Margin = New Padding(0, 0, 0, 8)
    End Sub

    ''' <summary>How many steps the wizard has; also sets the strip's design size.</summary>
    <Browsable(False), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Friend Property Count As Integer
        Get
            Return total
        End Get
        Set(value As Integer)
            total = Math.Max(0, value)
            Size = New Size(If(total = 0, 0, (total - 1) * (8 + 6) + 24), DesignHeight)
            Invalidate()
        End Set
    End Property

    ''' <summary>The zero-based current step.</summary>
    <Browsable(False), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Friend Property Index As Integer
        Get
            Return current
        End Get
        Set(value As Integer)
            If value = current Then Return
            current = value
            Invalidate()
        End Set
    End Property

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        If total = 0 OrElse ClientSize.Height <= 0 Then Return
        Dim palette As ThemePalette = ThemeService.Palette
        ' DPI rounding can grow the height more than the width; fit both axes so no dot is clipped.
        Dim designWidth As Integer = (total - 1) * (8 + 6) + 24
        Dim unit As Single = Math.Min(ClientSize.Height / CSng(DesignHeight), ClientSize.Width / CSng(designWidth))
        Dim dot As Single = 8.0F * unit
        Dim pill As Single = 24.0F * unit
        Dim gap As Single = 6.0F * unit
        Dim rtl As Boolean = (RightToLeft = RightToLeft.Yes)
        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias

        Dim x As Single = If(rtl, ClientSize.Width, 0.0F)
        Dim top As Single = (ClientSize.Height - dot) / 2.0F
        For i As Integer = 0 To total - 1
            Dim width As Single = If(i = current, pill, dot)
            If rtl Then x -= width
            Dim ink As Color = If(i <= current, palette.Accent, palette.Border)
            Using brush As New SolidBrush(ink)
                FillPill(g, brush, x, top, width, dot)
            End Using
            If rtl Then x -= gap Else x += width + gap
        Next
    End Sub

    Private Shared Sub FillPill(g As Graphics, brush As Brush, x As Single, y As Single, width As Single, height As Single)
        If width <= height + 0.5F Then
            g.FillEllipse(brush, x, y, height, height)
            Return
        End If
        Using path As New GraphicsPath()
            path.AddArc(x, y, height, height, 90, 180)
            path.AddArc(x + width - height, y, height, height, 270, 180)
            path.CloseFigure()
            g.FillPath(brush, path)
        End Using
    End Sub
End Class

''' <summary>
''' One line of small pictures: key caps ("Shift", "0-9"), catalog glyphs ("@content.folder") and arrows ("->") or
''' plus signs ("+"). Written in the order they are read; under right-to-left they run from the right and the arrow
''' turns round. It is decoration for a sentence that already says the same in words.
''' </summary>
Friend NotInheritable Class StepChips
    Inherits Control

    Private ReadOnly openAsset As Func(Of String, Stream)
    Private ReadOnly tokens As New List(Of String)()

    Friend Sub New(openAsset As Func(Of String, Stream), ParamArray items As String())
        Me.openAsset = openAsset
        tokens.AddRange(items)
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.ResizeRedraw Or ControlStyles.SupportsTransparentBackColor, True)
        SetStyle(ControlStyles.Selectable, False)
        BackColor = Color.Transparent
        TabStop = False
        AutoSize = True
        Margin = New Padding(0, 10, 0, 0)
    End Sub

    Private Function DpiScale() As Single
        Return DeviceDpi / 96.0F
    End Function

    Private Function ChipHeight() As Integer
        Return CInt(Math.Ceiling(Font.Height + 10 * DpiScale()))
    End Function

    Private Function GlyphSize() As Integer
        Return CInt(Math.Round(ChipHeight() * 0.8))
    End Function

    Private Function Gap() As Integer
        Return CInt(Math.Round(8 * DpiScale()))
    End Function

    Private Function TokenWidth(token As String) As Integer
        If token.StartsWith("@", StringComparison.Ordinal) Then Return GlyphSize()
        If token = "->" Then Return CInt(Math.Round(20 * DpiScale()))
        If token = "+" Then Return TextRenderer.MeasureText("+", Font, Size.Empty, TextFormatFlags.NoPadding).Width
        Return TextRenderer.MeasureText(token, Font, Size.Empty, TextFormatFlags.NoPadding).Width + CInt(Math.Round(20 * DpiScale()))
    End Function

    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        Dim width As Integer = 0
        For i As Integer = 0 To tokens.Count - 1
            If i > 0 Then width += Gap()
            width += TokenWidth(tokens(i))
        Next
        Return New Size(width + 2, ChipHeight() + 2)
    End Function

    Protected Overrides Sub OnDpiChangedAfterParent(e As EventArgs)
        MyBase.OnDpiChangedAfterParent(e)
        PerformLayout()
        If Parent IsNot Nothing Then Parent.PerformLayout()
        Invalidate()
    End Sub

    Protected Overrides Sub OnFontChanged(e As EventArgs)
        MyBase.OnFontChanged(e)
        If Parent IsNot Nothing Then Parent.PerformLayout()
        Invalidate()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim palette As ThemePalette = ThemeService.Palette
        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.PixelOffsetMode = PixelOffsetMode.HighQuality
        Dim rtl As Boolean = (RightToLeft = RightToLeft.Yes)
        Dim height As Integer = ChipHeight()
        Dim top As Integer = (ClientSize.Height - height) \ 2
        Dim x As Integer = If(rtl, ClientSize.Width - 1, 1)

        For i As Integer = 0 To tokens.Count - 1
            Dim token As String = tokens(i)
            Dim width As Integer = TokenWidth(token)
            If rtl Then x -= width
            Dim box As New Rectangle(x, top, width, height)
            If token.StartsWith("@", StringComparison.Ordinal) Then
                PaintGlyph(g, token.Substring(1), box, palette.Accent)
            ElseIf token = "->" Then
                PaintArrow(g, box, palette.TextMuted, rtl)
            ElseIf token = "+" Then
                TextRenderer.DrawText(g, "+", Font, box, palette.TextMuted,
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
            Else
                PaintKey(g, token, box, palette)
            End If
            If rtl Then x -= Gap() Else x += width + Gap()
        Next
    End Sub

    Private Sub PaintKey(g As Graphics, text As String, box As Rectangle, palette As ThemePalette)
        Dim radius As Integer = CInt(Math.Round(5 * DpiScale()))
        box.Width -= 1
        box.Height -= 1
        Using path As GraphicsPath = RoundedRectangle(box, radius)
            Using fill As New SolidBrush(palette.SurfaceRaised)
                g.FillPath(fill, path)
            End Using
            Using pen As New Pen(palette.Border, Math.Max(1.0F, DpiScale()))
                g.DrawPath(pen, path)
            End Using
            ' The thick bottom edge that makes a flat rounded box read as a key.
            Using edge As New Pen(palette.Border, Math.Max(2.0F, 2 * DpiScale()))
                g.DrawLine(edge, box.Left + radius, box.Bottom, box.Right - radius, box.Bottom)
            End Using
        End Using
        TextRenderer.DrawText(g, text, Font, box, palette.TextPrimary,
                              TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
    End Sub

    Private Sub PaintGlyph(g As Graphics, glyphId As String, box As Rectangle, ink As Color)
        Dim size As Integer = Math.Min(box.Width, box.Height)
        Try
            Using picture As Bitmap = GlyphRenderer.Render(glyphId, size, ink, openAsset)
                g.DrawImage(picture, box.Left, box.Top + (box.Height - size) \ 2, size, size)
            End Using
        Catch
            ' Missing art is skipped, never raised: this row only echoes the sentence above it.
        End Try
    End Sub

    ''' <summary>A short arrow toward the result; it points the way the line is read, so it turns round in right-to-left.</summary>
    Private Shared Sub PaintArrow(g As Graphics, box As Rectangle, ink As Color, rtl As Boolean)
        Dim midY As Single = box.Top + box.Height / 2.0F
        Dim span As Single = box.Width * 0.7F
        Dim startX As Single = box.Left + (box.Width - span) / 2.0F
        Dim endX As Single = startX + span
        If rtl Then
            Dim swap As Single = startX
            startX = endX
            endX = swap
        End If
        Dim head As Single = Math.Max(3.0F, box.Height * 0.16F)
        Dim direction As Single = If(rtl, -1.0F, 1.0F)
        Using pen As New Pen(ink, Math.Max(1.5F, box.Height / 14.0F))
            pen.StartCap = LineCap.Round
            pen.EndCap = LineCap.Round
            pen.LineJoin = LineJoin.Round
            g.DrawLine(pen, startX, midY, endX, midY)
            g.DrawLine(pen, endX - direction * head, midY - head, endX, midY)
            g.DrawLine(pen, endX - direction * head, midY + head, endX, midY)
        End Using
    End Sub

    Private Shared Function RoundedRectangle(box As Rectangle, radius As Integer) As GraphicsPath
        Dim path As New GraphicsPath()
        Dim d As Integer = Math.Max(2, radius * 2)
        path.AddArc(box.Left, box.Top, d, d, 180, 90)
        path.AddArc(box.Right - d, box.Top, d, d, 270, 90)
        path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90)
        path.AddArc(box.Left, box.Bottom - d, d, d, 90, 90)
        path.CloseFigure()
        Return path
    End Function
End Class
#End If
