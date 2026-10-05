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
''' The layout test of SPECIFICATION_THIRTEEN_UI_LANGUAGES §7.2, revised for the
''' two-column row grid of SPECIFICATION 127 §3.1: build the settings window's rows on
''' each of the 13 languages and fail when a caption cannot live in the width the grid
''' gives it.
'''
''' It exists because the two tests that already guard the 4 300 translated values
''' deliberately cannot see geometry. LocalizationParityTests proves every key has all
''' twelve translations with matching placeholder arity; LocalizationCoverageTests proves
''' no Russian literal bypasses the layer. Both stay green while a German caption is
''' silently crushed into four lines - the caption may wrap inside its label column by
''' design, but a caption that needs most of the window for itself is a defect.
'''
''' Modern-only, exactly like the window it measures: Table_Form.ModernLayout.vb and
''' Table_Form.ExpandedSettings.vb are whole-file "#If Not NETFRAMEWORK", and on the
''' net48 leg Localization.Codes is cut to RU+EN anyway.
'''
''' WHY IT READS THE SOURCES instead of building the form: Table_Form is a partial class
''' over a Designer file that reaches into Main_Form (GetModernPreferences), LibVLC and
''' Tesseract - the whole dependency tree this test project exists to stay out of (see the
''' comment at the top of Lite.Tests.vbproj). The rows are declared by call sites regular
''' enough to read exactly, the page placement is declared by AddSettingsGroup calls, and
''' the arithmetic is the linked <see cref="SettingsRowGridRules"/> helper itself - the
''' same math production runs. The precedent is LocalizationCoverageTests, which scans
''' the same tree for the same reason.
''' </summary>
Public Class SettingsLayoutTests

    ' --- geometry, mirrored from Table_Form.ModernLayout.vb -----------------------
    '
    ' SizeModernSettingRow, the two-column grid: the page's shared value column V is
    ' the widest editor on the page clamped to 200..340; a row is two-column from 560
    ' logical px of content while the labels keep their floor. Inside the row the
    ' caption starts at 14 and ends 12 before the value column, whose own right edge
    ' is 12 inside the host (the row's right padding):
    '     labelWidth = hostWidth - V - 38
    '
    ' The label floor is checked after the row insets: the widest column stacks
    ' until the host reaches 598, leaving exactly 220 for the caption. Below the threshold the
    ' caption gets the whole stacked row (hostWidth - 28), which is roomier - the
    ' two-column grid at its floor is the true worst case.

    ''' <summary>The narrowest two-column label width: the stacked threshold minus the
    ''' widest value column minus the row's own insets.</summary>
    Private Shared ReadOnly NarrowestLabelWidth As Integer =
        SettingsRowGridRules.MinLabelColumnWidth

    ''' <summary>A stacked caption gets the whole row minus the two 14 px insets.</summary>
    Private Const StackedLabelWidth As Integer = SettingsRowGridRules.StackedBelowWidth - 28

    ''' <summary>A compact row (the About page's link rows) is at its narrowest exactly at
    ''' the two-column page threshold - one pixel more flow and it is cut in half.</summary>
    Private Const NarrowestCompactHost As Integer = 406

    ''' <summary>AddProjectLinkRow: link.Width = host.ClientSize.Width - 24, compact row.</summary>
    Private Shared ReadOnly ProjectLinkWidth As Integer = NarrowestCompactHost - 24

    ''' <summary>AddSectionHeader: a Dock=Fill label with Padding(4, 0, 0, 0) in a full row.
    ''' These headings are re-parented into groups (their hosts disposed) and rendered as
    ''' group header bands instead, so the stacked row width is the honest budget.</summary>
    Private Shared ReadOnly SectionHeaderWidth As Integer = StackedLabelWidth - 4

    ' The fonts the captions are drawn in, verbatim from the builders.
    Private Const RowTitleFont As String = "Segoe UI Semibold"
    Private Const RowTitleSize As Single = 8.75F
    Private Const SectionHeaderSize As Single = 10.5F
    Private Const SummarySize As Single = 8.0F

    ''' <summary>How many lines a caption may wrap into at the narrowest label width.
    ''' One was the old stacked anatomy's budget; the two-column grid wraps by design,
    ''' and a caption that needs three lines at the floor is the defect this test names.</summary>
    Private Const MaxCaptionLines As Integer = 2

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

        ''' <summary>The caption width this row can rely on: the page's shared value
        ''' column decides, so the page placement is resolved first.</summary>
        Public Function TitleBudget(pageColumn As Integer) As Integer
            Select Case Kind
                Case RowKind.SectionHeader : Return SectionHeaderWidth
                Case RowKind.ProjectLink : Return ProjectLinkWidth
                Case Else : Return Math.Max(SettingsRowGridRules.MinLabelColumnWidth,
                    SettingsRowGridRules.LabelColumnWidth(SettingsRowGridRules.StackedBelowWidth, pageColumn))
            End Select
        End Function

        Public Function TitleFont() As Font
            Dim size As Single = If(Kind = RowKind.SectionHeader, SectionHeaderSize, RowTitleSize)
            Return New Font(RowTitleFont, size)
        End Function
    End Class

    ' ------------------------------------------------------------------- tests ----

    <Fact>
    Public Sub No_settings_caption_exceeds_its_label_column_in_any_language()
        Dim rows As List(Of SettingRow) = DiscoverRows()
        Dim titles As Dictionary(Of String, String) = DiscoverTitles()
        Dim rowColumns As Dictionary(Of String, Integer) = DiscoverRowValueColumns(rows)

        Dim overflow As New List(Of String)()

        For Each row As SettingRow In rows
            Dim ru As String = Nothing
            ' A key with no Case of its own falls through to "Case Else : Return key",
            ' i.e. it shows its own identifier. That is a different defect and belongs to
            ' the test below, not here.
            If Not titles.TryGetValue(row.Key, ru) Then Continue For
            If ru.Length = 0 Then Continue For

            Dim budget As Integer = row.TitleBudget(rowColumns(row.Key))
            Using font As Font = row.TitleFont()
                For index As Integer = 0 To Localization.Codes.Length - 1
                    Dim text As String = Translate(ru, index)
                    Dim lines As Integer = WrappedLines(text, font, budget)
                    If lines <= MaxCaptionLines Then Continue For
                    overflow.Add(String.Format("{0} [{1}] {2}: wraps to {3} lines at {4} px - ""{5}""",
                                               row.Key, Localization.Codes(index), row.Kind,
                                               lines, budget, Shorten(text)))
                Next
            End Using
        Next

        Assert.True(overflow.Count = 0,
                    "Settings captions that do not fit their label column (" & overflow.Count & "):" & vbLf &
                    String.Join(vbLf, overflow.OrderByDescending(Function(line) line).Take(40)))
    End Sub

    <Fact>
    Public Sub Every_caption_fits_the_stacked_fallback_on_one_line()
        ' Below the two-column threshold a caption owns the whole row. A caption that
        ' cannot stand on ONE line there would be a wall of text in the narrow layout.
        Dim rows As List(Of SettingRow) = DiscoverRows()
        Dim titles As Dictionary(Of String, String) = DiscoverTitles()

        Dim overflow As New List(Of String)()
        For Each row As SettingRow In rows
            Dim ru As String = Nothing
            If Not titles.TryGetValue(row.Key, ru) Then Continue For
            If ru.Length = 0 Then Continue For
            Using font As Font = row.TitleFont()
                For index As Integer = 0 To Localization.Codes.Length - 1
                    Dim text As String = Translate(ru, index)
                    Dim needed As Integer = Measure(text, font)
                    If needed <= StackedLabelWidth Then Continue For
                    overflow.Add(String.Format("{0} [{1}]: needs {2} px, has {3} px - ""{4}""",
                                               row.Key, Localization.Codes(index), needed, StackedLabelWidth, Shorten(text)))
                Next
            End Using
        Next

        Assert.True(overflow.Count = 0,
                    "Settings captions that overflow even the stacked fallback (" & overflow.Count & "):" & vbLf &
                    String.Join(vbLf, overflow.OrderByDescending(Function(line) line).Take(40)))
    End Sub

    ''' <summary>
    ''' Every row the window declares must have a caption of its own. Without this a
    ''' key that lost its Case in ModernSettingTitle shows the raw identifier
    ''' ("video_end_action") in all 13 languages, and the overflow tests above would pass
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

        Dim pageColumns As Dictionary(Of String, Integer) = DiscoverRowValueColumns(rows)
        Assert.True(pageColumns.Values.All(Function(column) column >= SettingsRowGridRules.MinValueColumnWidth AndAlso
                                                            column <= SettingsRowGridRules.MaxValueColumnWidth),
                    "A page value column outside the 127 §3.1 clamp.")

        ' The check can fail: a caption nobody would fit still has to come out over budget.
        Using font As Font = New Font(RowTitleFont, RowTitleSize)
            Dim filler As String = String.Join(" ", Enumerable.Repeat("word", 80))
            Assert.True(WrappedLines(filler, font, NarrowestLabelWidth) > MaxCaptionLines,
                        "The wrapped measurement never exceeds the line budget - it is not measuring anything.")
        End Using
    End Sub

    ''' <summary>The pinned agreement between the sidebar and the real TabControl (13.1):
    ''' nav row i selects page i. The pairing itself is a checked fact of this ticket -
    ''' the Designer's page names are historical, the modern ones are readable.</summary>
    Private Shared ReadOnly ExpectedNavPages As (Nav As String, Page As String)() = {
        ("settingsNavGeneral", "Tab_Page_General"),
        ("settingsNavRecipients", "Tab_Page_1"),
        ("settingsNavViewing", "Tab_Page_2"),
        ("settingsNavVideo", "Tab_Page_3"),
        ("settingsNavAudio", "Tab_Page_Audio"),
        ("settingsNavFiles", "Tab_Page_4"),
        ("settingsNavResources", "Tab_Page_Resources"),
        ("settingsNavOcr", "Tab_Page_5"),
        ("settingsNavTranslation", "Tab_Page_Translation"),
        ("settingsNavStorage", "Tab_Page_Storage"),
        ("settingsNavSftp", "Tab_Page_Sftp"),
        ("settingsNavAbout", "Tab_Page_About")}

    ''' <summary>
    ''' The sidebar's nav rows are positional: a nav button's Tag is an index into
    ''' Tab_Control.TabPages, and those pages come from two constructions - the Designer's
    ''' five Adds, then the builder's Insert/Add calls. Nothing at runtime re-checks that
    ''' the two orders agree, and ticket 118's S1-S7 must move together: one missed edit
    ''' opens the WRONG PAGE under a nav caption. The window itself cannot be built in
    ''' this project (LibVLC, Tesseract - see the file header), so the guard replays the
    ''' same lines the builder runs - the established way this file reads the sources.
    ''' </summary>
    <Fact>
    Public Sub The_sidebar_nav_order_matches_the_tab_page_order()
        Dim dir As String = SourceDir()
        Dim designer As String = LocalizationCoverageTests.BlankComments(File.ReadAllText(Path.Combine(dir, "Table_Form.Designer.vb")))
        Dim modern As String = LocalizationCoverageTests.BlankComments(File.ReadAllText(Path.Combine(dir, "Table_Form.ModernLayout.vb")))

        ' Variable -> page name: the modern pages carry their own, the Designer's five are
        ' named after their variables.
        Dim names As New Dictionary(Of String, String)()
        For Each m As Match In Regex.Matches(modern, "(modern\w*Page) = New TabPage With \{\.Name = ""([^""]+)""")
            names(m.Groups(1).Value) = m.Groups(2).Value
        Next
        Dim pages As New List(Of String)()
        For Each m As Match In Regex.Matches(designer, "Tab_Control\.Controls\.Add\(Me\.(Tab_Page_\w+)\)")
            names(m.Groups(1).Value) = m.Groups(1).Value
            pages.Add(m.Groups(1).Value)
        Next
        Assert.Equal(5, pages.Count)

        ' Replay the builder's calls in source order - the three argument shapes it uses
        ' today: Insert(n, page), Add(page), and Insert through IndexOf(page), page.
        For Each line As String In modern.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            Dim callMatch As Match = Regex.Match(line, "Tab_Control\.TabPages\.(Insert|Add)\((.*)\)\s*$")
            If Not callMatch.Success Then Continue For
            Dim args As String = callMatch.Groups(2).Value.Trim()
            Dim inserted As String = Nothing
            Dim at As Integer = -1
            Dim byAnchor As Match = Regex.Match(args, "IndexOf\((modern\w*Page)\)\s*,\s*(modern\w*Page)\s*$")
            If byAnchor.Success Then
                at = pages.IndexOf(byAnchor.Groups(1).Value)
                Assert.True(at >= 0, "Insert() before its anchor page exists: " & line.Trim())
                inserted = byAnchor.Groups(2).Value
            Else
                Dim byNumber As Match = Regex.Match(args, "(\d+)\s*,\s*(modern\w*Page)\s*$")
                If byNumber.Success Then
                    at = Integer.Parse(byNumber.Groups(1).Value)
                    inserted = byNumber.Groups(2).Value
                Else
                    Dim appended As Match = Regex.Match(args, "^(modern\w*Page)\s*$")
                    If appended.Success Then inserted = appended.Groups(1).Value
                End If
            End If
            Assert.True(inserted IsNot Nothing, "A TabPages call this guard cannot read: " & line.Trim())
            If at >= 0 Then pages.Insert(Math.Min(at, pages.Count), inserted) Else pages.Add(inserted)
        Next

        Assert.Equal(ExpectedNavPages.Length, pages.Count)
        For i As Integer = 0 To ExpectedNavPages.Length - 1
            Assert.True(names.ContainsKey(pages(i)), "A page with no readable name: " & pages(i))
            Assert.True(names(pages(i)) = ExpectedNavPages(i).Page,
                        "Nav row """ & ExpectedNavPages(i).Nav & """ lands on page """ & names(pages(i)) &
                        """ instead of """ & ExpectedNavPages(i).Page & """ - the S1-S7 edits move together.")
        Next

        ' And the nav array itself carries the same rows, in the same order.
        Dim navMatch As Match = Regex.Match(modern, "ModernNavControlNames As String\(\) = \{(.*?)\}", RegexOptions.Singleline)
        Assert.True(navMatch.Success, "ModernNavControlNames not found.")
        Dim navNames As New List(Of String)()
        For Each m As Match In Regex.Matches(navMatch.Groups(1).Value, """(\w+)""")
            navNames.Add(m.Groups(1).Value)
        Next
        Assert.Equal(ExpectedNavPages.Select(Function(pair) pair.Nav).ToArray(), navNames.ToArray())
    End Sub

    ' ----------------------------------------------------------------- scanning ----

    ''' <summary>
    ''' Row declarations. AddSettingRow states its editor width as a literal and 34 is the
    ''' checkbox size, which is what separates the two kinds. The AddPreference* helpers
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
            AddSimple(rows, text, name, "AddPreferenceSlider", RowKind.Editor, 230)
            ' The Storage usage+clear rows of 130 §3.3: a strip declared at 320 (measured
            ' at runtime), so the conservative widest-column estimate applies.
            AddSimple(rows, text, name, "AddCacheUsageRow", RowKind.Editor, 320)
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
    ''' The page's shared value column (127 §3.1.3), estimated per page from the declared
    ''' rows: exact widths where the declaration is exact (the checkbox, the numeric rows
    ''' with their unit suffix, the slider), and the worst case - the widest clamp - for
    ''' the measured editors (drop-downs sized for their longest localized item, command
    ''' buttons fitted to their text, strips). Placement comes from the AddSettingsGroup
    ''' calls; a row outside every group gets the widest column, the conservative choice.
    ''' Returned per row key, the way the budgets consume it.
    ''' </summary>
    Private Shared Function DiscoverRowValueColumns(rows As List(Of SettingRow)) As Dictionary(Of String, Integer)
        Dim rowPage As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Dim text As String = LocalizationCoverageTests.BlankComments(
            File.ReadAllText(Path.Combine(SourceDir(), "Table_Form.SettingsGroups.vb")))
        For Each m As Match In Regex.Matches(text, "AddSettingsGroup\(\s*""([^""]+)""\s*,\s*""[^""]+""\s*,\s*""[^""]+""\s*,\s*\w+\s*,([^)]*)\)")
            Dim pageId As String = m.Groups(1).Value
            For Each rowKey As Match In Regex.Matches(m.Groups(2).Value, """([^""]+)""")
                rowPage(rowKey.Groups(1).Value) = pageId
            Next
        Next

        Dim widest As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
        For Each row As SettingRow In rows
            Dim width As Integer = EstimatedEditorWidth(row)
            Dim page As String = Nothing
            If Not rowPage.TryGetValue(row.Key, page) Then page = ""
            Dim current As Integer = Nothing
            widest.TryGetValue(page, current)
            widest(page) = Math.Max(current, width)
        Next

        Dim columns As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
        For Each entry In widest
            columns(entry.Key) = SettingsRowGridRules.PageValueColumn(entry.Value)
        Next

        ' Per row: the column of the page the row is placed on; the widest column for a
        ' row outside every declared group.
        Dim worst As Integer = SettingsRowGridRules.MaxValueColumnWidth
        Dim perRow As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
        For Each row As SettingRow In rows
            Dim page As String = Nothing
            If rowPage.TryGetValue(row.Key, page) AndAlso columns.ContainsKey(page) Then
                perRow(row.Key) = columns(page)
            Else
                perRow(row.Key) = worst
            End If
        Next
        Return perRow
    End Function

    Private Shared Function EstimatedEditorWidth(row As SettingRow) As Integer
        Select Case row.Kind
            Case RowKind.CheckBox : Return 28
            Case RowKind.SectionHeader, RowKind.ProjectLink : Return 0
            Case Else
                ' A numeric editor is 110 plus its muted unit suffix; a slider is 180 plus
                ' its 64 numeric companion. Everything measured (drop-downs, buttons,
                ' strips, text) is estimated at the widest column the clamp allows.
                If row.EditorWidth = 100 Then Return SettingsRowGridRules.NumericEditorWidth + 40
                If row.EditorWidth = 230 AndAlso row.Source.EndsWith("ExpandedSettings.vb", StringComparison.Ordinal) Then
                    Return SettingsRowGridRules.SliderEditorWidth + SettingsRowGridRules.SliderCompanionWidth
                End If
                Return SettingsRowGridRules.MaxValueColumnWidth
        End Select
    End Function

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

    ''' <summary>How many lines the caption wraps into at the given width - what the row's
    ''' measured WordBreak height means in whole lines.</summary>
    Private Shared Function WrappedLines(text As String, font As Font, width As Integer) As Integer
        Dim height As Integer = TextRenderer.MeasureText(text, font, New Size(Math.Max(1, width), Integer.MaxValue),
                                                          TextFormatFlags.WordBreak).Height
        Return Math.Max(1, CInt(Math.Ceiling(height / CDbl(Math.Max(1, font.Height)))))
    End Function

    ''' <summary>Single-line GDI measurement.</summary>
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
