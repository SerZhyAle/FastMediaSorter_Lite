#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' The source-scan gate of SPECIFICATION 127 §3.5: every visible ink/fill pair of the
''' settings window comes from the settings palette. The gate walks the settings sources
''' and fails on colour literals - <c>Color.FromArgb(</c>, <c>Color.Black</c>,
''' <c>Color.White</c>, <c>Color.Gainsboro</c>, <c>SystemColors.</c> - outside the
''' palette loader and its blend helpers.
'''
''' Like SettingsLayoutTests, this reads the sources rather than building the form:
''' Table_Form drags in LibVLC and Tesseract, and a literal planted tomorrow morning
''' must fail at noon, not at the next manual screenshot run. The net48 fallback
''' branches of the shared partials are out of scope (the fallback keeps its existing
''' UI under the maintenance policy of CLAUDE.md), so <c>#If NETFRAMEWORK</c> regions
''' are skipped and <c>#If Not NETFRAMEWORK</c> regions are scanned.
''' </summary>
Public Class SettingsColorGateTests

    Private Shared ReadOnly LiteralPattern As New Regex(
        "Color\.FromArgb\(|Color\.Black\b|Color\.White\b|Color\.Gainsboro\b|SystemColors\.", RegexOptions.Compiled)

    ''' <summary>The palette loader owns every literal; the blend helpers do the
    ''' arithmetic. They live in Table_Form.ModernLayout.vb and SettingsCheckBox.vb.</summary>
    Private Shared ReadOnly AllowedMethods As String() =
        {"LoadSystemSettingsPalette", "BlendSettingsColor", "ScaleSettingsColor", "BlendSettingsInk",
         "LoadSettingsNavIdentityInks", "ResolveInk"} ' Palette-owned contrast arithmetic (WINDOWS-UI section 6).

    ''' <summary>Reviewed allow-list. The Designer file is generated; its one GrayText
    ''' label is recoloured by the runtime palette walker on every theme pass.</summary>
    Private Shared ReadOnly AllowedFiles As String() = {"Table_Form.Designer.vb"}

    <Fact>
    Public Sub Settings_sources_carry_no_colour_literals_outside_the_palette()
        Dim violations As New List(Of String)()
        For Each source As String In SettingsSources()
            Dim name As String = Path.GetFileName(source)
            If AllowedFiles.Contains(name) Then Continue For
            For Each finding In Scan(File.ReadAllText(source))
                violations.Add(name & ":" & finding.Line & "  " & finding.Match)
            Next
        Next

        Assert.True(violations.Count = 0,
                    "Colour literals outside the settings palette (" & violations.Count & "):" & vbLf &
                    String.Join(vbLf, violations.Take(40)))
    End Sub

    <Fact>
    Public Sub The_gate_scans_a_real_body_of_sources()
        Dim sources As String() = SettingsSources()
        Assert.True(sources.Length >= 8, "Only " & sources.Length & " settings sources found - the glob is missing files.")
        Assert.Contains(sources, Function(f) Path.GetFileName(f) = "Table_Form.ModernLayout.vb")
        Assert.Contains(sources, Function(f) Path.GetFileName(f) = "SettingsCheckBox.vb")
        Assert.Contains(sources, Function(f) Path.GetFileName(f) = "SettingsCollapsibleGroup.vb")
    End Sub

    <Fact>
    Public Sub A_planted_literal_fails_and_the_allow_list_passes()
        ' The scanner can fail: a planted literal outside the loader is reported.
        Dim planted As String =
            "Private Sub PaintSomething()" & vbLf &
            "    control.BackColor = Color.FromArgb(3, 3, 3)" & vbLf &
            "End Sub"
        Assert.Single(Scan(planted))

        ' The palette loader and its blend helpers are exactly the allowed places.
        Dim loader As String =
            "Private Sub LoadSystemSettingsPalette()" & vbLf &
            "    SettingsCanvas = Color.FromArgb(30, 31, 34)" & vbLf &
            "End Sub"
        Assert.Empty(Scan(loader))
        Dim blend As String =
            "Private Shared Function BlendSettingsColor(baseColor As Color, overlay As Color, amount As Single) As Color" & vbLf &
            "    Return Color.FromArgb(1, 2, 3)" & vbLf &
            "End Function"
        Assert.Empty(Scan(blend))
    End Sub

    <Fact>
    Public Sub The_net48_fallback_branches_are_out_of_scope()
        Dim mixed As String =
            "#If NETFRAMEWORK Then" & vbLf &
            "cell.Style.BackColor = Color.FromArgb(255, 248, 220)" & vbLf &
            "#Else" & vbLf &
            "cell.Style.BackColor = BlendSettingsColor(SettingsAccent, SettingsSurface, 0.2F)" & vbLf &
            "#End If"
        Assert.Empty(Scan(mixed))
    End Sub

    ' ------------------------------------------------------------------ scanner ----

    Private NotInheritable Class Finding
        Public Line As Integer
        Public Match As String
    End Class

    Private Shared Iterator Function Scan(sourceText As String) As IEnumerable(Of Finding)
        Dim text As String = LocalizationCoverageTests.BlankComments(sourceText)
        Dim lines As String() = text.Split({vbCr, vbLf}, StringSplitOptions.None)

        ' Conditional-compilation state: which #If levels currently skip their content.
        Dim stack As New List(Of Boolean)()
        Dim allowedMethod As Boolean = False

        For index As Integer = 0 To lines.Length - 1
            Dim trimmed As String = lines(index).Trim()

            If trimmed.StartsWith("#If", StringComparison.Ordinal) Then
                stack.Add(AnySkipped(stack) OrElse IsNetFrameworkOnlyBranch(trimmed))
                Continue For
            ElseIf trimmed.StartsWith("#ElseIf", StringComparison.Ordinal) Then
                Continue For
            ElseIf trimmed = "#Else" OrElse trimmed.StartsWith("#Else ", StringComparison.Ordinal) Then
                If stack.Count > 0 Then
                    Dim current As Boolean = stack(stack.Count - 1)
                    stack.RemoveAt(stack.Count - 1)
                    stack.Add(If(AnySkipped(stack), True, Not current))
                End If
                Continue For
            ElseIf trimmed.StartsWith("#End", StringComparison.Ordinal) Then
                If stack.Count > 0 Then stack.RemoveAt(stack.Count - 1)
                Continue For
            End If

            If AnySkipped(stack) Then Continue For

            If Not allowedMethod Then
                Dim method As String = AllowedMethods.FirstOrDefault(
                    Function(candidate) Regex.IsMatch(trimmed, "\b(Sub|Function)\s+" & Regex.Escape(candidate) & "\b"))
                If method IsNot Nothing Then
                    allowedMethod = True
                    Continue For
                End If
            ElseIf trimmed = "End Sub" OrElse trimmed = "End Function" Then
                allowedMethod = False
                Continue For
            End If

            If allowedMethod Then Continue For

            For Each m As Match In LiteralPattern.Matches(lines(index))
                Yield New Finding With {.Line = index + 1, .Match = m.Value}
            Next
        Next
    End Function

    Private Shared Function AnySkipped(stack As List(Of Boolean)) As Boolean
        Return stack.Any(Function(level) level)
    End Function

    Private Shared Function IsNetFrameworkOnlyBranch(directive As String) As Boolean
        Return directive.Contains("NETFRAMEWORK") AndAlso Not directive.Contains("Not NETFRAMEWORK")
    End Function

    ''' <summary>The settings sources of §3.5: the Table_Form partials and the Settings*
    ''' sources, including the row grid and the drawn checkbox.</summary>
    Private Shared Function SettingsSources() As String()
        Return Directory.GetFiles(SourceDir(), "*.vb", SearchOption.TopDirectoryOnly).
            Where(Function(f)
                      Dim name As String = Path.GetFileName(f)
                      Return name.StartsWith("Table_Form.", StringComparison.Ordinal) OrElse
                             name.StartsWith("Settings", StringComparison.Ordinal)
                  End Function).
            OrderBy(Function(f) f).ToArray()
    End Function

    Private Shared Function SourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Common_Module.vb")) Then
                Return candidate
            End If
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("Could not locate the src\ directory from the test binary.")
    End Function
End Class
#End If
