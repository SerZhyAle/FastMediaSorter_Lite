Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Xunit

''' <summary>
''' APP-BEHAVIOUR gates for the viewer (028_SPECIFICATION_CONTRACT_SYNC_DESKTOP_APP_UX.md §5).
''' Source reads, like LocalizationCoverageTests and SettingsLayoutTests - no form is built.
''' Runs on both legs; the sources are the same files either way.
''' </summary>
Public Class DesktopUxConformanceTests

    ''' <summary>
    ''' Uses of an exception's Message outside a log call that are allowed, each with the
    ''' reason it never reaches the user. A new entry needs a reason as good as these;
    ''' "it is only shown in one place" is not one - that is how the text travels.
    ''' </summary>
    Private Shared ReadOnly Allowed As (File As String, Fragment As String, Reason As String)() = {
        ("FailureText.vb", "ex.Message",
         "the one sanctioned place, and only on the x86 fallback (frozen, dated exception)"),
        ("AppFileLogger.vb", ".Message &",
         "the logger itself, composing the line it writes to current.log"),
        ("Main_Form.OcrTranslate.vb", "ocrResult.Message =",
         "not an exception: the engine's own status token, compared, never shown"),
        ("Main_Form.FileScanning.vb", "bgWorker_Result = ",
         "a trace token compared against LOADED/SKIPED and written only to Debug (w0230)"),
        ("Main_Form.Lifecycle.vb", "probe.LastError",
         "the argument probe's reason, written only to Debug (w0280)"),
        ("Main_Form.SlotHealth.vb", "SlotVerdict.ForState(",
         "a verdict's Detail, written only to the log; the user reads SlotHealthText"),
        ("TesseractOcrEngine.vb", ".IndexOf(",
         "classifies a native load failure; the text is compared, never shown"),
        ("LogPackage.vb", "unreadable: ",
         "a line inside the diagnostic report the user is sending - the report is the log"),
        ("AnimationToVideo.vb", "run.Detail = ex.Message",
         "the encoder run's Detail is logged by DescribeFailure, never shown")
    }

    <Fact>
    Public Sub NoExceptionTextReachesTheUser()
        Dim srcDir = FindSourceDir()
        Assert.True(srcDir IsNot Nothing, "Could not locate the src\ directory from the test binary.")

        Dim leaks As New List(Of String)()
        Dim scanned = 0
        For Each srcPath In Directory.GetFiles(srcDir, "*.vb", SearchOption.AllDirectories)
            If srcPath.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            If srcPath.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            ' The Share Manager has its own gate in Companion.Tests.
            If srcPath.IndexOf("\FastMediaSorterCompanion\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            scanned += 1
            Dim name = Path.GetFileName(srcPath)
            For Each hit In ExceptionTextScanner.Scan(File.ReadAllText(srcPath))
                If Allowed.Any(Function(a) a.File = name AndAlso hit.Text.Contains(a.Fragment)) Then Continue For
                leaks.Add(name & ":" & hit.Line & "  " & hit.Text)
            Next
        Next

        Assert.True(scanned > 100, "Only " & scanned & " source files scanned.")
        Assert.True(leaks.Count = 0,
                    "An exception's Message outside a log call (APP-BEHAVIOUR rule 6) - log the exception " &
                    "and show FailureText.Describe(ex) instead:" & vbLf & String.Join(vbLf, leaks))
    End Sub

    ''' <summary>The gate can only fail if it catches something: a seeded message box is a hit.</summary>
    <Fact>
    Public Sub The_rule6_scanner_catches_a_message_box_and_a_status_line()
        Dim hits = ExceptionTextScanner.Scan(
            "MessageBox.Show(Me, ex.Message)" & vbLf &
            "lbl_Status.Text = Localization.TF(""Failed: {0}"", failure.InnerException.Message)" & vbLf &
            "reason = e.Error.Message")
        Assert.Equal(3, hits.Count)
    End Sub

    ''' <summary>A log call - one line or several - passes; so do comments, strings and MessageBox.</summary>
    <Fact>
    Public Sub The_rule6_scanner_passes_log_calls_comments_and_strings()
        Dim hits = ExceptionTextScanner.Scan(
            "Debug.WriteLine(""x: "" & ex.Message)" & vbLf &
            "AppFileLogger.WriteLine(""Archive: "" &" & vbLf &
            "                        dir & "" - "" & ex.Message)" & vbLf &
            "' lbl.Text = ex.Message in a comment" & vbLf &
            "Dim s = ""ex.Message in a string""" & vbLf &
            "MessageBox.Show(Me, FailureText.Describe(ex))")
        Assert.Empty(hits)
    End Sub

    ''' <summary>
    ''' APP-BEHAVIOUR rule 9 (§5 item 6): a file that captions a control with a private-use
    ''' or MDL2 codepoint - a glyph a screen reader reads as nothing - also names it.
    ''' </summary>
    <Fact>
    Public Sub Every_file_with_a_glyph_caption_sets_an_accessible_name()
        Dim srcDir = FindSourceDir()
        Dim glyph As New Text.RegularExpressions.Regex("ChrW\(&H[EF][0-9A-Fa-f]{3}\)|""[-]")
        Dim unnamed As New List(Of String)()
        Dim withGlyphs = 0
        For Each srcPath In Directory.GetFiles(srcDir, "*.vb", SearchOption.AllDirectories)
            If srcPath.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            Dim text = File.ReadAllText(srcPath)
            If Not glyph.IsMatch(text) Then Continue For
            withGlyphs += 1
            If Not text.Contains(".AccessibleName =") AndAlso Not text.Contains(".AccessibleName = ") Then
                unnamed.Add(Path.GetFileName(srcPath))
            End If
        Next
        Assert.True(withGlyphs > 0, "No glyph caption found at all - the scanner is not looking.")
        Assert.True(unnamed.Count = 0, "Glyph captions with no AccessibleName in the same file: " & String.Join(", ", unnamed))
    End Sub

    ''' <summary>
    ''' Rule 9 for the toolbar (§1.1a): every button on it has words - an IconMap meaning
    ''' or an OverflowText case - and ApplyToolbarAccessibleNames puts them on the button
    ''' on every layout and language change.
    ''' </summary>
    <Fact>
    Public Sub Every_toolbar_button_has_words_and_they_are_applied_on_a_language_change()
        Dim srcDir = FindSourceDir()
        Dim layout = File.ReadAllText(Path.Combine(srcDir, "Main_Form.ModernLayout.vb"))
        Dim overflow = File.ReadAllText(Path.Combine(srcDir, "Main_Form.ToolbarOverflow.vb"))
        Dim iconMap = File.ReadAllText(Path.Combine(srcDir, "Icons", "IconMap.vb"))
        Dim iconography = File.ReadAllText(Path.Combine(srcDir, "Main_Form.Iconography.vb"))
        Dim localization = File.ReadAllText(Path.Combine(srcDir, "Main_Form.Localization.vb"))

        Dim order = Text.RegularExpressions.Regex.Match(layout, "toolbar_Order As Control\(\) = \{(?<list>[^}]*)\}")
        Assert.True(order.Success, "toolbar_Order not found in Main_Form.ModernLayout.vb.")
        Dim buttons = order.Groups("list").Value.Split(","c).Select(Function(s) s.Trim()).
            Where(Function(n) n.StartsWith("btn_", StringComparison.Ordinal) OrElse n.StartsWith("bt_", StringComparison.Ordinal)).ToList()
        Assert.True(buttons.Count > 10, "Only " & buttons.Count & " toolbar buttons parsed.")

        Dim wordless = buttons.Where(Function(n) Not overflow.Contains("Case """ & n & """") AndAlso
                                                 Not iconMap.Contains("{""" & n & """")).ToList()
        Assert.True(wordless.Count = 0, "Toolbar buttons with no words for a screen reader: " & String.Join(", ", wordless))

        Assert.Contains("ApplyToolbarAccessibleNames()", iconography)
        Assert.Contains("ApplyCatalogToolbarGlyphs()", localization)
    End Sub

    Private Shared Function FindSourceDir() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Common_Module.vb")) Then
                Return candidate
            End If
            dir = dir.Parent
        End While
        Return Nothing
    End Function

End Class
