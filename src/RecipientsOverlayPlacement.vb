#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing

''' <summary>
''' The pure geometry of the recipients overlay's free position
''' (219_SPECIFICATION_RECIPIENTS_OVERLAY_DRAG_BY_HEADER.md §3, §6, §7): the anchored
''' corner offsets, the logical-px storage conversions of a dropped point, the
''' out-of-window recovery clamp and the hot-zone rule of a moved panel. Points and
''' rectangles only - no window, no form - so the placement math, the DPI scenes and
''' the lost-panel scenes are provable here without a screen.
''' </summary>
Friend NotInheritable Class RecipientsOverlayPlacement

    Private Sub New()
    End Sub

    ''' <summary>The anchored corner's offset from the media area's top-left, in device pixels -
    ''' the caller has already scaled the margin and the chrome-aware top offset. The same
    ''' arithmetic the corner choice always placed the panel by, kept here so the anchored
    ''' state stays one honest function instead of a copy that can drift.</summary>
    Public Shared Function CornerOffset(corner As String, areaSize As Size, panelSize As Size,
                                        margin As Integer, topOffset As Integer) As Point
        Dim x As Integer = margin
        Dim y As Integer = topOffset
        Select Case If(corner, "")
            Case "topRight"
                x = Math.Max(margin, areaSize.Width - panelSize.Width - margin)
            Case "bottomLeft"
                y = Math.Max(margin, areaSize.Height - panelSize.Height - margin)
            Case "bottomRight"
                x = Math.Max(margin, areaSize.Width - panelSize.Width - margin)
                y = Math.Max(margin, areaSize.Height - panelSize.Height - margin)
        End Select
        Return New Point(x, y)
    End Function

    ''' <summary>A dropped position to the stored point: the device-pixel offset from the media
    ''' area's top-left divided by the scale, back to LOGICAL (96-dpi) px - two monitors, two
    ''' scales, one sane answer (219 §3). Zero and negative offsets are positions like any
    ''' other: a drop outside the media area is allowed, and the recovery clamp decides what
    ''' happens at the next placement.</summary>
    Public Shared Function ToLogicalPoint(deviceOffset As Point, factor As Single) As Point
        Return New Point(ToLogical(deviceOffset.X, factor), ToLogical(deviceOffset.Y, factor))
    End Function

    ''' <summary>The stored point back to device pixels, through the same scale seam every metric
    ''' of the overlay goes through - one honest arithmetic, extended to signed offsets: a
    ''' position is not a size, so a negative coordinate (a drop left of or above the media
    ''' area) scales like any other instead of being clamped to zero. A DPI or monitor change
    ''' re-places the panel at the same visual spot instead of compounding device pixels.</summary>
    Public Shared Function ToDeviceOffset(logical As Point, factor As Single) As Point
        Return New Point(ToDevice(logical.X, factor), ToDevice(logical.Y, factor))
    End Function

    ''' <summary>True when the panel counts as stranded (219 §6): the header strip - the grip, the
    ''' only thing that can bring it back - is not fully inside the media area.</summary>
    Public Shared Function HeaderInside(location As Point, panelSize As Size, headerHeight As Integer,
                                        area As Rectangle) As Boolean
        Return area.Contains(New Rectangle(location, New Size(panelSize.Width, headerHeight)))
    End Function

    ''' <summary>The shortest translation that puts the whole panel inside <paramref name="bounds"/>
    ''' (the media area, margins already applied), in the direction it left (219 §6): the panel keeps
    ''' its side of the area, only the offending axis moves. A panel larger than the bounds keeps its
    ''' top-left corner at the bounds' origin, so the header stays reachable.</summary>
    Public Shared Function PullInside(location As Point, panelSize As Size, bounds As Rectangle) As Point
        Dim x As Integer = location.X
        Dim y As Integer = location.Y
        If panelSize.Width <= bounds.Width Then
            x = Math.Max(bounds.X, Math.Min(x, bounds.Right - panelSize.Width))
        Else
            x = bounds.X
        End If
        If panelSize.Height <= bounds.Height Then
            y = Math.Max(bounds.Y, Math.Min(y, bounds.Bottom - panelSize.Height))
        Else
            y = bounds.Y
        End If
        Return New Point(x, y)
    End Function

    ''' <summary>The reveal hot zone of a placed panel (219 §7, Q219-4): the panel's own rectangle
    ''' grown outward toward the nearest edges of the media area, wherever the user put it - the
    ''' configured corner is a position, not a compass. Each axis grows toward the half of the area
    ''' the panel's centre is in, which reproduces the anchored-corner zones of 117 Ф-2 exactly
    ''' wherever the corner choice still places the panel.</summary>
    Public Shared Function HotZone(panel As Rectangle, area As Rectangle, reach As Integer) As Rectangle
        Dim zone As Rectangle = panel
        If reach <= 0 Then Return zone
        Dim growLeft As Boolean = panel.Left + panel.Width \ 2 < area.Left + area.Width \ 2
        Dim growUp As Boolean = panel.Top + panel.Height \ 2 < area.Top + area.Height \ 2
        If growLeft Then
            zone.X -= reach
            zone.Width += reach
        Else
            zone.Width += reach
        End If
        If growUp Then
            zone.Y -= reach
            zone.Height += reach
        Else
            zone.Height += reach
        End If
        Return zone
    End Function

    Private Shared Function ToLogical(device As Integer, factor As Single) As Integer
        If Single.IsNaN(factor) OrElse factor <= 0.0F Then factor = 1.0F
        Return CInt(Math.Round(device / CDbl(factor), MidpointRounding.AwayFromZero))
    End Function

    Private Shared Function ToDevice(logical As Integer, factor As Single) As Integer
        If Single.IsNaN(factor) OrElse factor <= 0.0F Then factor = 1.0F
        Dim scaled As Double = Math.Round(logical * CDbl(factor), MidpointRounding.AwayFromZero)
        If scaled > 100000.0R Then Return 100000
        If scaled < -100000.0R Then Return -100000
        Return CInt(scaled)
    End Function

End Class

''' <summary>
''' The pure arithmetic of the recipients table's ROWS (125 UX-332, layout part): where the delete row stands
''' and whether a folder row has the room to say its verb. Numbers in, numbers out - no window, no font.
''' </summary>
Friend NotInheritable Class RecipientsOverlayRows

    Private Sub New()
    End Sub

    ''' <summary>How much of a row's height the picture it carries takes: the bin of the delete row and the copy
    ''' sheets of the copy zone. Seven tenths keeps the drawing clear of the row's edges and, at the default
    ''' font, lands where the old fixed 16 px picture did.</summary>
    Public Const GlyphShareOfRow As Double = 0.7R

    ''' <summary>The least a row picture is drawn at, in device pixels: below it a catalog drawing is a smudge.</summary>
    Public Const MinimumGlyphSide As Integer = 12

    ''' <summary>The side of the square a row's picture is drawn in, from the row's own height (device pixels in,
    ''' device pixels out). The table's font is a setting and the row follows it, so a picture with a size of its
    ''' own stayed a 16 px stamp beside a large caption; this one grows and shrinks with the row. Never taller
    ''' than the row less two pixels of air, so it cannot touch the row's edge or the next row.</summary>
    Public Shared Function GlyphSide(rowHeight As Integer) As Integer
        If rowHeight <= 0 Then Return 0
        Dim side As Integer = CInt(Math.Round(rowHeight * GlyphShareOfRow, MidpointRounding.AwayFromZero))
        Return Math.Max(1, Math.Min(Math.Max(MinimumGlyphSide, side), rowHeight - 2))
    End Function

    ''' <summary>The air between the last folder row and the delete row. Delete is the one row that destroys, and it
    ''' used to sit flush under the last folder - one slip of the pointer away from "move there". Half a row of
    ''' space (in the same units as <paramref name="rowHeight"/>) is enough to read it as another thing; none when
    ''' there is no folder row above it (nothing to be apart from).</summary>
    Public Shared Function DeleteRowGap(folderRowsAbove As Integer, rowHeight As Integer) As Integer
        If folderRowsAbove <= 0 OrElse rowHeight <= 0 Then Return 0
        Return Math.Max(1, rowHeight \ 2)
    End Function

    ''' <summary>Whether a folder row (its move zone) has room for the verb at its end. The folder's name is what
    ''' the row is for: the verb is drawn only when the caption still keeps <paramref name="minimumCaptionWidth"/>
    ''' after it, so a narrow table loses the word, never the path.</summary>
    ''' <param name="rowWidth">The row's whole width.</param>
    ''' <param name="captionLeft">Where the caption starts (after the padding and the glyph).</param>
    ''' <param name="edgePadding">The padding at the row's far end.</param>
    ''' <param name="verbWidth">The verb's width with the space before it; 0 or less = there is no verb.</param>
    ''' <param name="minimumCaptionWidth">The least the caption must keep.</param>
    Public Shared Function ShowsMoveVerb(rowWidth As Integer, captionLeft As Integer, edgePadding As Integer,
                                         verbWidth As Integer, minimumCaptionWidth As Integer) As Boolean
        If verbWidth <= 0 Then Return False
        Return rowWidth - captionLeft - edgePadding - verbWidth >= Math.Max(0, minimumCaptionWidth)
    End Function

End Class
#End If
