#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing

''' <summary>The COLLAPSIBLE-GROUP 1.1 section 3 state marker, shared by every collapsible group header of the product
''' (ticket 243, T-11c): a FILLED TRIANGLE at the reading start - right when the group is collapsed, down when it is
''' open, left when it is collapsed under RTL - painted as a vector in the header's own ink, on glyph tier 16 scaled with
''' the window and 8 logical px from the caption. Not an up/down action chevron and not a baked bitmap, so a theme, DPI
''' or RightToLeft change repaints it with nothing to re-render.
''' The viewer's settings group (<c>SettingsCollapsibleGroup</c>) and the Share Manager's two header kinds
''' (<c>SettingsGroupCard</c>, <c>CollapsibleSection</c>) all take the shape, the size and the vertical placement from
''' here, so the three can never drift apart. Pure geometry plus one paint call; no control state.</summary>
Friend NotInheritable Class GroupMarker

    Private Sub New()
    End Sub

    ''' <summary>Where the state triangle points.</summary>
    Friend Enum Direction
        Right
        Down
        Left
    End Enum

    ''' <summary>Marker glyph tier and the gap to the caption, logical pixels (COLLAPSIBLE-GROUP section 3 rules 2 and 6).
    ''' Both come from the shared token table (<c>UiSizing</c>).</summary>
    Friend Const TierLogical As Integer = UiSizing.MarkerTier
    Friend Const GapLogical As Integer = UiSizing.GlyphGap

    ''' <summary>The direction of the state triangle: down when open; right when collapsed, left when
    ''' collapsed under RTL (it points toward the reading end).</summary>
    Friend Shared Function DirectionFor(expanded As Boolean, rightToLeft As Boolean) As Direction
        If expanded Then Return Direction.Down
        Return If(rightToLeft, Direction.Left, Direction.Right)
    End Function

    ''' <summary>The three vertices of the triangle inside a square <paramref name="cell"/>, drawn on a
    ''' 16-unit grid and rounded to whole pixels at the cell's size: 12 units along the base, 8 deep,
    ''' centred - a tier-16 glyph with a 2 unit margin, readable beside 10.5 pt Semibold.</summary>
    Friend Shared Function Polygon(direction As Direction, cell As Rectangle) As Point()
        Dim s As Integer = cell.Width
        Select Case direction
            Case GroupMarker.Direction.Down
                Return {GridPoint(cell, s, 2, 4), GridPoint(cell, s, 14, 4), GridPoint(cell, s, 8, 12)}
            Case GroupMarker.Direction.Left
                Return {GridPoint(cell, s, 12, 2), GridPoint(cell, s, 12, 14), GridPoint(cell, s, 4, 8)}
            Case Else
                Return {GridPoint(cell, s, 4, 2), GridPoint(cell, s, 4, 14), GridPoint(cell, s, 12, 8)}
        End Select
    End Function

    Private Shared Function GridPoint(cell As Rectangle, size As Integer, gx As Integer, gy As Integer) As Point
        Return New Point(cell.X + CInt(Math.Round(gx * size / 16.0, MidpointRounding.AwayFromZero)),
                         cell.Y + CInt(Math.Round(gy * size / 16.0, MidpointRounding.AwayFromZero)))
    End Function

    ''' <summary>The side of the marker's square cell in device pixels at an interface factor (device DPI / 96): tier 16
    ''' scaled with the window, never smaller than 16.</summary>
    Friend Shared Function CellPx(factor As Single) As Integer
        Return Math.Max(TierLogical, UiSizing.DevicePx(TierLogical, factor))
    End Function

    ''' <summary>The gap between the marker and the caption in device pixels, never smaller than 8.</summary>
    Friend Shared Function GapPx(factor As Single) As Integer
        Return Math.Max(GapLogical, UiSizing.DevicePx(GapLogical, factor))
    End Function

    ''' <summary>The vertical centre of the FIRST caption line when a text block of <paramref name="blockHeight"/>
    ''' (at least one line) is centred in a zone that starts at <paramref name="zoneTop"/> and is
    ''' <paramref name="zoneHeight"/> tall - the way a vertically centred, wrapping caption is laid out. The marker is
    ''' centred on this line, so a caption that wraps to several lines keeps its marker beside the first one.</summary>
    Friend Shared Function FirstLineCentre(zoneTop As Integer, zoneHeight As Integer, blockHeight As Integer,
                                           lineHeight As Integer) As Integer
        Dim block As Integer = Math.Max(lineHeight, blockHeight)
        Dim firstLineTop As Integer = zoneTop + (zoneHeight - block) \ 2
        Return (2 * firstLineTop + lineHeight + 1) \ 2
    End Function

    ''' <summary>Fills the state triangle in <paramref name="ink"/> inside <paramref name="cell"/>.</summary>
    Friend Shared Sub Fill(g As Graphics, ink As Color, direction As Direction, cell As Rectangle)
        Dim polygonPoints As Point() = Polygon(direction, cell)
        Dim saved As Drawing2D.GraphicsState = g.Save()
        Try
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            ' Whole-pixel vertices then fall on pixel edges: the vertical and horizontal sides stay
            ' crisp and the left and right triangles are exact mirror images.
            g.PixelOffsetMode = Drawing2D.PixelOffsetMode.Half
            Using brush As New SolidBrush(ink)
                g.FillPolygon(brush, polygonPoints)
            End Using
        Finally
            g.Restore(saved)
        End Try
    End Sub
End Class
#End If
