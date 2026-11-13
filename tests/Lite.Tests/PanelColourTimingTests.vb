#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' L0303 M-4 (b): the line of one colour step is exactly what tests/Integration/PanelColourTimingStats.ps1 reads,
''' and the measurement stays silent while FMS_UI_TIMING is not set.
''' </summary>
Public Class PanelColourTimingTests

    ' The reader's pattern, copied from PanelColourTimingStats.ps1 (Get-PanelColourStats).
    Private Shared ReadOnly ReaderPattern As New Regex("Panel colour timing: ([0-9]+(?:\.[0-9]+)?) ms \| layout=(true|false)\s*$")

    <Fact>
    Public Sub The_line_matches_the_statistics_reader()
        Dim line As String = PanelColourTiming.FormatLine(1.23456R, True)
        Assert.Equal("Panel colour timing: 1.235 ms | layout=true", line)
        Assert.Matches(ReaderPattern, line)
        Assert.Matches(ReaderPattern, PanelColourTiming.FormatLine(1234.5R, False))
    End Sub

    <Fact>
    Public Sub A_bad_value_still_writes_a_readable_line()
        Assert.Matches(ReaderPattern, PanelColourTiming.FormatLine(Double.NaN, False))
        Assert.Matches(ReaderPattern, PanelColourTiming.FormatLine(-1.0R, False))
    End Sub

    <Fact>
    Public Sub Off_reports_nothing()
        If UiTiming.Enabled Then Return ' a test host started with the switch on measures; nothing to hold here
        Dim layouts As Integer = -1
        Assert.Equal(0L, PanelColourTiming.Begin(layouts))
        Assert.Equal(-1, layouts)
        PanelColourTiming.Complete(0, 0)
    End Sub

End Class
#End If
