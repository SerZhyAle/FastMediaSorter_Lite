Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The layout test of SPECIFICATION_SHARE_MANAGER_COMPACT_WINDOW.md §7.8, the compact
''' window's half of 013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md §7.2: build every section
''' header's title in all 13 languages at the NARROWEST width the window can be, and fail
''' when one needs more room than it gets.
'''
''' It exists because the two tests that already guard the translated values deliberately
''' cannot see geometry. LocalizationParityTests proves every key has all twelve
''' translations with matching placeholder arity; LocalizationCoverageTests proves no
''' Russian literal bypasses the layer. Both stay green while a German section title is
''' clipped in half - and a clipped title is the one failure this control cannot report,
''' because the title is drawn WITHOUT ellipsis on purpose (that budget belongs to the
''' summary beside it, which always ellipsizes).
'''
''' WHY IT MEASURES INSTEAD OF BUILDING THE FORM: MainWindow reaches the worker, the
''' service and the registry on construction; SectionHeader is private to
''' CollapsibleSection and draws itself. The arithmetic below is copied from
''' CollapsibleSection.OnPaint next to the line it comes from - the same precedent the
''' viewer's SettingsLayoutTests set for Table_Form.
''' </summary>
Public Class SectionLayoutTests

    ' --- geometry, copied from the sources it measures ---------------------------
    '
    ' MainWindow.Layout.vb, BuildUi:
    '     Me.MinimumSize = New Size(560, 420)          ' window, 96-DPI design units
    '     _root.Padding  = New Padding(12, 10, 12, 4)
    ' CollapsibleSection.OnPaint:
    '     glyph  = Math.Max(7, CInt(Me.Font.Height * 0.42F))
    '     pad    = 6
    '     left   = pad + glyph + 8
    '     right  = w - pad
    '     title is drawn into (right - left) with NO EndEllipsis.
    '
    ' A section is Dock=Fill in a single Percent-100 column, so its width is the root
    ' panel's display width: the client width, minus the root padding, minus the vertical
    ' scrollbar the root shows once the content outgrows it (AutoScroll = True - the
    ' worst case, and the case a small window is actually in).

    ''' <summary>Window MinimumSize.Width minus a 3D border pair - the client width.</summary>
    Private Const MinClientWidth As Integer = 560 - 16

    ''' <summary>_root.Padding horizontal (12 + 12).</summary>
    Private Const RootPaddingHorizontal As Integer = 24

    ''' <summary>SystemInformation.VerticalScrollBarWidth at 96 DPI. Hard-coded rather than
    ''' read, so the budget does not move with the machine running the test.</summary>
    Private Const ScrollBarWidth As Integer = 17

    Private Const HeaderPad As Integer = 6
    Private Const ChevronGap As Integer = 8

    ''' <summary>Application.SetDefaultFont in Program.Main. The section header draws its
    ''' title BOLD in this family and size.</summary>
    Private Const UiFontFamily As String = "Segoe UI"
    Private Const UiFontSize As Single = 9.0F

    ''' <summary>
    ''' The width a title actually gets on the narrowest window. The chevron scales with the
    ''' font, so it is derived from the same font the text is measured in.
    ''' </summary>
    Private Shared Function TitleBudget(font As Font) As Integer
        Dim sectionWidth As Integer = MinClientWidth - RootPaddingHorizontal - ScrollBarWidth
        Dim glyph As Integer = Math.Max(7, CInt(font.Height * 0.42F))
        Dim left As Integer = HeaderPad + glyph + ChevronGap
        Return sectionWidth - left - HeaderPad
    End Function

    ' ------------------------------------------------------------------- tests ----

    <Fact>
    Public Sub No_section_title_overflows_its_header_in_any_language()
        Dim overflow As New List(Of String)()

        Using regular As New Font(UiFontFamily, UiFontSize)
            Using bold As New Font(regular, FontStyle.Bold)
                Dim budget As Integer = TitleBudget(regular)
                For Each ru As String In DiscoverSectionTitles()
                    For index As Integer = 0 To Localization.Codes.Length - 1
                        Dim text As String = Translate(ru, index)
                        Dim needed As Integer = Measure(text, bold)
                        If needed <= budget Then Continue For
                        overflow.Add(String.Format("[{0}] needs {1} px, has {2} px - ""{3}""",
                                                   Localization.Codes(index), needed, budget, text))
                    Next
                Next
            End Using
        End Using

        Assert.True(overflow.Count = 0,
                    "Section titles that do not fit their header (" & overflow.Count & "):" & vbLf &
                    String.Join(vbLf, overflow))
    End Sub

    ''' <summary>
    ''' The asymmetry the design rests on: a folded section shows "title - summary", and
    ''' when the row is too narrow the SUMMARY is what gives way. Ellipsizing the title
    ''' instead would hide which section the row even is, and dropping the ellipsis from the
    ''' summary would spill a long access verdict past the edge.
    ''' </summary>
    <Fact>
    Public Sub The_title_never_ellipsizes_and_the_summary_always_does()
        Dim paint As String = PaintBody()

        Dim titleDraw As Match = Regex.Match(paint, "TextRenderer\.DrawText\(g, _title,.*?\)", RegexOptions.Singleline)
        Assert.True(titleDraw.Success, "The title is no longer drawn by CollapsibleSection.OnPaint.")
        Assert.DoesNotContain("EndEllipsis", titleDraw.Value)

        Dim summaryDraw As Match = Regex.Match(paint, "TextRenderer\.DrawText\(g, _summary,.*?\)", RegexOptions.Singleline)
        Assert.True(summaryDraw.Success, "The summary is no longer drawn by CollapsibleSection.OnPaint.")
        Assert.Contains("EndEllipsis", summaryDraw.Value)
    End Sub

    ''' <summary>
    ''' A scan that found nothing passes as quietly as a clean one. This pins both halves:
    ''' the three sections are really discovered, and the measurement can actually fail.
    ''' </summary>
    <Fact>
    Public Sub The_scanner_actually_inspects_the_window()
        Dim titles As List(Of String) = DiscoverSectionTitles()
        Assert.True(titles.Count >= 3,
                    "Only " & titles.Count & " section titles found - the scanner is missing call sites.")

        Using regular As New Font(UiFontFamily, UiFontSize)
            Using bold As New Font(regular, FontStyle.Bold)
                Assert.True(Measure(New String("W"c, 120), bold) > TitleBudget(regular),
                            "The measurement never exceeds the budget - it is not measuring anything.")
            End Using
        End Using
    End Sub

    ''' <summary>Each section's persisted key must be unique, or Share_ExpandedSections
    ''' cannot tell two sections apart and one of them silently follows the other.</summary>
    <Fact>
    Public Sub Section_keys_are_unique()
        Dim keys As List(Of String) = DiscoverSectionKeys()
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count())
    End Sub

    ' ----------------------------------------------------------------- scanning ----

    ''' <summary>Every `New CollapsibleSection("key", Localization.T("Title"))` in the
    ''' Companion sources - so a section added later is measured without touching this
    ''' test.</summary>
    Private Shared Function SectionSites() As MatchCollection
        Dim layout As String = Path.Combine(CompanionSourceDir(), "Forms", "MainWindow.Layout.vb")
        Return Regex.Matches(File.ReadAllText(layout),
            "New CollapsibleSection\(\s*""(?<key>[^""]+)""\s*,\s*Localization\.T\(\s*""(?<title>[^""]+)""")
    End Function

    Private Shared Function DiscoverSectionTitles() As List(Of String)
        Return SectionSites().Cast(Of Match)().Select(Function(m) m.Groups("title").Value).ToList()
    End Function

    Private Shared Function DiscoverSectionKeys() As List(Of String)
        Return SectionSites().Cast(Of Match)().Select(Function(m) m.Groups("key").Value).ToList()
    End Function

    Private Shared Function PaintBody() As String
        Dim src As String = Path.Combine(CompanionSourceDir(), "Forms", "CollapsibleSection.vb")
        Dim body As Match = Regex.Match(File.ReadAllText(src),
                                        "Protected Overrides Sub OnPaint\b.*?End Sub", RegexOptions.Singleline)
        Assert.True(body.Success, "CollapsibleSection.OnPaint not found.")
        Return body.Value
    End Function

    ' ------------------------------------------------------------------ helpers ----

    ''' <summary>
    ''' The same resolution T does, without touching Localization.CurrentCode: index 0 is
    ''' the Russian key itself, an empty translation falls back to English, and an empty
    ''' English one to the source. Reading the map directly keeps this test free of global
    ''' state another test class could be changing at the same moment.
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

    ''' <summary>Single-line GDI measurement, the way the header draws it.</summary>
    Private Shared Function Measure(text As String, font As Font) As Integer
        Return TextRenderer.MeasureText(text, font, New Size(Integer.MaxValue, Integer.MaxValue),
                                        TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine).Width
    End Function

    Private Shared Function CompanionSourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "FastMediaSorterCompanion")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Program.vb")) Then
                Return candidate
            End If
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("Could not locate src\FastMediaSorterCompanion from the test binary.")
    End Function

End Class
