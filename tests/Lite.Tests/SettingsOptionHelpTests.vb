#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' The help of the Settings window's boolean options (ticket 243, slice T-11b-help; BOOLEAN-OPTION 2.0 section 4):
''' <see cref="SettingsOptionHelp"/> names the options that carry a `?` and holds the ONE authored source of each.
'''
''' What is proven here, and how. The sources are checked in all thirteen languages (sound under <see cref="OptionHelpRules"/>,
''' unique keys, every placeholder filled, nothing left in Russian). The rows are the real controls and the real arrangement
''' (<see cref="SettingsCheckBox.ArrangeRow"/>) built the way <c>Table_Form.AddSettingRow</c> builds one, on an STA thread, at the
''' interface scales in Russian and English. Which rows of the window get a `?` is held by scanning the window's sources:
''' this test assembly links the controls and the rules, not <c>Table_Form</c> itself (a window that needs the whole viewer to
''' open), so the window's wiring is checked as text - the single place the help is attached, the language switch, and the two
''' theme walkers that must not style the `?` as a command button.
'''
''' Not covered here, by nature: the look in the real window on a 150/200 percent monitor, the live high-contrast switch, a
''' real pointer travelling to the popup, and a screen reader reading the names.
''' </summary>
<Collection("LocalizationState")>
Public Class SettingsOptionHelpTests

    Private Shared ReadOnly GlyphDir As String = Path.Combine(AppContext.BaseDirectory, "TestData", "glyphs")

    ''' <summary>The five options, in the order of the window.</summary>
    Private Shared ReadOnly Expected As String() = {"perspective", "dynamic_perspective", "animated_perspective", "reduce_motion", "ocr_auto"}

    ''' <summary>The options whose visible line already says all there is to say: owner rule "no nanny confirmations", never a `?`.</summary>
    Private Shared ReadOnly NeverHelp As String() = {"no_confirmation", "confirm_delete", "delete_to_recycle_bin"}

    ''' <summary>The Russian names of the other options each full text refers to (filled through placeholders in every language).</summary>
    Private Shared ReadOnly Mentions As New Dictionary(Of String, String())(StringComparer.Ordinal) From {
        {"perspective", New String() {"Цвет фона", "Динамический ореол", "Анимация ореола", "Перспективный фон"}},
        {"dynamic_perspective", New String() {"Анимация ореола"}},
        {"animated_perspective", New String() {"Уменьшать анимацию", "Перспективный фон", "Динамический ореол"}},
        {"reduce_motion", New String() {}},
        {"ocr_auto", New String() {}}}

    Shared Sub New()
        ButtonGlyphs.AssetOpener = Function(logicalName As String) As Stream
                                       Dim glyphFile As String = Path.Combine(GlyphDir, Path.GetFileName(logicalName))
                                       Return If(File.Exists(glyphFile), CType(File.OpenRead(glyphFile), Stream), Nothing)
                                   End Function
    End Sub

    ' ---------------------------------------------------------------- helpers

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
        Assert.True(thread.Join(TimeSpan.FromSeconds(180)), "the settings option-help check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    Private Shared Sub InLanguage(code As String, body As Action)
        Dim saved As String = Localization.CurrentCode
        Localization.CurrentCode = code
        Try
            body()
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    Private Shared Function SourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Common_Module.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("Could not locate the src\ directory from the test binary.")
    End Function

    Private Shared Function ReadSource(fileName As String) As String
        Return LocalizationCoverageTests.BlankComments(File.ReadAllText(Path.Combine(SourceDir(), fileName)))
    End Function

    ''' <summary>All the Table_Form*.vb sources of the window, comments blanked.</summary>
    Private Shared Function WindowSources() As String
        Dim files As String() = Directory.GetFiles(SourceDir(), "Table_Form*.vb")
        Assert.True(files.Length >= 5, "the window's sources were not found")
        Return String.Join(vbLf, files.Select(Function(f) LocalizationCoverageTests.BlankComments(File.ReadAllText(f))))
    End Function

    ''' <summary>The body of one method of a source: from its header to the next End Sub / End Function at class level.</summary>
    Private Shared Function BodyOf(code As String, header As String) As String
        Dim start As Integer = code.IndexOf(header, StringComparison.Ordinal)
        Assert.True(start >= 0, "not found in the window's source: " & header)
        Dim tail As String = code.Substring(start)
        Dim finish As Match = Regex.Match(tail, "\r?\n    End (Sub|Function)")
        Assert.True(finish.Success, "no End for: " & header)
        Return tail.Substring(0, finish.Index)
    End Function

    ''' <summary>The caption (ordinal 0) or the visible explanation (ordinal 1) of an option, read from the window's own tables
    ''' (ModernSettingTitle / ModernSettingDescription) so the rows below carry the words the window shows.</summary>
    Private Shared Function VisibleLine(key As String, ordinal As Integer) As String
        Dim code As String = ReadSource("Table_Form.ModernLayout.vb")
        Dim found As MatchCollection = Regex.Matches(code, "Case """ & Regex.Escape(key) & """ : Return Localization\.T\(""([^""]*)""\)")
        Assert.True(found.Count = 2, key & ": expected one title and one explanation in the window's tables, found " & found.Count)
        Return found(ordinal).Groups(1).Value
    End Function

    Private Shared Function LightPalette() As OptionHelpPalette
        Return New OptionHelpPalette With {.Surface = Color.FromArgb(240, 240, 240), .Ink = Color.FromArgb(20, 20, 20),
                                           .MutedInk = Color.FromArgb(90, 90, 90), .Border = Color.FromArgb(120, 120, 120),
                                           .Accent = Color.FromArgb(0, 90, 200)}
    End Function

    Private NotInheritable Class RowParts
        Public Form As Form
        Public Host As Panel
        Public Check As CheckBox
        Public Title As Label
        Public Description As Label
        Public Heading As OptionHeading
        Public Key As String
        Public Scale As Single
        Public Fonts As New List(Of Font)()
    End Class

    ''' <summary>A row built the way <c>Table_Form.AddSettingRow</c> builds one: the caption is still empty when the help is
    ''' attached, and the texts arrive with <see cref="ApplyTexts"/> (the window's RewriteModernSettingRows).</summary>
    Private Shared Function BuildRow(key As String, scale As Single, rtl As Boolean, width As Integer) As RowParts
        Dim parts As New RowParts() With {.Key = key, .Scale = scale}
        parts.Form = New Form With {.StartPosition = FormStartPosition.Manual, .Location = New Point(-20000, -20000),
                                    .ShowInTaskbar = False, .ClientSize = New Size(width + 40, 600)}
        parts.Host = New Panel With {.Left = 10, .Top = 10, .Width = width, .Height = SettingsCheckBox.Px(48, scale)}
        If rtl Then parts.Host.RightToLeft = RightToLeft.Yes
        Dim titleFont As New Font("Segoe UI Semibold", CSng(SettingsCheckBox.Px(12, scale)), FontStyle.Regular, GraphicsUnit.Pixel)
        Dim descriptionFont As New Font("Segoe UI", CSng(SettingsCheckBox.Px(SettingsRowGridRules.ExplanationFontPixels, scale)),
                                        FontStyle.Regular, GraphicsUnit.Pixel)
        parts.Fonts.Add(titleFont)
        parts.Fonts.Add(descriptionFont)
        parts.Title = New Label With {.AutoSize = False, .Font = titleFont, .ForeColor = LightPalette().Ink}
        parts.Description = New Label With {.AutoSize = False, .Font = descriptionFont}
        parts.Check = New CheckBox()
        parts.Host.Controls.Add(parts.Check)
        Dim light As OptionHelpPalette = LightPalette()
        SettingsCheckBox.Attach(parts.Check, scale)
        SettingsCheckBox.ApplyPalette(parts.Check, light.Surface, Color.White, light.Border, light.Ink, light.Accent, Color.White)
        SettingsCheckBox.BindCaption(parts.Title, parts.Check)
        parts.Host.Controls.Add(parts.Title)
        parts.Host.Controls.Add(parts.Description)
        parts.Heading = SettingsOptionHelp.Attach(parts.Host, parts.Check, parts.Title, parts.Description, scale, key)
        If parts.Heading IsNot Nothing Then
            parts.Heading.ApplyPalette(SettingsOptionHelp.PaletteOf(light.Surface, light.Ink, light.MutedInk, light.Border, light.Accent))
        End If
        parts.Form.Controls.Add(parts.Host)
        parts.Form.Show()
        Return parts
    End Function

    ''' <summary>The window's RewriteModernSettingRows for one boolean row, in the language now current: the texts, the
    ''' checkbox's accessibility, the help, then the arrangement.</summary>
    Private Shared Sub ApplyTexts(parts As RowParts)
        parts.Title.Text = Localization.T(VisibleLine(parts.Key, 0))
        parts.Description.Text = Localization.T(VisibleLine(parts.Key, 1))
        parts.Check.AccessibleName = parts.Title.Text
        parts.Check.AccessibleDescription = parts.Description.Text
        SettingsOptionHelp.Relocalize(parts.Check, parts.Key)
        SettingsCheckBox.ArrangeRow(parts.Host, parts.Check, parts.Title, parts.Description, parts.Scale)
    End Sub

    Private Shared Sub Release(parts As RowParts)
        parts.Form.Dispose()
        For Each font As Font In parts.Fonts
            font.Dispose()
        Next
        Assert.Equal(0, OptionHelpController.OpenCount)
    End Sub

    ' ---------------------------------------------------------------- the sources

    <Fact>
    Public Sub The_options_with_help_are_exactly_these_five_and_never_a_confirmation_option()
        Assert.Equal(Expected, SettingsOptionHelp.Keys)
        For Each key As String In Expected
            Assert.True(SettingsOptionHelp.HasHelp(key), key)
            Assert.NotNull(SettingsOptionHelp.SourceFor(key))
        Next
        For Each key As String In NeverHelp
            Assert.DoesNotContain(key, SettingsOptionHelp.Keys)
            Assert.False(SettingsOptionHelp.HasHelp(key), key & " must never carry a `?` (owner rule: no nanny confirmations)")
            Assert.Null(SettingsOptionHelp.SourceFor(key))
        Next
        Assert.Null(SettingsOptionHelp.SourceFor("animated_via_video"))
        Assert.Null(SettingsOptionHelp.SourceFor("no_such_option"))
        Assert.Null(SettingsOptionHelp.SourceFor(String.Empty))
    End Sub

    <Fact>
    Public Sub Every_source_is_sound_with_a_unique_key_filled_and_translated_in_all_thirteen_languages()
        Assert.Equal(13, Localization.Codes.Length)
        Dim russian As New Dictionary(Of String, OptionHelpSource)(StringComparer.Ordinal)
        InLanguage("ru", Sub()
                             For Each source As OptionHelpSource In SettingsOptionHelp.AllSources()
                                 russian(source.Key) = source
                             Next
                         End Sub)
        Assert.Equal(Expected.Length, russian.Count)

        For Each code As String In Localization.Codes
            InLanguage(code,
                Sub()
                    Dim sources As List(Of OptionHelpSource) = SettingsOptionHelp.AllSources()
                    Assert.Equal(Expected.Length, sources.Count)
                    Assert.Empty(OptionHelpRules.DuplicateKeys(sources))
                    For i As Integer = 0 To sources.Count - 1
                        Dim source As OptionHelpSource = sources(i)
                        Dim label As String = code & "/" & Expected(i)
                        Assert.NotNull(source)
                        Assert.Equal(Expected(i), source.Key)
                        Dim problems As List(Of OptionHelpProblem) = OptionHelpRules.Problems(source)
                        Assert.True(problems.Count = 0, label & ": " & String.Join(", ", problems))
                        Assert.Matches("^[a-z0-9._-]+$", source.Key)
                        ' Every placeholder was filled, in the right language.
                        Assert.DoesNotMatch("\{\d+\}", source.Summary)
                        Assert.DoesNotMatch("\{\d+\}", source.Full)
                        For Each ruName As String In Mentions(source.Key)
                            Assert.True(source.Full.Contains(Localization.T(ruName)), label & ": the full text does not name «" & Localization.T(ruName) & "»")
                        Next
                        ' House style: no smart double quote, no three dots.
                        For Each text As String In {source.Summary, source.Full}
                            Assert.False(text.Contains(ChrW(&H201C)), label & ": smart double quote U+201C")
                            Assert.False(text.Contains(ChrW(&H201D)), label & ": smart double quote U+201D")
                            Assert.DoesNotContain("...", text)
                        Next
                        If code <> "ru" Then
                            ' A missing entry returns the Russian key; Cyrillic belongs to ru and uk only.
                            Assert.NotEqual(russian(source.Key).Summary, source.Summary)
                            Assert.NotEqual(russian(source.Key).Full, source.Full)
                            If code <> "uk" Then
                                Assert.DoesNotMatch("[Ѐ-ӿ]", source.Summary)
                                Assert.DoesNotMatch("[Ѐ-ӿ]", source.Full)
                            End If
                        End If
                    Next
                End Sub)
        Next
    End Sub

    <Fact>
    Public Sub A_help_text_adds_facts_the_visible_line_does_not_and_keeps_out_the_claims_that_were_not_verified()
        InLanguage("ru",
            Sub()
                For Each key As String In Expected
                    Dim source As OptionHelpSource = SettingsOptionHelp.SourceFor(key)
                    Dim visible As String = Localization.T(VisibleLine(key, 1))
                    Assert.NotEqual(visible, source.Summary)
                    Assert.NotEqual(visible, source.Full)
                    Assert.True(source.Full.Length > visible.Length + 80, key & ": the full text adds almost nothing to the visible line")
                Next
                ' Removed after review: neither claim is true of the code.
                Dim ocr As String = SettingsOptionHelp.SourceFor("ocr_auto").Full
                Assert.DoesNotContain(".fd-sec", ocr)
                Assert.DoesNotContain("Языковые пакеты", ocr)
                ' The animation is available only while BOTH parents are on (Table_Form.SyncDynamicPerspectiveEnabled).
                Dim animated As String = SettingsOptionHelp.SourceFor("animated_perspective").Full
                Assert.Contains(Localization.T("Перспективный фон"), animated)
                Assert.Contains(Localization.T("Динамический ореол"), animated)
            End Sub)
    End Sub

    ' ---------------------------------------------------------------- which rows of the window get the `?`

    <Fact>
    Public Sub The_window_gives_a_help_button_to_exactly_the_listed_boolean_rows_and_no_other()
        Dim window As String = WindowSources()

        ' Every boolean row of the window: a preference check, or a row whose editor is declared as a CheckBox.
        Dim boolean_ As New HashSet(Of String)(StringComparer.Ordinal)
        For Each m As Match In Regex.Matches(window, "AddPreferenceCheck\(\s*\w+\s*,\s*""(\w+)""")
            boolean_.Add(m.Groups(1).Value)
        Next
        For Each m As Match In Regex.Matches(window, "AddSettingRow\(\s*\w+\s*,\s*""(\w+)""\s*,\s*(\w+)")
            If Regex.IsMatch(window, "\b" & Regex.Escape(m.Groups(2).Value) & "\s+As\s+(New\s+)?CheckBox\b") Then boolean_.Add(m.Groups(1).Value)
        Next
        Assert.True(boolean_.Count >= 20, "the boolean rows of the window were not found, only " & boolean_.Count)

        ' The listed options are boolean rows of the window (a typo or an option that became a list would show here).
        For Each key As String In Expected
            Assert.True(boolean_.Contains(key), key & " is not a boolean row of the Settings window")
        Next
        ' Every other boolean row has no source, so no `?`: the confirmation options above all.
        For Each key As String In boolean_
            If Array.IndexOf(Expected, key) >= 0 Then Continue For
            Assert.Null(SettingsOptionHelp.SourceFor(key))
        Next
        Assert.Contains("no_confirmation", boolean_)
        Assert.Contains("delete_to_recycle_bin", boolean_)
        Assert.Equal(Expected.Length, boolean_.Where(Function(k) SettingsOptionHelp.HasHelp(k)).Count())
    End Sub

    <Fact>
    Public Sub The_help_is_attached_in_one_place_and_follows_every_theme_and_language_change()
        Dim window As String = WindowSources()

        ' One attach for the whole window, inside the one row builder, gated by the option list.
        Assert.Equal(1, Regex.Matches(window, "SettingsOptionHelp\.Attach\(").Count)
        Assert.DoesNotContain("OptionHeading.Attach(", window)
        Dim addRow As String = BodyOf(window, "Private Sub AddSettingRow(")
        Assert.Contains("SettingsOptionHelp.Attach(", addRow)
        Assert.Contains("SettingsOptionHelp.HasHelp(key)", addRow)
        Assert.Contains("check IsNot Nothing", addRow)
        ' The preference checks go through that same builder.
        Assert.Contains("AddSettingRow(", BodyOf(window, "Private Sub AddPreferenceCheck("))

        ' A language switch (and a lazy page build) re-reads the help of every checkbox row.
        Dim rewrite As String = BodyOf(window, "Private Sub RewriteModernSettingRows(")
        Assert.Contains("SettingsOptionHelp.Relocalize(check, row.Key)", rewrite)
        Assert.True(rewrite.IndexOf("ConfigureModernCheckbox(check, row)", StringComparison.Ordinal) <
                    rewrite.IndexOf("SettingsOptionHelp.Relocalize(", StringComparison.Ordinal),
                    "the help is re-read after the caption and description are set")

        ' Both theme walkers leave the `?` alone: it is not a command button.
        Dim palette As String = BodyOf(window, "Private Sub ApplyModernPaletteToTree(")
        Assert.True(palette.IndexOf("TypeOf control Is OptionHelpButton", StringComparison.Ordinal) >= 0, "palette walker")
        Assert.True(palette.IndexOf("TypeOf control Is OptionHelpButton", StringComparison.Ordinal) <
                    palette.IndexOf("TypeOf control Is Button", StringComparison.Ordinal), "palette walker: the `?` is skipped before the Button branch")
        Assert.Contains("OptionHeading.Find(box)", palette)
        Assert.Contains("ApplyPalette(SettingsHelpPalette())", palette)
        Dim style As String = BodyOf(window, "Private Sub StyleModernSettingsControlTree(")
        Assert.True(style.IndexOf("TypeOf control Is OptionHelpButton", StringComparison.Ordinal) >= 0, "style walker")
        Assert.True(style.IndexOf("TypeOf control Is OptionHelpButton", StringComparison.Ordinal) <
                    style.IndexOf("button IsNot Nothing", StringComparison.Ordinal), "style walker: the `?` is skipped before the Button branch")
    End Sub

    <Fact>
    Public Sub The_help_belongs_to_the_modern_build_only_and_the_fallback_project_does_not_list_it()
        Dim code As String = File.ReadAllText(Path.Combine(SourceDir(), "SettingsOptionHelp.vb"))
        Assert.StartsWith("#If Not NETFRAMEWORK Then", code.TrimStart())
        Dim legacy As String = File.ReadAllText(Path.Combine(SourceDir(), "FastMediaSorter.vbproj"))
        Assert.DoesNotContain("SettingsOptionHelp", legacy)
    End Sub

    ' ---------------------------------------------------------------- the real rows

    <Fact>
    Public Sub A_row_with_help_names_the_option_and_the_button_carries_the_summary_in_both_languages()
        OnSta(
            Sub()
                For Each code As String In {"ru", "en"}
                    InLanguage(code,
                        Sub()
                            For Each key As String In Expected
                                Dim row As RowParts = BuildRow(key, 1.0F, False, SettingsCheckBox.Px(620, 1.0F))
                                Try
                                    ApplyTexts(row)
                                    Dim heading As OptionHeading = row.Heading
                                    Assert.NotNull(heading)
                                    Assert.True(heading.HasHelp, key)
                                    Assert.Null(heading.Glyph)
                                    Dim buttons As List(Of OptionHelpButton) = row.Host.Controls.OfType(Of OptionHelpButton)().ToList()
                                    Assert.Single(buttons)
                                    Dim help As OptionHelpButton = buttons(0)
                                    Assert.Same(heading.Help, help)
                                    Assert.Equal("optionHelp", help.Name)
                                    ' The checkbox is the one named option; the `?` is "Help: caption" and describes itself by the summary.
                                    Assert.Equal(row.Title.Text, row.Check.AccessibleName)
                                    Assert.Equal(row.Description.Text, row.Check.AccessibleDescription)
                                    Assert.Equal(OptionHelpRules.AccessibleName(row.Title.Text), help.AccessibleName)
                                    Dim source As OptionHelpSource = SettingsOptionHelp.SourceFor(key)
                                    Assert.Equal(source.Summary, help.AccessibleDescription)
                                    Assert.Equal(source.Key, heading.Controller.Source.Key)
                                    Assert.Equal(source.Full, heading.Controller.Source.Full)
                                    ' Focus order: the box, then its `?`.
                                    Assert.Equal(row.Check.TabIndex + 1, help.TabIndex)
                                Finally
                                    Release(row)
                                End Try
                            Next
                        End Sub)
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub A_row_of_an_option_without_help_gets_no_button_and_no_heading()
        OnSta(
            Sub()
                For Each key As String In NeverHelp.Concat({"animated_via_video", "show_file_size"})
                    Dim row As RowParts = BuildRow(key, 1.0F, False, SettingsCheckBox.Px(620, 1.0F))
                    Try
                        row.Title.Text = "Caption"
                        row.Description.Text = "Explanation."
                        SettingsOptionHelp.Relocalize(row.Check, key)
                        SettingsCheckBox.ArrangeRow(row.Host, row.Check, row.Title, row.Description, 1.0F)
                        Assert.Null(row.Heading)
                        Assert.Null(OptionHeading.Find(row.Check))
                        Assert.DoesNotContain(row.Host.Controls.Cast(Of Control)(), Function(c) TypeOf c Is OptionHelpButton)
                        Assert.Equal(3, row.Host.Controls.Count)
                    Finally
                        Release(row)
                    End Try
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub Rows_with_help_still_fit_their_page_at_the_usual_interface_scales_in_russian_and_english()
        OnSta(
            Sub()
                For Each code As String In {"ru", "en"}
                    InLanguage(code,
                        Sub()
                            For Each scale As Single In {1.0F, 1.25F, 1.5F, 1.75F, 2.0F}
                                For Each logicalWidth As Integer In {420, 760}
                                    For Each key As String In Expected
                                        Dim width As Integer = SettingsCheckBox.Px(logicalWidth, scale)
                                        Dim row As RowParts = BuildRow(key, scale, False, width)
                                        Try
                                            ApplyTexts(row)
                                            Dim label As String = code & " " & key & " scale=" & scale & " width=" & logicalWidth
                                            Dim help As OptionHelpButton = row.Heading.Help
                                            Dim boxes As New List(Of Rectangle) From {row.Check.Bounds, row.Title.Bounds, help.Bounds, row.Description.Bounds}
                                            For i As Integer = 0 To boxes.Count - 1
                                                Assert.True(row.Host.ClientRectangle.Contains(boxes(i)), label & ": part " & i & " leaves the row")
                                                For j As Integer = i + 1 To boxes.Count - 1
                                                    Assert.False(boxes(i).IntersectsWith(boxes(j)), label & ": parts " & i & " and " & j & " overlap")
                                                Next
                                            Next
                                            ' The text is never clipped: the row grows with it.
                                            Assert.True(row.Title.Height >= SettingsRowText.WrappedHeight(row.Title, row.Title.Width, 0, 0) - 1, label & ": caption clipped")
                                            Assert.True(row.Description.Height >= SettingsRowText.WrappedHeight(row.Description, row.Description.Width, 0, 0) - 1, label & ": explanation clipped")
                                            Assert.True(row.Host.Height >= row.Description.Bottom, label & ": row shorter than its explanation")
                                            Assert.True(row.Host.Height >= help.Bottom, label & ": row shorter than the help target")
                                            ' The help follows the name, on the first line of the heading, at the target size.
                                            Assert.True(help.Left >= row.Title.Left, label & ": help left of the caption")
                                            Assert.InRange(help.Top + help.Height \ 2, row.Title.Top, row.Title.Bottom)
                                            Assert.True(help.Width >= UiSizing.DevicePx(UiSizing.CompactTargetFloor, scale), label & ": below the compact floor")
                                        Finally
                                            Release(row)
                                        End Try
                                    Next
                                Next
                            Next
                        End Sub)
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub A_language_switch_rereads_the_help_into_the_same_button()
        OnSta(
            Sub()
                For Each key As String In Expected
                    Dim row As RowParts = BuildRow(key, 1.0F, False, SettingsCheckBox.Px(620, 1.0F))
                    Try
                        Dim russian As OptionHelpSource = Nothing
                        Dim button As OptionHelpButton = row.Heading.Help
                        Dim ruCaption As String = String.Empty
                        InLanguage("ru",
                            Sub()
                                ApplyTexts(row)
                                russian = SettingsOptionHelp.SourceFor(key)
                                ruCaption = row.Title.Text
                                Assert.Equal(russian.Summary, row.Heading.Controller.Source.Summary)
                                Assert.Equal(OptionHelpRules.AccessibleName(row.Title.Text), button.AccessibleName)
                            End Sub)

                        InLanguage("en",
                            Sub()
                                ApplyTexts(row)
                                Dim english As OptionHelpSource = SettingsOptionHelp.SourceFor(key)
                                Assert.NotEqual(russian.Summary, english.Summary)
                                Assert.Same(button, row.Heading.Help)
                                Assert.Equal(english.Summary, row.Heading.Controller.Source.Summary)
                                Assert.Equal(english.Full, row.Heading.Controller.Source.Full)
                                Assert.NotEqual(ruCaption, row.Title.Text)
                                Assert.Equal(OptionHelpRules.AccessibleName(row.Title.Text), button.AccessibleName)
                                Assert.Equal(english.Summary, button.AccessibleDescription)
                                Assert.Equal(row.Title.Text, row.Check.AccessibleName)
                            End Sub)
                        Assert.Equal(1, row.Host.Controls.OfType(Of OptionHelpButton)().Count())
                    Finally
                        Release(row)
                    End Try
                Next
            End Sub)
    End Sub
End Class
#End If
