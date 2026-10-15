#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports Xunit

''' <summary>
''' The wiring of the last open items of ticket 125 (UX-313 b, i, m and UX-332): a window with no media holds its
''' panels, the end of a path edit returns the keyboard without taking the panel off the screen, a conditional
''' button keeps its place, and the recipients table sets its delete row apart and gives the move zone its verb.
''' The rules themselves are pure and held by ChromeAutoHideTests, ChromeFoundationTests, ChromeMenuRulesTests and
''' RecipientsOverlayPlacementTests; what is proven HERE is that the glue calls them where the ticket says - by
''' reading the sources, the way the dialog-layer and the overlay-seam tests hold their hooks.
''' </summary>
Public Class ChromeResidueSeamTests

    Private Shared Function Source(name As String) As String
        Dim root As New DirectoryInfo(AppContext.BaseDirectory)
        While root IsNot Nothing AndAlso Not File.Exists(Path.Combine(root.FullName, "src", "Main_Form.vb"))
            root = root.Parent
        End While
        Assert.NotNull(root)
        Dim fullPath As String = Path.Combine(root.FullName, "src", name)
        Assert.True(File.Exists(fullPath), "missing source: " & name)
        Return File.ReadAllText(fullPath).Replace(vbCrLf, vbLf).Replace(vbLf, vbCrLf)
    End Function

    ''' <summary>The body of the first member whose declaration contains <paramref name="declared"/>, from the
    ''' declaration to the line before its closing End Sub / End Function.</summary>
    Private Shared Function MemberBody(text As String, declared As String) As String
        Dim start As Integer = text.IndexOf(declared, StringComparison.Ordinal)
        Assert.True(start >= 0, "declaration not found: " & declared)
        Dim endSub As Integer = text.IndexOf(vbCrLf & "    End Sub", start, StringComparison.Ordinal)
        Dim endFunction As Integer = text.IndexOf(vbCrLf & "    End Function", start, StringComparison.Ordinal)
        Dim [end] As Integer = If(endSub >= 0 AndAlso (endFunction < 0 OrElse endSub < endFunction), endSub, endFunction)
        Assert.True([end] > start, "member end not found: " & declared)
        Return text.Substring(start, [end] - start)
    End Function

    ''' <summary>The needle stands inside an <c>#If Not NETFRAMEWORK</c> branch: the net48 fallback keeps its
    ''' behaviour (the maintenance policy of 2026-07-19).</summary>
    Private Shared Sub AssertInModernBranch(text As String, needle As String)
        Dim at As Integer = text.IndexOf(needle, StringComparison.Ordinal)
        Assert.True(at >= 0, "not found: " & needle)
        Dim opened As Integer = text.LastIndexOf("#If ", at, StringComparison.Ordinal)
        Dim closed As Integer = text.LastIndexOf("#End If", at, StringComparison.Ordinal)
        Assert.True(opened >= 0 AndAlso opened > closed, "not inside a conditional branch: " & needle)
        Assert.StartsWith("#If Not NETFRAMEWORK", text.Substring(opened), StringComparison.Ordinal)
    End Sub

    ' --- UX-313m: a window with no media holds its panels ---------------------------------------------

    <Fact>
    Public Sub The_tick_holds_the_panels_while_the_window_has_no_media_and_media_means_a_picture_or_a_player()
        Dim chrome As String = Source("Main_Form.Chrome.vb")
        Dim tick As String = MemberBody(chrome, "Private Sub Chrome_Timer_Tick(")
        Assert.Contains("ChromeAutoHide.HoldsForEmptyWindow(ChromeMediaOnScreen(), now, chrome_Empty_Since)", tick)
        ' It is the tick's own HOLD (gated by the window being in use), written before the loop that reads it.
        Assert.True(tick.IndexOf("HoldsForEmptyWindow", StringComparison.Ordinal) <
                    tick.IndexOf("ChromeAutoHide.NextWanted(", StringComparison.Ordinal))
        Dim media As String = MemberBody(chrome, "Private Function ChromeMediaOnScreen()")
        For Each flag As String In New String() {"is_PictureBox1_Visible", "is_PictureBox2_Visible", "is_Vlc_Playing"}
            Assert.Contains(flag, media)
        Next
    End Sub

    ' --- UX-313i: the keyboard goes back without the panel blinking --------------------------------

    <Fact>
    Public Sub The_end_of_a_path_edit_activates_the_viewer_directly_and_hides_the_panel_only_as_the_fallback()
        Dim chrome As String = Source("Main_Form.Chrome.vb")
        Dim helper As String = MemberBody(chrome, "Private Sub ChromeReturnKeyboardToViewer(")
        Dim direct As Integer = helper.IndexOf("SetActiveWindow(Me.Handle)", StringComparison.Ordinal)
        Dim verify As Integer = helper.IndexOf("If GetActiveWindow() = Me.Handle Then Return", StringComparison.Ordinal)
        Dim fallbackHide As Integer = helper.IndexOf("panel.Host.Hide()", StringComparison.Ordinal)
        Assert.True(direct >= 0, "the viewer is activated directly")
        Assert.True(verify > direct, "the activation is verified before the panel is touched")
        Assert.True(fallbackHide > verify, "the hide, activate, show sequence is only what runs when the direct one did not land")
        Assert.Contains("If panel.Alpha > 0 Then panel.Host.Show()", helper)
        ' The handler of EditingEnded no longer hides the panel itself.
        Dim row As String = MemberBody(chrome, "Private Sub BuildChromeRow(")
        Assert.Contains("ChromeReturnKeyboardToViewer(panel)", row)
        Assert.DoesNotContain("panel.Host.Hide()", row)
    End Sub

    ' --- UX-313b: a conditional button keeps its place ----------------------------------------------

    <Fact>
    Public Sub Every_synced_item_says_whether_it_keeps_its_place_by_the_one_rule()
        Dim chrome As String = Source("Main_Form.Chrome.vb")
        Dim sync As String = MemberBody(chrome, "Private Function SyncChromeItem(")
        Assert.Contains("item.KeepsSlotWhenHidden = ChromeMenuRules.KeepsSlotWhenHidden(carrier.Name, DocHtmlTranslate.IsAvailableCached())", sync)
    End Sub

    ' --- UX-332: the delete row apart, a verb on the move zone --------------------------------------

    <Fact>
    Public Sub The_delete_row_is_set_apart_and_the_move_zone_carries_its_verb_on_the_modern_build_only()
        Dim overlay As String = Source("Main_Form.RecipientsOverlay.vb")
        Dim build As String = MemberBody(overlay, "Private Sub BuildRecipientsOverlay()")
        Dim gap As String = "If isDelete Then y += RecipientsOverlayRows.DeleteRowGap(rows.Count - 1, rowH)"
        Assert.Contains(gap, build)
        AssertInModernBranch(build, gap)
        Assert.Contains("Localization.T(""переместить"")", build)
        AssertInModernBranch(build, "b.TrailingText = move_Verb")
        ' The word is dropped before the folder is: the paint asks the pure rule whether there is room for it.
        Assert.Contains("RecipientsOverlayRows.ShowsMoveVerb(", overlay)
    End Sub

End Class
#End If
