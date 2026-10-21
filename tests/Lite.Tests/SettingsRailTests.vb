#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Runtime.ExceptionServices
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit
Imports Xunit.Abstractions

''' <summary>
''' The page rail of the viewer's settings window against SETTINGS-NAVIGATION 2.0 section 2 (ticket 243, slice T-7a):
''' semantics (rule 7), reading direction (rule 1), sizes (rule 5) and the narrow window (rule 6).
'''
''' <c>Table_Form</c> cannot be built in this project (it drags the whole viewer: Main_Form, LibVLC, Tesseract), so the
''' rail is the standalone <see cref="SettingsRailList"/> the window itself uses, tested here with REAL controls on an
''' STA thread, and the way the window wires it is held by reading its source - the same split as
''' <c>SettingsLayoutTests</c> and <c>SettingsUiContextTests</c>. The on-screen look of the rail is a hand check.
''' </summary>
Public Class SettingsRailTests

    ''' <summary>The content column must keep at least this much at the window's minimum width (rule 6 forbids a
    ''' drawer, so the rail may not eat the content). Logical px.</summary>
    Private Const MinContentLogical As Integer = 480

    Private ReadOnly output As ITestOutputHelper

    Public Sub New(output As ITestOutputHelper)
        Me.output = output
    End Sub

    Private Shared ReadOnly Translations As Dictionary(Of String, String()) =
        Localization.All.ToDictionary(Function(kv) kv.Key, Function(kv) kv.Value, StringComparer.Ordinal)

    Private Shared ReadOnly EnglishCaptions As String() = {
        "General", "Destination folders", "Images", "Video", "Audio", "Files and system", "Server and resources",
        "OCR", "Translation", "Storage", "About"}

    ' --- semantics (rule 7) --------------------------------------------------------------------

    <Fact>
    Public Sub The_rail_is_a_list_of_eleven_list_items_named_by_their_caption()
        OnSta(Sub()
                  Using font As New Font("Segoe UI Semibold", 8.5F)
                      Using form As New Form()
                          Dim rail As SettingsRailList = NewRail(EnglishCaptions, font, 1.0F)
                          ' The focus rests outside the list, as it does when the user is on another part of the window.
                          Dim outside As New TextBox()
                          form.Controls.Add(outside)
                          form.Controls.Add(rail)
                          form.Show()
                          outside.Select()
                          Try
                              Assert.Equal(AccessibleRole.List, rail.AccessibleRole)
                              Assert.Equal(AccessibleRole.List, rail.AccessibilityObject.Role)
                              Assert.Equal(11, rail.Rows.Count)
                              For index As Integer = 0 To rail.Rows.Count - 1
                                  Dim row As SettingsRailRow = rail.Rows(index)
                                  Assert.Equal(AccessibleRole.ListItem, row.AccessibleRole)
                                  Assert.Equal(AccessibleRole.ListItem, row.AccessibilityObject.Role)
                                  Assert.Equal(EnglishCaptions(index), row.AccessibleName)
                                  Assert.Equal(EnglishCaptions(index), row.AccessibilityObject.Name)
                                  Dim state As AccessibleStates = row.AccessibilityObject.State
                                  Assert.True((state And AccessibleStates.Selectable) = AccessibleStates.Selectable, "Selectable: " & row.Text)
                                  Assert.True((state And AccessibleStates.Focusable) = AccessibleStates.Focusable, "Focusable: " & row.Text)
                              Next
                          Finally
                              form.Hide()
                          End Try
                      End Using
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Exactly_one_row_is_selected_and_it_moves_with_the_page()
        OnSta(Sub()
                  Using font As New Font("Segoe UI Semibold", 8.5F)
                      Using form As New Form()
                          Dim rail As SettingsRailList = NewRail(EnglishCaptions, font, 1.0F)
                          ' The focus rests outside the list, as it does when the user is on another part of the window.
                          Dim outside As New TextBox()
                          form.Controls.Add(outside)
                          form.Controls.Add(rail)
                          form.Show()
                          outside.Select()
                          Try
                              Assert.Equal(-1, rail.SelectedIndex)
                              Assert.Equal(0, SelectedCount(rail))
                              For Each page As Integer In {0, 5, 10, 2, 2, 7}
                                  rail.SelectedIndex = page
                                  Assert.Equal(page, rail.SelectedIndex)
                                  Assert.Equal(1, SelectedCount(rail))
                                  Assert.Equal(1, SelectedStateCount(rail))
                                  Assert.True(rail.Rows(page).Selected)
                                  Assert.True((rail.Rows(page).AccessibilityObject.State And AccessibleStates.Selected) = AccessibleStates.Selected)
                              Next
                              ' No page, or one that does not exist: nothing is selected, never two rows.
                              rail.SelectedIndex = -1
                              Assert.Equal(0, SelectedCount(rail))
                              rail.SelectedIndex = 3
                              rail.SelectedIndex = 99
                              Assert.Equal(-1, rail.SelectedIndex)
                              Assert.Equal(0, SelectedCount(rail))
                              Assert.Equal(0, SelectedStateCount(rail))
                          Finally
                              form.Hide()
                          End Try
                      End Using
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub The_rail_is_one_tab_stop_that_follows_the_selected_page()
        OnSta(Sub()
                  Using font As New Font("Segoe UI Semibold", 8.5F)
                      Using form As New Form()
                          Dim rail As SettingsRailList = NewRail(EnglishCaptions, font, 1.0F)
                          ' The focus rests outside the list, as it does when the user is on another part of the window.
                          Dim outside As New TextBox()
                          form.Controls.Add(outside)
                          form.Controls.Add(rail)
                          form.Show()
                          outside.Select()
                          Try
                              Assert.Equal(1, rail.TabStopCount())
                              For page As Integer = 0 To rail.Rows.Count - 1
                                  rail.SelectedIndex = page
                                  Assert.Equal(1, rail.TabStopCount())
                                  For index As Integer = 0 To rail.Rows.Count - 1
                                      Assert.Equal(index = page, rail.Rows(index).TabStop)
                                  Next
                              Next
                          Finally
                              form.Hide()
                          End Try
                      End Using
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Arrow_keys_move_only_the_focus_ring_and_never_choose_a_page()
        OnSta(Sub()
                  Using font As New Font("Segoe UI Semibold", 8.5F)
                      Using form As New Form()
                          Dim rail As SettingsRailList = NewRail(EnglishCaptions, font, 1.0F)
                          ' The focus rests outside the list, as it does when the user is on another part of the window.
                          Dim outside As New TextBox()
                          form.Controls.Add(outside)
                          form.Controls.Add(rail)
                          form.Show()
                          outside.Select()
                          Try
                              Dim clicks As Integer = 0
                              For Each row As SettingsRailRow In rail.Rows
                                  AddHandler row.Click, Sub() clicks += 1
                              Next
                              rail.SelectedIndex = 4

                              KeyDown(rail.Rows(4), Keys.Down)
                              Assert.Equal(5, TabStopIndex(rail))
                              Assert.Equal(1, rail.TabStopCount())
                              KeyDown(rail.Rows(5), Keys.Down)
                              Assert.Equal(6, TabStopIndex(rail))
                              KeyDown(rail.Rows(6), Keys.Up)
                              Assert.Equal(5, TabStopIndex(rail))
                              KeyDown(rail.Rows(5), Keys.End)
                              Assert.Equal(10, TabStopIndex(rail))
                              KeyDown(rail.Rows(10), Keys.Down)
                              Assert.Equal(10, TabStopIndex(rail))        ' no wrap
                              KeyDown(rail.Rows(10), Keys.Home)
                              Assert.Equal(0, TabStopIndex(rail))
                              KeyDown(rail.Rows(0), Keys.Up)
                              Assert.Equal(0, TabStopIndex(rail))         ' no wrap

                              Assert.Equal(0, clicks)                     ' not one page switch
                              Assert.Equal(4, rail.SelectedIndex)         ' the page that is shown did not change
                              Assert.Equal(1, SelectedCount(rail))
                              Assert.Equal(1, rail.TabStopCount())
                          Finally
                              form.Hide()
                          End Try
                      End Using
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Enter_and_space_choose_the_focused_row_through_the_click_path_once()
        OnSta(Sub()
                  Using font As New Font("Segoe UI Semibold", 8.5F)
                      Using form As New Form()
                          Dim rail As SettingsRailList = NewRail(EnglishCaptions, font, 1.0F)
                          ' The focus rests outside the list, as it does when the user is on another part of the window.
                          Dim outside As New TextBox()
                          form.Controls.Add(outside)
                          form.Controls.Add(rail)
                          form.Show()
                          outside.Select()
                          Try
                              Dim clicked As New List(Of Integer)()
                              For Each row As SettingsRailRow In rail.Rows
                                  Dim captured As SettingsRailRow = row
                                  AddHandler row.Click, Sub() clicked.Add(CInt(captured.Tag))
                              Next

                              Dim enter As KeyEventArgs = KeyDown(rail.Rows(3), Keys.Return)
                              Assert.True(enter.Handled)
                              Assert.Equal(New Integer() {3}, clicked.ToArray())

                              Dim space As KeyEventArgs = KeyDown(rail.Rows(7), Keys.Space)
                              Assert.True(space.Handled)
                              Assert.Equal(New Integer() {3, 7}, clicked.ToArray())
                              ' The button's own key-up click would choose the page a second time.
                              KeyUp(rail.Rows(7), Keys.Space)
                              Assert.Equal(New Integer() {3, 7}, clicked.ToArray())
                          Finally
                              form.Hide()
                          End Try
                      End Using
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Left_and_right_do_nothing_and_a_modified_key_is_left_to_the_window()
        OnSta(Sub()
                  Using font As New Font("Segoe UI Semibold", 8.5F)
                      Using form As New Form()
                          Dim rail As SettingsRailList = NewRail(EnglishCaptions, font, 1.0F)
                          ' The focus rests outside the list, as it does when the user is on another part of the window.
                          Dim outside As New TextBox()
                          form.Controls.Add(outside)
                          form.Controls.Add(rail)
                          form.Show()
                          outside.Select()
                          Try
                              Dim clicks As Integer = 0
                              For Each row As SettingsRailRow In rail.Rows
                                  AddHandler row.Click, Sub() clicks += 1
                              Next
                              rail.SelectedIndex = 2

                              Assert.True(KeyDown(rail.Rows(2), Keys.Left).Handled)
                              Assert.True(KeyDown(rail.Rows(2), Keys.Right).Handled)
                              Assert.Equal(2, TabStopIndex(rail))
                              Assert.Equal(0, clicks)

                              Assert.False(KeyDown(rail.Rows(2), Keys.Down, Keys.Control).Handled)
                              Assert.False(KeyDown(rail.Rows(2), Keys.Return, Keys.Alt).Handled)
                              Assert.Equal(2, TabStopIndex(rail))
                              Assert.Equal(0, clicks)
                          Finally
                              form.Hide()
                          End Try
                      End Using
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_row_claims_the_navigation_keys_but_lets_tab_leave_the_rail()
        OnSta(Sub()
                  Using font As New Font("Segoe UI Semibold", 8.5F)
                      Using row As SettingsRailRow = NewRow("General", 0, font, 1.0F)
                          For Each key As Keys In {Keys.Up, Keys.Down, Keys.Home, Keys.End, Keys.Left, Keys.Right, Keys.Return}
                              Assert.True(IsInputKey(row, key), key.ToString() & " must reach the row.")
                          Next
                          Assert.False(IsInputKey(row, Keys.Tab), "Tab leaves the rail.")
                          Assert.False(IsInputKey(row, Keys.Tab Or Keys.Shift), "Shift+Tab leaves the rail.")
                          Assert.False(IsInputKey(row, Keys.Escape), "Escape closes the window.")
                      End Using
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub The_tab_stop_returns_to_the_selected_row_when_the_focus_leaves()
        OnSta(Sub()
                  Using font As New Font("Segoe UI Semibold", 8.5F)
                      Using form As New Form()
                          Dim rail As SettingsRailList = NewRail(EnglishCaptions, font, 1.0F)
                          ' The focus rests outside the list, as it does when the user is on another part of the window.
                          Dim outside As New TextBox()
                          form.Controls.Add(outside)
                          form.Controls.Add(rail)
                          form.Show()
                          outside.Select()
                          Try
                              rail.SelectedIndex = 1
                              KeyDown(rail.Rows(1), Keys.End)
                              Assert.Equal(10, TabStopIndex(rail))
                              Dim leave As MethodInfo = GetType(SettingsRailList).GetMethod("OnLeave", BindingFlags.Instance Or BindingFlags.NonPublic)
                              leave.Invoke(rail, {EventArgs.Empty})
                              Assert.Equal(1, TabStopIndex(rail))
                              Assert.Equal(1, rail.TabStopCount())
                          Finally
                              form.Hide()
                          End Try
                      End Using
                  End Using
              End Sub)
    End Sub

    ' --- reading direction (rule 1) ------------------------------------------------------------

    <Fact>
    Public Sub The_rail_docks_at_the_reading_start_with_an_explicit_physical_side()
        Assert.Equal(DockStyle.Left, SettingsRailList.DockFor(False))
        Assert.Equal(DockStyle.Right, SettingsRailList.DockFor(True))
        ' A null side panel is a no-op, not a crash.
        SettingsRailList.ApplyDirection(Nothing, True)
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub Under_right_to_left_the_rail_ends_up_on_the_right_of_a_real_window(isRtl As Boolean)
        OnSta(Sub()
                  Using form As New Form()
                      form.ClientSize = New Size(800, 500)
                      form.RightToLeft = If(isRtl, RightToLeft.Yes, RightToLeft.No)
                      Dim content As New Panel With {.Dock = DockStyle.Fill}
                      Dim side As New Panel With {.Width = 200}
                      form.Controls.Add(content)
                      form.Controls.Add(side)
                      SettingsRailList.ApplyDirection(side, form.RightToLeft = RightToLeft.Yes)
                      form.Show()
                      Try
                          form.PerformLayout()
                          Assert.False(form.RightToLeftLayout, "The mirroring flag must never be set.")
                          If isRtl Then
                              Assert.Equal(DockStyle.Right, side.Dock)
                              Assert.Equal(form.ClientSize.Width, side.Right)
                              Assert.Equal(0, content.Left)
                          Else
                              Assert.Equal(DockStyle.Left, side.Dock)
                              Assert.Equal(0, side.Left)
                              Assert.Equal(form.ClientSize.Width, content.Right)
                          End If
                          ' A direction change at run time (a language switch) moves it back and forth.
                          form.RightToLeft = If(isRtl, RightToLeft.No, RightToLeft.Yes)
                          SettingsRailList.ApplyDirection(side, form.RightToLeft = RightToLeft.Yes)
                          form.PerformLayout()
                          If isRtl Then
                              Assert.Equal(0, side.Left)
                          Else
                              Assert.Equal(form.ClientSize.Width, side.Right)
                          End If
                      Finally
                          form.Hide()
                      End Try
                  End Using
              End Sub)
    End Sub

    ' --- sizes (rule 5) and the narrow window (rule 6) -----------------------------------------

    <Theory>
    <InlineData(1.0F)>
    <InlineData(1.5F)>
    Public Sub Every_row_is_at_least_the_target_tall_and_every_caption_stays_whole_in_every_language(factor As Single)
        Dim settings As SourceSettings = ReadSourceSettings()
        Dim entries As List(Of NavCaption) = ReadNavCaptions()
        Dim problems As New List(Of String)()
        OnSta(Sub()
                  Dim clientPx As Integer = MinimumClientWidth(settings, factor)
                  For language As Integer = 0 To Localization.Codes.Length - 1
                      Dim probe As LanguageProbe = Measure(language, entries, settings, factor, clientPx)
                      Dim target As Integer = SettingsRailRules.RowTargetPx(factor)
                      If probe.ShortestRow < target Then
                          problems.Add(String.Format("{0} x{1}: a row is {2} px, below the {3} px target", probe.Code, factor, probe.ShortestRow, target))
                      End If
                      If probe.Overlap Then
                          problems.Add(String.Format("{0} x{1}: two rows overlap", probe.Code, factor))
                      End If
                      If probe.Clipped <> "" Then
                          problems.Add(String.Format("{0} x{1}: caption does not fit its row - {2}", probe.Code, factor, probe.Clipped))
                      End If
                      If probe.WordBroken <> "" Then
                          problems.Add(String.Format("{0} x{1}: a word is wider than its row ({2} px available) - {3}", probe.Code, factor, probe.AvailablePx, probe.WordBroken))
                      End If
                  Next
              End Sub)
        Assert.True(problems.Count = 0, String.Join(vbLf, problems))
    End Sub

    <Theory>
    <InlineData(1.0F)>
    <InlineData(1.5F)>
    Public Sub At_the_minimum_window_width_the_content_column_keeps_480_logical_px_in_every_language(factor As Single)
        Dim settings As SourceSettings = ReadSourceSettings()
        Dim entries As List(Of NavCaption) = ReadNavCaptions()
        Dim probes As New List(Of LanguageProbe)()
        Dim clientPx As Integer = 0
        OnSta(Sub()
                  clientPx = MinimumClientWidth(settings, factor)
                  For language As Integer = 0 To Localization.Codes.Length - 1
                      probes.Add(Measure(language, entries, settings, factor, clientPx))
                  Next
              End Sub)

        output.WriteLine(String.Format(CultureInfo.InvariantCulture,
            "Interface factor {0}: minimum window {1} logical px, client {2} device px ({3:0.0} logical).",
            factor, settings.MinWidthLogical, clientPx, clientPx / factor))
        output.WriteLine("lang  longest-caption  rail  content(logical)  max-lines")
        For Each probe As LanguageProbe In probes
            output.WriteLine(String.Format(CultureInfo.InvariantCulture, "{0,-4}  {1,15}  {2,4}  {3,16:0.0}  {4,9}",
                                           probe.Code, probe.LongestPx, probe.RailPx, probe.ContentLogical, probe.MaxLines))
        Next
        Dim worst As LanguageProbe = probes.OrderBy(Function(p) p.ContentLogical).First()
        output.WriteLine(String.Format(CultureInfo.InvariantCulture, "WORST x{0}: {1} - content {2:0.0} logical px (rail {3} px of {4}).",
                                       factor, worst.Code, worst.ContentLogical, worst.RailPx, clientPx))

        Assert.Equal(Localization.Codes.Length, probes.Count)
        Assert.True(worst.ContentLogical >= MinContentLogical,
                    String.Format(CultureInfo.InvariantCulture,
                                  "At the minimum width ({0} logical px window, x{1}) the content column is {2:0.0} logical px in '{3}', below {4}.",
                                  settings.MinWidthLogical, factor, worst.ContentLogical, worst.Code, MinContentLogical))
    End Sub

    <Fact>
    Public Sub The_rail_width_follows_the_captions_it_is_not_a_fixed_number()
        Dim settings As SourceSettings = ReadSourceSettings()
        Dim entries As List(Of NavCaption) = ReadNavCaptions()
        Dim widths As New Dictionary(Of String, Integer)()
        OnSta(Sub()
                  ' A roomy window, so the share limit does not decide: only the captions do.
                  For language As Integer = 0 To Localization.Codes.Length - 1
                      Dim probe As LanguageProbe = Measure(language, entries, settings, 1.0F, 4000)
                      widths(probe.Code) = probe.RailPx
                  Next
              End Sub)
        Assert.True(widths.Values.Distinct().Count() > 1, "Thirteen languages gave one rail width: " & String.Join(",", widths.Values))
        Assert.True(widths.Values.All(Function(w) w > 0 AndAlso w < 4000 * SettingsRailRules.MaxShareOfClient + 1))
    End Sub

    ' --- the way the window wires the rail (source) --------------------------------------------

    <Fact>
    Public Sub The_window_builds_its_rail_from_the_rules_and_keeps_it_in_step()
        Dim text As String = BlankComments(File.ReadAllText(Path.Combine(SourceDir(), "Table_Form.ModernLayout.vb")))

        ' Rows are the list-item control, a row is not its own tab stop, a caption never ends in an ellipsis.
        Dim block As String = Between(text, "Dim button As New SettingsRailRow With {", "}")
        Assert.Contains("AutoEllipsis = False", block)
        Assert.DoesNotContain("TabStop", block)
        Assert.Contains("SettingsRailRules.RowTargetPx(settingsScale)", block)
        Assert.Contains("navigation.AddRow(button)", text)

        ' The width is measured, never a fixed Sp(218).
        Assert.Contains("SettingsRailRules.RailWidth(", text)
        Assert.DoesNotContain("Sp(218)", text)

        ' Rule 5 sizes come from the rules class.
        Dim glyphs As String = Between(text, "Private Sub RenderModernNavGlyph(", "End Sub")
        Assert.Contains("SettingsRailRules.GlyphPx(settingsScale)", glyphs)
        Assert.Contains("SettingsRailRules.GlyphGapPx(settingsScale)", glyphs)

        ' Rule 1: the side is explicit, re-evaluated on a language or direction change, and RightToLeftLayout is never set.
        Dim arrange As String = Between(text, "Private Sub ArrangeModernRail()", "End Sub")
        Assert.Contains("SettingsRailList.ApplyDirection(modernSettingsSidebar, RightToLeft = RightToLeft.Yes)", arrange)
        Assert.Matches("AddHandler RightToLeftChanged, Sub\(\) ArrangeModernRail\(\)", text)
        Dim localize As String = Between(text, "Private Sub LocalizeModernSettingsLayout()", "LinkLabel1.Text")
        Assert.Contains("ArrangeModernRail()", localize)
        Assert.DoesNotContain("RightToLeftLayout", text)

        ' Rule 7: the shown page is the selected row, and the list has a name.
        Dim header As String = Between(text, "Private Sub RefreshModernSettingsHeader()", "For buttonIndex")
        Assert.Contains("modernSettingsRail.SelectedIndex = index", header)
        Assert.Contains("modernSettingsRail.AccessibleName", localize)
        Assert.Contains("AccessibleName = navLabels(index)", localize)
    End Sub

    ' --- helpers -------------------------------------------------------------------------------

    Private NotInheritable Class NavCaption
        Friend ReadOnly Context As String
        Friend ReadOnly Russian As String

        Friend Sub New(context As String, russian As String)
            Me.Context = context
            Me.Russian = russian
        End Sub
    End Class

    Private NotInheritable Class SourceSettings
        Friend MinWidthLogical As Integer
        Friend SidePadding As Integer
        Friend Family As String
        Friend Points As Single
    End Class

    Private NotInheritable Class LanguageProbe
        Friend Code As String
        Friend LongestPx As Integer
        Friend RailPx As Integer
        Friend ContentLogical As Double
        Friend MaxLines As Integer
        Friend ShortestRow As Integer
        Friend Overlap As Boolean
        Friend Clipped As String = ""
        Friend WordBroken As String = ""
        Friend AvailablePx As Integer
    End Class

    ''' <summary>The nav captions as the window localizes them, read from <c>LocalizeModernSettingsLayout</c> so a page
    ''' added to the rail is measured here without a second list.</summary>
    Private Shared Function ReadNavCaptions() As List(Of NavCaption)
        Dim text As String = BlankComments(File.ReadAllText(Path.Combine(SourceDir(), "Table_Form.ModernLayout.vb")))
        Dim start As Integer = text.IndexOf("Private Sub LocalizeModernSettingsLayout()", StringComparison.Ordinal)
        Assert.True(start >= 0, "LocalizeModernSettingsLayout not found.")
        Dim blockStart As Integer = text.IndexOf("Dim navLabels As String() = {", start, StringComparison.Ordinal)
        Assert.True(blockStart >= 0, "navLabels not found.")
        Dim blockEnd As Integer = text.IndexOf("}"c, blockStart)
        Dim block As String = text.Substring(blockStart, blockEnd - blockStart)
        Dim list As New List(Of NavCaption)()
        For Each m As Match In Regex.Matches(block, "Localization\.T(C?)\(\s*""([^""]+)""(?:\s*,\s*""([^""]+)"")?\s*\)")
            If m.Groups(1).Value = "C" Then
                list.Add(New NavCaption(m.Groups(2).Value, m.Groups(3).Value))
            Else
                list.Add(New NavCaption("", m.Groups(2).Value))
            End If
        Next
        Assert.Equal(11, list.Count)
        Return list
    End Function

    Private Shared Function ReadSourceSettings() As SourceSettings
        Dim text As String = BlankComments(File.ReadAllText(Path.Combine(SourceDir(), "Table_Form.ModernLayout.vb")))
        Dim result As New SourceSettings()
        Dim minimum As Match = Regex.Match(text, "MinimumSize = New Size\(Sp\((\d+)\)")
        Assert.True(minimum.Success, "MinimumSize not found.")
        result.MinWidthLogical = Integer.Parse(minimum.Groups(1).Value, CultureInfo.InvariantCulture)
        Dim side As Match = Regex.Match(text, "modernSettingsSidebar = New Panel With \{[^}]*?\.Padding = New Padding\((\d+),\s*\d+,\s*(\d+),\s*\d+\)")
        Assert.True(side.Success, "The sidebar's padding was not found.")
        result.SidePadding = Integer.Parse(side.Groups(1).Value, CultureInfo.InvariantCulture) + Integer.Parse(side.Groups(2).Value, CultureInfo.InvariantCulture)
        Dim block As String = Between(text, "Dim button As New SettingsRailRow With {", "}")
        Dim font As Match = Regex.Match(block, "\.Font = SettingsFont\(""([^""]+)"",\s*Sf\(([\d.]+)F\)")
        Assert.True(font.Success, "The row font was not found.")
        result.Family = font.Groups(1).Value
        result.Points = Single.Parse(font.Groups(2).Value, CultureInfo.InvariantCulture)
        Return result
    End Function

    ''' <summary>The window's client width at its minimum size, device px: the minimum (the form's own width, frame
    ''' included, scaled like <c>Sp</c>) less the frame, which a real 96 dpi form reports and which grows with the factor.</summary>
    Private Shared Function MinimumClientWidth(settings As SourceSettings, factor As Single) As Integer
        Dim frame As Integer
        Using probe As New Form()
            probe.FormBorderStyle = FormBorderStyle.Sizable
            GC.KeepAlive(probe.Handle)
            frame = probe.Width - probe.ClientSize.Width
        End Using
        Dim minimum As Integer = CInt(Math.Round(settings.MinWidthLogical * factor))
        Return minimum - CInt(Math.Round(frame * factor))
    End Function

    Private Shared Function Measure(language As Integer, entries As List(Of NavCaption), settings As SourceSettings,
                                    factor As Single, clientPx As Integer) As LanguageProbe
        Dim captions As List(Of String) = entries.Select(Function(entry) Caption(entry, language)).ToList()
        Dim probe As New LanguageProbe With {.Code = Localization.Codes(language)}
        Using font As New Font(settings.Family, settings.Points * factor, FontStyle.Regular)
            Using rail As SettingsRailList = NewRail(captions, font, factor)
                Dim glyph As Integer = SettingsRailRules.GlyphPx(factor)
                Dim gap As Integer = SettingsRailRules.GlyphGapPx(factor)
                Dim leading As Integer = UiSizing.DevicePx(SettingsRailList.LeadingInsetLogical, factor)
                Dim trailing As Integer = UiSizing.DevicePx(SettingsRailList.TrailingInsetLogical, factor)
                Dim rowGap As Integer = UiSizing.DevicePx(SettingsRailList.RowGapLogical, factor)

                probe.LongestPx = rail.LongestCaptionPx()
                probe.RailPx = SettingsRailRules.RailWidth(probe.LongestPx, glyph, gap, SettingsRailList.ChromePx(settings.SidePadding, factor), clientPx)
                probe.ContentLogical = (clientPx - probe.RailPx) / CDbl(factor)

                Dim rowWidth As Integer = Math.Max(1, probe.RailPx - settings.SidePadding - leading - trailing)
                rail.LayoutRows(rowWidth, leading, rowGap, factor)

                Dim slack As Integer = glyph + gap + UiSizing.DevicePx(SettingsRailList.RenderSlackLogical, factor)
                probe.AvailablePx = Math.Max(1, rowWidth - rail.Rows(0).Padding.Horizontal - slack)
                Dim lineHeight As Integer = TextRenderer.MeasureText("Ag", font, New Size(Integer.MaxValue, Integer.MaxValue), TextFormatFlags.SingleLine).Height
                probe.ShortestRow = Integer.MaxValue
                Dim previousBottom As Integer = Integer.MinValue
                For Each row As SettingsRailRow In rail.Rows
                    probe.ShortestRow = Math.Min(probe.ShortestRow, row.Height)
                    If previousBottom <> Integer.MinValue AndAlso row.Top < previousBottom Then probe.Overlap = True
                    previousBottom = row.Bottom
                    Dim captionHeight As Integer = TextRenderer.MeasureText(row.Text, font, New Size(probe.AvailablePx, Integer.MaxValue), TextFormatFlags.WordBreak).Height
                    probe.MaxLines = Math.Max(probe.MaxLines, CInt(Math.Round(captionHeight / CDbl(Math.Max(1, lineHeight)))))
                    If row.Height < captionHeight Then probe.Clipped = row.Text & " (" & row.Height & " < " & captionHeight & ")"
                    For Each word As String In row.Text.Split(" "c)
                        If word.Length = 0 Then Continue For
                        Dim wordWidth As Integer = TextRenderer.MeasureText(word, font, New Size(Integer.MaxValue, Integer.MaxValue), TextFormatFlags.SingleLine).Width
                        If wordWidth > probe.AvailablePx Then probe.WordBroken = word & " " & wordWidth & " px"
                    Next
                Next
            End Using
        End Using
        Return probe
    End Function

    ''' <summary>A caption in a language, the way <c>Localization.T</c> / <c>TC</c> would answer, read from the table
    ''' without switching the process-wide language (other tests run beside this one).</summary>
    Private Shared Function Caption(entry As NavCaption, language As Integer) As String
        If language = 0 Then Return entry.Russian
        Dim table As Dictionary(Of String, String()) = Translations
        Dim values As String() = Nothing
        If entry.Context.Length > 0 AndAlso table.TryGetValue(entry.Context & ChrW(1) & entry.Russian, values) Then
            Dim scoped As String = values(language - 1)
            If scoped.Length = 0 Then scoped = values(0)
            If scoped.Length > 0 Then Return scoped
        End If
        If Not table.TryGetValue(entry.Russian, values) Then Return entry.Russian
        Dim value As String = values(language - 1)
        If value.Length = 0 Then value = values(0)
        Return If(value.Length = 0, entry.Russian, value)
    End Function

    Private Shared Function NewRow(caption As String, index As Integer, font As Font, factor As Single) As SettingsRailRow
        Return New SettingsRailRow With {
            .Name = "settingsNavTest" & index.ToString(CultureInfo.InvariantCulture),
            .Tag = index,
            .Text = caption,
            .AccessibleName = caption,
            .FlatStyle = FlatStyle.Flat,
            .Font = font,
            .AutoEllipsis = False,
            .Padding = New Padding(UiSizing.DevicePx(SettingsRailList.RowPaddingStartLogical, factor), 0,
                                   UiSizing.DevicePx(SettingsRailList.RowPaddingEndLogical, factor), 0),
            .Height = SettingsRailRules.RowTargetPx(factor)
        }
    End Function

    Private Shared Function NewRail(captions As IList(Of String), font As Font, factor As Single) As SettingsRailList
        Dim rail As New SettingsRailList With {.Dock = DockStyle.Fill}
        For index As Integer = 0 To captions.Count - 1
            rail.AddRow(NewRow(captions(index), index, font, factor))
        Next
        Return rail
    End Function

    Private Shared Function SelectedCount(rail As SettingsRailList) As Integer
        Return rail.Rows.Where(Function(row) row.Selected).Count()
    End Function

    Private Shared Function SelectedStateCount(rail As SettingsRailList) As Integer
        Return rail.Rows.Where(Function(row) (row.AccessibilityObject.State And AccessibleStates.Selected) = AccessibleStates.Selected).Count()
    End Function

    ''' <summary>The index of the one row that is a tab stop (the test fails when it is not exactly one).</summary>
    Private Shared Function TabStopIndex(rail As SettingsRailList) As Integer
        Assert.Equal(1, rail.TabStopCount())
        For index As Integer = 0 To rail.Rows.Count - 1
            If rail.Rows(index).TabStop Then Return index
        Next
        Return -1
    End Function

    Private Shared Function KeyDown(row As SettingsRailRow, key As Keys, Optional modifiers As Keys = Keys.None) As KeyEventArgs
        Dim e As New KeyEventArgs(key Or modifiers)
        Dim method As MethodInfo = GetType(SettingsRailRow).GetMethod("OnKeyDown", BindingFlags.Instance Or BindingFlags.NonPublic)
        method.Invoke(row, New Object() {e})
        Return e
    End Function

    Private Shared Sub KeyUp(row As SettingsRailRow, key As Keys)
        Dim method As MethodInfo = GetType(SettingsRailRow).GetMethod("OnKeyUp", BindingFlags.Instance Or BindingFlags.NonPublic)
        method.Invoke(row, New Object() {New KeyEventArgs(key)})
    End Sub

    Private Shared Function IsInputKey(row As SettingsRailRow, key As Keys) As Boolean
        Dim method As MethodInfo = GetType(SettingsRailRow).GetMethod("IsInputKey", BindingFlags.Instance Or BindingFlags.NonPublic)
        Return CBool(method.Invoke(row, New Object() {key}))
    End Function

    Private Shared Sub OnSta(body As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         body()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(90)), "The rail test did not finish.")
        If failure IsNot Nothing Then ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    Private Shared Function Between(text As String, startMarker As String, endMarker As String) As String
        Dim start As Integer = text.IndexOf(startMarker, StringComparison.Ordinal)
        Assert.True(start >= 0, "Not found in the source: " & startMarker)
        Dim finish As Integer = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal)
        Assert.True(finish >= 0, "No end marker " & endMarker & " after " & startMarker)
        Return text.Substring(start, finish - start)
    End Function

    Private Shared Function BlankComments(text As String) As String
        Return Regex.Replace(text, "(?m)^[ \t]*'.*$", "")
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
