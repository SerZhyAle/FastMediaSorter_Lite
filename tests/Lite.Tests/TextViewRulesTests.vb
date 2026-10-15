#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports Xunit

''' <summary>
''' Ticket 124 UX-216: the text document can be selected from and copied, and says how far into the file the view
''' is. The rules are pure (<c>TextViewRules</c>) so they are held here without a window: where a point lands in the
''' text, what a selection covers on a row, what reaches the clipboard, where the thumb sits. The painting and the
''' mouse glue are read from the source, since a Main_Form cannot be built in a test.
''' </summary>
Public Class TextViewRulesTests

    Private Shared ReadOnly Lines As String() = {"alpha beta", "", "gamma", "delta epsilon zeta"}

    Private Shared Function Sel(l1 As Integer, c1 As Integer, l2 As Integer, c2 As Integer) As TextSelection
        Return New TextSelection(New TextPosition(l1, c1), New TextPosition(l2, c2))
    End Function

    ' --- positions and selections --------------------------------------------------------------------

    <Fact>
    Public Sub A_selection_dragged_backwards_is_the_same_selection()
        Dim forward As TextSelection = Sel(0, 2, 2, 3)
        Dim backward As TextSelection = Sel(2, 3, 0, 2)
        Assert.Equal(forward.StartPosition, backward.StartPosition)
        Assert.Equal(forward.EndPosition, backward.EndPosition)
        Assert.Equal(TextViewRules.Extract(Lines, forward), TextViewRules.Extract(Lines, backward))
    End Sub

    <Fact>
    Public Sub A_selection_with_the_same_anchor_and_focus_is_empty_and_extracts_nothing()
        Dim empty As TextSelection = Sel(0, 3, 0, 3)
        Assert.True(empty.IsEmpty)
        Assert.Equal("", TextViewRules.Extract(Lines, empty))
        Assert.Equal(0L, TextViewRules.CharacterCount(Lines, empty))
        Assert.Equal("", TextViewRules.Extract(Lines, Nothing))
    End Sub

    <Fact>
    Public Sub Extract_inside_one_line_is_the_substring()
        Assert.Equal("pha b", TextViewRules.Extract(Lines, Sel(0, 2, 0, 7)))
    End Sub

    <Fact>
    Public Sub Extract_across_lines_joins_them_with_the_newline_and_keeps_empty_lines()
        Dim expected As String = "beta" & Environment.NewLine & "" & Environment.NewLine & "gam"
        Assert.Equal(expected, TextViewRules.Extract(Lines, Sel(0, 6, 2, 3)))
    End Sub

    <Fact>
    Public Sub Select_all_is_the_whole_document_exactly_as_the_file_has_it()
        Dim all As TextSelection = TextViewRules.SelectAll(Lines)
        Assert.Equal(String.Join(Environment.NewLine, Lines), TextViewRules.Extract(Lines, all))
        Assert.Null(TextViewRules.SelectAll(Array.Empty(Of String)()))
        Assert.Null(TextViewRules.SelectAll(Nothing))
    End Sub

    ''' <summary>The status line puts a number on a select-all of a huge file: it must be the length of the text
    ''' without building it - and the same number.</summary>
    <Fact>
    Public Sub The_character_count_is_the_length_of_the_extracted_text()
        For Each selection As TextSelection In {TextViewRules.SelectAll(Lines), Sel(0, 6, 2, 3), Sel(1, 0, 3, 5), Sel(2, 1, 2, 4), Sel(0, 0, 1, 0)}
            Assert.Equal(CLng(TextViewRules.Extract(Lines, selection).Length), TextViewRules.CharacterCount(Lines, selection))
        Next
    End Sub

    <Fact>
    Public Sub A_position_outside_the_document_is_pulled_into_it()
        Assert.Equal(New TextPosition(3, 18), TextViewRules.Clamp(Lines, New TextPosition(99, 99)))
        Assert.Equal(New TextPosition(0, 0), TextViewRules.Clamp(Lines, New TextPosition(-4, -4)))
        Assert.Equal(New TextPosition(2, 5), TextViewRules.Clamp(Lines, New TextPosition(2, 50)))
        Assert.Equal(New TextPosition(0, 0), TextViewRules.Clamp(Nothing, New TextPosition(5, 5)))
        ' ... and Extract survives a selection that was made on a longer document.
        Assert.Equal("", TextViewRules.Extract(Lines, Sel(10, 0, 10, 0)))
        Assert.Equal("zeta", TextViewRules.Extract(Lines, Sel(3, 14, 40, 40)))
    End Sub

    ' --- what a row shows ------------------------------------------------------------------------

    <Fact>
    Public Sub A_row_the_selection_does_not_touch_has_no_span()
        Dim row As New TextRow(0, 0, "alpha beta")
        Assert.False(TextViewRules.SpanOnRow(Sel(2, 0, 3, 4), row).HasSpan)
        Assert.False(TextViewRules.SpanOnRow(Nothing, row).HasSpan)
        Assert.False(TextViewRules.SpanOnRow(Sel(0, 3, 0, 3), row).HasSpan)
    End Sub

    <Fact>
    Public Sub A_selection_inside_one_row_covers_exactly_its_columns()
        Dim span As TextRowSpan = TextViewRules.SpanOnRow(Sel(0, 2, 0, 7), New TextRow(0, 0, "alpha beta"))
        Assert.True(span.HasSpan)
        Assert.Equal(2, span.FromColumn)
        Assert.Equal(7, span.ToColumn)
    End Sub

    ''' <summary>A long line wrapped into rows: the second row starts at 6, and a selection from column 4 to 9 covers
    ''' the tail of the first row and the head of the second, in each row's own columns.</summary>
    <Fact>
    Public Sub A_selection_across_the_rows_of_one_wrapped_line_is_cut_per_row()
        Dim first As New TextRow(0, 0, "alpha ")
        Dim second As New TextRow(0, 6, "beta")
        Dim selection As TextSelection = Sel(0, 4, 0, 9)

        Dim a As TextRowSpan = TextViewRules.SpanOnRow(selection, first)
        Assert.True(a.HasSpan)
        Assert.Equal(4, a.FromColumn)
        Assert.Equal(6, a.ToColumn)

        Dim b As TextRowSpan = TextViewRules.SpanOnRow(selection, second)
        Assert.True(b.HasSpan)
        Assert.Equal(0, b.FromColumn)
        Assert.Equal(3, b.ToColumn)
    End Sub

    <Fact>
    Public Sub A_middle_line_is_covered_whole_and_the_end_lines_from_and_to_their_columns()
        Dim selection As TextSelection = Sel(0, 6, 3, 5)
        Dim head As TextRowSpan = TextViewRules.SpanOnRow(selection, New TextRow(0, 0, "alpha beta"))
        Assert.Equal(6, head.FromColumn)
        Assert.Equal(10, head.ToColumn)
        Dim middle As TextRowSpan = TextViewRules.SpanOnRow(selection, New TextRow(2, 0, "gamma"))
        Assert.Equal(0, middle.FromColumn)
        Assert.Equal(5, middle.ToColumn)
        Dim tail As TextRowSpan = TextViewRules.SpanOnRow(selection, New TextRow(3, 0, "delta epsilon zeta"))
        Assert.Equal(0, tail.FromColumn)
        Assert.Equal(5, tail.ToColumn)
    End Sub

    ''' <summary>A blank line the selection runs across has nothing to tint; it is marked so the painter can show a
    ''' stub - a selection with a hole in it reads as two selections.</summary>
    <Fact>
    Public Sub A_blank_line_inside_the_selection_is_marked_and_a_blank_line_at_its_edge_is_not()
        Dim blank As New TextRow(1, 0, "")
        Dim inside As TextRowSpan = TextViewRules.SpanOnRow(Sel(0, 3, 2, 2), blank)
        Assert.True(inside.HasSpan)
        Assert.True(inside.IsBlankLine)
        ' The selection ends where the blank line begins: its line break is not part of it.
        Assert.False(TextViewRules.SpanOnRow(Sel(0, 3, 1, 0), blank).HasSpan)
        ' A text row is never marked blank.
        Assert.False(TextViewRules.SpanOnRow(Sel(0, 3, 2, 2), New TextRow(0, 0, "alpha beta")).IsBlankLine)
    End Sub

    ' --- where a point lands -----------------------------------------------------------------------

    ''' <summary>A monospace measure: every character is 10 px wide.</summary>
    Private Shared ReadOnly Mono As Func(Of Integer, Integer) = Function(count) count * 10

    <Theory>
    <InlineData(-20, 0)>
    <InlineData(0, 0)>
    <InlineData(4, 0)>
    <InlineData(5, 0)>
    <InlineData(6, 1)>
    <InlineData(14, 1)>
    <InlineData(16, 2)>
    <InlineData(49, 5)>
    <InlineData(50, 5)>
    <InlineData(500, 5)>
    Public Sub The_caret_goes_to_the_nearest_character_boundary(x As Integer, expected As Integer)
        Assert.Equal(expected, TextViewRules.ColumnAtX("hello", x, Mono))
    End Sub

    <Fact>
    Public Sub An_empty_row_has_only_one_place_to_land()
        Assert.Equal(0, TextViewRules.ColumnAtX("", 100, Mono))
        Assert.Equal(0, TextViewRules.ColumnAtX(Nothing, 100, Mono))
    End Sub

    ''' <summary>Proportional text: the search must follow the measure, not an assumed width.</summary>
    <Fact>
    Public Sub A_proportional_measure_is_followed()
        ' "i" is 4 px, everything else 10 px: widths 0, 10, 14, 24 for "wiw".
        Dim text As String = "wiw"
        Dim measure As Func(Of Integer, Integer) =
            Function(count)
                Dim width As Integer = 0
                For index As Integer = 0 To count - 1
                    width += If(text(index) = "i"c, 4, 10)
                Next
                Return width
            End Function
        Assert.Equal(1, TextViewRules.ColumnAtX(text, 9, measure))
        Assert.Equal(2, TextViewRules.ColumnAtX(text, 13, measure))
        Assert.Equal(3, TextViewRules.ColumnAtX(text, 22, measure))
    End Sub

    ''' <summary>The pair of an emoji is one character: a click between its halves goes to a whole boundary.</summary>
    <Fact>
    Public Sub A_surrogate_pair_is_never_cut_in_two()
        Dim text As String = "a" & Char.ConvertFromUtf32(&H1F600) & "b"   ' a, high, low, b
        Assert.Equal(4, text.Length)
        ' Every character 10 px: the boundary between the two halves is at 20 px.
        Dim column As Integer = TextViewRules.ColumnAtX(text, 20, Mono)
        Assert.NotEqual(2, column)
        Assert.True(column = 1 OrElse column = 3)
    End Sub

    <Theory>
    <InlineData(20, 0)>
    <InlineData(36, 1)>
    <InlineData(52, 2)>
    <InlineData(10, 0)>
    <InlineData(2000, 3)>
    Public Sub The_row_under_a_point_is_counted_from_the_first_visible_row_and_clamped(y As Integer, expected As Integer)
        ' Rows start at y = 20 and are 16 px high; row 0 of the document is the first one visible; four rows.
        Assert.Equal(expected, TextViewRules.RowAt(y, 20, 16, 0, 4))
    End Sub

    <Fact>
    Public Sub The_row_under_a_point_follows_the_scroll_offset_and_reaches_the_row_above_the_view()
        Assert.Equal(10, TextViewRules.RowAt(20, 20, 16, 10, 50))
        Assert.Equal(9, TextViewRules.RowAt(8, 20, 16, 10, 50))   ' above the top margin: the row scrolled out
        Assert.Equal(0, TextViewRules.RowAt(0, 20, 16, 0, 0))
    End Sub

    ' --- the scroll cue ----------------------------------------------------------------------------

    <Fact>
    Public Sub Nothing_is_drawn_when_everything_fits()
        Assert.False(TextViewRules.ScrollThumb(10, 10, 0, 400, 12).Visible)
        Assert.False(TextViewRules.ScrollThumb(5, 10, 0, 400, 12).Visible)
        Assert.False(TextViewRules.ScrollThumb(0, 10, 0, 400, 12).Visible)
        Assert.False(TextViewRules.ScrollThumb(100, 10, 0, 0, 12).Visible)
    End Sub

    <Fact>
    Public Sub The_thumb_is_the_share_in_view_and_walks_the_whole_track()
        Dim top As TextScrollThumb = TextViewRules.ScrollThumb(100, 25, 0, 400, 12)
        Assert.True(top.Visible)
        Assert.Equal(100, top.Length)
        Assert.Equal(0, top.Top)

        Dim bottom As TextScrollThumb = TextViewRules.ScrollThumb(100, 25, 75, 400, 12)
        Assert.Equal(100, bottom.Length)
        Assert.Equal(300, bottom.Top)
        Assert.Equal(400, bottom.Top + bottom.Length)

        Dim middle As TextScrollThumb = TextViewRules.ScrollThumb(100, 25, 38, 400, 12)
        Assert.InRange(middle.Top, 150, 154)
    End Sub

    <Fact>
    Public Sub A_very_long_file_still_has_a_thumb_to_look_at()
        Dim thumb As TextScrollThumb = TextViewRules.ScrollThumb(1000000, 30, 500000, 400, 12)
        Assert.Equal(12, thumb.Length)
        Assert.InRange(thumb.Top, 0, 388)
    End Sub

    <Fact>
    Public Sub An_offset_beyond_the_end_keeps_the_thumb_on_the_track()
        Dim thumb As TextScrollThumb = TextViewRules.ScrollThumb(100, 25, 9999, 400, 12)
        Assert.Equal(400, thumb.Top + thumb.Length)
        Dim before As TextScrollThumb = TextViewRules.ScrollThumb(100, 25, -5, 400, 12)
        Assert.Equal(0, before.Top)
    End Sub

    ' --- the glue (read from the source: a Main_Form cannot be built here) -----------------------

    Private Shared Function Source(fileName As String) As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", fileName)
            If File.Exists(candidate) Then Return File.ReadAllText(candidate)
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src\" & fileName & " not found above " & AppContext.BaseDirectory)
    End Function

    ''' <summary>The left click on the media is "next file" and must stay so: a press on a text document is decided at
    ''' the release, and only a press that never became a drag is a click.</summary>
    <Fact>
    Public Sub A_press_on_a_text_document_is_a_click_only_when_it_never_became_a_drag()
        Dim mouse As String = Source("Main_Form.MouseInput.vb")
        Dim down As Integer = mouse.IndexOf("TextSelectionMouseDown(e)", StringComparison.Ordinal)
        Dim delayed As Integer = mouse.IndexOf("PRIORITY 3: DELAY LEFT-CLICK", StringComparison.Ordinal)
        Dim doubleClick As Integer = mouse.IndexOf("PRIORITY 2: DOUBLE-CLICK DETECTION", StringComparison.Ordinal)
        Assert.True(doubleClick > 0 AndAlso down > doubleClick, "the double click is decided on the press, before the text takes it")
        Assert.True(delayed > down, "the delayed click is what the text view takes over")
        Assert.Contains("TextSelectionMouseUp(e)", mouse)
        Assert.Contains("TextSelectionMouseMove(", mouse)

        Dim view As String = Source("Main_Form.TextView.vb")
        Dim up As Integer = view.IndexOf("Friend Sub TextSelectionMouseUp", StringComparison.Ordinal)
        Assert.True(up > 0)
        Dim body As String = view.Substring(up, view.IndexOf("End Sub", up, StringComparison.Ordinal) - up)
        Assert.Contains("pending_Single_Click_Timer.Start()", body)
        ' A drag returns before it: the timer is started only on the click path.
        Assert.True(body.IndexOf("Return", StringComparison.Ordinal) < body.IndexOf("pending_Single_Click_Timer.Start()", StringComparison.Ordinal))
    End Sub

    <Fact>
    Public Sub The_selection_survives_a_resize_because_it_is_held_in_text_coordinates()
        Dim view As String = Source("Main_Form.TextView.vb")
        Assert.Contains("Private text_Selection As TextSelection", view)
        ' The rows are re-cut by a resize or a zoom, inside the paint; the selection is not stored in them and the
        ' re-wrap never touches it.
        Dim start As Integer = view.IndexOf("Friend Sub PaintTextDocument", StringComparison.Ordinal)
        Dim finish As Integer = view.IndexOf("Private Shared Function SelectionBandColor", StringComparison.Ordinal)
        Assert.True(start > 0 AndAlso finish > start)
        Dim paint As String = view.Substring(start, finish - start)
        Assert.Contains("text_Rows.Clear()", paint)
        Assert.DoesNotContain("text_Selection = Nothing", paint)
        Assert.DoesNotContain("text_Selection = New", paint)
    End Sub

    ''' <summary>The document is left behind with its selection - never carried to the next file.</summary>
    <Fact>
    Public Sub Releasing_the_document_clears_the_selection_and_the_press()
        Dim view As String = Source("Main_Form.TextView.vb")
        Dim at As Integer = view.IndexOf("Friend Sub ReleaseTextDocument", StringComparison.Ordinal)
        Dim body As String = view.Substring(at, view.IndexOf("End Sub", at, StringComparison.Ordinal) - at)
        Assert.Contains("text_Selection = Nothing", body)
        Assert.Contains("text_Press = Nothing", body)
        Assert.Contains("text_Selecting = False", body)
    End Sub

    <Fact>
    Public Sub Ctrl_A_and_Ctrl_C_are_asked_before_the_historical_ctrl_gate_and_only_for_a_text_document()
        Dim keys As String = Source("Main_Form.KeyboardInput.vb")
        Dim asked As Integer = keys.IndexOf("TryHandleTextSelectionKey(e)", StringComparison.Ordinal)
        Dim gate As Integer = keys.IndexOf("If Not KeyRouting.MainMapAccepts(e.KeyData) Then Exit Sub", StringComparison.Ordinal)
        Assert.True(asked > 0 AndAlso gate > asked, "the chord must be asked before the gate that hands Ctrl chords to Windows")
        Dim view As String = Source("Main_Form.TextView.vb")
        Dim at As Integer = view.IndexOf("Private Function TryHandleTextSelectionKey", StringComparison.Ordinal)
        Dim body As String = view.Substring(at, view.IndexOf("End Function", at, StringComparison.Ordinal) - at)
        Assert.Contains("If Not IsTextDocumentShown()", body)
        Assert.Contains("Utils.CopyTextToClipboard(", body)
        ' Nothing selected copies nothing (a whole file nobody pointed at must not replace the clipboard).
        Assert.Contains("If Not HasTextSelection() Then", body)
    End Sub

    ''' <summary>The F1 card documents the two new gestures, and the fixed rows are registered strings.</summary>
    <Fact>
    Public Sub The_help_card_names_the_chords_and_the_drag()
        Dim rows As String = Source("HelpCardRows.vb")
        Assert.Contains("""Ctrl+A / Ctrl+C""", rows)
        Assert.Contains("""Протяжка по тексту""", rows)
    End Sub

End Class
#End If
