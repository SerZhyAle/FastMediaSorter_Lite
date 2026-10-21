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
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' The boolean option row of the settings window against BOOLEAN-OPTION 2.0 (ticket 243, task T-5): the real
''' controls and the real row arrangement (<see cref="SettingsCheckBox.ArrangeRow"/>, the function the window's
''' row sizer calls), laid out on an STA thread. The window itself cannot be built in this project (LibVLC,
''' Tesseract), so what only the builder decides - the font role of the explanation, the absence of an ellipsis,
''' the wiring of the caption - is held by reading its source, the way SettingsLayoutTests does.
'''
''' Not covered here, by nature: a look at 150/200 percent on a real monitor and the live high-contrast switch.
''' </summary>
Public Class SettingsCheckBoxRowTests

    Private NotInheritable Class RowParts
        Public Host As Panel
        Public Check As CheckBox
        Public Title As Label
        Public Description As Label
        Public Fonts As New List(Of Font)()
    End Class

    Private Shared Sub OnSta(action As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         action()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(120)), "the row check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    ''' <summary>A row built the way AddSettingRow builds it: the caption in the 9 pt (12 px) role, the explanation in
    ''' the 12 logical px role, both pixel fonts so the test follows the scale like the window does.</summary>
    Private Shared Function BuildRow(scale As Single, rtl As Boolean, hostWidth As Integer,
                                     titleText As String, descriptionText As String) As RowParts
        Dim parts As New RowParts()
        parts.Host = New Panel With {.Width = hostWidth, .Height = SettingsCheckBox.Px(48, scale)}
        If rtl Then parts.Host.RightToLeft = RightToLeft.Yes
        Dim titleFont As New Font("Segoe UI Semibold", CSng(SettingsCheckBox.Px(12, scale)), FontStyle.Regular, GraphicsUnit.Pixel)
        Dim descriptionFont As New Font("Segoe UI",
            CSng(SettingsCheckBox.Px(SettingsRowGridRules.ExplanationFontPixels, scale)), FontStyle.Regular, GraphicsUnit.Pixel)
        parts.Fonts.Add(titleFont)
        parts.Fonts.Add(descriptionFont)
        parts.Title = New Label With {.AutoSize = False, .Text = titleText, .Font = titleFont}
        parts.Description = New Label With {.AutoSize = False, .Text = descriptionText, .Font = descriptionFont,
                                            .AutoEllipsis = True}
        parts.Check = New CheckBox()
        parts.Host.Controls.Add(parts.Check)
        parts.Host.Controls.Add(parts.Title)
        parts.Host.Controls.Add(parts.Description)
        SettingsCheckBox.Attach(parts.Check, scale)
        SettingsCheckBox.ApplyPalette(parts.Check, Color.FromArgb(240, 240, 240), Color.FromArgb(255, 255, 255),
                                      Color.FromArgb(120, 120, 120), Color.FromArgb(20, 20, 20),
                                      Color.FromArgb(0, 90, 200), Color.FromArgb(255, 255, 255))
        SettingsCheckBox.BindCaption(parts.Title, parts.Check)
        Return parts
    End Function

    Private Shared Sub Release(parts As RowParts)
        parts.Host.Dispose()
        For Each font As Font In parts.Fonts
            font.Dispose()
        Next
    End Sub

    ''' <summary>Raises the control's Click the way the message loop does (OnClick is protected).</summary>
    Private Shared Sub ClickOn(control As Control)
        Dim current As Type = control.GetType()
        Dim method As MethodInfo = Nothing
        While current IsNot Nothing AndAlso method Is Nothing
            method = current.GetMethod("OnClick", BindingFlags.Instance Or BindingFlags.NonPublic Or BindingFlags.DeclaredOnly,
                                       Nothing, New Type() {GetType(EventArgs)}, Nothing)
            current = current.BaseType
        End While
        Assert.NotNull(method)
        method.Invoke(control, New Object() {EventArgs.Empty})
    End Sub

    ' ---------------------------------------------------------------- section 3 rule 1 / 5

    <Fact>
    Public Sub Only_the_checkbox_and_its_caption_toggle_the_option_the_explanation_does_not()
        OnSta(
            Sub()
                Dim row As RowParts = BuildRow(1.0F, False, 520, "Caption", "Explanation that says what the option does.")
                Try
                    Assert.False(row.Check.Checked)

                    ClickOn(row.Description)
                    Assert.False(row.Check.Checked)
                    Assert.NotEqual(Cursors.Hand, row.Description.Cursor)

                    ClickOn(row.Title)
                    Assert.True(row.Check.Checked)
                    Assert.Equal(Cursors.Hand, row.Title.Cursor)
                    ClickOn(row.Title)
                    Assert.False(row.Check.Checked)

                    ' The checkbox itself toggles exactly once per click.
                    ClickOn(row.Check)
                    Assert.True(row.Check.Checked)
                    ClickOn(row.Description)
                    Assert.True(row.Check.Checked)
                Finally
                    Release(row)
                End Try
            End Sub)
    End Sub

    <Fact>
    Public Sub A_disabled_option_has_no_toggle_path_from_any_element()
        OnSta(
            Sub()
                Dim row As RowParts = BuildRow(1.0F, False, 520, "Caption", "Explanation. Available when the other option is on.")
                Try
                    row.Check.Checked = True
                    row.Check.Enabled = False
                    ClickOn(row.Title)
                    ClickOn(row.Description)
                    Assert.True(row.Check.Checked)
                    ' The reason stays visible: the explanation is still laid out and shown.
                    SettingsCheckBox.ArrangeRow(row.Host, row.Check, row.Title, row.Description, 1.0F)
                    Assert.True(row.Description.Visible)
                    Assert.True(row.Description.Height > 0)
                Finally
                    Release(row)
                End Try
            End Sub)
    End Sub

    ' ---------------------------------------------------------------- section 2 rule 5 and 6

    ''' <summary>The explanation texts of the boolean rows, as the window ships them.</summary>
    Private Shared Function BooleanExplanations() As List(Of String)
        Dim keys As New HashSet(Of String)(StringComparer.Ordinal)
        For Each sourceName As String In {"Table_Form.ModernLayout.vb", "Table_Form.ExpandedSettings.vb"}
            Dim text As String = LocalizationCoverageTests.BlankComments(File.ReadAllText(Path.Combine(SourceDir(), sourceName)))
            For Each m As Match In Regex.Matches(text, "AddSettingRow\(\w+, ""([a-z_0-9]+)"", [^,\r\n]+, 34\b")
                keys.Add(m.Groups(1).Value)
            Next
        Next
        Dim layout As String = LocalizationCoverageTests.BlankComments(File.ReadAllText(Path.Combine(SourceDir(), "Table_Form.ModernLayout.vb")))
        Dim start As Integer = layout.IndexOf("Function ModernSettingDescription", StringComparison.Ordinal)
        Assert.True(start > 0, "ModernSettingDescription not found")
        Dim finish As Integer = layout.IndexOf("End Function", start, StringComparison.Ordinal)
        Dim span As String = layout.Substring(start, finish - start)
        Dim result As New List(Of String)()
        For Each m As Match In Regex.Matches(span, "Case ((?:""[a-z_0-9]+""(?:, )?)+) : Return Localization\.T\(""([^""]+)""\)")
            Dim caseKeys As IEnumerable(Of String) = Regex.Matches(m.Groups(1).Value, """([a-z_0-9]+)""").Cast(Of Match)().Select(Function(k) k.Groups(1).Value)
            If caseKeys.Any(Function(k) keys.Contains(k)) Then result.Add(m.Groups(2).Value)
        Next
        Return result
    End Function

    <Fact>
    Public Sub The_scan_finds_the_explanations_of_the_boolean_rows()
        Dim texts As List(Of String) = BooleanExplanations()
        Assert.True(texts.Count >= 10, "Only " & texts.Count & " boolean explanations found - the scan lost the call sites.")
        Assert.True(texts.Max(Function(t) t.Length) > 80, "No long explanation among the boolean rows.")
    End Sub

    ''' <summary>The longest Russian explanations, then each of the 13 languages of them.</summary>
    Private Shared Function LongExplanationsInEveryLanguage() As List(Of String)
        Dim longest As List(Of String) = BooleanExplanations().OrderByDescending(Function(t) t.Length).Take(6).ToList()
        Dim result As New List(Of String)()
        For Each ru As String In longest
            For index As Integer = 0 To Localization.Codes.Length - 1
                result.Add(Translate(ru, index))
            Next
        Next
        ' And a text far past anything shipped: rows grow without limit (section 2 rule 5).
        result.Add(String.Join(" ", Enumerable.Repeat(longest(0), 4)))
        Return result
    End Function

    <Fact>
    Public Sub A_long_explanation_grows_the_row_and_is_never_clipped_at_100_and_150_percent()
        Dim texts As List(Of String) = LongExplanationsInEveryLanguage()
        OnSta(
            Sub()
                Dim failures As New List(Of String)()
                For Each scale As Single In {1.0F, 1.5F}
                    Dim hostWidth As Integer = SettingsCheckBox.Px(520, scale)
                    For Each text As String In texts
                        Dim row As RowParts = BuildRow(scale, False, hostWidth, "Caption of the option", text)
                        Try
                            ' A leftover ellipsis from the builder is switched off by the arrangement.
                            Assert.True(row.Description.AutoEllipsis)
                            SettingsCheckBox.ArrangeRow(row.Host, row.Check, row.Title, row.Description, scale)
                            ' What the label itself needs, and what GDI says for the same text and width.
                            Dim need As Integer = Math.Max(
                                row.Description.GetPreferredSize(New Size(row.Description.Width, 0)).Height,
                                TextRenderer.MeasureText(row.Description.Text, row.Description.Font,
                                                         New Size(row.Description.Width, Integer.MaxValue), TextFormatFlags.WordBreak).Height)
                            Dim problem As String = Nothing
                            If row.Description.AutoEllipsis Then
                                problem = "AutoEllipsis stayed on"
                            ElseIf row.Description.Height < need Then
                                problem = "label " & row.Description.Height & " px is shorter than its text " & need & " px"
                            ElseIf row.Description.Bottom > row.Host.Height Then
                                problem = "label bottom " & row.Description.Bottom & " is outside the row " & row.Host.Height
                            ElseIf row.Title.Bottom > row.Description.Top Then
                                problem = "title overlaps the explanation"
                            ElseIf row.Check.Top < 0 OrElse row.Check.Bottom > row.Host.Height Then
                                problem = "checkbox leaves the row"
                            ElseIf row.Description.Left < 0 OrElse row.Description.Right > row.Host.Width Then
                                problem = "explanation leaves the row horizontally"
                            ElseIf row.Description.Left < row.Check.Right Then
                                problem = "explanation overlaps the checkbox target"
                            End If
                            If problem IsNot Nothing Then
                                failures.Add(String.Format(CultureInfo.InvariantCulture, "{0:0.00}x ""{1}"": {2}", scale,
                                                           If(text.Length > 50, text.Substring(0, 50) & "..", text), problem))
                            End If
                        Finally
                            Release(row)
                        End Try
                    Next
                Next
                Assert.True(failures.Count = 0, "Rows that clip or overflow (" & failures.Count & "):" & vbLf & String.Join(vbLf, failures.Take(30)))
            End Sub)
    End Sub

    <Fact>
    Public Sub A_longer_text_makes_a_taller_row_and_the_checkbox_stays_on_the_first_heading_line()
        OnSta(
            Sub()
                Dim shortRow As RowParts = BuildRow(1.0F, False, 400, "Caption", "Short.")
                Dim longRow As RowParts = BuildRow(1.0F, False, 400, "Caption",
                                                   String.Join(" ", Enumerable.Repeat("A long explanation of what this option does.", 6)))
                Try
                    SettingsCheckBox.ArrangeRow(shortRow.Host, shortRow.Check, shortRow.Title, shortRow.Description, 1.0F)
                    SettingsCheckBox.ArrangeRow(longRow.Host, longRow.Check, longRow.Title, longRow.Description, 1.0F)
                    Assert.True(longRow.Host.Height > shortRow.Host.Height + 20, "the row did not grow with its text")
                    ' The box lines up with the first heading line, not with the middle of the long explanation.
                    Assert.Equal(shortRow.Check.Top, longRow.Check.Top)
                    Dim boxMiddle As Integer = longRow.Check.Top + longRow.Check.Height \ 2
                    Assert.InRange(boxMiddle, longRow.Title.Top, longRow.Title.Bottom)
                Finally
                    Release(shortRow)
                    Release(longRow)
                End Try
            End Sub)
    End Sub

    <Fact>
    Public Sub The_explanation_role_is_at_least_12_units_and_not_larger_than_the_caption()
        Assert.True(SettingsRowGridRules.ExplanationFontPixels >= 12)
        ' 9 pt at 96 dpi is 12 px: the caption is the ordinary control role, the explanation the next one down.
        Assert.True(SettingsRowGridRules.CaptionFontPoints * 96.0F / 72.0F >= SettingsRowGridRules.ExplanationFontPixels - 0.01F)
    End Sub

    ''' <summary>What only AddSettingRow and the window's palette walk decide, read from the sources.</summary>
    <Fact>
    Public Sub The_window_builds_the_explanation_in_the_12_unit_role_without_an_ellipsis_and_binds_only_the_caption()
        Dim layout As String = LocalizationCoverageTests.BlankComments(File.ReadAllText(Path.Combine(SourceDir(), "Table_Form.ModernLayout.vb")))
        Dim discovery As String = LocalizationCoverageTests.BlankComments(File.ReadAllText(Path.Combine(SourceDir(), "Table_Form.SettingsDiscovery.vb")))

        Dim builder As String = Slice(layout, "Private Sub AddSettingRow(", "Private Sub AddModernNumericUnit(")
        Dim descriptionLine As String = Regex.Match(builder, "Dim description As New Label With \{[^}]*\}", RegexOptions.Singleline).Value
        Assert.Contains("SettingsPixelFont(""Segoe UI"", SettingsRowGridRules.ExplanationFontPixels)", descriptionLine)
        Assert.DoesNotContain("AutoEllipsis", descriptionLine)
        Assert.Contains("SettingsCheckBox.BindCaption(title, check)", builder)
        ' The explanation is wired to nothing: no handler, no cursor, no tag naming the checkbox.
        Assert.DoesNotMatch("description\.(Click|MouseClick|MouseDown|MouseUp|DoubleClick|Tag|Cursor)", builder)
        Assert.DoesNotContain("ToggleModernCheckboxFromText", layout)

        ' The palette walk that runs after every page build must not put the explanation back to 8 pt.
        Dim tree As String = Slice(layout, "Private Sub StyleModernSettingsControlTree(", "Private Sub LayoutModernSettingsPages(")
        Assert.Contains("SettingsPixelFont(""Segoe UI"", SettingsRowGridRules.ExplanationFontPixels)", tree)

        ' The sizer never switches an ellipsis back on, for any row.
        Dim sizer As String = Slice(layout, "Private Sub SizeModernSettingRow(", "Private Function MakeModernControlStrip(")
        Assert.DoesNotContain("AutoEllipsis = True", sizer)
        Assert.Contains("SettingsCheckBox.ArrangeRow(", sizer)

        ' The dependency pass gives a disabled option no hand anywhere and the explanation never one.
        Assert.DoesNotMatch("Description\.Cursor = (row\.Title\.Cursor|Cursors\.Hand)", discovery)
    End Sub

    ' ---------------------------------------------------------------- section 2 rule 4

    <Fact>
    Public Sub The_hit_target_is_at_least_32_and_the_box_at_least_20_with_an_8_unit_gap()
        OnSta(
            Sub()
                For Each scale As Single In {1.0F, 1.5F, 2.0F}
                    Dim row As RowParts = BuildRow(scale, False, SettingsCheckBox.Px(520, scale), "Caption", "Explanation.")
                    Try
                        SettingsCheckBox.ArrangeRow(row.Host, row.Check, row.Title, row.Description, scale)
                        Assert.True(row.Check.Width >= SettingsCheckBox.Px(32, scale), "hit target width at " & scale)
                        Assert.True(row.Check.Height >= SettingsCheckBox.Px(32, scale), "hit target height at " & scale)
                        Assert.True(SettingsCheckBox.BoxSide(scale) >= SettingsCheckBox.Px(20, scale), "box at " & scale)
                        Dim inset As Integer = (row.Check.Width - SettingsCheckBox.BoxSide(scale)) \ 2
                        Dim boxRight As Integer = row.Check.Left + inset + SettingsCheckBox.BoxSide(scale)
                        Assert.Equal(SettingsCheckBox.Px(8, scale), row.Title.Left - boxRight)
                        Assert.Equal(row.Title.Left, row.Description.Left)
                        ' The hidden part of the hit target never reaches the text cell.
                        Assert.True(row.Check.Right <= row.Title.Left, "the hit target overlaps the text at " & scale)
                    Finally
                        Release(row)
                    End Try
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub The_painted_box_is_20_units_wide_inside_the_32_unit_target()
        OnSta(
            Sub()
                Using form As New Form()
                    form.StartPosition = FormStartPosition.Manual
                    form.Location = New Point(-20000, -20000)
                    ' Something else takes the focus, so no focus ring is drawn around the box being measured.
                    form.Controls.Add(New Button() With {.TabIndex = 0})
                    Dim check As New CheckBox() With {.TabIndex = 1}
                    form.Controls.Add(check)
                    form.Show()
                    SettingsCheckBox.Attach(check, 1.0F)
                    Dim surface As Color = Color.FromArgb(240, 240, 240)
                    SettingsCheckBox.ApplyPalette(check, surface, Color.White, Color.FromArgb(120, 120, 120),
                                                  Color.FromArgb(20, 20, 20), Color.FromArgb(0, 90, 200), Color.White)
                    Application.DoEvents()
                    Assert.Equal(32, check.Width)
                    Using bitmap As New Bitmap(check.Width, check.Height)
                        check.DrawToBitmap(bitmap, New Rectangle(0, 0, check.Width, check.Height))
                        Dim middle As Integer = check.Height \ 2
                        Dim first As Integer = -1
                        Dim last As Integer = -1
                        For x As Integer = 0 To bitmap.Width - 1
                            If bitmap.GetPixel(x, middle) <> surface Then
                                If first < 0 Then first = x
                                last = x
                            End If
                        Next
                        Assert.True(first >= 0, "no box painted")
                        Dim span As Integer = last - first + 1
                        Assert.InRange(span, 20, 22)
                        ' Centred: room for the focus ring on both sides.
                        Assert.InRange(first, 5, 7)
                    End Using
                    form.Hide()
                End Using
            End Sub)
    End Sub

    ' ---------------------------------------------------------------- section 2 rule 1 (RTL)

    <Fact>
    Public Sub Under_RightToLeft_the_checkbox_stands_at_the_right_and_the_text_cell_mirrors()
        OnSta(
            Sub()
                For Each scale As Single In {1.0F, 1.5F}
                    Dim width As Integer = SettingsCheckBox.Px(520, scale)
                    Dim ltr As RowParts = BuildRow(scale, False, width, "Caption", "Explanation of the option in a few words.")
                    Dim rtl As RowParts = BuildRow(scale, True, width, "Caption", "Explanation of the option in a few words.")
                    Try
                        SettingsCheckBox.ArrangeRow(ltr.Host, ltr.Check, ltr.Title, ltr.Description, scale)
                        SettingsCheckBox.ArrangeRow(rtl.Host, rtl.Check, rtl.Title, rtl.Description, scale)

                        ' LTR: box left, text to its right. RTL: box right, text to its left.
                        Assert.True(ltr.Check.Right <= ltr.Title.Left)
                        Assert.True(rtl.Check.Left >= rtl.Title.Right, "RTL: the text cell must be left of the checkbox")
                        Assert.True(rtl.Check.Left > width \ 2, "RTL: the checkbox must stand in the right half")
                        Assert.True(rtl.Check.Right <= width)

                        ' An exact mirror of the whole leading part.
                        Assert.Equal(width - ltr.Check.Right, rtl.Check.Left)
                        Assert.Equal(width - ltr.Title.Right, rtl.Title.Left)
                        Assert.Equal(width - ltr.Description.Right, rtl.Description.Left)
                        Assert.Equal(ltr.Check.Top, rtl.Check.Top)
                        Assert.Equal(ltr.Host.Height, rtl.Host.Height)

                        ' The gap from the visible box to the text is the same on both sides.
                        Dim inset As Integer = (rtl.Check.Width - SettingsCheckBox.BoxSide(scale)) \ 2
                        Assert.Equal(SettingsCheckBox.Px(8, scale), (rtl.Check.Left + inset) - rtl.Title.Right)

                        ' Reading direction of the text follows the host (RightToLeft only, never RightToLeftLayout);
                        ' the drawn box is symmetric, so it stays out of the mirroring.
                        Assert.Equal(RightToLeft.Yes, rtl.Title.RightToLeft)
                        Assert.Equal(RightToLeft.Yes, rtl.Description.RightToLeft)
                        Assert.Equal(RightToLeft.No, rtl.Check.RightToLeft)
                    Finally
                        Release(ltr)
                        Release(rtl)
                    End Try
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub A_narrow_row_keeps_a_positive_text_width_in_both_directions()
        For Each rtl As Boolean In {False, True}
            Dim place As SettingsCheckBox.Leading = SettingsCheckBox.PlaceLeading(60, rtl, 1.0F)
            Assert.True(place.TextWidth >= 40)
        Next
    End Sub

    ' ---------------------------------------------------------------- helpers

    Private Shared Function Slice(text As String, startMarker As String, endMarker As String) As String
        Dim start As Integer = text.IndexOf(startMarker, StringComparison.Ordinal)
        Assert.True(start >= 0, startMarker & " not found")
        Dim finish As Integer = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal)
        Assert.True(finish > start, endMarker & " not found after " & startMarker)
        Return text.Substring(start, finish - start)
    End Function

    Private Shared Function Translate(ru As String, index As Integer) As String
        If index = 0 Then Return ru
        Dim values As String() = Localization.All.
            Where(Function(kv) String.Equals(kv.Key, ru, StringComparison.Ordinal)).
            Select(Function(kv) kv.Value).FirstOrDefault()
        If values Is Nothing Then Return ru
        Dim value As String = values(index - 1)
        If value.Length = 0 Then value = values(0)
        Return If(value.Length = 0, ru, value)
    End Function

    Private Shared Function SourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Common_Module.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("Could not locate the src\ directory from the test binary.")
    End Function

End Class
#End If
