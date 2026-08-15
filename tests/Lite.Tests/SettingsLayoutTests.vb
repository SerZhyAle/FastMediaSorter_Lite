#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' The layout test of SPECIFICATION_THIRTEEN_UI_LANGUAGES §7.2: build the settings
''' window's rows on each of the 13 languages and fail when a caption needs more room
''' than its control was given.
'''
''' It exists because the two tests that already guard the 4 300 translated values
''' deliberately cannot see geometry. LocalizationParityTests proves every key has all
''' twelve translations with matching placeholder arity; LocalizationCoverageTests proves
''' no Russian literal bypasses the layer. Both stay green while a German caption is
''' silently clipped in half - the row title has no AutoEllipsis and no tooltip, so an
''' overflow shows as a truncated word, not as an error.
'''
''' Modern-only, exactly like the window it measures: Table_Form.ModernLayout.vb and
''' Table_Form.ExpandedSettings.vb are whole-file "#If Not NETFRAMEWORK", and on the net48
''' leg Localization.Codes is cut to RU+EN anyway.
'''
''' WHY IT READS THE SOURCES instead of building the form: Table_Form is a partial class
''' over a Designer file that reaches into Main_Form (GetModernPreferences), LibVLC and
''' Tesseract - the whole dependency tree this test project exists to stay out of (see the
''' comment at the top of Lite.Tests.vbproj). The rows are declared by call sites regular
''' enough to read exactly - AddSettingRow carries its editor width as a literal - and the
''' arithmetic those widths feed is mirrored below, next to the line it is copied from.
''' The precedent is LocalizationCoverageTests, which scans the same tree for the same
''' reason.
''' </summary>
Public Class SettingsLayoutTests

    ' --- geometry, copied from Table_Form.ModernLayout.vb -------------------------
    '
    ' ResizeModernPageItems:
    '     width = Math.Max(460, flow.ClientSize.Width - flow.Padding.Horizontal - 18)
    '     useTwoColumns = width >= 820
    '     child.Width = If(compact AndAlso useTwoColumns, Math.Max(300, (width - 8) \ 2), width)
    '
    ' So a "modern:compact" row is at its narrowest exactly at the two-column threshold -
    ' one pixel more flow and it is cut in half - and a "modern:full" row is at its
    ' narrowest at the single-column floor. Those two numbers are the worst case a caption
    ' has to survive; anything wider only helps.

    ''' <summary>(820 - 8) \ 2 - a compact row at the moment two columns switch on.</summary>
    Private Const NarrowestCompactHost As Integer = 406

    ''' <summary>Math.Max(460, ..) - a full-width row at the single-column floor.</summary>
    Private Const NarrowestFullHost As Integer = 460

    ''' <summary>
    ''' AddSettingRow, the branch for an editor that sits on the right (combo, numeric,
    ''' text box, button). All three lines matter and the order does too - the editor is
    ''' SHRUNK first, and its left edge is then derived from the shrunk width, so a narrow
    ''' row gives the caption more room than the declared editor width suggests:
    '''     editor.Width = Math.Min(editorWidth, Math.Max(150, host.ClientSize.Width - 260))
    '''     editor.Left  = Math.Max(230, Math.Min(420, host.ClientSize.Width - editor.Width - 12))
    '''     labelWidth   = Math.Max(180, editor.Left - 28)
    '''
    ''' Worth knowing before widening a caption: in the band where the editor is being
    ''' shrunk, editor.Left is hostWidth - (hostWidth - 260) - 12 = 248, i.e. CONSTANT, so
    ''' the caption gets a flat 220 px no matter how much wider the row grows - until the
    ''' editor reaches its declared width and the row starts paying out again. There is no
    ''' "just make the window bigger" for a caption that does not fit here.
    ''' </summary>
    Private Shared Function EditorRowTitleWidth(hostWidth As Integer, editorWidth As Integer) As Integer
        Dim shrunk As Integer = Math.Min(editorWidth, Math.Max(150, hostWidth - 260))
        Dim editorLeft As Integer = Math.Max(230, Math.Min(420, hostWidth - shrunk - 12))
        Return Math.Max(180, editorLeft - 28)
    End Function

    ''' <summary>
    ''' AddSettingRow, the checkbox branch. ConfigureModernCheckbox pins the box to 28x28
    ''' and clears its own caption, so the title starts at 14 + 28 + 10:
    '''     title.Width = Math.Max(180, host.ClientSize.Width - title.Left - 14)
    ''' </summary>
    Private Shared Function CheckRowTitleWidth(hostWidth As Integer) As Integer
        Return Math.Max(180, hostWidth - (14 + 28 + 10) - 14)
    End Function

    ''' <summary>AddSectionHeader: a Dock=Fill label with Padding(4, 0, 0, 0) in a full row.</summary>
    Private Shared ReadOnly SectionHeaderWidth As Integer = NarrowestFullHost - 4

    ''' <summary>AddProjectLinkRow: link.Width = host.ClientSize.Width - 24, compact row.</summary>
    Private Shared ReadOnly ProjectLinkWidth As Integer = NarrowestCompactHost - 24

    ' The three fonts the captions are drawn in, verbatim from the builders.
    Private Const RowTitleFont As String = "Segoe UI Semibold"
    Private Const RowTitleSize As Single = 8.75F
    Private Const SectionHeaderSize As Single = 9.5F

    ''' <summary>What kind of row a key was declared as - it decides the budget.</summary>
    Private Enum RowKind
        Editor
        CheckBox
        SectionHeader
        ProjectLink
    End Enum

    Private NotInheritable Class SettingRow
        Public Property Key As String
        Public Property Kind As RowKind
        Public Property EditorWidth As Integer
        Public Property Source As String       ' declaring file, for the failure message

        Public Function TitleBudget() As Integer
            Select Case Kind
                Case RowKind.CheckBox : Return CheckRowTitleWidth(NarrowestCompactHost)
                Case RowKind.SectionHeader : Return SectionHeaderWidth
                Case RowKind.ProjectLink : Return ProjectLinkWidth
                Case Else : Return EditorRowTitleWidth(NarrowestCompactHost, EditorWidth)
            End Select
        End Function

        Public Function TitleFont() As Font
            Dim size As Single = If(Kind = RowKind.SectionHeader, SectionHeaderSize, RowTitleSize)
            Return New Font(RowTitleFont, size)
        End Function
    End Class

    ' ------------------------------------------------------------------- tests ----

    <Fact>
    Public Sub No_settings_caption_overflows_its_control_in_any_language()
        Dim rows As List(Of SettingRow) = DiscoverRows()
        Dim titles As Dictionary(Of String, String) = DiscoverTitles()

        Dim overflow As New List(Of String)()

        For Each row As SettingRow In rows
            Dim ru As String = Nothing
            ' A key with no Case of its own falls through to "Case Else : Return key",
            ' i.e. it shows its own identifier. That is a different defect and belongs to
            ' the test below, not here.
            If Not titles.TryGetValue(row.Key, ru) Then Continue For
            If ru.Length = 0 Then Continue For

            Dim budget As Integer = row.TitleBudget()
            Using font As Font = row.TitleFont()
                For index As Integer = 0 To Localization.Codes.Length - 1
                    Dim text As String = Translate(ru, index)
                    Dim needed As Integer = Measure(text, font)
                    If needed <= budget Then Continue For
                    overflow.Add(String.Format("{0} [{1}] {2}: needs {3} px, has {4} px - ""{5}""",
                                               row.Key, Localization.Codes(index), row.Kind,
                                               needed, budget, Shorten(text)))
                Next
            End Using
        Next

        Assert.True(overflow.Count = 0,
                    "Settings captions that do not fit their control (" & overflow.Count & "):" & vbLf &
                    String.Join(vbLf, overflow.OrderByDescending(Function(line) line).Take(40)))
    End Sub

    ''' <summary>
    ''' Every row the window declares must have a caption of its own. Without this a
    ''' key that lost its Case in ModernSettingTitle shows the raw identifier
    ''' ("video_end_action") in all 13 languages, and the overflow test above would pass
    ''' it happily - an ASCII identifier always fits.
    ''' </summary>
    <Fact>
    Public Sub Every_declared_row_has_a_caption()
        Dim titles As Dictionary(Of String, String) = DiscoverTitles()
        Dim missing As List(Of String) = DiscoverRows().
            Where(Function(row) Not titles.ContainsKey(row.Key)).
            Select(Function(row) row.Key & "  (" & row.Source & ")").
            Distinct().ToList()

        Assert.True(missing.Count = 0,
                    "Settings rows with no Case in ModernSettingTitle (" & missing.Count & "):" & vbLf &
                    String.Join(vbLf, missing))
    End Sub

    ''' <summary>
    ''' A scan that found nothing passes as quietly as a clean one - the control fact
    ''' §7.1 asks for. It also pins the measurement itself: a caption far past the budget
    ''' has to be reported, or the loop above proves nothing.
    ''' </summary>
    <Fact>
    Public Sub The_layout_scanner_actually_inspects_the_window()
        Dim rows As List(Of SettingRow) = DiscoverRows()
        Assert.True(rows.Count > 50, "Only " & rows.Count & " settings rows found - the scanner is missing call sites.")
        Assert.Contains(rows, Function(r) r.Kind = RowKind.CheckBox)
        Assert.Contains(rows, Function(r) r.Kind = RowKind.Editor)
        Assert.Contains(rows, Function(r) r.Kind = RowKind.SectionHeader)
        Assert.Contains(rows, Function(r) r.Kind = RowKind.ProjectLink)

        Dim titles As Dictionary(Of String, String) = DiscoverTitles()
        Assert.True(titles.Count > 50, "Only " & titles.Count & " captions parsed out of ModernSettingTitle.")

        ' The check can fail: a caption nobody would fit still has to come out over budget.
        Dim narrow As New SettingRow With {.Key = "control", .Kind = RowKind.Editor, .EditorWidth = 300}
        Using font As Font = narrow.TitleFont()
            Assert.True(Measure(New String("W"c, 120), font) > narrow.TitleBudget(),
                        "The measurement never exceeds the budget - it is not measuring anything.")
        End Using
    End Sub

    ' ----------------------------------------------------------------- scanning ----

    ''' <summary>
    ''' Row declarations. AddSettingRow states its editor width as a literal and 34 is the
    ''' checkbox size, which is what separates the two budgets. The AddPreference* helpers
    ''' of Table_Form.ExpandedSettings.vb pass the key on to AddSettingRow as a variable,
    ''' so they are read at their own call sites, with the width their helper hard-codes.
    ''' </summary>
    Private Shared Function DiscoverRows() As List(Of SettingRow)
        Dim rows As New List(Of SettingRow)()

        ' NOTE: no local may be called "file" or "path" - VB is case-insensitive, so such a
        ' name shadows System.IO.File / System.IO.Path for the whole method.
        For Each source As String In LayoutSources()
            Dim text As String = LocalizationCoverageTests.BlankComments(File.ReadAllText(source))
            Dim name As String = Path.GetFileName(source)

            For Each m As Match In Regex.Matches(text, "AddSettingRow\(\s*\w+\s*,\s*""([^""]+)""\s*,.*?,\s*(\d+)\s*[,)]")
                Dim width As Integer = Integer.Parse(m.Groups(2).Value)
                rows.Add(New SettingRow With {.Key = m.Groups(1).Value, .Source = name,
                                              .EditorWidth = width,
                                              .Kind = If(width = 34, RowKind.CheckBox, RowKind.Editor)})
            Next

            AddSimple(rows, text, name, "AddSectionHeader", RowKind.SectionHeader, 0)
            AddSimple(rows, text, name, "AddProjectLinkRow", RowKind.ProjectLink, 0)

            ' Widths as hard-coded by each helper in Table_Form.ExpandedSettings.vb.
            AddSimple(rows, text, name, "AddPreferenceCheck", RowKind.CheckBox, 34)
            AddSimple(rows, text, name, "AddPreferenceChoice", RowKind.Editor, 230)
            AddSimple(rows, text, name, "AddPreferenceNumber", RowKind.Editor, 100)
            AddSimple(rows, text, name, "AddPreferenceText", RowKind.Editor, 260)
        Next

        ' AddInformationBlock is deliberately absent. Its "title" is a sentence - the
        ' secondary-window warning is two lines of prose - and no width holds it in any
        ' language, Russian included. Those blocks are the one place the window means the
        ' caption to wrap out of its box, so measuring them would be a permanent red that
        ' says nothing about a translation.
        Return rows
    End Function

    Private Shared Sub AddSimple(rows As List(Of SettingRow), text As String, source As String,
                                 helper As String, kind As RowKind, editorWidth As Integer)
        For Each m As Match In Regex.Matches(text, Regex.Escape(helper) & "\(\s*\w+\s*,\s*""([^""]+)""")
            rows.Add(New SettingRow With {.Key = m.Groups(1).Value, .Kind = kind,
                                          .EditorWidth = editorWidth, .Source = source})
        Next
    End Sub

    ''' <summary>
    ''' The captions, from the Select Case of ModernSettingTitle. Three shapes are read:
    ''' Localization.T(".."), Localization.TC("context", "..") and a bare literal - the
    ''' brands ("GitHub", the product names), which are the same word in every language
    ''' and still have to fit their row.
    ''' </summary>
    Private Shared Function DiscoverTitles() As Dictionary(Of String, String)
        Dim titles As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Dim layout As String = Path.Combine(SourceDir(), "Table_Form.ModernLayout.vb")
        Dim text As String = LocalizationCoverageTests.BlankComments(File.ReadAllText(layout))

        Dim body As Match = Regex.Match(text, "Function ModernSettingTitle\b.*?End Function", RegexOptions.Singleline)
        Assert.True(body.Success, "ModernSettingTitle not found in Table_Form.ModernLayout.vb.")

        For Each m As Match In Regex.Matches(body.Value,
                "Case\s+""([^""]+)""\s*:\s*Return\s+Localization\.(?:T|TC\s*\(\s*""[^""]*""\s*,)\s*\(?\s*""([^""]*)""")
            titles(m.Groups(1).Value) = m.Groups(2).Value
        Next
        For Each m As Match In Regex.Matches(body.Value, "Case\s+""([^""]+)""\s*:\s*Return\s+""([^""]*)""")
            If Not titles.ContainsKey(m.Groups(1).Value) Then titles(m.Groups(1).Value) = m.Groups(2).Value
        Next
        Return titles
    End Function

    Private Shared Function LayoutSources() As String()
        Dim dir As String = SourceDir()
        Return {Path.Combine(dir, "Table_Form.ModernLayout.vb"),
                Path.Combine(dir, "Table_Form.ExpandedSettings.vb")}
    End Function

    ' ------------------------------------------------------------------ helpers ----

    ''' <summary>
    ''' The same resolution T/TC do, without touching Localization.CurrentCode: index 0 is
    ''' the Russian key itself, an empty translation falls back to English, and an empty
    ''' English one to the source. Reading the map directly keeps this test free of global
    ''' state that another test class could be changing at the same moment.
    ''' </summary>
    Private Shared Function Translate(ru As String, index As Integer) As String
        If index = 0 Then Return ru
        Dim values As String() = Localization.All.
            Where(Function(kv) String.Equals(kv.Key, ru, StringComparison.Ordinal)).
            Select(Function(kv) kv.Value).FirstOrDefault()
        If values Is Nothing Then Return ru
        Dim value As String = values(index - 1)
        If value.Length = 0 Then value = values(0)          ' English sits first after Russian
        Return If(value.Length = 0, ru, value)
    End Function

    ''' <summary>
    ''' Single-line GDI measurement, the way a Label with AutoSize=False draws: these
    ''' captions are one line in a 20 px box and never wrap.
    ''' </summary>
    Private Shared Function Measure(text As String, font As Font) As Integer
        Return TextRenderer.MeasureText(text, font, New Size(Integer.MaxValue, Integer.MaxValue),
                                        TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine).Width
    End Function

    Private Shared Function Shorten(s As String) As String
        Return If(s.Length <= 60, s, s.Substring(0, 60) & "..")
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
