Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' L0303 section 7.2, done criterion 1: every colour a floating panel or its ink paints comes from the panel
''' colour model. The gate reads the mainline lines of the panel files (sections 4.1-4.2; a
''' <c>#If NETFRAMEWORK</c> branch is the x86 fallback's and is left as it was) and fails on another
''' black-or-white rule, on a colour literal outside the two declared tables (section 6.9), on the retired
''' 220 machinery, on a popup that is not themed like a menu, and on a luma override in RecolorChrome.
''' Pure file reading, so it runs on both targets.
''' </summary>
Public Class PanelColourSourceTests

    ''' <summary>The files of sections 4.1-4.2 that paint a floating panel or its ink.</summary>
    Private Shared ReadOnly PanelFiles As String() = {
        "Chrome\ChromeTheme.vb", "Chrome\ChromeMetrics.vb", "Chrome\ChromeHost.vb", "Chrome\ChromeSurface.vb",
        "Chrome\ChromePathBox.vb", "Chrome\ChromeEditBox.vb", "Chrome\ChromeMenu.vb",
        "Main_Form.Chrome.vb", "Main_Form.VideoControls.vb", "Main_Form.RecipientsOverlay.vb",
        "Main_Form.ModernLayout.vb", "Image_Panel_Form.Modern.vb", "DialogRules.vb"}

    ''' <summary>The two places a panel colour number may be written (section 6.9).</summary>
    Private Shared ReadOnly DeclaredTables As String() = {"Chrome\PanelColourModel.vb", "Theming\RecipientPalette.vb"}

    Private Shared ReadOnly LumaThreshold As New Regex("0\.299\s*\*", RegexOptions.Compiled)
    Private Shared ReadOnly ChannelSum As New Regex("\.R\)?\s*\+\s*(CInt\()?\w*\.?G\)?\s*\+\s*(CInt\()?\w*\.?B\b", RegexOptions.Compiled)
    Private Shared ReadOnly HalfLuma As New Regex("(?i)(luma|luminance)\w*(\([^)]*\))?\s*>\s*0\.5\b", RegexOptions.Compiled)
    Private Shared ReadOnly ColourLiteral As New Regex("Color\.FromArgb\(\s*\d+\s*(,\s*\d+\s*){2,3}\)", RegexOptions.Compiled)

    <Fact>
    Public Sub No_other_black_or_white_rule_in_the_panel_files()
        Dim faults As New List(Of String)()
        For Each name As String In PanelFiles
            For Each line As KeyValuePair(Of Integer, String) In MainlineLines(name)
                If LumaThreshold.IsMatch(line.Value) OrElse ChannelSum.IsMatch(line.Value) OrElse HalfLuma.IsMatch(line.Value) Then
                    faults.Add(name & ":" & line.Key.ToString() & " " & line.Value.Trim())
                End If
            Next
        Next
        Assert.True(faults.Count = 0, "R-1: a black-or-white rule other than WCAG luminance: " & String.Join(" ; ", faults))
    End Sub

    <Fact>
    Public Sub No_colour_literal_in_the_panel_files_outside_the_declared_tables()
        Dim faults As New List(Of String)()
        For Each name As String In PanelFiles
            For Each line As KeyValuePair(Of Integer, String) In MainlineLines(name)
                If ColourLiteral.IsMatch(line.Value) Then faults.Add(name & ":" & line.Key.ToString() & " " & line.Value.Trim())
            Next
        Next
        Assert.True(faults.Count = 0, "Section 6.9: a colour literal outside PanelColourMetrics / RecipientPalette: " & String.Join(" ; ", faults))
        ' The declared tables exist and do hold the numbers.
        For Each name As String In DeclaredTables
            Assert.Matches(ColourLiteral, File.ReadAllText(Path.Combine(SourceDir(), name)))
        Next
    End Sub

    <Fact>
    Public Sub The_retired_220_machinery_and_the_ring_are_gone_from_the_source()
        Dim retired As New Regex("\b(WithInvertedInk|InvertsInk|InkInvertBelowPercent|ChromeInkOutline)\b")
        Dim faults As New List(Of String)()
        For Each sourceFile As String In Directory.GetFiles(SourceDir(), "*.vb", SearchOption.AllDirectories)
            Dim text As String = File.ReadAllText(sourceFile)
            Dim hit As Match = retired.Match(text)
            If hit.Success Then faults.Add(Path.GetFileName(sourceFile) & " " & hit.Value)
        Next
        Assert.True(faults.Count = 0, "Q303-3 / Q303-4: " & String.Join(" ; ", faults))
    End Sub

    <Fact>
    Public Sub Every_chrome_popup_is_themed_like_a_menu()
        ' R-13 (APP-STYLE section 5 condition 2): a popup list is a menu - opaque, the application's theme,
        ' never the content-tinted panel theme.
        Dim caller As New Regex("ChromeMenu\.ShowPopup\(([^\r\n]*(\r?\n[^\r\n]*){0,2})")
        Dim count As Integer
        For Each sourceFile As String In Directory.GetFiles(SourceDir(), "*.vb", SearchOption.AllDirectories)
            Dim text As String = File.ReadAllText(sourceFile)
            For Each call_Site As Match In caller.Matches(text)
                count += 1
                Dim args As String = call_Site.Groups(1).Value
                Dim themed As Boolean = args.Contains("ChromeTheme.ForMenu(") OrElse args.Contains("menuTheme") OrElse
                                        Regex.IsMatch(args, ",\s*menu\s*,")
                Assert.True(themed, Path.GetFileName(sourceFile) & ": a popup without a ForMenu theme: " & args.Trim())
            Next
        Next
        Assert.True(count >= 6, "expected the chrome's popups, found " & count.ToString())

        ' The two that took the panels' colours (section 4.2) take the menu theme now.
        Dim pathBox As String = File.ReadAllText(Path.Combine(SourceDir(), "Chrome", "ChromePathBox.vb"))
        Assert.Matches("If menu Is Nothing Then menu = ChromeTheme\.ForMenu\(", pathBox)
        Assert.DoesNotMatch("ChromeMenu\.ShowPopup\([^\r\n]*,\s*theme\s*,", pathBox)
        Dim chrome As String = File.ReadAllText(Path.Combine(SourceDir(), "Main_Form.Chrome.vb"))
        Assert.Matches("chrome_Path\.MenuTheme = Function\(\) ChromeTheme\.ForMenu\(ThemeService\.Palette\)", chrome)
        Dim resources As String = File.ReadAllText(Path.Combine(SourceDir(), "Resource_List_Panel.vb"))
        Assert.Matches("menuTheme = ChromeTheme\.ForMenu\(", resources)
        Assert.DoesNotMatch("menuTheme = ChromeTheme\.FromScheme\(", resources)
    End Sub

    <Fact>
    Public Sub RecolorChrome_passes_the_sample_unaltered()
        Dim body As String = MainlineBody("Main_Form.ModernLayout.vb", "Friend Sub RecolorChrome(")
        Assert.DoesNotMatch("IsDarkColor", body)
        Assert.Matches("Dim effectiveOpposite As Color = opposite_Color", body)
        Dim layout As String = File.ReadAllText(Path.Combine(SourceDir(), "Main_Form.ModernLayout.vb"))
        Assert.Matches("ChromeApplyScheme\(back_Color,\s*Form_Color_Scheme\)", layout)
    End Sub

    <Fact>
    Public Sub The_thumbnail_panel_takes_the_wcag_better_ink()
        Dim panel As String = File.ReadAllText(Path.Combine(SourceDir(), "Image_Panel_Form.Modern.vb"))
        Assert.Matches("Dim ink As Color = PanelColourModel\.ContentInk\(back\)", panel)
        Assert.Matches("\.ForeColor = PanelColourModel\.ContentInk\(band_Ink\)", panel)
        Assert.DoesNotMatch("LuminanceOf", panel)
    End Sub

    ' --- the reader ------------------------------------------------------------------------------------

    ''' <summary>The lines compiled on the mainline (NETFRAMEWORK undefined), with their 1-based numbers; a
    ''' comment line is skipped.</summary>
    Private Shared Function MainlineLines(name As String) As List(Of KeyValuePair(Of Integer, String))
        Dim result As New List(Of KeyValuePair(Of Integer, String))()
        Dim active As New List(Of Boolean)()
        Dim lines As String() = File.ReadAllLines(Path.Combine(SourceDir(), name))
        For i As Integer = 0 To lines.Length - 1
            Dim t As String = lines(i).Trim()
            Dim directive As Match = Regex.Match(t, "^#If\s+(Not\s+)?NETFRAMEWORK\s+Then\b")
            If directive.Success Then
                active.Add(directive.Groups(1).Success)
                Continue For
            End If
            If t.StartsWith("#If ", StringComparison.Ordinal) Then
                active.Add(True)
                Continue For
            End If
            If t.StartsWith("#Else", StringComparison.Ordinal) Then
                active(active.Count - 1) = Not active(active.Count - 1)
                Continue For
            End If
            If t.StartsWith("#End If", StringComparison.Ordinal) Then
                active.RemoveAt(active.Count - 1)
                Continue For
            End If
            If active.Contains(False) OrElse t.StartsWith("'", StringComparison.Ordinal) Then Continue For
            result.Add(New KeyValuePair(Of Integer, String)(i + 1, lines(i)))
        Next
        Return result
    End Function

    ''' <summary>The mainline lines of one method, from its signature to its End Sub.</summary>
    Private Shared Function MainlineBody(name As String, signature As String) As String
        Dim collecting As Boolean
        Dim body As New System.Text.StringBuilder()
        For Each line As KeyValuePair(Of Integer, String) In MainlineLines(name)
            If Not collecting AndAlso line.Value.Contains(signature) Then collecting = True
            If collecting Then
                body.AppendLine(line.Value)
                If line.Value.Trim() = "End Sub" Then Exit For
            End If
        Next
        Assert.True(body.Length > 0, signature & " not found in " & name)
        Return body.ToString()
    End Function

    Private Shared Function SourceDir() As String
        For Each start As String In {AppContext.BaseDirectory, Environment.CurrentDirectory}
            Dim dir As New DirectoryInfo(start)
            While dir IsNot Nothing
                Dim candidate As String = Path.Combine(dir.FullName, "src")
                If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Common_Module.vb")) Then Return candidate
                dir = dir.Parent
            End While
        Next
        Throw New DirectoryNotFoundException("Could not locate the src\ directory from the test binary.")
    End Function

End Class
