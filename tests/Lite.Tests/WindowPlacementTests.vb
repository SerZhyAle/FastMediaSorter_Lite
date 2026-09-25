#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' APP-BEHAVIOUR rule 10 - the arithmetic half (028_SPECIFICATION_CONTRACT_SYNC_DESKTOP_APP_UX.md
''' Ф-2, §5 item 7). Both failures it prevents are invisible until they happen to a user: a
''' window that has forgotten its size because it was closed maximized, and one that opens
''' on a monitor that is no longer there.
''' </summary>
Public Class WindowPlacementTests

    Private Shared ReadOnly Primary As New Rectangle(0, 0, 1920, 1040)
    Private Shared ReadOnly LeftOfPrimary As New Rectangle(-1280, 0, 1280, 984)

    <Fact>
    Public Sub A_maximized_window_never_overwrites_the_normal_rectangle()
        Dim normal As New Rectangle(100, 120, 900, 700)
        Dim screenRect As New Rectangle(-8, -8, 1936, 1056)
        Assert.Equal(normal, WindowPlacement.NormalRectangle(FormWindowState.Maximized, screenRect, normal))
        Assert.Equal(normal, WindowPlacement.NormalRectangle(FormWindowState.Minimized, New Rectangle(-32000, -32000, 160, 28), normal))
    End Sub

    <Fact>
    Public Sub A_normal_window_remembers_its_own_bounds()
        Dim bounds As New Rectangle(100, 120, 900, 700)
        Assert.Equal(bounds, WindowPlacement.NormalRectangle(FormWindowState.Normal, bounds, New Rectangle(1, 2, 3, 4)))
    End Sub

    <Fact>
    Public Sub An_empty_restore_rectangle_falls_back_to_the_bounds()
        Dim bounds As New Rectangle(0, 0, 1920, 1040)
        Assert.Equal(bounds, WindowPlacement.NormalRectangle(FormWindowState.Maximized, bounds, Rectangle.Empty))
    End Sub

    <Fact>
    Public Sub A_rectangle_on_a_display_stays_where_it_was()
        Dim onSecond As New Rectangle(-1000, 50, 800, 600)
        Assert.Equal(onSecond, WindowPlacement.Place(onSecond, New List(Of Rectangle) From {Primary, LeftOfPrimary}))
    End Sub

    <Fact>
    Public Sub A_rectangle_on_a_lost_monitor_moves_to_the_nearest_surviving_one()
        ' It was on the monitor left of the primary; that monitor is unplugged.
        Dim wasOnLeft As New Rectangle(-1000, 50, 800, 600)
        Dim placed = WindowPlacement.Place(wasOnLeft, New List(Of Rectangle) From {Primary})
        Assert.True(Primary.Contains(placed), "Placed at " & placed.ToString())
        Assert.Equal(wasOnLeft.Size, placed.Size)
        Assert.Equal(wasOnLeft.Top, placed.Top)
    End Sub

    <Fact>
    Public Sub The_nearest_of_several_displays_is_chosen()
        Dim right As New Rectangle(1920, 0, 1920, 1040)
        Dim farRight As New Rectangle(5000, 300, 600, 400)
        Dim placed = WindowPlacement.Place(farRight, New List(Of Rectangle) From {Primary, right})
        Assert.True(right.Contains(placed), "Placed at " & placed.ToString())
    End Sub

    <Fact>
    Public Sub A_rectangle_larger_than_the_display_is_shrunk_to_fit()
        Dim huge As New Rectangle(-5000, -5000, 4000, 3000)
        Dim placed = WindowPlacement.Place(huge, New List(Of Rectangle) From {Primary})
        Assert.True(Primary.Contains(placed), "Placed at " & placed.ToString())
    End Sub

    <Fact>
    Public Sub A_sliver_on_screen_does_not_count_as_on_screen()
        ' Only 10 px of the window reaches the primary display: not enough to grab it.
        Dim sliver As New Rectangle(1910, 100, 800, 600)
        Dim placed = WindowPlacement.Place(sliver, New List(Of Rectangle) From {Primary})
        Assert.True(Primary.Contains(placed), "Placed at " & placed.ToString())
    End Sub

    <Fact>
    Public Sub A_window_whose_title_bar_is_off_every_display_is_moved()
        ' Audit B-16: saved straddling two stacked monitors, the upper one removed. The
        ' body overlaps the primary by 1200x500, the title bar is at y = -300.
        Dim straddling As New Rectangle(100, -300, 1200, 800)
        Dim placed = WindowPlacement.Place(straddling, New List(Of Rectangle) From {Primary})
        Assert.True(placed.Top >= Primary.Top, "Placed at " & placed.ToString())
        Assert.True(Primary.Contains(placed), "Placed at " & placed.ToString())
    End Sub

    <Fact>
    Public Sub A_window_at_the_top_edge_stays_despite_the_invisible_frame()
        Dim atTop As New Rectangle(200, Primary.Top - 7, 800, 600)
        Assert.Equal(atTop, WindowPlacement.Place(atTop, New List(Of Rectangle) From {Primary}))
    End Sub

    <Fact>
    Public Sub No_display_information_keeps_the_rectangle()
        Dim r As New Rectangle(10, 10, 500, 400)
        Assert.Equal(r, WindowPlacement.Place(r, New List(Of Rectangle)()))
    End Sub

    <Fact>
    Public Sub Unknown_states_read_back_as_normal()
        Assert.Equal(WindowPlacement.StateNormal, WindowPlacement.ParseState(Nothing))
        Assert.Equal(WindowPlacement.StateNormal, WindowPlacement.ParseState("bogus"))
        Assert.Equal(WindowPlacement.StateMaximized, WindowPlacement.ParseState(" Maximized "))
        Assert.Equal(WindowPlacement.StateFullScreen, WindowPlacement.ParseState("fullscreen"))
    End Sub

End Class
#End If
