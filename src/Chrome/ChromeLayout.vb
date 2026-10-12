#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic

''' <summary>
''' One item's claim on a row: how wide it wants to be and whether it can give some of that up.
''' </summary>
Friend Structure ChromeSlot
    Public ReadOnly Id As String
    ''' <summary>The width the item wants (device px).</summary>
    Public ReadOnly Width As Integer
    ''' <summary>The narrowest the item can be. Smaller than <see cref="Width"/> = the flexible item
    ''' of the row (the path box); equal = a rigid one.</summary>
    Public ReadOnly MinWidth As Integer
    ''' <summary>A pinned item is the row's priority core and never collapses into the menu.</summary>
    Public ReadOnly Pinned As Boolean

    Public Sub New(id As String, width As Integer, Optional minWidth As Integer = -1, Optional pinned As Boolean = False)
        Me.Id = id
        Me.Width = Math.Max(0, width)
        Me.MinWidth = If(minWidth < 0, Me.Width, Math.Min(Math.Max(0, minWidth), Me.Width))
        Me.Pinned = pinned
    End Sub

    Public ReadOnly Property IsFlexible As Boolean
        Get
            Return MinWidth < Width
        End Get
    End Property
End Structure

''' <summary>What <see cref="ChromeLayout.ArrangeRow"/> decided.</summary>
Friend NotInheritable Class ChromeRowLayout

    Public Sub New(rects As Rectangle(), overflowed As Boolean(), overflowButton As Rectangle)
        Me.Rects = rects
        Me.Overflowed = overflowed
        Me.OverflowButton = overflowButton
    End Sub

    ''' <summary>One rectangle per input slot, in input order. An overflowed slot has
    ''' <see cref="Rectangle.Empty"/>.</summary>
    Public ReadOnly Property Rects As Rectangle()

    ''' <summary>Per input slot: True when it collapsed into the row's menu.</summary>
    Public ReadOnly Property Overflowed As Boolean()

    ''' <summary>Where the "more" button goes; <see cref="Rectangle.Empty"/> when nothing overflowed.</summary>
    Public ReadOnly Property OverflowButton As Rectangle

    Public ReadOnly Property HasOverflow As Boolean
        Get
            Return Not OverflowButton.IsEmpty
        End Get
    End Property

    ''' <summary>The input indexes that collapsed, in reading order.</summary>
    Public Function OverflowIndexes() As List(Of Integer)
        Dim result As New List(Of Integer)()
        For i As Integer = 0 To Overflowed.Length - 1
            If Overflowed(i) Then result.Add(i)
        Next
        Return result
    End Function
End Class

''' <summary>
''' The row layout of the floating chrome (002 section 2.5) - a PURE function: widths in, rectangles
''' out, so the overflow behaviour that was only ever visible by dragging the window edge can be
''' tested. The two-row toolbar and the per-row "more" menu of Main_Form.ToolbarOverflow.vb are kept as
''' rules rather than as code:
'''   * everything fits - nothing overflows, nothing is narrowed;
'''   * it does not fit - the flexible item (the path box) gives up width first, down to its minimum;
'''   * it still does not fit - the LAST non-pinned items, in reading order, collapse into the menu
'''     and a "more" button takes the end of the row; the flexible item then takes back whatever is
'''     left, up to its preferred width;
'''   * a right-to-left row is the same row mirrored: reading order is the contract, not left to right.
''' </summary>
Friend NotInheritable Class ChromeLayout

    Private Sub New()
    End Sub

    ''' <param name="slots">The visible items in reading order (a hidden item is not passed).</param>
    ''' <param name="availableWidth">The inner width of the row.</param>
    ''' <param name="rowHeight">Each rectangle's height; they all start at y = 0.</param>
    ''' <param name="gap">Space between neighbours.</param>
    ''' <param name="overflowButtonWidth">Width of the "more" button, reserved only when needed.</param>
    ''' <param name="rightToLeft">Mirror the finished row.</param>
    Public Shared Function ArrangeRow(slots As IList(Of ChromeSlot), availableWidth As Integer, rowHeight As Integer,
                                      gap As Integer, overflowButtonWidth As Integer,
                                      Optional rightToLeft As Boolean = False) As ChromeRowLayout
        If slots Is Nothing Then Throw New ArgumentNullException(NameOf(slots))
        If gap < 0 Then gap = 0
        If availableWidth < 0 Then availableWidth = 0

        Dim count As Integer = slots.Count
        Dim widths(count - 1) As Integer
        Dim kept As New List(Of Integer)(count)
        For i As Integer = 0 To count - 1
            widths(i) = slots(i).Width
            kept.Add(i)
        Next

        ' 1. Does it fit as asked?
        Dim overflowed(count - 1) As Boolean
        Dim hasOverflow As Boolean = False

        If Not Fits(widths, kept, gap, availableWidth, 0) Then
            ' 2. The flexible items give up width first, down to their minimum.
            For i As Integer = 0 To count - 1
                If slots(i).IsFlexible Then widths(i) = slots(i).MinWidth
            Next

            ' 3. Still too wide: drop the last non-pinned item, again and again, with the "more"
            '    button's room reserved the moment the first one goes.
            Dim reserve As Integer = 0
            While Not Fits(widths, kept, gap, availableWidth, reserve)
                Dim drop As Integer = LastDroppable(slots, kept)
                If drop < 0 Then Exit While   ' only the pinned core is left: it stays, clipped by the caller
                kept.Remove(drop)
                overflowed(drop) = True
                hasOverflow = True
                reserve = overflowButtonWidth + If(kept.Count > 0, gap, 0)
            End While

            ' 4. Whatever is left over goes back to the flexible items, in order, up to what they wanted.
            Dim spare As Integer = availableWidth - reserve - Used(widths, kept, gap)
            For Each i As Integer In kept
                If spare <= 0 Then Exit For
                If slots(i).IsFlexible Then
                    Dim give As Integer = Math.Min(spare, slots(i).Width - widths(i))
                    widths(i) += give
                    spare -= give
                End If
            Next
        End If

        ' Positions, left to right.
        Dim rects(count - 1) As Rectangle
        Dim x As Integer = 0
        For Each i As Integer In kept
            rects(i) = New Rectangle(x, 0, widths(i), rowHeight)
            x += widths(i) + gap
        Next

        Dim moreRect As Rectangle = Rectangle.Empty
        If hasOverflow Then
            moreRect = New Rectangle(If(kept.Count > 0, x, 0), 0, overflowButtonWidth, rowHeight)
        End If

        If rightToLeft Then
            For Each i As Integer In kept
                rects(i) = Mirror(rects(i), availableWidth)
            Next
            If hasOverflow Then moreRect = Mirror(moreRect, availableWidth)
        End If

        Return New ChromeRowLayout(rects, overflowed, moreRect)
    End Function

    ''' <summary>The width a row of these items would use, gaps included.</summary>
    Public Shared Function NaturalWidth(slots As IList(Of ChromeSlot), gap As Integer) As Integer
        If slots Is Nothing OrElse slots.Count = 0 Then Return 0
        Dim total As Integer = 0
        For Each s As ChromeSlot In slots
            total += s.Width
        Next
        Return total + Math.Max(0, gap) * (slots.Count - 1)
    End Function

    ''' <summary>The floor of <see cref="ArrangeRow"/>: the flexible items at their minimum, only the
    ''' pinned core kept, and the "more" button's room reserved the moment anything has collapsed
    ''' (which it has, or the caller would not be asking). A row given less width than this has lost
    ''' priority content - the caller must find room another way, not shrink further (188 §3.5).</summary>
    Public Shared Function PinnedCoreWidth(slots As IList(Of ChromeSlot), gap As Integer,
                                           overflowButtonWidth As Integer) As Integer
        If slots Is Nothing OrElse slots.Count = 0 Then Return 0
        Dim gapValue As Integer = Math.Max(0, gap)
        Dim total As Integer = 0
        Dim pinnedCount As Integer = 0
        Dim droppable As Boolean = False
        For index As Integer = 0 To slots.Count - 1
            Dim slot As ChromeSlot = slots(index)
            If slot.Pinned Then
                Dim width As Integer = If(slot.IsFlexible, slot.MinWidth, slot.Width)
                total += If(pinnedCount > 0, gapValue, 0) + Math.Max(0, width)
                pinnedCount += 1
            Else
                droppable = True
            End If
        Next
        If Not droppable Then Return total
        Return total + overflowButtonWidth + If(pinnedCount > 0, gapValue, 0)
    End Function

    ''' <summary>Where a TOP row stands beside the window-controls panel in its corner (188 §3.5):
    ''' the row shares the top band with the panel, capped to the width left of it, or - when its
    ''' minimum content cannot fit in that width - wraps locally BELOW the panel at full width.
    ''' Pure; every rectangle is in one coordinate space (the media area), the panel's top-left
    ''' corner at its <see cref="Rectangle.Location"/>.</summary>
    Public Structure RowBandPlacement
        ''' <summary>The row's top, media-relative.</summary>
        Public ReadOnly Top As Integer
        ''' <summary>The widest the row's host may be. The full band when the row wrapped or never overlapped.</summary>
        Public ReadOnly MaxWidth As Integer
        ''' <summary>The width taken out of the band's far (corner) end for the panel; 0 when the row wrapped.</summary>
        Public ReadOnly ReservedWidth As Integer
        ''' <summary>True when the row moved below the panel instead of sharing the band.</summary>
        Public ReadOnly Wrapped As Boolean

        Public Sub New(top As Integer, maxWidth As Integer, reservedWidth As Integer, wrapped As Boolean)
            Me.Top = top
            Me.MaxWidth = maxWidth
            Me.ReservedWidth = reservedWidth
            Me.Wrapped = wrapped
        End Sub
    End Structure

    ''' <summary>The band decision of <see cref="RowBandPlacement"/>.</summary>
    Public Shared Function PlaceRowBesideCornerPanel(naturalTop As Integer, rowHeight As Integer,
                                                     minimumRowWidth As Integer,
                                                     cornerPanel As Rectangle, mediaWidth As Integer,
                                                     gap As Integer, inset As Integer) As RowBandPlacement
        Dim bandWidth As Integer = Math.Max(0, mediaWidth - 2 * Math.Max(0, inset))
        Dim overlaps As Boolean = cornerPanel.Width > 0 AndAlso cornerPanel.Height > 0 AndAlso
                                  naturalTop < cornerPanel.Bottom AndAlso naturalTop + rowHeight > cornerPanel.Top
        If Not overlaps Then Return New RowBandPlacement(naturalTop, bandWidth, 0, False)
        Dim gapValue As Integer = Math.Max(0, gap)
        Dim sharedWidth As Integer = bandWidth - cornerPanel.Width - gapValue
        If sharedWidth > 0 AndAlso minimumRowWidth <= sharedWidth Then
            Return New RowBandPlacement(naturalTop, sharedWidth, cornerPanel.Width + gapValue, False)
        End If
        Return New RowBandPlacement(Math.Max(naturalTop, cornerPanel.Bottom + gapValue), bandWidth, 0, True)
    End Function

    ''' <summary>
    ''' The status strip's two cells: the file name and the status words (121 UX-415). The name is entitled to its own
    ''' width up to half the strip, so a long sentence can no longer squeeze it (it used to be left with the third
    ''' the sentence did not take); the words get everything the name leaves, never more than they need.
    ''' </summary>
    ''' <returns>The width of the status cell; the file name's is <c>contentWidth - result</c>.</returns>
    Public Shared Function StatusCellWidth(contentWidth As Integer, fileNaturalWidth As Integer, statusNaturalWidth As Integer) As Integer
        If contentWidth <= 0 Then Return 0
        Dim nameShare As Integer = Math.Min(Math.Max(0, fileNaturalWidth), contentWidth \ 2)
        Return Math.Max(0, Math.Min(Math.Max(0, statusNaturalWidth), contentWidth - nameShare))
    End Function

    Private Shared Function Mirror(r As Rectangle, availableWidth As Integer) As Rectangle
        Return New Rectangle(availableWidth - r.Right, r.Y, r.Width, r.Height)
    End Function

    Private Shared Function Used(widths As Integer(), kept As List(Of Integer), gap As Integer) As Integer
        If kept.Count = 0 Then Return 0
        Dim total As Integer = 0
        For Each i As Integer In kept
            total += widths(i)
        Next
        Return total + gap * (kept.Count - 1)
    End Function

    Private Shared Function Fits(widths As Integer(), kept As List(Of Integer),
                                 gap As Integer, availableWidth As Integer, reserve As Integer) As Boolean
        Return Used(widths, kept, gap) + reserve <= availableWidth
    End Function

    Private Shared Function LastDroppable(slots As IList(Of ChromeSlot), kept As List(Of Integer)) As Integer
        For k As Integer = kept.Count - 1 To 0 Step -1
            If Not slots(kept(k)).Pinned Then Return kept(k)
        Next
        Return -1
    End Function

End Class
#End If
