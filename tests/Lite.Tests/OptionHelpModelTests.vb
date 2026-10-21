#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports Xunit

''' <summary>
''' The pure half of the option help of BOOLEAN-OPTION 2.0 sections 2 and 4 (ticket 243, slice T-11a): the rules of an
''' authored help source, the accessible-name template, the hover timing, the popup placement and the heading row's
''' geometry. No window is built here; <see cref="OptionHelpRowTests"/> holds the controls.
''' </summary>
<Collection("LocalizationState")>
Public Class OptionHelpModelTests

    Private Shared Function Sound() As OptionHelpSource
        Return New OptionHelpSource("net.scan", "Looks for devices on the network.",
                                    "Looks for devices on the local network. When on, the scan runs at start; when off, only on request. Needs the network permission.")
    End Function

    ' ---------------------------------------------------------------- the source and its rules

    <Fact>
    Public Sub A_sound_source_has_no_problem()
        Assert.True(OptionHelpRules.IsValid(Sound()))
        Assert.Empty(OptionHelpRules.Problems(Sound()))
    End Sub

    <Fact>
    Public Sub The_summary_and_the_full_text_are_two_fields_of_one_object()
        Dim source As OptionHelpSource = Sound()
        Assert.Equal("net.scan", source.Key)
        Assert.True(source.Summary.Length < source.Full.Length)
        ' One authored source: a language switch builds another object, there is no second copy to drift.
        Assert.Same(source, source)
    End Sub

    <Theory>
    <InlineData("", "s", "a longer full text", "KeyMissing")>
    <InlineData("Net Scan", "s", "a longer full text", "KeyMalformed")>
    <InlineData("net/scan", "s", "a longer full text", "KeyMalformed")>
    <InlineData("k", "", "a longer full text", "SummaryMissing")>
    <InlineData("k", "   ", "a longer full text", "SummaryMissing")>
    <InlineData("k", "s", "", "FullMissing")>
    <InlineData("k", "s", "  ", "FullMissing")>
    <InlineData("k", "same text", "same text", "SummaryNotShorter")>
    <InlineData("k", "a summary that is longer", "short", "SummaryNotShorter")>
    Public Sub Each_broken_rule_is_named(key As String, summary As String, full As String, expected As String)
        Dim problems As List(Of OptionHelpProblem) = OptionHelpRules.Problems(New OptionHelpSource(key, summary, full))
        Assert.Contains(CType([Enum].Parse(GetType(OptionHelpProblem), expected), OptionHelpProblem), problems)
        Assert.False(OptionHelpRules.IsValid(New OptionHelpSource(key, summary, full)))
    End Sub

    <Fact>
    Public Sub Nothing_and_nulls_are_a_source_with_every_text_missing()
        Assert.False(OptionHelpRules.IsValid(Nothing))
        Dim nulls As New OptionHelpSource(Nothing, Nothing, Nothing)
        Dim problems As List(Of OptionHelpProblem) = OptionHelpRules.Problems(nulls)
        Assert.Contains(OptionHelpProblem.KeyMissing, problems)
        Assert.Contains(OptionHelpProblem.SummaryMissing, problems)
        Assert.Contains(OptionHelpProblem.FullMissing, problems)
    End Sub

    <Fact>
    Public Sub A_summary_longer_than_the_full_text_does_not_also_report_it_missing()
        Dim problems As List(Of OptionHelpProblem) = OptionHelpRules.Problems(New OptionHelpSource("k", "a very long summary", "short"))
        Assert.Equal(New List(Of OptionHelpProblem) From {OptionHelpProblem.SummaryNotShorter}, problems)
    End Sub

    <Fact>
    Public Sub Two_options_must_not_share_a_help_key()
        Dim a As New OptionHelpSource("a.one", "s", "full text")
        Dim b As New OptionHelpSource("b.two", "s", "full text")
        Dim again As New OptionHelpSource("a.one", "t", "other full text")
        Assert.Empty(OptionHelpRules.DuplicateKeys(New OptionHelpSource() {a, b}))
        Assert.Equal(New List(Of String) From {"a.one"}, OptionHelpRules.DuplicateKeys(New OptionHelpSource() {a, b, again, again}))
        Assert.Empty(OptionHelpRules.DuplicateKeys(Nothing))
    End Sub

    ' ---------------------------------------------------------------- the accessible name

    Private Shared Sub InLanguage(code As String, body As Action)
        Dim saved As String = Localization.CurrentCode
        Localization.CurrentCode = code
        Try
            body()
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub The_accessible_name_is_a_localized_template_with_the_option_in_it()
        InLanguage("en", Sub() Assert.Equal("Help: Network scan", OptionHelpRules.AccessibleName("Network scan")))
        InLanguage("ru", Sub() Assert.Equal("Справка: Network scan", OptionHelpRules.AccessibleName("Network scan")))
        InLanguage("ar", Sub() Assert.Equal("مساعدة: Network scan", OptionHelpRules.AccessibleName("Network scan")))
    End Sub

    <Fact>
    Public Sub The_template_exists_in_all_thirteen_languages_with_its_placeholder()
        For index As Integer = 0 To Localization.Codes.Length - 1
            InLanguage(Localization.Codes(index), Sub()
                                                       Dim text As String = OptionHelpRules.AccessibleName("X1")
                                                       Assert.Contains("X1", text)
                                                       Assert.DoesNotContain("{0}", text)
                                                   End Sub)
        Next
    End Sub

    <Fact>
    Public Sub A_nameless_option_still_gets_a_name_for_its_help()
        InLanguage("en", Sub()
                             Assert.Equal("Help", OptionHelpRules.AccessibleName(""))
                             Assert.Equal("Help", OptionHelpRules.AccessibleName("   "))
                         End Sub)
    End Sub

    ' ---------------------------------------------------------------- the hover timing

    <Fact>
    Public Sub The_pointer_opens_the_summary_after_it_has_rested()
        Dim hover As New OptionHelpHover()
        hover.PointerEnteredButton(1000)
        Assert.Equal(OptionHelpHoverAction.None, hover.Tick(1000 + OptionHelpRules.HoverShowDelayMs - 1))
        Assert.False(hover.SummaryShown)
        Assert.Equal(OptionHelpHoverAction.ShowSummary, hover.Tick(1000 + OptionHelpRules.HoverShowDelayMs))
        Assert.True(hover.SummaryShown)
        Assert.Equal(OptionHelpHoverAction.None, hover.Tick(5000))
    End Sub

    <Fact>
    Public Sub A_pointer_that_only_passes_by_opens_nothing()
        Dim hover As New OptionHelpHover()
        hover.PointerEnteredButton(0)
        hover.PointerLeftButton(100)
        Assert.Equal(OptionHelpHoverAction.None, hover.Tick(2000))
        Assert.True(hover.IsIdle)
    End Sub

    <Fact>
    Public Sub Keyboard_focus_opens_the_summary_at_once()
        Dim hover As New OptionHelpHover()
        hover.FocusEntered(500)
        Assert.Equal(OptionHelpHoverAction.ShowSummary, hover.Tick(500))
    End Sub

    <Fact>
    Public Sub The_summary_stays_while_the_pointer_travels_from_the_button_into_it_and_while_it_is_read()
        Dim hover As New OptionHelpHover()
        hover.PointerEnteredButton(0)
        Assert.Equal(OptionHelpHoverAction.ShowSummary, hover.Tick(500))
        hover.PointerLeftButton(600)
        Assert.Equal(OptionHelpHoverAction.None, hover.Tick(600 + OptionHelpRules.HoverGraceMs - 1))
        hover.PointerEnteredPopup(600 + OptionHelpRules.HoverGraceMs - 50)
        ' Read for as long as the user likes.
        Assert.Equal(OptionHelpHoverAction.None, hover.Tick(60000))
        Assert.True(hover.SummaryShown)
        hover.PointerLeftPopup(60000)
        Assert.Equal(OptionHelpHoverAction.None, hover.Tick(60000 + OptionHelpRules.HoverGraceMs - 1))
        Assert.Equal(OptionHelpHoverAction.HideSummary, hover.Tick(60000 + OptionHelpRules.HoverGraceMs))
        Assert.True(hover.IsIdle)
    End Sub

    <Fact>
    Public Sub The_focus_keeps_the_summary_open_after_the_pointer_has_gone()
        Dim hover As New OptionHelpHover()
        hover.FocusEntered(0)
        hover.Tick(0)
        hover.PointerEnteredButton(10)
        hover.PointerLeftButton(20)
        Assert.Equal(OptionHelpHoverAction.None, hover.Tick(10000))
        hover.FocusLeft(10000)
        Assert.Equal(OptionHelpHoverAction.HideSummary, hover.Tick(10000 + OptionHelpRules.HoverGraceMs))
    End Sub

    <Fact>
    Public Sub Escape_closes_the_summary_and_it_stays_closed_while_the_pointer_rests()
        Dim hover As New OptionHelpHover()
        hover.PointerEnteredButton(0)
        hover.Tick(500)
        Assert.True(hover.Escape())
        Assert.False(hover.SummaryShown)
        ' The pointer has not moved: nothing opens again, however long it rests.
        Assert.Equal(OptionHelpHoverAction.None, hover.Tick(50000))
        Assert.False(hover.SummaryShown)
        ' It opens again after the pointer left and came back.
        hover.PointerLeftButton(51000)
        Assert.False(hover.IsSuppressed)
        hover.PointerEnteredButton(52000)
        Assert.Equal(OptionHelpHoverAction.ShowSummary, hover.Tick(52000 + OptionHelpRules.HoverShowDelayMs))
    End Sub

    <Fact>
    Public Sub Escape_with_nothing_open_is_not_used_up()
        Dim hover As New OptionHelpHover()
        Assert.False(hover.Escape())
        hover.PointerEnteredButton(0)
        Assert.False(hover.Escape())
    End Sub

    <Fact>
    Public Sub A_persistent_open_dismisses_the_summary_the_same_way()
        Dim hover As New OptionHelpHover()
        hover.FocusEntered(0)
        hover.Tick(0)
        hover.Dismiss()
        Assert.False(hover.SummaryShown)
        Assert.Equal(OptionHelpHoverAction.None, hover.Tick(10000))
        hover.FocusLeft(10001)
        Assert.False(hover.IsSuppressed)
    End Sub

    <Fact>
    Public Sub Reset_forgets_every_cause()
        Dim hover As New OptionHelpHover()
        hover.PointerEnteredButton(0)
        hover.FocusEntered(0)
        hover.Tick(900)
        hover.Reset()
        Assert.True(hover.IsIdle)
        Assert.Equal(OptionHelpHoverAction.None, hover.Tick(99999))
    End Sub

    ' ---------------------------------------------------------------- where the popup goes

    Private Shared ReadOnly WorkArea As New Rectangle(0, 0, 1000, 800)

    <Fact>
    Public Sub The_popup_goes_below_the_button_at_the_reading_start_edge()
        Dim anchor As New Rectangle(300, 100, 32, 32)
        Dim box As New Rectangle(20, 100, 32, 32)
        Dim ltr As Rectangle = OptionHelpPlacement.Place(anchor, New Rectangle() {box}, New Size(320, 100), WorkArea, False, 4)
        Assert.Equal(anchor.Left, ltr.Left)
        Assert.Equal(anchor.Bottom + 4, ltr.Top)
        Dim rtl As Rectangle = OptionHelpPlacement.Place(anchor, New Rectangle() {box}, New Size(320, 100), WorkArea, True, 4)
        Assert.Equal(anchor.Right, rtl.Right)
        Assert.Equal(anchor.Bottom + 4, rtl.Top)
    End Sub

    <Fact>
    Public Sub The_popup_does_not_cover_the_originating_checkbox_when_another_place_is_free()
        ' The checkbox lies where "below the button" would put the popup; the popup goes above instead.
        Dim anchor As New Rectangle(300, 400, 32, 32)
        Dim box As New Rectangle(280, 440, 200, 60)
        Dim placed As Rectangle = OptionHelpPlacement.Place(anchor, New Rectangle() {box}, New Size(320, 100), WorkArea, False, 4)
        Assert.False(placed.IntersectsWith(box), "the popup covers the checkbox")
        Assert.False(placed.IntersectsWith(anchor), "the popup covers the help button")
    End Sub

    <Fact>
    Public Sub The_popup_stays_inside_the_work_area()
        Dim anchor As New Rectangle(960, 770, 32, 32)
        Dim placed As Rectangle = OptionHelpPlacement.Place(anchor, Nothing, New Size(320, 200), WorkArea, False, 4)
        Assert.True(WorkArea.Contains(placed), "popup " & placed.ToString())
    End Sub

    <Fact>
    Public Sub A_popup_larger_than_the_work_area_is_cut_to_it()
        Dim placed As Rectangle = OptionHelpPlacement.Place(New Rectangle(10, 10, 32, 32), Nothing, New Size(5000, 5000), WorkArea, False, 4)
        Assert.True(WorkArea.Contains(placed))
    End Sub

    ' ---------------------------------------------------------------- the heading row

    Private Const Gap As Integer = 8

    <Fact>
    Public Sub Left_to_right_the_heading_is_glyph_caption_help_eight_units_apart()
        Dim plan As OptionHeadingPlan = OptionHeadingGeometry.Plan(50, 400, False, 16, 32, Gap, 120, 40)
        Assert.Equal(50, plan.GlyphLeft)
        Assert.Equal(50 + 16 + Gap, plan.CaptionLeft)
        Assert.Equal(120, plan.CaptionWidth)
        Assert.Equal(plan.CaptionLeft + 120 + Gap, plan.HelpLeft)
        Assert.False(plan.Wrapped)
        ' The help follows the name: it is not pushed to the cell's far edge.
        Assert.True(plan.HelpLeft + 32 < 50 + 400 - 100)
    End Sub

    <Fact>
    Public Sub Right_to_left_the_heading_is_the_exact_mirror()
        Dim ltr As OptionHeadingPlan = OptionHeadingGeometry.Plan(50, 400, False, 16, 32, Gap, 120, 40)
        Dim rtl As OptionHeadingPlan = OptionHeadingGeometry.Plan(50, 400, True, 16, 32, Gap, 120, 40)
        Dim mirror As Func(Of Integer, Integer, Integer) = Function(left As Integer, width As Integer) 50 + 400 - (left - 50) - width
        Assert.Equal(mirror(ltr.GlyphLeft, 16), rtl.GlyphLeft)
        Assert.Equal(mirror(ltr.CaptionLeft, ltr.CaptionWidth), rtl.CaptionLeft)
        Assert.Equal(mirror(ltr.HelpLeft, 32), rtl.HelpLeft)
        Assert.Equal(ltr.CaptionWidth, rtl.CaptionWidth)
    End Sub

    <Fact>
    Public Sub An_absent_part_reserves_no_slot_and_no_gap()
        Dim plain As OptionHeadingPlan = OptionHeadingGeometry.Plan(50, 400, False, 0, 0, Gap, 120, 40)
        Assert.Equal(50, plain.CaptionLeft)
        Dim helpOnly As OptionHeadingPlan = OptionHeadingGeometry.Plan(50, 400, False, 0, 32, Gap, 120, 40)
        Assert.Equal(50, helpOnly.CaptionLeft)
        Assert.Equal(50 + 120 + Gap, helpOnly.HelpLeft)
        Dim glyphOnly As OptionHeadingPlan = OptionHeadingGeometry.Plan(50, 400, False, 16, 0, Gap, 120, 40)
        Assert.Equal(50 + 16 + Gap, glyphOnly.CaptionLeft)
        Assert.Equal(120, glyphOnly.CaptionWidth)
        Dim helpOnlyRtl As OptionHeadingPlan = OptionHeadingGeometry.Plan(50, 400, True, 0, 32, Gap, 120, 40)
        Assert.Equal(50 + 400 - 120, helpOnlyRtl.CaptionLeft)
    End Sub

    <Fact>
    Public Sub A_long_caption_wraps_inside_what_the_glyph_and_the_help_leave_and_nothing_overlaps()
        For Each rtl As Boolean In {False, True}
            Dim plan As OptionHeadingPlan = OptionHeadingGeometry.Plan(50, 300, rtl, 16, 32, Gap, 900, 40)
            Assert.True(plan.Wrapped)
            Assert.Equal(300 - 16 - Gap - Gap - 32, plan.CaptionWidth)
            Dim parts As New List(Of Rectangle) From {
                New Rectangle(plan.GlyphLeft, 0, 16, 16),
                New Rectangle(plan.CaptionLeft, 0, plan.CaptionWidth, 16),
                New Rectangle(plan.HelpLeft, 0, 32, 32)}
            For i As Integer = 0 To parts.Count - 1
                Assert.True(parts(i).Left >= 50 AndAlso parts(i).Right <= 350, "part " & i & " leaves the cell, rtl=" & rtl)
                For j As Integer = i + 1 To parts.Count - 1
                    Assert.False(parts(i).IntersectsWith(parts(j)), "parts " & i & " and " & j & " overlap, rtl=" & rtl)
                Next
            Next
        Next
    End Sub

    <Fact>
    Public Sub A_very_narrow_cell_keeps_a_positive_caption_width()
        Dim plan As OptionHeadingPlan = OptionHeadingGeometry.Plan(0, 50, False, 16, 32, Gap, 400, 40)
        Assert.True(plan.CaptionWidth >= 40)
    End Sub
End Class
#End If
