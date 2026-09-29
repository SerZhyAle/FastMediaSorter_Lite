#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Every ContextMenuStrip, MenuStrip and ToolStrip left at ManagerRenderMode paints through
''' ToolStripManager.Renderer, so ThemeService swaps THIS in for dark mode and puts the original
''' back for light (spec 060 rule 9) - the viewer's media menus, the toolbar overflow, the tray
''' menu and the language picker follow without a line of their own.
''' </summary>
Friend NotInheritable Class ThemedMenuRenderer
    Inherits ToolStripProfessionalRenderer

    Private ReadOnly _palette As ThemePalette

    Public Sub New(palette As ThemePalette)
        MyBase.New(New ThemedMenuColorTable(palette))
        _palette = palette
        Me.RoundedEdges = False
    End Sub

    ''' <summary>An item whose colour the code chose keeps it; one left at a system colour takes
    ''' the palette's ink, and a disabled one the disabled ink.</summary>
    Protected Overrides Sub OnRenderItemText(e As ToolStripItemTextRenderEventArgs)
        If e.Item IsNot Nothing AndAlso (e.Item.ForeColor.IsSystemColor OrElse e.Item.ForeColor.IsEmpty) Then
            e.TextColor = If(e.Item.Enabled, _palette.TextPrimary, _palette.TextDisabled)
        End If
        MyBase.OnRenderItemText(e)
    End Sub

    Protected Overrides Sub OnRenderArrow(e As ToolStripArrowRenderEventArgs)
        e.ArrowColor = If(e.Item Is Nothing OrElse e.Item.Enabled, _palette.TextPrimary, _palette.TextDisabled)
        MyBase.OnRenderArrow(e)
    End Sub

    ''' <summary>The base draws the check mark from a light-theme bitmap; on a dark item it
    ''' disappears, so the mark is drawn in the text colour instead.</summary>
    Protected Overrides Sub OnRenderItemCheck(e As ToolStripItemImageRenderEventArgs)
        Dim r As Rectangle = e.ImageRectangle
        If r.Width <= 0 OrElse r.Height <= 0 OrElse (e.Item IsNot Nothing AndAlso e.Item.Image IsNot Nothing) Then
            MyBase.OnRenderItemCheck(e)
            Return
        End If
        Using back As New SolidBrush(_palette.SurfaceSelected)
            e.Graphics.FillRectangle(back, Rectangle.Inflate(r, 1, 1))
        End Using
        Dim old As Drawing2D.SmoothingMode = e.Graphics.SmoothingMode
        e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
        Using pen As New Pen(_palette.TextPrimary, Math.Max(1.6F, r.Width / 9.0F))
            e.Graphics.DrawLines(pen, New PointF() {
                New PointF(r.Left + r.Width * 0.2F, r.Top + r.Height * 0.52F),
                New PointF(r.Left + r.Width * 0.42F, r.Top + r.Height * 0.74F),
                New PointF(r.Left + r.Width * 0.8F, r.Top + r.Height * 0.28F)})
        End Using
        e.Graphics.SmoothingMode = old
    End Sub

    Private NotInheritable Class ThemedMenuColorTable
        Inherits ProfessionalColorTable

        Private ReadOnly _p As ThemePalette

        Public Sub New(palette As ThemePalette)
            _p = palette
            Me.UseSystemColors = False
        End Sub

        Public Overrides ReadOnly Property ToolStripDropDownBackground As Color
            Get
                Return _p.SurfaceRaised
            End Get
        End Property
        Public Overrides ReadOnly Property ImageMarginGradientBegin As Color
            Get
                Return _p.SurfaceRaised
            End Get
        End Property
        Public Overrides ReadOnly Property ImageMarginGradientMiddle As Color
            Get
                Return _p.SurfaceRaised
            End Get
        End Property
        Public Overrides ReadOnly Property ImageMarginGradientEnd As Color
            Get
                Return _p.SurfaceRaised
            End Get
        End Property
        Public Overrides ReadOnly Property MenuBorder As Color
            Get
                Return _p.Border
            End Get
        End Property
        Public Overrides ReadOnly Property MenuItemBorder As Color
            Get
                Return _p.ControlHover
            End Get
        End Property
        Public Overrides ReadOnly Property MenuItemSelected As Color
            Get
                Return _p.ControlHover
            End Get
        End Property
        Public Overrides ReadOnly Property MenuItemSelectedGradientBegin As Color
            Get
                Return _p.ControlHover
            End Get
        End Property
        Public Overrides ReadOnly Property MenuItemSelectedGradientEnd As Color
            Get
                Return _p.ControlHover
            End Get
        End Property
        Public Overrides ReadOnly Property MenuItemPressedGradientBegin As Color
            Get
                Return _p.ControlPressed
            End Get
        End Property
        Public Overrides ReadOnly Property MenuItemPressedGradientMiddle As Color
            Get
                Return _p.ControlPressed
            End Get
        End Property
        Public Overrides ReadOnly Property MenuItemPressedGradientEnd As Color
            Get
                Return _p.ControlPressed
            End Get
        End Property
        Public Overrides ReadOnly Property MenuStripGradientBegin As Color
            Get
                Return _p.SurfaceWindow
            End Get
        End Property
        Public Overrides ReadOnly Property MenuStripGradientEnd As Color
            Get
                Return _p.SurfaceWindow
            End Get
        End Property
        Public Overrides ReadOnly Property SeparatorDark As Color
            Get
                Return _p.Border
            End Get
        End Property
        Public Overrides ReadOnly Property SeparatorLight As Color
            Get
                Return _p.Border
            End Get
        End Property
        Public Overrides ReadOnly Property ToolStripBorder As Color
            Get
                Return _p.Border
            End Get
        End Property
        Public Overrides ReadOnly Property ToolStripGradientBegin As Color
            Get
                Return _p.SurfaceWindow
            End Get
        End Property
        Public Overrides ReadOnly Property ToolStripGradientMiddle As Color
            Get
                Return _p.SurfaceWindow
            End Get
        End Property
        Public Overrides ReadOnly Property ToolStripGradientEnd As Color
            Get
                Return _p.SurfaceWindow
            End Get
        End Property
        Public Overrides ReadOnly Property ToolStripContentPanelGradientBegin As Color
            Get
                Return _p.SurfaceWindow
            End Get
        End Property
        Public Overrides ReadOnly Property ToolStripContentPanelGradientEnd As Color
            Get
                Return _p.SurfaceWindow
            End Get
        End Property
        Public Overrides ReadOnly Property ToolStripPanelGradientBegin As Color
            Get
                Return _p.SurfaceWindow
            End Get
        End Property
        Public Overrides ReadOnly Property ToolStripPanelGradientEnd As Color
            Get
                Return _p.SurfaceWindow
            End Get
        End Property
        Public Overrides ReadOnly Property StatusStripGradientBegin As Color
            Get
                Return _p.SurfaceWindow
            End Get
        End Property
        Public Overrides ReadOnly Property StatusStripGradientEnd As Color
            Get
                Return _p.SurfaceWindow
            End Get
        End Property
        Public Overrides ReadOnly Property ButtonSelectedHighlight As Color
            Get
                Return _p.ControlHover
            End Get
        End Property
        Public Overrides ReadOnly Property ButtonSelectedHighlightBorder As Color
            Get
                Return _p.Border
            End Get
        End Property
        Public Overrides ReadOnly Property ButtonSelectedBorder As Color
            Get
                Return _p.Border
            End Get
        End Property
        Public Overrides ReadOnly Property ButtonSelectedGradientBegin As Color
            Get
                Return _p.ControlHover
            End Get
        End Property
        Public Overrides ReadOnly Property ButtonSelectedGradientMiddle As Color
            Get
                Return _p.ControlHover
            End Get
        End Property
        Public Overrides ReadOnly Property ButtonSelectedGradientEnd As Color
            Get
                Return _p.ControlHover
            End Get
        End Property
        Public Overrides ReadOnly Property ButtonPressedGradientBegin As Color
            Get
                Return _p.ControlPressed
            End Get
        End Property
        Public Overrides ReadOnly Property ButtonPressedGradientMiddle As Color
            Get
                Return _p.ControlPressed
            End Get
        End Property
        Public Overrides ReadOnly Property ButtonPressedGradientEnd As Color
            Get
                Return _p.ControlPressed
            End Get
        End Property
        Public Overrides ReadOnly Property ButtonCheckedGradientBegin As Color
            Get
                Return _p.SurfaceSelected
            End Get
        End Property
        Public Overrides ReadOnly Property ButtonCheckedGradientMiddle As Color
            Get
                Return _p.SurfaceSelected
            End Get
        End Property
        Public Overrides ReadOnly Property ButtonCheckedGradientEnd As Color
            Get
                Return _p.SurfaceSelected
            End Get
        End Property
        Public Overrides ReadOnly Property CheckBackground As Color
            Get
                Return _p.SurfaceSelected
            End Get
        End Property
        Public Overrides ReadOnly Property CheckSelectedBackground As Color
            Get
                Return _p.SurfaceSelected
            End Get
        End Property
        Public Overrides ReadOnly Property CheckPressedBackground As Color
            Get
                Return _p.SurfaceSelected
            End Get
        End Property
        Public Overrides ReadOnly Property GripDark As Color
            Get
                Return _p.Border
            End Get
        End Property
        Public Overrides ReadOnly Property GripLight As Color
            Get
                Return _p.SurfaceWindow
            End Get
        End Property
        Public Overrides ReadOnly Property OverflowButtonGradientBegin As Color
            Get
                Return _p.SurfaceWindow
            End Get
        End Property
        Public Overrides ReadOnly Property OverflowButtonGradientMiddle As Color
            Get
                Return _p.SurfaceWindow
            End Get
        End Property
        Public Overrides ReadOnly Property OverflowButtonGradientEnd As Color
            Get
                Return _p.SurfaceWindow
            End Get
        End Property
    End Class

End Class
#End If
