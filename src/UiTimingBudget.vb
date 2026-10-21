#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System.Globalization

''' <summary>What is timed: the three kinds of surface WINDOWS-UI section 5 (amended 0.2) puts a budget on.</summary>
Public Enum UiTimingKind
    ''' <summary>A settings page the first time it is shown: it creates what it holds. 500 ms.</summary>
    PageFirst = 0
    ''' <summary>A settings page that was shown before. 150 ms.</summary>
    PageRepeat = 1
    ''' <summary>A dialog or a secondary window - the settings window itself included. 500 ms.</summary>
    Dialog = 2
End Enum

Public Enum UiTimingVerdict
    Pass = 0
    Over = 1
End Enum

''' <summary>
''' The budget of WINDOWS-UI section 5 (amended 0.2) and the one line that carries a measurement. Pure: no clock,
''' no window, no file - <see cref="UiTiming"/> measures, this decides and writes the line, and
''' <c>tests/Integration/SettingsTimingScenes.ps1</c> reads the same line back (<see cref="LinePrefix"/>).
'''
''' The figures are FileDO's measurements of 2026-10-06 and the catalog owner's to revise ("on the reference
''' hardware a page that was visited once appears in under 150 ms, and the first visit, which creates its controls,
''' in under 500 ms; a dialog appears in under 500 ms"). "Under" is strict: a page that took exactly 150 ms is over.
''' </summary>
Friend NotInheritable Class UiTimingBudget

    Private Sub New()
    End Sub

    Friend Const FirstVisitMs As Integer = 500
    Friend Const RepeatVisitMs As Integer = 150
    Friend Const DialogMs As Integer = 500

    ''' <summary>The first words of a measurement line in the log. The scene script and the test hold this value.</summary>
    Friend Const LinePrefix As String = "UI timing: "

    Friend Shared Function BudgetMs(kind As UiTimingKind) As Integer
        Select Case kind
            Case UiTimingKind.PageFirst : Return FirstVisitMs
            Case UiTimingKind.PageRepeat : Return RepeatVisitMs
            Case Else : Return DialogMs
        End Select
    End Function

    ''' <summary>The name of a kind in the log line: stable, never translated.</summary>
    Friend Shared Function KindName(kind As UiTimingKind) As String
        Select Case kind
            Case UiTimingKind.PageFirst : Return "page-first"
            Case UiTimingKind.PageRepeat : Return "page-repeat"
            Case Else : Return "dialog"
        End Select
    End Function

    Friend Shared Function Judge(kind As UiTimingKind, elapsedMs As Double) As UiTimingVerdict
        ' A measurement that is not a number cannot be reported as a pass.
        If Double.IsNaN(elapsedMs) OrElse elapsedMs >= BudgetMs(kind) Then Return UiTimingVerdict.Over
        Return UiTimingVerdict.Pass
    End Function

    ''' <summary>
    ''' <c>UI timing: page-first | settings/audio | 38.4 ms | budget 500 ms | PASS</c>. Only the surface's own
    ''' name goes in - a window's type or a page's id, never its caption, a path or a value.
    ''' </summary>
    Friend Shared Function FormatLine(name As String, kind As UiTimingKind, elapsedMs As Double) As String
        Return LinePrefix & KindName(kind) & " | " & If(String.IsNullOrEmpty(name), "(unnamed)", name) & " | " &
            elapsedMs.ToString("0.0", CultureInfo.InvariantCulture) & " ms | budget " &
            BudgetMs(kind).ToString(CultureInfo.InvariantCulture) & " ms | " &
            If(Judge(kind, elapsedMs) = UiTimingVerdict.Pass, "PASS", "OVER")
    End Function

    ''' <summary>Reads a line written by <see cref="FormatLine"/>; False for any other line.</summary>
    Friend Shared Function TryParseLine(line As String, ByRef name As String, ByRef kind As UiTimingKind,
                                        ByRef elapsedMs As Double, ByRef verdict As UiTimingVerdict) As Boolean
        If line Is Nothing Then Return False
        Dim at As Integer = line.IndexOf(LinePrefix, StringComparison.Ordinal)
        If at < 0 Then Return False
        Dim parts As String() = line.Substring(at + LinePrefix.Length).Split({" | "}, StringSplitOptions.None)
        If parts.Length <> 5 Then Return False
        Select Case parts(0)
            Case "page-first" : kind = UiTimingKind.PageFirst
            Case "page-repeat" : kind = UiTimingKind.PageRepeat
            Case "dialog" : kind = UiTimingKind.Dialog
            Case Else : Return False
        End Select
        name = parts(1)
        If Not parts(2).EndsWith(" ms", StringComparison.Ordinal) Then Return False
        If Not Double.TryParse(parts(2).Substring(0, parts(2).Length - 3), NumberStyles.Float, CultureInfo.InvariantCulture, elapsedMs) Then Return False
        Select Case parts(4).Trim()
            Case "PASS" : verdict = UiTimingVerdict.Pass
            Case "OVER" : verdict = UiTimingVerdict.Over
            Case Else : Return False
        End Select
        Return True
    End Function

End Class

''' <summary>What the sample statistics say about one destination and one kind of visit.</summary>
Public Enum UiTimingStatsVerdict
    ''' <summary>At least <see cref="UiTimingStats.MinimumSamples"/> samples, p95 strictly below the budget, no repeated over-budget delay.</summary>
    Pass = 0
    ''' <summary>The p95 is not below the budget, or the same destination was over budget repeatedly (masked by the percentile or not).</summary>
    OverBudget = 1
    ''' <summary>Fewer than <see cref="UiTimingStats.MinimumSamples"/> samples: nothing can be concluded either way.</summary>
    Insufficient = 2
End Enum

''' <summary>
''' The figures <see cref="UiTimingStats.Judge"/> computed: the counts, nearest-rank p95 and maximum over ALL recorded
''' samples (nothing is dropped), the verdict, and the samples that were not below the budget, in recorded order.
''' </summary>
Friend NotInheritable Class UiTimingStatsResult

    Friend Sub New(kind As UiTimingKind, budgetMs As Integer, count As Integer, p95Ms As Double, maxMs As Double,
                   overBudget As IReadOnlyList(Of Double), repeatedOverBudget As Boolean, verdict As UiTimingStatsVerdict)
        Me.Kind = kind
        Me.BudgetMs = budgetMs
        Me.Count = count
        Me.P95Ms = p95Ms
        Me.MaxMs = maxMs
        Me.OverBudgetSamples = overBudget
        Me.RepeatedOverBudget = repeatedOverBudget
        Me.Verdict = verdict
    End Sub

    Friend ReadOnly Property Kind As UiTimingKind
    Friend ReadOnly Property BudgetMs As Integer
    Friend ReadOnly Property Count As Integer
    ''' <summary>Nearest-rank p95 of every sample; NaN when there is none.</summary>
    Friend ReadOnly Property P95Ms As Double
    ''' <summary>The slowest sample; NaN when there is none.</summary>
    Friend ReadOnly Property MaxMs As Double
    ''' <summary>Every sample that was not strictly below the budget, in the order it was recorded. Never trimmed:
    ''' when <see cref="RepeatedOverBudget"/> is False these are the isolated outliers to investigate and report.</summary>
    Friend ReadOnly Property OverBudgetSamples As IReadOnlyList(Of Double)
    ''' <summary>True when <see cref="UiTimingStats.RepeatedOverBudgetCount"/> or more samples were over budget.</summary>
    Friend ReadOnly Property RepeatedOverBudget As Boolean
    Friend ReadOnly Property Verdict As UiTimingStatsVerdict

    ''' <summary>One or two over-budget samples: an outlier that is reported, not a failure of its own.</summary>
    Friend ReadOnly Property HasIsolatedOutliers As Boolean
        Get
            Return OverBudgetSamples.Count > 0 AndAlso Not RepeatedOverBudget
        End Get
    End Property

End Class

''' <summary>
''' A list of samples of ONE kind. Warm visits (<see cref="UiTimingKind.PageRepeat"/>) and first visits
''' (<see cref="UiTimingKind.PageFirst"/>) are judged against different budgets and must never share a list: a warm
''' sample counted as cold would hide a regression, a cold one counted as warm would invent one (rule 4: "do not
''' relabel warm samples cold"). <see cref="Add"/> therefore takes the kind of the sample and refuses a mismatch.
''' </summary>
Friend NotInheritable Class UiTimingSamples

    Private ReadOnly _values As New List(Of Double)()

    Friend Sub New(kind As UiTimingKind)
        Me.Kind = kind
    End Sub

    Friend ReadOnly Property Kind As UiTimingKind

    Friend ReadOnly Property Count As Integer
        Get
            Return _values.Count
        End Get
    End Property

    ''' <summary>Records one sample. A sample of another kind is refused with an exception - never dropped, never
    ''' filed under this kind.</summary>
    Friend Sub Add(sampleKind As UiTimingKind, elapsedMs As Double)
        If sampleKind <> Kind Then
            Throw New ArgumentException("A " & UiTimingBudget.KindName(sampleKind) & " sample cannot be added to the " &
                                        UiTimingBudget.KindName(Kind) & " list: warm and first-visit samples are never mixed.", NameOf(sampleKind))
        End If
        _values.Add(elapsedMs)
    End Sub

    ''' <summary>The samples in the order they were recorded.</summary>
    Friend Function Values() As Double()
        Return _values.ToArray()
    End Function

    Friend Function Judge() As UiTimingStatsResult
        Return UiTimingStats.Judge(Kind, _values)
    End Function

End Class

''' <summary>
''' The statistics of SETTINGS-NAVIGATION 2.0 section 5 rule 4, pure (no clock, no window, no file): p95 below the
''' budget over at least <see cref="MinimumSamples"/> samples per destination and kind, nearest-rank, over ALL recorded
''' samples, with the maximum reported too and no outlier dropped. The hand-run
''' <c>tests/Integration/SettingsTimingScenes.ps1</c> mirrors this arithmetic on the log lines; the tests hold both.
''' </summary>
Friend NotInheritable Class UiTimingStats

    Private Sub New()
    End Sub

    ''' <summary>Rule 4: "at least 20 warm transitions and 20 independent first-visit samples per baseline destination".
    ''' Fewer is <see cref="UiTimingStatsVerdict.Insufficient"/>.</summary>
    Friend Const MinimumSamples As Integer = 20

    ''' <summary>The percentile the budget is stated at (rule 4: "p95").</summary>
    Friend Const BudgetPercentile As Integer = 95

    ''' <summary>Rule 4: "repeated reproducible over-budget delays fail acceptance even if masked by the aggregate
    ''' percentile". The rule does not say how many is "repeated"; three samples of one destination not below the
    ''' budget is this product's reading (one or two are isolated outliers to investigate and report). A catalog
    ''' amendment that names a number replaces this one.</summary>
    Friend Const RepeatedOverBudgetCount As Integer = 3

    ''' <summary>The 1-based rank of the nearest-rank percentile: ceil(percent / 100 * count), in integers so that
    ''' 95 % of 20 is exactly 19 and never 19.000000000000004. 0 for an empty list.</summary>
    Friend Shared Function NearestRank(count As Integer, percent As Integer) As Integer
        If count <= 0 Then Return 0
        If percent < 1 Then percent = 1
        If percent > 100 Then percent = 100
        Return CInt((CLng(percent) * count + 99L) \ 100L)
    End Function

    ''' <summary>The nearest-rank percentile of the samples (any order, ties kept); NaN when there are none.</summary>
    Friend Shared Function Percentile(samples As IEnumerable(Of Double), percent As Integer) As Double
        Dim sorted As List(Of Double) = Normalised(samples)
        If sorted.Count = 0 Then Return Double.NaN
        sorted.Sort()
        Return sorted(NearestRank(sorted.Count, percent) - 1)
    End Function

    ''' <summary>The slowest sample; NaN when there are none.</summary>
    Friend Shared Function Maximum(samples As IEnumerable(Of Double)) As Double
        Dim values As List(Of Double) = Normalised(samples)
        If values.Count = 0 Then Return Double.NaN
        Dim result As Double = values(0)
        For Each v As Double In values
            If v > result Then result = v
        Next
        Return result
    End Function

    ''' <summary>
    ''' Judges the samples of one destination and one kind against the budget of that kind. Fewer than
    ''' <see cref="MinimumSamples"/> is Insufficient; otherwise OverBudget when the p95 is not strictly below the budget
    ''' or when <see cref="RepeatedOverBudgetCount"/> or more samples were over it; otherwise Pass. A sample that is
    ''' not a number counts as infinitely slow: a broken measurement is never a pass and never dropped.
    ''' </summary>
    Friend Shared Function Judge(kind As UiTimingKind, samples As IEnumerable(Of Double)) As UiTimingStatsResult
        Dim values As List(Of Double) = Normalised(samples)
        Dim budget As Integer = UiTimingBudget.BudgetMs(kind)
        Dim over As New List(Of Double)()
        For Each v As Double In values
            If Not (v < budget) Then over.Add(v)
        Next
        Dim p95 As Double = Percentile(values, BudgetPercentile)
        Dim repeated As Boolean = over.Count >= RepeatedOverBudgetCount
        Dim verdict As UiTimingStatsVerdict
        If values.Count < MinimumSamples Then
            verdict = UiTimingStatsVerdict.Insufficient
        ElseIf repeated OrElse Not (p95 < budget) Then
            verdict = UiTimingStatsVerdict.OverBudget
        Else
            verdict = UiTimingStatsVerdict.Pass
        End If
        Return New UiTimingStatsResult(kind, budget, values.Count, p95, Maximum(values), over, repeated, verdict)
    End Function

    ''' <summary>The words the hand-run script and the log use for a verdict.</summary>
    Friend Shared Function VerdictName(verdict As UiTimingStatsVerdict) As String
        Select Case verdict
            Case UiTimingStatsVerdict.Pass : Return "PASS"
            Case UiTimingStatsVerdict.OverBudget : Return "OVER_BUDGET"
            Case Else : Return "INSUFFICIENT"
        End Select
    End Function

    Private Shared Function Normalised(samples As IEnumerable(Of Double)) As List(Of Double)
        Dim result As New List(Of Double)()
        If samples Is Nothing Then Return result
        For Each v As Double In samples
            result.Add(If(Double.IsNaN(v), Double.PositiveInfinity, v))
        Next
        Return result
    End Function

End Class
#End If
