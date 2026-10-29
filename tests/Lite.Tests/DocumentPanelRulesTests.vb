#If Not NETFRAMEWORK Then
Option Strict On
Imports System.Collections.Generic
Imports System.Linq
Imports Xunit

''' <summary>
''' The pure half of the document panel (specification 280, T-1): the item lists and their fixed reading order, the
''' pinned core, the conditional items, the captions and the page / chapter box.
''' </summary>
Public Class DocumentPanelRulesTests

    Private Shared ReadOnly Readers As DocumentReaderKind() = {
        DocumentReaderKind.Pdf, DocumentReaderKind.Epub, DocumentReaderKind.Html, DocumentReaderKind.Text}

    ' --- the lists ------------------------------------------------------------------------------------------------

    <Fact>
    Public Sub Every_reader_has_items_and_no_id_stands_twice()
        For Each kind In Readers
            Dim ids = DocumentPanelRules.ItemIds(kind)
            Assert.NotEmpty(ids)
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count())
        Next
        Assert.Empty(DocumentPanelRules.ItemIds(DocumentReaderKind.None))
        Assert.Empty(DocumentPanelRules.Groups(DocumentReaderKind.None))
    End Sub

    <Fact>
    Public Sub The_reading_order_of_each_reader_is_the_one_of_the_specification()
        Assert.Equal("doc_Back doc_PrevPage doc_PagePosition doc_NextPage " &
                     "doc_PaneOutline doc_PaneSearch doc_PaneText doc_PaneBookmarks doc_PaneThumbnails doc_AddBookmark doc_Speak doc_StopSpeech " &
                     "doc_Rotate doc_Layout doc_ColorMode doc_ResetAppearance doc_OpenWith doc_Translate doc_Context",
                     String.Join(" ", DocumentPanelRules.ItemIds(DocumentReaderKind.Pdf)))
        Assert.Equal("doc_Back doc_PrevChapter doc_ChapterPosition doc_NextChapter " &
                     "doc_PaneOutline doc_PaneSearch doc_PaneBookmarks doc_AddBookmark doc_Copy doc_Speak doc_StopSpeech " &
                     "doc_ResetAppearance doc_OpenWith doc_Translate doc_Context",
                     String.Join(" ", DocumentPanelRules.ItemIds(DocumentReaderKind.Epub)))
        Assert.Equal("doc_Back doc_Forward doc_Reload doc_Find doc_FindPrev doc_FindNext doc_FindCount " &
                     "doc_ZoomOut doc_ZoomValue doc_ZoomIn doc_FitWidth doc_ReaderMode doc_ResetAppearance " &
                     "doc_ViewSource doc_OpenInBrowser doc_Print doc_Blocked doc_ReturnToFiles doc_Context",
                     String.Join(" ", DocumentPanelRules.ItemIds(DocumentReaderKind.Html)))
        Assert.Equal("doc_ZoomOut doc_ZoomIn doc_ZoomReset doc_Context",
                     String.Join(" ", DocumentPanelRules.ItemIds(DocumentReaderKind.Text)))
    End Sub

    <Fact>
    Public Sub No_command_that_a_floating_row_already_carries_is_repeated()
        For Each id In {"doc_PrevFile", "doc_NextFile", "doc_Settings"}
            Assert.True(DocumentPanelRules.IsRowCommand(id))
        Next
        For Each kind In Readers
            For Each id In DocumentPanelRules.ItemIds(kind)
                Assert.False(DocumentPanelRules.IsRowCommand(id), kind.ToString() & " repeats a row command: " & id)
            Next
        Next
    End Sub

    <Fact>
    Public Sub The_context_readout_ends_every_list_and_is_the_first_to_give_its_room_up()
        For Each kind In Readers
            Assert.Equal(DocumentPanelRules.IdContext, DocumentPanelRules.ItemIds(kind).Last())
        Next
        Assert.True(DocumentPanelRules.ReleasedFirst(DocumentPanelRules.IdContext))
        Assert.False(DocumentPanelRules.ReleasedFirst(DocumentPanelRules.IdFindCount))
    End Sub

    ' --- the pinned core and the conditional items ----------------------------------------------------------------

    <Fact>
    Public Sub The_pinned_core_is_the_position_box_of_a_paged_reader_and_the_html_find()
        Assert.Equal({"doc_PagePosition"}, CoreOf(DocumentReaderKind.Pdf))
        Assert.Equal({"doc_ChapterPosition"}, CoreOf(DocumentReaderKind.Epub))
        Assert.Equal({"doc_Find"}, CoreOf(DocumentReaderKind.Html))
        Assert.Empty(CoreOf(DocumentReaderKind.Text))
        Assert.Equal(DocumentItemKind.Position, DocumentPanelRules.KindOf(DocumentPanelRules.IdPagePosition))
        Assert.Equal(DocumentItemKind.Position, DocumentPanelRules.KindOf(DocumentPanelRules.IdChapterPosition))
        Assert.Equal(DocumentItemKind.FindBox, DocumentPanelRules.KindOf(DocumentPanelRules.IdFind))
    End Sub

    Private Shared Function CoreOf(kind As DocumentReaderKind) As String()
        Return DocumentPanelRules.ItemIds(kind).Where(Function(id) DocumentPanelRules.IsPinnedCore(kind, id)).ToArray()
    End Function

    <Fact>
    Public Sub Only_the_html_blocked_menu_and_view_source_are_conditional()
        For Each kind In Readers
            Dim conditional = DocumentPanelRules.ItemIds(kind).Where(Function(id) DocumentPanelRules.IsConditional(kind, id)).ToArray()
            If kind = DocumentReaderKind.Html Then
                Assert.Equal({"doc_ViewSource", "doc_Blocked"}, conditional)
            Else
                Assert.Empty(conditional)
            End If
        Next
    End Sub

    ' --- what an item is ------------------------------------------------------------------------------------------

    <Fact>
    Public Sub The_item_kinds_follow_the_table_of_the_specification()
        For Each id In {"doc_PaneOutline", "doc_PaneSearch", "doc_PaneText", "doc_PaneBookmarks", "doc_PaneThumbnails", "doc_ReaderMode"}
            Assert.Equal(DocumentItemKind.Toggle, DocumentPanelRules.KindOf(id))
        Next
        For Each id In {"doc_Layout", "doc_ColorMode", "doc_Blocked"}
            Assert.Equal(DocumentItemKind.MenuButton, DocumentPanelRules.KindOf(id))
        Next
        For Each id In {"doc_Context", "doc_FindCount"}
            Assert.Equal(DocumentItemKind.Readout, DocumentPanelRules.KindOf(id))
        Next
        For Each id In {"doc_Back", "doc_ZoomValue", "doc_Print", "doc_ReturnToFiles"}
            Assert.Equal(DocumentItemKind.Button, DocumentPanelRules.KindOf(id))
        Next
    End Sub

    <Fact>
    Public Sub Every_item_has_words_except_the_two_the_glue_formats()
        Dim withoutWords As New List(Of String)()
        For Each kind In Readers
            For Each id In DocumentPanelRules.ItemIds(kind)
                If DocumentPanelRules.CaptionKey(id).Length = 0 Then withoutWords.Add(id)
            Next
        Next
        Assert.Equal({"doc_Context", "doc_Blocked"}.OrderBy(Function(s) s), withoutWords.Distinct().OrderBy(Function(s) s))
    End Sub

    ''' <summary>The panel adds no word: every caption is already a key of the 13-language table. The translate command is
    ''' a product name and stays as it is.</summary>
    <Fact>
    Public Sub Every_caption_panel_name_and_menu_choice_is_already_in_the_localization_table()
        Dim known As New HashSet(Of String)(Localization.All.Select(Function(kv) StripContext(kv.Key)), StringComparer.Ordinal)
        Dim missing As New List(Of String)()
        For Each kind In Readers
            If Not known.Contains(DocumentPanelRules.PanelNameKey(kind)) Then missing.Add(kind.ToString() & " panel name")
            For Each id In DocumentPanelRules.ItemIds(kind)
                Dim key = DocumentPanelRules.CaptionKey(id)
                If key.Length > 0 AndAlso key <> "doc-html-translate" AndAlso Not known.Contains(key) Then missing.Add(id & " = " & key)
                For Each choice In DocumentPanelRules.MenuChoices(id)
                    If Not known.Contains(choice.Value) Then missing.Add(id & " choice " & choice.Key)
                Next
            Next
        Next
        Assert.True(missing.Count = 0, "Keys not in the localization table: " & String.Join("; ", missing.Distinct()))
        Assert.Equal("", DocumentPanelRules.PanelNameKey(DocumentReaderKind.None))
    End Sub

    Private Shared Function StripContext(key As String) As String
        Dim sep = key.IndexOf(ChrW(1))
        Return If(sep < 0, key, key.Substring(sep + 1))
    End Function

    <Fact>
    Public Sub The_two_choice_menus_offer_three_distinct_stored_values_each()
        For Each id In {"doc_Layout", "doc_ColorMode"}
            Dim choices = DocumentPanelRules.MenuChoices(id)
            Assert.Equal(3, choices.Length)
            Assert.Equal(3, choices.Select(Function(c) c.Key).Distinct().Count())
        Next
        Assert.Equal({"single", "continuous", "spread"}, DocumentPanelRules.MenuChoices("doc_Layout").Select(Function(c) c.Key))
        Assert.Equal({"original", "night", "sepia"}, DocumentPanelRules.MenuChoices("doc_ColorMode").Select(Function(c) c.Key))
        Assert.Empty(DocumentPanelRules.MenuChoices("doc_Print"))
    End Sub

    ' --- the position box -----------------------------------------------------------------------------------------

    Private Shared Sub AssertParsed(text As String, total As Integer, status As PositionParseStatus, value As Integer, clamped As Boolean)
        Dim result = DocumentPanelRules.ParsePosition(text, total)
        Dim shown As String = If(text, "<Nothing>")
        Assert.True(status = result.Status, "'" & shown & "' of " & total & ": status " & result.Status.ToString())
        Assert.True(value = result.Value, "'" & shown & "' of " & total & ": value " & result.Value)
        Assert.True(clamped = result.WasClamped, "'" & shown & "' of " & total & ": clamped " & result.WasClamped)
    End Sub

    <Fact>
    Public Sub A_number_inside_the_range_is_taken_as_typed()
        AssertParsed("7", 120, PositionParseStatus.Accepted, 7, False)
        AssertParsed("1", 120, PositionParseStatus.Accepted, 1, False)
        AssertParsed("120", 120, PositionParseStatus.Accepted, 120, False)
        AssertParsed("0042", 120, PositionParseStatus.Accepted, 42, False)
        AssertParsed("5", 5, PositionParseStatus.Accepted, 5, False)
    End Sub

    <Fact>
    Public Sub A_number_outside_the_range_is_pulled_in_like_the_numeric_box_did()
        AssertParsed("0", 120, PositionParseStatus.Accepted, 1, True)
        AssertParsed("000", 120, PositionParseStatus.Accepted, 1, True)
        AssertParsed("121", 120, PositionParseStatus.Accepted, 120, True)
        AssertParsed("99999999999999999999999999999999999999999", 120, PositionParseStatus.Accepted, 120, True)
        AssertParsed("0000000000000000000000000000000000000000005", 120, PositionParseStatus.Accepted, 5, False)
        AssertParsed("9999999999", Integer.MaxValue, PositionParseStatus.Accepted, Integer.MaxValue, True)
        AssertParsed("2147483647", Integer.MaxValue, PositionParseStatus.Accepted, Integer.MaxValue, False)
    End Sub

    <Fact>
    Public Sub Nothing_typed_leaves_the_old_value_and_says_nothing()
        AssertParsed("", 120, PositionParseStatus.Empty, 0, False)
        AssertParsed(Nothing, 120, PositionParseStatus.Empty, 0, False)
        AssertParsed("   ", 120, PositionParseStatus.Empty, 0, False)
        AssertParsed(vbCrLf, 120, PositionParseStatus.Empty, 0, False)
    End Sub

    <Fact>
    Public Sub White_space_around_the_digits_is_ignored_and_a_pasted_line_brings_its_line_end()
        AssertParsed("  7 ", 120, PositionParseStatus.Accepted, 7, False)
        AssertParsed("12" & vbCrLf, 120, PositionParseStatus.Accepted, 12, False)
        AssertParsed(vbTab & "12" & vbLf, 120, PositionParseStatus.Accepted, 12, False)
        AssertParsed(ChrW(&HA0) & "9" & ChrW(&H3000), 120, PositionParseStatus.Accepted, 9, False)
    End Sub

    <Fact>
    Public Sub Anything_that_is_not_a_run_of_digits_is_refused_and_never_throws()
        For Each sample In {"-5", "+5", "5 6", "1,5", "1.5", "12a", "a12", "page 3", "3/120", "1e3", "5" & vbCrLf & "6",
                          ChrW(&HB2).ToString(), ChrW(&HBD).ToString(), "x", "--", "0x10", "٣٤٥" & "ا", "１２３" & "。"}
            AssertParsed(sample, 120, PositionParseStatus.NotANumber, 0, False)
        Next
    End Sub

    <Fact>
    Public Sub Digits_of_other_scripts_count_as_digits_for_the_users_of_those_languages()
        Dim arabicIndic = ChrW(&H661).ToString() & ChrW(&H662)
        Dim easternArabic = ChrW(&H6F1).ToString() & ChrW(&H6F2)
        Dim devanagari = ChrW(&H967).ToString() & ChrW(&H968)
        Dim bengali = ChrW(&H9E7).ToString() & ChrW(&H9E8)
        Dim fullwidth = ChrW(&HFF11).ToString() & ChrW(&HFF12)
        For Each sample In {arabicIndic, easternArabic, devanagari, bengali, fullwidth}
            AssertParsed(sample, 120, PositionParseStatus.Accepted, 12, False)
        Next
        ' A digit from the supplementary planes is a surrogate pair: not a digit character here, refused rather than guessed.
        AssertParsed(Char.ConvertFromUtf32(&H1D7CF), 120, PositionParseStatus.NotANumber, 0, False)
    End Sub

    <Fact>
    Public Sub A_document_with_nothing_to_number_has_no_range()
        AssertParsed("5", 0, PositionParseStatus.NoRange, 0, False)
        AssertParsed("5", -3, PositionParseStatus.NoRange, 0, False)
        AssertParsed("", 0, PositionParseStatus.NoRange, 0, False)
    End Sub

    <Fact>
    Public Sub The_readout_is_current_over_total_with_numbers_only_and_a_stable_width()
        Dim format = DocumentPanelRules.PositionReadoutFormat
        Assert.Equal("{0} / {1}", format)
        Assert.Equal("3 / 120", DocumentPanelRules.PositionReadout(3, 120, format))
        Assert.Equal("1 / 120", DocumentPanelRules.PositionReadout(0, 120, format))
        Assert.Equal("120 / 120", DocumentPanelRules.PositionReadout(999, 120, format))
        Assert.Equal("", DocumentPanelRules.PositionReadout(1, 0, format))
        Assert.Equal("120 / 120", DocumentPanelRules.PositionReadoutReserved(120, format))
        Assert.True(DocumentPanelRules.PositionReadoutReserved(120, format).Length >= DocumentPanelRules.PositionReadout(99, 120, format).Length)
    End Sub

    ' --- glyphs ---------------------------------------------------------------------------------------------------

    <Fact>
    Public Sub Every_glyph_an_item_names_is_a_file_of_the_catalogue()
        Dim root As New System.IO.DirectoryInfo(AppContext.BaseDirectory)
        While root IsNot Nothing AndAlso Not System.IO.Directory.Exists(System.IO.Path.Combine(root.FullName, "assets", "glyphs"))
            root = root.Parent
        End While
        Assert.NotNull(root)
        Dim named As Integer
        For Each kind In Readers
            For Each id In DocumentPanelRules.ItemIds(kind)
                Dim glyph = DocumentPanelRules.GlyphId(id)
                If glyph.Length = 0 Then Continue For
                named += 1
                Assert.True(System.IO.File.Exists(System.IO.Path.Combine(root.FullName, "assets", "glyphs", glyph & ".svg")), id & " names a glyph the catalogue does not hold: " & glyph)
            Next
        Next
        Assert.True(named > 0)
    End Sub

    <Fact>
    Public Sub An_item_without_a_catalogue_meaning_keeps_its_word_and_the_readouts_never_take_a_glyph()
        For Each id In {DocumentPanelRules.IdPrevPage, DocumentPanelRules.IdNextPage, DocumentPanelRules.IdPagePosition, DocumentPanelRules.IdPaneOutline,
                        DocumentPanelRules.IdAddBookmark, DocumentPanelRules.IdRotate, DocumentPanelRules.IdLayout, DocumentPanelRules.IdColorMode,
                        DocumentPanelRules.IdContext, DocumentPanelRules.IdFindCount, DocumentPanelRules.IdZoomValue}
            Assert.Equal("", DocumentPanelRules.GlyphId(id))
            ' The context readout is formatted by the glue; every other glyph-less item has the word it shows.
            If id <> DocumentPanelRules.IdContext Then Assert.NotEqual("", DocumentPanelRules.CaptionKey(id))
        Next
        Assert.True(DocumentPanelRules.MirrorsInRtl(DocumentPanelRules.IdBack))
        Assert.True(DocumentPanelRules.MirrorsInRtl(DocumentPanelRules.IdForward))
        Assert.False(DocumentPanelRules.MirrorsInRtl(DocumentPanelRules.IdPrevPage))
    End Sub

End Class
#End If
