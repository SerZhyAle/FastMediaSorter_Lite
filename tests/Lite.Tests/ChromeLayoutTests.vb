#If Not NETFRAMEWORK Then
Option Strict On
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports Xunit

''' <summary>
''' The top band with the document panel (280 section 7): row 1, row 2 and the document panel stacked from the top of the
''' media area beside the window-controls panel in its corner. Each is placed by the band rule of 188 section 3.5 the way
''' <c>ChromeAnchoredRect</c> composes it - row 2 under row 1, the document panel under the lowest anchored row, capped to
''' the width left of the corner or wrapped below it - and the top rows' reveal zone is measured from the lowest of them.
''' </summary>
Public Class ChromeLayoutTests

    Private Const MediaWidth As Integer = 1600
    Private Const Gap As Integer = 4
    Private Const Inset As Integer = 8
    Private Const CornerWidth As Integer = 224
    ''' <summary>Every top row wants more than the width left of the corner, so a row that shares the band is capped.</summary>
    Private Const NaturalWidth As Integer = 1400

    Private Shared Function Corner(height As Integer) As Rectangle
        Return New Rectangle(MediaWidth - Inset - CornerWidth, Inset, CornerWidth, height)
    End Function

    ''' <summary>The top rows as the glue places them, media-relative: each one's natural top is the bottom of the one above
    ''' plus the gap, its band decision is <see cref="ChromeLayout.PlaceRowBesideCornerPanel"/> against the corner's obstacle,
    ''' and its width is its natural width capped to what the decision leaves.</summary>
    Private Shared Function StackTopRows(corner As Rectangle, rowHeight As Integer, ParamArray minimumWidths As Integer()) As Rectangle()
        Dim obstacle As Rectangle = ChromeLayout.BandObstacle(corner, MediaWidth, Inset)
        Dim rows(minimumWidths.Length - 1) As Rectangle
        Dim naturalTop As Integer = Inset
        For index As Integer = 0 To minimumWidths.Length - 1
            Dim placement As ChromeLayout.RowBandPlacement = ChromeLayout.PlaceRowBesideCornerPanel(
                naturalTop, rowHeight, minimumWidths(index), obstacle, MediaWidth, Gap, Inset)
            rows(index) = New Rectangle(Inset, placement.Top, Math.Min(NaturalWidth, placement.MaxWidth), rowHeight)
            naturalTop = rows(index).Bottom + Gap
        Next
        Return rows
    End Function

    <Theory>
    <InlineData(44, 700, 700, 700)>
    <InlineData(52, 700, 700, 1500)>
    <InlineData(120, 700, 700, 700)>
    <InlineData(120, 700, 1500, 700)>
    <InlineData(120, 1500, 700, 700)>
    <InlineData(120, 700, 700, 1500)>
    <InlineData(200, 1500, 1500, 1500)>
    Public Sub The_three_top_rows_never_cover_each_other_or_the_corner_panel(cornerHeight As Integer, row1Minimum As Integer,
                                                                            row2Minimum As Integer, documentMinimum As Integer)
        Dim controls As Rectangle = Corner(cornerHeight)
        Dim rows As Rectangle() = StackTopRows(controls, 44, row1Minimum, row2Minimum, documentMinimum)
        For index As Integer = 0 To rows.Length - 1
            Assert.False(rows(index).IntersectsWith(controls), "row " & (index + 1) & " " & rows(index).ToString() & " covers the corner panel " & controls.ToString())
            If index > 0 Then
                Assert.True(rows(index).Top >= rows(index - 1).Bottom + Gap, "row " & (index + 1) & " is not below row " & index)
            End If
            For other As Integer = index + 1 To rows.Length - 1
                Assert.False(rows(index).IntersectsWith(rows(other)), "rows " & (index + 1) & " and " & (other + 1) & " overlap")
            Next
        Next
    End Sub

    <Fact>
    Public Sub A_document_panel_beside_a_tall_corner_panel_is_capped_and_one_that_cannot_shrink_wraps_below_it()
        ' At 200 percent the window commands reach past row 2: the document panel is the third row in their band.
        Dim controls As Rectangle = Corner(120)
        Dim capped As Rectangle() = StackTopRows(controls, 44, 700, 700, 700)
        Dim besideWidth As Integer = MediaWidth - 2 * Inset - CornerWidth - Gap
        Assert.True(capped(2).Top < controls.Bottom, "the fixture must put the document panel in the corner's band")
        Assert.Equal(capped(1).Bottom + Gap, capped(2).Top)
        Assert.Equal(besideWidth, capped(2).Width)

        Dim wrapped As Rectangle() = StackTopRows(controls, 44, 700, 700, besideWidth + 1)
        Assert.Equal(controls.Bottom + Gap, wrapped(2).Top)
        Assert.Equal(NaturalWidth, wrapped(2).Width)
        ' Rows 1 and 2 are where they were: the third row's wrap moves nothing above it.
        Assert.Equal(capped(0), wrapped(0))
        Assert.Equal(capped(1), wrapped(1))
    End Sub

    <Fact>
    Public Sub Below_the_corner_panel_the_document_panel_takes_the_whole_band()
        Dim controls As Rectangle = Corner(44)
        Dim rows As Rectangle() = StackTopRows(controls, 44, 700, 700, 700)
        Assert.True(rows(2).Top >= controls.Bottom)
        Assert.Equal(NaturalWidth, rows(2).Width)
    End Sub

    <Fact>
    Public Sub The_reveal_zone_of_the_top_band_reaches_the_document_panel_only_while_it_is_shown()
        Const zoneMargin As Integer = 48
        Dim controls As Rectangle = Corner(120)
        Dim withPanel As Rectangle() = StackTopRows(controls, 44, 700, 700, 700)
        Dim withoutPanel As Rectangle() = StackTopRows(controls, 44, 700, 700)
        Dim shown As Integer = ChromeAutoHide.TopHotZoneBottom(New Integer() {withPanel(0).Bottom, withPanel(1).Bottom, withPanel(2).Bottom}, zoneMargin)
        Dim absent As Integer = ChromeAutoHide.TopHotZoneBottom(New Integer() {withoutPanel(0).Bottom, withoutPanel(1).Bottom}, zoneMargin)
        Assert.Equal(withPanel(2).Bottom + zoneMargin, shown)
        Assert.Equal(withoutPanel(1).Bottom + zoneMargin, absent)
        Assert.True(shown > absent)
        ' The order the panels are listed in does not matter: the lowest bottom decides.
        Assert.Equal(shown, ChromeAutoHide.TopHotZoneBottom(New Integer() {withPanel(2).Bottom, withPanel(0).Bottom, withPanel(1).Bottom}, zoneMargin))
    End Sub

    ' --- the glue composes the band the way the facts above do -------------------------------------------------------

    Private Shared Function Source(name As String) As String
        Dim root As New DirectoryInfo(AppContext.BaseDirectory)
        While root IsNot Nothing AndAlso Not File.Exists(Path.Combine(root.FullName, "src", "Main_Form.vb"))
            root = root.Parent
        End While
        Assert.NotNull(root)
        Return File.ReadAllText(Path.Combine(root.FullName, "src", name)).Replace(vbCrLf, vbLf)
    End Function

    <Fact>
    Public Sub The_glue_places_the_document_panel_by_the_band_rule_under_the_lowest_anchored_row()
        Dim chrome As String = Source("Main_Form.Chrome.vb")
        Assert.Contains("ElseIf p.Name = Document_Panel_Name Then" & vbLf & "            top = DocumentPanelTop(media, inset, factor)", chrome)
        Assert.Contains("If p.Name = ""chrome_Row1"" OrElse p.Name = ""chrome_Row2"" OrElse p.Name = Document_Panel_Name Then", chrome)
        Assert.Contains("ChromeAutoHide.TopHotZoneBottom(bottoms, zoneHeight)", chrome)
        Dim panel As String = Source("Main_Form.DocumentPanel.vb")
        Assert.Contains("If ChromePanelIsFree(other) Then Continue For", panel)
        Assert.Contains("bottom = Math.Max(bottom, ChromeAnchoredRect(other).Bottom)", panel)
        Assert.Contains("bottom + UiScale.Px(ChromeMetrics.Gap, factor)", panel)
    End Sub
End Class
#End If
