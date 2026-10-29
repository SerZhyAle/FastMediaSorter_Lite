#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization

''' <summary>The physical corner of the media area a free panel's place is measured from (L0281 §3.2). Physical,
''' not reading-direction: a place does not mirror with the UI language.</summary>
Friend Enum PanelCorner
    TopLeft
    TopRight
    BottomLeft
    BottomRight
End Enum

''' <summary>The stored place of a free floating panel (L0281 §3.2): the nearest corner of the media area and the
''' distances, in LOGICAL (96-dpi) px, from that corner's two edges to the panel's nearest edges. Never negative.
''' An anchored panel has no place at all.</summary>
Friend Structure PanelPlace
    Public ReadOnly Corner As PanelCorner
    Public ReadOnly Dx As Integer
    Public ReadOnly Dy As Integer

    Public Sub New(corner As PanelCorner, dx As Integer, dy As Integer)
        Me.Corner = corner
        Me.Dx = Math.Max(0, Math.Min(dx, FloatingPanelPlacement.MaxLogicalDistance))
        Me.Dy = Math.Max(0, Math.Min(dy, FloatingPanelPlacement.MaxLogicalDistance))
    End Sub
End Structure

''' <summary>What a finished move of a floating panel comes to (L0281 §3.5): nothing to write, the place cleared (the panel is
''' anchored again), the move refused (the panel returns to where the gesture began), or a new place to store.</summary>
Friend Enum PanelDropOutcome
    Unchanged
    Anchor
    Refused
    Placed
End Enum

''' <summary>
''' The pure geometry of a user-movable floating panel (L0281 §3): the place a drop is stored as, the rectangle a
''' place resolves to in an area of any size, the fit that keeps every pixel of a panel inside the allowed area,
''' the push-out from a protected surface, the snap back to the anchor, the keyboard step and the reveal zone.
''' Rectangles and numbers only - no window, no form - so R2 and R3 are provable without a screen. The intended
''' place is kept apart from the effective rectangle: nothing here ever writes a fitted rectangle back into a place.
''' </summary>
Friend NotInheritable Class FloatingPanelPlacement

    Private Sub New()
    End Sub

    ''' <summary>The largest distance a stored place may carry, in logical px. A larger number is not a place a
    ''' drop on any real monitor can produce, so the reader takes it for a garbled value (anchored).</summary>
    Public Const MaxLogicalDistance As Integer = 20000

    ''' <summary>The corner of <paramref name="area"/> nearest to the panel's centre - the same half-split the
    ''' reveal zone uses, so the corner a place is measured from is the corner its zone grows toward.</summary>
    Public Shared Function NearestCorner(panel As Rectangle, area As Rectangle) As PanelCorner
        Dim left As Boolean = IsInStartHalf(panel.Left, panel.Width, area.Left, area.Width)
        Dim top As Boolean = IsInStartHalf(panel.Top, panel.Height, area.Top, area.Height)
        If top Then Return If(left, PanelCorner.TopLeft, PanelCorner.TopRight)
        Return If(left, PanelCorner.BottomLeft, PanelCorner.BottomRight)
    End Function

    ''' <summary>A dropped rectangle to the place stored for it: the nearest corner and the two distances from that
    ''' corner's edges, through the scale back to logical px. A panel standing past an edge counts as flush.</summary>
    Public Shared Function FromRectangle(panel As Rectangle, area As Rectangle, factor As Single) As PanelPlace
        Dim corner As PanelCorner = NearestCorner(panel, area)
        Dim dx As Integer = If(IsLeft(corner), panel.Left - area.Left, area.Right - panel.Right)
        Dim dy As Integer = If(IsTop(corner), panel.Top - area.Top, area.Bottom - panel.Bottom)
        Return New PanelPlace(corner, ToLogical(Math.Max(0, dx), factor), ToLogical(Math.Max(0, dy), factor))
    End Function

    ''' <summary>The 219 recipients point (a logical offset from the media area's top-left) as a place: the
    ''' special case <c>TL:x,y</c>, so an old profile's value is read unchanged (L0281 §3.2, T-6).</summary>
    Public Shared Function FromTopLeftPoint(logical As Point) As PanelPlace
        Return New PanelPlace(PanelCorner.TopLeft, logical.X, logical.Y)
    End Function

    ''' <summary>Where a place puts a panel of <paramref name="panelSize"/> in <paramref name="area"/>, before the
    ''' fit: the distances scaled to device pixels and measured inward from the place's corner.</summary>
    Public Shared Function Resolve(place As PanelPlace, area As Rectangle, panelSize As Size, factor As Single) As Rectangle
        Dim dx As Integer = ToDevice(place.Dx, factor)
        Dim dy As Integer = ToDevice(place.Dy, factor)
        Dim x As Integer = If(IsLeft(place.Corner), area.Left + dx, area.Right - dx - panelSize.Width)
        Dim y As Integer = If(IsTop(place.Corner), area.Top + dy, area.Bottom - dy - panelSize.Height)
        Return New Rectangle(x, y, panelSize.Width, panelSize.Height)
    End Function

    ''' <summary>The effective rectangle of a free panel (L0281 §3.4): the place resolved in the area as it is now,
    ''' then fitted. Computed afresh at every placement; the place itself is never changed by it.</summary>
    Public Shared Function Effective(place As PanelPlace, allowed As Rectangle, panelSize As Size, factor As Single,
                                     startIsRight As Boolean) As Rectangle
        Return Fit(Resolve(place, allowed, panelSize, factor), allowed, startIsRight)
    End Function

    ''' <summary>The fit of L0281 §3.3, per axis: a panel no larger than <paramref name="allowed"/> is moved inside
    ''' it by the shortest translation; a panel larger than it on an axis is aligned to the top, or to the start
    ''' edge (the right one when <paramref name="startIsRight"/>), on that axis. The size never changes.</summary>
    Public Shared Function Fit(panel As Rectangle, allowed As Rectangle, startIsRight As Boolean) As Rectangle
        Dim x As Integer = panel.X
        Dim y As Integer = panel.Y
        If panel.Width <= allowed.Width Then
            x = Math.Max(allowed.X, Math.Min(x, allowed.Right - panel.Width))
        Else
            x = If(startIsRight, allowed.Right - panel.Width, allowed.X)
        End If
        If panel.Height <= allowed.Height Then
            y = Math.Max(allowed.Y, Math.Min(y, allowed.Bottom - panel.Height))
        Else
            y = allowed.Y
        End If
        Return New Rectangle(x, y, panel.Width, panel.Height)
    End Function

    ''' <summary>A drop that meets a protected surface (the window commands, the visible transport bar) is pushed
    ''' out along the shortest axis and refitted (L0281 §3.5). False when no candidate is both inside the allowed
    ''' area and clear of every protected surface: the move is refused and <paramref name="result"/> is the panel
    ''' unchanged. A drop that meets none is its own result.</summary>
    Public Shared Function TryPushOut(panel As Rectangle, allowed As Rectangle, protectedRects As IList(Of Rectangle),
                                      startIsRight As Boolean, ByRef result As Rectangle) As Boolean
        result = panel
        If Not MeetsAny(panel, protectedRects) Then Return True
        Dim found As Boolean = False
        Dim bestCost As Long = Long.MaxValue
        For Each guarded As Rectangle In protectedRects
            If guarded.IsEmpty OrElse Not guarded.IntersectsWith(panel) Then Continue For
            Dim candidates As Rectangle() = {
                New Rectangle(guarded.Left - panel.Width, panel.Y, panel.Width, panel.Height),
                New Rectangle(guarded.Right, panel.Y, panel.Width, panel.Height),
                New Rectangle(panel.X, guarded.Top - panel.Height, panel.Width, panel.Height),
                New Rectangle(panel.X, guarded.Bottom, panel.Width, panel.Height)}
            For Each candidate As Rectangle In candidates
                Dim fitted As Rectangle = Fit(candidate, allowed, startIsRight)
                If MeetsAny(fitted, protectedRects) Then Continue For
                Dim cost As Long = CLng(Math.Abs(fitted.X - panel.X)) + Math.Abs(fitted.Y - panel.Y)
                If cost < bestCost Then
                    bestCost = cost
                    result = fitted
                    found = True
                End If
            Next
        Next
        If Not found Then result = panel
        Return found
    End Function

    ''' <summary>True when a drop lands within <paramref name="snap"/> device pixels of the panel's anchored
    ''' rectangle on both axes (L0281 §3.5, Q281-6): the place is cleared and the panel is anchored again.</summary>
    Public Shared Function SnapsToAnchor(panel As Rectangle, anchored As Rectangle, snap As Integer) As Boolean
        If snap < 0 OrElse anchored.IsEmpty Then Return False
        Return Math.Abs(panel.X - anchored.X) <= snap AndAlso Math.Abs(panel.Y - anchored.Y) <= snap
    End Function

    ''' <summary>The allowed rectangle of L0281 §3.3: the media area inside the window's client area, inside the visible
    ''' part of the screen the window is on, inset on every side. A window with no part on that screen falls back to the
    ''' window alone (and an empty media area to the media area as given), so the result is never a stranded
    ''' rectangle at the origin; its size is never negative.</summary>
    Public Shared Function AllowedArea(media As Rectangle, client As Rectangle, screenArea As Rectangle, inset As Integer) As Rectangle
        Dim inWindow As Rectangle = Rectangle.Intersect(media, client)
        If inWindow.Width <= 0 OrElse inWindow.Height <= 0 Then inWindow = media
        Dim visible As Rectangle = Rectangle.Intersect(inWindow, screenArea)
        If visible.Width <= 0 OrElse visible.Height <= 0 Then visible = inWindow
        Dim edge As Integer = Math.Max(0, inset)
        Dim width As Integer = Math.Max(0, visible.Width - 2 * edge)
        Dim height As Integer = Math.Max(0, visible.Height - 2 * edge)
        Return New Rectangle(visible.X + Math.Min(edge, Math.Max(0, visible.Width) \ 2),
                             visible.Y + Math.Min(edge, Math.Max(0, visible.Height) \ 2), width, height)
    End Function

    ''' <summary>What a finished move comes to (L0281 §3.5). A drop where the gesture began writes nothing
    ''' (<see cref="PanelDropOutcome.Unchanged"/>, <paramref name="result"/> = <paramref name="start"/>); a drop within
    ''' <paramref name="snap"/> of the anchored rectangle clears the place (<see cref="PanelDropOutcome.Anchor"/>,
    ''' <paramref name="result"/> = the anchored rectangle); any other drop is fitted into <paramref name="allowed"/> and
    ''' pushed out of the protected surfaces: no free fit refuses it (<see cref="PanelDropOutcome.Refused"/>, the panel
    ''' returns to <paramref name="start"/>), a result equal to the start is unchanged, anything else is the new place
    ''' (<see cref="PanelDropOutcome.Placed"/>, <paramref name="result"/> inside <paramref name="allowed"/>).</summary>
    Public Shared Function DecideDrop(start As Rectangle, dropped As Rectangle, anchored As Rectangle, allowed As Rectangle,
                                      protectedRects As IList(Of Rectangle), snap As Integer, startIsRight As Boolean,
                                      ByRef result As Rectangle) As PanelDropOutcome
        result = start
        If dropped = start Then Return PanelDropOutcome.Unchanged
        Dim fitted As Rectangle = Fit(dropped, allowed, startIsRight)
        If SnapsToAnchor(fitted, anchored, snap) Then
            result = anchored
            Return PanelDropOutcome.Anchor
        End If
        Dim pushed As Rectangle
        If Not TryPushOut(fitted, allowed, protectedRects, startIsRight, pushed) Then Return PanelDropOutcome.Refused
        If pushed = start Then Return PanelDropOutcome.Unchanged
        result = pushed
        Return PanelDropOutcome.Placed
    End Function

    ''' <summary>One keyboard step of the move mode (L0281 §3.6): the panel moved by whole steps of
    ''' <paramref name="stepPixels"/> on each axis, through the same fit as a drag.</summary>
    Public Shared Function Nudge(panel As Rectangle, stepsX As Integer, stepsY As Integer, stepPixels As Integer,
                                 allowed As Rectangle, startIsRight As Boolean) As Rectangle
        Dim moved As Rectangle = panel
        moved.Offset(stepsX * Math.Max(0, stepPixels), stepsY * Math.Max(0, stepPixels))
        Return Fit(moved, allowed, startIsRight)
    End Function

    ''' <summary>The reveal zone of a free panel (219 §7, Q219-4; L0281 §3.8): its own rectangle grown outward toward
    ''' the nearest edges of the area, by the same half-split as <see cref="NearestCorner"/>.</summary>
    Public Shared Function HotZone(panel As Rectangle, area As Rectangle, reach As Integer) As Rectangle
        Dim zone As Rectangle = panel
        If reach <= 0 Then Return zone
        Select Case NearestCorner(panel, area)
            Case PanelCorner.TopLeft
                zone = New Rectangle(zone.X - reach, zone.Y - reach, zone.Width + reach, zone.Height + reach)
            Case PanelCorner.TopRight
                zone = New Rectangle(zone.X, zone.Y - reach, zone.Width + reach, zone.Height + reach)
            Case PanelCorner.BottomLeft
                zone = New Rectangle(zone.X - reach, zone.Y, zone.Width + reach, zone.Height + reach)
            Case Else
                zone = New Rectangle(zone.X, zone.Y, zone.Width + reach, zone.Height + reach)
        End Select
        Return zone
    End Function

    ''' <summary>A place as stored: <c>BR:24,36</c>. Invariant digits, no spaces.</summary>
    Public Shared Function Encode(place As PanelPlace) As String
        Return CornerCode(place.Corner) & ":" & place.Dx.ToString(CultureInfo.InvariantCulture) & "," &
            place.Dy.ToString(CultureInfo.InvariantCulture)
    End Function

    ''' <summary>The tolerant reader of L0281 §5: True and the place only for the encoder's own shape - a corner code
    ''' of two capitals, a colon, two unsigned invariant integers within <see cref="MaxLogicalDistance"/>. Empty,
    ''' Nothing, an unknown corner, a missing or signed number, a third part or any future shape is False
    ''' (anchored). Never throws.</summary>
    Public Shared Function TryDecode(text As String, ByRef place As PanelPlace) As Boolean
        place = New PanelPlace(PanelCorner.TopLeft, 0, 0)
        If String.IsNullOrEmpty(text) Then Return False
        Dim colon As Integer = text.IndexOf(":"c)
        If colon <> 2 Then Return False
        Dim corner As PanelCorner
        Select Case text.Substring(0, 2)
            Case "TL" : corner = PanelCorner.TopLeft
            Case "TR" : corner = PanelCorner.TopRight
            Case "BL" : corner = PanelCorner.BottomLeft
            Case "BR" : corner = PanelCorner.BottomRight
            Case Else : Return False
        End Select
        Dim parts As String() = text.Substring(colon + 1).Split(","c)
        If parts.Length <> 2 Then Return False
        Dim dx, dy As Integer
        If Not TryReadDistance(parts(0), dx) OrElse Not TryReadDistance(parts(1), dy) Then Return False
        place = New PanelPlace(corner, dx, dy)
        Return True
    End Function

    ''' <summary>Device pixels to logical px through the chrome scale (219 §3); a broken factor is 1.</summary>
    Public Shared Function ToLogical(device As Integer, factor As Single) As Integer
        If Single.IsNaN(factor) OrElse factor <= 0.0F Then factor = 1.0F
        Return CInt(Math.Round(device / CDbl(factor), MidpointRounding.AwayFromZero))
    End Function

    ''' <summary>Logical px to device pixels, signed (a position is not a size), held within +-100000.</summary>
    Public Shared Function ToDevice(logical As Integer, factor As Single) As Integer
        If Single.IsNaN(factor) OrElse factor <= 0.0F Then factor = 1.0F
        Dim scaled As Double = Math.Round(logical * CDbl(factor), MidpointRounding.AwayFromZero)
        If scaled > 100000.0R Then Return 100000
        If scaled < -100000.0R Then Return -100000
        Return CInt(scaled)
    End Function

    Private Shared Function TryReadDistance(text As String, ByRef value As Integer) As Boolean
        value = 0
        If text.Length = 0 OrElse text.Length > 5 Then Return False
        If Not Integer.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, value) Then Return False
        Return value <= MaxLogicalDistance
    End Function

    Private Shared Function CornerCode(corner As PanelCorner) As String
        Select Case corner
            Case PanelCorner.TopRight : Return "TR"
            Case PanelCorner.BottomLeft : Return "BL"
            Case PanelCorner.BottomRight : Return "BR"
            Case Else : Return "TL"
        End Select
    End Function

    Private Shared Function IsInStartHalf(start As Integer, length As Integer, areaStart As Integer, areaLength As Integer) As Boolean
        Return start + length \ 2 < areaStart + areaLength \ 2
    End Function

    Private Shared Function IsLeft(corner As PanelCorner) As Boolean
        Return corner = PanelCorner.TopLeft OrElse corner = PanelCorner.BottomLeft
    End Function

    Private Shared Function IsTop(corner As PanelCorner) As Boolean
        Return corner = PanelCorner.TopLeft OrElse corner = PanelCorner.TopRight
    End Function

    Private Shared Function MeetsAny(panel As Rectangle, protectedRects As IList(Of Rectangle)) As Boolean
        If protectedRects Is Nothing Then Return False
        For Each guarded As Rectangle In protectedRects
            If Not guarded.IsEmpty AndAlso guarded.IntersectsWith(panel) Then Return True
        Next
        Return False
    End Function

End Class
#End If
