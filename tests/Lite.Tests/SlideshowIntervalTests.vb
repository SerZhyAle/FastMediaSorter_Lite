Option Strict On

Imports Xunit

''' <summary>
''' The slideshow interval rule of MEDIA-VIEWER 1.0 (specification 252 T-3): the first press starts at the
''' configured interval, every further press while the slideshow runs halves the interval it has now, and the
''' 1 s floor holds. Shared by both exes (Utils is compiled into both), so the net48 leg runs it too.
''' </summary>
Public Class SlideshowIntervalTests

    Private Const Floor As Integer = 1000

    <Fact>
    Public Sub The_first_press_starts_at_the_configured_interval_whatever_the_timer_holds()
        Assert.Equal(10000, Utils.SlideshowIntervalAfterPress(10000, 100, False, Floor))
        Assert.Equal(10000, Utils.SlideshowIntervalAfterPress(10000, 10000, False, Floor))
        Assert.Equal(1000, Utils.SlideshowIntervalAfterPress(1000, 1000, False, Floor))
    End Sub

    <Fact>
    Public Sub Each_press_while_it_runs_halves_the_current_interval()
        Assert.Equal(5000, Utils.SlideshowIntervalAfterPress(10000, 10000, True, Floor))
        Assert.Equal(2500, Utils.SlideshowIntervalAfterPress(10000, 5000, True, Floor))
        Assert.Equal(1250, Utils.SlideshowIntervalAfterPress(10000, 2500, True, Floor))
    End Sub

    <Fact>
    Public Sub The_interval_never_goes_below_the_one_second_floor()
        Assert.Equal(1000, Utils.SlideshowIntervalAfterPress(10000, 1250, True, Floor))
        Assert.Equal(1000, Utils.SlideshowIntervalAfterPress(10000, 1000, True, Floor))
        Assert.Equal(1000, Utils.SlideshowIntervalAfterPress(1000, 1000, True, Floor))
        Assert.Equal(1000, Utils.SlideshowIntervalAfterPress(10000, 1, True, Floor))
    End Sub

    <Fact>
    Public Sub Repeated_presses_from_the_default_settle_on_the_floor_and_stay_there()
        Dim current As Integer = Utils.SlideshowIntervalAfterPress(10000, 0, False, Floor)
        Assert.Equal(10000, current)
        Dim seen As New System.Collections.Generic.List(Of Integer)()
        For press As Integer = 1 To 8
            current = Utils.SlideshowIntervalAfterPress(10000, current, True, Floor)
            seen.Add(current)
        Next
        Assert.Equal(New Integer() {5000, 2500, 1250, 1000, 1000, 1000, 1000, 1000}, seen.ToArray())
    End Sub

#If Not NETFRAMEWORK Then
    <Fact>
    Public Sub Both_slideshow_entry_points_use_the_one_rule()
        Dim source As String = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "src", "Main_Form.Slideshow.vb"))
        Dim calls As Integer = System.Text.RegularExpressions.Regex.Matches(source, "SlideshowIntervalAfterPress\(").Count
        Assert.Equal(2, calls)
        ' The inline halving is gone: nothing in the partial divides the timer interval itself any more.
        Assert.DoesNotContain("SlideShowTimer.Interval / 2", source)
    End Sub

    Private Shared Function RepoRoot() As String
        Dim dir As New System.IO.DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("repository root not found above " & AppContext.BaseDirectory)
    End Function
#End If

End Class
