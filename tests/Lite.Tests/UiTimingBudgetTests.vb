#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System.IO
Imports System.Linq
Imports Xunit

''' <summary>
''' WINDOWS-UI section 5 (amended 0.2) puts a budget on every page and dialog - 500 ms the first time a page is
''' shown, 150 ms when it was shown before, 500 ms for a dialog - and section 9 asks for every one of them to be
''' timed. This holds the pure half (kind, budget, verdict, the one log line) and where the measurement is hooked
''' (the dialog layer's two entries, the settings window's page switch and open), and that it is OFF without the
''' environment switch.
''' </summary>
Public Class UiTimingBudgetTests

    <Fact>
    Public Sub The_budget_is_the_one_of_the_amended_section_5()
        Assert.Equal(500, UiTimingBudget.BudgetMs(UiTimingKind.PageFirst))
        Assert.Equal(150, UiTimingBudget.BudgetMs(UiTimingKind.PageRepeat))
        Assert.Equal(500, UiTimingBudget.BudgetMs(UiTimingKind.Dialog))
    End Sub

    <Theory>
    <InlineData(UiTimingKind.PageRepeat, 0.0, UiTimingVerdict.Pass)>
    <InlineData(UiTimingKind.PageRepeat, 149.9, UiTimingVerdict.Pass)>
    <InlineData(UiTimingKind.PageRepeat, 150.0, UiTimingVerdict.Over)>      ' "under 150 ms" is strict
    <InlineData(UiTimingKind.PageRepeat, 151.0, UiTimingVerdict.Over)>
    <InlineData(UiTimingKind.PageFirst, 499.9, UiTimingVerdict.Pass)>
    <InlineData(UiTimingKind.PageFirst, 500.0, UiTimingVerdict.Over)>
    <InlineData(UiTimingKind.PageFirst, 200.0, UiTimingVerdict.Pass)>        ' a first visit may use the first-visit budget
    <InlineData(UiTimingKind.Dialog, 499.0, UiTimingVerdict.Pass)>
    <InlineData(UiTimingKind.Dialog, 612.0, UiTimingVerdict.Over)>
    Public Sub A_surface_is_judged_against_the_budget_of_its_own_kind(kind As UiTimingKind, elapsed As Double, expected As UiTimingVerdict)
        Assert.Equal(expected, UiTimingBudget.Judge(kind, elapsed))
    End Sub

    <Fact>
    Public Sub A_measurement_that_is_not_a_number_is_never_a_pass()
        Assert.Equal(UiTimingVerdict.Over, UiTimingBudget.Judge(UiTimingKind.Dialog, Double.NaN))
    End Sub

    <Fact>
    Public Sub The_log_line_reads_back_to_what_was_written()
        Dim line As String = UiTimingBudget.FormatLine("settings/audio", UiTimingKind.PageRepeat, 38.44)
        Assert.Equal("UI timing: page-repeat | settings/audio | 38.4 ms | budget 150 ms | PASS", line)

        Dim name As String = Nothing
        Dim kind As UiTimingKind
        Dim elapsed As Double
        Dim verdict As UiTimingVerdict
        ' The logger puts its own time stamp in front of the text.
        Assert.True(UiTimingBudget.TryParseLine("2026-10-13 18:00:01.123  " & line, name, kind, elapsed, verdict))
        Assert.Equal("settings/audio", name)
        Assert.Equal(UiTimingKind.PageRepeat, kind)
        Assert.Equal(38.4R, elapsed, 6)
        Assert.Equal(UiTimingVerdict.Pass, verdict)

        Dim over As String = UiTimingBudget.FormatLine("Ask_Dialog", UiTimingKind.Dialog, 731.0)
        Assert.EndsWith("| OVER", over)
        Assert.True(UiTimingBudget.TryParseLine(over, name, kind, elapsed, verdict))
        Assert.Equal(UiTimingVerdict.Over, verdict)
    End Sub

    <Fact>
    Public Sub A_line_that_is_not_ours_is_not_read_and_a_missing_name_is_marked()
        Dim name As String = Nothing
        Dim kind As UiTimingKind
        Dim elapsed As Double
        Dim verdict As UiTimingVerdict
        Assert.False(UiTimingBudget.TryParseLine("Reading audio tags: song.mp3", name, kind, elapsed, verdict))
        Assert.False(UiTimingBudget.TryParseLine("UI timing: page-first | settings/audio | soon | budget 500 ms | PASS", name, kind, elapsed, verdict))
        Assert.False(UiTimingBudget.TryParseLine("UI timing: wide | x | 1.0 ms | budget 500 ms | PASS", name, kind, elapsed, verdict))
        Assert.False(UiTimingBudget.TryParseLine(Nothing, name, kind, elapsed, verdict))
        Assert.Contains("(unnamed)", UiTimingBudget.FormatLine(String.Empty, UiTimingKind.Dialog, 1.0))
    End Sub

    <Fact>
    Public Sub Without_the_switch_nothing_is_measured_and_nothing_is_armed()
        If String.Equals(Environment.GetEnvironmentVariable(UiTiming.EnvironmentVariable), "1", StringComparison.Ordinal) Then Return
        Assert.False(UiTiming.Enabled)
        Assert.Equal(0L, UiTiming.Begin())
        ' A start of 0 is "the switch was off when the action began": no handler is attached, nothing throws.
        UiTiming.CompleteAtIdle("settings/audio", UiTimingKind.PageRepeat, 0L)
        UiTiming.WatchWindow(Nothing, 0L)
    End Sub

    ' ------------------------------------------------------------------ where it is hooked ----

    Private Shared Function ReadSource(name As String) As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", name)
            If File.Exists(candidate) Then Return File.ReadAllText(candidate)
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src/" & name & " not found above " & AppContext.BaseDirectory)
    End Function

    <Fact>
    Public Sub Every_secondary_window_is_timed_through_the_one_dialog_layer()
        Dim source As String = ReadSource("DialogLayer.vb")
        Dim dialog As String = source.Substring(source.IndexOf("Function ShowViewerDialog", StringComparison.Ordinal))
        dialog = dialog.Substring(0, dialog.IndexOf("End Function", StringComparison.Ordinal))
        Dim window As String = source.Substring(source.IndexOf("Sub ShowViewerWindow", StringComparison.Ordinal))
        window = window.Substring(0, window.IndexOf("End Sub", StringComparison.Ordinal))
        For Each body As String In {dialog, window}
            Dim begin As Integer = body.IndexOf("UiTiming.Begin()", StringComparison.Ordinal)
            Dim watch As Integer = body.IndexOf("UiTiming.WatchWindow(", StringComparison.Ordinal)
            Dim show As Integer = body.IndexOf(".Show", StringComparison.Ordinal)
            Assert.True(begin >= 0 AndAlso watch > begin AndAlso show > watch,
                        "the layer must start the clock on entry and watch the window before it is shown")
        Next
    End Sub

    <Fact>
    Public Sub The_settings_window_times_its_page_switch_and_its_opening()
        Dim layout As String = ReadSource("Table_Form.ModernLayout.vb")
        Assert.Contains("AddHandler Tab_Control.Selecting, AddressOf ModernSettingsTabSelecting", layout)
        Assert.Contains("UiTiming.CompleteAtIdle(""settings/"" & pageName, kind, pageSwitchStarted)", layout)
        Assert.Contains("UiTiming.CompleteAtIdle(""settings/window"", UiTimingKind.Dialog, OpenTimingStarted)", layout)
        Dim viewer As String = ReadSource("Main_Form.KeyboardInput.vb")
        Dim ensure As String = viewer.Substring(viewer.IndexOf("Friend Sub EnsureSettingsWindow()", StringComparison.Ordinal))
        ensure = ensure.Substring(0, ensure.IndexOf("End Sub", StringComparison.Ordinal))
        Assert.True(ensure.IndexOf("UiTiming.Begin()", StringComparison.Ordinal) < ensure.IndexOf("New Table_Form()", StringComparison.Ordinal),
                    "the open is timed from before the window is made")
    End Sub

    <Fact>
    Public Sub The_hand_run_scene_reads_the_line_this_class_writes()
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        Dim script As String = Nothing
        While dir IsNot Nothing AndAlso script Is Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "tests", "Integration", "SettingsTimingScenes.ps1")
            If File.Exists(candidate) Then script = File.ReadAllText(candidate)
            dir = dir.Parent
        End While
        Assert.NotNull(script)
        Assert.Contains(UiTimingBudget.LinePrefix, script)
        Assert.Contains("FMS_UI_TIMING", script)
        For Each budget As Integer In {UiTimingBudget.FirstVisitMs, UiTimingBudget.RepeatVisitMs, UiTimingBudget.DialogMs}
            Assert.Contains(budget.ToString(), script)
        Next
    End Sub

End Class
#End If
