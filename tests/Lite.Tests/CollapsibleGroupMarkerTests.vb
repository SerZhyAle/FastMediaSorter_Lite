#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.IO
Imports System.Reflection
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' COLLAPSIBLE-GROUP 1.1 section 3, rules 2 and 6, as the viewer's settings window holds them: the header
''' carries a FILLED STATE TRIANGLE at the reading start - right when the group is collapsed, down when it
''' is open, left when it is collapsed under RTL - in the header's own ink, on glyph tier 16 scaled with the
''' window, and the whole header (not the triangle alone) is still the one toggle.
''' The triangle is a vector painted by the header, so the checks render the real header
''' (<see cref="SettingsCollapsibleGroup"/>) to a bitmap and read the pixels; what stays manual is the look on
''' a physical 150/200 percent monitor and under a live high-contrast switch.
''' </summary>
Public Class CollapsibleGroupMarkerTests

    Private Const GroupWidth As Integer = 360

    ''' <summary>Pixels at least this dark (0..255, black ink on white) count as ink; low enough to keep the tip of the
    ''' triangle, whose last column is covered by a few per cent.</summary>
    Private Const InkThreshold As Integer = 48

    Private Shared Sub OnSta(body As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(
            Sub()
                Try
                    body()
                Catch ex As Exception
                    failure = ex
                End Try
            End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The marker check did not finish.")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    ''' <summary>A header at <paramref name="scale"/> (the window's Sp scale), black ink on white, laid out once.</summary>
    Private Shared Sub WithGroup(scale As Single, caption As String, expanded As Boolean, rtl As Boolean,
                                 body As Action(Of SettingsCollapsibleGroup))
        OnSta(
            Sub()
                Using font As New Font("Segoe UI Semibold", 10.5F * scale, FontStyle.Regular)
                    Using group As New SettingsCollapsibleGroup("test/marker", scale, font)
                        group.Width = GroupWidth
                        group.Header.ForeColor = Color.Black
                        group.Header.BackColor = Color.White
                        If rtl Then group.RightToLeft = RightToLeft.Yes
                        group.Caption = caption
                        group.Expanded = expanded
                        group.ArrangeContents()
                        body(group)
                    End Using
                End Using
            End Sub)
    End Sub

    Private Shared Function Render(group As SettingsCollapsibleGroup) As Bitmap
        Dim bmp As New Bitmap(group.Header.Width, group.Header.Height)
        group.Header.DrawToBitmap(bmp, New Rectangle(0, 0, bmp.Width, bmp.Height))
        Return bmp
    End Function

    Private Shared Function Darkness(c As Color) As Integer
        Return 255 - (CInt(c.R) + CInt(c.G) + CInt(c.B)) \ 3
    End Function

    ''' <summary>The bounding box of the ink inside <paramref name="area"/>; <see cref="Rectangle.Empty"/> when there is none.</summary>
    Private Shared Function InkBox(bmp As Bitmap, area As Rectangle) As Rectangle
        Dim minX As Integer = Integer.MaxValue
        Dim minY As Integer = Integer.MaxValue
        Dim maxX As Integer = -1
        Dim maxY As Integer = -1
        area.Intersect(New Rectangle(0, 0, bmp.Width, bmp.Height))
        For y As Integer = area.Top To area.Bottom - 1
            For x As Integer = area.Left To area.Right - 1
                If Darkness(bmp.GetPixel(x, y)) >= InkThreshold Then
                    If x < minX Then minX = x
                    If x > maxX Then maxX = x
                    If y < minY Then minY = y
                    If y > maxY Then maxY = y
                End If
            Next
        Next
        If maxX < 0 Then Return Rectangle.Empty
        Return New Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)
    End Function

    ''' <summary>The darkness of every pixel of <paramref name="cell"/>, [x, y].</summary>
    Private Shared Function Crop(bmp As Bitmap, cell As Rectangle) As Integer(,)
        Dim result(cell.Width - 1, cell.Height - 1) As Integer
        For y As Integer = 0 To cell.Height - 1
            For x As Integer = 0 To cell.Width - 1
                result(x, y) = Darkness(bmp.GetPixel(cell.X + x, cell.Y + y))
            Next
        Next
        Return result
    End Function

    Private Shared Function InkInColumn(mask As Integer(,), x As Integer) As Integer
        Dim total As Integer = 0
        For y As Integer = 0 To mask.GetLength(1) - 1
            total += mask(x, y)
        Next
        Return total
    End Function

    Private Shared Function InkInRow(mask As Integer(,), y As Integer) As Integer
        Dim total As Integer = 0
        For x As Integer = 0 To mask.GetLength(0) - 1
            total += mask(x, y)
        Next
        Return total
    End Function

    ''' <summary>Pixels whose darkness differs by more than 64 between two crops of the same size.</summary>
    Private Shared Function Differing(a As Integer(,), b As Integer(,), mirrorB As Boolean) As Integer
        Dim count As Integer = 0
        Dim w As Integer = a.GetLength(0)
        For y As Integer = 0 To a.GetLength(1) - 1
            For x As Integer = 0 To w - 1
                Dim other As Integer = If(mirrorB, b(w - 1 - x, y), b(x, y))
                If Math.Abs(a(x, y) - other) > 64 Then count += 1
            Next
        Next
        Return count
    End Function

    ' ------------------------------------------------------------------ the three states ----

    <Fact>
    Public Sub The_triangle_points_right_when_collapsed_down_when_open_and_left_when_collapsed_under_rtl()
        Assert.Equal(SettingsCollapsibleGroup.MarkerDirection.Right, SettingsCollapsibleGroup.DirectionFor(expanded:=False, rightToLeft:=False))
        Assert.Equal(SettingsCollapsibleGroup.MarkerDirection.Down, SettingsCollapsibleGroup.DirectionFor(expanded:=True, rightToLeft:=False))
        Assert.Equal(SettingsCollapsibleGroup.MarkerDirection.Left, SettingsCollapsibleGroup.DirectionFor(expanded:=False, rightToLeft:=True))
        ' Open stays down in RTL (section 3 rule 2).
        Assert.Equal(SettingsCollapsibleGroup.MarkerDirection.Down, SettingsCollapsibleGroup.DirectionFor(expanded:=True, rightToLeft:=True))
    End Sub

    ''' <summary>The pixels of the cell of one state: where the ink is heavy tells where the base is, so which way it points.</summary>
    Private Shared Function StateMask(expanded As Boolean, rtl As Boolean) As Integer(,)
        Dim mask As Integer(,) = Nothing
        WithGroup(1.0F, String.Empty, expanded, rtl,
            Sub(group)
                Using bmp As Bitmap = Render(group)
                    mask = Crop(bmp, group.MarkerBounds())
                End Using
            End Sub)
        Return mask
    End Function

    <Fact>
    Public Sub The_three_states_are_three_different_triangles_pointing_the_stated_way()
        Dim right As Integer(,) = StateMask(expanded:=False, rtl:=False)
        Dim down As Integer(,) = StateMask(expanded:=True, rtl:=False)
        Dim left As Integer(,) = StateMask(expanded:=False, rtl:=True)
        Dim last As Integer = right.GetLength(0) - 1

        ' Collapsed, LTR: the base (heavy ink) is on the left of the cell, the tip on the right.
        Assert.True(InkInColumn(right, 4) > 3 * InkInColumn(right, 11), "collapsed LTR does not point right")
        ' Collapsed, RTL: the mirror - the base on the right, the tip on the left.
        Assert.True(InkInColumn(left, last - 4) > 3 * InkInColumn(left, last - 11), "collapsed RTL does not point left")
        ' Open: the base is the top row, the tip points down.
        Assert.True(InkInRow(down, 4) > 3 * InkInRow(down, 11), "open does not point down")

        ' Three different masks, and left is exactly the mirror image of right.
        Assert.True(Differing(right, down, mirrorB:=False) > 20, "right and down look alike")
        Assert.True(Differing(right, left, mirrorB:=False) > 20, "right and left look alike")
        Assert.True(Differing(down, left, mirrorB:=False) > 20, "down and left look alike")
        Assert.True(Differing(left, right, mirrorB:=True) <= 2,
                    "the RTL triangle is not the mirror of the LTR one (" & Differing(left, right, mirrorB:=True) & " pixels differ)")
    End Sub

    ' ------------------------------------------------------------------ the reading start ----

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub The_triangle_stands_at_the_reading_start_and_the_caption_follows_it(rtl As Boolean)
        ' Marker alone: where its ink is.
        Dim markerInk As Rectangle = Rectangle.Empty
        Dim cell As Rectangle = Rectangle.Empty
        Dim headerWidth As Integer = 0
        Dim headerHeight As Integer = 0
        WithGroup(1.0F, String.Empty, expanded:=False, rtl:=rtl,
            Sub(group)
                Using bmp As Bitmap = Render(group)
                    markerInk = InkBox(bmp, New Rectangle(0, 0, bmp.Width, bmp.Height))
                    cell = group.MarkerBounds()
                    headerWidth = bmp.Width
                    headerHeight = bmp.Height
                End Using
            End Sub)
        Assert.False(markerInk.IsEmpty, "no marker is painted")
        Assert.True(cell.Contains(markerInk), "the ink " & markerInk.ToString() & " leaves its tier cell " & cell.ToString())
        If rtl Then
            Assert.True(markerInk.Left > headerWidth * 7 \ 8, "the RTL marker is not at the right edge: " & markerInk.ToString())
        Else
            Assert.True(markerInk.Right < headerWidth \ 8, "the LTR marker is not at the left edge: " & markerInk.ToString())
        End If
        ' Centred on the single caption line (a 32 band).
        Assert.True(Math.Abs(cell.Top + cell.Height \ 2 - headerHeight \ 2) <= 2, "the marker is not centred: " & cell.ToString())

        ' With a caption: the caption's ink lies on the end side of the marker, at least the 8 gap away.
        Dim captionInk As Rectangle = Rectangle.Empty
        WithGroup(1.0F, "Group", expanded:=False, rtl:=rtl,
            Sub(group)
                Using bmp As Bitmap = Render(group)
                    Using g As Graphics = Graphics.FromImage(bmp)
                        g.FillRectangle(Brushes.White, Rectangle.Inflate(group.MarkerBounds(), 2, 2))
                    End Using
                    captionInk = InkBox(bmp, New Rectangle(0, 0, bmp.Width, bmp.Height))
                End Using
            End Sub)
        Assert.False(captionInk.IsEmpty, "no caption is painted")
        If rtl Then
            Assert.True(captionInk.Right <= cell.Left - SettingsCollapsibleGroup.MarkerGapLogical,
                        "the caption " & captionInk.ToString() & " is not at the end side of the marker " & cell.ToString())
            Assert.True(captionInk.Right > cell.Left - SettingsCollapsibleGroup.MarkerGapLogical - 12, "the RTL caption is not right-aligned: " & captionInk.ToString())
        Else
            Assert.True(captionInk.Left >= cell.Right + SettingsCollapsibleGroup.MarkerGapLogical,
                        "the caption " & captionInk.ToString() & " is not at the end side of the marker " & cell.ToString())
            Assert.True(captionInk.Left < cell.Right + SettingsCollapsibleGroup.MarkerGapLogical + 12, "the LTR caption is not left-aligned: " & captionInk.ToString())
        End If
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub A_collapsed_summary_line_and_a_wrapped_caption_keep_the_marker_on_the_first_line(rtl As Boolean)
        ' The painted summary line (127 section 3.4) lives under the caption: the marker stays on the caption's
        ' line, and the summary starts where the caption starts - at the reading start, so on the right under RTL.
        WithGroup(1.0F, "Display settings", expanded:=False, rtl:=rtl,
            Sub(group)
                Using summaryFont As New Font("Segoe UI", 8.0F)
                    group.SetSummaryStyle(summaryFont, Color.Black)
                    group.SetSummary("3 options: a, b, c")
                    Dim cell As Rectangle = group.MarkerBounds()
                    Using bmp As Bitmap = Render(group)
                        Dim split As Integer = bmp.Height * 55 \ 100
                        Dim endSide As Integer = If(rtl, 0, cell.Right)
                        Dim endWidth As Integer = If(rtl, cell.Left, bmp.Width - cell.Right)
                        Dim caption As Rectangle = InkBox(bmp, New Rectangle(endSide, 0, endWidth, split))
                        Dim summary As Rectangle = InkBox(bmp, New Rectangle(endSide, split, endWidth, bmp.Height - split))
                        Dim markerInk As Rectangle = InkBox(bmp, cell)
                        Assert.False(caption.IsEmpty, "no caption")
                        Assert.False(summary.IsEmpty, "no summary line")
                        Assert.True(summary.Top > caption.Bottom - 4, "the summary is not under the caption")
                        Assert.True(Math.Abs((markerInk.Top + markerInk.Bottom) \ 2 - (caption.Top + caption.Bottom) \ 2) <= 3,
                                    "the marker " & markerInk.ToString() & " is off the caption line " & caption.ToString())
                        If rtl Then
                            Assert.True(Math.Abs(summary.Right - caption.Right) <= 4, "the summary does not end where the caption ends: " & summary.ToString() & " / " & caption.ToString())
                        Else
                            Assert.True(Math.Abs(summary.Left - caption.Left) <= 4, "the summary does not start where the caption starts: " & summary.ToString() & " / " & caption.ToString())
                        End If
                    End Using
                End Using
            End Sub)

        ' A caption that wraps grows the header; the marker stays on its first line.
        WithGroup(1.0F, "Display settings that wrap onto the second line of the band for sure and even more words here to wrap",
                  expanded:=False, rtl:=rtl,
            Sub(group)
                Dim cell As Rectangle = group.MarkerBounds()
                Using bmp As Bitmap = Render(group)
                    Dim endSide As Integer = If(rtl, 0, cell.Right)
                    Dim endWidth As Integer = If(rtl, cell.Left, bmp.Width - cell.Right)
                    Dim caption As Rectangle = InkBox(bmp, New Rectangle(endSide, 0, endWidth, bmp.Height))
                    Dim centre As Integer = cell.Top + cell.Height \ 2
                    Assert.True(group.Header.Height > 40, "the header did not grow for a wrapped caption: " & group.Header.Height)
                    Assert.True(centre > caption.Top AndAlso centre < caption.Top + group.Header.Font.Height,
                                "the marker (centre " & centre & ") is not on the first caption line " & caption.ToString())
                End Using
            End Sub)
    End Sub

    ' ------------------------------------------------------------------ the tier ----

    <Theory>
    <InlineData(1.0F)>
    <InlineData(1.25F)>
    <InlineData(1.5F)>
    <InlineData(2.0F)>
    Public Sub The_marker_is_glyph_tier_16_scaled_and_the_header_does_not_shrink(scale As Single)
        WithGroup(scale, "Group", expanded:=False, rtl:=False,
            Sub(group)
                Dim cell As Rectangle = group.MarkerBounds()
                Dim tier As Integer = CInt(Math.Round(16 * scale))
                Assert.Equal(tier, cell.Width)
                Assert.Equal(tier, cell.Height)
                Assert.True(group.Header.Height >= Math.Max(32, CInt(Math.Ceiling(32 * scale))), "the header shrank: " & group.Header.Height)

                ' Collapsed LTR points right: the base is the tall side. 12 of 16 units along the base, 8 deep.
                Using bmp As Bitmap = Render(group)
                    Dim ink As Rectangle = InkBox(bmp, cell)
                    Assert.False(ink.IsEmpty)
                    Assert.True(Math.Abs(ink.Height - 12.0 * tier / 16.0) <= 1.5, "base " & ink.Height & " at tier " & tier)
                    Assert.True(Math.Abs(ink.Width - 8.0 * tier / 16.0) <= 1.5, "depth " & ink.Width & " at tier " & tier)
                    Assert.True(ink.Height <= tier, "the ink is taller than its tier cell")
                End Using
            End Sub)
    End Sub

    ' ------------------------------------------------------------------ live changes ----

    <Fact>
    Public Sub The_marker_follows_a_toggle_a_right_to_left_change_a_scale_change_and_the_ink_live()
        WithGroup(1.0F, "Group", expanded:=False, rtl:=False,
            Sub(group)
                Dim before As Integer(,)
                Dim cellBefore As Rectangle = group.MarkerBounds()
                Using bmp As Bitmap = Render(group)
                    before = Crop(bmp, cellBefore)
                End Using

                ' A toggle: right becomes down.
                group.Expanded = True
                Using bmp As Bitmap = Render(group)
                    Assert.True(Differing(before, Crop(bmp, group.MarkerBounds()), mirrorB:=False) > 20, "the marker did not change on a toggle")
                End Using
                group.Expanded = False

                ' Ambient RTL from the window: the marker moves to the right edge and points left.
                group.RightToLeft = RightToLeft.Yes
                Dim cellRtl As Rectangle = group.MarkerBounds()
                Assert.True(cellRtl.Left > GroupWidth \ 2, "the marker did not move to the right: " & cellRtl.ToString())
                Using bmp As Bitmap = Render(group)
                    Assert.True(Differing(before, Crop(bmp, cellRtl), mirrorB:=True) <= 2, "the RTL marker is not the mirrored LTR one")
                End Using
                group.RightToLeft = RightToLeft.No
                Assert.True(group.MarkerBounds().Right < GroupWidth \ 2, "the marker did not come back to the left")

                ' The ink: a theme or high-contrast change recolours the header, and the triangle with it.
                group.Header.ForeColor = Color.FromArgb(200, 0, 0)
                Using bmp As Bitmap = Render(group)
                    Dim cell As Rectangle = group.MarkerBounds()
                    Dim solid As Color = bmp.GetPixel(cell.X + 6, cell.Y + 8)
                    Assert.True(solid.R > 180 AndAlso solid.G < 40 AndAlso solid.B < 40, "the triangle did not take the header's ink: " & solid.ToString())
                End Using
                group.Header.ForeColor = Color.Black

                ' The window's DPI/scale: tier 16 becomes 24 at 150 percent without rebuilding the group.
                group.SetLayoutScale(1.5F)
                group.ArrangeContents()
                Assert.Equal(24, group.MarkerBounds().Width)
                Assert.True(group.Header.Height >= 48)
            End Sub)
    End Sub

    ' ------------------------------------------------------------------ what it must not break ----

    <Fact>
    Public Sub A_click_on_the_header_toggles_the_group_once_whatever_the_marker_does()
        WithGroup(1.0F, "Group", expanded:=True, rtl:=False,
            Sub(group)
                Dim raised As Integer = 0
                AddHandler group.ExpansionChanged, Sub() raised += 1
                Dim onClick As MethodInfo = GetType(Control).GetMethod("OnClick", BindingFlags.Instance Or BindingFlags.NonPublic)
                onClick.Invoke(group.Header, New Object() {EventArgs.Empty})
                Assert.False(group.Expanded)
                Assert.Equal(1, raised)
                onClick.Invoke(group.Header, New Object() {EventArgs.Empty})
                Assert.True(group.Expanded)
                Assert.Equal(2, raised)
            End Sub)
    End Sub

    Private Shared Function SourceFile(name As String) As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", name)
            If File.Exists(candidate) Then Return File.ReadAllText(candidate)
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src\" & name & " not found above " & AppContext.BaseDirectory)
    End Function

    <Fact>
    Public Sub The_group_header_no_longer_bakes_the_action_chevron()
        ' collapsibleSection / collapsibleSectionOpen are nav.expand / nav.collapse: action chevrons that stay
        ' on the expand-all / collapse-all commands, never on a group header (COLLAPSIBLE-GROUP section 3 rule 2).
        Dim groupSource As String = SourceFile("SettingsCollapsibleGroup.vb")
        Dim builder As String = SourceFile("Table_Form.SettingsGroups.vb")
        Assert.DoesNotContain("collapsibleSection", builder)
        Assert.DoesNotContain("GlyphRenderer", builder)
        Assert.DoesNotContain("Header.Image", groupSource)
        Assert.DoesNotContain("ImageBeforeText", groupSource)
    End Sub
End Class
#End If
