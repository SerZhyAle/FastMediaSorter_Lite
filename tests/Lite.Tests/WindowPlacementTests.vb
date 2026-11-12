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

    ' --- remembered size vs. DPI (124 UX-202) -------------------------------------------------------

    <Fact>
    Public Sub A_size_measured_at_the_same_dpi_is_applied_as_it_is()
        Assert.Equal(New Size(1100, 800), WindowPlacement.SizeForDpi(1100, 800, 120, 120))
    End Sub

    <Fact>
    Public Sub A_size_with_no_recorded_dpi_is_applied_as_it_is()
        ' Written by a build before the rule: it is device pixels at the DPI the window has now, so the first run
        ' after the update restores exactly what the user left - nothing grows, nothing shrinks.
        Assert.Equal(New Size(1100, 800), WindowPlacement.SizeForDpi(1100, 800, 0, 168))
        Assert.Equal(New Size(1100, 800), WindowPlacement.SizeForDpi(1100, 800, 168, 0))
    End Sub

    <Fact>
    Public Sub A_size_keeps_its_logical_size_across_displays()
        Dim at168 As Size = WindowPlacement.SizeForDpi(1100, 800, 120, 168)
        Assert.Equal(New Size(1540, 1120), at168)
        Assert.Equal(New Size(880, 640), WindowPlacement.SizeForDpi(at168.Width, at168.Height, 168, 96))
    End Sub

    ''' <summary>The bug the rule exists for: apply what was saved, let the window scale, save what it ended
    ''' up as, repeat. The size must not move - at any DPI, and across a monitor with another one.</summary>
    <Fact>
    Public Sub Reopening_the_window_session_after_session_never_changes_its_size()
        Dim width As Integer = 1100
        Dim height As Integer = 800
        Dim savedDpi As Integer = 120
        For Each dpi As Integer In New Integer() {120, 120, 120, 168, 168, 96, 120, 192, 120}
            Dim applied As Size = WindowPlacement.SizeForDpi(width, height, savedDpi, dpi)
            width = applied.Width
            height = applied.Height
            savedDpi = dpi
        Next
        ' Back on the 120-dpi display it started on: the same window, give or take the rounding of the trips.
        Assert.InRange(width, 1099, 1101)
        Assert.InRange(height, 799, 801)
    End Sub

    <Fact>
    Public Sub A_size_is_never_scaled_to_nothing()
        Assert.Equal(New Size(1, 1), WindowPlacement.SizeForDpi(1, 1, 960, 72))
    End Sub

    <Theory>
    <InlineData(Nothing, 0)>
    <InlineData("", 0)>
    <InlineData("abc", 0)>
    <InlineData("0", 0)>
    <InlineData("10", 0)>
    <InlineData("5000", 0)>
    <InlineData("120", 120)>
    <InlineData(" 168 ", 168)>
    Public Sub A_remembered_dpi_is_read_back_or_is_not_recorded(text As String, expected As Integer)
        Assert.Equal(expected, WindowPlacement.ParseDpi(text))
    End Sub

    ' --- a minimum that grows with the scale never passes the screen (279, T-2) -------------------------

    ''' <summary>The Settings window asks for 780 x 400 logical px times its composed scale (display x interface
    ''' scale x Windows Text size). On a 1920 px wide screen at 150 percent with Text size 200 percent that is
    ''' 2340 px: the bound brings it back to the work area, and a minimum that fits is left as it is.</summary>
    <Theory>
    <InlineData(780, 400, 1920, 1040, 780, 400)>
    <InlineData(1170, 600, 1920, 1040, 1170, 600)>
    <InlineData(2340, 1200, 1920, 1040, 1920, 1040)>
    <InlineData(1755, 900, 1366, 728, 1366, 728)>
    <InlineData(2632, 700, 1920, 1040, 1920, 700)>
    <InlineData(700, 1300, 1920, 1040, 700, 1040)>
    <InlineData(1920, 1040, 1920, 1040, 1920, 1040)>
    Public Sub A_minimum_size_never_exceeds_the_work_area_it_opens_on(wantedW As Integer, wantedH As Integer,
                                                                      areaW As Integer, areaH As Integer,
                                                                      expectedW As Integer, expectedH As Integer)
        Dim bounded As Size = WindowPlacement.BoundMinimum(New Size(wantedW, wantedH), New Rectangle(0, 0, areaW, areaH))
        Assert.Equal(New Size(expectedW, expectedH), bounded)
        ' The area may sit anywhere (a second monitor left of the primary): only its size counts.
        Assert.Equal(bounded, WindowPlacement.BoundMinimum(New Size(wantedW, wantedH), New Rectangle(-areaW, 0, areaW, areaH)))
    End Sub

    <Fact>
    Public Sub An_unknown_work_area_sets_no_bound_and_a_negative_minimum_is_none()
        Assert.Equal(New Size(2340, 1200), WindowPlacement.BoundMinimum(New Size(2340, 1200), Rectangle.Empty))
        Assert.Equal(New Size(2340, 1040), WindowPlacement.BoundMinimum(New Size(2340, 1200), New Rectangle(0, 0, 0, 1040)))
        Assert.Equal(New Size(0, 0), WindowPlacement.BoundMinimum(New Size(-5, -9), New Rectangle(0, 0, 1920, 1040)))
    End Sub

    ''' <summary>Every composed scale the Windows Text size can reach (1.0 to 2.25) on the common displays: the
    ''' bounded minimum is inside the work area, which is the whole point of the rule.</summary>
    <Fact>
    Public Sub At_every_display_and_text_size_the_settings_minimum_fits_the_work_area()
        Dim areas As Rectangle() = {New Rectangle(0, 0, 1366, 728), New Rectangle(0, 0, 1920, 1040), New Rectangle(0, 0, 2560, 1400), New Rectangle(0, 0, 3840, 2120)}
        For Each area As Rectangle In areas
            For Each display As Single In New Single() {1.0F, 1.25F, 1.5F, 2.0F}
                For Each interfaceScale As Single In New Single() {1.0F, 1.5F}
                    For Each textSize As Single In New Single() {1.0F, 1.5F, 2.0F, UiSizing.MaxTextFactor}
                        Dim composed As Single = display * interfaceScale * textSize
                        Dim wanted As New Size(CInt(Math.Round(780 * composed)), CInt(Math.Round(400 * composed)))
                        Dim bounded As Size = WindowPlacement.BoundMinimum(wanted, area)
                        Assert.True(bounded.Width <= area.Width AndAlso bounded.Height <= area.Height,
                                    String.Format("{0}x{1}: display {2} x interface {3} x text {4} still asks {5}x{6}", area.Width, area.Height, display, interfaceScale, textSize, bounded.Width, bounded.Height))
                        Assert.True(bounded.Width <= wanted.Width AndAlso bounded.Height <= wanted.Height, "the bound only ever lowers a minimum")
                    Next
                Next
            Next
        Next
    End Sub

    ''' <summary>The Settings window keeps what it asks for in one field and bounds the live minimum by the work area
    ''' BEFORE it reads that minimum to clamp the client size (a clamp of the size alone cannot make a form smaller
    ''' than its MinimumSize).</summary>
    <Fact>
    Public Sub The_settings_window_bounds_its_minimum_by_the_work_area_before_it_clamps_its_size()
        Dim layout As String = Source("Table_Form.ModernLayout.vb")
        Assert.Contains("settingsDesiredMinimum = New Size(Sp(780), Sp(400))", layout)
        Assert.DoesNotContain("MinimumSize = New Size(Sp(780), Sp(400))", layout)
        Dim fit As String = BodyOf(layout, "Private Sub FitModernSettingsClientSize(")
        Dim bound As Integer = fit.IndexOf("MinimumSize = WindowPlacement.BoundMinimum(settingsDesiredMinimum, workArea)", StringComparison.Ordinal)
        Dim reads As Integer = fit.IndexOf("Dim minClientWidth As Integer = Math.Max(1, MinimumSize.Width - chromeWidth)", StringComparison.Ordinal)
        Assert.True(bound >= 0, "FitModernSettingsClientSize no longer bounds the minimum by the work area")
        Assert.True(reads > bound, "the minimum is read for the clamp before it is bounded")
    End Sub

    ' --- the order the two windows apply it in -------------------------------------------------------

    Private Shared Function Source(fileName As String) As String
        Dim dir As New IO.DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If IO.File.Exists(IO.Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then
                Return IO.File.ReadAllText(IO.Path.Combine(dir.FullName, "src", fileName))
            End If
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("repository root not found above " & AppContext.BaseDirectory)
    End Function

    ''' <summary>The body of the method that starts at <paramref name="signature"/>, up to its End Sub.</summary>
    Private Shared Function BodyOf(source As String, signature As String) As String
        Dim at As Integer = source.IndexOf(signature, StringComparison.Ordinal)
        Assert.True(at >= 0, signature & " is gone")
        Dim [end] As Integer = source.IndexOf("End Sub", at, StringComparison.Ordinal)
        Assert.True([end] > at, signature & " has no End Sub")
        Return source.Substring(at, [end] - at)
    End Function

    ''' <summary>A window that scales itself multiplies whatever size it has when it first lays out, so the
    ''' remembered size must go on AFTER the scaling - and the two windows say so in the same two words.</summary>
    <Theory>
    <InlineData("Image_Editor_Form.vb")>
    <InlineData("Image_Panel_Form.Modern.vb")>
    Public Sub The_remembered_size_is_applied_after_the_window_has_scaled_itself(fileName As String)
        Dim onLoad As String = BodyOf(Source(fileName), "Protected Overrides Sub OnLoad")
        Dim scaled As Integer = onLoad.IndexOf("PerformAutoScale()", StringComparison.Ordinal)
        Dim applied As Integer = onLoad.IndexOf("ApplyRememberedBounds()", StringComparison.Ordinal)
        Assert.True(scaled >= 0, fileName & ": OnLoad does not scale the window before using its size")
        Assert.True(applied > scaled, fileName & ": OnLoad applies the remembered size before the window has scaled itself")
    End Sub

    <Theory>
    <InlineData("Image_Editor_Form.vb", "EditorDpi")>
    <InlineData("Image_Panel_Form.Modern.vb", "PicturePanelDpi")>
    Public Sub The_dpi_a_size_was_measured_at_is_written_with_it_and_read_back(fileName As String, key As String)
        Dim text As String = Source(fileName)
        Assert.Contains("""" & key & """", text)
        Assert.Contains("WindowPlacement.ParseDpi(", text)
        Assert.Contains("WindowPlacement.SizeForDpi(", text)
    End Sub

    <Fact>
    Public Sub Both_windows_turn_scaling_on_as_the_last_line_of_their_builder()
        Dim editor As String = Source("Image_Editor_Form.vb")
        Assert.True(editor.IndexOf("DialogLayout.ApplyAutoScale(Me)", StringComparison.Ordinal) >
                    editor.IndexOf("Me.Controls.Add(BuildToolBar())", StringComparison.Ordinal),
                    "the editor scales before its last band exists")
        Dim panel As String = Source("Image_Panel_Form.Modern.vb")
        Assert.True(panel.IndexOf("DialogLayout.ApplyAutoScale(Me)", StringComparison.Ordinal) >
                    panel.IndexOf("Controls.Add(toolbar)", StringComparison.Ordinal),
                    "the panel scales before its last band exists")
    End Sub

    ''' <summary>Ticket 279, T-2: two Settings-window pieces that kept a fixed text size at a larger Windows Text size.
    ''' The consent window of "send the logs" scales after its own fonts exist (the grown ones are exactly the controls'
    ''' own), and the recipient grid's header font takes the window's font scale like every other font of the window.</summary>
    <Fact>
    Public Sub The_send_logs_consent_window_scales_after_its_fonts_and_the_grid_header_takes_the_font_scale()
        Dim logs As String = Source("Table_Form.SendLogs.vb")
        Dim fonts As Integer = logs.IndexOf("ApplySendLogsScriptFont(dialog, header, ownedFonts)", StringComparison.Ordinal)
        Dim scaled As Integer = logs.IndexOf("DialogLayout.ApplyAutoScale(dialog)", StringComparison.Ordinal)
        Dim shown As Integer = logs.IndexOf("DialogLayer.ShowViewerDialog(dialog, Me)", StringComparison.Ordinal)
        Assert.True(fonts >= 0, "the consent window no longer sets its script font where this test looks")
        Assert.True(scaled > fonts, "the consent window scales before its own fonts exist, so the Text size would not reach them")
        Assert.True(shown > scaled, "the consent window is shown before it is scaled")

        Dim layout As String = Source("Table_Form.ModernLayout.vb")
        Assert.Contains("ColumnHeadersDefaultCellStyle.Font = SettingsFont(""Segoe UI Semibold"", Sf(9.0F)", layout)
    End Sub

End Class
#End If
