#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Window geometry arithmetic behind APP-BEHAVIOUR rule 10, kept free of any window so
''' it is testable without a display (028_SPECIFICATION_CONTRACT_SYNC_DESKTOP_APP_UX.md
''' Ф-2). Two questions, both easy to get wrong in a way nobody notices until it happens
''' to a user:
'''
'''   * which rectangle to REMEMBER - a maximized or full-screen window reports the
'''     screen's rectangle, and writing that over the normal one means the window has
'''     forgotten its size the next time it is un-maximized;
'''   * where to PUT a remembered rectangle - one that lands on no display (the monitor
'''     it was on is gone) is moved to the nearest surviving display, silently, rather
'''     than to a fixed default.
'''
''' Modern build only: the x86 fallback keeps its historical placement under a dated
''' exception (maintenance policy, CLAUDE.md "Two builds, one source tree").
''' </summary>
Friend NotInheritable Class WindowPlacement

    Private Sub New()
    End Sub

    ''' <summary>The remembered window state, as written to the registry.</summary>
    Friend Const StateNormal As String = "normal"
    Friend Const StateMaximized As String = "maximized"
    Friend Const StateFullScreen As String = "fullscreen"

    ''' <summary>
    ''' The rectangle to remember: the window's own bounds when it is in the normal state,
    ''' its restore bounds otherwise. A maximized, full-screen or minimized window never
    ''' overwrites the remembered normal rectangle.
    ''' </summary>
    Friend Shared Function NormalRectangle(state As FormWindowState, bounds As Rectangle, restoreBounds As Rectangle) As Rectangle
        If state = FormWindowState.Normal Then Return bounds
        ' RestoreBounds is empty for a window that was never shown normal; the current
        ' bounds are then the only rectangle there is.
        If restoreBounds.Width <= 0 OrElse restoreBounds.Height <= 0 Then Return bounds
        Return restoreBounds
    End Function

    ''' <summary>
    ''' Where a remembered rectangle goes on the displays that exist now.
    '''
    ''' A rectangle whose TITLE BAR is on a display - its top edge inside a working area
    ''' and a grab-able stretch of the strip below it visible - stays exactly where it
    ''' was. Overlapping a display with the body is not enough: a window saved straddling
    ''' two stacked monitors, the upper one since removed, overlaps the survivor widely
    ''' with its title bar off every screen (audit B-16). Anything else is moved onto the
    ''' nearest display's working area, keeping its size when it fits and its offset from
    ''' that area's edge when it can. With no display information at all the rectangle is
    ''' returned unchanged.
    ''' </summary>
    Friend Shared Function Place(saved As Rectangle, workAreas As IList(Of Rectangle)) As Rectangle
        If workAreas Is Nothing OrElse workAreas.Count = 0 Then Return saved

        For Each area In workAreas
            If TitleBarOn(saved, area) Then Return saved
        Next

        Dim nearest = workAreas(0)
        Dim best = Long.MaxValue
        For Each area In workAreas
            Dim d = DistanceSquared(area, saved)
            If d < best Then
                best = d
                nearest = area
            End If
        Next
        Return ClampInto(saved, nearest)
    End Function

    ''' <summary>A rectangle moved and, if it must be, shrunk to lie inside <paramref name="area"/>.</summary>
    Friend Shared Function ClampInto(r As Rectangle, area As Rectangle) As Rectangle
        Dim width = Math.Min(r.Width, area.Width)
        Dim height = Math.Min(r.Height, area.Height)
        Dim left = Math.Max(area.Left, Math.Min(r.Left, area.Right - width))
        Dim top = Math.Max(area.Top, Math.Min(r.Top, area.Bottom - height))
        Return New Rectangle(left, top, width, height)
    End Function

    ''' <summary>How much of a window has to be on a display for it to count as on it -
    ''' enough of a title bar to grab.</summary>
    Friend Const MinimumVisible As Integer = 40

    ''' <summary>Height of the strip at the top of a window that is its title bar, for
    ''' the purpose of "can it be grabbed".</summary>
    Friend Const TitleStripHeight As Integer = 30

    ''' <summary>How far above a working area a window's top may sit and still count as on
    ''' it: the invisible resize frame Windows 10+ adds around a window.</summary>
    Friend Const TopFrameTolerance As Integer = 8

    ''' <summary>The window's title strip lies on <paramref name="area"/>: its top edge is
    ''' inside the area (allowing for the invisible frame) with the whole strip above the
    ''' area's bottom, and at least <see cref="MinimumVisible"/> pixels of it are
    ''' horizontally inside.</summary>
    Private Shared Function TitleBarOn(r As Rectangle, area As Rectangle) As Boolean
        If r.Top < area.Top - TopFrameTolerance Then Return False
        If r.Top + TitleStripHeight > area.Bottom Then Return False
        Dim visibleWidth = Math.Min(r.Right, area.Right) - Math.Max(r.Left, area.Left)
        Return visibleWidth >= MinimumVisible
    End Function

    ''' <summary>Squared distance between the rectangle's centre and the nearest point of the area.</summary>
    Private Shared Function DistanceSquared(area As Rectangle, r As Rectangle) As Long
        Dim cx = r.Left + r.Width \ 2
        Dim cy = r.Top + r.Height \ 2
        Dim dx As Long = 0
        If cx < area.Left Then
            dx = area.Left - cx
        ElseIf cx > area.Right Then
            dx = cx - area.Right
        End If
        Dim dy As Long = 0
        If cy < area.Top Then
            dy = area.Top - cy
        ElseIf cy > area.Bottom Then
            dy = cy - area.Bottom
        End If
        Return dx * dx + dy * dy
    End Function

    ''' <summary>The working areas of the displays attached right now.</summary>
    Friend Shared Function CurrentWorkAreas() As List(Of Rectangle)
        Dim areas As New List(Of Rectangle)()
        Try
            For Each s In Screen.AllScreens
                areas.Add(s.WorkingArea)
            Next
        Catch
            ' No display information: Place then keeps the rectangle as it is.
        End Try
        Return areas
    End Function

    ''' <summary>A registry value read back as a state, with anything unknown as normal.</summary>
    Friend Shared Function ParseState(value As String) As String
        Select Case If(value, "").Trim().ToLowerInvariant()
            Case StateMaximized : Return StateMaximized
            Case StateFullScreen : Return StateFullScreen
            Case Else : Return StateNormal
        End Select
    End Function

End Class
#End If
