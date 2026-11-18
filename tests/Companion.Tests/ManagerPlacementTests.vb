Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' L0326: where the Share Manager's windows go, held without a display (<see cref="ManagerPlacement"/>).
'''   * APP-BEHAVIOUR rule 10 - the main window's remembered rectangle that meets no display is moved to the nearest one;
'''   * APP-SETTINGS rule 1 - a reused settings window is brought to the monitor the user is working on.
''' </summary>
Public Class ManagerPlacementTests

    Private Shared ReadOnly LeftScreen As New Rectangle(0, 0, 1920, 1040)
    Private Shared ReadOnly RightScreen As New Rectangle(1920, 0, 1920, 1040)
    Private Shared ReadOnly SmallestWindow As New Size(560, 420)

    Private Shared Function Displays(ParamArray areas As Rectangle()) As IList(Of Rectangle)
        Return New List(Of Rectangle)(areas)
    End Function

    Private Shared Function Restore(x As Integer, y As Integer, w As Integer, h As Integer, areas As IList(Of Rectangle),
                                    ByRef hasDisplays As Boolean) As Rectangle
        Dim reference As Rectangle = If(areas.Count > 0, areas(0), Rectangle.Empty)
        Return ManagerPlacement.RestoreRectangle(x, y, w, h, SmallestWindow, reference, areas, hasDisplays)
    End Function

    ' --- APP-BEHAVIOUR rule 10: restore defensively -----------------------------------------------------------

    <Fact>
    Public Sub A_remembered_place_on_a_display_that_is_still_there_is_kept()
        Dim has As Boolean
        Dim r As Rectangle = Restore(300, 200, 900, 700, Displays(LeftScreen, RightScreen), has)
        Assert.True(has)
        Assert.Equal(New Rectangle(300, 200, 900, 700), r)
    End Sub

    <Fact>
    Public Sub A_place_on_a_monitor_that_is_gone_moves_to_the_nearest_surviving_one()
        Dim has As Boolean
        ' saved on a second monitor to the right (x 1920..3840) that has since been unplugged
        Dim r As Rectangle = Restore(2500, 300, 900, 700, Displays(LeftScreen), has)
        Assert.True(has)
        Assert.True(LeftScreen.Contains(r), "the window lands inside the surviving work area: " & r.ToString())
        Assert.Equal(New Size(900, 700), r.Size)
    End Sub

    <Fact>
    Public Sub The_nearest_of_several_surviving_displays_is_chosen()
        Dim has As Boolean
        Dim far As New Rectangle(-1920, 0, 1920, 1040)
        ' saved far to the right of everything: the right display is nearer than the far left one
        Dim r As Rectangle = Restore(5200, 100, 800, 600, Displays(far, RightScreen), has)
        Assert.True(RightScreen.Contains(r), "the nearest display takes it: " & r.ToString())
    End Sub

    <Fact>
    Public Sub A_title_bar_left_off_every_screen_is_not_a_place_that_counts()
        Dim has As Boolean
        ' a window saved straddling a removed upper monitor: its body overlaps the survivor, its title bar does not
        Dim r As Rectangle = Restore(200, -500, 900, 700, Displays(LeftScreen), has)
        Assert.True(r.Top >= LeftScreen.Top, "the title bar is on the screen again: " & r.ToString())
    End Sub

    <Fact>
    Public Sub The_size_is_raised_to_the_minimum_and_cut_to_the_working_area()
        Dim has As Boolean
        Dim small As Rectangle = Restore(100, 100, 100, 100, Displays(LeftScreen), has)
        Assert.Equal(SmallestWindow, small.Size)
        Dim huge As Rectangle = Restore(0, 0, 9000, 9000, Displays(LeftScreen), has)
        Assert.Equal(LeftScreen.Size, huge.Size)
        Assert.True(LeftScreen.Contains(huge))
    End Sub

    <Fact>
    Public Sub With_no_display_information_the_caller_is_told_to_keep_the_default_place()
        Dim has As Boolean = True
        Dim r As Rectangle = Restore(3000, 3000, 900, 700, Displays(), has)
        Assert.False(has)
        Assert.Equal(New Size(900, 700), r.Size)
    End Sub

    ' --- APP-SETTINGS rule 1: found where the user is ----------------------------------------------------------

    <Fact>
    Public Sub A_settings_window_on_another_monitor_is_centred_on_the_requesting_one()
        Dim onLeft As New Rectangle(200, 150, 1000, 760)
        Dim moved As Rectangle = ManagerPlacement.ForRequestingMonitor(onLeft, RightScreen)
        Assert.Equal(onLeft.Size, moved.Size)
        Assert.Equal(RightScreen.X + (RightScreen.Width - 1000) \ 2, moved.X)
        Assert.Equal(RightScreen.Y + (RightScreen.Height - 760) \ 2, moved.Y)
        Assert.True(RightScreen.Contains(moved))
    End Sub

    <Fact>
    Public Sub A_settings_window_already_on_the_requesting_monitor_stays_where_the_user_put_it()
        Dim placed As New Rectangle(2300, 80, 1000, 760)
        Assert.Equal(placed, ManagerPlacement.ForRequestingMonitor(placed, RightScreen))
    End Sub

    <Fact>
    Public Sub A_window_with_only_a_sliver_of_its_title_bar_on_the_requesting_monitor_is_moved()
        ' straddling the seam: the body reaches 30 px into the requesting monitor (less than the 40 px that can be grabbed)
        Dim straddling As New Rectangle(1230, 100, 800, 700)
        Dim area As New Rectangle(2000, 0, 1920, 1040)
        Dim moved As Rectangle = ManagerPlacement.ForRequestingMonitor(straddling, area)
        Assert.True(area.Contains(moved), moved.ToString())
    End Sub

    <Fact>
    Public Sub A_window_larger_than_the_requesting_work_area_is_shrunk_to_fit()
        ' title bar on no display at all, bigger than the area it is brought to
        Dim big As New Rectangle(-3000, 0, 3000, 2000)
        Dim moved As Rectangle = ManagerPlacement.ForRequestingMonitor(big, RightScreen)
        Assert.Equal(RightScreen, moved)
    End Sub

    <Fact>
    Public Sub An_unknown_work_area_changes_nothing()
        Dim w As New Rectangle(10, 10, 800, 600)
        Assert.Equal(w, ManagerPlacement.ForRequestingMonitor(w, Rectangle.Empty))
    End Sub

    <Fact>
    Public Sub The_Manager_asks_the_helper_in_both_places_and_keeps_no_second_screen_test()
        Dim layout As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\MainWindow.Layout.vb"))
        Assert.Contains("ManagerPlacement.RestoreRectangle(", layout, StringComparison.Ordinal)
        Assert.DoesNotContain("IntersectsAnyScreen", layout, StringComparison.Ordinal)
        Dim settings As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\MainWindow.Settings.vb"))
        Assert.Contains("ManagerPlacement.ForRequestingMonitor(", settings, StringComparison.Ordinal)
        Assert.Contains("If reused Then BringToRequestingMonitor(window)", settings, StringComparison.Ordinal)
    End Sub

End Class
