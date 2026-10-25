Option Strict On

Imports System.IO
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' MEDIA-VIEWER 1.0 rule 5, the three behaviours specification 252 T-3 owed beyond the
''' interval rule of SlideshowIntervalTests: the final 3-2-1 countdown, the navigation
''' action set that continues a running show, and the screen keep-awake. The pure halves
''' run on both legs; the form wiring is pinned by reading the form sources, because the
''' forms themselves are not compiled into this suite.
''' </summary>
Public Class MediaViewerRule5Tests

    <Theory>
    <InlineData(10000, 10000, False, "10s")>
    <InlineData(10000, 3001, False, "10s")>
    <InlineData(10000, 3000, False, "3s")>
    <InlineData(10000, 2500, False, "3s")>
    <InlineData(10000, 2000, False, "2s")>
    <InlineData(10000, 1001, False, "2s")>
    <InlineData(10000, 1000, False, "1s")>
    <InlineData(10000, 1, False, "1s")>
    <InlineData(10000, 0, False, "10s")>
    <InlineData(10000, -50, False, "10s")>
    <InlineData(3000, 3000, False, "3s")>
    <InlineData(3000, 1000, False, "3s")>
    <InlineData(2500, 900, False, "2s")>
    <InlineData(1000, 1000, False, "1s")>
    <InlineData(10000, 2000, True, "10s")>
    Public Sub The_final_countdown_runs_only_in_the_last_three_seconds_of_an_interval_over_three_seconds(
            intervalMs As Integer, remainingMs As Integer, held As Boolean, expected As String)
        Assert.Equal(expected, Utils.SlideshowCountdownText(intervalMs, remainingMs, held))
    End Sub

#If Not NETFRAMEWORK Then

    ' The navigation-action set lives in CustomHotkeys, which compiles only on the net10
    ' leg (the whole file is "#If Not NETFRAMEWORK"), so its tests do too.

    <Theory>
    <InlineData("nextFile")>
    <InlineData("prevFile")>
    <InlineData("randomFile")>
    <InlineData("back10")>
    <InlineData("forward10")>
    <InlineData("back100")>
    <InlineData("forward100")>
    <InlineData("firstFile")>
    <InlineData("lastFile")>
    <InlineData("jumpToNumber")>
    Public Sub The_ten_manual_navigation_actions_are_navigation(actionId As String)
        Assert.True(CustomHotkeys.IsNavigationAction(actionId))
    End Sub

    <Theory>
    <InlineData("slideshow")>
    <InlineData("randomSlideshow")>
    <InlineData("chooseFile")>
    <InlineData("rotateCw")>
    <InlineData("deleteFile")>
    <InlineData("playPause")>
    <InlineData("zoomIn")>
    <InlineData("")>
    Public Sub Everything_else_is_not_navigation(actionId As String)
        Assert.False(CustomHotkeys.IsNavigationAction(actionId))
    End Sub

    <Theory>
    <InlineData(Keys.Right, "nextFile")>
    <InlineData(Keys.Space, "nextFile")>
    <InlineData(Keys.PageDown, "nextFile")>
    <InlineData(Keys.Left, "prevFile")>
    <InlineData(Keys.PageUp, "prevFile")>
    <InlineData(Keys.Y, "randomFile")>
    <InlineData(Keys.Up, "back10")>
    <InlineData(Keys.Down, "forward10")>
    <InlineData(Keys.Home, "firstFile")>
    <InlineData(Keys.End, "lastFile")>
    <InlineData(Keys.N, "jumpToNumber")>
    Public Sub Factory_keys_of_navigation_actions_resolve(key As Keys, expected As String)
        Assert.Equal(expected, CustomHotkeys.NavigationActionOfFactoryKey(key))
    End Sub

    <Fact>
    Public Sub Factory_chords_of_the_relative_jumps_resolve_their_actions()
        ' Attribute arguments cannot combine enum flags, so the chords are built here.
        Assert.Equal("back100", CustomHotkeys.NavigationActionOfFactoryKey(Keys.Shift Or Keys.PageUp))
        Assert.Equal("forward100", CustomHotkeys.NavigationActionOfFactoryKey(Keys.Shift Or Keys.PageDown))
        Assert.Equal("firstFile", CustomHotkeys.NavigationActionOfFactoryKey(Keys.Control Or Keys.Home))
        Assert.Equal("lastFile", CustomHotkeys.NavigationActionOfFactoryKey(Keys.Control Or Keys.End))
    End Sub

    <Theory>
    <InlineData(Keys.R)>
    <InlineData(Keys.S)>
    <InlineData(Keys.F2)>
    <InlineData(Keys.D1)>
    <InlineData(Keys.Delete)>
    <InlineData(Keys.OemPeriod)>
    Public Sub Keys_of_other_actions_do_not_resolve_as_navigation(key As Keys)
        Assert.Equal(String.Empty, CustomHotkeys.NavigationActionOfFactoryKey(key))
    End Sub

    <Fact>
    Public Sub The_keep_awake_holder_starts_released_and_only_a_show_turns_it_on()
        Assert.False(DisplayKeepAwake.IsActive)
        DisplayKeepAwake.SetActive(True)
        Assert.True(DisplayKeepAwake.IsActive)
        ' Idempotent: repeating the state changes nothing.
        DisplayKeepAwake.SetActive(True)
        Assert.True(DisplayKeepAwake.IsActive)
        DisplayKeepAwake.Release()
        Assert.False(DisplayKeepAwake.IsActive)
        DisplayKeepAwake.Release()
        Assert.False(DisplayKeepAwake.IsActive)
    End Sub

    <Fact>
    Public Sub The_show_start_and_stop_hold_and_release_the_screen()
        Dim source As String = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Main_Form.Slideshow.vb"))
        Assert.Contains("DisplayKeepAwake.SetActive(True)", source)
        Assert.Contains("DisplayKeepAwake.Release()", source)
        ' The countdown is a display-only ticker fed by the one pure rule, and the flip
        ' hold (video to the end, a known-length audio track) is the same rule the flip
        ' timer itself uses.
        Assert.Contains("Utils.SlideshowCountdownText(", source)
        Assert.Contains("If SlideshowFlipHeld() Then Return", source)
    End Sub

    ''' <summary>
    ''' The form wiring of rule 5: the keyboard, custom-hotkey, random-button and wheel
    ''' paths decide navigation through ManualNavigationStopsShow, the net48 leg keeps the
    ''' historical stop, and the declared preference defaults to CONTINUE (False).
    ''' </summary>
    <Fact>
    Public Sub Every_manual_navigation_path_goes_through_the_preference_aware_helper()
        Dim root As String = RepoRoot()
        Dim keyboard As String = File.ReadAllText(Path.Combine(root, "src", "Main_Form.KeyboardInput.vb"))
        Assert.Contains("CustomHotkeys.NavigationActionOfFactoryKey(e.KeyData)", keyboard)
        Assert.Contains("ManualNavigationStopsShow()", keyboard)

        Dim hotkeys As String = File.ReadAllText(Path.Combine(root, "src", "Main_Form.CustomHotkeys.vb"))
        Assert.Contains("CustomHotkeys.IsNavigationAction(actionId)", hotkeys)
        Assert.Contains("ManualNavigationStopsShow()", hotkeys)

        Dim mouse As String = File.ReadAllText(Path.Combine(root, "src", "Main_Form.MouseInput.vb"))
        Assert.Contains("ManualNavigationStopsShow()", mouse)

        Dim slideshow As String = File.ReadAllText(Path.Combine(root, "src", "Main_Form.Slideshow.vb"))
        ' The random toolbar button is navigation too.
        Assert.Contains("ManualNavigationStopsShow()", slideshow)
        ' The contract default: continue. The preference names the stop, not the continue.
        Dim preferences As String = File.ReadAllText(Path.Combine(root, "src", "ModernViewerPreferences.vb"))
        Assert.Contains("StopSlideshowOnManualNavigation As Boolean = False", preferences)
    End Sub

    Private Shared Function RepoRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("repository root not found above " & AppContext.BaseDirectory)
    End Function

#End If

End Class
