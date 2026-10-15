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
#End If
