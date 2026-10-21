#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System.IO
Imports System.Linq
Imports Xunit

''' <summary>
''' SETTINGS-NAVIGATION 2.0 section 5 rule 4: p95 below 150 ms for warm visits and below 500 ms for first visits, at
''' least 20 warm and 20 independent first-visit samples per destination, nearest-rank p95 over ALL recorded samples,
''' the maximum reported too, no outlier dropped, no warm sample relabelled cold, and repeated over-budget delays fail
''' even when the percentile hides them. This holds the pure arithmetic (<see cref="UiTimingStats"/>) and that the
''' hand-run script mirrors it.
''' </summary>
Public Class UiTimingStatsTests

    Private Shared Function Run(value As Double, count As Integer) As Double()
        Return Enumerable.Repeat(value, count).ToArray()
    End Function

    ' ------------------------------------------------------------------ nearest rank ----

    <Theory>
    <InlineData(1, 95, 1)>
    <InlineData(2, 95, 2)>
    <InlineData(19, 95, 19)>   ' ceil(18.05)
    <InlineData(20, 95, 19)>   ' exactly 19, not 20: 95 % of 20 is the 19th value
    <InlineData(21, 95, 20)>   ' ceil(19.95)
    <InlineData(100, 95, 95)>
    <InlineData(20, 100, 20)>
    <InlineData(20, 50, 10)>
    <InlineData(0, 95, 0)>
    Public Sub The_rank_is_the_ceiling_of_percent_of_count(count As Integer, percent As Integer, expected As Integer)
        Assert.Equal(expected, UiTimingStats.NearestRank(count, percent))
    End Sub

    <Fact>
    Public Sub One_sample_is_its_own_p95_and_maximum()
        Assert.Equal(42.5R, UiTimingStats.Percentile({42.5R}, 95))
        Assert.Equal(42.5R, UiTimingStats.Maximum({42.5R}))
    End Sub

    <Fact>
    Public Sub Nothing_recorded_gives_no_number_not_a_zero()
        Assert.True(Double.IsNaN(UiTimingStats.Percentile(New Double() {}, 95)))
        Assert.True(Double.IsNaN(UiTimingStats.Maximum(New Double() {})))
        Assert.True(Double.IsNaN(UiTimingStats.Percentile(Nothing, 95)))
    End Sub

    <Fact>
    Public Sub Twenty_samples_put_the_p95_on_the_19th_value_and_the_maximum_stays_the_20th()
        ' 1..20 ms in a scrambled order: the 19th smallest is 19, the maximum is 20.
        Dim scrambled As Double() = {7, 3, 20, 11, 1, 16, 5, 19, 2, 14, 9, 18, 4, 12, 8, 17, 6, 15, 10, 13}
        Assert.Equal(19.0R, UiTimingStats.Percentile(scrambled, 95))
        Assert.Equal(20.0R, UiTimingStats.Maximum(scrambled))
    End Sub

    <Fact>
    Public Sub Nineteen_samples_put_the_p95_on_the_slowest_one()
        Dim samples As Double() = Enumerable.Range(1, 19).Select(Function(i) CDbl(i)).ToArray()
        Assert.Equal(19.0R, UiTimingStats.Percentile(samples, 95))
    End Sub

    <Fact>
    Public Sub Ties_do_not_move_the_rank()
        Dim samples As Double() = Run(10.0R, 18).Concat({10.0R, 90.0R}).ToArray()
        Assert.Equal(10.0R, UiTimingStats.Percentile(samples, 95))      ' the 19th value is still a tie
        Assert.Equal(90.0R, UiTimingStats.Maximum(samples))
    End Sub

    <Fact>
    Public Sub The_input_is_not_reordered_or_consumed()
        Dim samples As Double() = {30.0R, 10.0R, 20.0R}
        UiTimingStats.Percentile(samples, 95)
        UiTimingStats.Judge(UiTimingKind.PageRepeat, samples)
        Assert.Equal({30.0R, 10.0R, 20.0R}, samples)
    End Sub

    ' ------------------------------------------------------------------ the verdict ----

    <Fact>
    Public Sub Nineteen_samples_are_not_enough_and_twenty_are()
        Dim nineteen As UiTimingStatsResult = UiTimingStats.Judge(UiTimingKind.PageRepeat, Run(20.0R, 19))
        Assert.Equal(UiTimingStatsVerdict.Insufficient, nineteen.Verdict)
        Assert.Equal(19, nineteen.Count)
        Dim twenty As UiTimingStatsResult = UiTimingStats.Judge(UiTimingKind.PageRepeat, Run(20.0R, 20))
        Assert.Equal(UiTimingStatsVerdict.Pass, twenty.Verdict)
        Assert.Equal(20, twenty.Count)
        Assert.Equal(UiTimingStats.MinimumSamples, 20)
    End Sub

    <Fact>
    Public Sub Nothing_recorded_is_insufficient_not_a_pass()
        Dim none As UiTimingStatsResult = UiTimingStats.Judge(UiTimingKind.PageFirst, Nothing)
        Assert.Equal(UiTimingStatsVerdict.Insufficient, none.Verdict)
        Assert.Equal(0, none.Count)
        Assert.True(Double.IsNaN(none.P95Ms))
        Assert.True(Double.IsNaN(none.MaxMs))
        Assert.Empty(none.OverBudgetSamples)
    End Sub

    <Theory>
    <InlineData(UiTimingKind.PageRepeat, 149.9, UiTimingStatsVerdict.Pass)>
    <InlineData(UiTimingKind.PageRepeat, 150.0, UiTimingStatsVerdict.OverBudget)>   ' "below 150 ms" is strict
    <InlineData(UiTimingKind.PageRepeat, 151.0, UiTimingStatsVerdict.OverBudget)>
    <InlineData(UiTimingKind.PageFirst, 499.9, UiTimingStatsVerdict.Pass)>
    <InlineData(UiTimingKind.PageFirst, 500.0, UiTimingStatsVerdict.OverBudget)>
    <InlineData(UiTimingKind.PageFirst, 200.0, UiTimingStatsVerdict.Pass)>
    <InlineData(UiTimingKind.Dialog, 500.0, UiTimingStatsVerdict.OverBudget)>
    Public Sub The_p95_must_be_strictly_below_the_budget_of_its_kind(kind As UiTimingKind, sample As Double, expected As UiTimingStatsVerdict)
        Assert.Equal(expected, UiTimingStats.Judge(kind, Run(sample, 20)).Verdict)
    End Sub

    <Fact>
    Public Sub A_sample_of_exactly_150_ms_is_over_budget_and_fails_warm_once_it_is_the_19th_value()
        ' 19 fast samples and one at exactly 150.0: the 19th value is fast, so the p95 passes - one outlier does not fail.
        Dim oneAt As Double() = Run(20.0R, 19).Concat({150.0R}).ToArray()
        Dim one As UiTimingStatsResult = UiTimingStats.Judge(UiTimingKind.PageRepeat, oneAt)
        Assert.Equal(UiTimingStatsVerdict.Pass, one.Verdict)
        Assert.Equal(150.0R, one.MaxMs)
        ' ... but when the 19th value itself is 150.0 the p95 is not below the budget.
        Dim twoAt As Double() = Run(20.0R, 18).Concat({150.0R, 150.0R}).ToArray()
        Dim two As UiTimingStatsResult = UiTimingStats.Judge(UiTimingKind.PageRepeat, twoAt)
        Assert.Equal(150.0R, two.P95Ms)
        Assert.Equal(UiTimingStatsVerdict.OverBudget, two.Verdict)
    End Sub

    <Fact>
    Public Sub A_single_huge_outlier_is_in_the_maximum_and_the_list_and_is_not_dropped()
        Dim samples As Double() = Run(20.0R, 19).Concat({4000.0R}).ToArray()
        Dim result As UiTimingStatsResult = UiTimingStats.Judge(UiTimingKind.PageRepeat, samples)
        Assert.Equal(4000.0R, result.MaxMs)
        Assert.Equal(20.0R, result.P95Ms)                              ' one in twenty does not move the 19th value
        Assert.Equal({4000.0R}, result.OverBudgetSamples.ToArray())
        Assert.True(result.HasIsolatedOutliers)
        Assert.False(result.RepeatedOverBudget)
        Assert.Equal(20, result.Count)                                 ' counted, not trimmed
        Assert.Equal(UiTimingStatsVerdict.Pass, result.Verdict)
    End Sub

    <Fact>
    Public Sub Two_outliers_in_twenty_move_the_p95_onto_the_slow_value_and_fail_it()
        Dim samples As Double() = Run(20.0R, 18).Concat({400.0R, 900.0R}).ToArray()
        Dim result As UiTimingStatsResult = UiTimingStats.Judge(UiTimingKind.PageRepeat, samples)
        Assert.Equal(400.0R, result.P95Ms)                             ' the 19th value
        Assert.Equal(900.0R, result.MaxMs)
        Assert.Equal(UiTimingStatsVerdict.OverBudget, result.Verdict)
        Assert.Equal({400.0R, 900.0R}, result.OverBudgetSamples.ToArray())
    End Sub

    <Fact>
    Public Sub Repeated_over_budget_samples_fail_even_when_the_percentile_masks_them()
        ' 3 slow samples out of 100: the 95th value is fast, so the percentile alone would pass.
        Dim samples As Double() = Run(20.0R, 97).Concat({200.0R, 300.0R, 400.0R}).ToArray()
        Assert.True(UiTimingStats.Percentile(samples, 95) < 150.0R)
        Dim result As UiTimingStatsResult = UiTimingStats.Judge(UiTimingKind.PageRepeat, samples)
        Assert.True(result.RepeatedOverBudget)
        Assert.False(result.HasIsolatedOutliers)
        Assert.Equal(UiTimingStatsVerdict.OverBudget, result.Verdict)
        Assert.Equal(3, result.OverBudgetSamples.Count)
        Assert.Equal(400.0R, result.MaxMs)
    End Sub

    <Fact>
    Public Sub Two_over_budget_samples_in_a_hundred_are_isolated_and_pass()
        Dim samples As Double() = Run(20.0R, 98).Concat({200.0R, 300.0R}).ToArray()
        Dim result As UiTimingStatsResult = UiTimingStats.Judge(UiTimingKind.PageRepeat, samples)
        Assert.False(result.RepeatedOverBudget)
        Assert.True(result.HasIsolatedOutliers)
        Assert.Equal(UiTimingStatsVerdict.Pass, result.Verdict)
    End Sub

    <Fact>
    Public Sub The_repeat_threshold_is_three()
        Assert.Equal(3, UiTimingStats.RepeatedOverBudgetCount)
    End Sub

    <Fact>
    Public Sub A_first_visit_is_judged_against_500_and_a_warm_visit_against_150_for_the_same_numbers()
        Dim samples As Double() = Run(300.0R, 20)
        Assert.Equal(UiTimingStatsVerdict.Pass, UiTimingStats.Judge(UiTimingKind.PageFirst, samples).Verdict)
        Assert.Equal(UiTimingStatsVerdict.OverBudget, UiTimingStats.Judge(UiTimingKind.PageRepeat, samples).Verdict)
    End Sub

    <Fact>
    Public Sub A_sample_that_is_not_a_number_is_slowest_never_a_pass_and_never_dropped()
        Dim samples As Double() = Run(20.0R, 19).Concat({Double.NaN}).ToArray()
        Dim result As UiTimingStatsResult = UiTimingStats.Judge(UiTimingKind.PageRepeat, samples)
        Assert.Equal(20, result.Count)
        Assert.True(Double.IsPositiveInfinity(result.MaxMs))
        Assert.Single(result.OverBudgetSamples)
    End Sub

    <Theory>
    <InlineData(UiTimingStatsVerdict.Pass, "PASS")>
    <InlineData(UiTimingStatsVerdict.OverBudget, "OVER_BUDGET")>
    <InlineData(UiTimingStatsVerdict.Insufficient, "INSUFFICIENT")>
    Public Sub The_verdict_words_are_the_ones_of_the_script(verdict As UiTimingStatsVerdict, word As String)
        Assert.Equal(word, UiTimingStats.VerdictName(verdict))
    End Sub

    ' ------------------------------------------------------------------ kinds stay apart ----

    <Fact>
    Public Sub A_warm_list_refuses_a_first_visit_sample_and_keeps_what_it_has()
        Dim warm As New UiTimingSamples(UiTimingKind.PageRepeat)
        warm.Add(UiTimingKind.PageRepeat, 30.0R)
        Assert.Throws(Of ArgumentException)(Sub() warm.Add(UiTimingKind.PageFirst, 300.0R))
        Assert.Throws(Of ArgumentException)(Sub() warm.Add(UiTimingKind.Dialog, 300.0R))
        Assert.Equal(1, warm.Count)
        Assert.Equal({30.0R}, warm.Values())
    End Sub

    <Fact>
    Public Sub A_first_visit_list_refuses_a_warm_sample()
        Dim first As New UiTimingSamples(UiTimingKind.PageFirst)
        Assert.Throws(Of ArgumentException)(Sub() first.Add(UiTimingKind.PageRepeat, 30.0R))
        Assert.Equal(0, first.Count)
        first.Add(UiTimingKind.PageFirst, 300.0R)
        Assert.Equal(1, first.Count)
    End Sub

    <Fact>
    Public Sub A_list_is_judged_against_the_budget_of_its_own_kind()
        Dim first As New UiTimingSamples(UiTimingKind.PageFirst)
        Dim warm As New UiTimingSamples(UiTimingKind.PageRepeat)
        For i As Integer = 1 To 20
            first.Add(UiTimingKind.PageFirst, 300.0R)
            warm.Add(UiTimingKind.PageRepeat, 300.0R)
        Next
        Assert.Equal(UiTimingStatsVerdict.Pass, first.Judge().Verdict)
        Assert.Equal(UiTimingStatsVerdict.OverBudget, warm.Judge().Verdict)
        Assert.Equal(UiTimingKind.PageFirst, first.Judge().Kind)
        Assert.Equal(500, first.Judge().BudgetMs)
        Assert.Equal(150, warm.Judge().BudgetMs)
    End Sub

    ' ------------------------------------------------------------------ the script mirrors it ----

    Private Shared Function ReadScript() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "tests", "Integration", "SettingsTimingScenes.ps1")
            If File.Exists(candidate) Then Return File.ReadAllText(candidate)
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("tests/Integration/SettingsTimingScenes.ps1 not found above " & AppContext.BaseDirectory)
    End Function

    <Fact>
    Public Sub The_hand_run_script_holds_the_same_sample_count_repeat_threshold_and_percentile()
        Dim script As String = ReadScript()
        Assert.Contains("$MinimumSamples = " & UiTimingStats.MinimumSamples.ToString(), script)
        Assert.Contains("$RepeatedOverBudgetCount = " & UiTimingStats.RepeatedOverBudgetCount.ToString(), script)
        Assert.Contains("$BudgetPercentile = " & UiTimingStats.BudgetPercentile.ToString(), script)
        Assert.Contains("function Get-NearestRankPercentile", script)
        Assert.Contains("function Write-TimingStats", script)
        For Each word As String In {"PASS", "OVER_BUDGET", "INSUFFICIENT"}
            Assert.Contains(word, script)
        Next
    End Sub

End Class
#End If
