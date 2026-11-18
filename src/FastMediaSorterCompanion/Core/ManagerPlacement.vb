Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing

''' <summary>
''' Where the Share Manager's windows go (L0326), kept free of any window so it is testable without a display.
''' Two questions of the desktop contracts, both answered with the viewer's own pure arithmetic
''' (<see cref="WindowPlacement"/>, linked into this program):
'''   * <see cref="RestoreRectangle"/> - APP-BEHAVIOUR rule 10: a remembered rectangle that meets no display (the monitor
'''     it was on is gone) is moved to the nearest surviving one, silently, and its size is held to the minimum and to
'''     the working area;
'''   * <see cref="ForRequestingMonitor"/> - APP-SETTINGS rule 1: a settings window that already exists is brought to the
'''     monitor the user is working on, not left where it was last seen.
''' </summary>
Friend NotInheritable Class ManagerPlacement

    Private Sub New()
    End Sub

    ''' <summary>
    ''' The rectangle the main window opens with. The size (already in device pixels) is raised to <paramref name="minimum"/>
    ''' and cut to <paramref name="referenceArea"/> (the working area of the screen the window opens on); the place is the
    ''' saved one while its title bar is on a display, otherwise the nearest display's. With no display information the
    ''' place is the saved one unchanged and <paramref name="hasDisplays"/> is False, so the caller keeps the system's
    ''' default place instead of applying a coordinate nothing confirmed.
    ''' </summary>
    Friend Shared Function RestoreRectangle(savedX As Integer, savedY As Integer, widthPx As Integer, heightPx As Integer,
                                            minimum As Size, referenceArea As Rectangle,
                                            workAreas As IList(Of Rectangle), ByRef hasDisplays As Boolean) As Rectangle
        Dim w As Integer = Math.Max(widthPx, minimum.Width)
        Dim h As Integer = Math.Max(heightPx, minimum.Height)
        If referenceArea.Width > 0 Then w = Math.Min(w, referenceArea.Width)
        If referenceArea.Height > 0 Then h = Math.Min(h, referenceArea.Height)
        hasDisplays = workAreas IsNot Nothing AndAlso workAreas.Count > 0
        Return WindowPlacement.Place(New Rectangle(savedX, savedY, w, h), workAreas)
    End Function

    ''' <summary>
    ''' The bounds a reused window takes when it is asked for from <paramref name="area"/> (the working area of the monitor
    ''' the user is on). A window whose title bar is already on that area stays exactly where it is - the user may have
    ''' placed it - and any other (on another monitor, or on none) is centred on the area, shrunk to fit if it has to be.
    ''' An empty area changes nothing.
    ''' </summary>
    Friend Shared Function ForRequestingMonitor(bounds As Rectangle, area As Rectangle) As Rectangle
        If area.Width <= 0 OrElse area.Height <= 0 OrElse bounds.Width <= 0 OrElse bounds.Height <= 0 Then Return bounds
        Dim only As New List(Of Rectangle) From {area}
        If WindowPlacement.Place(bounds, only) = bounds Then Return bounds
        Dim w As Integer = Math.Min(bounds.Width, area.Width)
        Dim h As Integer = Math.Min(bounds.Height, area.Height)
        Return New Rectangle(area.Left + (area.Width - w) \ 2, area.Top + (area.Height - h) \ 2, w, h)
    End Function

End Class
